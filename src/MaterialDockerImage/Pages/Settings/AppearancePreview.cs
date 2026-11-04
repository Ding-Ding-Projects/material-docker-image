using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// A miniature of the window built from the same resources as the app (title bar, rail pill, buttons, chips, a switch,
/// a card, a table row and a toast), so every appearance change shows here immediately. Nothing in it runs anything.
/// </summary>
public sealed class AppearancePreview : Border
{
    private readonly StackPanel stage;
    private readonly TextBlock stat;

    public AppearancePreview()
    {
        SetResourceReference(BackgroundProperty, "Surface");
        SetResourceReference(BorderBrushProperty, "OutlineVariant");
        SetResourceReference(CornerRadiusProperty, "RadiusLarge");
        BorderThickness = new Thickness(1);
        ClipToBounds = true;
        AutomationProperties.SetName(this, Loc.T("Appearance preview"));

        // Title bar.
        var bar = new DockPanel { Height = 36 };
        bar.Children.Add(Ui.Icon(Icons.DeployedCode, 18, true, "Primary").Margin(12, 0, 8, 0).Dock(Dock.Left));
        bar.Children.Add(Ui.HStack(14, Ui.Icon(Icons.Minimize, 14, false, "OnSurface"), Ui.Icon(Icons.CropSquare, 14, false, "OnSurface"), Ui.Icon(Icons.Close, 14, false, "OnSurface")).Margin(0, 0, 12, 0).Dock(Dock.Right));
        bar.Children.Add(Ui.Text("Material Docker Image", 12, FontWeights.SemiBold, "OnSurface", false, true));
        var titleBar = new Border { Child = bar, BorderThickness = new Thickness(0, 0, 0, 1) };
        titleBar.SetResourceReference(Border.BorderBrushProperty, "SurfaceHigh");

        // Rail with a selected pill.
        FrameworkElement RailItem(string icon, string label, bool selected)
        {
            var pill = new Border { Width = 48, Height = 28, CornerRadius = new CornerRadius(14), HorizontalAlignment = HorizontalAlignment.Center, Child = Ui.Icon(icon, 18, selected, selected ? "OnSecondaryContainer" : "OnSurfaceVariant") };
            if (selected) pill.SetResourceReference(Border.BackgroundProperty, "SecondaryContainer");
            if (!selected && pill.Child is TextBlock glyph) glyph.SetResourceReference(TextBlock.FontFamilyProperty, "RailIconFont");
            var text = Ui.Text(label, 10, selected ? FontWeights.SemiBold : FontWeights.Normal, selected ? "OnSurface" : "OnSurfaceVariant", false, false);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            return Ui.VStack(2, pill, text).Margin(0, 6, 0, 0);
        }
        var rail = Ui.VStack(2, RailItem(Icons.Construction, "Build", false), RailItem(Icons.Layers, "Images", true), RailItem(Icons.Inventory2, "Containers", false));
        rail.Width = 64;

        // Content.
        var buttons = Ui.Wrap(8, Ui.Button("Filled", Icons.PlayArrow, ButtonKind.Filled), Ui.Button("Tonal", null, ButtonKind.Tonal), Ui.Button("Outlined", null, ButtonKind.Outlined), Ui.Button("Text", null, ButtonKind.Text));
        var chips = Ui.Wrap(8, Ui.Chip("Running", true, _ => { }), Ui.Chip("Dangling", false, _ => { }), Ui.Badge("healthy", Tone.Success), Ui.Badge("warning", Tone.Warning));
        var toggle = Ui.Switch("Auto-refresh", true, _ => { });
        stat = Ui.Text("128", 28, FontWeights.SemiBold, "OnSurface", false, false);
        stat.SetResourceReference(TextBlock.FontWeightProperty, "HeadingWeight");
        var statTile = Ui.Card(Ui.VStack(0, Ui.Overline("Images"), stat, Ui.Caption("3.4 GB on disk")), 12, "SurfaceContainer", 12);
        var row = new Border { Padding = new Thickness(10, 8, 10, 8), Child = Ui.Columns(10, [0, 1, 0], Ui.Icon(Icons.Layers, 18, false, "Primary"), Ui.Mono("sha256:4f2a9c1e7b", 12, "OnSurface"), Ui.Badge("in use", Tone.Primary)) };
        row.SetResourceReference(Border.BackgroundProperty, "HoverLayer");
        row.SetResourceReference(Border.CornerRadiusProperty, "RadiusSmall");
        var card = Ui.Card(Ui.VStack(8, Ui.Text("Card on a low surface", 13, FontWeights.SemiBold), Ui.Caption("Secondary text uses On surface variant."), row), 12, "SurfaceLow", 16);
        var toastRow = new DockPanel();
        toastRow.Children.Add(Ui.Icon(Icons.CheckCircle, 18, true, "OnSuccessContainer").Margin(0, 0, 10, 0).Dock(Dock.Left));
        toastRow.Children.Add(Ui.Text("Image built in 18 s.", 13, null, "OnSurface", false, true));
        var toast = Ui.Card(toastRow, 10, "SurfaceHighest", 12);
        toast.Effect = Ui.Elevation(14, 3, .35);
        toast.HorizontalAlignment = HorizontalAlignment.Right;
        toast.Margin = new Thickness(0, 4, 4, 8);
        var heading = Ui.Title("Images", 18);
        stage = Ui.VStack(10, heading, buttons, chips, Ui.Columns(10, [1, -140], card, statTile), toggle, toast);
        var content = new Border { Child = stage, Padding = new Thickness(14) };
        var body = new DockPanel();
        body.Children.Add(rail.Dock(Dock.Left));
        body.Children.Add(content);
        Child = Ui.Rows(0, [0, 1], titleBar, body);
        foreach (var b in buttons.Children.OfType<Button>()) b.ToolTip = Loc.T("Preview only. Nothing runs.");
    }

    /// <summary>Plays a short demonstration of the current motion settings on the preview.</summary>
    public void PlayDemo()
    {
        Motion.SharedAxis(stage, MotionAxis.Y, true);
        Motion.Stagger(stage.Children.OfType<UIElement>());
        Motion.CountUp(stat, 128, v => v.ToString("0"));
    }
}
