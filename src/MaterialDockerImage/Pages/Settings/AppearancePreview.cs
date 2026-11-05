using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// A miniature of the window built from the same resources as the app (title bar, rail, a build card with real buttons and
/// a progress bar, container states and a notification), so every appearance change shows here immediately. The content
/// is an example: nothing in it runs anything.
/// </summary>
public sealed class AppearancePreview : Border
{
    private readonly StackPanel stage;
    private readonly ProgressBar progress;
    private readonly Border snackbar;

    public AppearancePreview()
    {
        SetResourceReference(BackgroundProperty, "Surface");
        SetResourceReference(BorderBrushProperty, "SurfaceHigh");
        SetResourceReference(CornerRadiusProperty, "RadiusMedium");
        BorderThickness = new Thickness(1);
        ClipToBounds = true;
        AutomationProperties.SetName(this, Loc.T("Appearance preview"));

        // Title bar.
        var dot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 8, 0) };
        dot.SetResourceReference(Border.BackgroundProperty, "Primary");
        var bar = new Border { Height = 32, Padding = new Thickness(12, 0, 12, 0), Child = Ui.HStack(0, dot, Ui.Text("Material Docker Image", 12, null, "OnSurfaceVariant", false, false)) };
        bar.SetResourceReference(Border.BackgroundProperty, "SurfaceLow");

        // Rail: the selected pill and three destinations.
        Border Block(double w, double h, string brush, string radiusKey)
        {
            var b = new Border { Width = w, Height = h, HorizontalAlignment = HorizontalAlignment.Center };
            b.SetResourceReference(Border.BackgroundProperty, brush);
            b.SetResourceReference(Border.CornerRadiusProperty, radiusKey);
            return b;
        }
        var rail = Ui.VStack(8, Block(36, 24, "SecondaryContainer", "RadiusPill"), Block(20, 20, "SurfaceHighest", "RadiusSmall"), Block(20, 20, "SurfaceHighest", "RadiusSmall"), Block(20, 20, "SurfaceHighest", "RadiusSmall"));
        rail.Width = 44;

        // A build card with real buttons, so density, shape and fonts show exactly as in the app.
        progress = new ProgressBar { Minimum = 0, Maximum = 100, Value = 64, Height = 6 };
        AutomationProperties.SetName(progress, Loc.T("Example progress"));
        var build = Ui.Button("Build", null, ButtonKind.Filled); var check = Ui.Button("Check", null, ButtonKind.Outlined);
        foreach (var b in new[] { build, check }) b.ToolTip = Loc.T("Preview only. Nothing runs.");
        var buildCard = Ui.Card(Ui.VStack(10, Ui.Text("Build image", 14, FontWeights.SemiBold), progress, Ui.Wrap(8, build, check)), 12, "SurfaceContainer", 12);

        // Container states use the status colour roles.
        FrameworkElement State(string name, string state, string brush) => Ui.Columns(8, [1, 0], Ui.Text(name, 12, null, "OnSurface", false, false), Ui.Text(state, 12, null, brush, false));
        var states = Ui.Card(Ui.VStack(6, State("hello-api", "Running", "Success"), State("postgres", "Restarting", "Warning"), State("worker", "Exited (1)", "Error")), 12, "SurfaceContainer", 12);

        var snackRow = new DockPanel();
        snackRow.Children.Add(Ui.Icon(Icons.Info, 16, true, "OnPrimaryContainer").Margin(0, 0, 8, 0).Dock(Dock.Left));
        snackRow.Children.Add(Ui.Text("Notifications arrive with the chosen motion", 12, null, "OnPrimaryContainer"));
        snackbar = new Border { Child = snackRow, Padding = new Thickness(12, 10, 12, 10) };
        snackbar.SetResourceReference(Border.BackgroundProperty, "PrimaryContainer");
        snackbar.SetResourceReference(Border.CornerRadiusProperty, "RadiusMedium");

        stage = Ui.VStack(10, buildCard, states, snackbar);
        var body = Ui.Columns(10, [-44, 1], rail, stage);
        body.Margin = new Thickness(10);
        Child = Ui.Rows(0, [0, 1], bar, body);
    }

    /// <summary>Plays a short demonstration of the current motion settings on the preview.</summary>
    public void PlayDemo()
    {
        Motion.SharedAxis(stage, MotionAxis.Y, true);
        Motion.Stagger(stage.Children.OfType<UIElement>());
        progress.Value = 0;
        Motion.SmoothProgress(progress, 64);
        Motion.Reveal(snackbar);
    }
}
