using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MaterialDockerImage.UI;
using MaterialDockerImage.Services;

namespace MaterialDockerImage.Pages;

/// <summary>A tonal tile with one big number, used for the Images and Security stat strips.</summary>
internal sealed class ImageStatTile : Border
{
    private readonly TextBlock value;
    private readonly TextBlock caption;
    private readonly string label;
    private readonly FrameworkElement head;
    private bool compact;

    public ImageStatTile(string label, string icon, Tone tone = Tone.Neutral)
    {
        this.label = label;
        var (bg, fg) = tone == Tone.Neutral ? ("SurfaceContainer", "OnSurface") : Ui.ToneKeys(tone);
        CornerRadius = new CornerRadius(16);
        Padding = new Thickness(14, 10, 14, 12);
        SetResourceReference(BackgroundProperty, bg);
        head = Ui.HStack(6, Ui.Icon(icon, 18, true, tone == Tone.Neutral ? "Primary" : fg), Ui.Text(label, 12.5, FontWeights.SemiBold, fg, false));
        value = Ui.Text("—", 28, FontWeights.SemiBold, fg, false, false);
        caption = Ui.Text("", 12, null, tone == Tone.Neutral ? "OnSurfaceVariant" : fg, false, false);
        caption.TextTrimming = TextTrimming.CharacterEllipsis;
        Child = Ui.VStack(2, head, value, caption);
        System.Windows.Automation.AutomationProperties.SetName(this, Services.Loc.T(label));
    }

    /// <summary>One line (label and number, detail in the tooltip) so short windows keep list rows.</summary>
    public bool Compact
    {
        get => compact;
        set
        {
            if (compact == value) return;
            compact = value;
            if (this.value.Parent is Panel old) old.Children.Clear();
            this.value.FontSize = compact ? 18 : 28;
            Padding = compact ? new Thickness(12, 6, 12, 6) : new Thickness(14, 10, 14, 12);
            Child = compact ? Ui.Columns(8, [1, 0], head, this.value) : Ui.VStack(2, head, this.value, caption);
            ToolTip = compact && caption.Text.Length > 0 ? caption.Text : null;
        }
    }

    /// <summary>Switches the tiles to their one-line form while the host is shorter than the threshold.</summary>
    public static void CompactBelow(FrameworkElement host, double height, params ImageStatTile[] tiles) =>
        host.SizeChanged += (_, _) => { var on = host.ActualHeight > 0 && host.ActualHeight < height; foreach (var t in tiles) t.Compact = on; };

    public void Set(string number, string detail = "")
    {
        value.Text = number;
        caption.Text = detail;
        caption.ToolTip = detail.Length > 0 ? detail : null;
        System.Windows.Automation.AutomationProperties.SetName(this, Services.Loc.T(label) + ": " + number + (detail.Length > 0 ? ", " + detail : ""));
    }

    /// <summary>Lays tiles out in one evenly divided row.</summary>
    public static UniformGrid Strip(params ImageStatTile[] tiles)
    {
        var grid = new UniformGrid { Rows = 1, Columns = tiles.Length, Margin = new Thickness(0, 0, -8, 12) };
        foreach (var t in tiles) { t.Margin = new Thickness(0, 0, 8, 0); grid.Children.Add(t); }
        return grid;
    }
}
