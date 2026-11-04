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

    public ImageStatTile(string label, string icon, Tone tone = Tone.Neutral)
    {
        this.label = label;
        var (bg, fg) = tone == Tone.Neutral ? ("SurfaceContainer", "OnSurface") : Ui.ToneKeys(tone);
        CornerRadius = new CornerRadius(16);
        Padding = new Thickness(14, 10, 14, 12);
        SetResourceReference(BackgroundProperty, bg);
        var head = Ui.HStack(6, Ui.Icon(icon, 18, true, tone == Tone.Neutral ? "Primary" : fg), Ui.Text(label, 12.5, FontWeights.SemiBold, fg, false));
        value = Ui.Text("—", 28, FontWeights.SemiBold, fg, false, false);
        caption = Ui.Text("", 12, null, tone == Tone.Neutral ? "OnSurfaceVariant" : fg, false, false);
        caption.TextTrimming = TextTrimming.CharacterEllipsis;
        Child = Ui.VStack(2, head, value, caption);
        System.Windows.Automation.AutomationProperties.SetName(this, Services.Loc.T(label));
    }

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
