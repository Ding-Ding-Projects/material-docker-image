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
        detailScroll.MaxHeight = 300;
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

    private static UIElement Detail(ScoutFinding f)
    {
        var top = Ui.Wrap(8, Ui.Badge(f.SeverityText, ScoutSeverityUi.ToneOf(f.Severity), true),
            f.Score is { } s ? Ui.Badge(Loc.T("CVSS") + " " + s.ToString("0.0", CultureInfo.InvariantCulture), Tone.Neutral) : null,
            f.Fixable ? Ui.Badge("Fix available", Tone.Success, true) : Ui.Badge("No fix yet", Tone.Neutral, true));
        var id = Ui.Mono(f.Id, 16, "OnSurface", true);
        id.FontWeight = FontWeights.SemiBold;
        var package = Ui.Wrap(8, Ui.Text(f.Package.Length > 0 ? f.Package : "Unknown package", 14, FontWeights.SemiBold, "OnSurface", true, false),
            f.PackageType.Length > 0 ? Ui.Badge(f.PackageType, Tone.Primary) : null);
        var versions = Ui.Wrap(8,
            Ui.Mono(f.InstalledVersion.Length > 0 ? f.InstalledVersion : "version not reported", 13, "OnSurface", true),
            Ui.Icon(Icons.ArrowForward, 16, false, "OnSurfaceVariant"),
            Ui.Mono(f.Fixable ? f.FixedVersion : "no fixed version reported", 13, f.Fixable ? "Success" : "OnSurfaceVariant", true));
        var open = Ui.AsyncButton("Open advisory", Icons.OpenInNew, ButtonKind.Tonal, () => ScoutRunner.OpenAdvisoryAsync(f.Url));
        open.IsEnabled = f.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        if (!open.IsEnabled) open.ToolTip = Loc.T("The report has no https advisory link for this finding.");
        var stack = Ui.VStack(8,
            top, id, package, versions,
            f.AffectedRange.Length > 0 ? PageKit.Fact("Affected range", f.AffectedRange, true) : null,
            f.Purl.Length > 0 ? PageKit.Fact("Package URL", f.Purl, true) : null,
            f.Description.Length > 0 ? Ui.Text(f.Description, 13, null, "OnSurfaceVariant", true, false) : Ui.Caption("The report has no description for this finding."),
            Ui.Wrap(8, open, Ui.Button("Copy ID", Icons.ContentCopy, ButtonKind.Text, () => PageKit.Copy(f.Id, "the vulnerability ID"))));
        return Ui.Card(stack, 14, "SurfaceContainer", 14);
    }
}
