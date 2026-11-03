using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Zoomable, pannable drawing surface for node graphs. Nodes are focusable cards; edges are curved connectors with
/// an arrowhead and a label. Ctrl+wheel or +/- zooms, middle-drag or drag on the background pans, arrow keys scroll,
/// 0 fits the graph.
/// </summary>
public sealed class GraphSurface : Border
{
    public const double NodeWidth = 240;
    private readonly Canvas canvas = new() { Focusable = true, ClipToBounds = false };
    private readonly ScaleTransform scale = new(1, 1);
    private readonly ScrollViewer scroll;
    private readonly List<(Border Node, object Key)> nodes = new();
    private Point? panStart;
    private double panH, panV;
    public event Action<double>? ZoomChanged;
    public double ZoomLevel => scale.ScaleX;

    public GraphSurface()
    {
        CornerRadius = new CornerRadius(16); ClipToBounds = true;
        SetResourceReference(BackgroundProperty, "SurfaceLowest");
        canvas.Background = Brushes.Transparent;
        canvas.LayoutTransform = scale;
        canvas.FocusVisualStyle = null;
        System.Windows.Automation.AutomationProperties.SetName(canvas, Loc.T("Graph canvas. Arrow keys scroll, plus and minus zoom, 0 fits."));
        scroll = new ScrollViewer { Content = canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, CanContentScroll = false, Focusable = false, Padding = new Thickness(8) };
        Child = scroll;
        scroll.PreviewMouseWheel += (_, e) => { if (Keyboard.Modifiers == ModifierKeys.Control) { Zoom(e.Delta > 0 ? 1.1 : 1 / 1.1); e.Handled = true; } };
        canvas.MouseDown += (_, e) =>
        {
            var onBackground = e.OriginalSource == canvas;
            if (e.ChangedButton == MouseButton.Middle || (e.ChangedButton == MouseButton.Left && onBackground))
            {
                panStart = e.GetPosition(scroll); panH = scroll.HorizontalOffset; panV = scroll.VerticalOffset;
                canvas.CaptureMouse(); canvas.Focus(); e.Handled = true;
            }
        };
        canvas.MouseMove += (_, e) =>
        {
            if (panStart is not { } start) return;
            var p = e.GetPosition(scroll);
            scroll.ScrollToHorizontalOffset(panH + start.X - p.X);
            scroll.ScrollToVerticalOffset(panV + start.Y - p.Y);
        };
        canvas.MouseUp += (_, _) => EndPan();
        canvas.LostMouseCapture += (_, _) => panStart = null;
        canvas.Cursor = Cursors.SizeAll;
        PreviewKeyDown += (_, e) =>
        {
            if (e.OriginalSource is TextBox) return;
            switch (e.Key)
            {
                case System.Windows.Input.Key.Left: scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset - 60); break;
                case System.Windows.Input.Key.Right: scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset + 60); break;
                case System.Windows.Input.Key.Up: scroll.ScrollToVerticalOffset(scroll.VerticalOffset - 60); break;
                case System.Windows.Input.Key.Down: scroll.ScrollToVerticalOffset(scroll.VerticalOffset + 60); break;
                case System.Windows.Input.Key.OemPlus or System.Windows.Input.Key.Add: Zoom(1.2); break;
                case System.Windows.Input.Key.OemMinus or System.Windows.Input.Key.Subtract: Zoom(1 / 1.2); break;
                case System.Windows.Input.Key.D0 or System.Windows.Input.Key.NumPad0: Fit(); break;
                case System.Windows.Input.Key.Escape: EndPan(); return;
                default: return;
            }
            e.Handled = true;
        };
    }

    private void EndPan() { panStart = null; if (canvas.IsMouseCaptured) canvas.ReleaseMouseCapture(); }

    public void Zoom(double factor) => SetZoom(scale.ScaleX * factor);
    public void SetZoom(double value)
    {
        scale.ScaleX = scale.ScaleY = Math.Clamp(value, .25, 2.5);
        ZoomChanged?.Invoke(scale.ScaleX);
    }

    public void Fit()
    {
        if (canvas.Width <= 0 || scroll.ViewportWidth <= 0) { SetZoom(1); return; }
        SetZoom(Math.Min(1.25, Math.Min((scroll.ViewportWidth - 16) / canvas.Width, (scroll.ViewportHeight - 16) / canvas.Height)));
        scroll.ScrollToHome();
    }

    public void Clear(double width, double height)
    {
        canvas.Children.Clear(); nodes.Clear();
        canvas.Width = width; canvas.Height = height;
    }

    /// <summary>Adds a node card. The returned border is focusable; Enter, Space or a click calls <paramref name="select"/>.</summary>
    public Border AddNode(double x, double y, double width, FrameworkElement content, string accessibleName, object key, Action select, string background = "SurfaceContainer")
    {
        var card = new Border { Child = content, Width = width, CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 12, 14, 12), BorderThickness = new Thickness(1), Focusable = true, Cursor = Cursors.Hand, Tag = key };
        card.SetResourceReference(BackgroundProperty, background);
        card.SetResourceReference(BorderBrushProperty, "OutlineVariant");
        card.FocusVisualStyle = (Style)Application.Current.FindResource("FocusRing");
        System.Windows.Automation.AutomationProperties.SetName(card, accessibleName);
        card.MouseLeftButtonUp += (_, e) => { card.Focus(); select(); e.Handled = true; };
        card.MouseLeftButtonDown += (_, e) => e.Handled = true;
        card.KeyDown += (_, e) => { if (e.Key is System.Windows.Input.Key.Enter or System.Windows.Input.Key.Space) { select(); e.Handled = true; } };
        Canvas.SetLeft(card, x); Canvas.SetTop(card, y);
        Panel.SetZIndex(card, 2);
        canvas.Children.Add(card);
        nodes.Add((card, key));
        return card;
    }

    /// <summary>Highlights the node with <paramref name="key"/> and scrolls it into view.</summary>
    public void Highlight(object? key)
    {
        foreach (var (node, k) in nodes)
        {
            var on = Equals(k, key);
            node.SetResourceReference(BorderBrushProperty, on ? "Primary" : "OutlineVariant");
            node.BorderThickness = new Thickness(on ? 2 : 1);
            node.Padding = new Thickness(on ? 13 : 14, on ? 11 : 12, on ? 13 : 14, on ? 11 : 12);
            if (on) node.BringIntoView();
        }
    }

    public Border? NodeFor(object key) => nodes.FirstOrDefault(n => Equals(n.Key, key)).Node;

    /// <summary>Adds a curved connector from (x1,y1) to (x2,y2) with an arrowhead and an optional label box.</summary>
    public void AddEdge(Point from, Point to, string label, string brush = "Outline", bool dashed = false, double labelOffset = 0)
    {
        var dx = Math.Max(40, Math.Abs(to.X - from.X) / 2);
        var figure = new PathFigure { StartPoint = from };
        figure.Segments.Add(new BezierSegment(new Point(from.X + dx, from.Y), new Point(to.X - dx, to.Y), to, true));
        var path = new System.Windows.Shapes.Path { Data = new PathGeometry([figure]), StrokeThickness = 2, IsHitTestVisible = false };
        path.SetResourceReference(Shape.StrokeProperty, brush);
        if (dashed) path.StrokeDashArray = new DoubleCollection([5, 3]);
        canvas.Children.Add(path);
        var arrow = new Polygon { Points = new PointCollection([to, new Point(to.X - 9, to.Y - 5), new Point(to.X - 9, to.Y + 5)]), IsHitTestVisible = false };
        arrow.SetResourceReference(Shape.FillProperty, brush);
        canvas.Children.Add(arrow);
        if (label.Length == 0) return;
        var text = Ui.Mono(label, 11, brush);
        var box = new Border { Child = text, CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 2, 6, 2), BorderThickness = new Thickness(1), MaxWidth = 200, IsHitTestVisible = false };
        box.SetResourceReference(BackgroundProperty, "SurfaceLowest");
        box.SetResourceReference(BorderBrushProperty, "OutlineVariant");
        box.Loaded += (_, _) =>
        {
            Canvas.SetLeft(box, (from.X + to.X) / 2 - box.ActualWidth / 2);
            Canvas.SetTop(box, (from.Y + to.Y) / 2 - box.ActualHeight / 2 + labelOffset);
        };
        Canvas.SetLeft(box, (from.X + to.X) / 2 - 40);
        Canvas.SetTop(box, (from.Y + to.Y) / 2 - 10 + labelOffset);
        Panel.SetZIndex(box, 1);
        canvas.Children.Add(box);
    }

    public void AddLabel(double x, double y, string text, string brush = "OnSurfaceVariant")
    {
        var label = Ui.Overline(text);
        label.SetResourceReference(TextBlock.ForegroundProperty, brush);
        Canvas.SetLeft(label, x); Canvas.SetTop(label, y);
        canvas.Children.Add(label);
    }
}
