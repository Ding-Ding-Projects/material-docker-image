using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MaterialDockerImage.Pages;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Shell;

/// <summary>
/// The floating "Edit appearance…" panel. It is non-modal, opens beside the element it edits, focuses itself, and
/// returns focus to that element when closed (Escape or Close). It edits the element's colour roles with the infinite
/// picker, offers per-role reset, copy and paste of the element's style, undo and redo, quick theme and density, and
/// a link to the full editor in Settings.
/// </summary>
public static class AppearancePanel
{
    private static Popup? open;
    private static Action? closeOpen;

    /// <summary>Opens the panel for an element. <paramref name="openEditor"/> shows the full Settings editor.</summary>
    public static void Show(AppearanceTargeting.Target target, Action openEditor)
    {
        closeOpen?.Invoke();
        var host = AppHost.Current;
        if (host == null) return;
        var returnFocus = Keyboard.FocusedElement as IInputElement;
        var roles = target.Roles.Where(AppearanceRoles.IsKnown).ToList();
        var selected = roles.FirstOrDefault() ?? "Primary";
        var pickerHost = new ContentControl { Focusable = false };
        var roleList = new StackPanel();
        AppearanceColorPicker? picker = null;
        var own = false;

        AppearanceRgba Resolved(string key) => Application.Current.TryFindResource(key) is SolidColorBrush b ? AppearanceColorPicker.FromWpf(b.Color) : new AppearanceRgba(0, 0, 0);
        void Change(string label, Action<AppearanceState> m, bool coalesce = false) { own = true; AppearanceRuntime.Change(label, m, coalesce); own = false; }

        void RenderRoles()
        {
            roleList.Children.Clear();
            var group = "panelroles" + Guid.NewGuid().ToString("N");
            var overrides = AppearanceRuntime.Current.Roles;
            foreach (var key in roles)
            {
                var role = AppearanceRoles.Find(key)!;
                var swatch = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 10, 0) };
                swatch.SetResourceReference(Border.BackgroundProperty, key);
                swatch.SetResourceReference(Border.BorderBrushProperty, "Outline");
                var label = Ui.HStack(0, swatch, Ui.VStack(0, Ui.Text(role.Name, 13, null, "OnSurface", false), Ui.Mono(key + (overrides.TryGetValue(key, out var v) ? " · " + v : " · theme"), 11, "OnSurfaceVariant")));
                var radio = new RadioButton { GroupName = group, Content = label, IsChecked = key == selected, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 4) };
                AutomationProperties.SetName(radio, role.Name);
                radio.Checked += (_, _) => { selected = key; ShowPicker(); };
                roleList.Children.Add(radio);
            }
        }

        void ShowPicker()
        {
            var role = AppearanceRoles.Find(selected)!;
            var overrides = AppearanceRuntime.Current.Roles;
            picker = new AppearanceColorPicker(Resolved(selected), true, [AppearanceRoles.ContrastPartner(selected), "Surface"], overrides.TryGetValue(selected, out var v) && v == AppearanceRoles.Rainbow);
            var key = selected;
            picker.ColorChanged += c => Change($"{key} colour set to {c.ToStorageHex()}", m => m.Roles[key] = c.ToStorageHex(), true);
            picker.RainbowChanged += on => { Change($"{key} rainbow {(on ? "on" : "off")}", m => { if (on) m.Roles[key] = AppearanceRoles.Rainbow; else m.Roles.Remove(key); }); RenderRoles(); };
            var reset = Ui.Button("Reset role", Icons.RestartAlt, ButtonKind.Text, () => { Change(key + " reset to the theme colour", m => m.Roles.Remove(key)); RenderRoles(); ShowPicker(); });
            reset.IsEnabled = overrides.ContainsKey(key);
            pickerHost.Content = Ui.VStack(8, Ui.Columns(8, [1, 0], Ui.Text(role.Name, 14, FontWeights.SemiBold), reset), picker);
        }

        // Style clipboard for this element: its role colours, in order, so they can be pasted onto another element.
        var copy = Ui.Button("Copy style", Icons.ContentCopy, ButtonKind.Text, () =>
        {
            var lines = roles.Select(k => k + "=" + (AppearanceRuntime.Current.Roles.TryGetValue(k, out var v) ? v : Resolved(k).ToStorageHex()));
            ActionSounds.Copy("material-docker-image.element-style\n" + string.Join("\n", lines));
            host.Notify.Info($"Copied the style of this {target.Title.ToLowerInvariant()} ({roles.Count} colour roles).");
        });
        var paste = Ui.Button("Paste style", Icons.ContentCopy, ButtonKind.Text, () =>
        {
            string text;
            try { text = Clipboard.ContainsText() ? Clipboard.GetText() : ""; } catch (System.Runtime.InteropServices.ExternalException) { text = ""; }
            var lines = text.Replace("\r\n", "\n").Split('\n');
            if (lines.Length < 2 || lines[0] != "material-docker-image.element-style" || lines.Length > 40) { host.Notify.Info("The clipboard does not hold an element style. Use Copy style on another element first."); return; }
            var values = new List<string>();
            foreach (var line in lines.Skip(1).Where(l => l.Length > 0))
            {
                var value = line[(line.IndexOf('=') + 1)..].Trim();
                if (!AppearanceState.IsRoleValue(value)) { host.Notify.Error("The copied style contains an invalid colour, nothing changed."); return; }
                values.Add(value);
            }
            // Colours are applied by position: the first copied role onto this element's first role, and so on.
            Change($"pasted style onto {target.Title.ToLowerInvariant()}", m => { for (var i = 0; i < Math.Min(values.Count, roles.Count); i++) m.Roles[roles[i]] = values[i]; });
            RenderRoles(); ShowPicker();
        });
        var undo = Ui.IconButton(Icons.Undo, "Undo appearance change", () => AppearanceRuntime.Undo());
        var redo = Ui.IconButton(Icons.Redo, "Redo appearance change", () => AppearanceRuntime.Redo());
        void SyncUndo() { undo.IsEnabled = AppearanceRuntime.CanUndo; redo.IsEnabled = AppearanceRuntime.CanRedo; }
        void Sync()
        {
            SyncUndo();
            if (own) return;
            RenderRoles();
            picker?.SetColor(Resolved(selected));
        }
        SyncUndo();
        Action? close = null;
        var closeButton = Ui.IconButton(Icons.Close, "Close appearance panel", () => close!());
        var full = Ui.Button("Open full editor", Icons.OpenInNew, ButtonKind.Tonal, () => { close!(); openEditor(); });
        var current = AppearanceRuntime.Current;
        var quick = Ui.VStack(8,
            Ui.Field("Theme", Ui.Segmented(AppearanceState.Themes, current.Theme, v => Change("theme set to " + v, m => m.Theme = v))),
            Ui.Field("Density", Ui.Segmented(AppearanceState.Densities, current.Density, v => Change("density set to " + v, m => m.Density = v))));
        var header = new DockPanel();
        header.Children.Add(Ui.Icon(Icons.Palette, 22, true, "Primary").Margin(0, 0, 10, 0).Dock(Dock.Left));
        header.Children.Add(Ui.HStack(0, undo, redo, closeButton).Dock(Dock.Right));
        header.Children.Add(Ui.VStack(0, Ui.Text("Edit appearance", 16, FontWeights.SemiBold), Ui.Caption(target.Title + " · " + Loc.T("changes apply everywhere this colour role is used"))));
        RenderRoles();
        ShowPicker();
        var body = Ui.VStack(12, header, Ui.Overline("Colour roles used here"), roleList, pickerHost, Ui.Wrap(8, copy, paste, full), quick, Ui.Caption("Escape closes this panel and returns to the element. Everything here can be undone."));
        var card = Ui.Card(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 640 }, 16, "SurfaceHigh", 16);
        card.Width = 440;
        card.Effect = Ui.Elevation(24, 6, .4);
        card.Margin = new Thickness(8, 0, 16, 16);
        KeyboardNavigation.SetTabNavigation(card, KeyboardNavigationMode.Cycle);
        AutomationProperties.SetName(card, Loc.T("Edit appearance") + ": " + target.Title);
        var popup = new Popup { PlacementTarget = target.Element, Placement = PlacementMode.Right, StaysOpen = true, AllowsTransparency = true, Child = card };
        var window = Window.GetWindow(target.Element);
        void Nudge(object? s, EventArgs e) { popup.HorizontalOffset += 0.1; popup.HorizontalOffset -= 0.1; }
        var closed = false;
        close = () =>
        {
            if (closed) return; closed = true;
            AppearanceRuntime.Flush();
            AppearanceRuntime.Changed -= Sync;
            if (window != null) { window.LocationChanged -= Nudge; window.SizeChanged -= Nudge; }
            popup.IsOpen = false;
            open = null; closeOpen = null;
            if (target.Element.Focusable && target.Element.IsVisible) target.Element.Focus(); else returnFocus?.Focus();
        };
        card.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { close(); e.Handled = true; } };
        AppearanceRuntime.Changed += Sync;
        if (window != null) { window.LocationChanged += Nudge; window.SizeChanged += Nudge; }
        popup.Opened += (_, _) => card.Dispatcher.BeginInvoke(() => { card.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)); }, System.Windows.Threading.DispatcherPriority.Input);
        open = popup; closeOpen = close;
        Motion.ContainerTransform(card);
        popup.IsOpen = true;
    }

    public static bool IsOpen => open?.IsOpen == true;
    public static void Close() => closeOpen?.Invoke();
}
