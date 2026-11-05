using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Fit-to-window columns for the Registry and Diagnostics boards. Wide windows show every column side by side, each
/// scrolling on its own so the page itself never scrolls; narrow windows stack the same columns in one scroll.
/// </summary>
internal static class PageColumns
{
    public static Grid Fit(double breakpoint, double gap, params (UIElement Content, double Weight)[] columns)
    {
        var host = new Grid();
        bool? wide = null;
        void Arrange()
        {
            var now = host.ActualWidth <= 0 || host.ActualWidth >= breakpoint;
            if (wide == now) return;
            wide = now;
            foreach (var (content, _) in columns) Detach(content);
            host.Children.Clear(); host.ColumnDefinitions.Clear(); host.RowDefinitions.Clear();
            if (now)
            {
                for (var i = 0; i < columns.Length; i++)
                {
                    if (i > 0) host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gap) });
                    host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(columns[i].Weight, GridUnitType.Star), MinWidth = 220 });
                    if (columns[i].Content is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 8, 8);
                    var scroll = Ui.Scroll(columns[i].Content);
                    Grid.SetColumn(scroll, i * 2);
                    host.Children.Add(scroll);
                }
            }
            else
            {
                var stack = new StackPanel();
                foreach (var (content, _) in columns)
                {
                    if (content is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 8, gap);
                    stack.Children.Add(content);
                }
                host.Children.Add(Ui.Scroll(stack));
            }
        }
        host.SizeChanged += (_, _) => Arrange();
        Arrange();
        return host;
    }

    private static void Detach(UIElement element)
    {
        switch (LogicalTreeHelper.GetParent(element))
        {
            case Panel p: p.Children.Remove(element); break;
            case ContentControl c: c.Content = null; break;
            case Decorator d: d.Child = null; break;
        }
    }
}
