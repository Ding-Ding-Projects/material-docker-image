using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;

namespace MaterialDockerImage.UI;

/// <summary>Per-user preferences stored as JSON in local application data, outside the installation root.</summary>
public sealed class UserSettings
{
    public const string ShippedName = AppIdentity.ShippedName;
    public string Theme { get; set; } = "Dark";
    public string SeedColor { get; set; } = "#3A6FD8";
    public string Density { get; set; } = "Comfortable";
    public string Language { get; set; } = "English";
    public int FunnyEnglish { get; set; } = 5;
    public int FunnyCantonese { get; set; } = 5;
    public bool DialogEmoji { get; set; } = true;
    public string Context { get; set; } = "";
    public string BuildFolder { get; set; } = "";
    public string ImageTag { get; set; } = "my-image:latest";
    public bool SetupComplete { get; set; }
    public int SetupStep { get; set; }
    public bool ReducedMotion { get; set; }
    public bool HighContrast { get; set; }
    public bool LargerTargets { get; set; }
    public bool AccessibilityMenuVisible { get; set; } = true;
    public double FontSize { get; set; } = 14;
    public bool Sounds { get; set; } = true;
    /// <summary>Action sound volume, 0 to 100 percent of the system sound's own level. Applied immediately.</summary>
    public int SoundVolume { get; set; } = 100;
    /// <summary>When on, no cue plays between QuietHoursStart and QuietHoursEnd (local time, may cross midnight).</summary>
    public bool QuietHoursEnabled { get; set; }
    public string QuietHoursStart { get; set; } = "22:00";
    public string QuietHoursEnd { get; set; } = "07:00";
    /// <summary>Checks the published update feed in the background when the app was installed by the installer. Never restarts by itself.</summary>
    public bool AutoUpdateCheck { get; set; } = true;
    public bool Narration { get; set; }
    public string NarrationVoice { get; set; } = "";
    public int NarrationRate { get; set; }
    public string DisplayName { get; set; } = ShippedName;
    /// <summary>Title-bar logo: Default, Symbol (a bundled Material Symbol) or Image (a validated copy in the app data folder).</summary>
    public string LogoKind { get; set; } = LogoKinds.Default;
    public string LogoSymbol { get; set; } = AppIdentity.DefaultSymbolName;
    /// <summary>File name inside the app data logo folder; never a path.</summary>
    public string LogoImageFile { get; set; } = "";
    public bool SchoolMode { get; set; }
    public bool FocusMode { get; set; }
    public bool LowStimulation { get; set; }
    public bool TimeAwareness { get; set; }
    public bool OneThingAtATime { get; set; }
    public bool Momentum { get; set; }
    public bool PaletteFullWindow { get; set; }
    public bool OutputDockOpen { get; set; }
    public string LastDestination { get; set; } = "Build";
    public List<string> OpenTabs { get; set; } = new();
    public List<string> PinnedTabs { get; set; } = new();
    public string TabDock { get; set; } = "Top";
    public Dictionary<string, bool> CollapsedFilters { get; set; } = new();
    /// <summary>Ids of settings rows the person locked against change (see ElementLocks).</summary>
    /// <summary>Locked items stay listed and labelled in searches and the command palette; this hides them instead, and every list says when it did.</summary>
    public bool HideLockedInSearch { get; set; }
    /// <summary>Shuffles the digit positions of the on-screen PIN keypad. Off by default: the standard layout is used unless the person chooses this.</summary>
    public bool PinKeypadShuffle { get; set; }
    public List<string> LockedSettings { get; set; } = new();
    /// <summary>Readable names for locks on elements that are not settings rows, so the Locks manager can say what each lock is. Never holds a credential.</summary>
    public Dictionary<string, string> LockLabels { get; set; } = new();
    /// <summary>Scheduled setting rules (see ScheduleEvaluator). The base values of every lent setting stay what the person chose.</summary>
    public List<ScheduleRule> ScheduleRules { get; set; } = new();
    /// <summary>Optional local JSON file that supplies values for rules whose source is File. It is watched for changes; nothing is fetched from a network.</summary>
    public string ScheduleSourceFile { get; set; } = "";
    /// <summary>Optional https address that supplies values for rules whose source is Https. Read with GET only; never holds a user name, password or secret.</summary>
    public string ScheduleHttpsUrl { get; set; } = "";
    public int ScheduleHttpsPollMinutes { get; set; } = ScheduleSourceRules.DefaultHttpsPollMinutes;
    /// <summary>Home Assistant base address for rules whose source is HomeAssistant. The long-lived access token is NOT here: it is stored separately with Windows DPAPI for the current user.</summary>
    public string ScheduleHaBaseUrl { get; set; } = "";
    public string ScheduleHaEntity { get; set; } = "";
    public int ScheduleHaPollMinutes { get; set; } = ScheduleSourceRules.DefaultHomeAssistantPollMinutes;
    public string ExternalEditor { get; set; } = "";
    /// <summary>Opens interactive container terminals in a classic console window instead of the Windows default terminal application.</summary>
    public bool ClassicConsoleWindow { get; set; }
    /// <summary>Opens Suggestions automatically while typing in the Dockerfile and Compose editors. Ctrl+Space always works.</summary>
    public bool SuggestionsWhileTyping { get; set; } = true;
    public int MonitorIntervalSeconds { get; set; } = 2;
    public string PersonalVocabularyPath { get; set; } = "";
    // Appearance editor values (see AppearanceState). Ranges are enforced by AppearanceState.Normalize.
    public string MotionLevel { get; set; } = "Standard";
    public double MotionSpeed { get; set; } = 1;
    public double CornerScale { get; set; } = 1;
    public string UiFontFamily { get; set; } = "";
    public string MonoFontFamily { get; set; } = "";
    public int HeadingWeight { get; set; } = 600;
    public string RailIconStyle { get; set; } = "Outline";
    public double ElevationScale { get; set; } = 1;
    public double SurfaceTint { get; set; }
    public bool RainbowAccent { get; set; }
    public Dictionary<string, string> RoleOverrides { get; set; } = new();
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DingDingProjects", "MaterialDockerImage");
    private static string LegacyDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MaterialDockerImage");
    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    /// <summary>True when the values came from a saved file rather than compiled-in defaults.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool LoadedFromFile { get; private set; }
    public static UserSettings Load()
    {
        try
        {
            var path = SettingsPath;
            if (!File.Exists(path)) path = Path.Combine(LegacyDirectory, "settings.json");
            if (!File.Exists(path)) return new();
            if (new FileInfo(path).Length > 262144) return new();
            var loaded = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path)) ?? new();
            loaded.LoadedFromFile = true;
            loaded.Normalize();
            return loaded;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or NotSupportedException) { return new(); }
    }
    public void Normalize()
    {
        FunnyEnglish = Math.Clamp(FunnyEnglish, 1, 5);
        FunnyCantonese = Math.Clamp(FunnyCantonese, 1, 5);
        FontSize = Math.Clamp(FontSize, 12, 22);
        MonitorIntervalSeconds = Math.Clamp(MonitorIntervalSeconds, 1, 30);
        if (Theme is not ("Dark" or "Light" or "System")) Theme = "Dark";
        if (Language is not ("English" or "Cantonese" or "Bilingual")) Language = "English";
        DisplayName = AppIdentity.NameOrShipped(DisplayName);
        if (!LogoKinds.All.Contains(LogoKind)) LogoKind = LogoKinds.Default;
        if (string.IsNullOrWhiteSpace(LogoSymbol) || LogoSymbol.Length > 60) LogoSymbol = AppIdentity.DefaultSymbolName;
        if (!AppIdentity.IsStoredLogoName(LogoImageFile)) LogoImageFile = "";
        OpenTabs ??= new(); PinnedTabs ??= new();
        CollapsedFilters = CollapseFilterState.Normalize(CollapsedFilters);
        LockedSettings = ElementLocks.Normalize(LockedSettings);
        LockLabels = ElementLocks.NormalizeLabels(LockLabels);
        ScheduleRules = ScheduleEvaluator.Normalize(ScheduleRules);
        ScheduleSourceFile = (ScheduleSourceFile ?? "").Trim().Trim('"');
        if (ScheduleSourceFile.Length > 260) ScheduleSourceFile = "";
        ScheduleHttpsUrl = ScheduleSourceRules.TryNormalizeHttpsUrl(ScheduleHttpsUrl, out var httpsUrl, out _) ? httpsUrl : "";
        ScheduleHttpsPollMinutes = ScheduleSourceRules.ClampPollMinutes(ScheduleHttpsPollMinutes, ScheduleSourceRules.MinHttpsPollMinutes);
        ScheduleHaBaseUrl = ScheduleSourceRules.TryNormalizeHomeAssistantUrl(ScheduleHaBaseUrl, out var haUrl, out _) ? haUrl : "";
        ScheduleHaEntity = ScheduleSourceRules.IsValidEntityId(ScheduleHaEntity) ? ScheduleHaEntity : "";
        ScheduleHaPollMinutes = ScheduleSourceRules.ClampPollMinutes(ScheduleHaPollMinutes, ScheduleSourceRules.MinHomeAssistantPollMinutes);
        SoundVolume = Math.Clamp(SoundVolume, 0, 100);
        if (!ScheduleEvaluator.TryParseTime(QuietHoursStart, out _)) QuietHoursStart = "22:00";
        if (!ScheduleEvaluator.TryParseTime(QuietHoursEnd, out _)) QuietHoursEnd = "07:00";
    }

    // ----- Scheduled overrides. The saved values are always the person's own choice; a schedule only changes the live values. -----
    private readonly Dictionary<string, (string Base, string Applied)> lent = new(StringComparer.Ordinal);
    [System.Text.Json.Serialization.JsonIgnore] public bool ScheduleOverrideActive => lent.Count > 0;

    private string LiveValue(string setting) => setting switch
    {
        ScheduleSettings.Language => Language,
        ScheduleSettings.Theme => Theme,
        ScheduleSettings.Density => Density,
        ScheduleSettings.Accent => SeedColor,
        ScheduleSettings.FontSize => FontSize.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        ScheduleSettings.Motion => MotionLevel,
        ScheduleSettings.DisplayName => DisplayName,
        _ => ""
    };

    private void SetLive(string setting, string value)
    {
        switch (setting)
        {
            case ScheduleSettings.Language: Language = value; break;
            case ScheduleSettings.Theme: Theme = value; break;
            case ScheduleSettings.Density: Density = value; break;
            case ScheduleSettings.Accent: SeedColor = value; break;
            case ScheduleSettings.FontSize: if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var size)) FontSize = size; break;
            case ScheduleSettings.Motion: MotionLevel = value; break;
            case ScheduleSettings.DisplayName: DisplayName = value; break;
        }
    }

    /// <summary>Applies the scheduled language and theme (null means no schedule for that setting). Every other lent setting is released.</summary>
    public bool ApplyScheduled(string? language, string? theme)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (language != null) values[ScheduleSettings.Language] = language;
        if (theme != null) values[ScheduleSettings.Theme] = theme;
        return ApplyScheduled(values);
    }

    /// <summary>
    /// Lends the scheduled values (setting name to already validated value) to the live settings and returns true when any live value changed.
    /// A setting missing from the map gets the person's own value back. A value the person changed by hand while it was lent becomes the new base instead of being overwritten.
    /// </summary>
    public bool ApplyScheduled(IReadOnlyDictionary<string, string> values)
    {
        var changed = false;
        foreach (var setting in ScheduleSettings.All)
        {
            if (lent.TryGetValue(setting, out var held) && !string.Equals(LiveValue(setting), held.Applied, StringComparison.OrdinalIgnoreCase)) lent.Remove(setting);
            if (values.TryGetValue(setting, out var value))
            {
                lent[setting] = lent.TryGetValue(setting, out var current) ? (current.Base, value) : (LiveValue(setting), value);
                if (!string.Equals(LiveValue(setting), value, StringComparison.OrdinalIgnoreCase)) { SetLive(setting, value); changed = true; }
            }
            else if (lent.TryGetValue(setting, out var release))
            {
                SetLive(setting, release.Base); lent.Remove(setting); changed = true;
            }
        }
        return changed;
    }

    /// <summary>Puts the saved choices back on the live values and forgets any override. Used before a reset or an import replaces the settings.</summary>
    public void ClearScheduledOverride() => ApplyScheduled(new Dictionary<string, string>());

    public string ToJson()
    {
        // The file always holds the person's own choice, never a value a schedule is currently lending.
        var live = lent.Keys.ToDictionary(k => k, LiveValue, StringComparer.Ordinal);
        foreach (var (setting, held) in lent) SetLive(setting, held.Base);
        try { return JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }); }
        finally { foreach (var (setting, value) in live) SetLive(setting, value); }
    }
    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        var path = SettingsPath;
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, ToJson());
        File.Move(temporary, path, true);
        LoadedFromFile = true;
    }
}
