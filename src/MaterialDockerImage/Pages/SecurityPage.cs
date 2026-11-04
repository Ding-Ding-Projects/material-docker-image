using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Security: Docker Scout vulnerability analysis of tagged local images. Scans run one image at a time, can be canceled,
/// and keep their results in memory for this session. Scanning and export live in Security/SecurityPage.Scan.cs.
/// </summary>
public sealed partial class SecurityPage : PageBase
{
    public override string Key => "Security";

    private readonly DataTable<SecurityImageRow> table = new("Analyzed images", "Filter images");
    private readonly SecurityImagePanel panel;
    private readonly TextBlock subtitle;
    private readonly ContentControl banner = new();
    private readonly ImageStatTile criticalTile = new("Critical", Icons.GppBad, Tone.Error);
    private readonly ImageStatTile highTile = new("High", Icons.Warning, Tone.Warning);
    private readonly ImageStatTile mediumTile = new("Medium", Icons.Report, Tone.Tertiary);
    private readonly ImageStatTile lowTile = new("Low", Icons.Info, Tone.Neutral);
    private List<SecurityImageRow> baseRows = [];
    private bool loaded;
    private bool reloading;

    internal string? Context => SnapshotContext;

    public SecurityPage()
    {
        panel = new SecurityImagePanel(this);
        scanAll = Ui.AsyncButton("Scan all images", Icons.Security, ButtonKind.Filled, ScanAllAsync);
        scanSelected = Ui.AsyncButton("Scan selected", Icons.ManageSearch, ButtonKind.Tonal, ScanSelectedAsync);
        var header = Ui.Header("Security", "Docker Scout vulnerability analysis of local images", out subtitle,
            Ui.AsyncIconButton(Icons.Refresh, "Refresh", SafeRefresh),
            Ui.Button("Export findings", Icons.FileExport, ButtonKind.Outlined, ExportFindings),
            scanSelected,
            scanAll);

        table.Key = r => r.Id;
        table.EmptyTitle = "No tagged local images";
        table.EmptyMessage = "Security analyzes tagged images on this engine. Pull or build an image first, then refresh.";
        table.Column("Image", r => r.Name, 220)
             .Column("Image ID", r => r.ShortId, 110, true)
             .ChipColumn("Critical", r => r.Count(ScoutSeverity.Critical), r => r.CountTone(ScoutSeverity.Critical), 84)
             .ChipColumn("High", r => r.Count(ScoutSeverity.High), r => r.CountTone(ScoutSeverity.High), 76)
             .ChipColumn("Medium", r => r.Count(ScoutSeverity.Medium), r => r.CountTone(ScoutSeverity.Medium), 84)
             .ChipColumn("Low", r => r.Count(ScoutSeverity.Low), r => r.CountTone(ScoutSeverity.Low), 70)
             .Column("Fixable", r => r.FixableText, 76)
             .ChipColumn("Analyzed", r => r.StatusText, r => r.StatusTone, 170)
             .Filter("Critical or high", r => r.Counts is { } c && c.Critical + c.High > 0)
             .Filter("Fixable only", r => r.Counts is { Fixable: > 0 })
             .Filter("Not analyzed", r => r.Result == null)
             .Filter("Failed", r => r.Failed)
             .BulkAction("Scan selected", Icons.Security, rows => ScanAsync(rows))
             .RowAction("Scan", Icons.Security, r => ScanAsync([r]), "Enter")
             .RowAction("Open Images", Icons.Layers, _ => { Host.Navigate("Images"); return Task.CompletedTask; })
             .RowAction("Copy reference", Icons.ContentCopy, r => { PageKit.Copy(r.Reference, "the image reference"); return Task.CompletedTask; })
             .RowAction("Copy image ID", Icons.ContentCopy, r => { PageKit.Copy(r.Id, "the image ID"); return Task.CompletedTask; });
        table.FocusedChanged += r => { if (!reloading) panel.Show(r); };
        table.Activated += r => _ = Guarded(() => ScanAsync([r]));

        var top = Ui.VStack(0, banner, ImageStatTile.Strip(criticalTile, highTile, mediumTile, lowTile), progressCard);
        var split = Ui.SplitInspector(table, panel, 1250, 480, summary: () => table.Focused is { } f ? $"{f.Reference} · {f.Size}" : null);
        table.FocusedChanged += _ => (split.Tag as Action)?.Invoke();
        var body = new Grid();
        ImageStatTile.CompactBelow(body, 600, criticalTile, highTile, mediumTile, lowTile);
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(top);
        body.Children.Add(split.At(1));
        Layout(header, body);
        BuildProgressCard();
        UpdateTiles();

        ScoutSessionCache.Updated += OnResultUpdated;
    }

    public override async Task RefreshAsync()
    {
        var context = SnapshotContext;
        subtitle.Text = Loc.T("Loading images from context ") + (context ?? "default") + "…";
        IReadOnlyList<System.Text.Json.JsonElement> images;
        try { images = await Host.ReadRows(["image", "ls", "--format", "{{json .}}"], context); }
        catch (Exception e) when (e is InvalidOperationException or System.Text.Json.JsonException)
        {
            banner.Content = Ui.Card(Ui.Columns(10, [0, 1, 0], Ui.Icon(Icons.Error, 20, true, "OnErrorContainer"), Ui.Text("Images could not be listed: " + e.Message, 13, null, "OnErrorContainer", true, false), Ui.Button("Open Diagnostics", null, ButtonKind.Text, () => Host.Navigate("Diagnostics"))), 12, "ErrorContainer", 12).Margin(0, 0, 0, 12);
            subtitle.Text = "Could not reach the engine on context " + (context ?? "default") + ".";
            baseRows = [];
            Apply();
            return;
        }
        baseRows = images.Select(ImageRow.From).Where(i => i.Tagged)
            .GroupBy(i => i.Id, StringComparer.Ordinal)
            .Select(g =>
            {
                var refs = g.Select(i => i.Reference).Distinct(StringComparer.Ordinal).OrderBy(r => r, StringComparer.Ordinal).ToList();
                var first = g.First();
                return new SecurityImageRow(refs[0], refs.Skip(1).ToList(), first.Id, first.Size, first.CreatedSince);
            })
            .OrderBy(r => r.Reference, StringComparer.Ordinal)
            .ToList();
        loaded = true;
        Apply();

        var availability = await ScoutRunner.CheckAsync(context, true);
        ShowAvailability(availability);
        var analysed = baseRows.Count(r => ScoutSessionCache.Get(context, r.Id) is { Succeeded: true });
        subtitle.Text = $"{baseRows.Count} tagged {(baseRows.Count == 1 ? "image" : "images")} · {analysed} analyzed this session · "
            + (availability.Available ? "Docker Scout " + (availability.Version.Length > 0 ? availability.Version : "available") : "Docker Scout unavailable")
            + $" · context {context ?? "default"} · refreshed {DateTime.Now:HH:mm:ss}";
    }

    private void ShowAvailability(ScoutAvailability availability)
    {
        if (availability.Available) { banner.Content = null; return; }
        banner.Content = ScoutSeverityUi.StateCard(ScoutState.PluginMissing, availability.Output.Length > 0 ? availability.Output : $"(exit code {availability.ExitCode}, no output)", availability.Command,
            ScoutSeverityUi.InstallGuideButton(),
            Ui.AsyncButton("Check again", Icons.Refresh, ButtonKind.Text, async () => ShowAvailability(await ScoutRunner.CheckAsync(SnapshotContext, true))));
    }

    /// <summary>Joins the loaded images with the session cache and refreshes the table, tiles and panel.</summary>
    private void Apply()
    {
        var context = SnapshotContext;
        var rows = baseRows.Select(r => r with { Result = ScoutSessionCache.Get(context, r.Id) }).ToList();
        reloading = true;
        try { table.SetItems(rows); }
        finally { reloading = false; }
        panel.Show(table.Focused);
        UpdateTiles();
    }

    private void OnResultUpdated(ScoutScanResult result)
    {
        if (result.Context != SnapshotContext || baseRows.All(r => r.Id != result.ImageId)) return;
        Dispatcher.BeginInvoke(Apply);
    }

    private void UpdateTiles()
    {
        var results = baseRows.Select(r => ScoutSessionCache.Get(SnapshotContext, r.Id)).Where(r => r is { Succeeded: true }).Select(r => r!).ToList();
        var total = results.Aggregate(ScoutSeverityCounts.Empty, (sum, r) => sum.Add(r.Counts));
        string Detail(ScoutSeverity s)
        {
            if (results.Count == 0) return "no images analyzed yet";
            var images = results.Count(r => r.Counts.Of(s) > 0);
            return $"in {images} of {results.Count} analyzed";
        }
        string N(int n) => results.Count == 0 ? "—" : n.ToString("N0", CultureInfo.CurrentCulture);
        criticalTile.Set(N(total.Critical), Detail(ScoutSeverity.Critical));
        highTile.Set(N(total.High), Detail(ScoutSeverity.High));
        mediumTile.Set(N(total.Medium), Detail(ScoutSeverity.Medium));
        lowTile.Set(N(total.Low), total.Unspecified > 0 ? $"{total.Unspecified} more unrated" : Detail(ScoutSeverity.Low));
    }

    internal bool EnsureCurrentForAction()
    {
        if (IsCurrent) return true;
        Host.Notify.Error(PageKit.StaleMessage(SnapshotContext));
        return false;
    }

    public override IEnumerable<AppCommand> PageCommands() =>
    [
        new AppCommand { Id = "security.scanAll", Title = "Scan all images with Docker Scout", Group = "Security", Icon = Icons.Security, Keywords = "vulnerabilities cve scout analyze", Run = () => { Host.Navigate("Security"); _ = Guarded(ScanAllAsync); } },
        new AppCommand { Id = "security.scanSelected", Title = "Scan selected image with Docker Scout", Group = "Security", Icon = Icons.ManageSearch, Keywords = "vulnerabilities cve scout analyze", Run = () => { Host.Navigate("Security"); _ = Guarded(ScanSelectedAsync); } },
        new AppCommand { Id = "security.export", Title = "Export security findings…", Group = "Security", Icon = Icons.FileExport, Keywords = "cve json csv report", Run = () => Ui.Guard(ExportFindings) },
        new AppCommand { Id = "security.cancel", Title = "Cancel security scan", Group = "Security", Icon = Icons.Cancel, Keywords = "stop scout", CanRun = () => scanCancellation != null, Run = CancelScan },
        new AppCommand { Id = "security.refresh", Title = "Refresh security", Group = "Security", Icon = Icons.Refresh, Run = () => _ = SafeRefresh() },
    ];

    private static async Task Guarded(Func<Task> action)
    {
        try { await action(); }
        catch (Exception e) when (e is not OutOfMemoryException) { Host.Notify.Error(e.Message); }
    }
}
