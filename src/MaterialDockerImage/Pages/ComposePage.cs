using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Compose projects from <c>docker compose ls --all</c> plus recently opened files. The selected project shows its
/// actions, profiles and services; its Compose file (when on this computer) is editable beside it.
/// </summary>
public sealed class ComposePage : PageBase, IDirtyAware
{
    public override string Key => "Compose";
    private readonly ComposeProjectList projects = new();
    private readonly ComposeProjectPanel panel = new();
    private readonly ComposeSourceEditor source = new();
    private readonly TextBlock subtitle;
    private readonly ContentControl detail = new();
    private readonly FrameworkElement noSelection;
    private readonly ScrollViewer narrowScroll = Ui.Scroll(null!);
    private string? selectKey;
    private bool switching;

    public bool IsDirty => source.IsDirty;
    public string DirtyLabel => Loc.T("Compose source:") + " " + source.LoadedPath;

    public ComposePage()
    {
        panel.Context = () => SnapshotContext;
        panel.IsCurrent = () => IsCurrent;
        panel.HasUnsavedEdits = () => source.IsDirty;
        panel.AfterAction = ReloadProjectsAsync;
        source.Saved = async path =>
        {
            var (valid, message) = await panel.ValidateAsync();
            source.SetValidation(valid, message);
            await panel.LoadDetailsAsync();
        };
        projects.SelectionChanged += e => _ = Guard(() => SelectAsync(e));
        projects.OpenFileRequested += path => _ = Guard(() => OpenFileAsync(path));
        var header = Ui.Header("Compose", "Compose projects, services and source", out subtitle,
            Ui.Button("Open Compose file…", Icons.FolderOpen, ButtonKind.Tonal, projects.Browse),
            Ui.AsyncButton("Refresh", Icons.Refresh, ButtonKind.Tonal, RefreshAsync));
        panel.SetSource(Ui.Card(source, 0, "SurfaceLow"));
        noSelection = Ui.EmptyState(Icons.Stacks, "Select a project", "Choose a project on the left, or open a Compose file to edit and run it.",
            Ui.Button("Open Compose file…", Icons.FolderOpen, ButtonKind.Filled, projects.Browse));
        detail.Content = noSelection;
        projects.MinWidth = 240;
        var body = Ui.Columns(12, [-300, 1], projects, detail);
        Layout(header, body);
        SizeChanged += (_, _) => Reflow();
    }

    /// <summary>At narrow widths the source editor moves under the project panel and the detail area scrolls.</summary>
    private void Reflow()
    {
        var narrow = ActualWidth > 0 && ActualWidth < 1280;
        panel.Stacked = narrow;
        if (detail.Content == noSelection) return;
        var wanted = narrow ? (object)narrowScroll : panel;
        if (detail.Content == wanted) return;
        narrowScroll.Content = null;
        detail.Content = null;
        if (narrow) narrowScroll.Content = panel;
        detail.Content = wanted;
    }

    private static async Task Guard(Func<Task> action)
    {
        try { await action(); }
        catch (Exception e) when (e is not OutOfMemoryException) { AppHost.Current.Notify.Error(e.Message); }
    }

    public override async Task RefreshAsync() => await ReloadProjectsAsync();

    private async Task ReloadProjectsAsync()
    {
        var context = SnapshotContext;
        var listed = new List<ComposeEntry>();
        string? error = null;
        try
        {
            var rows = await Host.ReadRows(["compose", "ls", "--all", "--format", "json"], context);
            foreach (var r in rows)
            {
                var files = CliValues.Str(r, "ConfigFiles").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                listed.Add(new ComposeEntry(CliValues.Str(r, "Name"), CliValues.Str(r, "Status"), files, true, false));
            }
        }
        catch (Exception e) when (e is InvalidOperationException or JsonException) { error = e.Message; }
        var recent = ComposeRecentFiles.Load();
        var entries = new List<ComposeEntry>();
        foreach (var e in listed)
        {
            var isRecent = e.Files.Any(f => recent.Contains(f, StringComparer.OrdinalIgnoreCase));
            entries.Add(e with { Recent = isRecent });
        }
        foreach (var path in recent)
        {
            if (listed.Any(e => e.Files.Contains(path, StringComparer.OrdinalIgnoreCase))) continue;
            var dir = Path.GetFileName(Path.GetDirectoryName(path)) ?? path;
            entries.Add(new ComposeEntry(dir, File.Exists(path) ? "file · not running" : "file missing", [path], false, true));
        }
        var running = listed.Count(e => e.StatusTone == Tone.Success);
        subtitle.Text = Loc.Line(error != null
            ? Loc.T("docker compose ls failed:") + " " + error
            : $"{listed.Count} project{(listed.Count == 1 ? "" : "s")} reported by the engine · {running} running · {entries.Count - listed.Count} recent file{(entries.Count - listed.Count == 1 ? "" : "s")} · context {context ?? "current"}");
        projects.SetEntries(entries, error != null ? "The engine list is unavailable; recent files are still shown." : null, selectKey ?? projects.Selected?.Key);
        selectKey = null;
        if (projects.Selected is { } current && panel.Entry?.Key == current.Key) await panel.ShowAsync(current);
    }

    private async Task OpenFileAsync(string path)
    {
        var full = Path.GetFullPath(path);
        if (source.IsDirty && !string.Equals(source.LoadedPath, full, StringComparison.OrdinalIgnoreCase))
        {
            if (!await source.LoadAsync(full)) return;
        }
        ComposeRecentFiles.Add(full);
        var listed = projects.Entries.FirstOrDefault(e => e.Listed && e.Files.Contains(full, StringComparer.OrdinalIgnoreCase));
        selectKey = listed?.Key ?? "file:" + full;
        await ReloadProjectsAsync();
    }

    private async Task SelectAsync(ComposeEntry? entry)
    {
        if (switching) return;
        if (entry == null) { detail.Content = noSelection; await panel.ShowAsync(null); return; }
        switching = true;
        try
        {
            var file = entry.LocalFiles.FirstOrDefault();
            if (file != null && !string.Equals(file, source.LoadedPath, StringComparison.OrdinalIgnoreCase))
            {
                if (!await source.LoadAsync(file))
                {
                    // The user kept the unsaved draft; return the selection to the project that owns it.
                    var owner = projects.Entries.FirstOrDefault(e => e.Files.Contains(source.LoadedPath, StringComparer.OrdinalIgnoreCase));
                    if (owner != null) projects.Select(owner.Key);
                    return;
                }
            }
            else if (file == null)
            {
                if (source.IsDirty && !await Host.Overlay.ConfirmAsync("Discard unsaved Compose edits?", $"{source.LoadedPath} has unsaved changes. The selected project has no local file, so the editor will be cleared.", "Discard", true))
                {
                    var owner = projects.Entries.FirstOrDefault(e => e.Files.Contains(source.LoadedPath, StringComparer.OrdinalIgnoreCase));
                    if (owner != null) projects.Select(owner.Key);
                    return;
                }
                source.Clear();
            }
            if (detail.Content == noSelection) detail.Content = null;
            Reflow();
            if (detail.Content == null) detail.Content = panel;
            await panel.ShowAsync(entry);
            if (file != null)
            {
                var (valid, message) = await panel.ValidateAsync();
                if (!source.IsDirty) source.SetValidation(valid, message);
            }
        }
        finally { switching = false; }
    }

    public override IEnumerable<AppCommand> PageCommands() =>
    [
        new AppCommand { Id = "compose.open", Title = "Open Compose file…", Group = "Compose", Icon = Icons.FolderOpen, Keywords = "yaml docker-compose", Run = () => { Host.Navigate("Compose"); projects.Browse(); } },
        new AppCommand { Id = "compose.refresh", Title = "Refresh Compose projects", Group = "Compose", Icon = Icons.Refresh, Keywords = "compose ls", Run = () => { Host.Navigate("Compose"); _ = SafeRefresh(); } },
    ];
}
