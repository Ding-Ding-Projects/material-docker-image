using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// The infinite colour picker: a continuous saturation/value field, hue and alpha sliders, a translator that shows and
/// accepts HEX, HEX8, RGB, HSL, HSV, HWB, OKLCH and CMYK, contrast badges against the colour it is read with, an
/// OKLCH gamut warning, and an optional animated rainbow choice. Every part is keyboard operable.
/// </summary>
public sealed class AppearanceColorPicker : StackPanel
{
    /// <summary>Raised while the colour changes (dragging, typing, sliders).</summary>
    public event Action<AppearanceRgba>? ColorChanged;
    /// <summary>Raised when the rainbow choice is switched (only when the picker offers it).</summary>
    public event Action<bool>? RainbowChanged;

    private double hue, saturation, value, alpha = 1;
    private readonly AppearanceRgba original;
    private readonly bool allowAlpha;
    private readonly IReadOnlyList<string> contrastRoles;
    private readonly Border hueLayer = new();
    private readonly Grid field = new() { Height = 150, MinWidth = 200, Focusable = true, Cursor = Cursors.Cross, ClipToBounds = true };
    private readonly Canvas thumbLayer = new() { IsHitTestVisible = false };
    private readonly Ellipse thumb = new() { Width = 18, Height = 18, StrokeThickness = 2, Stroke = Brushes.White };
    private readonly Ellipse thumbInner = new() { Width = 14, Height = 14, StrokeThickness = 1, Stroke = Brushes.Black };
    private readonly Slider hueSlider = new() { Minimum = 0, Maximum = 360, SmallChange = 1, LargeChange = 15 };
    private readonly Slider alphaSlider = new() { Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 10 };
    private readonly Border alphaStrip = new() { Height = 10, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 4, 0, 0) };
    private readonly Border currentSwatch = new() { Width = 56, Height = 36, CornerRadius = new CornerRadius(0, 10, 10, 0) };
    private readonly TextBlock fieldState = Ui.Caption("");
    private readonly TextBlock warning = Ui.Text("", 12, null, "Warning");
    private readonly WrapPanel contrast = new();
    private readonly Dictionary<AppearanceColorFormat, TextBox> formatBoxes = new();
    private readonly CheckBox? rainbow;
    private bool updating;

    public AppearanceRgba Current => AppearanceRgba.FromHsv(hue, saturation, value, alpha);

    public AppearanceColorPicker(AppearanceRgba initial, bool allowAlpha, IReadOnlyList<string> contrastRoles, bool? rainbowOn = null, string rainbowLabel = "Animated rainbow")
    {
        original = initial; this.allowAlpha = allowAlpha; this.contrastRoles = contrastRoles;
        SetFrom(initial, keepHue: false);
        if (!allowAlpha) alpha = 1;

        // Saturation / value field.
        var white = new Rectangle { Fill = new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), 0) };
        var black = new Rectangle { Fill = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, 90) };
        thumbLayer.Children.Add(thumb); thumbLayer.Children.Add(thumbInner);
        var fieldFrame = new Border { CornerRadius = new CornerRadius(12), ClipToBounds = true, Child = field, BorderThickness = new Thickness(1) };
        fieldFrame.SetResourceReference(Border.BorderBrushProperty, "OutlineVariant");
        field.Children.Add(hueLayer); field.Children.Add(white); field.Children.Add(black); field.Children.Add(thumbLayer);
        AutomationProperties.SetName(field, Loc.T("Saturation and brightness"));
        AutomationProperties.SetHelpText(field, Loc.T("Drag, or use the arrow keys: left and right change saturation, up and down change brightness. Hold Shift for bigger steps."));
        field.MouseLeftButtonDown += (_, e) => { field.Focus(); field.CaptureMouse(); PickAt(e.GetPosition(field)); e.Handled = true; };
        field.MouseMove += (_, e) => { if (field.IsMouseCaptured) PickAt(e.GetPosition(field)); };
        field.MouseLeftButtonUp += (_, _) => field.ReleaseMouseCapture();
        field.SizeChanged += (_, _) => PlaceThumb();
        field.KeyDown += FieldKeys;
        field.GotKeyboardFocus += (_, _) => fieldFrame.BorderThickness = new Thickness(2);
        field.LostKeyboardFocus += (_, _) => fieldFrame.BorderThickness = new Thickness(1);
        field.FocusVisualStyle = null;

        // Hue strip above its slider.
        var hueStops = new GradientStopCollection();
        for (var i = 0; i <= 6; i++) { var c = AppearanceRgba.FromHsv(i * 60, 1, 1); hueStops.Add(new GradientStop(System.Windows.Media.Color.FromRgb(c.R8, c.G8, c.B8), i / 6.0)); }
        var hueStrip = new Border { Height = 10, CornerRadius = new CornerRadius(5), Background = new LinearGradientBrush(hueStops, 0), Margin = new Thickness(0, 4, 0, 0) };
        AutomationProperties.SetName(hueSlider, Loc.T("Hue"));
        AutomationProperties.SetName(alphaSlider, Loc.T("Opacity"));
        hueSlider.ValueChanged += (_, _) => { if (updating) return; hue = hueSlider.Value; Changed(); };
        alphaSlider.ValueChanged += (_, _) => { if (updating) return; alpha = alphaSlider.Value / 100; Changed(); };

        // Original and current swatches over a checkerboard so alpha is visible.
        var originalSwatch = new Border { Width = 56, Height = 36, CornerRadius = new CornerRadius(10, 0, 0, 10), Background = new SolidColorBrush(ToWpf(original)), ToolTip = Loc.T("Original colour"), Cursor = Cursors.Hand };
        originalSwatch.MouseLeftButtonUp += (_, _) => Revert();
        var swatches = new Border { CornerRadius = new CornerRadius(10), Background = Checkerboard(), Child = Ui.HStack(0, originalSwatch, currentSwatch), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left };
        swatches.SetResourceReference(Border.BorderBrushProperty, "Outline");
        var revert = Ui.Button("Revert", Icons.Undo, ButtonKind.Text, Revert);
        revert.ToolTip = Loc.T("Back to the colour this picker opened with");

        // Translator: every format, editable, with copy.
        var translator = new StackPanel();
        foreach (var format in Enum.GetValues<AppearanceColorFormat>())
        {
            if (!allowAlpha && format == AppearanceColorFormat.Hex8) continue;
            var box = Ui.Input("", FormatName(format) + " value", true);
            box.MinWidth = 200;
            var f = format;
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(f, box.Text); e.Handled = true; } };
            box.LostKeyboardFocus += (_, _) => Commit(f, box.Text);
            formatBoxes[format] = box;
            var copy = Ui.IconButton(Icons.ContentCopy, "Copy " + FormatName(format), () => { Clipboard.SetText(box.Text); AppHost.Current?.Notify.Info(FormatName(f) + " copied: " + box.Text); });
            copy.Width = copy.Height = copy.MinHeight = 32; copy.FontSize = 16;
            translator.Children.Add(Ui.Columns(8, [-64, 1, 0], Ui.Text(FormatName(format), 12, FontWeights.SemiBold, "OnSurfaceVariant", false, false), box, copy).Margin(0, 2, 0, 2));
        }
        var any = Ui.Input("", "Paste any colour", true);
        any.ToolTip = Loc.T("Paste or type any supported format, then press Enter");
        any.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(null, any.Text); e.Handled = true; } };

        if (rainbowOn != null)
        {
            rainbow = Ui.Switch(rainbowLabel, rainbowOn.Value, on => RainbowChanged?.Invoke(on), "Cycles the hue continuously. With Reduced motion, Low stimulation or motion Off it stays on the chosen hue.");
        }

        Children.Add(Ui.Columns(16, [1, 1],
            Ui.VStack(10, fieldFrame, fieldState, Ui.VStack(0, hueStrip, hueSlider), allowAlpha ? Ui.VStack(0, alphaStrip, alphaSlider) : null, Ui.HStack(8, swatches, revert), rainbow),
            Ui.VStack(6, Ui.Overline("Translator"), translator, Ui.Field("Any format", any, "Accepts #RGB, #RRGGBB, #RRGGBBAA (alpha last), rgb(), rgba(), hsl(), hsv(), hwb(), oklch() and cmyk(). OKLCH colours outside sRGB are mapped to the nearest displayable colour, with a warning.", "oklch(62% 0.19 262)"), warning, Ui.Overline("Contrast").Margin(0, 6, 0, 0), contrast)));
        // Narrow layouts stack the two halves.
        var grid = (Grid)Children[0];
        void Arrange()
        {
            var narrow = ActualWidth > 0 && ActualWidth < 560;
            grid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            grid.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(16);
            grid.ColumnDefinitions[2].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            var right = grid.Children[1];
            if (narrow) { if (grid.RowDefinitions.Count == 0) { grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); } Grid.SetColumn(right, 0); Grid.SetRow(right, 1); ((FrameworkElement)right).Margin = new Thickness(0, 16, 0, 0); }
            else { Grid.SetColumn(right, 2); Grid.SetRow(right, 0); ((FrameworkElement)right).Margin = new Thickness(0); }
        }
        SizeChanged += (_, _) => Arrange();
        Refresh(false);
    }

    public static string FormatName(AppearanceColorFormat f) => f switch { AppearanceColorFormat.Hex8 => "HEX8", AppearanceColorFormat.Oklch => "OKLCH", _ => f.ToString().ToUpperInvariant() };
    public static Color ToWpf(AppearanceRgba c) => System.Windows.Media.Color.FromArgb(c.A8, c.R8, c.G8, c.B8);
    public static AppearanceRgba FromWpf(Color c) => AppearanceRgba.FromBytes(c.R, c.G, c.B, c.A);

    /// <summary>Sets the colour from outside (for example after undo) without raising <see cref="ColorChanged"/>.</summary>
    public void SetColor(AppearanceRgba color) { SetFrom(color, true); Refresh(false); }
    public void SetRainbow(bool on) { if (rainbow != null && rainbow.IsChecked != on) { updating = true; rainbow.IsChecked = on; updating = false; } }

    private void SetFrom(AppearanceRgba c, bool keepHue)
    {
        var (h, s, v) = c.ToHsv();
        if (!keepHue || s > 0.001 && v > 0.001) hue = h;
        saturation = s; value = v; alpha = allowAlpha ? c.A : 1;
    }

    private void Revert() { SetFrom(original, false); warning.Text = ""; Changed(); }

    private void PickAt(Point p)
    {
        if (field.ActualWidth <= 0 || field.ActualHeight <= 0) return;
        saturation = Math.Clamp(p.X / field.ActualWidth, 0, 1);
        value = 1 - Math.Clamp(p.Y / field.ActualHeight, 0, 1);
        Changed();
    }

    private void FieldKeys(object sender, KeyEventArgs e)
    {
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.1 : 0.01;
        switch (e.Key)
        {
            case Key.Left: saturation = Math.Max(0, saturation - step); break;
            case Key.Right: saturation = Math.Min(1, saturation + step); break;
            case Key.Up: value = Math.Min(1, value + step); break;
            case Key.Down: value = Math.Max(0, value - step); break;
            case Key.Home: saturation = 0; break;
            case Key.End: saturation = 1; break;
            default: return;
        }
        e.Handled = true;
        Changed();
    }

    private void Commit(AppearanceColorFormat? format, string text)
    {
        if (updating) return;
        if (format is { } f && text == Current.Format(f)) return; // unchanged
        AppearanceRgba parsed;
        string? gamut = null;
        var ok = format is AppearanceColorFormat.Hex or AppearanceColorFormat.Hex8 ? AppearanceRgba.TryParseHex(text, out parsed) : AppearanceRgba.TryParse(text, out parsed, out gamut);
        if (!ok) { warning.Text = Loc.T("That value is not a colour in a supported format. Nothing changed."); return; }
        warning.Text = gamut ?? "";
        if (!allowAlpha && parsed.A8 != 255) { warning.Text = Loc.T("This colour cannot be translucent, so its opacity was set to 100%."); parsed = parsed.WithAlpha(1); }
        SetFrom(parsed, true);
        Changed(keepWarning: true);
    }

    private void Changed(bool keepWarning = false)
    {
        if (!keepWarning) warning.Text = "";
        Refresh(true);
    }

    private void Refresh(bool raise)
    {
        updating = true;
        var c = Current;
        var pure = AppearanceRgba.FromHsv(hue, 1, 1);
        hueLayer.Background = new SolidColorBrush(ToWpf(pure));
        hueSlider.Value = hue;
        alphaSlider.Value = Math.Round(alpha * 100);
        currentSwatch.Background = new SolidColorBrush(ToWpf(c));
        alphaStrip.Background = new LinearGradientBrush(ToWpf(c.WithAlpha(0)), ToWpf(c.WithAlpha(1)), 0);
        foreach (var (format, box) in formatBoxes) if (!box.IsKeyboardFocused) box.Text = c.Format(format);
        fieldState.Text = $"{Loc.T("Saturation")} {saturation * 100:0}% · {Loc.T("Brightness")} {value * 100:0}% · {Loc.T("Hue")} {hue:0}°";
        AutomationProperties.SetItemStatus(field, fieldState.Text);
        PlaceThumb();
        RenderContrast(c);
        updating = false;
        if (raise) ColorChanged?.Invoke(c);
    }

    private void PlaceThumb()
    {
        var (w, h) = (field.ActualWidth, field.ActualHeight);
        if (w <= 0 || h <= 0) return;
        var x = saturation * w; var y = (1 - value) * h;
        Canvas.SetLeft(thumb, x - 9); Canvas.SetTop(thumb, y - 9);
        Canvas.SetLeft(thumbInner, x - 7); Canvas.SetTop(thumbInner, y - 7);
    }

    private void RenderContrast(AppearanceRgba c)
    {
        contrast.Children.Clear();
        foreach (var role in contrastRoles.Distinct())
        {
            if (Application.Current.TryFindResource(role) is not SolidColorBrush brush) continue;
            var other = FromWpf(brush.Color);
            var ratio = AppearanceRgba.ContrastRatio(c.Over(other.WithAlpha(1)), other.WithAlpha(1));
            var (verdict, tone) = ratio >= 7 ? ("AAA", Tone.Success) : ratio >= 4.5 ? ("AA", Tone.Success) : ratio >= 3 ? ("AA large text only", Tone.Warning) : ("Below 3:1", Tone.Error);
            var badge = Ui.Badge($"{ratio:0.00}:1 {Loc.T("against")} {role} · {verdict}", tone);
            AutomationProperties.SetName(badge, $"Contrast {ratio:0.00} to 1 against {role}, {verdict}");
            badge.Margin = new Thickness(0, 0, 6, 6);
            contrast.Children.Add(badge);
        }
    }

    private static DrawingBrush Checkerboard()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 12, 12))));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC)), null, new GeometryGroup { Children = { new RectangleGeometry(new Rect(0, 0, 6, 6)), new RectangleGeometry(new Rect(6, 6, 6, 6)) } }));
        var brush = new DrawingBrush(group) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 12, 12), ViewportUnits = BrushMappingMode.Absolute };
        brush.Freeze();
        return brush;
    }
}
