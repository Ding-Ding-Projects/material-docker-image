using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Offline tools: Base64 toolkit, embedded documentation browser and version information.</summary>
public sealed class ToolsPage : PageBase
{
    public override string Key => "Tools";
    private const string ReleasesUrl = "https://github.com/Ding-Ding-Projects/material-docker-image/releases";
    private readonly ContentControl host = new() { Focusable = false };
    private readonly Dictionary<string, Func<FrameworkElement>> factories;
    private readonly Dictionary<string, FrameworkElement> built = new();
    private Border tabs = new();

    public ToolsPage()
    {
        factories = new()
        {
            ["Base64"] = () => new Base64Panel().Root,
            ["Documentation"] = () => new DocsBrowser().Root,
            ["Changelog"] = Changelog,
        };
        tabs = Ui.Segmented(["Base64", "Documentation", "Changelog"], "Base64", Show);
        tabs.HorizontalAlignment = HorizontalAlignment.Left; tabs.MinWidth = 420;
        var header = Ui.Header("Tools", "Everything here works offline", out _, tabs);
        Layout(header, host);
        Show("Base64");
    }

    private void Show(string tab)
    {
        if (!built.TryGetValue(tab, out var view)) built[tab] = view = factories[tab]();
        host.Content = view;
    }

    private void Select(string tab)
    {
        var index = Array.IndexOf(["Base64", "Documentation", "Changelog"], tab);
        if (tabs.Child is Grid grid && index >= 0 && grid.Children[index] is RadioButton r) r.IsChecked = true;
        Show(tab);
    }

    private static FrameworkElement Changelog()
    {
        var (version, informational, built) = PageParts.AppInfo();
        var rows = Ui.VStack(10,
            Ui.Columns(16, [-180, 1], Ui.Overline("Version"), Ui.Mono(version, 14)),
            informational != null && informational != version ? Ui.Columns(16, [-180, 1], Ui.Overline("Informational version"), Ui.Mono(informational, 13, "OnSurface", true)) : null,
            Ui.Columns(16, [-180, 1], Ui.Overline("Build timestamp"), Ui.Mono(PageParts.BuildText(built), 14)),
            Ui.Columns(16, [-180, 1], Ui.Overline("Runtime"), Ui.Mono(".NET " + Environment.Version, 14)));
        var open = Ui.AsyncButton("Open releases page", Icons.OpenInNew, ButtonKind.Filled, async () =>
        {
            if (await AppHost.Current.Overlay.ConfirmAsync("Open in your browser?", Loc.T("This opens") + " " + ReleasesUrl + " in your default browser.", "Open browser"))
                PageParts.OpenExternal(ReleasesUrl);
        });
        open.HorizontalAlignment = HorizontalAlignment.Left;
        var body = Ui.VStack(16,
            Ui.Title("Changelog", 18),
            Ui.Caption("This build reads its version and build time from its own assembly metadata. It does not contact the internet or a Git repository."),
            rows,
            Ui.Divider(),
            Ui.Text("Release notes for each version are published on the project's releases page. They are not bundled in the app, so nothing is shown here that the releases page does not say."),
            Ui.Mono(ReleasesUrl, 13, "OnSurfaceVariant", true),
            open);
        return Ui.Scroll(Ui.Card(body, 24, "SurfaceLow", 16).Margin(0, 0, 8, 16));
    }

    public override IEnumerable<AppCommand> PageCommands() =>
    [
        new AppCommand { Id = "tools.base64", Title = "Base64 encode or decode", Group = "Tools", Icon = Icons.DataObject, Keywords = "base64 encode decode data url mime", Run = () => { Host.Navigate(Key); Select("Base64"); } },
        new AppCommand { Id = "tools.docs", Title = "Open offline documentation", Group = "Tools", Icon = Icons.MenuBook, Keywords = "help docs guide manual", Run = () => { Host.Navigate(Key); Select("Documentation"); } },
        new AppCommand { Id = "tools.changelog", Title = "Version and changelog", Group = "Tools", Icon = Icons.Info, Keywords = "version release notes build", Run = () => { Host.Navigate(Key); Select("Changelog"); } },
    ];
}
