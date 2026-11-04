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
    private readonly StorageStatStrip stats = new();
    private string mode = "Volumes";
    private string volumeSummary = "", networkSummary = "";
    private bool volumesLoaded, networksLoaded;

    public StoragePage()
    {
        volumes = new VolumesView(() => SnapshotContext, () => IsCurrent, s => { volumeSummary = s; UpdateSubtitle(); }) { Reload = RefreshAsync };
        networks = new NetworksView(() => SnapshotContext, () => IsCurrent, s => { networkSummary = s; UpdateSubtitle(); }) { Reload = RefreshAsync };
        volumes.StatsChanged += (count, total, unused) => stats.SetVolumes(count, total, unused);
        networks.CountChanged += stats.SetNetworks;
        var segment = Ui.Segmented(["Volumes", "Networks"], "Volumes", v => { mode = v; Show(); });
        segment.Width = 260;
        var header = Ui.Header("Storage", "Volumes and networks", out subtitle,
            segment,
            Ui.AsyncButton("Refresh", Icons.Refresh, ButtonKind.Tonal, RefreshAsync));
        Layout(header, Ui.Rows(0, [0, 1], CollapsibleBar.Wrap("storage.stats", "Statistics", stats), host));
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
        subtitle.Text = Loc.Line(text.Length > 0 ? text + $" · context {SnapshotContext ?? "current"}" : Loc.T("Volumes and networks"));
    }

    public override async Task RefreshAsync()
    {
        if (mode == "Volumes") { volumesLoaded = true; await volumes.LoadAsync(); networksLoaded = false; await LightCountAsync("network", stats.SetNetworks); }
        else { networksLoaded = true; await networks.LoadAsync(); volumesLoaded = false; await LightCountAsync("volume", stats.SetVolumeCount); }
    }

    /// <summary>Counts the resources of the hidden view with one quiet read so every stat tile shows real numbers.</summary>
    private async Task LightCountAsync(string kind, Action<int> set)
    {
        var r = await Host.Read([kind, "ls", "--quiet"], SnapshotContext);
        if (r.Success) set(r.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length);
    }

    public override IEnumerable<AppCommand> PageCommands() =>
    [
        new AppCommand { Id = "storage.volumes", Title = "Show volumes", Group = "Storage", Icon = Icons.Database, Keywords = "volume ls", Run = () => Host.Navigate("Storage") },
        new AppCommand { Id = "storage.volume.create", Title = "Create volume…", Group = "Storage", Icon = Icons.Add, Keywords = "volume create", Run = () => { Host.Navigate("Storage"); VolumeCreateForm.Show(() => SnapshotContext, () => IsCurrent, RefreshAsync); } },
    ];
}
