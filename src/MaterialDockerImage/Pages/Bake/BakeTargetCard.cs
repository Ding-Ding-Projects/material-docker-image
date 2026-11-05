using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// One bake target: a selection check, the name, context and Dockerfile, tag badges, platform chips and argument
/// count. Clicking the card (or Enter) shows its resolved definition; the check box adds it to "Build selected".
/// </summary>
public sealed class BakeTargetCard : Border
{
    private readonly CheckBox check;
    private bool focused, hover, inGroup;
    public BakeTarget Target { get; }
    public bool IsChecked { get => check.IsChecked == true; set => check.IsChecked = value; }
    public event Action? SelectionChanged;

    public BakeTargetCard(BakeTarget target, Action show)
    {
        Target = target;
        check = new CheckBox { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 10, 0) };
        System.Windows.Automation.AutomationProperties.SetName(check, Loc.T("Select target") + " " + target.Name);
        check.Checked += (_, _) => { Paint(); SelectionChanged?.Invoke(); };
        check.Unchecked += (_, _) => { Paint(); SelectionChanged?.Invoke(); };

        var dockerfile = target.Dockerfile.Length > 0 ? target.Dockerfile : "Dockerfile";
        var sourcePath = target.Context.Length > 0 && target.Context != "." ? target.Context.TrimEnd('/', Path.DirectorySeparatorChar) + "/" + dockerfile : dockerfile;
        var source = Ui.Text(sourcePath, 12, null, "OnSurfaceVariant", false, false);
        var title = new DockPanel();
        title.Children.Add(source.Dock(System.Windows.Controls.Dock.Right));
        var name = Ui.HStack(8, Ui.Mono(target.Name, 14, "OnSurface"), target.Stage.Length > 0 ? Ui.Badge("stage " + target.Stage, Tone.Neutral) : null);
        title.Children.Add(name);
        // Platforms are small mono chips; tags are summarised as a count and listed in the tooltip and the definition.
        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 0) };
        foreach (var platform in target.Platforms) chips.Children.Add(PlatformChip(platform));
        var summary = new List<string> { target.Tags.Count == 0 ? Loc.T("no tags") : $"{target.Tags.Count} {Loc.T(target.Tags.Count == 1 ? "tag" : "tags")}" };
        if (target.Args.Count > 0) summary.Add($"{target.Args.Count} {Loc.T(target.Args.Count == 1 ? "arg" : "args")}");
        var counts = Ui.Text(string.Join(" · ", summary), 12, null, "OnSurfaceVariant", false, false);
        counts.Margin = new Thickness(4, 0, 0, 6);
        if (target.Tags.Count > 0) counts.ToolTip = string.Join(Environment.NewLine, target.Tags);
        chips.Children.Add(counts);

        var body = new DockPanel();
        body.Children.Add(check.Dock(System.Windows.Controls.Dock.Left));
        body.Children.Add(Ui.VStack(8, title, chips));
        Child = body;
        Padding = new Thickness(12, 12, 12, 6);
        CornerRadius = new CornerRadius(12);
        Margin = new Thickness(0, 0, 0, 8);
        BorderThickness = new Thickness(1);
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.Hand;
        System.Windows.Automation.AutomationProperties.SetName(this, Loc.T("Target") + " " + target.Name);
        MouseEnter += (_, _) => { hover = true; Paint(); };
        MouseLeave += (_, _) => { hover = false; Paint(); };
        GotKeyboardFocus += (_, _) => Paint();
        LostKeyboardFocus += (_, _) => Paint();
        MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is DependencyObject d && IsInside(d, check)) return; Focus(); show(); };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { show(); e.Handled = true; }
            else if (e.Key == Key.Space) { check.IsChecked = check.IsChecked != true; e.Handled = true; }
        };
        ToolTip = Loc.T("Click to show the resolved definition. Space selects the target for building.");
        Paint();
    }

    /// <summary>Highlights the card shown in the definition panel and marks members of the chosen group.</summary>
    public void SetState(bool isFocused, bool isInGroup)
    {
        focused = isFocused; inGroup = isInGroup;
        Paint();
    }

    private void Paint()
    {
        SetResourceReference(BackgroundProperty, focused || hover || IsKeyboardFocused || IsChecked ? "SurfaceHigh" : "SurfaceContainer");
        // A selected target is outlined like the design's checked card; the focused one also gets the raised surface.
        SetResourceReference(BorderBrushProperty, inGroup || focused || IsChecked ? "Primary" : hover || IsKeyboardFocused ? "SurfaceHigh" : "SurfaceContainer");
    }

    private static Border PlatformChip(string platform)
    {
        var label = Ui.Mono(platform, 12, "OnSecondaryContainer");
        var chip = new Border { Child = label, Height = 24, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 6, 6) };
        label.VerticalAlignment = VerticalAlignment.Center;
        chip.SetResourceReference(BackgroundProperty, "SecondaryContainer");
        return chip;
    }

    private static bool IsInside(DependencyObject element, DependencyObject container)
    {
        for (var current = element; current != null; current = current is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (current == container) return true;
        return false;
    }
}
