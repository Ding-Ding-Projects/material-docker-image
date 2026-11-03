using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Chunk grid where the user drags out the area to pre-generate. Arrow keys move the rectangle by one chunk; Shift+arrow
/// keys resize it. North (negative Z) is up. Colours come from theme roles.
/// </summary>
internal sealed class MinecraftAreaCanvas : FrameworkElement
{
    private MinecraftChunkArea area = MinecraftChunkArea.Default;
    private (int X, int Z)? dragStart;
    /// <summary>Number of chunks visible across the shorter side.</summary>
    public int Span { get; private set; } = 160;
    public event Action<MinecraftChunkArea>? AreaChanged;
    public MinecraftChunkArea Area { get => area; set { area = value; InvalidateVisual(); } }

    public MinecraftAreaCanvas()
    {
        Focusable = true;
        MinHeight = 280; Height = 360;
        Cursor = Cursors.Cross;
        FocusVisualStyle = null;
        System.Windows.Automation.AutomationProperties.SetName(this, Loc.T("Generation area. Drag to draw; arrow keys move; Shift and arrow keys resize."));
        GotKeyboardFocus += (_, _) => InvalidateVisual();
        LostKeyboardFocus += (_, _) => InvalidateVisual();
        Loaded += (_, _) => AppHost.Current.SettingsChanged += InvalidateVisual;
        Unloaded += (_, _) => AppHost.Current.SettingsChanged -= InvalidateVisual;
    }

    public void SetSpan(int chunks) { Span = Math.Clamp(chunks, 16, 4096); InvalidateVisual(); }

    private double Scale => Math.Max(0.1, Math.Min(ActualWidth, ActualHeight) / Span);
    private (int X, int Z) ChunkAt(Point p)
    {
        var s = Scale;
        return ((int)Math.Floor((p.X - ActualWidth / 2) / s), (int)Math.Floor((p.Y - ActualHeight / 2) / s));
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton != MouseButton.Left) return;
        Focus();
        dragStart = ChunkAt(e.GetPosition(this));
        CaptureMouse();
        Update(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragStart != null && e.LeftButton == MouseButtonState.Pressed) Update(e.GetPosition(this));
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (dragStart == null) return;
        Update(e.GetPosition(this));
        dragStart = null;
        ReleaseMouseCapture();
    }

    private void Update(Point p)
    {
        var (x, z) = ChunkAt(p);
        area = MinecraftChunkArea.FromCorners(dragStart!.Value.X, dragStart.Value.Z, x, z);
        InvalidateVisual();
        AreaChanged?.Invoke(area);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int dx = e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0, dz = e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0;
        if (dx == 0 && dz == 0) return;
        var a = area;
        area = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
            ? new MinecraftChunkArea(a.MinChunkX, a.MinChunkZ, Math.Max(a.MinChunkX, a.MaxChunkX + dx), Math.Max(a.MinChunkZ, a.MaxChunkZ + dz))
            : new MinecraftChunkArea(a.MinChunkX + dx, a.MinChunkZ + dz, a.MaxChunkX + dx, a.MaxChunkZ + dz);
        InvalidateVisual();
        AreaChanged?.Invoke(area);
        e.Handled = true;
    }

    private Brush B(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth; var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h), 12, 12));
        dc.DrawRectangle(B("SurfaceLowest"), null, new Rect(0, 0, w, h));
        var s = Scale;
        var step = 1;
        while (step * s < 10) step *= 2;
        var grid = new Pen(B("OutlineVariant"), 0.5); grid.Freeze();
        var cx = w / 2; var cz = h / 2;
        var firstX = (int)Math.Floor(-cx / s / step) * step; var lastX = (int)Math.Ceiling(cx / s / step) * step;
        for (var i = firstX; i <= lastX; i += step) { var x = cx + i * s; dc.DrawLine(grid, new Point(x, 0), new Point(x, h)); }
        var firstZ = (int)Math.Floor(-cz / s / step) * step; var lastZ = (int)Math.Ceiling(cz / s / step) * step;
        for (var i = firstZ; i <= lastZ; i += step) { var y = cz + i * s; dc.DrawLine(grid, new Point(0, y), new Point(w, y)); }
        var axis = new Pen(B("Outline"), 1.2); axis.Freeze();
        dc.DrawLine(axis, new Point(cx, 0), new Point(cx, h));
        dc.DrawLine(axis, new Point(0, cz), new Point(w, cz));

        var rect = new Rect(cx + area.MinChunkX * s, cz + area.MinChunkZ * s, area.WidthChunks * s, area.DepthChunks * s);
        var fill = B("Primary").Clone(); fill.Opacity = 0.22;
        dc.DrawRoundedRectangle(fill, new Pen(B("Primary"), IsKeyboardFocused ? 3 : 2), rect, 3, 3);
        dc.DrawEllipse(B("Tertiary"), null, new Point(cx, cz), 4, 4);

        var typeface = new Typeface((TryFindResource("TextFont") as FontFamily) ?? new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        void Label(string text, double x, double y)
        {
            var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, 12, B("OnSurfaceVariant"), dpi);
            dc.DrawText(ft, new Point(Math.Clamp(x, 4, Math.Max(4, w - ft.Width - 4)), Math.Clamp(y, 4, Math.Max(4, h - ft.Height - 4))));
        }
        Label("0, 0", cx + 6, cz + 4);
        Label("N  −Z", cx + 6, 4);
        Label("+X  E", w - 60, cz + 4);
        Label($"grid: {step} chunk{(step == 1 ? "" : "s")} · view {Span} chunks", 6, h - 20);
        dc.Pop();
    }
}
