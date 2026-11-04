using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using MaterialDockerImage.Services;

namespace MaterialDockerImage.UI;

public enum ButtonKind { Filled, Tonal, Outlined, Text, Danger }
public enum Tone { Neutral, Primary, Success, Warning, Error, Tertiary }

/// <summary>Small factory for Material 3 building blocks used by every page. All colours are dynamic resource roles.</summary>
public static class Ui
{
    public static T Bind<T>(this T element, DependencyProperty property, string resourceKey) where T : FrameworkElement { element.SetResourceReference(property, resourceKey); return element; }
    public static T Named<T>(this T element, string name) where T : DependencyObject { AutomationProperties.SetName(element, Loc.T(name)); return element; }
    public static T Tip<T>(this T element, string text) where T : FrameworkElement { element.ToolTip = Loc.T(text); return element; }
    public static T At<T>(this T element, int row = 0, int column = 0, int rowSpan = 1, int columnSpan = 1) where T : UIElement { Grid.SetRow(element, row); Grid.SetColumn(element, column); Grid.SetRowSpan(element, rowSpan); Grid.SetColumnSpan(element, columnSpan); return element; }
    public static T Margin<T>(this T element, double left, double top, double right, double bottom) where T : FrameworkElement { element.Margin = new Thickness(left, top, right, bottom); return element; }
    public static T Margin<T>(this T element, double uniform) where T : FrameworkElement { element.Margin = new Thickness(uniform); return element; }
    public static T Dock<T>(this T element, System.Windows.Controls.Dock dock) where T : UIElement { DockPanel.SetDock(element, dock); return element; }

    public static TextBlock Icon(string glyph, double size = 20, bool filled = false, string brush = "OnSurfaceVariant")
    {
        var t = new TextBlock { Text = glyph, FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, Focusable = false };
        t.SetResourceReference(TextBlock.FontFamilyProperty, filled ? "IconFontFilled" : "IconFont");
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    public static TextBlock Text(string text, double size = 14, FontWeight? weight = null, string brush = "OnSurface", bool wrap = true, bool translate = true)
    {
        var t = new TextBlock { Text = translate ? Loc.T(text) : text, FontSize = size, FontWeight = weight ?? FontWeights.Normal, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center, LineHeight = Math.Round(size * 1.35) };
        if (!wrap) t.TextTrimming = TextTrimming.CharacterEllipsis;
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }
    public static TextBlock Mono(string text, double size = 13, string brush = "OnSurface", bool wrap = false)
    {
        var t = Text(text, size, null, brush, wrap, false);
        t.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        return t;
    }
    public static TextBlock Title(string text, double size = 22)
    {
        var t = Text(text, size, FontWeights.SemiBold);
        t.SetResourceReference(TextBlock.FontWeightProperty, "HeadingWeight"); // heading weight is an appearance setting
        return t;
    }
    public static TextBlock Caption(string text) => Text(text, 13, null, "OnSurfaceVariant");
    public static TextBlock Overline(string text) => Text(text, 12, FontWeights.SemiBold, "OnSurfaceVariant");

    public static Button Button(string label, string? icon = null, ButtonKind kind = ButtonKind.Tonal, Action? onClick = null, bool translate = true)
    {
        var b = new Button { Style = (Style)Application.Current.FindResource("Btn." + kind) };
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (icon != null) { var glyph = Icon(icon, 18, kind == ButtonKind.Filled); glyph.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(Control.Foreground)) { Source = b }); glyph.Margin = new Thickness(-4, 0, 8, 0); content.Children.Add(glyph); }
        var text = new TextBlock { Text = translate ? Loc.T(label) : label, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(text);
        b.Content = content;
        AutomationProperties.SetName(b, text.Text);
        if (onClick != null) b.Click += (_, _) => Guard(onClick);
        return b;
    }
    public static Button AsyncButton(string label, string? icon, ButtonKind kind, Func<Task> onClick, bool translate = true)
    {
        var b = Button(label, icon, kind, null, translate);
        b.Click += async (_, _) => await RunGuarded(b, onClick);
        return b;
    }
    public static Button IconButton(string glyph, string name, Action? onClick = null, string brush = "OnSurfaceVariant")
    {
        var b = new Button { Style = (Style)Application.Current.FindResource("Btn.Icon"), Content = glyph, ToolTip = Loc.T(name) };
        b.SetResourceReference(Control.ForegroundProperty, brush);
        AutomationProperties.SetName(b, Loc.T(name));
        if (onClick != null) b.Click += (_, _) => Guard(onClick);
        return b;
    }
    public static Button AsyncIconButton(string glyph, string name, Func<Task> onClick)
    {
        var b = IconButton(glyph, name);
        b.Click += async (_, _) => await RunGuarded(b, onClick);
        return b;
    }
    /// <summary>Runs an async click handler with re-entry protection; the control stays disabled until the real result.</summary>
    public static async Task RunGuarded(UIElement control, Func<Task> action)
    {
        if (!control.IsEnabled) return;
        control.IsEnabled = false;
        try { await action(); }
        catch (Exception e) when (e is not OutOfMemoryException) { AppHost.Current?.Notify.Error(e.Message); }
        finally { control.IsEnabled = true; }
    }
    public static void Guard(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is not OutOfMemoryException) { AppHost.Current?.Notify.Error(e.Message); }
    }

    public static Border Card(UIElement child, double padding = 16, string brush = "SurfaceLow", double radius = 16)
    {
        var b = new Border { Child = child, Padding = new Thickness(padding), CornerRadius = new CornerRadius(radius) };
        b.SetResourceReference(Border.BackgroundProperty, brush);
        // Standard radii follow the corner radius scale of the appearance editor.
        if (RadiusKey(radius) is { } key) b.SetResourceReference(Border.CornerRadiusProperty, key);
        return b;
    }

    public static StackPanel VStack(double gap, params UIElement?[] children) => Stack(Orientation.Vertical, gap, children);
    public static StackPanel HStack(double gap, params UIElement?[] children) => Stack(Orientation.Horizontal, gap, children);
    public static StackPanel Stack(Orientation orientation, double gap, IEnumerable<UIElement?> children)
    {
        var panel = new StackPanel { Orientation = orientation };
        foreach (var child in children) Add(panel, child, gap);
        return panel;
    }
    public static void Add(Panel panel, UIElement? child, double gap)
    {
        if (child == null) return;
        if (panel.Children.Count > 0 && child is FrameworkElement fe)
        {
            var m = fe.Margin;
            var vertical = panel is StackPanel { Orientation: Orientation.Vertical };
            fe.Margin = vertical ? new Thickness(m.Left, m.Top + gap, m.Right, m.Bottom) : new Thickness(m.Left + gap, m.Top, m.Right, m.Bottom);
        }
        panel.Children.Add(child);
    }
    public static WrapPanel Wrap(double gap, params UIElement?[] children)
    {
        var panel = new WrapPanel();
        foreach (var child in children) { if (child is FrameworkElement fe) fe.Margin = new Thickness(0, 0, gap, gap); if (child != null) panel.Children.Add(child); }
        return panel;
    }

    /// <summary>Columns sized by star weights or fixed pixels; a negative number means fixed pixels.</summary>
    public static Grid Columns(double gap, double[] widths, params UIElement[] children)
    {
        var g = new Grid();
        for (var i = 0; i < widths.Length; i++)
        {
            if (i > 0 && gap > 0) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gap) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = widths[i] < 0 ? new GridLength(-widths[i]) : widths[i] == 0 ? GridLength.Auto : new GridLength(widths[i], GridUnitType.Star) });
        }
        for (var i = 0; i < children.Length && i < widths.Length; i++) { Grid.SetColumn(children[i], gap > 0 ? i * 2 : i); g.Children.Add(children[i]); }
        return g;
    }
    public static Grid Rows(double gap, double[] heights, params UIElement[] children)
    {
        var g = new Grid();
        for (var i = 0; i < heights.Length; i++)
        {
            if (i > 0 && gap > 0) g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(gap) });
            g.RowDefinitions.Add(new RowDefinition { Height = heights[i] < 0 ? new GridLength(-heights[i]) : heights[i] == 0 ? GridLength.Auto : new GridLength(heights[i], GridUnitType.Star) });
        }
        for (var i = 0; i < children.Length && i < heights.Length; i++) { Grid.SetRow(children[i], gap > 0 ? i * 2 : i); g.Children.Add(children[i]); }
        return g;
    }

    /// <summary>A labelled field with an optional info button directly above the target that opens target-specific guidance.</summary>
    public static StackPanel Field(string label, FrameworkElement control, string? info = null, string? example = null)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(Overline(label));
        if (info != null) header.Children.Add(Info(label, info, example));
        AutomationProperties.SetName(control, Loc.T(label));
        if (control is TextBox tb && tb.MinWidth < 1) tb.MinWidth = 60;
        var stack = new StackPanel();
        stack.Children.Add(header);
        control.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(control);
        return stack;
    }
    /// <summary>
    /// The one info-icon component: an icon button with the accessible name "About &lt;target&gt;", a tooltip that carries the explanation
    /// for hover and screen readers, and a popover with the same explanation and an optional worked example.
    /// </summary>
    public static Button Info(string target, string explanation, string? example = null)
    {
        var b = IconButton(Icons.Info, "About " + Loc.T(target));
        b.Width = b.Height = b.MinHeight = 28; b.FontSize = 16; b.Margin = new Thickness(4, -6, 0, -6);
        b.ToolTip = Loc.T(explanation);
        AutomationProperties.SetHelpText(b, Loc.T(explanation));
        b.Click += (_, _) =>
        {
            var body = VStack(10, Text(target, 16, FontWeights.SemiBold), Text(explanation, 14, null, "OnSurfaceVariant"));
            if (example != null) body.Children.Add(Card(VStack(6, Overline("Worked example"), Mono(example, 13, "OnSurface", true)), 12, "SurfaceContainer", 12).Margin(0, 10, 0, 0));
            ShowPopup(b, body, 380);
        };
        return b;
    }

    /// <summary>Older name for <see cref="Info"/>, kept so existing call sites read the same.</summary>
    public static Button InfoButton(string target, string explanation, string? example = null) => Info(target, explanation, example);

    /// <summary>An anchored popup that paints its own surface and closes on Escape or outside click, returning focus to the opener.</summary>
    public static Popup ShowPopup(FrameworkElement anchor, UIElement content, double width = 360)
    {
        var card = Card(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 520 }, 16, "SurfaceHigh", 16);
        card.Width = width;
        card.Effect = Elevation(18, 4, .35);
        card.Margin = new Thickness(0, 4, 12, 12);
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, Child = card, IsOpen = true };
        card.KeyDown += (_, e) => { if (e.Key == Key.Escape) { popup.IsOpen = false; e.Handled = true; } };
        popup.Closed += (_, _) => anchor.Focus();
        card.Focusable = true;
        card.Loaded += (_, _) => card.Focus();
        return popup;
    }

    public static CheckBox Switch(string label, bool value, Action<bool> changed, string? description = null)
    {
        var content = description == null ? (object)Loc.T(label) : VStack(2, Text(label), Caption(description));
        var c = new CheckBox { Style = (Style)Application.Current.FindResource("Switch"), Content = content, IsChecked = value };
        AutomationProperties.SetName(c, Loc.T(label));
        c.Checked += (_, _) => changed(true); c.Unchecked += (_, _) => changed(false);
        return c;
    }
    public static CheckBox Check(string label, bool value, Action<bool>? changed = null)
    {
        var c = new CheckBox { Content = Loc.T(label), IsChecked = value };
        if (changed != null) { c.Checked += (_, _) => changed(true); c.Unchecked += (_, _) => changed(false); }
        return c;
    }
    public static ToggleButton Chip(string label, bool value, Action<bool> changed)
    {
        var c = new ToggleButton { Style = (Style)Application.Current.FindResource("Chip"), Content = Loc.T(label), IsChecked = value };
        AutomationProperties.SetName(c, Loc.T(label));
        c.Checked += (_, _) => changed(true); c.Unchecked += (_, _) => changed(false);
        return c;
    }

    /// <summary>Material segmented button. Returns the container; selection changes call back with the option text.</summary>
    public static Border Segmented(IReadOnlyList<string> options, string selected, Action<string> changed, bool translate = true)
    {
        var group = "seg" + Guid.NewGuid().ToString("N");
        // Star columns fill a fixed width; with no width set they size equally to the widest label and its check mark.
        var grid = new Grid();
        for (var i = 0; i < options.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var option = options[i];
            var radius = options.Count == 1 ? new CornerRadius(20) : i == 0 ? new CornerRadius(20, 0, 0, 20) : i == options.Count - 1 ? new CornerRadius(0, 20, 20, 0) : new CornerRadius(0);
            var r = new RadioButton { Style = (Style)Application.Current.FindResource("Segment"), GroupName = group, Content = translate ? Loc.T(option) : option, IsChecked = option == selected, Tag = radius, BorderThickness = new Thickness(i == 0 ? 0 : 1, 0, 0, 0) };
            AutomationProperties.SetName(r, translate ? Loc.T(option) : option);
            r.Checked += (_, _) => changed(option);
            Grid.SetColumn(r, i);
            grid.Children.Add(r);
        }
        var border = new Border { Child = grid, CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1), ClipToBounds = true };
        border.SetResourceReference(Border.BorderBrushProperty, "Outline");
        return border;
    }

    public static Border Badge(string text, Tone tone = Tone.Neutral, bool translate = true)
    {
        var (bg, fg) = ToneKeys(tone);
        var label = Text(translate ? Loc.T(text) : text, 12, FontWeights.SemiBold, fg, false, false);
        var b = new Border { Child = label, CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(Border.BackgroundProperty, bg);
        return b;
    }
    public static (string Background, string Foreground) ToneKeys(Tone tone) => tone switch
    {
        Tone.Primary => ("PrimaryContainer", "OnPrimaryContainer"),
        Tone.Success => ("SuccessContainer", "OnSuccessContainer"),
        Tone.Warning => ("WarningContainer", "OnWarningContainer"),
        Tone.Error => ("ErrorContainer", "OnErrorContainer"),
        Tone.Tertiary => ("TertiaryContainer", "OnTertiaryContainer"),
        _ => ("SurfaceHigh", "OnSurfaceVariant")
    };

    public static FrameworkElement EmptyState(string icon, string title, string message, params UIElement[] actions)
    {
        var stack = VStack(8, Icon(icon, 40, false, "Outline"), Text(title, 18, FontWeights.SemiBold), Text(message, 14, null, "OnSurfaceVariant"));
        foreach (var t in stack.Children.OfType<TextBlock>()) t.TextAlignment = TextAlignment.Center;
        if (actions.Length > 0) { var row = HStack(8, actions); row.HorizontalAlignment = HorizontalAlignment.Center; row.Margin = new Thickness(0, 12, 0, 0); stack.Children.Add(row); }
        stack.MaxWidth = 440; stack.HorizontalAlignment = HorizontalAlignment.Center; stack.VerticalAlignment = VerticalAlignment.Center;
        return stack;
    }

    /// <summary>Page header: title, subtitle and right-aligned actions that wrap at narrow widths.</summary>
    public static Grid Header(string title, string subtitle, out TextBlock subtitleText, params UIElement[] actions)
    {
        subtitleText = Caption(subtitle);
        var left = VStack(2, Title(title), subtitleText);
        var right = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        foreach (var a in actions) { if (a is FrameworkElement fe) fe.Margin = new Thickness(8, 4, 0, 4); right.Children.Add(a); }
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 220 });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.Children.Add(left); g.Children.Add(right.At(0, 1));
        // An Auto column gives the actions unlimited width, so they can never wrap there. When the title's minimum and
        // the actions do not fit side by side, the actions move to their own row under the title, where they wrap.
        g.SizeChanged += (_, _) =>
        {
            right.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var stacked = right.DesiredSize.Width + 220 > g.ActualWidth;
            // The unconstrained measure above must not stand in for the real one in the next layout pass.
            right.InvalidateMeasure();
            if (stacked == (Grid.GetRow(right) == 1)) return;
            Grid.SetRow(right, stacked ? 1 : 0); Grid.SetColumn(right, stacked ? 0 : 1); Grid.SetColumnSpan(right, stacked ? 2 : 1);
            right.HorizontalAlignment = stacked ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            right.Margin = stacked ? new Thickness(-8, 8, 0, 0) : new Thickness(0);
        };
        return g;
    }

    public static TextBox Input(string text = "", string? name = null, bool mono = false)
    {
        var t = new TextBox { Text = text };
        if (mono) t.SetResourceReference(Control.FontFamilyProperty, "MonoFont");
        if (name != null) AutomationProperties.SetName(t, Loc.T(name));
        return t;
    }
    public static TextBox CodeBox(string text = "", double minHeight = 160)
    {
        var t = new TextBox { Style = (Style)Application.Current.FindResource("Field.Code"), Text = text, MinHeight = minHeight };
        return t;
    }
    public static ComboBox Combo(IEnumerable<string> items, string? selected = null, bool editable = false)
    {
        var list = items.ToList();
        var c = new ComboBox { ItemsSource = list, IsEditable = editable };
        if (selected != null) { if (list.Contains(selected)) c.SelectedItem = selected; else if (editable) c.Text = selected; }
        else if (list.Count > 0) c.SelectedIndex = 0;
        return c;
    }

    /// <summary>
    /// A list beside an inspector on wide layouts; below the breakpoint the inspector moves under the list so
    /// neither is squeezed.
    /// </summary>
    /// <summary>
    /// List beside its inspector from the breakpoint, otherwise stacked. When stacked on a short window and a summary is
    /// supplied, the inspector folds into a one-line strip with Show details, so the list keeps its rows.
    /// </summary>
    public static Grid SplitInspector(FrameworkElement list, FrameworkElement inspector, double breakpoint = 1100, double wideInspectorWidth = 400, double gap = 12, Func<string?>? summary = null, double compactBelowHeight = 560)
    {
        var grid = new Grid();
        grid.Children.Add(list); grid.Children.Add(inspector);
        var summaryText = Text("", 14, FontWeights.SemiBold, "OnSurface", false, false);
        var expanded = false;
        string? mode = null;
        var strip = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 6, 8, 6), Visibility = Visibility.Collapsed };
        Button toggle = null!;
        toggle = Button("Show details", Icons.ExpandLess, ButtonKind.Text, () => { expanded = !expanded; mode = null; Arrange(); });
        strip.Child = Columns(12, [1, 0], summaryText, toggle);
        strip.SetResourceReference(Border.BackgroundProperty, "SurfaceHigh");
        AutomationProperties.SetName(strip, Loc.T("Selected item summary"));
        grid.Children.Add(strip);
        void Arrange()
        {
            var wide = grid.ActualWidth <= 0 || grid.ActualWidth >= breakpoint;
            var compact = !wide && summary != null && grid.ActualHeight > 0 && grid.ActualHeight < compactBelowHeight;
            var now = wide ? "wide" : compact && !expanded ? "compact" : "stacked";
            if (compact) summaryText.Text = summary!() ?? Loc.T("Nothing selected");
            if (mode == now) return;
            mode = now;
            grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
            strip.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
            inspector.Visibility = now == "compact" ? Visibility.Collapsed : Visibility.Visible;
            toggle.Content = Loc.T(expanded ? "Hide details" : "Show details"); AutomationProperties.SetName(toggle, Loc.T(expanded ? "Hide details" : "Show details"));
            if (now == "wide")
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(wideInspectorWidth + gap) });
                list.At(0, 0); inspector.At(0, 1);
                inspector.Width = wideInspectorWidth; inspector.Margin = new Thickness(gap, 0, 0, 0);
            }
            else if (now == "compact")
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                list.At(0, 0); strip.At(1, 0); strip.Margin = new Thickness(0, gap, 0, 0);
            }
            else
            {
                // Stacked: the list keeps enough height for its header, filters and several full rows above any scroll bar.
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star), MinHeight = 240 });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 150 });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                list.At(0, 0); inspector.At(1, 0); strip.At(2, 0);
                inspector.Width = double.NaN; inspector.Margin = new Thickness(0, gap, 0, 0); strip.Margin = new Thickness(0, gap, 0, 0);
            }
        }
        grid.SizeChanged += (_, _) => Arrange();
        grid.Tag = (Action)(() => { if (mode == "compact" || expanded) summaryText.Text = summary?.Invoke() ?? Loc.T("Nothing selected"); });
        Arrange();
        return grid;
    }

    public static Border Divider(bool vertical = false)
    {
        var b = vertical ? new Border { Width = 1 } : new Border { Height = 1 };
        b.SetResourceReference(Border.BackgroundProperty, "SurfaceHigh");
        return b;
    }

    public static ScrollViewer Scroll(UIElement content, bool horizontal = false) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = horizontal ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled, Focusable = false };

    /// <summary>
    /// A menu item whose shortcut text comes from the registered <see cref="AppCommand"/> with this id, so the shown shortcut is always
    /// the working binding. Use this for anything that is also a command; hand-typed gestures on <see cref="MenuItem"/> are only for
    /// keys local to one list (Del, Enter, Alt+Up) and are checked by tests/UiState.
    /// </summary>
    public static MenuItem CommandMenuItem(string header, string? icon, string commandId, Action action, bool danger = false)
    {
        var m = MenuItem(header, icon, action, null, danger);
        void Refresh() { m.InputGestureText = AppHost.Current?.Commands.GestureText(commandId) ?? ""; }
        Refresh();
        // Menus are often built before the commands are registered, so the text is read again when the item is first shown.
        m.Loaded += (_, _) => Refresh();
        return m;
    }

    public static MenuItem MenuItem(string header, string? icon, Action action, string? gesture = null, bool danger = false)
    {
        var m = new MenuItem { Header = Loc.T(header), InputGestureText = gesture ?? "" };
        if (icon != null) m.Icon = Icon(icon, 18, false, danger ? "Error" : "OnSurfaceVariant");
        if (danger) m.SetResourceReference(Control.ForegroundProperty, "Error");
        m.Click += (_, _) => Guard(action);
        return m;
    }

    /// <summary>The shape resource for a standard corner radius (8, 12, 16, 20 or 28), or null for other values.</summary>
    public static string? RadiusKey(double radius) => radius switch { 8 => "RadiusSmall", 12 => "RadiusMedium", 16 => "RadiusLarge", 20 => "RadiusPill", 28 => "RadiusExtraLarge", _ => null };

    /// <summary>A shadow scaled by the elevation intensity from the appearance editor (none when it is 0).</summary>
    public static System.Windows.Media.Effects.DropShadowEffect? Elevation(double blur, double depth, double opacity)
    {
        var scale = Application.Current?.TryFindResource("ElevationOpacity") is double e ? e / 0.35 : 1;
        if (scale <= 0.001) return null;
        return new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = blur, ShadowDepth = depth, Opacity = Math.Clamp(opacity * scale, 0, 1) };
    }

    public static string FormatBytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var i = 0; while (bytes >= 1000 && i < units.Length - 1) { bytes /= 1000; i++; }
        return i == 0 ? $"{bytes:0} {units[i]}" : $"{bytes:0.##} {units[i]}";
    }
}
