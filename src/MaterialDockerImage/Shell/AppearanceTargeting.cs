using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Shell;

/// <summary>
/// Works out which colour roles an element is painted with, so "Edit appearance…" can open on exactly those roles.
/// It compares the element's (and its nearest ancestors') brushes with the role brushes in the application resources.
/// </summary>
public static class AppearanceTargeting
{
    public sealed record Target(FrameworkElement Element, string Title, IReadOnlyList<string> Roles);

    public static Target For(DependencyObject? start, FrameworkElement fallback)
    {
        var element = Climb(start) ?? fallback;
        var roles = new List<string>();
        var brushes = AppearanceRoles.All.Select(r => (r.Key, Brush: Application.Current.TryFindResource(r.Key) as Brush)).Where(p => p.Brush != null).ToList();
        void Match(Brush? brush) { if (brush == null) return; foreach (var (key, b) in brushes) if (ReferenceEquals(b, brush) && !roles.Contains(key)) roles.Add(key); }
        DependencyObject? node = start ?? element;
        for (var depth = 0; node != null && depth < 8 && roles.Count < 5; depth++)
        {
            switch (node)
            {
                case Control c: Match(c.Background); Match(c.Foreground); Match(c.BorderBrush); break;
                case Border b: Match(b.Background); Match(b.BorderBrush); break;
                case Panel p: Match(p.Background); break;
                case TextBlock t: Match(t.Foreground); Match(t.Background); break;
            }
            node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        var title = element switch
        {
            RadioButton r when r.Style == Application.Current.TryFindResource("Rail.Item") => "Navigation item",
            ButtonBase => "Button",
            TextBox or PasswordBox or ComboBox => "Field",
            TextBlock => "Text",
            Border => "Card or surface",
            _ => element.GetType().Name
        };
        // Sensible defaults by element type when nothing matched (for example a transparent element).
        var defaults = title switch
        {
            "Navigation item" => new[] { "SecondaryContainer", "OnSecondaryContainer", "OnSurfaceVariant" },
            "Button" => new[] { "Primary", "OnPrimary", "SecondaryContainer", "OnSecondaryContainer" },
            "Field" => new[] { "SurfaceLowest", "OutlineVariant", "Primary", "OnSurface" },
            "Text" => new[] { "OnSurface", "OnSurfaceVariant" },
            _ => new[] { "SurfaceLow", "Surface", "OnSurface" }
        };
        foreach (var d in defaults) if (roles.Count < 5 && !roles.Contains(d)) roles.Add(d);
        return new Target(element, title, roles);
    }

    private static FrameworkElement? Climb(DependencyObject? start)
    {
        for (var node = start; node != null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is FrameworkElement fe && (fe is Control or TextBlock or Border)) return fe;
        return null;
    }
}
