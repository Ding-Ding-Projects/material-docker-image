using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// The right-hand column of the image detail (design/workspace-redesign-2026-11/project/ImagesV2.dc.html): a Platforms
/// card and a Docker Scout summary card. Platforms come from docker image inspect and, for pulled images, from the
/// registry manifest read with docker buildx imagetools inspect --raw. The Scout card only shows an analysis that ran in
/// this session, and its base image recommendation runs docker scout recommendations on request; nothing is estimated.
/// </summary>
internal sealed class ImageSummaryColumn : StackPanel
{
    private static AppHost Host => AppHost.Current;
    private readonly ImageDetailContext ctx;
    private readonly StackPanel platformList = new();
    private readonly TextBlock platformKind = Ui.Text("", 12, null, "OnSurfaceVariant", false, false);
    private readonly TextBlock analysed = Ui.Text("", 12, null, "OnSurfaceVariant", false, false);
    private readonly TextBlock summary = Ui.Text("", 13, null, "OnSurfaceVariant", true, false);
    private readonly Grid severity = new();
    private readonly Button analyze;
    private readonly Action viewFindings;

    /// <summary>Raised with the number of runnable platforms once the registry manifest has been read.</summary>
    public event Action<int>? PlatformsCounted;

    public ImageSummaryColumn(ImageDetailContext context, Action viewFindings)
    {
        ctx = context;
        this.viewFindings = viewFindings;
        platformKind.VerticalAlignment = VerticalAlignment.Center;
        analysed.VerticalAlignment = VerticalAlignment.Center;
        var platformsCard = Ui.Card(Ui.VStack(10,
            Ui.Columns(8, [0, 1, 0], Ui.Icon(Icons.DeveloperBoard, 20, false, "Primary"), Ui.Text("Platforms", 15, FontWeights.SemiBold, "OnSurface", false), platformKind),
            platformList), 16, "SurfaceLow", 16);
        System.Windows.Automation.AutomationProperties.SetName(platformsCard, Loc.T("Platforms"));

        for (var i = 0; i < 4; i++) severity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        analyze = Ui.AsyncButton("Analyze", Icons.Refresh, ButtonKind.Tonal, AnalyzeAsync);
        var findings = Ui.Button("View findings", null, ButtonKind.Outlined, viewFindings);
        findings.HorizontalAlignment = HorizontalAlignment.Stretch;
        analyze.HorizontalAlignment = HorizontalAlignment.Stretch;
        var buttons = Ui.Columns(8, [1, 1], findings, analyze);
        var scoutCard = Ui.Card(Ui.VStack(12,
            Ui.Columns(8, [0, 1, 0], Ui.Icon(Icons.Security, 20, false, "Primary"), Ui.Text("Scout summary", 15, FontWeights.SemiBold, "OnSurface", false), analysed),
            severity, summary, new BaseImageRecommendationPanel(context), buttons), 16, "SurfaceLow", 16);
        System.Windows.Automation.AutomationProperties.SetName(scoutCard, Loc.T("Scout summary"));

        Children.Add(platformsCard);
        Children.Add(scoutCard.Margin(0, 12, 0, 0));

        ShowLocalPlatform();
        ShowScout(ScoutSessionCache.Get(ctx.Context, ctx.Image.Id));
        void OnUpdated(ScoutScanResult r) { if (r.ImageId == ctx.Image.Id && r.Context == ctx.Context) ShowScout(r); }
        Loaded += (_, _) => ScoutSessionCache.Updated += OnUpdated;
        Unloaded += (_, _) => ScoutSessionCache.Updated -= OnUpdated;
    }

    /// <summary>Reads the registry manifest when the image was pulled (it has a repository digest).</summary>
    public async Task LoadAsync()
    {
        if (ctx.RepoDigests.FirstOrDefault() is not { } reference) return;
        if (PageKit.ValidateReference(reference) != null) return;
        platformKind.Text = Loc.T("Reading the registry…");
        CommandResult result;
        try { result = await Host.Read(["buildx", "imagetools", "inspect", "--raw", reference], ctx.Context); }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or IOException or System.ComponentModel.Win32Exception)
        {
            platformKind.Text = Loc.T("Registry not read");
            platformKind.ToolTip = e.Message;
            return;
        }
        if (!result.Success)
        {
            platformKind.Text = Loc.T("Registry not read");
            platformKind.ToolTip = (result.StandardError + "\n" + result.StandardOutput).Trim();
            return;
        }
        ImageManifestReport report;
        try { report = ImageManifest.Parse(result.StandardOutput); }
        catch (FormatException e) { platformKind.Text = Loc.T("Registry not read"); platformKind.ToolTip = e.Message; return; }
        if (!report.IsIndex)
        {
            platformKind.Text = Loc.T("Single manifest");
            PlatformsCounted?.Invoke(1);
            return;
        }
        platformKind.Text = Loc.T(report.KindText);
        platformList.Children.Clear();
        foreach (var p in report.Platforms.Take(6))
        {
            var local = Matches(p.Platform, ctx.Platform);
            platformList.Children.Add(Entry(p.Platform, p.Digest, local ? "This machine" : "Needs emulation", local));
        }
        if (report.Platforms.Count > 6) platformList.Children.Add(Ui.Text(Format("{0} more in the Platforms tab", report.Platforms.Count - 6), 12, null, "OnSurfaceVariant", true, false));
        PlatformsCounted?.Invoke(report.Platforms.Count);
    }

    private void ShowLocalPlatform()
    {
        platformList.Children.Clear();
        var digest = ctx.RepoDigests.FirstOrDefault() is { } d && d.Contains('@') ? d[(d.IndexOf('@') + 1)..] : ctx.Image.Id;
        platformList.Children.Add(Entry(ctx.Platform, digest, "This machine", true));
        platformKind.Text = ctx.RepoDigests.Count == 0 ? Loc.T("Local only") : "";
    }

    private static bool Matches(string remote, string local) =>
        local.Length > 1 && (remote == local || remote.StartsWith(local + "/", StringComparison.Ordinal) || remote.StartsWith(local + " (", StringComparison.Ordinal));

    private static UIElement Entry(string platform, string digest, string chip, bool local)
    {
        var name = Ui.Text(platform, 14, FontWeights.SemiBold, "OnSurface", false, false);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var badge = Ui.Badge(chip, local ? Tone.Success : Tone.Neutral);
        if (!local) badge.ToolTip = Loc.T("This engine runs a different architecture, so running this platform needs emulation such as QEMU.");
        var mono = Ui.Mono(ShortDigest(digest), 12, "OnSurfaceVariant");
        mono.ToolTip = digest;
        var card = Ui.Card(Ui.VStack(4, Ui.Columns(8, [1, 0], name, badge), mono), 10, "SurfaceContainer", 12);
        card.Padding = new Thickness(12, 10, 12, 10);
        card.Margin = new Thickness(0, 0, 0, 8);
        card.ContextMenu = new ContextMenu();
        card.ContextMenu.Items.Add(Ui.MenuItem("Copy digest", Icons.ContentCopy, () => PageKit.Copy(digest, "the digest")));
        return card;
    }

    /// <summary>Translates a template such as "{0} with a fix available" and fills in the numbers.</summary>
    private static string Format(string template, params object[] values) =>
        string.Format(CultureInfo.CurrentCulture, Loc.T(template), values);

    /// <summary>sha256:2c41e8b07f9a…5d03 style, so a digest fits one line.</summary>
    internal static string ShortDigest(string digest)
    {
        var colon = digest.IndexOf(':');
        var algorithm = colon > 0 ? digest[..(colon + 1)] : "";
        var hex = colon > 0 ? digest[(colon + 1)..] : digest;
        return hex.Length > 20 ? algorithm + hex[..12] + "…" + hex[^4..] : digest;
    }

    private void ShowScout(ScoutScanResult? result)
    {
        severity.Children.Clear();
        var counts = result is { Succeeded: true } ? result.Counts : null;
        var tiles = new (string Label, int Count, string Bg, string Fg)[]
        {
            ("Critical", counts?.Critical ?? 0, "ErrorContainer", "OnErrorContainer"),
            ("High", counts?.High ?? 0, "WarningContainer", "OnWarningContainer"),
            ("Medium", counts?.Medium ?? 0, "TertiaryContainer", "OnTertiaryContainer"),
            ("Low", counts?.Low ?? 0, "SecondaryContainer", "OnSecondaryContainer"),
        };
        for (var i = 0; i < tiles.Length; i++)
        {
            var t = tiles[i];
            var number = Ui.Text(counts == null ? "—" : t.Count.ToString("N0", CultureInfo.CurrentCulture), 22, FontWeights.Bold, t.Fg, false, false);
            var label = Ui.Text(t.Label, 12, FontWeights.SemiBold, t.Fg, false);
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            var tile = Ui.Card(Ui.VStack(2, number, label), 10, counts == null ? "SurfaceContainer" : t.Bg, 12);
            tile.Margin = new Thickness(i == 0 ? 0 : 4, 0, i == tiles.Length - 1 ? 0 : 4, 0);
            System.Windows.Automation.AutomationProperties.SetName(tile, Loc.T(t.Label) + " " + number.Text);
            severity.Children.Add(tile.At(0, i));
        }
        if (result == null)
        {
            analysed.Text = Loc.T("Not analyzed");
            summary.Text = Loc.T("This image has not been analyzed with Docker Scout in this session.");
            SetLabel(analyze, "Analyze");
        }
        else if (!result.Succeeded)
        {
            analysed.Text = Loc.T("Analysis failed");
            summary.Text = Loc.T(ScoutStatus.Explain(result.State));
            SetLabel(analyze, "Try again");
        }
        else
        {
            var c = result.Counts;
            var packages = result.Findings.Select(f => f.Package).Where(p => p.Length > 0).Distinct(StringComparer.Ordinal).Count();
            analysed.Text = Loc.T("analyzed") + " " + result.AnalysedUtc.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);
            summary.Text = c.Total == 0
                ? Loc.T("No vulnerabilities found")
                : Format("{0} vulnerabilities in {1} packages", c.Total, packages) + " · " + Format("{0} with a fix available", c.Fixable);
            if (c.Unspecified > 0) summary.Text += " · " + Format("{0} without a severity rating", c.Unspecified);
            SetLabel(analyze, "Analyze again");
        }
    }

    /// <summary>Changes the label of a button made by Ui.Button while keeping its icon.</summary>
    private static void SetLabel(Button button, string label)
    {
        if (button.Content is DockPanel { } panel && panel.Children.OfType<TextBlock>().LastOrDefault() is { } text) text.Text = Loc.T(label);
        System.Windows.Automation.AutomationProperties.SetName(button, Loc.T(label));
    }

    private async Task AnalyzeAsync()
    {
        if (!ctx.Page.EnsureCurrent()) return;
        var availability = await ScoutRunner.CheckAsync(ctx.Context);
        if (!availability.Available)
        {
            analysed.Text = Loc.T("Not installed");
            summary.Text = Loc.T("Docker Scout is not installed.") + " " + Loc.T("Open the Vulnerabilities tab for the install guide.");
            return;
        }
        var result = await ScoutRunner.ScanForegroundAsync(ctx.Image.Reference, ctx.Image.Id, ctx.Context);
        if (result == null || result.State == ScoutState.Cancelled) return;
        ScoutSessionCache.Put(result);
        if (!result.Succeeded) viewFindings();
    }
}
