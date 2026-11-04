using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Container lifecycle with a tabbed inspector. Operations live in Containers/ContainersPage.Actions.cs.</summary>
public sealed partial class ContainersPage : PageBase
{
    public override string Key => "Containers";

    private readonly DataTable<ContainerRow> table = new("Containers", "Filter by name, image or port");
    private readonly ContainerInspector inspector;
    private readonly TextBlock subtitle;
    private readonly ContentControl errorHost = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly CheckBox autoRefresh;
    private bool refreshing;
    // A request that arrives mid-refresh (for example an engine context switch) runs once the current one ends.
    private bool refreshAgain;
    public override bool Busy => refreshing || refreshAgain;
    private bool reloading;

    /// <summary>The engine context the current list was loaded from.</summary>
    internal string? Context => SnapshotContext;

    public ContainersPage()
    {
        inspector = new ContainerInspector(this);
        autoRefresh = Ui.Switch("Auto-refresh", false, on => { if (on && !Host.IsCapture) timer.Start(); else timer.Stop(); });
        autoRefresh.ToolTip = Loc.T("Reloads the list every 5 seconds while this page is visible.");
        if (Host.IsCapture) autoRefresh.IsEnabled = false;
        timer.Tick += async (_, _) => { if (IsVisible && !refreshing && !Host.Overlay.IsOpen) await SafeRefresh(); };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) { timer.Stop(); inspector.Stop(); }
            else
            {
                if (autoRefresh.IsChecked == true && !Host.IsCapture) timer.Start();
                inspector.Resume();
            }
        };

        var header = Ui.Header("Containers", "Container lifecycle, logs, stats, exec and files", out subtitle,
            autoRefresh,
            Ui.AsyncIconButton(Icons.Refresh, "Refresh", SafeRefresh),
            Ui.AsyncButton("Run container", Icons.PlayArrow, ButtonKind.Filled, RunContainerAsync));

        table.Key = c => c.Id;
        table.EmptyTitle = Loc.T("No containers");
        table.EmptyMessage = Loc.T("This engine has no containers. Run one from a local image with Run container, or from Images.");
        table.ChipColumn("State", c => c.StateLabel, c => c.StateTone, 110)
             .Column("Name", c => c.Name, 180)
             .Column("Image", c => c.Image, 200)
             .Column("Ports", c => c.PortsText, 200, true)
             .Column("Status", c => c.Status, 160)
             .Column("Created", c => c.RunningFor, 130)
             .Filter("Running", c => c.IsRunning || c.IsRestarting)
             .Filter("Stopped", c => c.IsStopped)
             .Filter("Paused", c => c.IsPaused)
             .BulkAction("Start", Icons.PlayArrow, rows => BulkAsync("start", rows))
             .BulkAction("Stop", Icons.Stop, rows => BulkAsync("stop", rows))
             .BulkAction("Restart", Icons.RestartAlt, rows => BulkAsync("restart", rows))
             .BulkAction("Remove", Icons.Delete, RemoveManyAsync, true)
             .RowAction("Start", Icons.PlayArrow, c => LifecycleAsync("start", c))
             .RowAction("Stop", Icons.Stop, c => LifecycleAsync("stop", c))
             .RowAction("Restart", Icons.RestartAlt, c => LifecycleAsync("restart", c))
             .RowAction("Pause or unpause", Icons.Pause, c => LifecycleAsync(c.IsPaused ? "unpause" : "pause", c))
             .RowAction("Kill…", Icons.Bolt, KillAsync, null, true)
             .RowAction("Rename…", Icons.DriveFileRenameOutline, RenameAsync)
             .RowAction("Commit to image…", Icons.Commit, CommitAsync)
             .RowAction("Export filesystem…", Icons.FileExport, ExportAsync)
             .RowAction("Copy ID", Icons.ContentCopy, c => { PageKit.Copy(c.Id, "the container ID"); return Task.CompletedTask; })
             .RowAction("Remove…", Icons.Delete, RemoveOneAsync, "Del", true);
        table.FocusedChanged += c => { if (!reloading) inspector.Show(c); };

        var left = new Grid();
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        left.Children.Add(errorHost);
        left.Children.Add(table.At(1));

        var body = Ui.SplitInspector(left, inspector, 1100, 420, summary: () => table.Focused is { } f ? $"{f.Name} · {f.Image} · {f.Status}" : null);
        table.FocusedChanged += _ => (body.Tag as Action)?.Invoke();
        Layout(header, body);
    }

    public override async Task RefreshAsync()
    {
        if (refreshing) { refreshAgain = true; return; }
        refreshing = true;
        var context = SnapshotContext;
        try
        {
            IReadOnlyList<System.Text.Json.JsonElement> rows;
            try { rows = await Host.ReadRows(["container", "ls", "--all", "--no-trunc", "--format", "{{json .}}"], context); }
            catch (Exception e) when (e is InvalidOperationException or System.Text.Json.JsonException)
            {
                errorHost.Content = Ui.Card(Ui.Columns(10, [0, 1, 0], Ui.Icon(Icons.Error, 20, true, "OnErrorContainer"), Ui.Text(Loc.T("Containers could not be listed:") + " " + e.Message, 13, null, "OnErrorContainer", true, false), Ui.Button("Open Diagnostics", null, ButtonKind.Text, () => Host.Navigate("Diagnostics"))), 12, "ErrorContainer", 12).Margin(0, 0, 0, 8);
                subtitle.Text = Loc.T("Could not reach the engine on context") + " " + (context ?? "default") + ".";
                table.SetItems([]);
                return;
            }
            if (context != SnapshotContext) { refreshAgain = true; return; }
            errorHost.Content = null;
            var list = rows.Select(ContainerRow.From).ToList();
            // Selection is restored by key; the inspector sees only the final focus, so an open log stream survives a refresh.
            reloading = true;
            try { table.SetItems(list); }
            finally { reloading = false; }
            inspector.Show(table.Focused);
            var running = list.Count(c => c.IsRunning);
            var paused = list.Count(c => c.IsPaused);
            subtitle.Text = Loc.Line($"{list.Count} {(list.Count == 1 ? "container" : "containers")} · {running} running · {paused} paused · context {context ?? "default"} · refreshed {DateTime.Now:HH:mm:ss}");
        }
        finally
        {
            refreshing = false;
            if (refreshAgain) { refreshAgain = false; _ = SafeRefresh(); }
        }
    }

    public override IEnumerable<AppCommand> PageCommands() =>
    [
        new AppCommand { Id = "containers.run", Title = "Run container…", Group = "Containers", Icon = Icons.PlayArrow, Keywords = "create start new", Run = () => _ = Guarded(RunContainerAsync) },
        new AppCommand { Id = "containers.refresh", Title = "Refresh containers", Group = "Containers", Icon = Icons.Refresh, Run = () => _ = SafeRefresh() },
        new AppCommand { Id = "containers.prune", Title = "Remove all stopped containers…", Group = "Containers", Icon = Icons.CleaningServices, Keywords = "prune cleanup", Run = () => _ = Guarded(PruneAsync) },
    ];

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
}
