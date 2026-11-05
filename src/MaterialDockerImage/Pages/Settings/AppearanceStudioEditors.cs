using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;
using WpfEllipse = System.Windows.Shapes.Ellipse;

namespace MaterialDockerImage.Pages;

/// <summary>One contrast check of the studio: a colour role read against the surface it normally sits on.</summary>
internal sealed record StudioContrastCheck(string Role, string Name, string Against, double Need)
{
    private static Color Resolve(string key) => Application.Current?.TryFindResource(key) is SolidColorBrush b ? b.Color : Colors.Transparent;
    public double Ratio() => Ratio(Resolve);

    /// <summary>The ratio with colours from any source, such as a candidate scheme that is not applied.</summary>
    public double Ratio(Func<string, Color> resolve)
    {
        var fore = resolve(Role); var back = resolve(Against);
        // A translucent role is read as it appears over its background.
        var flat = Theme.Mix(back, Color.FromRgb(fore.R, fore.G, fore.B), fore.A / 255.0);
        return Theme.ContrastRatio(flat, back);
    }
}

/// <summary>The colour, motion and shape editors of the appearance studio.</summary>
public sealed partial class SettingsPage
{
    /// <summary>Text needs 4.5:1 (WCAG AA); an outline is a non-text boundary and needs 3:1.</summary>
    private static readonly StudioContrastCheck[] StudioContrastChecks =
    [
        new("Primary", "Primary", "Surface", 4.5), new("OnPrimaryContainer", "On primary container", "PrimaryContainer", 4.5),
        new("Tertiary", "Tertiary", "Surface", 4.5), new("Error", "Error", "Surface", 4.5),
        new("Outline", "Outline", "Surface", 3), new("OnSurfaceVariant", "Secondary text", "Surface", 4.5)
    ];

    private static readonly (string Name, (double, double, double, double) Token)[] EasingTokens =
    [
        ("Emphasized decelerate", AppearanceMotion.EmphasizedDecelerate), ("Emphasized accelerate", AppearanceMotion.EmphasizedAccelerate),
        ("Standard", AppearanceMotion.Standard), ("Standard decelerate", AppearanceMotion.StandardDecelerate)
    ];

    /// <summary>What the motion setting does right now, including the system and accessibility settings that override it.</summary>
    private string MotionNoteText()
    {
        var effective = Motion.EffectiveLevel;
        return Motion.WindowsAnimationsOff ? Loc.T("Windows animation effects are off (Settings, Accessibility, Visual effects), so this app does not animate either. Turn them on in Windows to see motion.")
            : S.ReducedMotion ? Loc.T("Reduced motion is on: nothing moves, and pages change with a short fade only.")
            : S.LowStimulation ? Loc.T("Low stimulation is on: nothing moves, and pages change with a short fade only.")
            : effective == "Off" ? Loc.T("Motion is off: every change appears instantly.")
            : effective == "Reduced" ? Loc.T("Reduced: nothing moves, and pages change with a short fade only.")
            : effective == "Expressive" ? Loc.T("Expressive: longer, bouncier Material motion with larger travel.")
            : Loc.T("Standard Material 3 motion: emphasized easing, 50 to 600 ms.");
    }

    private void BringRowIntoView(string id)
    {
        if (rows.All.FirstOrDefault(r => r.Id == id) is not { } row) return;
        if (row.Element.Visibility != Visibility.Visible) row.Element.Visibility = Visibility.Visible;
        row.Element.BringIntoView();
    }

    // ----- Colour -----

    private FrameworkElement StudioColourPanel(List<Action> syncs)
    {
        var theme = new StudioChoice(["Light", "Dark", "System"], () => AppearanceRuntime.Current.Theme, v => Look("theme set to " + v, m => m.Theme = v)) { MaxWidth = 264, MinWidth = 180 };
        AutomationProperties.SetName(theme, Loc.T("Theme"));
        StudioLock(theme, "theme");
        syncs.Add(theme.Sync);

        // Seed swatch inside a hue wheel; it opens the full colour picker row below.
        var seedFill = new WpfEllipse { Width = 52, Height = 52 };
        var seedGap = new WpfEllipse { Width = 62, Height = 62 };
        seedGap.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "SurfaceContainer");
        var ring = new Grid { Width = 96, Height = 96 };
        ring.Children.Add(new Image { Source = StudioParts.HueRing(), Width = 96, Height = 96 });
        ring.Children.Add(seedGap); ring.Children.Add(seedFill);
        var ringButton = StudioParts.Tile(ring, "Open the full seed colour picker", () => BringRowIntoView("seed"));
        ringButton.Background = Brushes.Transparent; ringButton.BorderThickness = new Thickness(0); ringButton.Padding = new Thickness(0);
        ringButton.HorizontalAlignment = HorizontalAlignment.Center;
        ringButton.ToolTip = Loc.T("Open the full seed colour picker (any format, contrast and rainbow) further down this section.");
        var seedLabel = Ui.Mono("", 12, "OnSurfaceVariant");
        seedLabel.HorizontalAlignment = HorizontalAlignment.Center;
        var seedStack = Ui.VStack(6, ringButton, seedLabel);

        // Hue, chroma and tone of the seed in OKLCH, the perceptual space the colour translator also uses.
        AppearanceRgba.TryParseStorageHex(AppearanceRuntime.Current.Seed, out var seed);
        var (l0, c0, h0) = seed.ToOklch();
        var hue = StudioParts.GradientSlider(0, 360, h0, 1, "Seed hue");
        var chroma = StudioParts.GradientSlider(0, 0.37, Math.Min(c0, 0.37), 0.005, "Seed chroma");
        var tone = StudioParts.GradientSlider(0, 100, l0 * 100, 1, "Seed tone");
        var hueText = Ui.Mono("", 12); var chromaText = Ui.Mono("", 12); var toneText = Ui.Mono("", 12);
        foreach (var t in new[] { hueText, chromaText, toneText }) t.TextAlignment = TextAlignment.Right;
        FrameworkElement SliderRow(string label, Slider slider, TextBlock value) => Ui.Columns(10, [-64, 1, -48], Ui.Text(label, 13, null, "OnSurfaceVariant", false), slider, value);
        var gamut = Ui.Text("", 12, null, "Warning", true, false);
        gamut.Visibility = Visibility.Collapsed;
        var sliders = Ui.VStack(4, SliderRow("Hue", hue, hueText), SliderRow("Chroma", chroma, chromaText), SliderRow("Tone", tone, toneText), gamut);
        StudioLock(sliders, "seed");
        StudioLock(ringButton, "seed");
        var own = false;
        void Paint()
        {
            var current = AppearanceRgba.TryParseStorageHex(AppearanceRuntime.Current.Seed, out var c) ? c : seed;
            seedFill.Fill = new SolidColorBrush(AppearanceColorPicker.ToWpf(current));
            seedLabel.Text = Loc.T("Seed") + " " + current.ToHex();
            hueText.Text = $"{hue.Value:0}°"; chromaText.Text = $"{chroma.Value:0.000}"; toneText.Text = $"{tone.Value:0}";
            double L = tone.Value / 100, C = chroma.Value, H = hue.Value;
            hue.Background = StudioParts.Gradient(Enumerable.Range(0, 13).Select(i => AppearanceRgba.FromOklch(0.72, 0.14, i * 30, out _)));
            chroma.Background = StudioParts.Gradient(Enumerable.Range(0, 7).Select(i => AppearanceRgba.FromOklch(L, i * 0.37 / 6, H, out _)));
            tone.Background = StudioParts.Gradient(Enumerable.Range(0, 11).Select(i => AppearanceRgba.FromOklch(i / 10.0, C, H, out _)));
            AutomationProperties.SetItemStatus(hue, hueText.Text); AutomationProperties.SetItemStatus(chroma, chromaText.Text); AutomationProperties.SetItemStatus(tone, toneText.Text);
        }
        void Push()
        {
            if (own || studioSyncing) return;
            var c = AppearanceRgba.FromOklch(tone.Value / 100, chroma.Value, hue.Value, out var fits);
            gamut.Text = fits ? "" : Loc.T("That hue and chroma cannot be shown at this tone, so the nearest displayable colour is used.");
            gamut.Visibility = fits ? Visibility.Collapsed : Visibility.Visible;
            own = true;
            try { Look("seed colour set to " + c.ToHex(), m => m.Seed = c.ToHex(), true); }
            finally { own = false; }
            Paint();
        }
        foreach (var s in new[] { hue, chroma, tone }) s.ValueChanged += (_, _) => Push();
        syncs.Add(() =>
        {
            if (own) return;
            if (!AppearanceRgba.TryParseStorageHex(AppearanceRuntime.Current.Seed, out var c)) return;
            var shownColour = AppearanceRgba.FromOklch(tone.Value / 100, chroma.Value, hue.Value, out _);
            if (shownColour.ToHex() != c.ToHex())
            {
                var (l, ch, h) = c.ToOklch();
                own = true;
                try { if (ch > 0.002) hue.Value = h; chroma.Value = Math.Min(ch, 0.37); tone.Value = l * 100; }
                finally { own = false; }
                gamut.Visibility = Visibility.Collapsed;
            }
            Paint();
        });
        Paint();
        var rainbowNote = Ui.Text("Animated rainbow accent is on, so the accent cycles around this seed.", 12, null, "OnSurfaceVariant");
        void Rainbow() => rainbowNote.Visibility = AppearanceRuntime.Current.Rainbow ? Visibility.Visible : Visibility.Collapsed;
        Rainbow();
        syncs.Add(Rainbow);
        var top = new StudioReflow(380, 16, [-120, 1], seedStack, Ui.VStack(6, sliders, rainbowNote));

        // The colour roles the theme generates from the seed, read live from the app's resources.
        var strip = new UniformGrid { Rows = 1, Height = 28 };
        foreach (var key in new[] { "Primary", "OnPrimary", "PrimaryContainer", "OnPrimaryContainer", "SecondaryContainer", "OnSecondaryContainer", "Tertiary", "TertiaryContainer", "OnTertiaryContainer", "SurfaceHighest", "SurfaceHigh", "SurfaceContainer", "Surface" })
        {
            var swatch = StudioParts.RoleSwatch(key, double.NaN, 6);
            swatch.Width = double.NaN; swatch.Height = double.NaN; swatch.Margin = new Thickness(0, 0, 3, 0);
            var name = Loc.T(AppearanceRoles.Find(key)?.Name ?? key);
            swatch.ToolTip = name; AutomationProperties.SetName(swatch, name);
            strip.Children.Add(swatch);
        }
        var palette = Ui.VStack(6, Ui.Overline("Generated colour roles · from the seed and theme"), strip);

        // Contrast of the roles people read most, against the surface they sit on.
        var checks = new StudioColumns(210, 6, 2);
        foreach (var check in StudioContrastChecks)
        {
            var badge = new ContentControl { Focusable = false };
            var row = new Border { MinHeight = 40, Padding = new Thickness(10, 4, 10, 4), Child = Ui.Columns(10, [0, 1, 0], StudioParts.RoleSwatch(check.Role, 24, 6), Ui.Text(check.Name, 13, null, "OnSurface", true), badge) };
            row.SetResourceReference(Border.BackgroundProperty, "SurfaceHigh");
            row.SetResourceReference(Border.CornerRadiusProperty, "RadiusSmall");
            var c = check;
            contrastViews.Add(() =>
            {
                var ratio = c.Ratio();
                var pass = ratio >= c.Need;
                var b = Ui.Badge($"{ratio:0.0}:1", pass ? Tone.Success : Tone.Error, false);
                ((TextBlock)b.Child).FontWeight = FontWeights.Bold;
                badge.Content = b;
                AutomationProperties.SetName(row, $"{Loc.T(c.Name)}: {Loc.T("contrast")} {ratio:0.0} {Loc.T("to 1 against")} {Loc.T(AppearanceRoles.Find(c.Against)?.Name ?? c.Against)}, {Loc.T(pass ? "passes" : "fails")} {(c.Need >= 4.5 ? "AA" : "3:1")}");
            });
            checks.Add(row);
        }
        return StudioPanel(Ui.VStack(14, top, palette, checks), Icons.Palette, "Colour", theme);
    }

    // ----- Motion -----

    private FrameworkElement StudioMotionPanel(List<Action> syncs, AppearancePreview preview)
    {
        var reduced = Ui.Switch("Reduced motion", S.ReducedMotion, v =>
        {
            if (studioSyncing || S.ReducedMotion == v) return;
            S.ReducedMotion = v; Commit("Reduced motion " + (v ? "on" : "off"));
        });
        StudioLock(reduced, "motion", "Reduced motion");
        syncs.Add(() => { if (reduced.IsChecked != S.ReducedMotion) reduced.IsChecked = S.ReducedMotion; });

        var levels = new StudioColumns(96, 8, 4);
        var levelTiles = new List<(Button Tile, string Level)>();
        foreach (var (level, icon) in new[] { ("Off", Icons.Stop), ("Reduced", Icons.Pause), ("Standard", Icons.AutoAwesomeMotion), ("Expressive", Icons.Bolt) })
        {
            var label = Ui.Text(level, 13, null, "OnSurface", false);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            var glyph = Ui.Icon(icon, 22, false, "Primary");
            var tile = StudioParts.Tile(Ui.VStack(6, glyph, label), Loc.T("Motion") + ": " + Loc.T(level), () => Look("motion set to " + level, m => m.MotionLevel = level));
            tile.MinHeight = 72;
            tile.HorizontalContentAlignment = HorizontalAlignment.Center;
            levels.Add(tile);
            levelTiles.Add((tile, level));
        }
        StudioLock(levels, "motionlevel");
        void MarkLevels() { var now = AppearanceRuntime.Current.MotionLevel; foreach (var (tile, level) in levelTiles) StudioParts.Select(tile, level == now); }
        MarkLevels();
        syncs.Add(MarkLevels);

        var speed = StepSlider(0.5, 2, AppearanceRuntime.Current.MotionSpeed, 0.25, double.NaN);
        speed.Width = double.NaN;
        AutomationProperties.SetName(speed, Loc.T("Motion speed"));
        var speedText = Ui.Mono("", 12);
        speedText.TextAlignment = TextAlignment.Right;
        speed.ValueChanged += (_, _) => { speedText.Text = $"{speed.Value:0.00}×"; if (!studioSyncing) Look($"motion speed set to {speed.Value:0.00}x", m => m.MotionSpeed = speed.Value, true); };
        speedText.Text = $"{speed.Value:0.00}×";
        StudioLock(speed, "motionspeed");
        syncs.Add(() => { var now = AppearanceRuntime.Current.MotionSpeed; if (Math.Abs(speed.Value - now) > 0.001) speed.Value = now; });
        var speedRow = Ui.Columns(10, [-64, 1, -52], Ui.Text("Speed", 13, null, "OnSurfaceVariant", false), speed, speedText);
        var transitions = StudioTransitionCards(syncs, preview);
        Action redrawCurve = () => { };
        var springRow = StudioSpringRow(syncs, () => redrawCurve());

        var curve = new StudioEasingCurve();
        var timing = Ui.Text("", 12, null, "OnSurfaceVariant");
        var note = Ui.Caption("");
        var chosen = EasingTokens[0];
        const string springName = "Spring";
        var springChosen = false;
        var chips = new WrapPanel();
        var chipList = new List<ToggleButton>();
        void Draw()
        {
            var effective = Motion.EffectiveLevel;
            var animates = AppearanceMotion.Animates(effective);
            var ms = AppearanceMotion.Duration(AppearanceMotion.Medium2, effective, AppearanceRuntime.Current.MotionSpeed);
            if (springChosen)
            {
                var bounce = AppearanceRuntime.Current.SpringBounce;
                var applies = AppearanceSpring.Applies(effective, bounce);
                var overshoot = AppearanceSpring.Overshoot(bounce) * 100;
                var springText = $"{Loc.T("Spring easing")}: {bounce}% {Loc.T("bounce")}, {overshoot:0}% {Loc.T("overshoot")}" + (applies ? "" : ". " + Loc.T("Used only at the Expressive level; this shows what it would do."));
                curve.ShowSamples(AppearanceSpring.Sample(bounce, 48), springText);
            }
            else
            {
                var description = animates ? $"{Loc.T("Easing curve")}: {Loc.T(chosen.Name)}" : Loc.T("Motion is not running, so changes snap to their final state.");
                curve.Show(chosen.Token, animates, description);
            }
            timing.Text = animates ? $"{Loc.T("A medium transition now lasts")} {ms:0} ms." : Loc.T("Motion is not running, so changes snap to their final state.");
            note.Text = MotionNoteText();
        }
        foreach (var token in EasingTokens)
        {
            var t = token;
            ToggleButton? chip = null;
            chip = Ui.Chip(t.Name, t.Name == chosen.Name, on =>
            {
                if (!on) { if (!springChosen && chosen.Name == t.Name && chip != null) chip.IsChecked = true; return; }
                chosen = t; springChosen = false;
                foreach (var other in chipList) if (other != chip && other.IsChecked == true) other.IsChecked = false;
                Draw();
            });
            chip.Margin = new Thickness(0, 0, 6, 6);
            chip.ToolTip = Loc.T("Shows this Material easing token on the curve. The motion level decides where each token is used.");
            chipList.Add(chip);
            chips.Children.Add(chip);
        }
        ToggleButton? springChip = null;
        springChip = Ui.Chip(springName, false, on =>
        {
            if (!on) { if (springChosen && springChip != null) springChip.IsChecked = true; return; }
            springChosen = true;
            foreach (var other in chipList) if (other != springChip && other.IsChecked == true) other.IsChecked = false;
            Draw();
        });
        springChip.Margin = new Thickness(0, 0, 6, 6);
        springChip.ToolTip = Loc.T("Shows the spring curve Expressive motion uses for movement, with the current spring bounce.");
        chipList.Add(springChip);
        chips.Children.Add(springChip);
        redrawCurve = Draw;
        Draw();
        syncs.Add(Draw);
        var left = Ui.VStack(8, speedRow, springRow, timing, Ui.Overline("Easing tokens"), chips);
        var detail = new StudioReflow(460, 16, [1, -200], left, curve);
        var demo = Ui.Button("Play motion demo", Icons.PlayArrow, ButtonKind.Text, preview.PlayDemo);
        demo.HorizontalAlignment = HorizontalAlignment.Left;
        return StudioPanel(Ui.VStack(12, levels, note, Ui.Overline("Page and tab transition"), transitions, detail, demo), Icons.AutoAwesomeMotion, "Motion", reduced);
    }

    // ----- Shape, density and type -----

    private FrameworkElement StudioShapePanel(List<Action> syncs)
    {
        // Corner shape: three steps of the corner radius scale.
        var corners = new WrapPanel();
        var cornerTiles = new List<(Button Tile, double Scale)>();
        foreach (var (scale, radius, name) in new[] { (0.5, 4.0, "Square corners (0.5x)"), (1.0, 10.0, "Default corners (1x)"), (1.5, 16.0, "Round corners (1.5x)") })
        {
            var shape = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(2) };
            shape.SetResourceReference(Border.BorderBrushProperty, "Outline");
            var tile = StudioParts.Tile(shape, name, () => Look($"corner radius set to {scale:0.00}x", m => m.RadiusScale = scale));
            tile.ToolTip = Loc.T(name);
            tile.Margin = new Thickness(0, 0, 6, 0);
            corners.Children.Add(tile);
            cornerTiles.Add((tile, scale));
        }
        void MarkCorners() { var now = AppearanceRuntime.Current.RadiusScale; foreach (var (tile, scale) in cornerTiles) StudioParts.Select(tile, Math.Abs(now - scale) < 0.01, 6); }
        MarkCorners();
        syncs.Add(MarkCorners);
        StudioLock(corners, "radius");
        var cornerNote = Ui.Text("", 12, null, "OnSurfaceVariant");
        void CornerNote() => cornerNote.Text = $"{AppearanceRuntime.Current.RadiusScale:0.00}x";
        CornerNote(); syncs.Add(CornerNote);

        var density = new StudioChoice(AppearanceState.Densities, () => AppearanceRuntime.Current.Density, v => Look("density set to " + v, m => m.Density = v));
        AutomationProperties.SetName(density, Loc.T("Density"));
        StudioLock(density, "density");
        syncs.Add(density.Sync);

        // Type scale: three common base sizes; the Font size row below offers every size from 12 to 22.
        var sizes = new WrapPanel();
        var sizeTiles = new List<(Button Tile, double Size)>();
        foreach (var (size, display, name) in new[] { (13.0, 13.0, "Small text (13 px)"), (14.0, 16.0, "Default text (14 px)"), (16.0, 20.0, "Large text (16 px)") })
        {
            var sample = Ui.Text("Aa", display, FontWeights.SemiBold, "OnSurface", false, false);
            sample.HorizontalAlignment = HorizontalAlignment.Center;
            var tile = StudioParts.Tile(sample, name, () => Look("font size set to " + size.ToString("0"), m => m.FontSize = size));
            tile.MinWidth = 44; tile.MinHeight = 44; tile.HorizontalContentAlignment = HorizontalAlignment.Center;
            tile.ToolTip = Loc.T(name);
            tile.Margin = new Thickness(0, 0, 6, 0);
            sizes.Children.Add(tile);
            sizeTiles.Add((tile, size));
        }
        var percent = Ui.Mono("", 12, "OnSurfaceVariant");
        void MarkSizes()
        {
            var now = AppearanceRuntime.Current.FontSize;
            foreach (var (tile, size) in sizeTiles) StudioParts.Select(tile, Math.Abs(now - size) < 0.01, 6);
            percent.Text = $"{now:0} px · {now / 14 * 100:0}%";
        }
        MarkSizes();
        syncs.Add(MarkSizes);
        StudioLock(sizes, "fontsize");

        FrameworkElement Group(string title, params UIElement[] content) => Ui.VStack(8, new UIElement[] { Ui.Overline(title) }.Concat(content).ToArray());
        var grid = new StudioReflow(560, 16, [0, 1, 0], Group("Corner shape", corners, cornerNote), Group("Density", density), Group("Type scale", sizes, percent));
        return StudioPanel(grid);
    }
}
