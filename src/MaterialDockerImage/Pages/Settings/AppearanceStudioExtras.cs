using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDockerImage.Services;
using MaterialDockerImage.Shell;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// The parts of the appearance studio that edit motion patterns and compare schemes: transition pattern cards, the spring
/// bounce slider, the "From wallpaper" preset tile and the A/B comparison. Every edit goes through
/// <see cref="AppearanceRuntime"/>, so it is saved, undoable, recorded in local history and held back by locks.
/// </summary>
public sealed partial class SettingsPage
{
    /// <summary>The last wallpaper reading, shared by the preset tile and the A/B comparison. Null until the person asks.</summary>
    private WallpaperColourResult? wallpaperResult;
    private event Action? WallpaperRead;

    // ----- Transition pattern -----

    private static readonly (string Pattern, string Icon)[] TransitionCards =
    [
        (AppearanceTransitions.SharedAxis, Icons.SwapHoriz), (AppearanceTransitions.FadeThrough, Icons.Layers),
        (AppearanceTransitions.ContainerTransform, Icons.OpenInFull), (AppearanceTransitions.Slide, Icons.ArrowForward)
    ];

    /// <summary>Four cards that choose the page and tab transition. Choosing one also plays it on the live preview.</summary>
    private FrameworkElement StudioTransitionCards(List<Action> syncs, AppearancePreview preview)
    {
        var cards = new StudioColumns(120, 8, 4);
        var tiles = new List<(Button Tile, string Pattern)>();
        foreach (var (pattern, icon) in TransitionCards)
        {
            var name = AppearanceTransitions.Name(pattern);
            var label = Ui.Text(name, 13, null, "OnSurface", true);
            label.TextAlignment = TextAlignment.Center;
            var glyph = Ui.Icon(icon, 22, false, "Primary");
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            var p = pattern;
            var tile = StudioParts.Tile(Ui.VStack(6, glyph, label), Loc.T("Transition") + ": " + Loc.T(name), () =>
            {
                Look("transition pattern set to " + AppearanceTransitions.Name(p), m => m.TransitionPattern = p);
                preview.PlayTransition();
            });
            tile.MinHeight = 72;
            tile.HorizontalContentAlignment = HorizontalAlignment.Center;
            tile.ToolTip = Loc.T(AppearanceTransitions.Describe(pattern)) + " " + Loc.T("Choosing it plays it on the live preview.");
            cards.Add(tile);
            tiles.Add((tile, pattern));
        }
        StudioLock(cards, "transition");
        void Mark() { var now = AppearanceRuntime.Current.TransitionPattern; foreach (var (tile, pattern) in tiles) StudioParts.Select(tile, pattern == now); }
        Mark();
        syncs.Add(Mark);
        return cards;
    }

    // ----- Spring bounce -----

    /// <summary>Whether the spring bounce can change anything right now, and if not, why.</summary>
    private string? SpringBlockedReason()
    {
        var level = AppearanceRuntime.Current.MotionLevel;
        if (level != "Expressive") return Loc.T("Spring bounce only applies when motion is Expressive. Choose Expressive above to use it.");
        if (Motion.EffectiveLevel != "Expressive") return Loc.T("Spring bounce is paused because Reduced motion, Low stimulation or the Windows animation setting turns movement off.");
        return null;
    }

    /// <summary>The spring bounce slider (0 to 100%), enabled only when Expressive motion actually runs.</summary>
    private FrameworkElement StudioSpringRow(List<Action> syncs, Action redraw)
    {
        var slider = StepSlider(AppearanceSpring.Minimum, AppearanceSpring.Maximum, AppearanceRuntime.Current.SpringBounce, 5, double.NaN);
        slider.Width = double.NaN;
        AutomationProperties.SetName(slider, Loc.T("Spring bounce"));
        var value = Ui.Mono("", 12);
        value.TextAlignment = TextAlignment.Right;
        void Text() { value.Text = $"{slider.Value:0}%"; AutomationProperties.SetItemStatus(slider, value.Text); }
        Text();
        slider.ValueChanged += (_, _) =>
        {
            Text();
            if (!studioSyncing) Look($"spring bounce set to {slider.Value:0}%", m => m.SpringBounce = (int)Math.Round(slider.Value), true);
            redraw();
        };
        StudioLock(slider, "springbounce");
        var row = Ui.Columns(10, [-64, 1, -52], Ui.Text("Bounce", 13, null, "OnSurfaceVariant", false), slider, value);
        row.ToolTip = Loc.T("How far Expressive movement overshoots before it settles, from none to about 20%.");
        var reason = Ui.Text("", 12, null, "OnSurfaceVariant", true, false);
        void Gate()
        {
            var now = AppearanceRuntime.Current.SpringBounce;
            if (Math.Abs(slider.Value - now) > 0.001) slider.Value = now;
            var blocked = SpringBlockedReason();
            row.IsEnabled = blocked == null; // the lock keeps the slider itself disabled separately
            reason.Text = blocked ?? "";
            reason.Visibility = blocked == null ? Visibility.Collapsed : Visibility.Visible;
        }
        Gate();
        syncs.Add(Gate);
        return Ui.VStack(4, row, reason);
    }

    // ----- From wallpaper -----

    /// <summary>
    /// A preset tile that reads the Windows desktop background and sets the seed colour from it. Reading happens only when
    /// the tile is chosen (never in captures), off the interface thread, and never writes anything to Windows.
    /// </summary>
    private FrameworkElement StudioWallpaperTile()
    {
        var swatch = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
        swatch.SetResourceReference(Border.BorderBrushProperty, "OutlineVariant");
        swatch.SetResourceReference(Border.BackgroundProperty, "SurfaceHighest");
        var note = Ui.Text("Seed from the Windows desktop", 12, null, "OnSurfaceVariant", true);
        var text = Ui.VStack(1, Ui.Text("From wallpaper", 14, FontWeights.SemiBold, "OnSurface", false), note);
        var status = Ui.Text("", 12, null, "OnSurfaceVariant", true, false);
        status.Visibility = Visibility.Collapsed;
        var reading = false;
        Button? tile = null;
        tile = StudioParts.Tile(Ui.Columns(10, [0, 1], swatch, text), "Apply the seed colour from the desktop wallpaper", async () =>
        {
            if (reading) return;
            if (Host.IsCapture) { status.Text = Loc.T("The wallpaper is not read in documentation captures."); status.Visibility = Visibility.Visible; return; }
            reading = true; tile!.IsEnabled = false;
            status.Text = Loc.T("Reading the desktop wallpaper…"); status.Visibility = Visibility.Visible;
            try
            {
                var result = await WallpaperColourReader.ReadAsync();
                wallpaperResult = result;
                WallpaperRead?.Invoke();
                if (result.Seed is { } seed && AppearanceRgba.TryParseStorageHex(seed, out var colour))
                {
                    swatch.Background = new SolidColorBrush(AppearanceColorPicker.ToWpf(colour));
                    status.Text = Loc.T(result.Detail) + " " + Loc.T("Seed") + " " + seed + ".";
                    Look("seed colour set from the desktop wallpaper (" + seed + ")", m => m.Seed = seed);
                }
                else status.Text = Loc.T(result.Detail) + " " + Loc.T("The seed colour was not changed.");
            }
            finally { reading = false; tile.IsEnabled = !LockUi.IsLocked("seed"); }
        });
        tile.ToolTip = Loc.T("Reads the current desktop picture (or the solid background colour) and uses its most prominent colour as the seed. Nothing in Windows changes. Undo returns to the previous seed.");
        StudioLock(tile, "seed");
        return Ui.VStack(4, tile, status);
    }

    // ----- Compare A/B -----

    /// <summary>A candidate scheme for side B: a preset, the wallpaper colour or a typed seed.</summary>
    private sealed record CompareCandidate(string Name, Func<AppearanceState, AppearanceState?> Build);

    /// <summary>
    /// Shows the current appearance (A) beside a candidate (B). B's colours are computed with <see cref="Theme.Roles"/> into a
    /// resource dictionary that belongs to its own preview only, so the live theme never changes until "Apply B".
    /// </summary>
    private FrameworkElement StudioComparePanel(List<Action> syncs)
    {
        WallpaperRead = null; // the studio is rebuilt with the section; only the newest comparison listens
        var candidates = new List<CompareCandidate>();
        foreach (var preset in AppearanceRuntime.AllPresets)
        {
            var p = preset;
            candidates.Add(new(p.BuiltIn ? Loc.T(p.Name) : p.Name, current => current.WithLookOf(p.State)));
        }
        candidates.Add(new(Loc.T("Wallpaper colour"), current => wallpaperResult?.Seed is { } seed ? With(current, seed) : null));
        candidates.Add(new(Loc.T("Typed seed colour"), current => AppearanceRgba.TryParseStorageHex(seedBox?.Text.Trim(), out var c) ? With(current, c.ToHex()) : null));
        static AppearanceState With(AppearanceState current, string seed) { var next = current.Clone(); next.Seed = seed; next.Normalize(); return next; }

        var combo = new ComboBox { MinWidth = 200 };
        foreach (var c in candidates) combo.Items.Add(new ComboBoxItem { Content = c.Name });
        combo.SelectedIndex = Math.Min(1, candidates.Count - 1);
        AutomationProperties.SetName(combo, Loc.T("Candidate for B"));
        seedBox = Ui.Input("#0B7A99", Loc.T("Seed colour for B"), true);
        seedBox.Width = 120;
        var seedLabel = Ui.Text("Seed", 13, null, "OnSurfaceVariant", false);
        var pick = Ui.Columns(10, [0, 1, 0, 0], Ui.Text("B is", 13, null, "OnSurfaceVariant", false), combo, seedLabel, seedBox);

        var previewA = new AppearancePreview();
        var previewB = new AppearancePreview();
        var verdictA = new ContentControl { Focusable = false }; var verdictB = new ContentControl { Focusable = false };
        var nameB = Ui.Text("", 13, FontWeights.SemiBold, "OnSurface", true, false);
        var problem = Ui.Text("", 12, null, "Warning", true, false);
        FrameworkElement Side(string title, UIElement name, UIElement verdict, UIElement preview) =>
            Ui.VStack(8, Ui.Columns(8, [0, 1, 0], Ui.Text(title, 13, FontWeights.Bold, "Primary", false), name, verdict), preview);
        var sides = new StudioReflow(560, 16, [1, 1], Side("A · current", Ui.Text("Your appearance now", 13, null, "OnSurfaceVariant", true), verdictA, previewA), Side("B ·", nameB, verdictB, previewB));
        AppearanceState? candidate = null;
        var applyB = Ui.Button("Apply B", Icons.Check, ButtonKind.Filled, () =>
        {
            if (candidate == null) return;
            AppearanceRuntime.Set(AppearanceRuntime.Current.WithLookOf(candidate), "applied comparison B: " + nameB.Text);
        });
        applyB.ToolTip = Loc.T("Makes B your appearance. Motion settings stay as they are, locked values keep their value, and Undo returns to A.");

        Border Verdict(Func<StudioContrastCheck, double> ratio)
        {
            var below = StudioContrastChecks.Count(c => ratio(c) < c.Need);
            return below == 0 ? StudioParts.StatusBadge(Loc.T("All roles pass AA"), Icons.CheckCircle, Tone.Success) : StudioParts.StatusBadge($"{below} {Loc.T("role(s) below AA")}", Icons.Warning, Tone.Error);
        }
        void Refresh()
        {
            var chosen = candidates[Math.Max(0, combo.SelectedIndex)];
            seedBox.Visibility = seedLabel.Visibility = chosen == candidates[^1] ? Visibility.Visible : Visibility.Collapsed;
            var current = AppearanceRuntime.Current;
            candidate = chosen.Build(current);
            nameB.Text = chosen.Name;
            verdictA.Content = Verdict(c => c.Ratio());
            if (candidate == null)
            {
                problem.Text = chosen == candidates[^1] ? Loc.T("Type a hex colour such as #0B7A99 for B.") : Loc.T("Choose From wallpaper in Presets first, so there is a wallpaper colour to compare.");
                problem.Visibility = Visibility.Visible;
                previewB.Resources = new ResourceDictionary();
                previewB.Opacity = 0.38;
                verdictB.Content = null;
                applyB.IsEnabled = false;
                return;
            }
            problem.Visibility = Visibility.Collapsed;
            previewB.Opacity = 1;
            var colours = Theme.Roles(candidate, S.HighContrast, Theme.ResolvesDark(candidate.Theme)).ToDictionary(r => r.Key, r => r.Color);
            var resources = new ResourceDictionary();
            foreach (var (key, colour) in colours) { var brush = new SolidColorBrush(colour); brush.Freeze(); resources[key] = brush; }
            var r = candidate.RadiusScale;
            resources["RadiusSmall"] = new CornerRadius(8 * r); resources["RadiusMedium"] = new CornerRadius(12 * r);
            resources["RadiusLarge"] = new CornerRadius(16 * r); resources["RadiusPill"] = new CornerRadius(20 * r);
            previewB.Resources = resources;
            verdictB.Content = Verdict(c => c.Ratio(key => colours.TryGetValue(key, out var v) ? v : Colors.Transparent));
            applyB.IsEnabled = !candidate.SameAs(current);
            AutomationProperties.SetName(previewB, Loc.T("Appearance preview") + " B: " + chosen.Name);
        }
        AutomationProperties.SetName(previewA, Loc.T("Appearance preview") + " A");
        combo.SelectionChanged += (_, _) => Refresh();
        seedBox.TextChanged += (_, _) => Refresh();
        WallpaperRead += Refresh;
        Refresh();
        syncs.Add(Refresh);
        var note = Ui.Caption("B is computed without changing your appearance. Both previews are examples: nothing in them runs.");
        var footer = Ui.Wrap(8, applyB);
        var close = Ui.IconButton(Icons.Close, "Close the comparison", () => ToggleCompare(false));
        close.Width = close.Height = close.MinHeight = 36; close.FontSize = 20;
        return StudioPanel(Ui.VStack(12, pick, problem, sides, note, footer), Icons.CompareArrows, "Compare A/B", close);
    }

    private TextBox? seedBox;
    private FrameworkElement? motionPanel;

    /// <summary>Scrolls the studio's motion panel into view for documentation captures. Returns false when the studio is not built.</summary>
    public bool CaptureMotion()
    {
        if (motionPanel == null) return false;
        motionPanel.BringIntoView();
        return true;
    }
    private FrameworkElement? comparePanel;

    /// <summary>Shows or hides the A/B comparison below the studio panes.</summary>
    private void ToggleCompare(bool? show = null)
    {
        if (comparePanel == null) return;
        var visible = show ?? comparePanel.Visibility != Visibility.Visible;
        comparePanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (visible) comparePanel.BringIntoView();
    }

    /// <summary>Opens the comparison for documentation captures. Returns false when the studio is not built.</summary>
    public bool CaptureCompare(bool show)
    {
        if (comparePanel == null) return false;
        ToggleCompare(show);
        return true;
    }
}
