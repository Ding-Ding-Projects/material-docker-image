using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MaterialDockerImage.Pages;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Services;

/// <summary>
/// Per-element appearance overrides. An element is identified by its path through the visual tree
/// (see <see cref="ElementKey"/>), overrides are kept in element-appearance.json in the data folder, and they are applied to live
/// elements: to every element already on screen when something changes, and to each element as it loads. An element that loses its
/// override gets its original values back exactly.
/// </summary>
public static class ElementAppearance
{
    private sealed record Saved(DependencyObject Target, DependencyProperty Property, object Original);
    private static readonly ConditionalWeakTable<FrameworkElement, List<Saved>> Touched = new();
    private static ElementAppearanceStore? store;
    private static bool initialised;
    private static string? loadProblem;
    private static string Path_ => System.IO.Path.Combine(UserSettings.DataDirectory, "element-appearance.json");

    /// <summary>Raised after any set, reset or reload.</summary>
    public static event Action? Changed;
    /// <summary>A problem reading the file; while set, edits are refused so the file is never overwritten silently.</summary>
    public static string? LoadProblem => loadProblem;

    public static ElementAppearanceStore Store
    {
        get
        {
            if (store != null) return store;
            try { store = ElementAppearanceStore.Load(Path_); loadProblem = null; }
            catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException) { store = new(); loadProblem = e.Message; }
            return store;
        }
    }

    /// <summary>Registers the load hook once so elements created later (a page opened after the override was saved) are styled too.</summary>
    public static void Initialize()
    {
        if (initialised) return;
        initialised = true;
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) =>
        {
            if (store == null || store.Count == 0 || s is not FrameworkElement fe) return;
            ApplyTo(fe);
        }));
        _ = Store; // load now so the first element to load already sees the overrides
    }

    // ----- Identity -----

    public static string KeyFor(FrameworkElement element)
    {
        var chain = new List<DependencyObject>();
        for (DependencyObject? node = element; node != null; node = Parent(node)) chain.Add(node);
        chain.Reverse();
        var path = new List<ElementKeySegment>();
        for (var i = 0; i < chain.Count; i++)
        {
            var node = chain[i];
            var type = node.GetType().Name;
            var name = node is FrameworkElement fe ? (string.IsNullOrEmpty(AutomationProperties.GetAutomationId(fe)) ? fe.Name : AutomationProperties.GetAutomationId(fe)) : null;
            var index = 0;
            if (i > 0 && Parent(node) is { } parent)
            {
                foreach (var sibling in Children(parent)) { if (ReferenceEquals(sibling, node)) break; if (sibling.GetType() == node.GetType()) index++; }
            }
            path.Add(new ElementKeySegment(type, name, index));
        }
        return ElementKey.Build(path);
    }

    private static DependencyObject? Parent(DependencyObject node) => node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);

    private static IEnumerable<DependencyObject> Children(DependencyObject parent)
    {
        var count = parent is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetChildrenCount(parent) : 0;
        for (var i = 0; i < count; i++) yield return VisualTreeHelper.GetChild(parent, i);
    }

    /// <summary>A friendly title such as "Button · btn.build" for lists and headings.</summary>
    public static string TitleFor(FrameworkElement element)
    {
        var id = AutomationProperties.GetAutomationId(element);
        var name = AutomationProperties.GetName(element);
        var label = !string.IsNullOrEmpty(name) ? name : !string.IsNullOrEmpty(id) ? id : element.Name;
        var type = element.GetType().Name;
        return string.IsNullOrEmpty(label) ? type : type + " · " + (label.Length > 40 ? label[..40] + "…" : label);
    }

    // ----- Editing -----

    /// <summary>Stores (or removes, when empty) the override and re-applies. Returns false with a reason when it cannot be saved.</summary>
    public static bool Set(string key, ElementStyle style, out string error, string? historyLabel = null, FrameworkElement? onlyThis = null)
    {
        error = "";
        if (loadProblem != null) { error = "element-appearance.json could not be read (" + loadProblem + "). Move or fix the file first."; return false; }
        style = Guarded(key, style, out var heldBack);
        if (heldBack.Count > 0) error = LockedMessage;
        if (!Store.Set(key, style) && !(style.Normalized().IsEmpty)) { error = ElementKey.IsValid(key) ? $"At most {ElementAppearanceStore.MaxItems} elements can be customised. Reset one first." : "That element cannot be identified."; return false; }
        var saved = Persist(historyLabel, out var persistError, onlyThis);
        if (!saved || persistError.Length > 0) error = persistError;
        return saved;
    }

    /// <summary>The style that may be stored for an element: every property a lock covers keeps its existing value.</summary>
    private static ElementStyle Guarded(string key, ElementStyle proposed, out IReadOnlyList<string> heldBack)
    {
        heldBack = Array.Empty<string>();
        if (AppHost.Current is not { } host) return proposed;
        return AppearanceLocks.Guard(host.Settings, key, Store.Get(key), proposed, out heldBack);
    }

    private const string LockedMessage = "Locked properties kept their values. Unlock them to change them.";

    public static bool Reset(string key, out string error)
    {
        error = "";
        var kept = Guarded(key, new ElementStyle(), out var heldBack);
        if (heldBack.Count > 0) { Store.Set(key, kept); error = LockedMessage; } else Store.Reset(key);
        var saved = Persist("element reset", out var persistError);
        if (!saved || persistError.Length > 0) error = persistError;
        return saved && heldBack.Count == 0;
    }

    public static bool ResetAll(out string error)
    {
        error = "";
        var protectedKeys = new List<string>();
        foreach (var (key, _) in Store.Items.ToList())
        {
            var kept = Guarded(key, new ElementStyle(), out var heldBack);
            if (heldBack.Count > 0) { Store.Set(key, kept); protectedKeys.Add(key); } else Store.Reset(key);
        }
        var saved = Persist("all elements reset", out var persistError);
        if (!saved || persistError.Length > 0) error = persistError;
        else if (protectedKeys.Count > 0) error = "Some elements keep locked properties. Unlock them to reset them.";
        return saved && protectedKeys.Count == 0;
    }

    private static bool Persist(string? historyLabel, out string error, FrameworkElement? onlyThis = null)
    {
        error = "";
        try { Store.Save(Path_); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { error = "The element appearance could not be saved: " + e.Message; return false; }
        if (historyLabel != null && AppHost.Current is { IsCapture: false } host)
            host.History.Record("appearance", "Element appearance: " + historyLabel, new Dictionary<string, string> { ["settings/element-appearance.json"] = File.ReadAllText(Path_) });
        if (onlyThis != null) ApplyTo(onlyThis); else ApplyEverywhere();
        Changed?.Invoke();
        return true;
    }

    // ----- Applying -----

    /// <summary>Re-applies every override to every open window, and restores elements whose override is gone.</summary>
    public static void ApplyEverywhere()
    {
        if (Application.Current == null) return;
        foreach (Window window in Application.Current.Windows) Walk(window);
    }

    private static void Walk(DependencyObject node)
    {
        if (node is FrameworkElement fe) ApplyTo(fe);
        var count = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetChildrenCount(node) : 0;
        for (var i = 0; i < count; i++) Walk(VisualTreeHelper.GetChild(node, i));
    }

    /// <summary>Restores the element's original values, then applies its override if it has one.</summary>
    public static void ApplyTo(FrameworkElement element)
    {
        if (Touched.TryGetValue(element, out var saved))
        {
            foreach (var s in saved) Restore(s);
            saved.Clear();
        }
        var style = Store.Count == 0 ? null : Store.Get(KeyFor(element));
        if (style == null) return;
        var list = Touched.GetOrCreateValue(element);
        void Put(DependencyObject target, DependencyProperty property, object value)
        {
            list.Add(new Saved(target, property, target.ReadLocalValue(property)));
            target.SetValue(property, value);
        }
        if (style.Background != null && BackgroundProperty(element) is { } bg && AppearanceRgba.TryParseStorageHex(style.Background, out var bgColor)) Put(element, bg, MakeBrush(bgColor));
        if (style.Foreground != null && ForegroundProperty(element) is { } fg && AppearanceRgba.TryParseStorageHex(style.Foreground, out var fgColor)) Put(element, fg, MakeBrush(fgColor));
        if (style.CornerRadius is { } radius)
        {
            if (element is Border) Put(element, Border.CornerRadiusProperty, new CornerRadius(radius));
            else if (element is Control && TemplateBorder(element) is { } border) Put(border, Border.CornerRadiusProperty, new CornerRadius(radius));
        }
        if (style.FontSize is { } size && (element is Control or TextBlock)) Put(element, TextElement.FontSizeProperty, size);
        if (style.FontWeight is { } weight && (element is Control or TextBlock)) Put(element, TextElement.FontWeightProperty, FontWeight.FromOpenTypeWeight(weight));
        if (style.Padding is { } padding)
        {
            var thickness = new Thickness(padding);
            if (element is Control) Put(element, Control.PaddingProperty, thickness);
            else if (element is Border) Put(element, Border.PaddingProperty, thickness);
            else if (element is TextBlock) Put(element, TextBlock.PaddingProperty, thickness);
        }
        if (style.Hidden) Put(element, UIElement.VisibilityProperty, Visibility.Hidden);
    }

    /// <summary>Puts a property back as it was: unset, a plain local value, a binding, or a resource reference.</summary>
    private static void Restore(Saved s)
    {
        switch (s.Original)
        {
            case var unset when ReferenceEquals(unset, DependencyProperty.UnsetValue): s.Target.ClearValue(s.Property); break;
            case System.Windows.Data.BindingExpressionBase binding:
                s.Target.ClearValue(s.Property);
                if (binding.ParentBindingBase != null) System.Windows.Data.BindingOperations.SetBinding(s.Target, s.Property, binding.ParentBindingBase);
                break;
            case Expression expression:
                s.Target.ClearValue(s.Property);
                var key = expression.GetType().GetProperty("ResourceKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)?.GetValue(expression);
                if (key != null && s.Target is FrameworkElement fe) fe.SetResourceReference(s.Property, key);
                break;
            default: s.Target.SetValue(s.Property, s.Original); break;
        }
    }

    private static Brush MakeBrush(AppearanceRgba c)
    {
        var brush = new SolidColorBrush(Color.FromArgb(c.A8, c.R8, c.G8, c.B8));
        brush.Freeze();
        return brush;
    }

    private static DependencyProperty? BackgroundProperty(FrameworkElement e) => e switch
    {
        Control => Control.BackgroundProperty, Border => Border.BackgroundProperty, Panel => Panel.BackgroundProperty, TextBlock => TextBlock.BackgroundProperty, _ => null
    };

    private static DependencyProperty? ForegroundProperty(FrameworkElement e) => e switch
    {
        Control => Control.ForegroundProperty, TextBlock => TextBlock.ForegroundProperty, _ => null
    };

    /// <summary>The rounded border a templated control draws itself with (the first border down its template).</summary>
    private static Border? TemplateBorder(FrameworkElement control)
    {
        DependencyObject node = control;
        for (var depth = 0; depth < 4; depth++)
        {
            if (VisualTreeHelper.GetChildrenCount(node) == 0) return null;
            node = VisualTreeHelper.GetChild(node, 0);
            if (node is Border b) return b;
        }
        return null;
    }

    // ----- Reading the live look (for the editor's starting values) -----

    public static AppearanceRgba CurrentColor(FrameworkElement element, bool foreground)
    {
        var brush = foreground ? ForegroundProperty(element) is { } f ? element.GetValue(f) as Brush : null : BackgroundProperty(element) is { } b ? element.GetValue(b) as Brush : null;
        return brush is SolidColorBrush solid ? AppearanceColorPicker.FromWpf(solid.Color) : foreground ? new AppearanceRgba(0.9, 0.9, 0.9) : new AppearanceRgba(0.1, 0.1, 0.1);
    }

    public static double CurrentRadius(FrameworkElement element) => element is Border b ? b.CornerRadius.TopLeft : TemplateBorder(element)?.CornerRadius.TopLeft ?? 0;
    public static double CurrentFontSize(FrameworkElement element) => element is Control c ? c.FontSize : element is TextBlock t ? t.FontSize : 14;
    public static int CurrentWeight(FrameworkElement element) => (element is Control c ? c.FontWeight : element is TextBlock t ? t.FontWeight : FontWeights.Normal).ToOpenTypeWeight();
    public static double CurrentPadding(FrameworkElement element) => element switch { Control c => c.Padding.Left, Border b => b.Padding.Left, TextBlock t => t.Padding.Left, _ => 0 };
    public static bool HasPaddingProperty(FrameworkElement e) => e is Control or Border or TextBlock;
    public static bool HasFontProperties(FrameworkElement e) => e is Control or TextBlock;
    public static bool HasRadius(FrameworkElement e) => e is Border || (e is Control && TemplateBorder(e) != null);

    // ----- Finding the element under a point (pick mode) -----

    /// <summary>Walks up from a hit-test result to the nearest element worth customising (a control, text or border).</summary>
    public static FrameworkElement? Climb(DependencyObject? start)
    {
        for (var node = start; node != null; node = Parent(node))
            if (node is FrameworkElement fe && fe is Control or TextBlock or Border) return fe;
        return null;
    }
}
