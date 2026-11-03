using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MaterialDockerImage.Services;

namespace MaterialDockerImage.Pages;

/// <summary>One measured point. Only real samples are ever drawn; nothing is interpolated into the buffer.</summary>
public readonly record struct ChartPoint(DateTimeOffset Time, double Value);

public enum ChartState { Loading, Live, Paused, Empty, Stale, Error }

/// <summary>
/// Live line chart: smooth line with a gradient fill from the series colour, a quiet grid, a pulsing dot at the
/// latest real sample, an updating value badge, a crosshair with a time/value tooltip, keyboard scrubbing and an
/// automation summary. Motion is disabled under reduced motion, low stimulation and capture mode.
/// </summary>
public sealed class LiveChart : FrameworkElement
{
    private IReadOnlyList<ChartPoint> points = [];
    private int? hoverIndex;
    private int? scrubIndex;
    private Point? mouse;

    public static readonly DependencyProperty PulseProperty = DependencyProperty.Register(nameof(Pulse), typeof(double), typeof(LiveChart), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty BadgeValueProperty = DependencyProperty.Register(nameof(BadgeValue), typeof(double), typeof(LiveChart), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Pulse { get => (double)GetValue(PulseProperty); set => SetValue(PulseProperty, value); }
    public double BadgeValue { get => (double)GetValue(BadgeValueProperty); set => SetValue(BadgeValueProperty, value); }

    /// <summary>Accessible name of the series, for example "CPU, hello-api".</summary>
    public string SeriesName { get; set; } = "Series";
    /// <summary>Resource key of the series colour (a role brush).</summary>
    public string SeriesBrushKey { get; set; } = "Primary";
    /// <summary>Formats a value for the badge, axis and tooltip.</summary>
    public Func<double, string> Format { get; set; } = v => v.ToString("0.#", CultureInfo.CurrentCulture);
    /// <summary>Smallest top of the value axis (for example 1 for percentages so a flat zero line still has a scale).</summary>
    public double MinimumCeiling { get; set; } = 1;
    /// <summary>Visible time window in seconds, ending at the latest sample.</summary>
    public double WindowSeconds { get; set; } = 240;
    /// <summary>Samples further apart than this many seconds are not joined by a line (a real gap stays a gap).</summary>
    public double GapSeconds { get; set; } = 10;
    public bool ShowAxisLabels { get; set; } = true;

    public ChartState State { get; private set; } = ChartState.Loading;
    public string StateText { get; private set; } = "Loading samples…";

    private static bool MotionAllowed => AppHost.Current is { } h && !h.Settings.ReducedMotion && !h.Settings.LowStimulation && !h.IsCapture;

    public LiveChart()
    {
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        MinHeight = 120;
        Cursor = Cursors.Cross;
        AutomationProperties.SetName(this, "Chart");
        IsVisibleChanged += (_, _) => UpdatePulse();
        GotKeyboardFocus += (_, _) => InvalidateVisual();
        LostKeyboardFocus += (_, _) => { scrubIndex = null; InvalidateVisual(); };
        if (AppHost.Current is { } host) host.SettingsChanged += () => { UpdatePulse(); InvalidateVisual(); };
    }

    /// <summary>Replaces the drawn samples and the honest state shown on top of them.</summary>
    public void SetData(IReadOnlyList<ChartPoint> samples, ChartState state, string? stateText = null)
    {
        var previousLast = points.Count > 0 ? points[^1].Value : (double?)null;
        points = samples;
        State = samples.Count == 0 && state is ChartState.Live or ChartState.Paused ? ChartState.Empty : state;
        StateText = stateText ?? State switch
        {
            ChartState.Loading => "Loading samples…",
            ChartState.Empty => "No samples yet",
            ChartState.Paused => "Paused",
            ChartState.Stale => "No recent sample",
            ChartState.Error => "The engine did not answer",
            _ => ""
        };
        if (scrubIndex is { } s && s >= points.Count) scrubIndex = points.Count - 1;
        if (hoverIndex is { } h && h >= points.Count) hoverIndex = null;
        if (points.Count > 0)
        {
            var target = points[^1].Value;
            if (MotionAllowed && previousLast is { } from && State == ChartState.Live)
            {
                BeginAnimation(BadgeValueProperty, null);
                BadgeValue = from;
                BeginAnimation(BadgeValueProperty, new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            else { BeginAnimation(BadgeValueProperty, null); BadgeValue = target; }
        }
        UpdatePulse();
        UpdateAutomation();
        InvalidateVisual();
    }

    private void UpdatePulse()
    {
        var animate = MotionAllowed && IsVisible && State == ChartState.Live && points.Count > 0;
        if (animate) { if (!pulsing) { pulsing = true; BeginAnimation(PulseProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(1600)) { RepeatBehavior = RepeatBehavior.Forever }); } }
        else { pulsing = false; BeginAnimation(PulseProperty, null); Pulse = 0; }
    }
    private bool pulsing;

    private void UpdateAutomation()
    {
        if (points.Count == 0) { AutomationProperties.SetName(this, $"{SeriesName}: {Loc.T(StateText)}"); AutomationProperties.SetHelpText(this, ""); return; }
        var values = points.Select(p => p.Value).ToList();
        var latest = values[^1];
        AutomationProperties.SetName(this, $"{SeriesName}: latest {Format(latest)}" + (State is ChartState.Live ? "" : $" ({Loc.T(StateText)})"));
        AutomationProperties.SetHelpText(this, $"{points.Count} samples from {points[0].Time.ToLocalTime():HH:mm:ss} to {points[^1].Time.ToLocalTime():HH:mm:ss}. Minimum {Format(values.Min())}, maximum {Format(values.Max())}, average {Format(values.Average())}. Use Left and Right arrows to read individual samples.");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (points.Count == 0) { base.OnKeyDown(e); return; }
        var current = scrubIndex ?? points.Count - 1;
        switch (e.Key)
        {
            case Key.Left: scrubIndex = Math.Max(0, current - (scrubIndex == null ? 0 : 1)); break;
            case Key.Right: scrubIndex = Math.Min(points.Count - 1, current + (scrubIndex == null ? 0 : 1)); break;
            case Key.Home: scrubIndex = 0; break;
            case Key.End: scrubIndex = points.Count - 1; break;
            case Key.Escape when scrubIndex != null: scrubIndex = null; break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
        if (scrubIndex is { } i)
        {
            var p = points[i];
            AutomationProperties.SetItemStatus(this, $"{p.Time.ToLocalTime():HH:mm:ss}, {Format(p.Value)}");
            Narrator.Speak($"{p.Time.ToLocalTime():HH:mm:ss}, {Format(p.Value)}");
        }
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e) { mouse = e.GetPosition(this); InvalidateVisual(); }
    protected override void OnMouseLeave(MouseEventArgs e) { mouse = null; hoverIndex = null; InvalidateVisual(); }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { Focus(); e.Handled = true; }

    private Brush Res(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;
    private Color SeriesColor => (Res(SeriesBrushKey) as SolidColorBrush)?.Color ?? Colors.SteelBlue;
    private double Dip => VisualTreeHelper.GetDpi(this).PixelsPerDip;
    private FormattedText Label(string text, double size, Brush brush, bool bold = false)
    {
        var family = TryFindResource("TextFont") as FontFamily ?? new FontFamily("Segoe UI");
        return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(family, FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal), size, brush, Dip);
    }

    private static double NiceCeiling(double value)
    {
        if (value <= 0) return 1;
        var exponent = Math.Pow(10, Math.Floor(Math.Log10(value)));
        var fraction = value / exponent;
        var nice = fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 2.5 ? 2.5 : fraction <= 5 ? 5 : 10;
        return nice * exponent;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth; var height = ActualHeight;
        if (width < 40 || height < 40) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
        var muted = Res("OnSurfaceVariant");
        var gridPen = new Pen(Res("OutlineVariant"), 1) { DashStyle = new DashStyle([2, 4], 0) };
        gridPen.Freeze();
        var left = ShowAxisLabels ? 48d : 4d; var right = 12d; var top = 12d; var bottom = ShowAxisLabels ? 24d : 4d;
        var plot = new Rect(left, top, Math.Max(10, width - left - right), Math.Max(10, height - top - bottom));

        var maxValue = points.Count > 0 ? points.Max(p => p.Value) : 0;
        var ceiling = NiceCeiling(Math.Max(MinimumCeiling, maxValue * 1.15));
        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Bottom - plot.Height * i / 4;
            dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            if (ShowAxisLabels)
            {
                var label = Label(Format(ceiling * i / 4), 11, muted);
                dc.DrawText(label, new Point(plot.Left - 8 - label.Width, y - label.Height / 2));
            }
        }

        if (points.Count > 0)
        {
            var end = points[^1].Time;
            var start = end - TimeSpan.FromSeconds(WindowSeconds);
            double X(DateTimeOffset t) => plot.Left + plot.Width * Math.Clamp((t - start).TotalSeconds / WindowSeconds, 0, 1);
            double Y(double v) => plot.Bottom - plot.Height * Math.Clamp(v / ceiling, 0, 1);
            var visible = Enumerable.Range(0, points.Count).Where(i => points[i].Time >= start).ToList();

            if (ShowAxisLabels)
            {
                for (var i = 0; i <= 4; i++)
                {
                    var t = start + TimeSpan.FromSeconds(WindowSeconds * i / 4);
                    var label = Label(i == 4 ? Loc.T("latest") : t.ToLocalTime().ToString("HH:mm:ss"), 11, muted);
                    var x = plot.Left + plot.Width * i / 4;
                    dc.DrawText(label, new Point(Math.Clamp(x - label.Width / 2, plot.Left - 20, width - label.Width), plot.Bottom + 6));
                }
            }

            var color = SeriesColor;
            var line = new Pen(new SolidColorBrush(color), 2.25) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            var fill = new LinearGradientBrush(Color.FromArgb(0x55, color.R, color.G, color.B), Color.FromArgb(0x00, color.R, color.G, color.B), 90);
            // Split into runs at real gaps so missing time is never drawn as data.
            var runs = new List<List<Point>>();
            List<Point>? run = null;
            DateTimeOffset? previous = null;
            foreach (var i in visible)
            {
                var p = points[i];
                if (run == null || previous is { } prev && (p.Time - prev).TotalSeconds > GapSeconds) { run = new(); runs.Add(run); }
                run.Add(new Point(X(p.Time), Y(p.Value)));
                previous = p.Time;
            }
            foreach (var r in runs)
            {
                if (r.Count == 1) { dc.DrawEllipse(new SolidColorBrush(color), null, r[0], 2.5, 2.5); continue; }
                var stroke = Smooth(r, false, plot.Bottom);
                var area = Smooth(r, true, plot.Bottom);
                dc.DrawGeometry(fill, null, area);
                dc.DrawGeometry(null, line, stroke);
            }

            // Latest point, pulse and value badge.
            var last = new Point(X(points[^1].Time), Y(points[^1].Value));
            if (State == ChartState.Live && Pulse > 0)
            {
                var ring = new SolidColorBrush(Color.FromArgb((byte)(110 * (1 - Pulse)), color.R, color.G, color.B));
                dc.DrawEllipse(ring, null, last, 5 + 10 * Pulse, 5 + 10 * Pulse);
            }
            dc.DrawEllipse(new SolidColorBrush(color), new Pen(Res("Surface"), 2), last, 5, 5);
            if (ShowAxisLabels)
            {
                var badgeText = Label(Format(BadgeValue), 12, Res("OnPrimary"), true);
                var bw = badgeText.Width + 16; var bh = badgeText.Height + 6;
                var bx = Math.Min(last.X - bw - 10, plot.Right - bw); var by = Math.Clamp(last.Y - bh / 2, 0, height - bh);
                if (bx < plot.Left) bx = plot.Left;
                dc.DrawRoundedRectangle(new SolidColorBrush(color), null, new Rect(bx, by, bw, bh), bh / 2, bh / 2);
                dc.DrawText(Label(Format(BadgeValue), 12, ContrastOn(color), true), new Point(bx + 8, by + 3));
            }

            // Crosshair from the mouse or the keyboard scrub position.
            int? focus = scrubIndex;
            if (focus == null && mouse is { } m && plot.Contains(m) && visible.Count > 0)
                focus = visible.OrderBy(i => Math.Abs(X(points[i].Time) - m.X)).First();
            hoverIndex = focus;
            if (focus is { } f && f < points.Count && points[f].Time >= start)
            {
                var p = points[f];
                var at = new Point(X(p.Time), Y(p.Value));
                var cross = new Pen(Res("Outline"), 1);
                dc.DrawLine(cross, new Point(at.X, plot.Top), new Point(at.X, plot.Bottom));
                dc.DrawEllipse(Res("Surface"), new Pen(new SolidColorBrush(color), 2), at, 4.5, 4.5);
                var tip = Label($"{p.Time.ToLocalTime():HH:mm:ss} · {Format(p.Value)}", 12, Res("OnSurface"));
                var tw = tip.Width + 16; var th = tip.Height + 8;
                var tx = Math.Clamp(at.X + 10, plot.Left, plot.Right - tw);
                if (at.X + 10 + tw > plot.Right) tx = Math.Max(plot.Left, at.X - 10 - tw);
                var tipBox = new Rect(tx, plot.Top, tw, th);
                dc.DrawRoundedRectangle(Res("SurfaceHighest"), new Pen(Res("OutlineVariant"), 1), tipBox, 8, 8);
                dc.DrawText(tip, new Point(tx + 8, plot.Top + 4));
            }
        }

        // Honest state overlays.
        if (points.Count == 0)
        {
            var text = Label(Loc.T(StateText), 14, muted);
            dc.DrawText(text, new Point(plot.Left + (plot.Width - text.Width) / 2, plot.Top + (plot.Height - text.Height) / 2));
        }
        else if (State is ChartState.Paused or ChartState.Stale or ChartState.Error)
        {
            var key = State == ChartState.Paused ? "SecondaryContainer" : State == ChartState.Stale ? "WarningContainer" : "ErrorContainer";
            var fg = State == ChartState.Paused ? "OnSecondaryContainer" : State == ChartState.Stale ? "OnWarningContainer" : "OnErrorContainer";
            var text = Label(Loc.T(StateText), 12, Res(fg), true);
            var r = new Rect(plot.Left + 8, plot.Top + 4, text.Width + 16, text.Height + 6);
            dc.DrawRoundedRectangle(Res(key), null, r, r.Height / 2, r.Height / 2);
            dc.DrawText(text, new Point(r.Left + 8, r.Top + 3));
        }

        if (IsKeyboardFocused)
            dc.DrawRoundedRectangle(null, new Pen(Res("Primary"), 2), new Rect(1, 1, width - 2, height - 2), 10, 10);
    }

    private static Brush ContrastOn(Color c)
    {
        var luminance = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
        return luminance > 0.55 ? Brushes.Black : Brushes.White;
    }

    /// <summary>Monotone-safe smoothing: Catmull-Rom segments with control points clamped to the segment's value range so the curve never overshoots real values.</summary>
    private static StreamGeometry Smooth(IReadOnlyList<Point> pts, bool closed, double baseline)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(closed ? new Point(pts[0].X, baseline) : pts[0], closed, closed);
            if (closed) ctx.LineTo(pts[0], false, false);
            for (var i = 0; i < pts.Count - 1; i++)
            {
                var p0 = pts[Math.Max(0, i - 1)]; var p1 = pts[i]; var p2 = pts[i + 1]; var p3 = pts[Math.Min(pts.Count - 1, i + 2)];
                var lo = Math.Min(p1.Y, p2.Y); var hi = Math.Max(p1.Y, p2.Y);
                var c1 = new Point(p1.X + (p2.X - p0.X) / 6, Math.Clamp(p1.Y + (p2.Y - p0.Y) / 6, lo, hi));
                var c2 = new Point(p2.X - (p3.X - p1.X) / 6, Math.Clamp(p2.Y - (p3.Y - p1.Y) / 6, lo, hi));
                ctx.BezierTo(c1, c2, p2, true, true);
            }
            if (closed) ctx.LineTo(new Point(pts[^1].X, baseline), false, false);
        }
        g.Freeze();
        return g;
    }
}

/// <summary>Tiny sparkline for container cards. Draws only real samples; shows nothing when there are fewer than two.</summary>
public sealed class Sparkline : FrameworkElement
{
    private IReadOnlyList<double> values = [];
    public string SeriesBrushKey { get; set; } = "Primary";
    public double MinimumCeiling { get; set; } = 1;
    public Sparkline() { Height = 28; IsHitTestVisible = false; }
    public void SetValues(IReadOnlyList<double> v) { values = v; InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        if (values.Count < 2 || ActualWidth < 4) return;
        var color = (TryFindResource(SeriesBrushKey) as SolidColorBrush)?.Color ?? Colors.SteelBlue;
        var max = Math.Max(MinimumCeiling, values.Max());
        var w = ActualWidth; var h = ActualHeight;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            for (var i = 0; i < values.Count; i++)
            {
                var p = new Point(w * i / (values.Count - 1), h - 2 - (h - 4) * Math.Clamp(values[i] / max, 0, 1));
                if (i == 0) ctx.BeginFigure(p, false, false); else ctx.LineTo(p, true, true);
            }
        }
        g.Freeze();
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(color), 1.5) { LineJoin = PenLineJoin.Round }, g);
    }
}
