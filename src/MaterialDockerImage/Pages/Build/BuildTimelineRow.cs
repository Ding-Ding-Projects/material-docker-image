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
    private const double BarWidth = 120;
    private readonly BuildKitStep step;
    private readonly TextBlock icon = Ui.Icon(Icons.Pending, 18, true, "Primary");
    private readonly Border stage;
    private readonly TextBlock stageText = Ui.Text("", 11, FontWeights.SemiBold, "OnPrimaryContainer", false, false);
    private readonly TextBlock name = Ui.Mono("", 13, "OnSurface");
    private readonly Border track = new() { Width = BarWidth, Height = 6, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center };
    private readonly Border fill = new() { Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ProgressBar live = new() { IsIndeterminate = true, Height = 6, Width = BarWidth, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock duration = Ui.Mono("", 12, "OnSurfaceVariant");
    private readonly Border cachedBadge = Ui.Badge("CACHED", Tone.Tertiary);
    private bool hover, isSelected;

    public BuildTimelineRow(BuildKitStep step, Action activate)
    {
        this.step = step;
        stage = new Border { Child = stageText, CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        track.SetResourceReference(BackgroundProperty, "SurfaceHighest");
        track.Child = fill;
        duration.TextAlignment = TextAlignment.Right; duration.Width = 64;
        cachedBadge.Margin = new Thickness(8, 0, 0, 0);
        var barCell = new Grid { Width = BarWidth, Margin = new Thickness(12, 0, 8, 0) };
        barCell.Children.Add(track); barCell.Children.Add(live);
        var grid = Ui.Columns(0, [-30, 0, 1, 0, 0, -80], icon, stage, name, barCell, duration, cachedBadge);
        Child = grid;
        Padding = new Thickness(8, 6, 8, 6);
        CornerRadius = new CornerRadius(10);
        Margin = new Thickness(0, 0, 0, 2);
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
        var (glyph, brush) = step.Status switch
        {
            BuildKitStepStatus.Cached => (Icons.Cached, "Tertiary"),
            BuildKitStepStatus.Done => (Icons.CheckCircle, "Success"),
            BuildKitStepStatus.Error => (Icons.Error, "Error"),
            BuildKitStepStatus.Canceled => (Icons.Cancel, "OnSurfaceVariant"),
            _ => (Icons.Pending, "Primary")
        };
        icon.Text = glyph;
        icon.SetResourceReference(TextBlock.ForegroundProperty, brush);
        var label = step.Stage is { } s ? step.Index is { } i ? $"{s} {i}/{step.Total}" : s : step.Index is { } j ? $"{j}/{step.Total}" : "";
        stageText.Text = label;
        stage.Visibility = label.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        var internalStep = step.IsInternal || !step.IsInstruction;
        stage.SetResourceReference(BackgroundProperty, internalStep ? "SurfaceHighest" : "PrimaryContainer");
        stageText.SetResourceReference(TextBlock.ForegroundProperty, internalStep ? "OnSurfaceVariant" : "OnPrimaryContainer");
        name.Text = $"#{step.Id}  {step.Name}";
        name.SetResourceReference(TextBlock.ForegroundProperty, internalStep ? "OnSurfaceVariant" : "OnSurface");
        var runningNow = step.Status == BuildKitStepStatus.Running;
        live.Visibility = runningNow ? Visibility.Visible : Visibility.Collapsed;
        track.Visibility = runningNow ? Visibility.Hidden : Visibility.Visible;
        var elapsed = step.Elapsed(now);
        fill.Width = Math.Max(step.Status == BuildKitStepStatus.Cached ? 0 : 2, BarWidth * Math.Min(1, elapsed / longest));
        fill.SetResourceReference(BackgroundProperty, step.Status switch { BuildKitStepStatus.Error => "Error", BuildKitStepStatus.Done => "Primary", _ => "Outline" });
        duration.Text = step.Status == BuildKitStepStatus.Cached ? "0.0s" : BuildTimelineView.FormatSeconds(elapsed);
        cachedBadge.Visibility = step.Status == BuildKitStepStatus.Cached ? Visibility.Visible : Visibility.Hidden;
        System.Windows.Automation.AutomationProperties.SetName(this, $"{Loc.T("Step")} {step.Id}: {step.Name}, {Loc.T(step.Status.ToString())}, {duration.Text}");
        Paint();
    }

    private void Paint()
    {
        var key = isSelected ? "SecondaryContainer" : hover || IsKeyboardFocused ? "HoverLayer" : null;
        if (key == null) Background = null; else SetResourceReference(BackgroundProperty, key);
    }
}
