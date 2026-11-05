using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Local image inventory: pull, load, import, tag, push, save, remove and run. Operations live in ImagesPage.Actions.cs.</summary>
public sealed partial class ImagesPage : PageBase
{
    public override string Key => "Images";
    internal const string RunHandOffKey = "Images.RunImage";

    private readonly DataTable<ImageRow> table = new("Images", "Filter images");
    private readonly ImageInspector inspector;
    private readonly TextBlock subtitle;
    private readonly ContentControl errorHost = new();
    private readonly Grid listView;
    private ImageDetailView? detail;
    private bool reloading;
    private readonly ImageStatTile totalTile = new("Images", Icons.Layers);
    private readonly ImageStatTile sizeTile = new("Total size", Icons.Storage);
    private readonly ImageStatTile danglingTile = new("Dangling", Icons.LayersClear);
    private readonly ImageStatTile unusedTile = new("Unused", Icons.Inventory2);

    /// <summary>Every image row from the last refresh (for pickers such as Compare).</summary>
    internal IReadOnlyList<ImageRow> AllImages => table.Items;

    /// <summary>The engine context the current list was loaded from.</summary>
    internal string? Context => SnapshotContext;

    public ImagesPage()
    {
        inspector = new ImageInspector(this);
        var header = Ui.Header("Images", "Local image inventory", out subtitle,
            Ui.AsyncIconButton(Icons.Refresh, "Refresh", SafeRefresh),
            Ui.Button("Build", Icons.Construction, ButtonKind.Text, () => Host.Navigate("Build")),
            Ui.AsyncButton("Import tarball", Icons.Input, ButtonKind.Outlined, ImportAsync),
            Ui.AsyncButton("Load archive", Icons.Upload, ButtonKind.Outlined, LoadArchiveAsync),
            Ui.AsyncButton("Pull image", Icons.CloudDownload, ButtonKind.Filled, PullAsync));

        table.Key = i => i.Key;
        table.EmptyTitle = Loc.T("No local images");
        table.EmptyMessage = Loc.T("This engine has no images yet. Pull one from a registry, load an archive, or build from a Dockerfile.");
        table.Column("Repository and tag", i => i.Name, 260)
             .Column("Image ID", i => i.ShortId, 120, true)
             .Column("Platform", i => i.Platform.Length > 0 ? i.Platform : "—", 110)
             .Column("Size", i => i.Size, 90)
             .Column("Created", i => i.CreatedSince, 130)
             .ChipColumn("Containers", i => i.Usage, i => i.Running > 0 ? Tone.Success : Tone.Neutral, 140)
             .Filter("Tagged", i => i.Tagged)
             .Filter("Dangling", i => i.Dangling)
             .Filter("Unused", i => i.Total == 0)
             .Filter("In use", i => i.Total > 0)
             .BulkAction("Export archives", Icons.Download, SaveAsync)
             .BulkAction("Push selected", Icons.CloudUpload, PushAsync)
             .BulkAction("Remove selected", Icons.Delete, RemoveManyAsync, true)
             .RowAction("Run…", Icons.PlayArrow, i => { OpenRun(i); return Task.CompletedTask; }, "Enter")
             .RowAction("Open details", Icons.OpenInFull, i => { OpenDetail(i); return Task.CompletedTask; })
             .RowAction("Tag…", Icons.Sell, TagAsync)
             .RowAction("Retag to multiple…", Icons.Tag, RetagManyAsync)
             .RowAction("Push to registry…", Icons.CloudUpload, i => PushAsync([i]))
             .RowAction("Push all tags of repository…", Icons.CloudSync, PushRepositoryAsync)
             .RowAction("Save archive…", Icons.Download, i => SaveAsync([i]))
             .RowAction("Copy pull command", Icons.ContentCopy, i => { CopyPullCommand(i); return Task.CompletedTask; })
             .RowAction("Copy image ID", Icons.ContentCopy, i => { PageKit.Copy(i.Id, "the image ID"); return Task.CompletedTask; })
             .RowAction("Remove…", Icons.Delete, RemoveOneAsync, "Del", true);
        table.FocusedChanged += i => { if (!reloading) inspector.Show(i); };
        table.Activated += OpenRun;

        // Prune sits in the filter row so it never costs the list a row of its own.
        table.ToolbarAction(Ui.AsyncButton("Prune dangling", Icons.CleaningServices, ButtonKind.Text, PruneAsync));
        var left = new Grid();
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        left.Children.Add(errorHost);
        var strip = ImageStatTile.Strip(totalTile, sizeTile, danglingTile, unusedTile);
        left.Children.Add(strip.At(1));
        // On short windows the tiles take one line each so the list keeps its rows.
        ImageStatTile.CompactBelow(left, 460, totalTile, sizeTile, danglingTile, unusedTile);
        left.Children.Add(table.At(2));

        // The detail needs room for its tabs: beside the list from 1300 px, otherwise stacked below it.
        // On short windows it folds into a one-line summary with Show details (design/polish-2026-11/ImagesCompact.dc.html).
        // One card holds the title, the tiles, the filters and the table, as in the design; the inspector is its own 340px card beside it.
        var listCard = new DockPanel { LastChildFill = true };
        header.Margin = new Thickness(4, 0, 0, 12);
        listCard.Children.Add(header.Dock(System.Windows.Controls.Dock.Top));
        listCard.Children.Add(left);
        var listSurface = Ui.Card(listCard, 16, "SurfaceLow", 16);
        var body = Ui.SplitInspector(listSurface, inspector, 1300, 340, summary: () => table.Focused is { } f ? $"{f.Name} · {f.ShortId} · {f.Size}" : null);
        table.FocusedChanged += _ => (body.Tag as Action)?.Invoke();
        Children.Clear(); RowDefinitions.Clear();
        listView = body;
        Children.Add(body);
    }

    /// <summary>Replaces the list with the full-page detail of one image (design/workspace-redesign-2026-11/project/ImagesV2.dc.html).</summary>
    /// <summary>Capture mode only: opens the full detail of the first listed image, if there is one.</summary>
    internal bool CaptureDetail() { if (table.Items.FirstOrDefault() is not { } first) return false; OpenDetail(first); return true; }

    /// <summary>Capture mode only: the list's search field, whose builder is captured open.</summary>
    internal SearchFilter CaptureSearch => table.Search;

    internal void OpenDetail(ImageRow image)
    {
        var view = new ImageDetailView(this, image, CloseDetail) { Focusable = true, FocusVisualStyle = null };
        detail = view;
        Children.Clear();
        Children.Add(view);
        view.Loaded += (_, _) => { if (detail == view && !view.IsKeyboardFocusWithin) view.Focus(); };
    }

    /// <summary>Returns from the image detail to the list, keeping the list's selection, filters and scroll position.</summary>
    internal void CloseDetail()
    {
        if (detail == null) return;
        detail = null;
        Children.Clear();
        Children.Add(listView);
        Dispatcher.BeginInvoke(() => table.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    // Count and total size of the last successful list, for the command palette; null until a list has loaded.
    private (int Count, double Bytes)? loadedSummary;
    internal override string? PaletteSummary() => loadedSummary is { } s ? PaletteLiveLines.Images(s.Count, Ui.FormatBytes(s.Bytes)) : null;
    private string? SelectedImageLine() => table.Focused is { } i ? PaletteLiveLines.SelectedImage(i.Name, i.Size) : null;

    public override async Task RefreshAsync()
    {
        var context = SnapshotContext;
        subtitle.Text = Loc.T("Loading images from context ") + (context ?? "default") + "…";
        IReadOnlyList<System.Text.Json.JsonElement> images, containers;
        try
        {
            images = await Host.ReadRows(["image", "ls", "--all", "--format", "{{json .}}"], context);
            containers = await Host.ReadRows(["container", "ls", "--all", "--format", "{{json .}}"], context);
        }
        catch (Exception e) when (e is InvalidOperationException or System.Text.Json.JsonException)
        {
            errorHost.Content = Ui.Card(Ui.Columns(10, [0, 1, 0], Ui.Icon(Icons.Error, 20, true, "OnErrorContainer"), Ui.Text(Loc.T("Images could not be listed:") + " " + e.Message, 13, null, "OnErrorContainer", true, false), Ui.Button("Open Diagnostics", null, ButtonKind.Text, () => Host.Navigate("Diagnostics"))), 12, "ErrorContainer", 12).Margin(0, 0, 0, 8);
            subtitle.Text = Loc.T("Could not reach the engine on context") + " " + (context ?? "default") + ".";
            table.SetItems([]);
            UpdateStats([]);
            loadedSummary = null;
            return;
        }
        errorHost.Content = null;
        var used = containers.Select(c => (Image: DockerJson.Text(c, "Image"), State: DockerJson.Text(c, "State"))).ToList();
        var rows = images.Select(ImageRow.From).Select(i =>
        {
            var mine = used.Where(c => i.UsedBy(c.Image)).ToList();
            return i with { Total = mine.Count, Running = mine.Count(c => c.State == "running") };
        }).ToList();
        rows = await WithPlatformsAsync(rows, context);
        reloading = true;
        try { table.SetItems(rows); }
        finally { reloading = false; }
        inspector.Show(table.Focused);
        var unique = rows.GroupBy(r => r.Id).Select(g => g.First()).ToList();
        var total = unique.Sum(r => ImageRow.ParseSize(r.Size));
        loadedSummary = (rows.Count, total);
        subtitle.Text = Loc.Line($"{rows.Count} {(rows.Count == 1 ? "image" : "images")} · {unique.Count} unique IDs · {Ui.FormatBytes(total)} · context {context ?? "default"} · refreshed {DateTime.Now:HH:mm:ss}");
        UpdateStats(rows);
    }

    /// <summary>Adds the os/architecture of every image with one docker image inspect call; a failed read leaves the column empty.</summary>
    private static async Task<List<ImageRow>> WithPlatformsAsync(List<ImageRow> rows, string? context)
    {
        var ids = rows.Select(r => r.Id).Where(id => id.Length > 0).Distinct().Take(300).ToList();
        if (ids.Count == 0) return rows;
        try
        {
            var args = new List<string> { "image", "inspect", "--format", "{{.Id}}|{{.Os}}/{{.Architecture}}{{if .Variant}}/{{.Variant}}{{end}}" };
            args.AddRange(ids);
            var result = await Host.Read(args, context);
            if (!result.Success) return rows;
            var map = new List<(string Id, string Platform)>();
            foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var bar = line.LastIndexOf('|');
                if (bar > 0) map.Add((line[..bar].Replace("sha256:", "", StringComparison.Ordinal), line[(bar + 1)..]));
            }
            // The list may show a short ID while inspect prints the full one, so match on the shared prefix.
            return rows.Select(r =>
            {
                var id = r.Id.Replace("sha256:", "", StringComparison.Ordinal);
                var hit = id.Length == 0 ? default : map.FirstOrDefault(m => m.Id.StartsWith(id, StringComparison.Ordinal) || id.StartsWith(m.Id, StringComparison.Ordinal));
                return hit.Platform is { Length: > 0 } p ? r with { Platform = p } : r;
            }).ToList();
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception) { return rows; }
    }

    private void UpdateStats(IReadOnlyList<ImageRow> rows)
    {
        var unique = rows.GroupBy(r => r.Id).Select(g => g.First()).ToList();
        var total = unique.Sum(r => ImageRow.ParseSize(r.Size));
        var dangling = unique.Where(r => r.Dangling).ToList();
        var unused = unique.Where(r => r.Total == 0).ToList();
        totalTile.Set(rows.Count.ToString("N0", CultureInfo.CurrentCulture), $"{unique.Count} unique IDs");
        sizeTile.Set(Ui.FormatBytes(total), "unique image data");
        danglingTile.Set(dangling.Count.ToString("N0", CultureInfo.CurrentCulture), dangling.Count == 0 ? "nothing to prune" : Ui.FormatBytes(dangling.Sum(r => ImageRow.ParseSize(r.Size))) + " untagged");
        unusedTile.Set(unused.Count.ToString("N0", CultureInfo.CurrentCulture), "no container uses them");
    }

    public override void OnShown()
    {
        if (Host.HandOff.Remove(RunHandOffKey, out var value) && value is string reference && reference.Length > 0)
        {
            Dispatcher.BeginInvoke(() => Ui.Guard(() =>
            {
                if (PageKit.ValidateReference(reference) is { } problem && !reference.StartsWith("sha256:", StringComparison.Ordinal)) { Host.Notify.Error($"Cannot run \"{reference}\": {problem}"); return; }
                RunEditor.Show(reference, SnapshotContext, SnapshotGeneration, SafeRefresh);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    public override IEnumerable<AppCommand> PageCommands() =>
    [
        new AppCommand { Id = "images.pull", Title = "Pull image…", Group = "Images", Icon = Icons.CloudDownload, Keywords = "download registry", Run = () => _ = Guarded(PullAsync) },
        new AppCommand { Id = "images.load", Title = "Load image archive…", Group = "Images", Icon = Icons.Upload, Keywords = "tar load", Run = () => _ = Guarded(LoadArchiveAsync) },
        new AppCommand { Id = "images.import", Title = "Import tarball as image…", Group = "Images", Icon = Icons.Input, Keywords = "filesystem import", Run = () => _ = Guarded(ImportAsync) },
        new AppCommand { Id = "images.prune", Title = "Prune dangling images…", Group = "Images", Icon = Icons.CleaningServices, Keywords = "cleanup remove untagged", Run = () => _ = Guarded(PruneAsync) },
        new AppCommand { Id = "images.retag", Title = "Retag selected image to multiple tags…", Group = "Images", Icon = Icons.Tag, Keywords = "tag many names", Run = () => _ = Guarded(() => table.Focused is { } i ? RetagManyAsync(i) : NoSelection()), Status = SelectedImageLine },
        new AppCommand { Id = "images.pushRepository", Title = "Push all tags of selected repository…", Group = "Images", Icon = Icons.CloudSync, Keywords = "upload all-tags registry", Run = () => _ = Guarded(() => table.Focused is { } i ? PushRepositoryAsync(i) : NoSelection()), Status = SelectedImageLine },
        new AppCommand { Id = "images.copyPull", Title = "Copy pull command of selected image", Group = "Images", Icon = Icons.ContentCopy, Keywords = "clipboard docker pull", Run = () => { if (table.Focused is { } i) CopyPullCommand(i); else _ = NoSelection(); }, Status = SelectedImageLine },
        new AppCommand { Id = "images.openDetail", Title = "Open details of selected image", Group = "Images", Icon = Icons.OpenInFull, Keywords = "layers platforms full view", Run = () => { if (table.Focused is { } i) OpenDetail(i); else _ = NoSelection(); }, Status = SelectedImageLine },
        new AppCommand { Id = "images.refresh", Title = "Refresh images", Group = "Images", Icon = Icons.Refresh, Run = () => _ = SafeRefresh(), Status = PaletteSummary },
    ];

    private static Task NoSelection()
    {
        Host.Notify.Info("Select an image in Images first.");
        return Task.CompletedTask;
    }

    private static async Task Guarded(Func<Task> action)
    {
        try { await action(); }
        catch (Exception e) when (e is not OutOfMemoryException) { Host.Notify.Error(e.Message); }
    }

    internal bool EnsureCurrent()
    {
        if (IsCurrent) return true;
        Host.Notify.Error(PageKit.StaleMessage(SnapshotContext));
        return false;
    }

    internal void OpenRun(ImageRow image)
    {
        if (!EnsureCurrent()) return;
        Ui.Guard(() => RunEditor.Show(image.Reference, SnapshotContext, SnapshotGeneration, SafeRefresh));
    }
}
