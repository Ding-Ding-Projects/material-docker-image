using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Volumes and networks of the bound engine context.</summary>
public sealed class StoragePage : PageBase
{
    public override string Key => "Storage";
    private readonly VolumesView volumes;
    private readonly NetworksView networks;
    private readonly TextBlock subtitle;
    private readonly ContentControl host = new();
    private string mode = "Volumes";
    private string volumeSummary = "", networkSummary = "";
    private bool volumesLoaded, networksLoaded;

    public StoragePage()
    {
        volumes = new VolumesView(() => SnapshotContext, () => IsCurrent, s => { volumeSummary = s; UpdateSubtitle(); }) { Reload = RefreshAsync };
        networks = new NetworksView(() => SnapshotContext, () => IsCurrent, s => { networkSummary = s; UpdateSubtitle(); }) { Reload = RefreshAsync };
        var segment = Ui.Segmented(["Volumes", "Networks"], "Volumes", v => { mode = v; Show(); });
        segment.Width = 260;
        var header = Ui.Header("Storage", "Volumes and networks", out subtitle,
            segment,
            Ui.AsyncButton("Refresh", Icons.Refresh, ButtonKind.Tonal, RefreshAsync));
        Layout(header, host);
        Show();
    }

    private void Show()
    {
        host.Content = mode == "Volumes" ? volumes : networks;
        UpdateSubtitle();
        if (IsVisible && (mode == "Volumes" ? !volumesLoaded : !networksLoaded)) _ = SafeRefresh();
    }

    private void UpdateSubtitle()
    {
        var text = mode == "Volumes" ? volumeSummary : networkSummary;
        subtitle.Text = text.Length > 0 ? text + $" · context {SnapshotContext ?? "current"}" : Loc.T("Volumes and networks");
    }

    public override async Task RefreshAsync()
    {
        if (mode == "Volumes") { volumesLoaded = true; await volumes.LoadAsync(); networksLoaded = false; }
        else { networksLoaded = true; await networks.LoadAsync(); volumesLoaded = false; }
    }

    public override IEnumerable<AppCommand> PageCommands() =>
    [
        new AppCommand { Id = "storage.volumes", Title = "Show volumes", Group = "Storage", Icon = Icons.Database, Keywords = "volume ls", Run = () => Host.Navigate("Storage") },
        new AppCommand { Id = "storage.volume.create", Title = "Create volume…", Group = "Storage", Icon = Icons.Add, Keywords = "volume create", Run = () => { Host.Navigate("Storage"); VolumeCreateForm.Show(() => SnapshotContext, () => IsCurrent, RefreshAsync); } },
    ];
}
