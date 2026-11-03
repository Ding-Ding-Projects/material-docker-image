using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage.Pages;

/// <summary>A backup archive found in the backup folder.</summary>
internal sealed record MinecraftBackupFile(string Path, string Name, long Size, DateTime Modified);

/// <summary>
/// Backups of the server volume as .tar.gz files in a folder on this computer: create (with RCON save-off/flush/on),
/// list with search, restore (super confirmation, safety backup first), show and delete.
/// </summary>
internal sealed class MinecraftBackupsTab : MinecraftTab
{
    private readonly DataTable<MinecraftBackupFile> table = new("Backups", "Filter backups");
    private readonly TextBlock folderText = Ui.Mono("", 13, "OnSurface", true);
    private readonly TextBlock provenance = Ui.Caption("");
    private readonly TextBlock summary = Ui.Caption("");

    public MinecraftBackupsTab(MinecraftServerDetail detail) : base(detail)
    {
        table.Key = b => b.Path;
        table.EmptyTitle = "No backups in this folder";
        table.EmptyMessage = "Create backup archives the whole /data volume (world, settings, plugins) into this folder.";
        table.Column("File", b => b.Name, 300, true)
            .Column("Size", b => Ui.FormatBytes(b.Size), 100)
            .Column("Created", b => b.Modified.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), 160)
            .RowAction("Restore…", Icons.Restore, RestoreAsync, null, true)
            .RowAction("Show in folder", Icons.FolderOpen, b => { PageParts.OpenExternal(System.IO.Path.GetDirectoryName(b.Path)!); return Task.CompletedTask; })
            .RowAction("Delete file…", Icons.Delete, DeleteAsync, "Del", true);
        var browse = Ui.Button("Browse", Icons.FolderOpen, ButtonKind.Outlined, Browse);
        var open = Ui.Button("Open folder", Icons.OpenInNew, ButtonKind.Text, () => PageParts.OpenFolder(MinecraftPrefs.Current.EffectiveBackupFolder));
        var create = Ui.AsyncButton("Create backup", Icons.Backup, ButtonKind.Filled, CreateAsync);
        var folderField = Ui.Field("Backup folder", Ui.VStack(6, Ui.Columns(8, [1, 0, 0], Ui.Card(folderText, 10, "SurfaceHigh", 10), browse, open), provenance), "Archives are written here as mc-<server>-<date>-<time>.tar.gz. Keep a copy on another disk for real protection.", "C:\\Users\\you\\Documents\\Minecraft server backups");
        var how = Ui.Caption("Create backup pauses saving through RCON (save-off, save-all flush), archives the volume read-only with a short-lived busybox container, copies the archive here with docker cp and turns saving back on. It works with remote engine contexts too.");
        var top = Ui.VStack(10, folderField, how, Ui.Columns(8, [1, 0], summary, create));
        top.Margin = new Thickness(0, 0, 0, 12);
        Children.Add(top.Dock(Dock.Top));
        Children.Add(table);
    }

    public override void OnShown() => Load();

    private void Load()
    {
        var folder = MinecraftPrefs.Current.EffectiveBackupFolder;
        folderText.Text = folder;
        provenance.Text = string.IsNullOrWhiteSpace(MinecraftPrefs.Current.BackupFolder) ? "Default: Documents\\Minecraft server backups (built-in)" : "Saved in minecraft.json beside settings.json";
        var list = new List<MinecraftBackupFile>();
        try
        {
            if (Directory.Exists(folder))
                foreach (var f in new DirectoryInfo(folder).EnumerateFiles(MinecraftOps.BackupPrefix(Server) + "*.tar.gz"))
                    list.Add(new MinecraftBackupFile(f.FullName, f.Name, f.Length, f.LastWriteTime));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { summary.Text = "The folder could not be read: " + e.Message; }
        table.SetItems(list.OrderByDescending(b => b.Modified));
        summary.Text = list.Count == 0 ? $"No backups of {Server.Name} yet." : $"{list.Count} backups of {Server.Name} · {Ui.FormatBytes(list.Sum(b => b.Size))} in total · newest {list.Max(b => b.Modified):yyyy-MM-dd HH:mm}";
    }

    private void Browse()
    {
        var picker = new OpenFolderDialog { Title = Services.Loc.T("Choose the backup folder") };
        if (picker.ShowDialog() != true) return;
        MinecraftPrefs.Current.BackupFolder = picker.FolderName;
        MinecraftPrefs.Current.Save();
        Load();
    }

    private async Task CreateAsync()
    {
        await Detail.Page.BackupAsync(Server);
        Load();
    }

    private async Task RestoreAsync(MinecraftBackupFile b)
    {
        if (!EnsureCurrent()) return;
        var s = Server;
        var affected = new List<string> { $"every file in volume {s.VolumeName} (replaced by {b.Name})" };
        if (s.IsRunning) affected.Add($"server {s.Name} is stopped during the restore and started again afterwards");
        affected.Add("a safety backup of the current data is written to the backup folder first");
        if (!await Host.Overlay.SuperConfirmAsync("Restore backup", $"Replaces the world and every other file of {s.Name} with the contents of {b.Name}. Changes since that backup are lost unless you keep the safety backup.", affected)) return;
        var safety = await MinecraftOps.BackupAsync(s, MinecraftPrefs.Current.EffectiveBackupFolder, Context);
        if (safety == null) { Host.Notify.Error("The safety backup failed, so nothing was restored."); return; }
        var wasRunning = s.IsRunning;
        if (wasRunning && !await MinecraftOps.StopAsync(s, Context)) { Host.Notify.Error("The server could not be stopped, so nothing was restored."); return; }
        var ok = await MinecraftOps.RestoreAsync(s, b.Path, Context);
        if (wasRunning) await MinecraftOps.StartAsync(s, Context);
        if (ok) Host.Notify.Success($"Restored {s.Name} from {b.Name}. Safety backup: {System.IO.Path.GetFileName(safety)}.");
        else Host.Notify.Error($"The restore did not complete. The safety backup {System.IO.Path.GetFileName(safety)} holds the data from before the attempt.");
        await Detail.ReloadAsync();
        Load();
    }

    private async Task DeleteAsync(MinecraftBackupFile b)
    {
        if (!await Host.Overlay.SuperConfirmAsync("Delete backup file", "Permanently deletes this backup archive from the folder.", [b.Path])) return;
        try { File.Delete(b.Path); Host.History.Record("minecraft", "Deleted Minecraft backup " + b.Path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Host.Notify.Error("The file could not be deleted: " + e.Message); }
        Load();
    }
}
