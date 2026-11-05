using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Zoomable, pannable drawing surface for node graphs. Nodes are focusable cards; edges are orthogonal elbow connectors
/// with rounded corners, an arrowhead and a label. Ctrl+wheel or +/- zooms, middle-drag or drag on the background pans, arrow keys scroll,
/// 0 fits the graph.
/// </summary>
public sealed class GraphSurface : Border
{
    public const double NodeWidth = 250;
    /// <summary>Corner radius of elbow connectors.</summary>
    public const double ConnectorRadius = 12;
    /// <summary>Inner padding of the scroll area around the drawing, in screen pixels.</summary>
    public const double ScrollPadding = 8;
    private int edgeCount;
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
        CornerRadius = new CornerRadius(12); ClipToBounds = true;
        SetResourceReference(BackgroundProperty, "SurfaceLowest");
        canvas.Background = Brushes.Transparent;
        canvas.LayoutTransform = scale;
        canvas.FocusVisualStyle = null;
        System.Windows.Automation.AutomationProperties.SetName(canvas, Loc.T("Graph canvas. Arrow keys scroll, plus and minus zoom, 0 fits."));
        scroll = new ScrollViewer { Content = canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, CanContentScroll = false, Focusable = false, Padding = new Thickness(ScrollPadding) };
        Child = scroll;
        scroll.ScrollChanged += (_, _) => RaiseViewChanged();
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
        RaiseViewChanged();
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
        edgeCount = 0;
        RaiseViewChanged();
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
        RaiseViewChanged();
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
        highlighted = key;
        RaiseViewChanged();
    }

    // ----------------------------------------------------------------- view model for the minimap

    private object? highlighted;
    private bool viewChangePending;

    /// <summary>Raised (coalesced, at render priority) whenever nodes, zoom or the scroll position change.</summary>
    public event Action? ViewChanged;

    private void RaiseViewChanged()
    {
        if (viewChangePending) return;
        viewChangePending = true;
        Dispatcher.BeginInvoke(() => { viewChangePending = false; ViewChanged?.Invoke(); }, System.Windows.Threading.DispatcherPriority.Render);
    }

    /// <summary>Size of the drawing in content (unzoomed) coordinates.</summary>
    public GraphRect ContentBounds => new(0, 0, double.IsNaN(canvas.Width) ? 0 : canvas.Width, double.IsNaN(canvas.Height) ? 0 : canvas.Height);

    /// <summary>Number of connectors currently drawn.</summary>
    public int EdgeCount => edgeCount;

    /// <summary>Every node rectangle in content coordinates, with whether it is the highlighted one.</summary>
    public IEnumerable<(GraphRect Bounds, bool Highlighted)> NodeBounds() =>
        nodes.Select(n => (new GraphRect(Canvas.GetLeft(n.Node), Canvas.GetTop(n.Node), n.Node.Width,
            double.IsNaN(n.Node.Height) ? Math.Max(40, n.Node.ActualHeight) : n.Node.Height), Equals(n.Key, highlighted)));

    /// <summary>The part of the drawing visible in the scroll viewport, in content coordinates.</summary>
    public GraphRect VisibleContent => CanvasRouting.VisibleContent(scroll.HorizontalOffset, scroll.VerticalOffset, scroll.ViewportWidth, scroll.ViewportHeight, ZoomLevel, ScrollPadding);

    /// <summary>Scrolls so the given content point sits in the middle of the viewport (as far as the extent allows).</summary>
    public void CenterOn(GraphPoint content)
    {
        var (h, v) = CanvasRouting.ScrollToCenter(content, scroll.ViewportWidth, scroll.ViewportHeight, ZoomLevel, ScrollPadding);
        scroll.ScrollToHorizontalOffset(h);
        scroll.ScrollToVerticalOffset(v);
    }

    /// <summary>Scrolls by screen pixels.</summary>
    public void PanBy(double dx, double dy)
    {
        scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset + dx);
        scroll.ScrollToVerticalOffset(scroll.VerticalOffset + dy);
    }

    public void ScrollHome() => scroll.ScrollToHome();

    public Border? NodeFor(object key) => nodes.FirstOrDefault(n => Equals(n.Key, key)).Node;

    /// <summary>Adds an elbow connector from the right side of one node to the left side of another (runtime view).</summary>
    public void AddEdge(Point from, Point to, string label, string brush = "Outline", bool dashed = false, double labelOffset = 0) =>
        AddRoute(CanvasRouting.Elbow(new(from.X, from.Y), PortSide.Right, new(to.X, to.Y), PortSide.Left), label, brush, labelOffset);

    /// <summary>
    /// Draws an orthogonal route with rounded corners, an arrowhead pointing along the final segment, and an optional
    /// label pill. A second label line (for example a copied path) is drawn as plain text on the other side of the run.
    /// </summary>
    public void AddRoute(IReadOnlyList<GraphPoint> points, string label, string brush = "Outline", double labelOffset = 0)
    {
        if (points.Count < 2) return;
        var figure = new PathFigure { StartPoint = new Point(points[0].X, points[0].Y) };
        foreach (var segment in CanvasRouting.Round(points, ConnectorRadius))
        {
            var end = new Point(segment.End.X, segment.End.Y);
            if (segment.IsArc) figure.Segments.Add(new ArcSegment(end, new Size(segment.Radius, segment.Radius), 0, false, segment.Clockwise ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true));
            else figure.Segments.Add(new LineSegment(end, true));
        }
        var path = new System.Windows.Shapes.Path { Data = new PathGeometry([figure]), StrokeThickness = 2, IsHitTestVisible = false, StrokeLineJoin = PenLineJoin.Round };
        path.SetResourceReference(Shape.StrokeProperty, brush);
        // Design: connectors are solid 2 px orthogonal lines; the pill label carries the kind of link.
        canvas.Children.Add(path);
        var tip = new Point(points[^1].X, points[^1].Y);
        var (dx, dy) = CanvasRouting.EndDirection(points);
        var back = new Point(tip.X - dx * 10, tip.Y - dy * 10);
        var arrow = new Polygon { Points = new PointCollection([tip, new Point(back.X - dy * 6, back.Y + dx * 6), new Point(back.X + dy * 6, back.Y - dx * 6)]), IsHitTestVisible = false };
        arrow.SetResourceReference(Shape.FillProperty, brush);
        canvas.Children.Add(arrow);
        edgeCount++;
        if (label.Length == 0) return;
        var tertiary = brush == "Tertiary";
        var lines = label.Split('\n');
        var text = Ui.Mono(lines[0], 12, tertiary ? "OnTertiaryContainer" : brush);
        text.FontWeight = FontWeights.SemiBold;
        text.VerticalAlignment = VerticalAlignment.Center;
        var box = new Border { Child = text, CornerRadius = new CornerRadius(13), Height = 26, Padding = new Thickness(10, 0, 10, 0), MaxWidth = 220, IsHitTestVisible = false };
        box.SetResourceReference(BackgroundProperty, tertiary ? "TertiaryContainer" : "SurfaceHighest");
        var (anchor, horizontal) = CanvasRouting.LabelAnchor(points);
        // A route that ends travelling upward enters its target from below: keep the pill under the run, like the board.
        var below = horizontal && dy < 0;
        double boxLeft, boxTop;
        if (horizontal) { boxLeft = anchor.X; boxTop = below ? anchor.Y + 6 : anchor.Y - 28; }
        else { boxLeft = anchor.X + 14; boxTop = anchor.Y - 13; }
        boxTop += labelOffset;
        Canvas.SetLeft(box, boxLeft); Canvas.SetTop(box, boxTop);
        Panel.SetZIndex(box, 1);
        canvas.Children.Add(box);
        if (lines.Length > 1)
        {
            var detail = Ui.Text(lines[1], 11, null, "OnSurfaceVariant", false, false);
            Canvas.SetLeft(detail, horizontal ? anchor.X : boxLeft);
            Canvas.SetTop(detail, (horizontal ? (below ? anchor.Y - 20 : anchor.Y + 6) : boxTop + 30) + (horizontal ? labelOffset : 0));
            Panel.SetZIndex(detail, 1);
            canvas.Children.Add(detail);
        }
    }

    /// <summary>Dashed base-image pill (28 high, 14 radius) with a 2 px connector down to the stage it feeds.</summary>
    public void AddBasePill(double x, double y, string text, double connectorLength)
    {
        var label = Ui.Mono(text, 12, "OnSurfaceVariant");
        label.VerticalAlignment = VerticalAlignment.Center;
        var pill = new Border { Child = label, Height = 28, CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 0, 12, 0), BorderThickness = new Thickness(1), MaxWidth = NodeWidth };
        pill.SetResourceReference(BorderBrushProperty, "Outline");
        var dash = new System.Windows.Shapes.Rectangle { RadiusX = 14, RadiusY = 14, StrokeThickness = 1, StrokeDashArray = new DoubleCollection([3, 3]), IsHitTestVisible = false };
        dash.SetResourceReference(Shape.StrokeProperty, "Outline");
        pill.BorderThickness = new Thickness(0);
        var host = new Grid { Height = 28 };
        host.Children.Add(dash);
        host.Children.Add(pill);
        Canvas.SetLeft(host, x); Canvas.SetTop(host, y);
        canvas.Children.Add(host);
        var line = new System.Windows.Shapes.Rectangle { Width = 2, Height = connectorLength, IsHitTestVisible = false };
        line.SetResourceReference(Shape.FillProperty, "Outline");
        Canvas.SetLeft(line, x + 60); Canvas.SetTop(line, y + 28);
        canvas.Children.Add(line);
    }

    public void AddLabel(double x, double y, string text, string brush = "OnSurfaceVariant")
    {
        var label = Ui.Overline(text);
        label.SetResourceReference(TextBlock.ForegroundProperty, brush);
        Canvas.SetLeft(label, x); Canvas.SetTop(label, y);
        canvas.Children.Add(label);
    }
}
