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
        check.Checked += (_, _) => SelectionChanged?.Invoke();
        check.Unchecked += (_, _) => SelectionChanged?.Invoke();

        var title = Ui.HStack(8, Ui.Mono(target.Name, 14, "OnSurface"), target.Stage.Length > 0 ? Ui.Badge("stage " + target.Stage, Tone.Neutral) : null);
        var source = Ui.Mono((target.Context.Length > 0 ? target.Context : ".") + "  ·  " + (target.Dockerfile.Length > 0 ? target.Dockerfile : "Dockerfile"), 12, "OnSurfaceVariant");
        var chips = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var tag in target.Tags.Take(4)) chips.Children.Add(Ui.Badge(tag, Tone.Primary).Margin(0, 0, 6, 6));
        if (target.Tags.Count > 4) chips.Children.Add(Ui.Badge($"+{target.Tags.Count - 4}", Tone.Primary).Margin(0, 0, 6, 6));
        foreach (var platform in target.Platforms) chips.Children.Add(Ui.Badge(platform, Tone.Tertiary).Margin(0, 0, 6, 6));
        if (target.Args.Count > 0) chips.Children.Add(Ui.Badge($"{target.Args.Count} {Loc.T(target.Args.Count == 1 ? "arg" : "args")}").Margin(0, 0, 6, 6));
        if (target.Tags.Count == 0) chips.Children.Add(Ui.Badge(Loc.T("no tags")).Margin(0, 0, 6, 6));

        var body = new DockPanel();
        body.Children.Add(check.Dock(System.Windows.Controls.Dock.Left));
        body.Children.Add(Ui.VStack(4, title, source, chips));
        Child = body;
        Padding = new Thickness(12, 10, 12, 6);
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
        SetResourceReference(BackgroundProperty, focused ? "SecondaryContainer" : hover || IsKeyboardFocused ? "SurfaceHighest" : "SurfaceHigh");
        SetResourceReference(BorderBrushProperty, inGroup ? "Primary" : focused ? "SecondaryContainer" : hover || IsKeyboardFocused ? "SurfaceHighest" : "SurfaceHigh");
    }

    private static bool IsInside(DependencyObject element, DependencyObject container)
    {
        for (var current = element; current != null; current = current is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (current == container) return true;
        return false;
    }
}
