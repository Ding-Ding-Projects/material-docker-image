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
    private FrameworkElement? severityStrip;
    private readonly SecurityStatTile criticalTile = new("Critical", Icons.GppBad, "ErrorContainer", "OnErrorContainer");
    private readonly SecurityStatTile highTile = new("High", Icons.Warning, "Error", "OnError");
    private readonly SecurityStatTile mediumTile = new("Medium", Icons.Report, "WarningContainer", "OnWarningContainer");
    private readonly SecurityStatTile lowTile = new("Low", Icons.Info, "TertiaryContainer", "OnTertiaryContainer");
    private readonly SecurityStatTile fixableTile = new("Fixable", Icons.CheckCircle, "SuccessContainer", "OnSuccessContainer");
    private readonly ContentControl signInRow = new();
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
        table.EmptyTitle = Loc.T("No tagged local images");
        table.EmptyMessage = Loc.T("Security analyzes tagged images on this engine. Pull or build an image first, then refresh.");
        table.Column("Image", r => r.Name, 220)
             .Column("Image ID", r => r.ShortId, 110, true)
             .ChipColumn("Critical", r => r.Count(ScoutSeverity.Critical), r => r.CountTone(ScoutSeverity.Critical), 84)
             .ChipColumn("High", r => r.Count(ScoutSeverity.High), r => r.CountTone(ScoutSeverity.High), 76)
             .ChipColumn("Medium", r => r.Count(ScoutSeverity.Medium), r => r.CountTone(ScoutSeverity.Medium), 84)
             .ChipColumn("Low", r => r.Count(ScoutSeverity.Low), r => r.CountTone(ScoutSeverity.Low), 70)
             .Column("Fixable", r => r.FixableText, 76)
             .ChipColumn("Analyzed", r => Loc.T(r.StatusText), r => r.StatusTone, 170)
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

        // A long state card (for example the full output when Docker Scout is missing) scrolls within a quarter of the
        // page so the list below always keeps its rows.
        var bannerScroll = new ScrollViewer { Content = banner, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        SizeChanged += (_, _) => bannerScroll.MaxHeight = Math.Max(120, ActualHeight * 0.25);
        severityStrip = SecurityStatTile.Strip(criticalTile, highTile, mediumTile, lowTile, fixableTile);
        var top = Ui.VStack(0, bannerScroll, severityStrip, progressCard);
        var listHost = new Grid();
        listHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        listHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        listHost.Children.Add(table.At(0));
        listHost.Children.Add(signInRow.At(1));
        var split = Ui.SplitInspector(listHost, panel, 1250, 480, summary: () => table.Focused is { } f ? $"{f.Reference} · {f.Size}" : null);
        table.FocusedChanged += _ => (split.Tag as Action)?.Invoke();
        var body = new Grid();
        SecurityStatTile.CompactBelow(body, 600, criticalTile, highTile, mediumTile, lowTile, fixableTile);
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
            banner.Content = Ui.Card(Ui.Columns(10, [0, 1, 0], Ui.Icon(Icons.Error, 20, true, "OnErrorContainer"), Ui.Text(Loc.T("Images could not be listed:") + " " + e.Message, 13, null, "OnErrorContainer", true, false), Ui.Button("Open Diagnostics", null, ButtonKind.Text, () => Host.Navigate("Diagnostics"))), 12, "ErrorContainer", 12).Margin(0, 0, 0, 12);
            subtitle.Text = Loc.T("Could not reach the engine on context") + " " + (context ?? "default") + ".";
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
        subtitle.Text = Loc.Line($"{baseRows.Count} tagged {(baseRows.Count == 1 ? "image" : "images")} · {analysed} analyzed this session · "
            + (availability.Available ? Loc.T("Docker Scout") + " " + (availability.Version.Length > 0 ? availability.Version : "available") : "Docker Scout unavailable")
            + $" · context {context ?? "default"} · refreshed {DateTime.Now:HH:mm:ss}");
    }

    private void ShowAvailability(ScoutAvailability availability)
    {
        // Without Docker Scout no severity can ever be known, so the tiles step aside for the explanation and the list.
        if (severityStrip != null) severityStrip.Visibility = availability.Available ? Visibility.Visible : Visibility.Collapsed;
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
        UpdateSignInRow(rows.Any(r => r.Result is { State: ScoutState.NotSignedIn }));
    }

    /// <summary>Info row under the list: shown only when Docker Scout really reported that it is not signed in.</summary>
    private void UpdateSignInRow(bool notSignedIn)
    {
        if (!notSignedIn) { signInRow.Content = null; return; }
        var message = Ui.Text("Docker Scout is not signed in. Local SBOMs still list packages, but vulnerability data needs a Docker account.", 13, null, "OnSurfaceVariant");
        var signIn = Ui.Button("Sign in", null, ButtonKind.Outlined, () => Host.Navigate("Registry"));
        signIn.Height = 32;
        signInRow.Content = Ui.Card(Ui.Columns(10, [-18, 1, 0], Ui.Icon(Icons.Info, 18, false, "OnSurfaceVariant"), message, signIn), 10, "SurfaceContainer", 12).Margin(12, 8, 12, 12);
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
        fixableTile.Set(N(total.Fixable), results.Count == 0 ? "no images analyzed yet" : $"vulnerabilities with a fixed version, across {results.Count} analyzed");
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
