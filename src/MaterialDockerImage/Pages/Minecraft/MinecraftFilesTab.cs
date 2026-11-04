using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Browses /data inside the running server (docker exec ls -la) and copies files or folders out to this computer or a
/// file in with docker cp. Existing files are never overwritten without confirmation.
/// </summary>
internal sealed class MinecraftFilesTab : MinecraftTab
{
    private readonly DataTable<MinecraftOutput.FileEntry> table = new("Server files", "Filter files");
    private readonly ContentControl notice = new();
    private readonly TextBlock pathText = Ui.Mono("/data", 14);
    private readonly Button up;
    private string path = "/data";
    private int version;

    public MinecraftFilesTab(MinecraftServerDetail detail) : base(detail)
    {
        table.Key = f => f.Name;
        table.EmptyTitle = Loc("Empty folder");
        table.EmptyMessage = Loc("This folder has no files. Use Copy file in to add one.");
        table.ChipColumn("Kind", f => f.IsDirectory ? "folder" : "file", f => f.IsDirectory ? Tone.Primary : Tone.Neutral, 90)
            .Column("Name", f => f.Name, 260, true)
            .Column("Size", f => f.IsDirectory ? "—" : Ui.FormatBytes(f.Size), 100)
            .Column("Modified", f => f.Modified, 140)
            .Column("Permissions", f => f.Permissions, 120, true)
            .Filter("Folders", f => f.IsDirectory)
            .Filter("Files", f => !f.IsDirectory)
            .RowAction("Open folder", Icons.FolderOpen, f => { if (f.IsDirectory) Navigate(Join(path, f.Name)); return Task.CompletedTask; }, "Enter")
            .RowAction("Copy out to this computer…", Icons.Download, CopyOutAsync);
        table.Activated += f => { if (f.IsDirectory) Navigate(Join(path, f.Name)); };
        up = Ui.IconButton(Icons.ArrowUpward, "Parent folder", () => Navigate(ParentPath(path)));
        var bar = Ui.Wrap(8,
            up,
            Ui.Card(pathText, 8, "SurfaceHigh", 10),
            Ui.AsyncIconButton(Icons.Refresh, "Refresh files", () => LoadAsync(++version)),
            Ui.AsyncButton("Copy file in…", Icons.Upload, ButtonKind.Tonal, CopyInAsync));
        var top = Ui.VStack(8, notice, bar);
        top.Margin = new Thickness(0, 0, 0, 8);
        Children.Add(top.Dock(Dock.Top));
        Children.Add(table);
    }

    public override void OnShown() => _ = LoadAsync(++version);

    private static string Join(string folder, string name) => folder.TrimEnd('/') + "/" + name;
    private static string ParentPath(string p) => p == "/data" ? "/data" : p[..p.TrimEnd('/').LastIndexOf('/')] is { Length: > 0 } x && x.StartsWith("/data", StringComparison.Ordinal) ? x : "/data";

    private void Navigate(string target) { path = target; _ = LoadAsync(++version); }

    private async Task LoadAsync(int mine)
    {
        pathText.Text = path;
        up.IsEnabled = path != "/data";
        if (!Server.IsRunning)
        {
            notice.Content = Notice("Start the server to browse its files (docker exec needs a running container). Copy out of the whole world still works from the World tab.", Tone.Warning);
            table.SetItems([]);
            return;
        }
        var r = await Host.Read(["exec", Server.Id, "ls", "-la", path], Context);
        if (mine != version) return;
        if (!r.Success) { notice.Content = Notice(Loc("The folder could not be listed:") + " " + MinecraftOps.Trim(r), Tone.Warning); table.SetItems([]); return; }
        notice.Content = null;
        table.SetItems(MinecraftOutput.ParseLs(r.StandardOutput).OrderByDescending(f => f.IsDirectory).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase));
    }

    private async Task CopyOutAsync(MinecraftOutput.FileEntry f)
    {
        if (!EnsureCurrent()) return;
        var picker = new OpenFolderDialog { Title = Loc("Choose the folder to copy into") };
        if (picker.ShowDialog() != true) return;
        var local = Path.Combine(picker.FolderName, f.Name);
        if ((File.Exists(local) || Directory.Exists(local)) && !await Host.Overlay.ConfirmAsync(Loc("Replace") + " " + f.Name + "?", $"{local} already exists. docker cp overwrites files with the same names.", "Replace", true)) return;
        var r = await Host.Execute($"Copy {f.Name} out of {Server.Name}", ["container", "cp", Server.Id + ":" + Join(path, f.Name), local], true, Context);
        if (r is { Success: true }) Host.Notify.Show(Services.NoticeKind.Success, Loc("Copied to") + " " + local, "Open folder", () => PageParts.OpenExternal(picker.FolderName));
    }

    private async Task CopyInAsync()
    {
        if (!EnsureCurrent()) return;
        if (!Server.IsRunning) { Host.Notify.Error("Start the server first so the folder can be checked before copying."); return; }
        var file = PageKit.PickOpenPath(Loc("Choose a file to copy into ") + path, "All files|*.*");
        if (file == null) return;
        var name = Path.GetFileName(file);
        if (table.Items.Any(x => x.Name == name) && !await Host.Overlay.ConfirmAsync("Replace " + name + "?", $"{Join(path, name)} already exists on the server and will be overwritten.", "Replace", true)) return;
        var r = await Host.Execute($"Copy {name} into {Server.Name}", ["container", "cp", file, Server.Id + ":" + path.TrimEnd('/') + "/"], true, Context);
        if (r is { Success: true }) Host.History.Record("minecraft", $"Copied {name} into {Server.Name}:{path}");
        await LoadAsync(++version);
    }

    private static string Loc(string text) => Services.Loc.T(text);
}
