using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Severity tile of the Security page: a filled icon beside a bold count and its label on one row, in the colours the
/// design assigns to each severity. The detail line (for example "in 3 of 11 analyzed") is the tooltip.
/// </summary>
internal sealed class SecurityStatTile : Border
{
    private readonly TextBlock value;
    private readonly string label;

    public SecurityStatTile(string label, string icon, string background, string foreground)
    {
        this.label = label;
        CornerRadius = new CornerRadius(16);
        Padding = new Thickness(16, 14, 16, 14);
        SetResourceReference(BackgroundProperty, background);
        value = Ui.Text("—", 26, FontWeights.Bold, foreground, false, false);
        var text = Ui.VStack(2, value, Ui.Text(label, 13, FontWeights.SemiBold, foreground, false));
        Child = Ui.Columns(12, [0, 1], Ui.Icon(icon, 24, true, foreground), text);
        System.Windows.Automation.AutomationProperties.SetName(this, Loc.T(label));
    }

    public void Set(string number, string detail = "")
    {
        value.Text = number;
        ToolTip = detail.Length > 0 ? detail : null;
        System.Windows.Automation.AutomationProperties.SetName(this, Loc.T(label) + ": " + number + (detail.Length > 0 ? ", " + detail : ""));
    }

    /// <summary>Short windows get a tighter tile so the list keeps its rows.</summary>
    public bool Compact { set => Padding = value ? new Thickness(12, 6, 12, 6) : new Thickness(16, 14, 16, 14); }

    public static void CompactBelow(FrameworkElement host, double height, params SecurityStatTile[] tiles) =>
        host.SizeChanged += (_, _) => { var on = host.ActualHeight > 0 && host.ActualHeight < height; foreach (var t in tiles) t.Compact = on; };

    /// <summary>One evenly divided row with 12 px between tiles.</summary>
    public static UniformGrid Strip(params SecurityStatTile[] tiles)
    {
        var grid = new UniformGrid { Rows = 1, Columns = tiles.Length, Margin = new Thickness(0, 0, -12, 12) };
        foreach (var t in tiles) { t.Margin = new Thickness(0, 0, 12, 0); grid.Children.Add(t); }
        return grid;
    }
}
