using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using MaterialDockerImage.Services;
using MaterialDockerImage.Pages;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Shell;

/// <summary>
/// The per-element appearance editor. It opens beside the element being customized, edits that one element live (colour through the
/// infinite picker, corner radius, font size and weight, padding, visibility), keeps the overrides keyed by stable element identity,
/// and lists every override with reset per element and reset all. A property whose appearance setting is locked stays read-only here.
/// </summary>
public static class ElementEditor
{
    private static Action? closeOpen;

    public static bool IsOpen => closeOpen != null;
    public static void Close() => closeOpen?.Invoke();

    /// <summary>Opens the editor for an element, on the Element tab (or Overrides when <paramref name="overridesTab"/> is set).</summary>
    public static void Show(FrameworkElement target, bool overridesTab = false)
    {
        closeOpen?.Invoke();
        var host = AppHost.Current;
        if (host == null) return;
        var returnFocus = Keyboard.FocusedElement as IInputElement;
        var key = ElementAppearance.KeyFor(target);
        var style = ElementAppearance.Store.Get(key)?.Clone() ?? new ElementStyle();
        var opening = ElementAppearance.Store.Get(key)?.Clone();
        var changedSinceOpen = false;
        var content = new ContentControl { Focusable = false };
        var status = Ui.Text("", 12, null, "Error");
        var contrast = Ui.Text("", 12, null, "Warning");

        void Live()
        {
            changedSinceOpen = true;
            contrast.Text = style.ContrastWarning() is { } warning ? Loc.T(warning) : "";
            if (!ElementAppearance.Set(key, style, out var error, null, target)) status.Text = Loc.T(error); else status.Text = "";
        }

        bool Locked(string property) => ElementStyle.LockIdFor(property) is { } id && host.IsLocked(id);
        FrameworkElement LockedNote(string property) => Ui.Caption("Locked in Settings. Unlock it there to change it.").Margin(0, 2, 0, 0);

        // A switch that turns one override on or off, with its editor underneath while it is on.
        FrameworkElement Property(string property, string label, bool on, Action<bool> toggle, Func<FrameworkElement> editor)
        {
            var locked = Locked(property);
            var holder = new ContentControl { Focusable = false, Content = on ? editor() : null };
            var sw = Ui.Switch(label, on, v => { toggle(v); holder.Content = v ? editor() : null; Live(); });
            sw.IsEnabled = !locked;
            return Ui.VStack(6, sw, locked ? LockedNote(property) : null, holder);
        }

        FrameworkElement Slider(double min, double max, double value, string name, Action<double> set)
        {
            var label = Ui.Mono(value.ToString("0.#"), 12, "OnSurfaceVariant");
            var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), SmallChange = 1, LargeChange = 4, MinWidth = 220, IsMoveToPointEnabled = true };
            AutomationProperties.SetName(slider, Loc.T(name));
            slider.ValueChanged += (_, e) => { var v = Math.Round(e.NewValue, 1); label.Text = v.ToString("0.#"); set(v); Live(); };
            return Ui.Columns(8, [1, 0], slider, label);
        }

        FrameworkElement ColorEditor(Func<string?> get, Action<string> set, bool foreground)
        {
            var initial = AppearanceRgba.TryParseStorageHex(get(), out var c) ? c : ElementAppearance.CurrentColor(target, foreground);
            var picker = new AppearanceColorPicker(initial, true, [foreground ? "Surface" : "OnSurface"]);
            picker.ColorChanged += color => { set(color.ToStorageHex()); Live(); };
            return picker;
        }

        FrameworkElement ElementTab()
        {
            var parts = new List<UIElement?>
            {
                Property(nameof(ElementStyle.Background), "Override the background colour", style.Background != null,
                    v => style.Background = v ? ElementAppearance.CurrentColor(target, false).ToStorageHex() : null,
                    () => ColorEditor(() => style.Background, c => style.Background = c, false))
            };
            if (target is Control or TextBlock)
                parts.Add(Property(nameof(ElementStyle.Foreground), "Override the text colour", style.Foreground != null,
                    v => style.Foreground = v ? ElementAppearance.CurrentColor(target, true).ToStorageHex() : null,
                    () => ColorEditor(() => style.Foreground, c => style.Foreground = c, true)));
            if (ElementAppearance.HasRadius(target))
                parts.Add(Property(nameof(ElementStyle.CornerRadius), "Override the corner radius", style.CornerRadius != null,
                    v => style.CornerRadius = v ? Math.Round(ElementAppearance.CurrentRadius(target), 1) : null,
                    () => Slider(0, ElementStyle.MaxRadius, style.CornerRadius ?? 0, "Corner radius", v => style.CornerRadius = v)));
            if (ElementAppearance.HasFontProperties(target))
            {
                parts.Add(Property(nameof(ElementStyle.FontSize), "Override the font size", style.FontSize != null,
                    v => style.FontSize = v ? Math.Round(ElementAppearance.CurrentFontSize(target), 1) : null,
                    () => Slider(ElementStyle.MinFont, ElementStyle.MaxFont, style.FontSize ?? 14, "Font size", v => style.FontSize = v)));
                parts.Add(Property(nameof(ElementStyle.FontWeight), "Override the font weight", style.FontWeight != null,
                    v => style.FontWeight = v ? ElementStyle.Weights.OrderBy(w => Math.Abs(w - ElementAppearance.CurrentWeight(target))).First() : null,
                    () => Ui.Segmented(ElementStyle.Weights.Select(w => w.ToString()).ToList(), (style.FontWeight ?? 400).ToString(), v => { style.FontWeight = int.Parse(v); Live(); }, false)));
            }
            if (ElementAppearance.HasPaddingProperty(target))
                parts.Add(Property(nameof(ElementStyle.Padding), "Override the padding", style.Padding != null,
                    v => style.Padding = v ? Math.Round(ElementAppearance.CurrentPadding(target), 1) : null,
                    () => Slider(0, ElementStyle.MaxPadding, style.Padding ?? 0, "Padding", v => style.Padding = v)));
            var hide = Ui.Switch("Hide this element (it keeps its space)", style.Hidden, v => { style.Hidden = v; Live(); }, "Reset it from the Overrides tab or with Reset element if it disappears.");
            parts.Add(hide);
            parts.Add(contrast);
            var reset = Ui.Button("Reset element", Icons.RestartAlt, ButtonKind.Tonal, () =>
            {
                style = new ElementStyle(); changedSinceOpen = true;
                if (!ElementAppearance.Reset(key, out var error)) status.Text = Loc.T(error); else { status.Text = ""; content.Content = ElementTab(); }
            });
            parts.Add(reset);
            return Ui.VStack(12, parts.ToArray());
        }

        FrameworkElement OverridesTab()
        {
            var list = new StackPanel();
            var search = new SearchFilter("Search customized elements", 360) { Width = double.NaN };
            void Fill()
            {
                list.Children.Clear();
                var items = ElementAppearance.Store.Items.OrderBy(p => p.Key, StringComparer.Ordinal).Where(p => search.Predicate.Matches(p.Key + " " + p.Value.Describe())).ToList();
                if (ElementAppearance.Store.Count == 0) { list.Children.Add(Ui.Caption("No element has been customized yet. Right-click an element, choose Customize this element…, and its overrides will be listed here.")); return; }
                if (items.Count == 0) { list.Children.Add(Ui.Caption("No customized element matches this search.")); return; }
                foreach (var (itemKey, itemStyle) in items)
                {
                    var k = itemKey;
                    var row = Ui.Columns(8, [1, 0],
                        Ui.VStack(0, Ui.Text(ElementKey.Leaf(k), 13, FontWeights.SemiBold, "OnSurface", false, false), Ui.Caption(itemStyle.Describe()), Ui.Mono(k, 11, "OnSurfaceVariant", true)),
                        Ui.Button("Reset", Icons.RestartAlt, ButtonKind.Text, () =>
                        {
                            if (k == key) style = new ElementStyle();
                            if (!ElementAppearance.Reset(k, out var error)) status.Text = Loc.T(error); else Fill();
                        }));
                    list.Children.Add(Ui.Card(row, 10, "SurfaceLow", 12).Margin(0, 0, 0, 6));
                }
            }
            search.Changed += Fill;
            Fill();
            var resetAll = Ui.AsyncButton("Reset all customized elements", Icons.RestartAlt, ButtonKind.Danger, async () =>
            {
                if (ElementAppearance.Store.Count == 0) { host.Notify.Info("No element has been customized."); return; }
                if (!await host.Overlay.ConfirmAsync("Reset all customized elements?", $"{ElementAppearance.Store.Count} element(s) return to the theme's look. This cannot be undone.", "Reset all", true)) return;
                style = new ElementStyle();
                if (!ElementAppearance.ResetAll(out var error)) status.Text = Loc.T(error); else { status.Text = ""; Fill(); }
            });
            return Ui.VStack(10, search, list, resetAll);
        }

        Action? close = null;
        FrameworkElement Tab(string name) => name == "Overrides" ? OverridesTab() : ElementTab();
        var tabs = Ui.Segmented(["Element", "Overrides"], overridesTab ? "Overrides" : "Element", name => content.Content = Tab(name));
        content.Content = Tab(overridesTab ? "Overrides" : "Element");
        var revert = Ui.Button("Revert changes", Icons.Undo, ButtonKind.Text, () =>
        {
            if (opening != null) ElementAppearance.Set(key, opening, out _, null, target); else ElementAppearance.Reset(key, out _);
            style = opening?.Clone() ?? new ElementStyle(); changedSinceOpen = false;
            content.Content = ElementTab();
        });
        var done = Ui.Button("Done", Icons.Check, ButtonKind.Filled, () => close!());
        var header = new DockPanel();
        header.Children.Add(Ui.Icon(Icons.Palette, 22, true, "Primary").Margin(0, 0, 10, 0).Dock(Dock.Left));
        header.Children.Add(Ui.IconButton(Icons.Close, "Close element editor", () => close!()).Dock(Dock.Right));
        header.Children.Add(Ui.VStack(0, Ui.Text("Customize this element", 16, FontWeights.SemiBold), Ui.Text(ElementAppearance.TitleFor(target), 12, null, "OnSurfaceVariant", false, false)));
        var identity = Ui.Mono(key, 11, "OnSurfaceVariant", true);
        AutomationProperties.SetName(identity, Loc.T("Element identity"));
        var body = Ui.VStack(12, header, identity, tabs, content, status, Ui.Wrap(8, revert, done),
            Ui.Caption("Changes apply to this element at once and are saved. Escape closes the editor. The element is found again by its place in the window, so the override survives restarts."));
        var card = Ui.Card(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 640 }, 16, "SurfaceHigh", 16);
        card.Width = 440;
        card.Effect = Ui.Elevation(24, 6, .4);
        card.Margin = new Thickness(8, 0, 16, 16);
        KeyboardNavigation.SetTabNavigation(card, KeyboardNavigationMode.Cycle);
        AutomationProperties.SetName(card, Loc.T("Customize this element") + ": " + ElementAppearance.TitleFor(target));
        var popup = new Popup { PlacementTarget = target, Placement = PlacementMode.Right, StaysOpen = true, AllowsTransparency = true, Child = card };
        var window = Window.GetWindow(target);
        void Nudge(object? s, EventArgs e) { popup.HorizontalOffset += 0.1; popup.HorizontalOffset -= 0.1; }
        var closed = false;
        close = () =>
        {
            if (closed) return; closed = true;
            if (changedSinceOpen && AppHost.Current is { IsCapture: false })
                ElementAppearance.Set(key, style, out _, "customized " + ElementAppearance.TitleFor(target), target);
            if (window != null) { window.LocationChanged -= Nudge; window.SizeChanged -= Nudge; }
            popup.IsOpen = false;
            closeOpen = null;
            if (target.Focusable && target.IsVisible) target.Focus(); else returnFocus?.Focus();
        };
        card.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { close(); e.Handled = true; } };
        if (window != null) { window.LocationChanged += Nudge; window.SizeChanged += Nudge; }
        popup.Opened += (_, _) => card.Dispatcher.BeginInvoke(() => card.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)), System.Windows.Threading.DispatcherPriority.Input);
        closeOpen = close;
        Motion.ContainerTransform(card);
        popup.IsOpen = true;
    }
}
