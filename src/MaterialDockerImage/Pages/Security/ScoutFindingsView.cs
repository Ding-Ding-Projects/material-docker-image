using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Severity colours and badges shared by the Security and Images destinations.</summary>
internal static class ScoutSeverityUi
{
    public static Tone ToneOf(ScoutSeverity severity) => severity switch
    {
        ScoutSeverity.Critical => Tone.Error,
        ScoutSeverity.High => Tone.Warning,
        ScoutSeverity.Medium => Tone.Tertiary,
        _ => Tone.Neutral
    };

    /// <summary>Four tiles: Critical, High, Medium and Low.</summary>
    public static (UIElement Strip, Action<ScoutSeverityCounts?, string> Set) Tiles()
    {
        var critical = new ImageStatTile("Critical", Icons.GppBad, Tone.Error);
        var high = new ImageStatTile("High", Icons.Warning, Tone.Warning);
        var medium = new ImageStatTile("Medium", Icons.Report, Tone.Tertiary);
        var low = new ImageStatTile("Low", Icons.Info, Tone.Neutral);
        void Set(ScoutSeverityCounts? counts, string detail)
        {
            string N(int n) => counts == null ? "—" : n.ToString("N0", CultureInfo.CurrentCulture);
            critical.Set(N(counts?.Critical ?? 0), detail);
            high.Set(N(counts?.High ?? 0), detail);
            medium.Set(N(counts?.Medium ?? 0), detail);
            low.Set(N(counts?.Low ?? 0), counts is { Unspecified: > 0 } c ? $"{c.Unspecified} unrated" : detail);
        }
        Set(null, "");
        return (ImageStatTile.Strip(critical, high, medium, low), Set);
    }

    /// <summary>An honest status card for a Scout state other than Ready, with the exact command output.</summary>
    public static UIElement StateCard(ScoutState state, string output, string? command = null, params UIElement[] actions)
    {
        var (bg, fg) = state == ScoutState.Failed ? ("ErrorContainer", "OnErrorContainer") : ("WarningContainer", "OnWarningContainer");
        var title = state switch
        {
            ScoutState.PluginMissing => "Docker Scout is not installed",
            ScoutState.NotSignedIn => "Sign in to use Docker Scout",
            ScoutState.Cancelled => "Analysis canceled",
            _ => "Docker Scout analysis failed"
        };
        var stack = Ui.VStack(8,
            Ui.HStack(10, Ui.Icon(state == ScoutState.Failed ? Icons.Error : Icons.Warning, 20, true, fg), Ui.Text(title, 15, FontWeights.SemiBold, fg)),
            Ui.Text(ScoutStatus.Explain(state), 13, null, fg));
        if (command != null) stack.Children.Add(Ui.Mono("$ " + command, 12, fg, true));
        if (output.Trim().Length > 0)
        {
            var box = Ui.CodeBox(output.Trim(), 40);
            box.IsReadOnly = true;
            box.MaxHeight = 160;
            System.Windows.Automation.AutomationProperties.SetName(box, Loc.T("Exact command output"));
            stack.Children.Add(box);
        }
        if (actions.Length > 0) stack.Children.Add(Ui.Wrap(8, actions));
        return Ui.Card(stack, 14, bg, 14).Margin(0, 0, 0, 10);
    }

    /// <summary>A button that opens the Docker Scout installation guide after confirmation.</summary>
    public static Button InstallGuideButton() =>
        Ui.AsyncButton("Docker Scout install guide", Icons.OpenInNew, ButtonKind.Text, () => ScoutRunner.OpenAdvisoryAsync("https://docs.docker.com/scout/install/"));
}

/// <summary>Findings list with severity filter, search (with regex builder), fixable filter, export and a detail card.</summary>
internal sealed class ScoutFindingsView : Grid
{
    private readonly DataTable<ScoutFinding> table = new("Vulnerability findings", "Search CVE, package or version");
    private readonly ContentControl detail = new();
    private IReadOnlyList<ScoutFinding> all = [];
    private string severity = "All";

    public ScoutFindingsView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 200 });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var severities = Ui.Segmented(["All", "Critical", "High", "Medium", "Low"], "All", s => { severity = s; Apply(); });
        severities.MinWidth = 300;
        var filterRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        filterRow.Children.Add(severities);
        filterRow.Children.Add(Ui.InfoButton("Severity filter",
            "Shows findings of one severity as reported by Docker Scout (CVSS v3 rating when available). Findings without a rating appear under All only. Fixable only keeps findings where Scout names a fixed version.",
            "High → only findings Scout rates HIGH"));
        Children.Add(filterRow);

        table.Key = f => f.Id + "|" + f.Purl + "|" + f.Package + "|" + f.InstalledVersion;
        table.EmptyTitle = Loc.T("No findings");
        table.EmptyMessage = Loc.T("Analyze the image with Docker Scout to list its vulnerabilities.");
        table.ChipColumn("Severity", f => f.SeverityText, f => ScoutSeverityUi.ToneOf(f.Severity), 100)
             .Column("ID", f => f.Id, 150, true)
             .Column("Package", f => f.Package, 150)
             .Column("Installed", f => f.InstalledVersion, 120, true)
             .Column("Fixed in", f => f.Fixable ? f.FixedVersion : "No fix", 120, true)
             .Filter("Fixable only", f => f.Fixable)
             .RowAction("Open advisory", Icons.OpenInNew, f => ScoutRunner.OpenAdvisoryAsync(f.Url), "Enter")
             .RowAction("Copy ID", Icons.ContentCopy, f => { PageKit.Copy(f.Id, "the vulnerability ID"); return Task.CompletedTask; })
             .RowAction("Copy package URL", Icons.Link, f => { PageKit.Copy(f.Purl.Length > 0 ? f.Purl : f.Package, "the package URL"); return Task.CompletedTask; });
        table.FocusedChanged += ShowDetail;
        table.Activated += f => _ = ScoutRunner.OpenAdvisoryAsync(f.Url);
        Children.Add(table.At(1));

        var detailScroll = Ui.Scroll(detail);
        detailScroll.MaxHeight = 460;
        detailScroll.Margin = new Thickness(0, 10, 0, 0);
        Children.Add(detailScroll.At(2));
        ShowDetail(default);
    }

    /// <summary>Shows a completed analysis. An empty list from a successful analysis is shown as such.</summary>
    public void Show(IReadOnlyList<ScoutFinding> findings, bool analysed)
    {
        all = findings;
        table.EmptyTitle = analysed ? "No vulnerabilities reported" : "No findings";
        table.EmptyMessage = analysed
            ? "Docker Scout reported no known vulnerabilities for the packages it detected in this image."
            : "Analyze the image with Docker Scout to list its vulnerabilities.";
        Apply();
    }

    public IReadOnlyList<ScoutFinding> Visible => table.Visible;

    private void Apply()
    {
        table.SetItems(severity == "All" ? all : all.Where(f => f.SeverityText == severity));
        ShowDetail(table.Focused);
    }

    private void ShowDetail(ScoutFinding? f)
    {
        if (f == null)
        {
            detail.Content = all.Count == 0 ? null : Ui.Card(Ui.Caption("Select a finding to see its package, fixed version, description and advisory link."), 12, "SurfaceContainer", 12);
            return;
        }
        detail.Content = Detail(f);
    }

    /// <summary>The image reference(s) that carry the findings, shown as "Found in" in the detail card.</summary>
    public string FoundIn { get; set; } = "";

    private UIElement Detail(ScoutFinding f)
    {
        var tone = ScoutSeverityUi.ToneOf(f.Severity);
        var pillText = Loc.T(f.SeverityText) + (f.Score is { } s ? " · " + Loc.T("CVSS") + " " + s.ToString("0.0", CultureInfo.InvariantCulture) : "");
        var open = Ui.AsyncIconButton(Icons.OpenInNew, "Open advisory in browser", () => ScoutRunner.OpenAdvisoryAsync(f.Url));
        open.IsEnabled = f.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        if (!open.IsEnabled) open.ToolTip = Loc.T("The report has no https advisory link for this finding.");
        var top = Ui.Columns(8, [0, 1, 0], Ui.Badge(pillText, tone, false), new Border(), open);
        var id = Ui.Mono(f.Id, 20, "OnSurface", true);
        id.FontWeight = FontWeights.SemiBold;

        // Key-value grid: Package, Installed, Upgrade (fixed version in green), Found in.
        var upgrade = Ui.Wrap(6,
            Ui.Mono(f.InstalledVersion.Length > 0 ? f.InstalledVersion : "—", 12.5, "OnSurface", true),
            Ui.Icon(Icons.ChevronRight, 16, false, "OnSurfaceVariant"),
            f.Fixable ? Ui.Mono(f.FixedVersion + " (" + Loc.T("fixed") + ")", 12.5, "Success", true) : Ui.Text("no fixed version reported", 12.5, null, "OnSurfaceVariant"));
        var grid = Ui.VStack(10,
            PageKit.Fact("Package", f.Purl.Length > 0 ? f.Purl : f.Package, true),
            PageKit.Fact("Installed", f.InstalledVersion, true),
            Ui.Columns(12, [-120, 1], Ui.Text("Upgrade", 13, null, "OnSurfaceVariant"), upgrade),
            FoundIn.Length > 0 ? PageKit.Fact("Found in", FoundIn) : null,
            f.AffectedRange.Length > 0 ? PageKit.Fact("Affected range", f.AffectedRange, true) : null);

        var description = Ui.VStack(6, Ui.Overline("Description"),
            f.Description.Length > 0 ? Ui.Text(f.Description, 13, null, "OnSurface", true, false) : Ui.Caption("The report has no description for this finding."));

        UIElement remediation;
        if (f.Fixable)
        {
            var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "OnSurfaceVariant");
            void Mono(string t) { var r = new System.Windows.Documents.Run(t) { FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas") }; r.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "OnSurface"); tb.Inlines.Add(r); }
            tb.Inlines.Add(Loc.T("Rebuild with a base image that ships") + " "); Mono((f.Package.Length > 0 ? f.Package : "the package") + " " + f.FixedVersion);
            tb.Inlines.Add(" " + Loc.T("or upgrade the package in the final stage of the Dockerfile."));
            remediation = Ui.Card(Ui.Columns(10, [-20, 1], Ui.Icon(Icons.CheckCircle, 20, true, "Success"), tb), 12, "SurfaceContainer", 12);
        }
        else remediation = Ui.Card(Ui.Columns(10, [-20, 1], Ui.Icon(Icons.Info, 20, false, "OnSurfaceVariant"), Ui.Text("No fix is published for this finding yet. Track the advisory and rebuild when the base image ships one.", 13, null, "OnSurfaceVariant")), 12, "SurfaceContainer", 12);

        var buttons = Ui.Columns(8, [1, 1],
            Ui.Button("Copy CVE ID", null, ButtonKind.Outlined, () => PageKit.Copy(f.Id, "the vulnerability ID")),
            Ui.Button("Rebuild image", Icons.Construction, ButtonKind.Filled, () => AppHost.Current.Navigate("Build")));
        foreach (var b in buttons.Children.OfType<Button>()) b.Height = 40;
        return Ui.VStack(14, top, id, grid, description, remediation, buttons.Margin(0, 6, 0, 0));
    }
}
