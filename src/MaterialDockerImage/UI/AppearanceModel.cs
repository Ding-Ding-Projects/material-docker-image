using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MaterialDockerImage.UI;

/// <summary>One Material colour role that can be overridden, with a readable name and group.</summary>
public sealed record AppearanceRole(string Key, string Name, string Group);

/// <summary>The colour roles the appearance editor exposes, in display order.</summary>
public static class AppearanceRoles
{
    public const string Rainbow = "rainbow";
    public static readonly IReadOnlyList<AppearanceRole> All =
    [
        new("Primary", "Primary", "Accent"), new("OnPrimary", "On primary", "Accent"), new("PrimaryContainer", "Primary container", "Accent"), new("OnPrimaryContainer", "On primary container", "Accent"),
        new("SecondaryContainer", "Secondary container (selection, tonal buttons)", "Accent"), new("OnSecondaryContainer", "On secondary container", "Accent"),
        new("Tertiary", "Tertiary", "Accent"), new("TertiaryContainer", "Tertiary container", "Accent"), new("OnTertiaryContainer", "On tertiary container", "Accent"),
        new("Surface", "Surface (window background)", "Surface"), new("SurfaceLowest", "Surface, lowest (fields)", "Surface"), new("SurfaceLow", "Surface, low (cards)", "Surface"),
        new("SurfaceContainer", "Surface container", "Surface"), new("SurfaceHigh", "Surface, high", "Surface"), new("SurfaceHighest", "Surface, highest (toasts)", "Surface"),
        new("OnSurface", "On surface (text)", "Surface"), new("OnSurfaceVariant", "On surface variant (secondary text)", "Surface"), new("Outline", "Outline", "Surface"), new("OutlineVariant", "Outline variant", "Surface"),
        new("Success", "Success", "Status"), new("SuccessContainer", "Success container", "Status"), new("OnSuccessContainer", "On success container", "Status"),
        new("Warning", "Warning", "Status"), new("WarningContainer", "Warning container", "Status"), new("OnWarningContainer", "On warning container", "Status"),
        new("Error", "Error", "Status"), new("OnError", "On error", "Status"), new("ErrorContainer", "Error container", "Status"), new("OnErrorContainer", "On error container", "Status"),
        new("Scrim", "Scrim (behind dialogs)", "Layers"), new("HoverLayer", "Hover layer", "Layers"), new("PressLayer", "Press layer", "Layers")
    ];
    private static readonly HashSet<string> Keys = All.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
    public static bool IsKnown(string key) => Keys.Contains(key);
    public static AppearanceRole? Find(string key) => All.FirstOrDefault(r => r.Key == key);

    /// <summary>The role a colour is usually read against, used for the contrast badge.</summary>
    public static string ContrastPartner(string key)
    {
        if (key.StartsWith("On", StringComparison.Ordinal))
        {
            var background = key[2..];
            return IsKnown(background) ? background : "Surface";
        }
        if (IsKnown("On" + key)) return "On" + key;
        return key.StartsWith("Surface", StringComparison.Ordinal) || key is "Scrim" or "HoverLayer" or "PressLayer" ? "OnSurface" : "Surface";
    }
}

/// <summary>Material 3 motion tokens and the rules that decide whether motion runs. Pure data and logic.</summary>
public static class AppearanceMotion
{
    public static readonly string[] Levels = ["Off", "Reduced", "Standard", "Expressive"];
    // Duration tokens in milliseconds.
    public const int Short1 = 50, Short2 = 100, Short3 = 150, Short4 = 200;
    public const int Medium1 = 250, Medium2 = 300, Medium3 = 350, Medium4 = 400;
    public const int Long1 = 450, Long2 = 500, Long3 = 550, Long4 = 600;
    // Easing tokens as cubic-bezier control points (x1, y1, x2, y2).
    public static readonly (double, double, double, double) Emphasized = (0.2, 0, 0, 1);
    public static readonly (double, double, double, double) EmphasizedDecelerate = (0.05, 0.7, 0.1, 1);
    public static readonly (double, double, double, double) EmphasizedAccelerate = (0.3, 0, 0.8, 0.15);
    public static readonly (double, double, double, double) Standard = (0.2, 0, 0, 1);
    public static readonly (double, double, double, double) StandardDecelerate = (0, 0, 0, 1);
    public static readonly (double, double, double, double) StandardAccelerate = (0.3, 0, 1, 1);
    public const double RainbowCycleSeconds = 24;

    /// <summary>
    /// The level that actually applies. Reduced motion, Low stimulation, the Windows animation setting and capture mode
    /// all win over the saved level, so the app never animates against a system or accessibility choice.
    /// </summary>
    public static string Effective(string level, bool reducedMotion, bool lowStimulation, bool windowsAnimations, bool capture)
    {
        if (capture || !windowsAnimations) return "Off";
        if (level is not ("Off" or "Reduced" or "Standard" or "Expressive")) level = "Standard";
        if ((reducedMotion || lowStimulation) && level is "Standard" or "Expressive") return "Reduced";
        return level;
    }

    /// <summary>Only Standard and Expressive animate. Off and Reduced snap to the final state instantly.</summary>
    public static bool Animates(string effective) => effective is "Standard" or "Expressive";

    /// <summary>Duration in milliseconds for a token at the given level and speed (0.5x slower to 2x faster).</summary>
    public static double Duration(int token, string effective, double speed)
    {
        if (!Animates(effective)) return 0;
        speed = double.IsFinite(speed) ? Math.Clamp(speed, 0.5, 2) : 1;
        return token * (effective == "Expressive" ? 1.2 : 1) / speed;
    }

    /// <summary>Accent hue for the animated rainbow; it settles on the saved hue when motion does not run.</summary>
    public static double RainbowHue(double baseHue, double elapsedSeconds, bool animates, double speed = 1)
    {
        if (!animates || !double.IsFinite(elapsedSeconds)) return AppearanceRgba.Wrap(baseHue);
        var cycle = RainbowCycleSeconds / Math.Clamp(double.IsFinite(speed) ? speed : 1, 0.5, 2);
        return AppearanceRgba.Wrap(baseHue + 360 * (elapsedSeconds % cycle) / cycle);
    }
}

/// <summary>
/// Every runtime appearance value in one serialisable object. Settings stay the single source of truth; this model
/// is read from and written to <see cref="UserSettings"/> and is what presets, undo and export carry.
/// </summary>
public sealed class AppearanceState
{
    public string Theme { get; set; } = "Dark";
    public string Seed { get; set; } = "#3A6FD8";
    public Dictionary<string, string> Roles { get; set; } = new(StringComparer.Ordinal);
    public double RadiusScale { get; set; } = 1;
    public string Density { get; set; } = "Comfortable";
    public string UiFont { get; set; } = "";
    public string MonoFont { get; set; } = "";
    public double FontSize { get; set; } = 14;
    public int HeadingWeight { get; set; } = 600;
    public string RailIcons { get; set; } = "Outline";
    public double Elevation { get; set; } = 1;
    public double SurfaceTint { get; set; }
    public bool Rainbow { get; set; }
    public string MotionLevel { get; set; } = "Standard";
    public double MotionSpeed { get; set; } = 1;
    public string TransitionPattern { get; set; } = AppearanceTransitions.Default;
    public int SpringBounce { get; set; } = AppearanceSpring.Default;

    public static readonly string[] Themes = ["System", "Light", "Dark"];
    public static readonly string[] Densities = ["Compact", "Comfortable", "Spacious"];
    public static readonly int[] HeadingWeights = [400, 500, 600, 700, 800];
    public static readonly string[] RailIconStyles = ["Outline", "Filled"];

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    public AppearanceState Clone()
    {
        var copy = (AppearanceState)MemberwiseClone();
        copy.Roles = new Dictionary<string, string>(Roles ?? new(), StringComparer.Ordinal);
        return copy;
    }

    public static AppearanceState FromSettings(UserSettings s)
    {
        var state = new AppearanceState
        {
            Theme = s.Theme, Seed = s.SeedColor, Roles = new Dictionary<string, string>(s.RoleOverrides ?? new(), StringComparer.Ordinal), RadiusScale = s.CornerScale, Density = s.Density,
            UiFont = s.UiFontFamily ?? "", MonoFont = s.MonoFontFamily ?? "", FontSize = s.FontSize, HeadingWeight = s.HeadingWeight, RailIcons = s.RailIconStyle ?? "Outline",
            Elevation = s.ElevationScale, SurfaceTint = s.SurfaceTint, Rainbow = s.RainbowAccent, MotionLevel = s.MotionLevel ?? "Standard", MotionSpeed = s.MotionSpeed,
            TransitionPattern = s.TransitionPattern ?? AppearanceTransitions.Default, SpringBounce = s.SpringBounce
        };
        state.Normalize();
        return state;
    }

    public void WriteTo(UserSettings s)
    {
        Normalize();
        s.Theme = Theme; s.SeedColor = Seed; s.RoleOverrides = new Dictionary<string, string>(Roles, StringComparer.Ordinal); s.CornerScale = RadiusScale; s.Density = Density;
        s.UiFontFamily = UiFont; s.MonoFontFamily = MonoFont; s.FontSize = FontSize; s.HeadingWeight = HeadingWeight; s.RailIconStyle = RailIcons;
        s.ElevationScale = Elevation; s.SurfaceTint = SurfaceTint; s.RainbowAccent = Rainbow; s.MotionLevel = MotionLevel; s.MotionSpeed = MotionSpeed;
        s.TransitionPattern = TransitionPattern; s.SpringBounce = SpringBounce;
    }

    private static double Clamp(double v, double min, double max, double fallback) => double.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;

    /// <summary>Repairs loaded settings into the supported ranges (used for settings.json, never for imports).</summary>
    public void Normalize()
    {
        if (!Themes.Contains(Theme)) Theme = "Dark";
        if (!AppearanceRgba.TryParseStorageHex(Seed, out var seed)) Seed = "#3A6FD8"; else Seed = seed.ToHex();
        Roles = (Roles ?? new()).Where(p => AppearanceRoles.IsKnown(p.Key) && IsRoleValue(p.Value)).ToDictionary(p => p.Key, p => NormalizeRoleValue(p.Value), StringComparer.Ordinal);
        RadiusScale = Math.Round(Clamp(RadiusScale, 0.5, 1.5, 1), 2);
        if (!Densities.Contains(Density)) Density = "Comfortable";
        UiFont = CleanFont(UiFont); MonoFont = CleanFont(MonoFont);
        FontSize = Clamp(FontSize, 12, 22, 14);
        if (!HeadingWeights.Contains(HeadingWeight)) HeadingWeight = 600;
        if (!RailIconStyles.Contains(RailIcons)) RailIcons = "Outline";
        Elevation = Math.Round(Clamp(Elevation, 0, 2, 1), 2);
        SurfaceTint = Math.Round(Clamp(SurfaceTint, 0, 1, 0), 2);
        if (!AppearanceMotion.Levels.Contains(MotionLevel)) MotionLevel = "Standard";
        MotionSpeed = Math.Round(Clamp(MotionSpeed, 0.5, 2, 1), 2);
        if (!AppearanceTransitions.IsKnown(TransitionPattern)) TransitionPattern = AppearanceTransitions.Default;
        SpringBounce = AppearanceSpring.Clamp(SpringBounce);
    }

    private static string CleanFont(string? font) => font is { Length: > 0 and <= 100 } f && !f.Any(char.IsControl) ? f.Trim() : "";
    public static bool IsRoleValue(string? value) => value == AppearanceRoles.Rainbow || AppearanceRgba.TryParseStorageHex(value, out _);
    private static string NormalizeRoleValue(string value) => value == AppearanceRoles.Rainbow ? value : AppearanceRgba.TryParseStorageHex(value, out var c) ? c.ToStorageHex() : value;

    /// <summary>Strict validation used for imported presets: every value must already be in range.</summary>
    public List<string> Validate()
    {
        var problems = new List<string>();
        if (!Themes.Contains(Theme)) problems.Add($"Theme must be one of {string.Join(", ", Themes)}.");
        if (!AppearanceRgba.TryParseStorageHex(Seed, out _)) problems.Add("Seed must be a hex colour such as #3A6FD8.");
        if (Roles == null) problems.Add("Roles must be an object.");
        else
        {
            if (Roles.Count > AppearanceRoles.All.Count) problems.Add("Too many role overrides.");
            foreach (var (key, value) in Roles)
            {
                if (!AppearanceRoles.IsKnown(key)) problems.Add($"Unknown colour role \"{Short(key)}\".");
                else if (!IsRoleValue(value)) problems.Add($"Role {key} must be a hex colour or \"rainbow\".");
            }
        }
        void Range(double v, double min, double max, string name) { if (!double.IsFinite(v) || v < min || v > max) problems.Add($"{name} must be between {min.ToString(CultureInfo.InvariantCulture)} and {max.ToString(CultureInfo.InvariantCulture)}."); }
        Range(RadiusScale, 0.5, 1.5, "Corner radius scale");
        Range(FontSize, 12, 22, "Font size");
        Range(Elevation, 0, 2, "Elevation");
        Range(SurfaceTint, 0, 1, "Surface tint");
        Range(MotionSpeed, 0.5, 2, "Motion speed");
        if (!Densities.Contains(Density)) problems.Add($"Density must be one of {string.Join(", ", Densities)}.");
        if (!HeadingWeights.Contains(HeadingWeight)) problems.Add("Heading weight must be 400, 500, 600, 700 or 800.");
        if (!RailIconStyles.Contains(RailIcons)) problems.Add("Rail icons must be Outline or Filled.");
        if (!AppearanceMotion.Levels.Contains(MotionLevel)) problems.Add("Motion level must be Off, Reduced, Standard or Expressive.");
        if (!AppearanceTransitions.IsKnown(TransitionPattern)) problems.Add($"Transition pattern must be one of {string.Join(", ", AppearanceTransitions.Patterns)}.");
        if (SpringBounce < AppearanceSpring.Minimum || SpringBounce > AppearanceSpring.Maximum) problems.Add("Spring bounce must be between 0 and 100.");
        foreach (var (font, name) in new[] { (UiFont, "UI font"), (MonoFont, "Mono font") })
            if (font == null || font.Length > 100 || font.Any(char.IsControl)) problems.Add(name + " must be a font name of at most 100 characters.");
        return problems;
    }

    private static string Short(string text) => text.Length <= 40 ? text : text[..40] + "…";

    /// <summary>Canonical JSON (sorted roles) used for equality, undo snapshots and export.</summary>
    public string ToJson()
    {
        var copy = Clone();
        copy.Roles = copy.Roles.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        return JsonSerializer.Serialize(copy, Json);
    }
    public bool SameAs(AppearanceState other) => ToJson() == other.ToJson();

    public static AppearanceState FromJson(string json)
    {
        var state = JsonSerializer.Deserialize<AppearanceState>(json, new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }) ?? throw new JsonException("The appearance is empty.");
        state.Roles = new Dictionary<string, string>(state.Roles ?? throw new JsonException("Roles must be an object."), StringComparer.Ordinal);
        return state;
    }

    /// <summary>Copies the look of another state but keeps this state's motion choices: level, speed, transition pattern and spring bounce (presets never change motion).</summary>
    public AppearanceState WithLookOf(AppearanceState look)
    {
        var next = look.Clone();
        next.MotionLevel = MotionLevel; next.MotionSpeed = MotionSpeed;
        next.TransitionPattern = TransitionPattern; next.SpringBounce = SpringBounce;
        return next;
    }
}

/// <summary>A named appearance.</summary>
public sealed record AppearancePreset(string Name, AppearanceState State, bool BuiltIn = false)
{
    public string Id => (BuiltIn ? "builtin." : "user.") + new string(Name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
}

/// <summary>Built-in presets, preset JSON export/import with validation, and the local user preset file.</summary>
public static class AppearancePresets
{
    public const string FormatName = "material-docker-image.appearance";
    public const int MaxPresetBytes = 64 * 1024;
    public const int MaxStoreBytes = 512 * 1024;
    public const int MaxUserPresets = 50;

    public static IReadOnlyList<AppearancePreset> BuiltIn { get; } = CreateBuiltIn();

    private static IReadOnlyList<AppearancePreset> CreateBuiltIn()
    {
        AppearanceState S(Action<AppearanceState> edit) { var s = new AppearanceState(); edit(s); s.Normalize(); return s; }
        return
        [
            new("Default dark", S(_ => { }), true),
            new("Default light", S(s => s.Theme = "Light"), true),
            new("High contrast", S(s => { s.Theme = "Dark"; s.Seed = "#FFD60A"; s.Elevation = 0; s.Roles = new(StringComparer.Ordinal) { ["Surface"] = "#000000", ["SurfaceLowest"] = "#000000", ["SurfaceLow"] = "#0A0A0A", ["SurfaceContainer"] = "#141414", ["SurfaceHigh"] = "#1F1F1F", ["SurfaceHighest"] = "#2A2A2A", ["OnSurface"] = "#FFFFFF", ["OnSurfaceVariant"] = "#FFFFFF", ["Outline"] = "#FFFFFF", ["OutlineVariant"] = "#BDBDBD", ["Primary"] = "#FFD60A", ["OnPrimary"] = "#000000" }; }), true),
            new("Ocean", S(s => { s.Theme = "Dark"; s.Seed = "#0B7A99"; s.SurfaceTint = 0.45; s.RadiusScale = 1.15; s.Roles = new(StringComparer.Ordinal) { ["Tertiary"] = "#7FD6C2" }; }), true),
            new("Forest", S(s => { s.Theme = "Dark"; s.Seed = "#2E7D32"; s.SurfaceTint = 0.4; s.RadiusScale = 0.85; s.Roles = new(StringComparer.Ordinal) { ["Tertiary"] = "#C7B26B" }; }), true),
            new("Sunset", S(s => { s.Theme = "Light"; s.Seed = "#E2582C"; s.SurfaceTint = 0.35; s.RadiusScale = 1.3; s.HeadingWeight = 700; s.Roles = new(StringComparer.Ordinal) { ["Tertiary"] = "#8A4FB3" }; }), true),
            new("Mono", S(s => { s.Theme = "Dark"; s.Seed = "#8A8A8A"; s.RadiusScale = 0.6; s.Elevation = 0.5; s.Roles = new(StringComparer.Ordinal) { ["Tertiary"] = "#BDBDBD", ["TertiaryContainer"] = "#3A3A3A", ["OnTertiaryContainer"] = "#EEEEEE" }; }), true)
        ];
    }

    private sealed class PresetFile
    {
        public string Format { get; set; } = FormatName;
        public int Version { get; set; } = 1;
        public string Name { get; set; } = "";
        public AppearanceState? Appearance { get; set; }
    }
    private sealed class StoreFile
    {
        public int Version { get; set; } = 1;
        public List<PresetFile> Presets { get; set; } = new();
    }
    private static readonly JsonSerializerOptions Strict = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, PropertyNameCaseInsensitive = true };

    public static string? ValidateName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "Enter a name." : name.Trim().Length > 60 ? "Use at most 60 characters." : name.Any(char.IsControl) ? "Names cannot contain control characters." : null;

    public static string Export(AppearancePreset preset) => JsonSerializer.Serialize(new PresetFile { Name = preset.Name, Appearance = preset.State.Clone() }, Strict);

    /// <summary>Parses and validates one exported preset. Throws <see cref="InvalidDataException"/> with a readable reason.</summary>
    public static AppearancePreset Import(string text)
    {
        if (text.Length > MaxPresetBytes) throw new InvalidDataException("The file is larger than 64 KiB, so it is not an appearance preset.");
        PresetFile file;
        try { file = JsonSerializer.Deserialize<PresetFile>(text, Strict) ?? throw new InvalidDataException("The file is empty."); }
        catch (JsonException e) { throw new InvalidDataException("The file is not valid preset JSON: " + e.Message); }
        return FromFile(file);
    }

    private static AppearancePreset FromFile(PresetFile file)
    {
        if (file.Format != FormatName) throw new InvalidDataException($"The format must be \"{FormatName}\".");
        if (file.Version != 1) throw new InvalidDataException("Only preset version 1 is supported.");
        if (ValidateName(file.Name) is { } nameProblem) throw new InvalidDataException("Preset name: " + nameProblem);
        if (file.Appearance == null) throw new InvalidDataException("The preset has no appearance.");
        var problems = file.Appearance.Validate();
        if (problems.Count > 0) throw new InvalidDataException(string.Join(" ", problems.Take(5)));
        var state = file.Appearance.Clone();
        state.Normalize();
        return new AppearancePreset(file.Name.Trim(), state);
    }

    /// <summary>Loads user presets. A missing file is an empty list; an invalid file throws so nothing is overwritten silently.</summary>
    public static List<AppearancePreset> LoadUser(string path)
    {
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > MaxStoreBytes) throw new InvalidDataException("appearance-presets.json is larger than 512 KiB.");
        StoreFile store;
        try { store = JsonSerializer.Deserialize<StoreFile>(File.ReadAllText(path), Strict) ?? new(); }
        catch (JsonException e) { throw new InvalidDataException("appearance-presets.json is not valid: " + e.Message); }
        if (store.Version != 1) throw new InvalidDataException("appearance-presets.json has an unsupported version.");
        return (store.Presets ?? new()).Take(MaxUserPresets).Select(FromFile).ToList();
    }

    public static void SaveUser(string path, IEnumerable<AppearancePreset> presets)
    {
        var list = presets.Where(p => !p.BuiltIn).Take(MaxUserPresets).Select(p => new PresetFile { Name = p.Name, Appearance = p.State.Clone() }).ToList();
        var json = JsonSerializer.Serialize(new StoreFile { Presets = list }, Strict);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, true);
    }
}

/// <summary>Bounded undo and redo of appearance snapshots (canonical JSON).</summary>
public sealed class AppearanceUndoStack
{
    private readonly LinkedList<string> undo = new();
    private readonly Stack<string> redo = new();
    public int Capacity { get; }
    public AppearanceUndoStack(int capacity = 100) => Capacity = Math.Max(1, capacity);
    public int UndoCount => undo.Count;
    public int RedoCount => redo.Count;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;

    /// <summary>Records the state before a change. A new change clears the redo stack.</summary>
    public void Push(AppearanceState before)
    {
        var json = before.ToJson();
        if (undo.Last?.Value == json) { redo.Clear(); return; }
        undo.AddLast(json);
        while (undo.Count > Capacity) undo.RemoveFirst();
        redo.Clear();
    }

    public AppearanceState? Undo(AppearanceState current)
    {
        if (undo.Last is not { } last) return null;
        undo.RemoveLast();
        redo.Push(current.ToJson());
        return AppearanceState.FromJson(last.Value);
    }

    public AppearanceState? Redo(AppearanceState current)
    {
        if (redo.Count == 0) return null;
        var next = redo.Pop();
        undo.AddLast(current.ToJson());
        while (undo.Count > Capacity) undo.RemoveFirst();
        return AppearanceState.FromJson(next);
    }

    public void Clear() { undo.Clear(); redo.Clear(); }
}
