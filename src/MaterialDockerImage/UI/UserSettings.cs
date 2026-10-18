using System;
using System.IO;
using System.Text.Json;

namespace MaterialDockerImage.UI;

public sealed class UserSettings
{
    public string Theme { get; set; } = "Dark";
    public string Language { get; set; } = "English";
    public string Context { get; set; } = "";
    public string BuildFolder { get; set; } = "";
    public string ImageTag { get; set; } = "my-image:latest";
    public bool SetupComplete { get; set; }
    public int SetupStep { get; set; }
    public bool ReducedMotion { get; set; }
    public double FontSize { get; set; } = 14;
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DingDingProjects", "MaterialDockerImage");
    private static string LegacyDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MaterialDockerImage");
    public static UserSettings Load()
    {
        try
        {
            var path = Path.Combine(DataDirectory, "settings.json");
            if (!File.Exists(path)) path = Path.Combine(LegacyDirectory, "settings.json");
            if (new FileInfo(path).Length > 65536) return new();
            return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        var path = Path.Combine(DataDirectory, "settings.json");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }
}
