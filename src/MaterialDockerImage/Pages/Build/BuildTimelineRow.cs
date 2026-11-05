using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// One step of the build timeline. The row is focusable; click, Enter or Space filters the log to the step.
/// Elements are created once and updated in place as the step changes.
/// </summary>
public sealed class BuildTimelineRow : Border
{
    private const double BarWidth = 180;
    private readonly BuildKitStep step;
    private readonly TextBlock icon = Ui.Icon(Icons.Pending, 18, true, "Primary");
    private readonly Border stage;
    private readonly TextBlock stageText = Ui.Text("", 11, FontWeights.SemiBold, "OnSurfaceVariant", false, false);
    private readonly TextBlock name = Ui.Mono("", 12, "OnSurface");
    private readonly Border track = new() { Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center };
    private readonly Border fill = new() { Height = 8, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ProgressBar live = new() { IsIndeterminate = true, Height = 6, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock duration = Ui.Text("", 12, null, "OnSurfaceVariant", false, false);
    private readonly Border cachedBadge;
    private bool hover, isSelected, runningNow;

    public BuildTimelineRow(BuildKitStep step, Action activate)
    {
        this.step = step;
        // Stage chip: 22 high, radius 6, quiet fill, left aligned in a 72px column.
        stage = new Border { Child = stageText, Height = 22, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        stage.SetResourceReference(BackgroundProperty, "SurfaceHigh");
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.VerticalAlignment = VerticalAlignment.Center;
        track.SetResourceReference(BackgroundProperty, "SurfaceHigh");
        track.Child = fill;
        duration.TextAlignment = TextAlignment.Right;
        // One 180px cell holds either the CACHED badge or the duration bar, as in the design.
        var badgeText = Ui.Text(Loc.T("CACHED"), 11, FontWeights.Bold, "OnTertiaryContainer", false, false);
        cachedBadge = new Border { Child = badgeText, Height = 20, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 0, 8, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        cachedBadge.SetResourceReference(BackgroundProperty, "TertiaryContainer");
        var barCell = new Grid { Width = BarWidth };
        barCell.Children.Add(track); barCell.Children.Add(live); barCell.Children.Add(cachedBadge);
        var grid = Ui.Columns(10, [-22, -72, 1, -BarWidth, -52], icon, stage, name, barCell, duration);
        Child = grid;
        Height = 34;
        Padding = new Thickness(8, 0, 8, 0);
        CornerRadius = new CornerRadius(8);
        Margin = new Thickness(0);
        Focusable = true;
        Cursor = Cursors.Hand;
        FocusVisualStyle = null;
        MouseEnter += (_, _) => { hover = true; Paint(); };
        MouseLeave += (_, _) => { hover = false; Paint(); };
        GotKeyboardFocus += (_, _) => Paint();
        LostKeyboardFocus += (_, _) => Paint();
        MouseLeftButtonUp += (_, _) => { Focus(); activate(); };
        KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { activate(); e.Handled = true; } };
        ToolTip = Loc.T("Show this step's log lines");
    }

    public void Update(double now, double longest, bool selected)
    {
        isSelected = selected;
        var (glyph, brush, filled) = step.Status switch
        {
            BuildKitStepStatus.Cached => (Icons.Cached, "Tertiary", false),
            BuildKitStepStatus.Done => (Icons.CheckCircle, "Success", true),
            BuildKitStepStatus.Error => (Icons.Error, "Error", true),
            BuildKitStepStatus.Canceled => (Icons.Cancel, "OnSurfaceVariant", true),
            _ => (Icons.Pending, "Primary", false)
        };
        icon.Text = glyph;
        icon.SetResourceReference(TextBlock.FontFamilyProperty, filled ? "IconFontFilled" : "IconFont");
        icon.SetResourceReference(TextBlock.ForegroundProperty, brush);
        var internalStep = step.IsInternal || !step.IsInstruction;
        var label = step.Stage is { Length: > 0 } s ? s : internalStep ? "internal" : "";
        stageText.Text = label;
        stage.Visibility = label.Length == 0 ? Visibility.Hidden : Visibility.Visible;
        stage.ToolTip = step.Index is { } i ? $"{label} {i}/{step.Total}" : null;
        name.Text = step.Name;
        runningNow = step.Status == BuildKitStepStatus.Running;
        name.SetResourceReference(TextBlock.ForegroundProperty, runningNow ? "OnSurface" : "OnSurfaceVariant");
        var cached = step.Status == BuildKitStepStatus.Cached;
        live.Visibility = runningNow ? Visibility.Visible : Visibility.Collapsed;
        track.Visibility = runningNow || cached ? Visibility.Collapsed : Visibility.Visible;
        cachedBadge.Visibility = cached ? Visibility.Visible : Visibility.Collapsed;
        var elapsed = step.Elapsed(now);
        fill.Width = Math.Max(2, BarWidth * Math.Min(1, elapsed / longest));
        fill.SetResourceReference(BackgroundProperty, step.Status switch { BuildKitStepStatus.Error => "Error", BuildKitStepStatus.Running => "Primary", _ => "Outline" });
        duration.Text = cached ? "0.0 s" : elapsed < 60 ? elapsed.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s" : BuildTimelineView.FormatSeconds(elapsed);
        System.Windows.Automation.AutomationProperties.SetName(this, $"{Loc.T("Step")} {step.Id}: {step.Name}, {Loc.T(step.Status.ToString())}, {duration.Text}");
        Paint();
    }

    private void Paint()
    {
        var key = isSelected ? "SecondaryContainer" : runningNow ? "SurfaceHigh" : hover || IsKeyboardFocused ? "HoverLayer" : null;
        if (key == null) Background = null; else SetResourceReference(BackgroundProperty, key);
    }
}
