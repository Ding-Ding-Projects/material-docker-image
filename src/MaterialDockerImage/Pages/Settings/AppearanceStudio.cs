using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDockerImage.Services;
using MaterialDockerImage.Shell;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// The appearance studio at the top of Settings, Appearance: presets, colour, motion and shape editors and a live preview,
/// laid out in three panes on a wide area and stacked when narrow. Every control edits the real appearance through
/// <see cref="AppearanceRuntime"/> (saved, undoable and guarded by locks); the detailed rows below stay available.
/// </summary>
public sealed partial class SettingsPage
{
    private Action? studioSettingsListener;
    private bool studioSyncing;
    private bool contrastQueued;
    private readonly List<Action> contrastViews = new();

    private FrameworkElement AppearanceStudio(List<Action> syncs, AppearancePreview preview)
    {
        contrastViews.Clear();
        if (studioSettingsListener != null) Host.SettingsChanged -= studioSettingsListener;
        // Settings outside the appearance model (high contrast, reduced motion) also change colours and motion.
        studioSettingsListener = () => { RunStudioSyncs(syncs); QueueContrast(); };
        Host.SettingsChanged += studioSettingsListener;
        syncs.Add(QueueContrast);

        var presets = StudioPresets(syncs);
        var colour = StudioColourPanel(syncs);
        var motion = StudioMotionPanel(syncs, preview);
        motionPanel = motion;
        var shape = StudioShapePanel(syncs);
        var editors = Ui.VStack(12, colour, motion, shape);
        var live = StudioPreviewPanel(syncs, preview);
        comparePanel = StudioComparePanel(syncs);
        comparePanel.Visibility = Visibility.Collapsed;

        // Search: filters the studio panels and the appearance rows below.
        var search = new SearchFilter("Search appearance", 260);
        var searchState = Ui.Caption("");
        searchState.Visibility = Visibility.Collapsed;
        var panels = new (FrameworkElement Element, string Words)[]
        {
            (presets, "Presets preset theme file import save export wallpaper desktop background"), (colour, "Colour color theme light dark system seed hue chroma tone roles contrast palette"),
            (motion, "Motion animation reduced motion speed easing transition level pattern shared axis fade through container transform slide spring bounce overshoot"), (shape, "Corner shape radius density compact comfortable spacious type scale font size text"),
            (live, "Live preview contrast reset undo compare candidate apply"), (comparePanel, "Compare candidate preset seed wallpaper apply")
        };
        void ApplyStudioSearch()
        {
            var hits = 0;
            foreach (var (element, words) in panels)
            {
                if (element == comparePanel && !search.IsActive) continue; // the comparison stays as the person left it
                var show = !search.IsActive || search.Matches(words + " " + string.Join(" ", words.Split(' ').Select(Loc.T)));
                element.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (show && search.IsActive) hits++;
            }
            foreach (var row in rows.All.Where(r => r.Section == "Appearance" && r.Id != "appearancestudio"))
            {
                var show = !search.IsActive || search.Matches(row.SearchText);
                row.Element.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (show && search.IsActive) hits++;
            }
            searchState.Visibility = search.IsActive ? Visibility.Visible : Visibility.Collapsed;
            searchState.Text = search.Status.Length > 0 ? search.Status : hits == 0 ? Loc.T("Nothing in Appearance matches.") : $"{hits} {Loc.T("matching panel(s) and setting(s)")}";
        }
        search.Changed += ApplyStudioSearch;

        var export = Ui.Button("Export theme", Icons.FileExport, ButtonKind.Outlined, () => ExportPreset(new AppearancePreset(Loc.T("My appearance"), AppearanceRuntime.Current)));
        export.ToolTip = Loc.T("Saves the current appearance as a preset file you can import here or on another computer.");
        var save = Ui.AsyncButton("Save as preset", Icons.Save, ButtonKind.Filled, () => SaveCurrentPresetAsync(null));
        var toolbar = Ui.Wrap(8, search, export, save);

        // Three panes when wide, preview beside the editors when medium, one column when narrow.
        var body = new Grid();
        body.Children.Add(presets); body.Children.Add(editors); body.Children.Add(live);
        bool? mode = null;
        void Arrange(double width)
        {
            var next = width >= 1080 ? true : width >= 760 ? (bool?)false : null;
            if (mode == next && body.ColumnDefinitions.Count > 0) return;
            mode = next;
            body.ColumnDefinitions.Clear(); body.RowDefinitions.Clear();
            void Col(double w) => body.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(-w) : new GridLength(w, GridUnitType.Star) });
            void Row(double h) => body.RowDefinitions.Add(new RowDefinition { Height = h < 0 ? new GridLength(-h) : GridLength.Auto });
            if (next == true)
            {
                Col(-260); Col(-12); Col(1); Col(-12); Col(-380);
                presets.At(0, 0); editors.At(0, 2); live.At(0, 4);
            }
            else if (next == false)
            {
                Col(1); Col(-12); Col(-340); Row(0); Row(-12); Row(0);
                presets.At(0, 0, 1, 3); editors.At(2, 0); live.At(2, 2);
            }
            else
            {
                Col(1); Row(0); Row(-12); Row(0); Row(-12); Row(0);
                presets.At(0, 0); editors.At(2, 0); live.At(4, 0);
            }
        }
        body.SizeChanged += (_, e) => Arrange(e.NewSize.Width);
        Arrange(0);
        QueueContrast();
        return Ui.VStack(12, toolbar, searchState, body, comparePanel);
    }

    /// <summary>Runs the studio's synchronisers without letting their own control updates save anything.</summary>
    private void RunStudioSyncs(List<Action> syncs)
    {
        if (studioSyncing) return;
        studioSyncing = true;
        try { foreach (var sync in syncs.ToList()) sync(); }
        finally { studioSyncing = false; }
    }

    /// <summary>Recomputes contrast once the new colour resources are in place (they are written while settings save).</summary>
    private void QueueContrast()
    {
        if (contrastQueued) return;
        contrastQueued = true;
        Dispatcher.BeginInvoke(() => { contrastQueued = false; foreach (var view in contrastViews.ToList()) view(); }, System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>Disables a studio control while the matching settings lock is on; a click on it then offers the unlock.</summary>
    private void StudioLock(FrameworkElement control, string id, string? label = null)
    {
        label ??= AppearanceLocks.LabelFor(id) ?? id;
        LockUi.SetClickTarget(control, id, label);
        void Apply() => control.IsEnabled = !LockUi.IsLocked(id);
        LockUi.Subscribe(id, control, Apply);
        Apply();
    }

    /// <summary>A studio panel: a raised surface with an optional icon and title row.</summary>
    private static Border StudioPanel(UIElement content, string? icon = null, string? title = null, UIElement? trailing = null)
    {
        var stack = new StackPanel();
        if (title != null)
        {
            var head = Ui.Columns(10, [0, 1, 0], icon == null ? new Border() : Ui.Icon(icon, 20, false, "Primary"), Ui.Text(title, 15, FontWeights.SemiBold, "OnSurface", false), trailing ?? new Border());
            head.Margin = new Thickness(0, 0, 0, 12);
            stack.Children.Add(head);
        }
        stack.Children.Add(content);
        return Ui.Card(stack, 16, "SurfaceContainer", 16);
    }

    // ----- Presets -----

    private FrameworkElement StudioPresets(List<Action> syncs)
    {
        var tiles = new StudioColumns(200, 8);
        var shown = new List<(Button Tile, AppearancePreset Preset)>();
        var signature = "";
        System.Windows.Media.Color Hex(string? value, string fallback) => AppearanceRgba.TryParseStorageHex(value ?? fallback, out var c) ? AppearanceColorPicker.ToWpf(c) : Colors.Gray;
        FrameworkElement Swatches(AppearanceState s)
        {
            var dark = s.Theme switch { "Light" => false, "System" => Theme.SystemPrefersDark(), _ => true };
            // Accent, tertiary, surface and text: the preset's own values, or the theme's defaults where it keeps them.
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Rows = 2, Columns = 2, Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Center };
            foreach (var colour in new[] { Hex(s.Roles.GetValueOrDefault("Primary"), s.Seed), Hex(s.Roles.GetValueOrDefault("Tertiary"), dark ? "#82D3E0" : "#00687A"), Hex(s.Roles.GetValueOrDefault("Surface"), dark ? "#111318" : "#F9F9FF"), Hex(s.Roles.GetValueOrDefault("OnSurface"), dark ? "#E2E2E9" : "#1A1B20") })
                grid.Children.Add(StudioParts.ColourSwatch(colour, 14, 4).Margin(0, 0, 2, 2));
            return grid;
        }
        void Mark()
        {
            var current = AppearanceRuntime.Current;
            foreach (var (tile, preset) in shown) StudioParts.Select(tile, current.WithLookOf(preset.State).SameAs(current));
        }
        void Fill()
        {
            var all = AppearanceRuntime.AllPresets.ToList();
            var next = string.Join("|", all.Select(p => p.Id + p.State.ToJson().GetHashCode()));
            if (next == signature) { Mark(); return; }
            signature = next;
            var focused = shown.FirstOrDefault(s => s.Tile.IsKeyboardFocusWithin).Preset?.Id;
            tiles.Children.Clear(); shown.Clear();
            foreach (var preset in all)
            {
                var theme = Loc.T(preset.State.Theme);
                var note = preset.BuiltIn ? theme + " · " + Loc.T("built-in") : theme + " · " + Loc.T("saved on this computer");
                var text = Ui.VStack(1, Ui.Text(preset.Name, 14, FontWeights.SemiBold, "OnSurface", false, preset.BuiltIn), Ui.Text(note, 12, null, "OnSurfaceVariant", false, false));
                var p = preset;
                var tile = StudioParts.Tile(Ui.Columns(10, [0, 1], Swatches(preset.State), text), Loc.T("Apply preset") + " " + (preset.BuiltIn ? Loc.T(preset.Name) : preset.Name), () => AppearanceRuntime.ApplyPreset(p));
                tile.ToolTip = Loc.T("Applies the colours, shape, density, fonts and effects of this preset. Motion stays as it is. Undo returns to the current look.");
                tiles.Add(tile);
                shown.Add((tile, preset));
                if (preset.Id == focused) tile.Focus();
            }
            Mark();
        }
        Fill();
        syncs.Add(Fill);
        var import = Ui.AsyncButton("Import theme file", Icons.Upload, ButtonKind.Outlined, () => ImportPresetAsync(null));
        import.HorizontalAlignment = HorizontalAlignment.Stretch;
        var wallpaper = StudioWallpaperTile();
        var problem = AppearanceRuntime.PresetProblem is { } issue ? Ui.Text(Loc.T("appearance-presets.json could not be read, so saving presets is paused:") + " " + issue, 12, null, "Error", true, false) : null;
        var panel = StudioPanel(Ui.VStack(10, Ui.Overline("Presets"), tiles, wallpaper, problem, import));
        return panel;
    }

    // ----- Live preview -----

    private FrameworkElement StudioPreviewPanel(List<Action> syncs, AppearancePreview preview)
    {
        var verdict = new ContentControl { Focusable = false };
        contrastViews.Add(() =>
        {
            var below = StudioContrastChecks.Count(c => c.Ratio() < c.Need);
            verdict.Content = below == 0 ? StudioParts.StatusBadge(Loc.T("All roles pass AA"), Icons.CheckCircle, Tone.Success)
                : StudioParts.StatusBadge($"{below} {Loc.T("role(s) below AA")}", Icons.Warning, Tone.Error);
        });
        var replay = Ui.IconButton(Icons.Refresh, "Replay the motion demo", preview.PlayDemo);
        replay.Width = replay.Height = replay.MinHeight = 36; replay.FontSize = 20;
        var head = Ui.Columns(8, [1, 0, 0], Ui.Text("Live preview", 15, FontWeights.SemiBold, "OnSurface", false), verdict, replay);
        var reset = Ui.AsyncButton("Reset to default", Icons.RestartAlt, ButtonKind.Outlined, ResetAppearanceAsync);
        var undo = Ui.Button("Undo last change", Icons.Undo, ButtonKind.Tonal, AppearanceRuntime.Undo);
        void UpdateUndo() => undo.IsEnabled = AppearanceRuntime.CanUndo;
        UpdateUndo();
        syncs.Add(UpdateUndo);
        foreach (var b in new[] { reset, undo }) b.HorizontalAlignment = HorizontalAlignment.Stretch;
        var compare = Ui.Button("Compare A/B", Icons.CompareArrows, ButtonKind.Tonal, () => ToggleCompare());
        compare.ToolTip = Loc.T("Shows your appearance beside a candidate preset or seed without changing anything until you apply it.");
        compare.HorizontalAlignment = HorizontalAlignment.Stretch;
        var footer = new StudioColumns(230, 8, 2);
        footer.Add(reset); footer.Add(undo);
        var note = Ui.Caption("Example content drawn with your current appearance. Nothing in it runs.");
        return StudioPanel(Ui.VStack(12, head, preview, note, footer, compare));
    }
}
