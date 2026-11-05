using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Right-hand panel of Security: the selected image's findings, quickview and a CVE detail card.</summary>
internal sealed class SecurityImagePanel : Border
{
    private static AppHost Host => AppHost.Current;
    private readonly SecurityPage page;
    private readonly FrameworkElement empty;
    private readonly StackPanel body = new();
    private readonly ContentControl header = new();
    private readonly ContentControl state = new();
    private readonly ScoutFindingsView findings = new();
    private readonly SearchableText quickview;
    private readonly Expander quickviewExpander;
    private readonly Button scan;
    private SecurityImageRow? current;
    private readonly ScrollViewer scroller;

    public SecurityImagePanel(SecurityPage page)
    {
        this.page = page;
        CornerRadius = new CornerRadius(16);
        Padding = new Thickness(16);
        SetResourceReference(BackgroundProperty, "SurfaceLow");
        System.Windows.Automation.AutomationProperties.SetName(this, Loc.T("Selected image findings"));

        empty = Ui.EmptyState(Icons.Policy, "No image selected", "Select an image to see its vulnerabilities, filter them by severity and open advisories.");
        scan = Ui.AsyncButton("Scan this image", Icons.Security, ButtonKind.Filled, () => current == null ? Task.CompletedTask : page.ScanAsync([current]));
        var actions = Ui.Wrap(8, scan,
            Ui.AsyncButton("Run quickview", Icons.ManageSearch, ButtonKind.Tonal, QuickviewAsync),
            Ui.Button("Copy reference", Icons.ContentCopy, ButtonKind.Text, () => { if (current != null) PageKit.Copy(current.Reference, "the image reference"); }));
        actions.Margin = new Thickness(0, 10, 0, 10);

        quickview = new SearchableText("Docker Scout quickview", "quickview.txt", "Search quickview") { Height = 220 };
        quickview.Search.Width = 200;
        quickviewExpander = new Expander { Header = new TextBlock { Text = Loc.T("Quickview summary (docker scout quickview)"), TextWrapping = TextWrapping.Wrap }, Content = quickview, Margin = new Thickness(0, 0, 0, 8) };

        // Fixed table height and an outer scroll keep every part reachable when the panel is stacked below the list.
        findings.Height = 600;
        foreach (var child in new UIElement[] { header, actions, state, quickviewExpander, findings }) body.Children.Add(child);
        scroller = Ui.Scroll(body);

        var grid = new Grid();
        grid.Children.Add(empty);
        grid.Children.Add(scroller);
        Child = grid;
        Show(null);
    }

    public void Show(SecurityImageRow? row)
    {
        var changedImage = row?.Id != current?.Id;
        current = row;
        empty.Visibility = row == null ? Visibility.Visible : Visibility.Collapsed;
        scroller.Visibility = row == null ? Visibility.Collapsed : Visibility.Visible;
        if (row == null) return;
        if (changedImage)
        {
            quickview.SetText("");
            quickview.FileName = PageKit.SafeFileName(row.Reference) + "-quickview.txt";
            quickviewExpander.IsExpanded = false;
        }
        var name = Ui.Text(row.Name, 18, FontWeights.SemiBold, "OnSurface", true, false);
        header.Content = Ui.VStack(4,
            Ui.Overline("Image"), name, Ui.Mono(row.Id, 11.5, "OnSurfaceVariant", true),
            Ui.Wrap(6, Ui.Badge(row.StatusText, row.StatusTone, true), Ui.Caption(row.Size + " · created " + row.CreatedSince)));
        scan.ToolTip = Loc.T(row.Result == null ? "Runs docker scout cves for this image." : "Runs docker scout cves again and replaces the cached result.");
        if (row.Result is { Succeeded: false } failed)
        {
            state.Content = ScoutSeverityUi.StateCard(failed.State, failed.Output, "docker scout cves --format sarif --output <temporary file> " + failed.Reference,
                failed.State == ScoutState.NotSignedIn ? Ui.Button("Open Registry sign-in", Icons.Login, ButtonKind.Text, () => Host.Navigate("Registry")) : ScoutSeverityUi.InstallGuideButton());
            findings.Show([], false);
        }
        else if (row.Result is { } ok)
        {
            state.Content = null;
            findings.FoundIn = string.Join(", ", new[] { row.Reference }.Concat(row.OtherTags));
            findings.Show(ok.Findings, true);
        }
        else
        {
            state.Content = Ui.Caption("Not analyzed in this session. Scan it to list vulnerabilities; results stay in memory until the app closes.").Margin(0, 0, 0, 8);
            findings.Show([], false);
        }
    }

    private async Task QuickviewAsync()
    {
        if (current is not { } row) return;
        if (!page.EnsureCurrentForAction()) return;
        var availability = await ScoutRunner.CheckAsync(page.Context);
        quickviewExpander.IsExpanded = true;
        if (!availability.Available) { quickview.SetText(availability.Output.Length > 0 ? availability.Output : "Docker Scout is not installed."); return; }
        quickview.SetText(Loc.T("Running docker scout quickview…"));
        var result = await ScoutRunner.QuickviewAsync(row.Reference, page.Context);
        if (current?.Id != row.Id) return;
        var text = ScoutReport.StripAnsi((result.StandardOutput + (result.StandardError.Trim().Length > 0 ? "\n" + result.StandardError : "")).Trim());
        quickview.SetText(result.Success ? text : $"docker scout quickview exited with code {result.ExitCode}.\n{text}");
    }
}
