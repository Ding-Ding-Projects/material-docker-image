using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage.Pages;

/// <summary>Stats, Processes and Files tabs. Each reads on demand and offers its own Refresh.</summary>
internal sealed partial class ContainerInspector
{
    private UIElement StatsView(ContainerRow row)
    {
        var host = new ContentControl();
        var stamp = Ui.Caption("");
        async Task Load()
        {
            var my = version;
            host.Content = Ui.Caption("Sampling once with docker container stats --no-stream…");
            if (!row.IsRunning) { host.Content = Ui.Caption("Statistics are sampled only while the container is running. A stopped or paused container reports zero usage."); return; }
            var result = await Host.Read(["container", "stats", "--no-stream", "--format", "{{json .}}", row.Id], page.Context);
            if (my != version) return;
            if (!result.Success) { host.Content = Error("docker container stats failed: " + result.StandardError.Trim()); return; }
            JsonElement stats;
            try { var rows = DockerJson.ParseRows(result.StandardOutput); if (rows.Count == 0) { host.Content = Ui.Caption("The engine returned no statistics."); return; } stats = rows[0]; }
            catch (JsonException e) { host.Content = Error("Statistics could not be parsed: " + e.Message); return; }
            host.Content = Ui.VStack(10,
                Tile("CPU", DockerJson.Text(stats, "CPUPerc"), "Share of one CPU core; above 100% means several cores."),
                Tile("Memory", DockerJson.Text(stats, "MemUsage"), DockerJson.Text(stats, "MemPerc") + " of the limit"),
                Tile("Network I/O", DockerJson.Text(stats, "NetIO"), "Received / sent since start"),
                Tile("Block I/O", DockerJson.Text(stats, "BlockIO"), "Read / written since start"),
                Tile("Processes", DockerJson.Text(stats, "PIDs"), "PIDs inside the container"));
            stamp.Text = "Sampled at " + DateTime.Now.ToString("HH:mm:ss") + ". Monitor shows live charts over time.";
        }
        var refresh = Ui.AsyncButton("Refresh", Icons.Refresh, ButtonKind.Tonal, Load);
        var view = Ui.VStack(12, Ui.Columns(8, [1, 0], stamp, refresh), host);
        _ = Ui.RunGuarded(refresh, Load);
        return Ui.Scroll(view);
    }

    private static UIElement Tile(string label, string value, string note) => Ui.Card(Ui.VStack(2,
        Ui.Overline(label),
        Ui.Text(value.Length == 0 ? "—" : value, 18, FontWeights.SemiBold, "OnSurface", true, false),
        Ui.Caption(note)), 12, "SurfaceContainer", 12);

    private UIElement ProcessesView(ContainerRow row)
    {
        var text = new SearchableText("Processes", PageKit.SafeFileName(row.Name) + "-processes.txt", "Search processes") { KeepFirstLine = true };
        text.Search.Width = 200;
        var note = Ui.Caption("");
        async Task Load()
        {
            var my = version;
            if (!row.IsRunning && !row.IsPaused) { note.Text = "Processes are listed only for a running or paused container."; text.SetText(""); return; }
            note.Text = "Reading docker container top…";
            var result = await Host.Read(["container", "top", row.Id], page.Context);
            if (my != version) return;
            if (!result.Success) { note.Text = "docker container top failed: " + result.StandardError.Trim(); return; }
            text.SetText(result.StandardOutput);
            note.Text = "Read at " + DateTime.Now.ToString("HH:mm:ss") + ".";
        }
        var refresh = Ui.AsyncButton("Refresh", Icons.Refresh, ButtonKind.Tonal, Load);
        text.Extra.Children.Add(refresh);
        var dock = new DockPanel();
        dock.Children.Add(note.Dock(System.Windows.Controls.Dock.Top).Margin(0, 0, 0, 8));
        dock.Children.Add(text);
        _ = Ui.RunGuarded(refresh, Load);
        return dock;
    }

    private UIElement FilesView(ContainerRow row)
    {
        var changes = new SearchableText("Filesystem changes", PageKit.SafeFileName(row.Name) + "-diff.txt", "Search changed paths");
        changes.Search.Width = 200;
        var note = Ui.Caption("");
        async Task Load()
        {
            var my = version;
            note.Text = "Reading docker container diff…";
            var result = await Host.Read(["container", "diff", row.Id], page.Context);
            if (my != version) return;
            if (!result.Success) { note.Text = "docker container diff failed: " + result.StandardError.Trim(); return; }
            var lines = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            changes.SetText(string.Join("\n", lines));
            note.Text = lines.Length == 0 ? "No changes compared with the image." : $"{lines.Length} changed paths compared with the image. A = added, C = changed, D = deleted.";
        }
        var refresh = Ui.AsyncButton("Refresh", Icons.Refresh, ButtonKind.Tonal, Load);
        changes.Extra.Children.Add(refresh);
        var copyOut = Ui.AsyncButton("Copy from container…", Icons.Download, ButtonKind.Outlined, () => CopyFromAsync(row));
        var copyIn = Ui.AsyncButton("Copy into container…", Icons.Upload, ButtonKind.Outlined, () => CopyIntoAsync(row));
        var dock = new DockPanel();
        dock.Children.Add(Ui.VStack(8, Ui.Wrap(8, copyOut, copyIn), note).Dock(System.Windows.Controls.Dock.Top).Margin(0, 0, 0, 8));
        dock.Children.Add(changes);
        _ = Ui.RunGuarded(refresh, Load);
        return dock;
    }

    private static string? ContainerPath(string value)
    {
        if (value.Length == 0) return "Enter an absolute path inside the container.";
        if (!value.StartsWith('/') && !System.Text.RegularExpressions.Regex.IsMatch(value, @"^[A-Za-z]:[\\/]", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100))) return "Use an absolute path inside the container, for example /app/config.";
        try { StructuredInputs.Token(value); } catch (ArgumentException e) { return e.Message; }
        return null;
    }

    private async Task CopyFromAsync(ContainerRow row)
    {
        if (!page.EnsureCurrent()) return;
        var source = await Host.Overlay.PromptAsync("Copy from " + row.Name, "Path in the container", "/", "docker cp copies a file or folder out of the container into a folder you choose next. Works for running and stopped containers.", ContainerPath);
        if (source == null) return;
        var picker = new OpenFolderDialog { Title = Loc.T("Choose the local folder to copy into") };
        if (picker.ShowDialog() != true) return;
        var name = source.TrimEnd('/').Split('/').LastOrDefault() is { Length: > 0 } n ? n : "root";
        var target = Path.Combine(picker.FolderName, name);
        if ((File.Exists(target) || Directory.Exists(target)) &&
            !await Host.Overlay.ConfirmAsync("Overwrite local files?", $"{target} already exists. docker cp replaces files with the same names inside it.", "Overwrite", true)) return;
        if (!page.EnsureCurrent()) return;
        var result = await Host.Execute($"Copy {row.Name}:{source} to {picker.FolderName}", ["container", "cp", row.Id + ":" + source, picker.FolderName], boundContext: page.Context);
        if (result?.Success == true) Host.Notify.Info("Copied to " + target + ".");
    }

    private async Task CopyIntoAsync(ContainerRow row)
    {
        if (!page.EnsureCurrent()) return;
        var file = PageKit.PickOpenPath("Choose a local file to copy into " + row.Name, "All files|*.*");
        if (file == null) return;
        var target = await Host.Overlay.PromptAsync("Copy into " + row.Name, "Destination path in the container", "/tmp/", $"Copies {Path.GetFileName(file)} into the container. A path ending in / is treated as a folder. Existing files at the destination are replaced.", ContainerPath);
        if (target == null) return;
        if (!await Host.Overlay.ConfirmAsync("Copy into container?", $"{file}\n→ {row.Name}:{target}\n\nAn existing file with the same path inside the container is replaced. This changes the container's filesystem.", "Copy", true)) return;
        if (!page.EnsureCurrent()) return;
        var result = await Host.Execute($"Copy {Path.GetFileName(file)} into {row.Name}", ["container", "cp", Path.GetFullPath(file), row.Id + ":" + target], boundContext: page.Context);
        if (result?.Success == true && tab == "Files") ShowTab();
    }
}
