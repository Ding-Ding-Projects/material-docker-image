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
    private bool reloading;

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
        table.EmptyTitle = "No local images";
        table.EmptyMessage = "This engine has no images yet. Pull one from a registry, load an archive, or build from a Dockerfile.";
        table.Column("Repository and tag", i => i.Name, 260)
             .Column("Image ID", i => i.ShortId, 120, true)
             .Column("Size", i => i.Size, 90)
             .Column("Created", i => i.CreatedSince, 130)
             .ChipColumn("Containers", i => i.Usage, i => i.Running > 0 ? Tone.Success : i.Total > 0 ? Tone.Primary : Tone.Neutral, 140)
             .Filter("Tagged", i => i.Tagged)
             .Filter("Dangling", i => i.Dangling)
             .Filter("Unused", i => i.Total == 0)
             .Filter("In use", i => i.Total > 0)
             .BulkAction("Export archives", Icons.Download, SaveAsync)
             .BulkAction("Push selected", Icons.CloudUpload, PushAsync)
             .BulkAction("Remove selected", Icons.Delete, RemoveManyAsync, true)
             .RowAction("Run…", Icons.PlayArrow, i => { OpenRun(i); return Task.CompletedTask; }, "Enter")
             .RowAction("Tag…", Icons.Sell, TagAsync)
             .RowAction("Push to registry…", Icons.CloudUpload, i => PushAsync([i]))
             .RowAction("Save archive…", Icons.Download, i => SaveAsync([i]))
             .RowAction("Copy image ID", Icons.ContentCopy, i => { PageKit.Copy(i.Id, "the image ID"); return Task.CompletedTask; })
             .RowAction("Remove…", Icons.Delete, RemoveOneAsync, "Del", true);
        table.FocusedChanged += i => { if (!reloading) inspector.Show(i); };
        table.Activated += OpenRun;

        var prune = Ui.AsyncButton("Prune dangling", Icons.CleaningServices, ButtonKind.Text, PruneAsync);
        prune.HorizontalAlignment = HorizontalAlignment.Right;
        var left = new Grid();
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.Children.Add(errorHost);
        left.Children.Add(table.At(1));
        left.Children.Add(prune.At(2));

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 360 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.Children.Add(left);
        body.Children.Add(inspector.At(0, 1));
        body.SizeChanged += (_, e) => inspector.Width = e.NewSize.Width < 1000 ? 320 : 400;
        Layout(header, body);
    }

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
            errorHost.Content = Ui.Card(Ui.Columns(10, [0, 1, 0], Ui.Icon(Icons.Error, 20, true, "OnErrorContainer"), Ui.Text("Images could not be listed: " + e.Message, 13, null, "OnErrorContainer", true, false), Ui.Button("Open Diagnostics", null, ButtonKind.Text, () => Host.Navigate("Diagnostics"))), 12, "ErrorContainer", 12).Margin(0, 0, 0, 8);
            subtitle.Text = "Could not reach the engine on context " + (context ?? "default") + ".";
            table.SetItems([]);
            return;
        }
        errorHost.Content = null;
        var used = containers.Select(c => (Image: DockerJson.Text(c, "Image"), State: DockerJson.Text(c, "State"))).ToList();
        var rows = images.Select(ImageRow.From).Select(i =>
        {
            var mine = used.Where(c => i.UsedBy(c.Image)).ToList();
            return i with { Total = mine.Count, Running = mine.Count(c => c.State == "running") };
        }).ToList();
        reloading = true;
        try { table.SetItems(rows); }
        finally { reloading = false; }
        inspector.Show(table.Focused);
        var unique = rows.GroupBy(r => r.Id).Select(g => g.First()).ToList();
        var total = unique.Sum(r => ImageRow.ParseSize(r.Size));
        subtitle.Text = $"{rows.Count} {(rows.Count == 1 ? "image" : "images")} · {unique.Count} unique IDs · {Ui.FormatBytes(total)} · context {context ?? "default"} · refreshed {DateTime.Now:HH:mm:ss}";
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
        new AppCommand { Id = "images.refresh", Title = "Refresh images", Group = "Images", Icon = Icons.Refresh, Run = () => _ = SafeRefresh() },
    ];

    private static async Task Guarded(Func<Task> action)
    {
        try { await action(); }
        catch (Exception e) when (e is not OutOfMemoryException) { Host.Notify.Error(e.Message); }
    }

    private bool EnsureCurrent()
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
