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
    public string ExternalEditor { get; set; } = "";
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
        OpenTabs ??= new(); PinnedTabs ??= new(); CollapsedFilters ??= new();
    }
    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
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
