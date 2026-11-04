using System.Windows;
using System.Windows.Controls;

namespace MaterialDockerImage.UI;

/// <summary>
/// Lays segmented buttons out in equal widths. With a finite width it never asks for more than it is offered, so a
/// segmented control cannot push past its card; a label that does not fit its share is shortened with an ellipsis by
/// the segment template. With unlimited width every segment gets the widest segment's natural width.
/// </summary>
public sealed class SegmentPanel : Panel
{
    protected override Size MeasureOverride(Size available)
    {
        var count = Math.Max(1, InternalChildren.Count);
        var finite = !double.IsInfinity(available.Width);
        var slot = new Size(finite ? available.Width / count : double.PositiveInfinity, available.Height);
        double widest = 0, tallest = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(slot);
            widest = Math.Max(widest, child.DesiredSize.Width);
            tallest = Math.Max(tallest, child.DesiredSize.Height);
        }
        var natural = widest * count;
        return new Size(finite ? Math.Min(natural, available.Width) : natural, tallest);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var count = Math.Max(1, InternalChildren.Count);
        var width = final.Width / count;
        for (var i = 0; i < InternalChildren.Count; i++) InternalChildren[i].Arrange(new Rect(i * width, 0, width, final.Height));
        return final;
    }
}
