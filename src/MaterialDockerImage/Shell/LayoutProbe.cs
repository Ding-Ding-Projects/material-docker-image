using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace MaterialDockerImage.Shell;

/// <summary>
/// Capture mode only (--capture-probe): walks the rendered visual tree and records layout problems a screenshot can
/// hide. "cut" is text on one line that is wider than its box and has no ellipsis, so the end is silently lost.
/// "ellipsis" is text shortened with an ellipsis, which is deliberate but worth reviewing. "outside" is a visible
/// element whose right edge passes the edge of the window or of an ancestor that clips horizontally.
/// Content that only extends below a vertical scroll area is not reported; scrolling reveals it.
/// </summary>
public static class LayoutProbe
{
    public sealed record Finding(string Kind, string Element, string Text, double Width, double Needed, string Path);

    public static List<Finding> Inspect(FrameworkElement root)
    {
        var findings = new List<Finding>();
        var dpi = VisualTreeHelper.GetDpi(root).PixelsPerDip;
        Walk(root, root, findings, dpi, reportedOutside: false);
        return findings;
    }

    public static void Write(string path, string surface, IReadOnlyList<Finding> findings) =>
        File.WriteAllText(path, JsonSerializer.Serialize(new { surface, findings }, new JsonSerializerOptions { WriteIndented = true }));

    private static void Walk(DependencyObject node, FrameworkElement root, List<Finding> findings, double dpi, bool reportedOutside)
    {
        if (node is UIElement { IsVisible: false }) return;
        if (node is FrameworkElement fe && fe.ActualWidth > 0 && fe.ActualHeight > 0)
        {
            if (fe is TextBlock tb && tb.Text is { Length: > 0 } text && tb.TextWrapping == TextWrapping.NoWrap)
            {
                var needed = Measure(tb);
                var available = tb.ActualWidth - tb.Padding.Left - tb.Padding.Right;
                if (needed > available + 1)
                    findings.Add(new(tb.TextTrimming == TextTrimming.None ? "cut" : "ellipsis", nameof(TextBlock), Short(text), Math.Round(available), Math.Round(needed), PathOf(tb)));
            }
            if (!reportedOutside && OverflowsHorizontally(fe, root) is { } limit)
            {
                findings.Add(new("outside", fe.GetType().Name, Short(TextOf(fe)), Math.Round(limit), Math.Round(RightEdge(fe, root)), PathOf(fe)));
                reportedOutside = true; // descendants of an overflowing element would only repeat the same finding
            }
        }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            Walk(VisualTreeHelper.GetChild(node, i), root, findings, dpi, reportedOutside);
    }

    // Ask the real TextBlock, in place, how wide it wants to be with no limit: that is WPF's own answer at the window's
    // real DPI, formatting mode and rounding, which a FormattedText estimate or an off-tree twin can miss by a pixel.
    // The probe runs after the capture, and the element's normal measure is restored straight away.
    private static double Measure(TextBlock tb)
    {
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var wanted = tb.DesiredSize.Width;
        tb.InvalidateMeasure();
        tb.UpdateLayout();
        return wanted - tb.Margin.Left - tb.Margin.Right - tb.Padding.Left - tb.Padding.Right; // DesiredSize includes the margin
    }

    private static double RightEdge(FrameworkElement fe, FrameworkElement root) =>
        fe.TransformToAncestor(root).TransformBounds(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight)).Right;

    /// <summary>Returns the right edge the element passes, or null when it fits.</summary>
    private static double? OverflowsHorizontally(FrameworkElement fe, FrameworkElement root)
    {
        double right;
        try { right = RightEdge(fe, root); } catch (InvalidOperationException) { return null; }
        double? clip = null;
        for (var parent = VisualTreeHelper.GetParent(fe); parent is FrameworkElement p && !ReferenceEquals(p, root); parent = VisualTreeHelper.GetParent(p))
        {
            // Horizontal scrolling is an intended way to reach the rest of the content; carets and other adorners are not layout.
            if (p is ScrollViewer { HorizontalScrollBarVisibility: not ScrollBarVisibility.Disabled } or Popup or System.Windows.Documents.AdornerLayer) return null;
            if (clip == null && (p.ClipToBounds || p is ScrollContentPresenter))
                clip = p.TransformToAncestor(root).TransformBounds(new Rect(0, 0, p.ActualWidth, p.ActualHeight)).Right;
        }
        if (right > root.ActualWidth + 1) return root.ActualWidth;
        return clip is { } limit && right > limit + 1 ? limit : null;
    }

    private static string TextOf(DependencyObject node)
    {
        if (node is TextBlock tb) return tb.Text;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            if (TextOf(VisualTreeHelper.GetChild(node, i)) is { Length: > 0 } t) return t;
        return "";
    }

    private static string Short(string text) => text.Length <= 80 ? text : text[..80] + "…";

    private static string PathOf(FrameworkElement fe)
    {
        var parts = new List<string>();
        for (DependencyObject? node = fe; node != null && parts.Count < 6; node = VisualTreeHelper.GetParent(node))
            if (node is FrameworkElement f) parts.Add(f.Name is { Length: > 0 } n ? $"{f.GetType().Name}#{n}" : f.GetType().Name);
        parts.Reverse();
        return string.Join(" > ", parts);
    }
}
