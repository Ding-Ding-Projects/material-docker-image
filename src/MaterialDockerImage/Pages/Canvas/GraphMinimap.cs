using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using MaterialDockerImage.Services;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Overview of a <see cref="GraphSurface"/>: every node as a small rectangle (the selected one in the primary colour)
/// and an outlined box for the visible area. Click or drag to move the view there; when focused, arrow keys pan,
/// Page Up / Page Down pan by a whole view and Home returns to the start. Movement is immediate, never animated.
/// </summary>
public sealed class GraphMinimap : Border
{
    public const double MapWidth = 160, MapHeight = 110, Inset = 8;
    private readonly GraphSurface surface;
    private readonly Canvas map = new() { Width = MapWidth - 2, Height = MapHeight - 2, ClipToBounds = true };
    private MinimapScale scale = new(1, Inset, Inset);
    private bool dragging;

    public GraphMinimap(GraphSurface surface)
    {
        this.surface = surface;
        Width = MapWidth; Height = MapHeight;
        CornerRadius = new CornerRadius(12);
        BorderThickness = new Thickness(1);
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = new Thickness(0, 0, 12, 12);
        Focusable = true;
        Cursor = Cursors.Hand;
        SetResourceReference(BackgroundProperty, "SurfaceContainer");
        SetResourceReference(BorderBrushProperty, "OutlineVariant");
        FocusVisualStyle = (Style)Application.Current.FindResource("FocusRing");
        map.Background = System.Windows.Media.Brushes.Transparent;
        Child = map;
        System.Windows.Automation.AutomationProperties.SetName(this, Loc.T("Minimap"));
        System.Windows.Automation.AutomationProperties.SetHelpText(this, Loc.T("Overview of the whole graph. The outlined box is the visible area. Click or drag to move the view; arrow keys pan and Home returns to the start."));
        ToolTip = Loc.T("Minimap: click or drag to move the view");
        Panel.SetZIndex(this, 5);

        surface.ViewChanged += Redraw;
        IsVisibleChanged += (_, _) => { if (IsVisible) Redraw(); };
        MouseLeftButtonDown += (_, e) => { dragging = true; CaptureMouse(); Focus(); MoveTo(e.GetPosition(map)); e.Handled = true; };
        MouseMove += (_, e) => { if (dragging) MoveTo(e.GetPosition(map)); };
        MouseLeftButtonUp += (_, _) => EndDrag();
        LostMouseCapture += (_, _) => dragging = false;
        KeyDown += OnKey;
    }

    private void EndDrag() { dragging = false; if (IsMouseCaptured) ReleaseMouseCapture(); }

    private void MoveTo(Point mapPoint)
    {
        if (surface.ContentBounds.Width <= 0) return;
        var content = CanvasRouting.FromMap(new(mapPoint.X, mapPoint.Y), scale);
        surface.CenterOn(content);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        var visible = surface.VisibleContent;
        var zoom = surface.ZoomLevel;
        double step = 60;
        switch (e.Key)
        {
            case Key.Left: surface.PanBy(-step, 0); break;
            case Key.Right: surface.PanBy(step, 0); break;
            case Key.Up: surface.PanBy(0, -step); break;
            case Key.Down: surface.PanBy(0, step); break;
            case Key.PageUp: surface.PanBy(0, -visible.Height * zoom); break;
            case Key.PageDown: surface.PanBy(0, visible.Height * zoom); break;
            case Key.Home: surface.ScrollHome(); break;
            case Key.Escape: EndDrag(); return;
            default: return;
        }
        e.Handled = true;
    }

    private void Redraw()
    {
        if (!IsVisible) return;
        map.Children.Clear();
        var bounds = surface.ContentBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        scale = CanvasRouting.MinimapFit(bounds.Width, bounds.Height, map.Width, map.Height, Inset);
        foreach (var (rect, highlighted) in surface.NodeBounds())
        {
            var r = CanvasRouting.ToMap(rect, scale);
            var shape = new Rectangle { Width = Math.Max(3, r.Width), Height = Math.Max(3, r.Height), RadiusX = 4, RadiusY = 4, IsHitTestVisible = false };
            shape.SetResourceReference(Shape.FillProperty, highlighted ? "Primary" : "OutlineVariant");
            Canvas.SetLeft(shape, r.X); Canvas.SetTop(shape, r.Y);
            map.Children.Add(shape);
        }
        var view = CanvasRouting.ToMap(CanvasRouting.Clip(surface.VisibleContent, bounds), scale);
        var frame = new Rectangle { Width = Math.Max(4, view.Width), Height = Math.Max(4, view.Height), RadiusX = 6, RadiusY = 6, StrokeThickness = 1, IsHitTestVisible = false };
        frame.SetResourceReference(Shape.StrokeProperty, "Outline");
        Canvas.SetLeft(frame, view.X); Canvas.SetTop(frame, view.Y);
        map.Children.Add(frame);
    }

    /// <summary>A border has no automation peer of its own, so without this the minimap's name, help text and keyboard
    /// focus would not reach screen readers or UI Automation.</summary>
    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new MinimapPeer(this);

    private sealed class MinimapPeer(GraphMinimap owner) : System.Windows.Automation.Peers.FrameworkElementAutomationPeer(owner)
    {
        protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() => System.Windows.Automation.Peers.AutomationControlType.Pane;
        protected override string GetClassNameCore() => nameof(GraphMinimap);
        protected override bool IsControlElementCore() => true;
        protected override bool IsContentElementCore() => true;
        protected override bool IsKeyboardFocusableCore() => true;
    }
}
