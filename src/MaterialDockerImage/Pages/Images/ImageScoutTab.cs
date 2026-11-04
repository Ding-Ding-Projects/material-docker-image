using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Vulnerabilities tab: Docker Scout analysis of the image (docker scout cves --format sarif) with severity tiles,
/// findings and a quickview summary. Results are shared with the Security destination for this session.
/// </summary>
internal sealed class ImageScoutTab : ImageDetailTab
{
    private readonly StackPanel layout = new();
    private readonly ScrollViewer view;
    private readonly ContentControl status = new();
    private readonly Action<ScoutSeverityCounts?, string> setTiles;
    private readonly UIElement tiles;
    private readonly ScoutFindingsView findings = new();
    private readonly Button analyze;
    private readonly SearchableText quickview;
    private readonly Expander quickviewExpander;
    private ScoutScanResult? rendered;

    public ImageScoutTab(ImageDetailContext context) : base(context)
    {
        (tiles, setTiles) = ScoutSeverityUi.Tiles();
        analyze = Ui.AsyncButton("Analyze with Docker Scout", Icons.Security, ButtonKind.Filled, AnalyzeAsync);
        var openSecurity = Ui.Button("Open Security", Icons.Policy, ButtonKind.Text, () => Host.Navigate("Security"));
        var actions = Ui.Wrap(8, analyze, openSecurity);
        actions.Margin = new Thickness(0, 0, 0, 8);

        quickview = new SearchableText("Docker Scout quickview", PageKit.SafeFileName(context.Image.Reference) + "-quickview.txt", "Search quickview") { Height = 220 };
        quickview.Search.Width = 200;
        quickview.SetText("");
        var runQuickview = Ui.AsyncButton("Run quickview", Icons.ManageSearch, ButtonKind.Tonal, QuickviewAsync);
        quickviewExpander = new Expander
        {
            Header = new TextBlock { Text = Loc.T("Quickview summary (docker scout quickview)"), TextWrapping = TextWrapping.Wrap },
            Content = Ui.VStack(8, Ui.Caption("Shows the command's own summary text verbatim, including base image recommendations when Scout has them."), runQuickview, quickview),
            Margin = new Thickness(0, 0, 0, 8)
        };

        // The findings table has a fixed height so the whole tab can scroll at small window sizes.
        findings.Height = 640;
        foreach (var child in new UIElement[] { actions, status, tiles, quickviewExpander, findings }) layout.Children.Add(child);
        view = Ui.Scroll(layout);

        void OnUpdated(ScoutScanResult r) { if (r.ImageId == Ctx.Image.Id && r.Context == Ctx.Context) Render(r); }
        view.Loaded += (_, _) =>
        {
            ScoutSessionCache.Updated += OnUpdated;
            // A scan from Security may have finished while this tab was hidden.
            if (rendered != null && ScoutSessionCache.Get(Ctx.Context, Ctx.Image.Id) is { } latest && !ReferenceEquals(latest, rendered)) Render(latest);
        };
        view.Unloaded += (_, _) => ScoutSessionCache.Updated -= OnUpdated;
    }

    public override string Title => "Vulnerabilities";
    public override FrameworkElement View => view;

    private string Reference => Ctx.Image.Reference;

    protected override async Task OnActivateAsync()
    {
        if (ScoutSessionCache.Get(Ctx.Context, Ctx.Image.Id) is { } cached) { Render(cached); return; }
        status.Content = Loading("Checking for Docker Scout…");
        var availability = await ScoutRunner.CheckAsync(Ctx.Context);
        if (!availability.Available) { ShowMissing(availability); return; }
        status.Content = Note($"Docker Scout {availability.Version} is available. This image has not been analyzed in this session. Analysis runs docker scout cves, which indexes the image packages and matches them against Docker's vulnerability data; it can take a minute.");
        findings.Show([], false);
    }

    private void ShowMissing(ScoutAvailability availability)
    {
        status.Content = ScoutSeverityUi.StateCard(ScoutState.PluginMissing, availability.Output, availability.Command,
            ScoutSeverityUi.InstallGuideButton(),
            Ui.AsyncButton("Check again", Icons.Refresh, ButtonKind.Text, async () =>
            {
                var again = await ScoutRunner.CheckAsync(Ctx.Context, true);
                if (again.Available) { status.Content = Note($"Docker Scout {again.Version} is available."); } else ShowMissing(again);
            }));
        setTiles(null, "");
        findings.Show([], false);
    }

    private async Task AnalyzeAsync()
    {
        if (!EnsureCurrent()) return;
        var availability = await ScoutRunner.CheckAsync(Ctx.Context);
        if (!availability.Available) { ShowMissing(availability); return; }
        var result = await ScoutRunner.ScanForegroundAsync(Reference, Ctx.Image.Id, Ctx.Context);
        if (result == null) return;
        if (result.State == ScoutState.Cancelled) { status.Content = ScoutSeverityUi.StateCard(ScoutState.Cancelled, result.Output); return; }
        ScoutSessionCache.Put(result);
        Render(result);
    }

    private void Render(ScoutScanResult result)
    {
        rendered = result;
        if (!result.Succeeded)
        {
            status.Content = ScoutSeverityUi.StateCard(result.State, result.Output, "docker scout cves --format sarif --output <temporary file> " + result.Reference,
                result.State == ScoutState.NotSignedIn ? Ui.Button("Open Registry sign-in", Icons.Login, ButtonKind.Text, () => Host.Navigate("Registry")) : ScoutSeverityUi.InstallGuideButton());
            setTiles(null, "");
            findings.Show([], false);
            return;
        }
        var c = result.Counts;
        status.Content = Ui.Card(Ui.Columns(10, [0, 1], Ui.Icon(c.Total == 0 ? Icons.GppGood : Icons.Security, 20, true, c.Critical + c.High > 0 ? "Error" : "Success"),
            Ui.VStack(2,
                Ui.Text($"{c.Total} {(c.Total == 1 ? "finding" : "findings")} · {c.Fixable} fixable", 14, FontWeights.SemiBold, "OnSurface", true, false),
                Ui.Caption(Loc.T("Analyzed") + " " + result.AnalysedText + " with docker scout cves. Results are kept for this session only."))), 12, "SurfaceContainer", 12).Margin(0, 0, 0, 10);
        setTiles(c, "");
        analyze.ToolTip = Loc.T("Analyze again to refresh the findings.");
        findings.Show(result.Findings, true);
    }

    private async Task QuickviewAsync()
    {
        if (!EnsureCurrent()) return;
        var availability = await ScoutRunner.CheckAsync(Ctx.Context);
        if (!availability.Available) { quickview.SetText(availability.Output.Length > 0 ? availability.Output : "Docker Scout is not installed."); return; }
        quickview.SetText(Loc.T("Running docker scout quickview…"));
        var result = await ScoutRunner.QuickviewAsync(Reference, Ctx.Context);
        var text = ScoutReport.StripAnsi((result.StandardOutput + (result.StandardError.Trim().Length > 0 ? "\n" + result.StandardError : "")).Trim());
        quickview.SetText(result.Success ? text : $"docker scout quickview exited with code {result.ExitCode}.\n{text}");
        quickviewExpander.IsExpanded = true;
    }

    private static UIElement Note(string text) =>
        Ui.Card(Ui.Columns(10, [0, 1], Ui.Icon(Icons.Info, 18, true, "Primary"), Ui.Text(text, 13, null, "OnSurfaceVariant")), 12, "SurfaceContainer", 12).Margin(0, 0, 0, 10);
}
