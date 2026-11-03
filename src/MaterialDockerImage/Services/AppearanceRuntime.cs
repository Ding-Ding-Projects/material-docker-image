using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Services;

/// <summary>
/// Applies the appearance to application resources and owns appearance editing: every change goes through
/// <see cref="Change"/> so it is saved to settings.json, recorded in local history ("Appearance: …", debounced for
/// sliders), and can be undone (100 steps) or redone. The animated rainbow accent is driven here.
/// </summary>
public static class AppearanceRuntime
{
    public const string DefaultTextFont = "Segoe UI Variable Text, Segoe UI";
    public const string DefaultDisplayFont = "Segoe UI Variable Display, Segoe UI";
    public const string DefaultMonoFont = "Cascadia Mono, Consolas";
    public static AppearanceUndoStack History { get; } = new(100);
    /// <summary>Raised after any change, undo, redo, preset or reset, and when the undo stack changes.</summary>
    public static event Action? Changed;
    private static AppHost? Host => AppHost.Current;
    private static readonly DispatcherTimer rainbowTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private static readonly System.Diagnostics.Stopwatch rainbowClock = System.Diagnostics.Stopwatch.StartNew();
    private static readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromSeconds(1) };
    private static string? pendingLabel;
    private static Action<Action> pendingGuard = save => save();
    private static ResourceDictionary? resources;
    private static bool systemHooked;

    static AppearanceRuntime()
    {
        rainbowTimer.Tick += (_, _) => RainbowTick();
        debounce.Tick += (_, _) => Flush();
    }

    public static double ElevationScale => Host?.Settings.ElevationScale is { } e && double.IsFinite(e) ? Math.Clamp(e, 0, 2) : 1;
    public static AppearanceState Current => Host == null ? new AppearanceState() : AppearanceState.FromSettings(Host.Settings);

    // ----- Applying resources -----

    /// <summary>Writes every appearance resource: colour roles, shape, density, fonts, heading weight and rail icons.</summary>
    public static void Apply(ResourceDictionary target, UserSettings settings)
    {
        resources = target;
        var a = AppearanceState.FromSettings(settings);
        var animateRainbow = Motion.Enabled && (a.Rainbow || a.Roles.ContainsValue(AppearanceRoles.Rainbow));
        Theme.Apply(target, a, settings.HighContrast, animateRainbow);
        var r = a.RadiusScale;
        Put(target, "RadiusSmall", new CornerRadius(8 * r));
        Put(target, "RadiusMedium", new CornerRadius(12 * r));
        Put(target, "RadiusLarge", new CornerRadius(16 * r));
        Put(target, "RadiusExtraLarge", new CornerRadius(28 * r));
        Put(target, "RadiusPill", new CornerRadius(20 * r));
        var height = a.Density switch { "Compact" => 36.0, "Spacious" => 44.0, _ => 40.0 };
        if (settings.LargerTargets) height = Math.Max(height, 48);
        Put(target, "TargetHeight", height);
        Put(target, "ButtonPadding", new Thickness(a.Density switch { "Compact" => 16, "Spacious" => 24, _ => 20 }, 0, a.Density switch { "Compact" => 16, "Spacious" => 24, _ => 20 }, 0));
        Put(target, "FieldPadding", new Thickness(a.Density switch { "Compact" => 8, "Spacious" => 14, _ => 10 }, 0, a.Density switch { "Compact" => 8, "Spacious" => 14, _ => 10 }, 0));
        Put(target, "TextFont", Family(a.UiFont, DefaultTextFont));
        Put(target, "DisplayFont", Family(a.UiFont, DefaultDisplayFont));
        Put(target, "MonoFont", Family(a.MonoFont, DefaultMonoFont));
        Put(target, "HeadingWeight", FontWeight.FromOpenTypeWeight(a.HeadingWeight));
        Put(target, "RailIconFont", target[a.RailIcons == "Filled" ? "IconFontFilled" : "IconFont"]);
        Put(target, "ElevationOpacity", 0.35 * a.Elevation);
        if (animateRainbow) { if (!rainbowTimer.IsEnabled) rainbowTimer.Start(); RainbowTick(); }
        else rainbowTimer.Stop();
        HookSystem();
    }

    /// <summary>Sets a resource only when it changed: every resource write makes WPF revisit all references to it.</summary>
    private static void Put(ResourceDictionary target, string key, object value)
    {
        if (target.Contains(key) && Equals(target[key], value)) return;
        target[key] = value;
    }

    private static FontFamily Family(string name, string fallback)
    {
        var f = new FontFamily(string.IsNullOrWhiteSpace(name) ? fallback : name + ", " + fallback);
        return f;
    }

    /// <summary>Re-applies with the current settings (used when Windows animation settings change).</summary>
    public static void Refresh() { if (resources != null && Host != null) Apply(resources, Host.Settings); }

    private static void HookSystem()
    {
        if (systemHooked) return;
        systemHooked = true;
        SystemParameters.StaticPropertyChanged += (_, e) => { if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation)) Application.Current?.Dispatcher.BeginInvoke(Refresh); };
    }

    private static void RainbowTick()
    {
        if (!Motion.Enabled) { rainbowTimer.Stop(); Refresh(); return; } // settle on the saved hue
        var offset = AppearanceMotion.RainbowHue(0, rainbowClock.Elapsed.TotalSeconds, true, Host?.Settings.MotionSpeed ?? 1);
        // A brush can be frozen by code that freezes a pen or drawing built on it; that role then simply stays still.
        foreach (var (brush, color) in Theme.AnimatedBrushes) if (!brush.IsFrozen) brush.Color = Theme.RotateHue(color, offset);
    }

    // ----- Editing -----

    /// <summary>
    /// Changes the appearance. <paramref name="coalesce"/> is for continuous controls (sliders, pickers): the value is
    /// applied and saved at once, one undo step covers the whole drag, and local history is written after a 1 s pause.
    /// <paramref name="guard"/> lets the settings page ignore its own save echo.
    /// </summary>
    public static void Change(string label, Action<AppearanceState> mutate, bool coalesce = false, Action<Action>? guard = null)
    {
        if (Host == null) return;
        var before = Current;
        var next = before.Clone();
        mutate(next);
        next.Normalize();
        if (next.SameAs(before)) return;
        if (coalesce)
        {
            if (pendingLabel == null) History.Push(before);
            pendingLabel = label; pendingGuard = guard ?? (save => save());
            next.WriteTo(Host.Settings);
            (guard ?? (save => save()))(() => Host.SaveSettings(null));
            debounce.Stop(); debounce.Start();
        }
        else
        {
            Flush();
            History.Push(before);
            next.WriteTo(Host.Settings);
            (guard ?? (save => save()))(() => Host.SaveSettings("Appearance: " + label));
        }
        Changed?.Invoke();
    }

    /// <summary>Replaces the whole appearance (presets, paste, import, undo).</summary>
    public static void Set(AppearanceState next, string label, bool recordUndo = true)
    {
        if (Host == null) return;
        Flush();
        var before = Current;
        next = next.Clone(); next.Normalize();
        if (next.SameAs(before)) { Host.Notify.Info("The appearance already matches. Nothing changed."); return; }
        if (recordUndo) History.Push(before);
        next.WriteTo(Host.Settings);
        Host.SaveSettings("Appearance: " + label);
        Changed?.Invoke();
    }

    /// <summary>Writes the pending debounced history revision now.</summary>
    public static void Flush()
    {
        debounce.Stop();
        if (pendingLabel == null || Host == null) return;
        var label = pendingLabel; pendingLabel = null;
        pendingGuard(() => Host.SaveSettings("Appearance: " + label));
    }

    public static bool CanUndo => History.CanUndo;
    public static bool CanRedo => History.CanRedo;

    public static void Undo()
    {
        Flush();
        if (Host == null || History.Undo(Current) is not { } previous) { Host?.Notify.Info("There is no appearance change to undo."); return; }
        previous.WriteTo(Host.Settings);
        Host.SaveSettings("Appearance: undo");
        Changed?.Invoke();
    }

    public static void Redo()
    {
        Flush();
        if (Host == null || History.Redo(Current) is not { } next) { Host?.Notify.Info("There is no appearance change to redo."); return; }
        next.WriteTo(Host.Settings);
        Host.SaveSettings("Appearance: redo");
        Changed?.Invoke();
    }

    /// <summary>Returns every appearance value to its built-in default (one undo step).</summary>
    public static void ResetAll() => Set(new AppearanceState(), "reset all appearance to defaults");

    // ----- Presets -----

    public static string PresetPath => Path.Combine(UserSettings.DataDirectory, "appearance-presets.json");
    private static List<AppearancePreset>? userPresets;
    /// <summary>A problem reading appearance-presets.json, shown in the editor; the file is never overwritten while set.</summary>
    public static string? PresetProblem { get; private set; }

    public static IReadOnlyList<AppearancePreset> UserPresets
    {
        get
        {
            if (userPresets != null) return userPresets;
            try { userPresets = AppearancePresets.LoadUser(PresetPath); PresetProblem = null; }
            catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException) { userPresets = new(); PresetProblem = e.Message; }
            return userPresets;
        }
    }

    public static IEnumerable<AppearancePreset> AllPresets => AppearancePresets.BuiltIn.Concat(UserPresets);

    public static void ApplyPreset(AppearancePreset preset) => Set(Current.WithLookOf(preset.State), "applied preset " + preset.Name);

    /// <summary>Saves, replaces or removes user presets. Returns false (and notifies) when the file cannot be written.</summary>
    public static bool SaveUserPresets(List<AppearancePreset> presets, string historyLabel)
    {
        if (PresetProblem != null) { Host?.Notify.Error("Presets were not saved because appearance-presets.json could not be read: " + PresetProblem + " Move or fix the file first."); return false; }
        try { AppearancePresets.SaveUser(PresetPath, presets); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Host?.Notify.Error("Presets could not be saved: " + e.Message); return false; }
        userPresets = presets.ToList();
        if (Host is { IsCapture: false } host)
            host.History.Record("appearance", "Appearance presets: " + historyLabel, new Dictionary<string, string> { ["settings/appearance-presets.json"] = File.ReadAllText(PresetPath) });
        Changed?.Invoke();
        return true;
    }
}
