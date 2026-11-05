using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// The compact Networks section shown under the volumes on the Storage page: a title with its count summary, one row per
/// network (name, driver, subnet, containers) and shortcuts to the create panel and the full Networks view.
/// </summary>
internal sealed class NetworksSection : Grid
{
    private readonly StackPanel rows = new();
    private readonly TextBlock summary = Ui.Caption("");
    public event Action? CreateRequested;
    public event Action? ManageRequested;

    public NetworksSection()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        summary.VerticalAlignment = VerticalAlignment.Center;
        summary.Margin = new Thickness(0, 0, 8, 0);
        var head = Ui.Columns(8, [0, 1, 0, 0],
            Ui.Text("Networks", 15, FontWeights.SemiBold),
            summary,
            Ui.Button("Create network", Icons.Add, ButtonKind.Text, () => CreateRequested?.Invoke()),
            Ui.Button("Manage networks", Icons.Lan, ButtonKind.Text, () => ManageRequested?.Invoke()));
        head.Margin = new Thickness(0, 0, 0, 4);
        Children.Add(head);
        Children.Add(Ui.Scroll(rows).At(1));
        System.Windows.Automation.AutomationProperties.SetName(rows, Loc.T("Networks"));
    }

    /// <summary>Rebuilds the rows from the loaded networks.</summary>
    public void SetItems(IReadOnlyList<NetworkRow> networks)
    {
        summary.Text = Loc.Line($"{networks.Count} network{(networks.Count == 1 ? "" : "s")} · {networks.Count(n => n.BuiltIn)} built-in");
        rows.Children.Clear();
        if (networks.Count == 0) { rows.Children.Add(Ui.Caption("The engine reported no networks.")); return; }
        foreach (var n in networks) rows.Children.Add(Row(n));
    }

    private static UIElement Row(NetworkRow n)
    {
        var name = Ui.Text(n.Name, 13, FontWeights.SemiBold, "OnSurface", false, false);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var driver = Ui.Text(n.Driver, 13, null, "OnSurface", false, false);
        var subnet = Ui.Mono(n.Subnets.Count == 0 ? "—" : n.SubnetText, 12, "OnSurface", false);
        subnet.TextTrimming = TextTrimming.CharacterEllipsis;
        var containers = Ui.Text(n.Containers.Count == 0 ? "None" : n.Containers.Count + (n.Containers.Count == 1 ? " container" : " containers"), 13, null, "OnSurface", false, true);
        foreach (var t in new[] { name, driver, subnet, containers }) t.VerticalAlignment = VerticalAlignment.Center;
        var row = Ui.Columns(10, [-24, 2, 1, 1.4, 1.2], Ui.Icon(Icons.Lan, 20, false, "OnSurfaceVariant"), name, driver, subnet, containers);
        row.Margin = new Thickness(0, 0, 0, 0);
        var host = new Border { Child = row, Padding = new Thickness(0, 10, 0, 10) };
        host.ToolTip = n.Name + " · " + n.Driver + " · " + n.SubnetText;
        return host;
    }
}
