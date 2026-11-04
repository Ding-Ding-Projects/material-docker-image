using System.Windows;
using System.Windows.Controls;

namespace MaterialDockerImage.UI;

/// <summary>
/// Lays out a page title and its actions. When the actions fit beside the title (keeping the title at least
/// <see cref="TitleMinWidth"/> wide) they sit on the right; otherwise they move to a row of their own under the title
/// and wrap there. A grid with an Auto column cannot do this, because it measures the actions with unlimited width and
/// then asks its parent for that much room.
/// </summary>
public sealed class HeaderPanel : Panel
{
    public const double TitleMinWidth = 220;
    public const double StackedGap = 8;
    private readonly UIElement title;
    private readonly FrameworkElement actions;
    private bool stacked;

    public HeaderPanel(UIElement title, FrameworkElement actions)
    {
        this.title = title; this.actions = actions;
        Children.Add(title); Children.Add(actions);
    }

    protected override Size MeasureOverride(Size available)
    {
        actions.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var wide = actions.DesiredSize;
        stacked = !double.IsInfinity(available.Width) && wide.Width + TitleMinWidth > available.Width;
        if (stacked)
        {
            title.Measure(new Size(available.Width, double.PositiveInfinity));
            actions.Measure(new Size(available.Width, double.PositiveInfinity));
            return new Size(Math.Max(title.DesiredSize.Width, actions.DesiredSize.Width), title.DesiredSize.Height + StackedGap + actions.DesiredSize.Height);
        }
        var titleWidth = double.IsInfinity(available.Width) ? double.PositiveInfinity : available.Width - wide.Width;
        title.Measure(new Size(titleWidth, available.Height));
        return new Size(title.DesiredSize.Width + wide.Width, Math.Max(title.DesiredSize.Height, wide.Height));
    }

    protected override Size ArrangeOverride(Size final)
    {
        if (stacked)
        {
            title.Arrange(new Rect(0, 0, final.Width, title.DesiredSize.Height));
            actions.HorizontalAlignment = HorizontalAlignment.Left;
            actions.Arrange(new Rect(0, title.DesiredSize.Height + StackedGap, final.Width, actions.DesiredSize.Height));
        }
        else
        {
            var actionsWidth = actions.DesiredSize.Width;
            title.Arrange(new Rect(0, 0, Math.Max(0, final.Width - actionsWidth), final.Height));
            actions.HorizontalAlignment = HorizontalAlignment.Right;
            actions.Arrange(new Rect(final.Width - actionsWidth, Math.Max(0, (final.Height - actions.DesiredSize.Height) / 2), actionsWidth, actions.DesiredSize.Height));
        }
        return final;
    }
}
