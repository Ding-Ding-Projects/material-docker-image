using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MaterialDockerImage.UI;

/// <summary>Per-user preferences stored as JSON in local application data, outside the installation root.</summary>
public sealed class UserSettings
{
    public const string ShippedName = "Material Docker Image";
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
    public bool Narration { get; set; }
    public string NarrationVoice { get; set; } = "";
    public int NarrationRate { get; set; }
    public string DisplayName { get; set; } = ShippedName;
    public bool SchoolMode { get; set; }
    public bool FocusMode { get; set; }
    public bool LowStimulation { get; set; }
    public bool TimeAwareness { get; set; }
    public bool OneThingAtATime { get; set; }
    public bool Momentum { get; set; }
    public bool PaletteFullWindow { get; set; }
    public bool OutputDockOpen { get; set; } = true;
    public string LastDestination { get; set; } = "Build";
    public List<string> OpenTabs { get; set; } = new();
    public List<string> PinnedTabs { get; set; } = new();
    public string TabDock { get; set; } = "Top";
    public Dictionary<string, bool> CollapsedFilters { get; set; } = new();
    public string ExternalEditor { get; set; } = "";
    public int MonitorIntervalSeconds { get; set; } = 2;
    public string PersonalVocabularyPath { get; set; } = "";
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
        if (string.IsNullOrWhiteSpace(DisplayName) || DisplayName.Length > 80) DisplayName = ShippedName;
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
