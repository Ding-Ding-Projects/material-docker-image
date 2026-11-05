using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Volumes and networks of the bound engine context. The volumes list sits above a compact Networks section on the same
/// page; the Networks segment opens the full networks list. Create network opens as a right-hand panel.
/// </summary>
public sealed class StoragePage : PageBase
{
    public override string Key => "Storage";
    private const double AsideWidth = 380;
    private readonly VolumesView volumes;
    private readonly NetworksView networks;
    private readonly NetworksSection networksSection = new();
    private readonly NetworkCreatePanel createPanel = new();
    private readonly ColumnDefinition asideColumn = new() { Width = new GridLength(0) };
    private readonly TextBlock subtitle;
    private readonly ContentControl host = new();
    private readonly StorageStatStrip stats = new();
    private readonly Button createVolume;
    private readonly Button createNetwork;
    private Border segment = null!;
    private string mode = "Volumes";
    private string volumeSummary = "", networkSummary = "";
    private bool loaded;
    private FrameworkElement? volumesBody;

    public StoragePage()
    {
        volumes = new VolumesView(() => SnapshotContext, () => IsCurrent, s => { volumeSummary = s; UpdateSubtitle(); }) { Reload = RefreshAsync };
        networks = new NetworksView(() => SnapshotContext, () => IsCurrent, s => { networkSummary = s; UpdateSubtitle(); }) { Reload = RefreshAsync };
        volumes.StatsChanged += (count, total, unused) => stats.SetVolumes(count, total, unused);
        networks.CountChanged += stats.SetNetworks;
        networks.ItemsChanged += networksSection.SetItems;
        networks.CreateRequested += OpenCreatePanel;
        networksSection.CreateRequested += OpenCreatePanel;
        networksSection.ManageRequested += () => SetMode("Networks");

        createPanel.Closed += () => asideColumn.Width = new GridLength(0);
        createPanel.Visibility = Visibility.Collapsed;
        createPanel.SetExistingSubnets(() => networks.Items.SelectMany(n => n.Subnets).ToList());
        createPanel.Create = (args, name) => networks.CreateNetworkAsync(args, name);
        createPanel.Closed += () => createPanel.Visibility = Visibility.Collapsed;

        segment = Ui.Segmented(["Volumes", "Networks"], "Volumes", v => { mode = v; Show(); });
        segment.Width = 260;
        createVolume = Ui.Button("Create volume", Icons.Add, ButtonKind.Filled, () => volumes.ShowCreate());
        createNetwork = Ui.Button("Create network", Icons.Add, ButtonKind.Filled, OpenCreatePanel);
        createNetwork.Visibility = Visibility.Collapsed;
        var header = Ui.Header("Storage", "Volumes and networks", out subtitle,
            segment,
            Ui.AsyncIconButton(Icons.Refresh, "Refresh", RefreshAsync),
            createVolume, createNetwork);

        // Volumes above the compact networks section, separated by a divider.
        var stack = new Grid();
        stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star), MinHeight = 200 });
        stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star), MinHeight = 140 });
        stack.Children.Add(volumes.At(0));
        var divider = Ui.Divider();
        divider.Margin = new Thickness(0, 8, 0, 8);
        stack.Children.Add(divider.At(1));
        stack.Children.Add(networksSection.At(2));
        volumesBody = stack;

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(asideColumn);
        content.Children.Add(host.At(0, 0));
        createPanel.Margin = new Thickness(16, 0, 0, 0);
        content.Children.Add(createPanel.At(0, 1));

        Layout(header, Ui.Rows(0, [0, 1], CollapsibleBar.Wrap("storage.stats", "Statistics", stats), content));
        Show();
    }

    private void SetMode(string value)
    {
        // Checking the matching segment raises its Checked handler, which sets the mode and shows the view.
        if (segment.Child is Panel p)
            foreach (var b in p.Children.OfType<RadioButton>())
                if (System.Windows.Automation.AutomationProperties.GetName(b) == Loc.T(value)) { b.IsChecked = true; return; }
        mode = value;
        Show();
    }

    private void OpenCreatePanel()
    {
        createPanel.Visibility = Visibility.Visible;
        asideColumn.Width = new GridLength(AsideWidth + 16);
        createPanel.FocusName();
    }

    private void Show()
    {
        host.Content = mode == "Volumes" ? volumesBody : networks;
        createVolume.Visibility = mode == "Volumes" ? Visibility.Visible : Visibility.Collapsed;
        createNetwork.Visibility = mode == "Networks" ? Visibility.Visible : Visibility.Collapsed;
        UpdateSubtitle();
        if (IsVisible && !loaded) _ = SafeRefresh();
    }

    private void UpdateSubtitle()
    {
        var text = mode == "Volumes" ? volumeSummary : networkSummary;
        subtitle.Text = Loc.Line(text.Length > 0 ? text + $" · context {SnapshotContext ?? "current"}" : Loc.T("Volumes and networks"));
    }

    /// <summary>Loads volumes and networks together so both sections and every stat tile show real numbers.</summary>
    public override async Task RefreshAsync()
    {
        loaded = true;
        await volumes.LoadAsync();
        await networks.LoadAsync();
    }

    public override IEnumerable<AppCommand> PageCommands() =>
    [
        new AppCommand { Id = "storage.volumes", Title = "Show volumes", Group = "Storage", Icon = Icons.Database, Keywords = "volume ls", Run = () => Host.Navigate("Storage") },
        new AppCommand { Id = "storage.volume.create", Title = "Create volume…", Group = "Storage", Icon = Icons.Add, Keywords = "volume create", Run = () => { Host.Navigate("Storage"); VolumeCreateForm.Show(() => SnapshotContext, () => IsCurrent, RefreshAsync); } },
        new AppCommand { Id = "storage.network.create", Title = "Create network…", Group = "Storage", Icon = Icons.Lan, Keywords = "network create bridge overlay subnet", Run = () => { Host.Navigate("Storage"); OpenCreatePanel(); } },
    ];
}
