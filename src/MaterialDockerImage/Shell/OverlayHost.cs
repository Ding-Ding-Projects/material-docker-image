using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Shell;

/// <summary>In-window modal surfaces (dialogs, palette, super confirmation). They render inside the window so they are part of captures.</summary>
public sealed class OverlayHost : Grid
{
    private readonly Stack<(FrameworkElement Layer, IInputElement? ReturnFocus, Action Cancel)> layers = new();
    public bool IsOpen => layers.Count > 0;
    public event Action? Changed;

    public OverlayHost()
    {
        Visibility = Visibility.Collapsed;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && layers.Count > 0) { layers.Peek().Cancel(); e.Handled = true; } };
    }

    /// <summary>Shows a card centred over a scrim. Returns a handle used to close it.</summary>
    public Action Show(FrameworkElement content, double width = 560, Action? onCancel = null, VerticalAlignment vertical = VerticalAlignment.Center, double maxHeightRatio = .86)
    {
        var scrim = new Border();
        scrim.SetResourceReference(Border.BackgroundProperty, "Scrim");
        var card = Ui.Card(content is ScrollViewer ? content : Ui.Scroll(content), 24, "SurfaceContainer", 28);
        card.Width = width;
        double HostWidth() => ActualWidth > 0 ? ActualWidth : (Parent as FrameworkElement)?.ActualWidth > 0 ? ((FrameworkElement)Parent).ActualWidth : Window.GetWindow(this)?.ActualWidth ?? 1440;
        double HostHeight() => ActualHeight > 0 ? ActualHeight : (Parent as FrameworkElement)?.ActualHeight > 0 ? ((FrameworkElement)Parent).ActualHeight : Window.GetWindow(this)?.ActualHeight ?? 960;
        void Fit() { card.MaxWidth = Math.Max(320, HostWidth() - 48); card.MaxHeight = Math.Max(240, HostHeight() * maxHeightRatio); }
        card.HorizontalAlignment = HorizontalAlignment.Center;
        card.VerticalAlignment = vertical;
        card.Margin = new Thickness(24, vertical == VerticalAlignment.Top ? 72 : 24, 24, 24);
        card.Effect = Ui.Elevation(30, 8, .4);
        KeyboardNavigation.SetTabNavigation(card, KeyboardNavigationMode.Cycle);
        AutomationProperties.SetIsDialog(card, true);
        var layer = new Grid();
        layer.Children.Add(scrim);
        layer.Children.Add(card);
        SizeChanged += (_, _) => Fit();
        Fit();
        // Keyboard focus is empty while the window is inactive; logical focus still names the element to return to.
        var focus = Keyboard.FocusedElement ?? (Window.GetWindow(this) is { } owner ? FocusManager.GetFocusedElement(owner) : null);
        var closed = false;
        void Close()
        {
            if (closed) return; closed = true;
            Children.Remove(layer);
            var list = layers.ToList(); list.RemoveAll(l => l.Layer == layer); layers.Clear(); foreach (var l in Enumerable.Reverse(list)) layers.Push(l);
            if (layers.Count == 0) Visibility = Visibility.Collapsed;
            focus?.Focus();
            Changed?.Invoke();
        }
        void Cancel() { onCancel?.Invoke(); Close(); }
        scrim.MouseLeftButtonDown += (_, _) => Cancel();
        layers.Push((layer, focus, Cancel));
        Children.Add(layer);
        Visibility = Visibility.Visible;
        // Material container transform: the dialog grows in over a fading scrim. Snaps when motion is off.
        Motion.FadeIn(scrim);
        Motion.ContainerTransform(card);
        card.Loaded += (_, _) => { Fit(); var first = FindFocusable(content); (first ?? card).Focus(); };
        Changed?.Invoke();
        return Close;
    }

    /// <summary>Cancels every open surface, newest first.</summary>
    public void CloseAll() { while (layers.Count > 0) layers.Peek().Cancel(); }

    private static IInputElement? FindFocusable(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBox { IsEnabled: true, IsReadOnly: false } t) return t;
            var nested = FindFocusable(child);
            if (nested != null) return nested;
        }
        return null;
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "Continue", bool danger = false)
    {
        var done = new TaskCompletionSource<bool>();
        Action? close = null;
        var cancel = Ui.Button("Cancel", null, ButtonKind.Text, () => { done.TrySetResult(false); close!(); });
        var ok = Ui.Button(confirmLabel, null, danger ? ButtonKind.Danger : ButtonKind.Filled, () => { done.TrySetResult(true); close!(); });
        var actions = Ui.HStack(8, cancel, ok); actions.HorizontalAlignment = HorizontalAlignment.Right;
        var emoji = Loc.Emoji(danger ? "danger" : "question");
        var body = Ui.VStack(16, Ui.Text((emoji.Length > 0 ? emoji + " " : "") + Loc.T(title), 22, FontWeights.SemiBold, "OnSurface", true, false), Ui.Text(Loc.T(message), 14, null, "OnSurfaceVariant", true, false), actions);
        close = Show(body, 520, () => done.TrySetResult(false));
        cancel.Loaded += (_, _) => cancel.Focus();
        return done.Task;
    }

    public Task<string?> PromptAsync(string title, string label, string initial = "", string? help = null, Func<string, string?>? validate = null)
    {
        var done = new TaskCompletionSource<string?>();
        Action? close = null;
        var input = Ui.Input(initial, label);
        var error = Ui.Text("", 13, null, "Error");
        void Apply()
        {
            var value = input.Text.Trim();
            var problem = value.Length == 0 ? "Enter a value." : value.StartsWith('-') ? "Values cannot begin with a hyphen." : validate?.Invoke(value);
            if (problem != null) { error.Text = Loc.T(problem); return; }
            done.TrySetResult(value); close!();
        }
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) Apply(); };
        var actions = Ui.HStack(8, Ui.Button("Cancel", null, ButtonKind.Text, () => { done.TrySetResult(null); close!(); }), Ui.Button("Apply", null, ButtonKind.Filled, Apply));
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        var body = Ui.VStack(14, Ui.Title(title), help == null ? null : Ui.Caption(help), Ui.Field(label, input), error, actions);
        close = Show(body, 520, () => done.TrySetResult(null));
        return done.Task;
    }

    /// <summary>
    /// Two-key super confirmation: names the action and affected data, needs both keys before the slider enables,
    /// acts only when the slider reaches the end, and always offers an emergency exit.
    /// </summary>
    public Task<bool> SuperConfirmAsync(string title, string action, IReadOnlyList<string> affected)
    {
        var done = new TaskCompletionSource<bool>();
        Action? close = null;
        var key1 = Ui.Switch("Key 1", false, _ => { });
        var key2 = Ui.Switch("Key 2", false, _ => { });
        key1.Content = null; key2.Content = null;
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = 0, IsEnabled = false, IsMoveToPointEnabled = false, Height = 56, Tag = Loc.T("Slide to confirm"), Template = PillSliderTemplate() };
        AutomationProperties.SetName(slider, Loc.T("Slide to confirm"));
        AutomationProperties.SetHelpText(slider, Loc.T("Drag the slider all the way across to confirm. This extra step stops a destructive action from happening by accident."));
        var state = Ui.Text("Turn on both keys to unlock the slider.", 12, null, "OnSurfaceVariant");
        // A key card: tonal when off, filled container when on. Clicking the card toggles its switch.
        Border keyCard(string label, CheckBox box)
        {
            var icon = Ui.Icon(Icons.Key, 20, false, "OnSurfaceVariant");
            var name = Ui.Text(label, 14, FontWeights.SemiBold, "OnSurface", false);
            var card = new Border { Height = 56, CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 0, 14, 0), Child = Ui.Columns(10, [0, 1, 0], icon, name, box), Cursor = Cursors.Hand };
            card.SetResourceReference(Border.BackgroundProperty, "SurfaceLow");
            card.MouseLeftButtonDown += (_, e) => { if (!IsInside(e.OriginalSource as DependencyObject, box)) { box.IsChecked = box.IsChecked != true; e.Handled = true; } };
            void Paint()
            {
                var on = box.IsChecked == true;
                card.SetResourceReference(Border.BackgroundProperty, on ? "PrimaryContainer" : "SurfaceLow");
                icon.SetResourceReference(TextBlock.ForegroundProperty, on ? "OnPrimaryContainer" : "OnSurfaceVariant");
                name.SetResourceReference(TextBlock.ForegroundProperty, on ? "OnPrimaryContainer" : "OnSurface");
            }
            box.Checked += (_, _) => Paint(); box.Unchecked += (_, _) => Paint();
            return card;
        }
        var card1 = keyCard("Key 1", key1); var card2 = keyCard("Key 2", key2);
        void Update()
        {
            var both = key1.IsChecked == true && key2.IsChecked == true;
            slider.IsEnabled = both;
            if (!both) slider.Value = 0;
            state.Text = both ? Loc.T("Both keys on. Slide all the way to the end to confirm.") : key1.IsChecked == true || key2.IsChecked == true ? Loc.T("One key on. Turn on the other key.") : Loc.T("Turn on both keys to unlock the slider.");
        }
        key1.Checked += (_, _) => Update(); key1.Unchecked += (_, _) => Update(); key2.Checked += (_, _) => Update(); key2.Unchecked += (_, _) => Update();
        slider.ValueChanged += (_, _) =>
        {
            if (slider.Value >= 100 && key1.IsChecked == true && key2.IsChecked == true) { state.Text = Loc.T("Confirmed."); done.TrySetResult(true); close!(); }
        };
        slider.LostMouseCapture += (_, _) => { if (slider.Value < 100) slider.Value = 0; };
        var list = Ui.VStack(6, affected.Take(40).Select(a => (UIElement)Ui.Text(a, 13, null, "OnSurface", true, false)).ToArray());
        if (affected.Count > 40) list.Children.Add(Ui.Text(Loc.T("…and {0} more.").Replace("{0}", (affected.Count - 40).ToString()), 13, null, "OnSurfaceVariant", true, false).Margin(0, 6, 0, 0));
        var exit = Ui.Button("Emergency exit", Icons.ExitToApp, ButtonKind.Tonal, () => { done.TrySetResult(false); close!(); });
        var emoji = Loc.Emoji("danger");
        var escKey = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(6, 0, 6, 0), Child = Ui.Text("Esc", 12, null, "OnSurfaceVariant", false, false), VerticalAlignment = VerticalAlignment.Center };
        escKey.SetResourceReference(Border.BorderBrushProperty, "OutlineVariant");
        var escHint = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        escHint.Children.Add(Ui.Text("Press", 12, null, "OnSurfaceVariant", false)); escHint.Children.Add(escKey); escHint.Children.Add(Ui.Text("to cancel without changing anything.", 12, null, "OnSurfaceVariant", false));
        var warn = Ui.Icon(Icons.Warning, 26, true, "Error");
        warn.VerticalAlignment = VerticalAlignment.Top; warn.Margin = new Thickness(0, 2, 0, 0);
        var head = Ui.Columns(12, [0, 1], warn, Ui.VStack(4,
            Ui.Text((emoji.Length > 0 ? emoji + " " : "") + Loc.T(title), 22, FontWeights.SemiBold, "OnSurface", true, false),
            Ui.Text(Loc.T(action), 13, null, "OnSurfaceVariant", true, false)));
        var affectedCard = Ui.Card(Ui.VStack(6, Ui.Overline("Affected"), list), 12, "SurfaceLow", 12);
        affectedCard.Padding = new Thickness(16, 12, 16, 12);
        var body = Ui.VStack(16,
            head,
            affectedCard,
            Ui.Columns(10, [1, 1], card1, card2),
            Ui.VStack(6, slider, state),
            Ui.Columns(12, [1, 0], escHint, exit));
        close = Show(body, 560, () => done.TrySetResult(false));
        exit.Loaded += (_, _) => exit.Focus();
        return done.Task;
    }

    private static bool IsInside(DependencyObject? node, DependencyObject ancestor)
    {
        for (; node != null; node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (ReferenceEquals(node, ancestor)) return true;
        return false;
    }

    /// <summary>Pill-shaped slide-to-confirm track: 56 px tall, dashed outline while locked, a 48 px round thumb that carries a lock glyph. Behaviour is the ordinary Slider's.</summary>
    private static ControlTemplate PillSliderTemplate() => (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
        "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Slider'>" +
        "<Grid Height='56'>" +
        "<Border x:Name='pill' CornerRadius='28' Background='{DynamicResource SurfaceLow}'/>" +
        "<Rectangle x:Name='edge' RadiusX='28' RadiusY='28' Stroke='{DynamicResource Outline}' StrokeThickness='1' StrokeDashArray='4 3'/>" +
        "<TextBlock x:Name='lbl' Text='{TemplateBinding Tag}' HorizontalAlignment='Center' VerticalAlignment='Center' Foreground='{DynamicResource OnSurfaceVariant}'/>" +
        "<Track x:Name='PART_Track' Margin='4,0,4,0' Height='48' VerticalAlignment='Center'>" +
        "<Track.DecreaseRepeatButton><RepeatButton Focusable='False' Command='Slider.DecreaseLarge'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>" +
        "<Track.IncreaseRepeatButton><RepeatButton Focusable='False' Command='Slider.IncreaseLarge'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>" +
        "<Track.Thumb><Thumb Width='48' Height='48'><Thumb.Template><ControlTemplate TargetType='Thumb'>" +
        "<Grid><Ellipse x:Name='dot' Fill='{DynamicResource SurfaceHighest}'/><TextBlock x:Name='glyph' Text='&#xE899;' FontFamily='{DynamicResource IconFont}' FontSize='22' HorizontalAlignment='Center' VerticalAlignment='Center' Foreground='{DynamicResource OnSurfaceVariant}'/></Grid>" +
        "<ControlTemplate.Triggers><Trigger Property='IsEnabled' Value='True'><Setter TargetName='dot' Property='Fill' Value='{DynamicResource Primary}'/><Setter TargetName='glyph' Property='Foreground' Value='{DynamicResource OnPrimary}'/><Setter TargetName='glyph' Property='Text' Value='&#xE898;'/></Trigger></ControlTemplate.Triggers>" +
        "</ControlTemplate></Thumb.Template></Thumb></Track.Thumb>" +
        "</Track>" +
        "</Grid>" +
        "<ControlTemplate.Triggers><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='edge' Property='Stroke' Value='{DynamicResource Primary}'/></Trigger><Trigger Property='IsEnabled' Value='True'><Setter TargetName='edge' Property='StrokeDashArray' Value='{x:Null}'/><Setter TargetName='lbl' Property='Foreground' Value='{DynamicResource OnSurface}'/></Trigger></ControlTemplate.Triggers>" +
        "</ControlTemplate>");
}
