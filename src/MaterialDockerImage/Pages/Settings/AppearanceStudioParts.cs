using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;
using WpfPath = System.Windows.Shapes.Path;
using WpfEllipse = System.Windows.Shapes.Ellipse;

namespace MaterialDockerImage.Pages;

/// <summary>Small building blocks of the appearance studio: selectable tiles, gradient sliders, reflowing grids and drawings.</summary>
internal static class StudioParts
{
    private const string Xmlns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";
    private static ControlTemplate? tileTemplate, gradientTemplate;
    private static ImageSource? hueRing;

    /// <summary>A selectable surface (preset, motion level, corner shape). Its corners follow the corner radius setting.</summary>
    public static Button Tile(UIElement content, string name, Action click)
    {
        tileTemplate ??= (ControlTemplate)XamlReader.Parse(
            "<ControlTemplate TargetType=\"Button\" " + Xmlns + "><Grid>" +
            "<Border Background=\"{TemplateBinding Background}\" BorderBrush=\"{TemplateBinding BorderBrush}\" BorderThickness=\"{TemplateBinding BorderThickness}\" CornerRadius=\"{DynamicResource RadiusMedium}\" SnapsToDevicePixels=\"True\"/>" +
            "<Border x:Name=\"state\" Background=\"Transparent\" CornerRadius=\"{DynamicResource RadiusMedium}\"/>" +
            "<ContentPresenter Margin=\"{TemplateBinding Padding}\" HorizontalAlignment=\"{TemplateBinding HorizontalContentAlignment}\" VerticalAlignment=\"{TemplateBinding VerticalContentAlignment}\"/>" +
            "</Grid><ControlTemplate.Triggers>" +
            "<Trigger Property=\"IsMouseOver\" Value=\"True\"><Setter TargetName=\"state\" Property=\"Background\" Value=\"{DynamicResource HoverLayer}\"/></Trigger>" +
            "<Trigger Property=\"IsPressed\" Value=\"True\"><Setter TargetName=\"state\" Property=\"Background\" Value=\"{DynamicResource PressLayer}\"/></Trigger>" +
            "<Trigger Property=\"IsEnabled\" Value=\"False\"><Setter Property=\"Opacity\" Value=\"0.38\"/></Trigger>" +
            "</ControlTemplate.Triggers></ControlTemplate>");
        var b = new Button
        {
            Template = tileTemplate, Content = content, Cursor = Cursors.Hand, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center,
            FocusVisualStyle = Application.Current.TryFindResource("FocusRing") as Style
        };
        b.SetResourceReference(Control.ForegroundProperty, "OnSurface");
        AutomationProperties.SetName(b, Loc.T(name));
        b.Click += (_, _) => click();
        Select(b, false);
        return b;
    }

    /// <summary>Shows a tile as chosen (primary outline on a raised surface) or not, and tells assistive technology.</summary>
    public static void Select(Button tile, bool on, double padding = 10)
    {
        tile.SetResourceReference(Control.BackgroundProperty, on ? "SurfaceHigh" : "SurfaceContainer");
        tile.SetResourceReference(Control.BorderBrushProperty, on ? "Primary" : "SurfaceHigh");
        tile.BorderThickness = new Thickness(on ? 2 : 1);
        tile.Padding = new Thickness(on ? padding - 1 : padding);
        AutomationProperties.SetItemStatus(tile, on ? Loc.T("Selected") : "");
        tile.Tag = on;
    }

    /// <summary>A slider drawn over a colour gradient (hue, chroma, tone). It keeps the standard slider keyboard and automation behaviour.</summary>
    public static Slider GradientSlider(double min, double max, double value, double step, string name)
    {
        gradientTemplate ??= (ControlTemplate)XamlReader.Parse(
            "<ControlTemplate TargetType=\"Slider\" " + Xmlns + "><Grid Background=\"Transparent\">" +
            "<Border x:Name=\"focus\" Height=\"20\" CornerRadius=\"10\" BorderThickness=\"2\" BorderBrush=\"Transparent\" Margin=\"-4,0\"/>" +
            "<Border Height=\"12\" CornerRadius=\"6\" Background=\"{TemplateBinding Background}\"/>" +
            "<Track x:Name=\"PART_Track\" VerticalAlignment=\"Center\">" +
            "<Track.DecreaseRepeatButton><RepeatButton Command=\"Slider.DecreaseLarge\" Focusable=\"False\"><RepeatButton.Template><ControlTemplate TargetType=\"RepeatButton\"><Border Background=\"Transparent\"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>" +
            "<Track.IncreaseRepeatButton><RepeatButton Command=\"Slider.IncreaseLarge\" Focusable=\"False\"><RepeatButton.Template><ControlTemplate TargetType=\"RepeatButton\"><Border Background=\"Transparent\"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>" +
            "<Track.Thumb><Thumb Width=\"20\" Height=\"20\"><Thumb.Template><ControlTemplate TargetType=\"Thumb\"><Ellipse Fill=\"{DynamicResource OnSurface}\" Stroke=\"{DynamicResource Surface}\" StrokeThickness=\"2\"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>" +
            "</Track></Grid><ControlTemplate.Triggers>" +
            "<Trigger Property=\"IsKeyboardFocused\" Value=\"True\"><Setter TargetName=\"focus\" Property=\"BorderBrush\" Value=\"{DynamicResource Primary}\"/></Trigger>" +
            "<Trigger Property=\"IsEnabled\" Value=\"False\"><Setter Property=\"Opacity\" Value=\"0.38\"/></Trigger>" +
            "</ControlTemplate.Triggers></ControlTemplate>");
        var s = new Slider { Template = gradientTemplate, Minimum = min, Maximum = max, Value = value, SmallChange = step, LargeChange = step * 10, MinHeight = 32, IsMoveToPointEnabled = true, Margin = new Thickness(4, 0, 4, 0) };
        AutomationProperties.SetName(s, Loc.T(name));
        return s;
    }

    public static LinearGradientBrush Gradient(IEnumerable<AppearanceRgba> colours)
    {
        var list = colours.ToList();
        var stops = new GradientStopCollection();
        for (var i = 0; i < list.Count; i++) stops.Add(new GradientStop(AppearanceColorPicker.ToWpf(list[i]), list.Count == 1 ? 0 : i / (double)(list.Count - 1)));
        var brush = new LinearGradientBrush(stops, 0);
        brush.Freeze();
        return brush;
    }

    /// <summary>A hue wheel drawn once into a bitmap (WPF has no conic gradient); it is decoration around the seed swatch.</summary>
    public static ImageSource HueRing()
    {
        if (hueRing != null) return hueRing;
        const int px = 192;
        var data = new byte[px * px * 4];
        const double centre = px / 2.0;
        for (var y = 0; y < px; y++)
            for (var x = 0; x < px; x++)
            {
                double dx = x + 0.5 - centre, dy = y + 0.5 - centre, d = Math.Sqrt(dx * dx + dy * dy);
                var a = Math.Clamp(centre - d, 0, 1);
                if (a <= 0) continue;
                var hue = Math.Atan2(dx, -dy) * 180 / Math.PI;
                var c = AppearanceRgba.FromHsv(AppearanceRgba.Wrap(hue), 0.55, 1);
                var i = (y * px + x) * 4;
                data[i] = (byte)(c.B8 * a); data[i + 1] = (byte)(c.G8 * a); data[i + 2] = (byte)(c.R8 * a); data[i + 3] = (byte)(255 * a);
            }
        var bitmap = new WriteableBitmap(px, px, 192, 192, PixelFormats.Pbgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, px, px), data, px * 4, 0);
        bitmap.Freeze();
        return hueRing = bitmap;
    }

    /// <summary>A pill-shaped status label with an icon, coloured by tone.</summary>
    public static Border StatusBadge(string text, string icon, Tone tone)
    {
        var (bg, fg) = Ui.ToneKeys(tone);
        var b = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(8, 0, 10, 0), MinHeight = 24, VerticalAlignment = VerticalAlignment.Center, Child = Ui.HStack(4, Ui.Icon(icon, 14, true, fg), Ui.Text(text, 12, FontWeights.SemiBold, fg, false, false)) };
        b.SetResourceReference(Border.BackgroundProperty, bg);
        AutomationProperties.SetName(b, text);
        return b;
    }

    /// <summary>A fixed-size swatch filled from a colour role, so it follows every appearance change.</summary>
    public static Border RoleSwatch(string role, double size, double radius)
    {
        var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(1) };
        b.SetResourceReference(Border.BackgroundProperty, role);
        b.SetResourceReference(Border.BorderBrushProperty, "OutlineVariant");
        return b;
    }

    public static Border ColourSwatch(Color colour, double size, double radius)
    {
        var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(radius), Background = new SolidColorBrush(colour), BorderThickness = new Thickness(1) };
        b.SetResourceReference(Border.BorderBrushProperty, "OutlineVariant");
        return b;
    }
}

/// <summary>Lays its items out side by side in fixed or star columns when wide enough, and stacks them in one column otherwise.</summary>
internal sealed class StudioReflow : Grid
{
    private readonly double breakpoint, gap;
    private readonly double[] widths;
    private readonly UIElement[] items;
    private bool? wide;

    public StudioReflow(double breakpoint, double gap, double[] widths, params UIElement[] items)
    {
        this.breakpoint = breakpoint; this.gap = gap; this.widths = widths; this.items = items;
        foreach (var item in items) Children.Add(item);
        SizeChanged += (_, e) => Arrange(e.NewSize.Width);
        Arrange(0);
    }

    private void Arrange(double width)
    {
        var now = width >= breakpoint;
        if (wide == now) return;
        wide = now;
        ColumnDefinitions.Clear(); RowDefinitions.Clear();
        for (var i = 0; i < items.Length; i++)
        {
            if (now)
            {
                if (i > 0) ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gap) });
                var w = widths[Math.Min(i, widths.Length - 1)];
                ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(-w) : w == 0 ? GridLength.Auto : new GridLength(w, GridUnitType.Star) });
                SetRow(items[i], 0); SetColumn(items[i], i * 2);
            }
            else
            {
                if (i > 0) RowDefinitions.Add(new RowDefinition { Height = new GridLength(gap) });
                RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                SetRow(items[i], i * 2); SetColumn(items[i], 0);
            }
        }
    }
}

/// <summary>Equal columns whose count follows the available width (at least one), with a gap between items.</summary>
internal sealed class StudioColumns : UniformGrid
{
    private readonly double minimum, gap;
    private readonly int maximum;

    public StudioColumns(double minimum, double gap, int maximum = 12)
    {
        this.minimum = minimum; this.gap = gap; this.maximum = maximum;
        Columns = 1;
        Margin = new Thickness(0, 0, -gap, -gap);
        SizeChanged += (_, e) => Columns = Math.Clamp((int)((e.NewSize.Width) / (minimum + gap)), 1, Math.Max(1, Math.Min(maximum, Children.Count)));
    }

    public void Add(FrameworkElement item)
    {
        item.Margin = new Thickness(0, 0, gap, gap);
        Children.Add(item);
        if (ActualWidth > 0) Columns = Math.Clamp((int)(ActualWidth / (minimum + gap)), 1, Math.Max(1, Math.Min(maximum, Children.Count)));
    }
}

/// <summary>A segmented choice that rebuilds itself when the saved value changes elsewhere (another control, undo or a preset).</summary>
internal sealed class StudioChoice : ContentControl
{
    private readonly IReadOnlyList<string> options;
    private readonly Func<string> read;
    private readonly Action<string> write;
    private string shown = "";

    public StudioChoice(IReadOnlyList<string> options, Func<string> read, Action<string> write)
    {
        this.options = options; this.read = read; this.write = write;
        Focusable = false;
        Build(read());
    }

    public void Sync() { var now = read(); if (now != shown) Build(now); }

    private void Build(string value)
    {
        shown = value;
        Content = Ui.Segmented(options, value, v => { shown = v; write(v); });
    }
}

/// <summary>Draws a Material easing token as a cubic Bézier curve, or a step when motion is not running.</summary>
internal sealed class StudioEasingCurve : Border
{
    private readonly WpfPath curve = new() { StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly WpfEllipse start = new() { Width = 8, Height = 8 }, end = new() { Width = 8, Height = 8 };

    public StudioEasingCurve()
    {
        Width = 200; Height = 120;
        BorderThickness = new Thickness(1);
        SetResourceReference(BackgroundProperty, "SurfaceHigh");
        SetResourceReference(BorderBrushProperty, "OutlineVariant");
        SetResourceReference(CornerRadiusProperty, "RadiusMedium");
        curve.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Primary");
        start.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Tertiary");
        end.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Tertiary");
        var canvas = new Canvas { Width = 200, Height = 120, ClipToBounds = true };
        canvas.Children.Add(curve); canvas.Children.Add(start); canvas.Children.Add(end);
        Canvas.SetLeft(start, 12); Canvas.SetTop(start, 100);
        Canvas.SetLeft(end, 180); Canvas.SetTop(end, 12);
        Child = canvas;
    }

    public void Show((double X1, double Y1, double X2, double Y2) token, bool animates, string description)
    {
        const double x0 = 16, y0 = 104, w = 168, h = 88;
        var figure = new PathFigure { StartPoint = new Point(x0, y0) };
        if (animates) figure.Segments.Add(new BezierSegment(new Point(x0 + w * token.X1, y0 - h * token.Y1), new Point(x0 + w * token.X2, y0 - h * token.Y2), new Point(x0 + w, y0 - h), true));
        else { figure.Segments.Add(new LineSegment(new Point(x0, y0 - h), true)); figure.Segments.Add(new LineSegment(new Point(x0 + w, y0 - h), true)); }
        curve.Data = new PathGeometry([figure]);
        AutomationProperties.SetName(this, description);
        ToolTip = description;
    }
}
