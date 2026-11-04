using System.Windows;
using System.Windows.Controls;

namespace MaterialDockerImage.UI;

/// <summary>
/// Lays segmented buttons out in one row and never asks for more width than it is offered, so a segmented control
/// cannot push past its card. Segments are equal when the widest one fits every slot; when only the total fits, each
/// segment gets its own width plus an equal share of what is left, so no label is shortened; only when even the total
/// does not fit are the segments shrunk in proportion and the longest labels end in an ellipsis (the segment template
/// trims, and the full text stays in the tooltip and the accessible name).
/// </summary>
public sealed class SegmentPanel : Panel
{
    private double[] widths = [];

    protected override Size MeasureOverride(Size available)
    {
        var children = InternalChildren.Cast<UIElement>().ToList();
        var natural = new List<double>();
        double tallest = 0;
        foreach (var child in children)
        {
            child.Measure(new Size(double.PositiveInfinity, available.Height));
            natural.Add(child.DesiredSize.Width);
            tallest = Math.Max(tallest, child.DesiredSize.Height);
        }
        widths = SegmentWidths.Allocate(natural, available.Width);
        for (var i = 0; i < children.Count; i++) children[i].Measure(new Size(widths[i], available.Height));
        return new Size(widths.Sum(), tallest);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var total = widths.Sum();
        var stretch = total > 0 ? final.Width / total : 1;
        double x = 0;
        for (var i = 0; i < InternalChildren.Count && i < widths.Length; i++)
        {
            var w = widths[i] * stretch;
            InternalChildren[i].Arrange(new Rect(x, 0, w, final.Height));
            x += w;
        }
        return final;
    }
}
