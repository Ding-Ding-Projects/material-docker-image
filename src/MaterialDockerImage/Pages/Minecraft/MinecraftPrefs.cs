using System.Text.Json;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Folders chosen on the Minecraft page, kept in minecraft.json beside settings.json. No secrets are stored.</summary>
internal sealed class MinecraftPrefs
{
    public string BackupFolder { get; set; } = "";
    public string ExportFolder { get; set; } = "";

    private static string FilePath => Path.Combine(UserSettings.DataDirectory, "minecraft.json");
    public static string DefaultBackupFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Minecraft server backups");
    public static bool LoadedFromFile { get; private set; }

    private static MinecraftPrefs? current;
    public static MinecraftPrefs Current => current ??= Load();

    private static MinecraftPrefs Load()
    {
        try
        {
            if (File.Exists(FilePath)) { LoadedFromFile = true; return JsonSerializer.Deserialize<MinecraftPrefs>(File.ReadAllText(FilePath)) ?? new(); }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        return new MinecraftPrefs();
    }

    public string EffectiveBackupFolder => string.IsNullOrWhiteSpace(BackupFolder) ? DefaultBackupFolder : BackupFolder;

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(UserSettings.DataDirectory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            LoadedFromFile = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Services.AppHost.Current.Notify.Error("Minecraft folder choices could not be saved: " + e.Message); }
    }
}
