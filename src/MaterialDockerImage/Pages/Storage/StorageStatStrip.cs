using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Stat tiles for Storage: volume count, total reported size, unused volumes and network count.</summary>
public sealed class StorageStatStrip : WrapPanel
{
    private readonly TextBlock volumes, size, unused, networks;
    private readonly TextBlock sizeNote = Ui.Caption("");
    private readonly TextBlock unusedNote = Ui.Caption("");

    public StorageStatStrip()
    {
        Border Tile(string label, string icon, out TextBlock value, TextBlock? note = null)
        {
            value = Ui.Text("—", 26, FontWeights.SemiBold, "OnSurface", false, false);
            var b = Ui.Card(Ui.VStack(4, Ui.HStack(6, Ui.Icon(icon, 16, false, "OnSurfaceVariant"), Ui.Overline(label)), value, note), 14, "SurfaceContainer", 14);
            b.MinWidth = 160; b.Margin = new Thickness(0, 0, 12, 12);
            return b;
        }
        Children.Add(Tile("Volumes", Icons.Database, out volumes));
        Children.Add(Tile("Total size", Icons.DataUsage, out size, sizeNote));
        Children.Add(Tile("Unused volumes", Icons.LayersClear, out unused, unusedNote));
        Children.Add(Tile("Networks", Icons.Lan, out networks));
    }

    /// <summary>Full volume statistics from a complete load. A null total means the engine did not report sizes.</summary>
    public void SetVolumes(int count, double? totalBytes, int? unusedCount)
    {
        volumes.Text = count.ToString();
        size.Text = totalBytes is { } t ? Ui.FormatBytes(t) : "—";
        sizeNote.Text = totalBytes == null ? Loc.T("Not reported by this engine") : Loc.T("From docker system df -v");
        unused.Text = unusedCount?.ToString() ?? "—";
        unusedNote.Text = unusedCount is > 0 ? Loc.T("Not mounted by any container") : unusedCount == 0 ? Loc.T("Every volume is in use") : "";
    }

    /// <summary>Count only, from a light read while the Networks view is shown.</summary>
    public void SetVolumeCount(int count)
    {
        volumes.Text = count.ToString();
        if (size.Text == "—") sizeNote.Text = Loc.T("Open Volumes to measure");
    }

    public void SetNetworks(int count) => networks.Text = count.ToString();
}
