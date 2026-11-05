using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDockerImage.Services;
using MaterialDockerImage.Shell;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage.Pages;

/// <summary>
/// The Appearance section: live preview, presets, undo and redo, theme and colour (infinite picker and per-role
/// overrides), shape, density, typography, motion and effects. Every change goes through <see cref="AppearanceRuntime"/>
/// so it is saved, recorded in local history and undoable.
/// </summary>
public sealed partial class SettingsPage
{
    private static string? selectedRole;
    private Action? appearanceListener;

    /// <summary>Applies an appearance change without rebuilding this page (the control being used keeps focus).</summary>
    private void Look(string label, Action<AppearanceState> mutate, bool coalesce = false)
    {
        AppearanceRuntime.Change(label, mutate, coalesce, rows.Guard);
        rows.UpdateProvenance();
    }

    /// <summary>A control followed by a reset button that returns just this property to its default (one undo step).</summary>
    private static FrameworkElement Resettable(FrameworkElement control, string what, Action<AppearanceState, AppearanceState> restore)
    {
        var reset = Ui.IconButton(Icons.RestartAlt, Loc.T("Reset") + " " + what + " to default", () => AppearanceRuntime.Change(what + " reset to default", m => restore(m, new AppearanceState())));
        reset.Width = reset.Height = reset.MinHeight = 32; reset.FontSize = 18;
        // A grid (not a horizontal stack) so wide controls such as the colour picker get the full row width.
        var row = Ui.Columns(8, [1, 0], control, reset);
        reset.VerticalAlignment = VerticalAlignment.Top;
        row.VerticalAlignment = VerticalAlignment.Center;
        return row;
    }

    private static Slider StepSlider(double min, double max, double value, double step, double width = 220)
        => new() { Minimum = min, Maximum = max, Value = value, TickFrequency = step, IsSnapToTickEnabled = true, SmallChange = step, LargeChange = step * 4, Width = width };

    private void AppearanceEditorSection()
    {
        if (appearanceListener != null) AppearanceRuntime.Changed -= appearanceListener;
        var syncs = new List<Action>();
        appearanceListener = () => RunStudioSyncs(syncs);
        AppearanceRuntime.Changed += appearanceListener;
        var a = AppearanceRuntime.Current;

        // ----- Studio: presets, colour, motion, shape and the live preview -----
        var preview = new AppearancePreview();
        rows.Add("appearancestudio", "Appearance studio", "Every change applies live and is saved in settings.json. Undo keeps the last 100 appearance changes.",
            AppearanceStudio(syncs, preview), null, null, true);

        // ----- Presets, undo, redo, reset, copy and paste -----
        rows.Add("presets", "Presets and history", "Apply a named look, save your own, export or import one, copy or paste a style, and undo or redo up to 100 appearance changes.",
            PresetsBar(syncs), null,
            "Presets change colours, shape, density, fonts and effects; they keep your motion level and speed. User presets are saved in appearance-presets.json in the data folder. Every change is also recorded in local history (Activity) as \"Appearance: …\".", true);

        // ----- Title-bar logo -----
        rows.Add("logo", "Title-bar logo", "Choose the mark shown at the left of the title bar: the default, any bundled Material Symbol, or an image from your computer. The installed app icon and package identity do not change.",
            LogoControl(), s => s.LogoKind == LogoKinds.Symbol ? s.LogoSymbol : s.LogoKind,
            "A custom image is checked by its actual bytes (PNG, ICO or JPG, up to 1 MiB and 2048 by 2048 pixels), copied into the local app data folder and never uploaded. If the file later goes missing or is damaged, the default logo is shown with a notice. Every change is recorded in local history.", true);

        // ----- Theme and colour -----
        rows.Add("theme", "Theme", "Chooses light or dark colours, or follows the Windows app theme.",
            Resettable(Synced(new StudioChoice(AppearanceState.Themes, () => AppearanceRuntime.Current.Theme, v => Look("theme set to " + v, m => m.Theme = v)), syncs), "theme", (m, d) => m.Theme = d.Theme), s => s.Theme,
            "System reads the Windows \"app mode\" setting when the theme is applied (at start and whenever a setting changes).");

        AppearanceRgba.TryParseStorageHex(a.Seed, out var seed);
        var seedPicker = new AppearanceColorPicker(seed, false, ["Surface", "SurfaceLow"], a.Rainbow, "Animated rainbow accent");
        var ownSeed = false;
        seedPicker.ColorChanged += c => { ownSeed = true; Look("seed colour set to " + c.ToHex(), m => m.Seed = c.ToHex(), true); ownSeed = false; };
        seedPicker.RainbowChanged += on => Look("rainbow accent " + (on ? "on" : "off"), m => m.Rainbow = on);
        syncs.Add(() => { if (ownSeed) return; var now = AppearanceRuntime.Current; if (AppearanceRgba.TryParseStorageHex(now.Seed, out var c) && c.ToHex() != seedPicker.Current.ToHex()) seedPicker.SetColor(c); seedPicker.SetRainbow(now.Rainbow); });
        rows.Add("seed", "Seed colour", "Tints the primary and secondary roles. Drag in the field, use the sliders, or type any format into the translator.",
            Resettable(seedPicker, "seed colour", (m, d) => { m.Seed = d.Seed; m.Rainbow = d.Rainbow; }), s => s.SeedColor,
            "The theme keeps lightness from its palette so text stays readable; the seed sets the hue, and very grey seeds also lower saturation for a neutral look. Error, warning and success colours never follow the seed. Contrast badges compare the seed with the window surfaces.", true, null, "#3A6FD8");

        rows.Add("roles", "Colour roles", "Override any individual Material colour role, reset one role, or reset them all.",
            RoleEditor(syncs), s => (s.RoleOverrides?.Count ?? 0) == 0 ? "" : s.RoleOverrides!.Count + " override(s)",
            "Overrides apply after the theme, seed, surface tint and high contrast, so they always win. Each role shows its contrast against the colour it is normally read with. Choosing Animated rainbow for a role cycles its hue (only while motion is on).", true, v => v is string { Length: > 0 } t ? t : "none");

        rows.Add("contrast", "High contrast", "Makes surfaces and outlines stronger so edges and secondary text are easier to see.",
            SyncedToggle(syncs, () => S.HighContrast, v => { S.HighContrast = v; Commit("High contrast " + (v ? "on" : "off")); }), s => s.HighContrast);

        // ----- Shape -----
        var radiusText = Ui.Text($"{a.RadiusScale:0.00}x", 13, null, "OnSurfaceVariant", false, false);
        var radius = StepSlider(0.5, 1.5, a.RadiusScale, 0.05);
        radius.ValueChanged += (_, e) => { radiusText.Text = $"{radius.Value:0.00}x"; if (!studioSyncing) Look($"corner radius set to {radius.Value:0.00}x", m => m.RadiusScale = radius.Value, true); };
        syncs.Add(() => { var now = AppearanceRuntime.Current.RadiusScale; if (Math.Abs(radius.Value - now) > 0.001) radius.Value = now; });
        FrameworkElement Shape(string key, string label)
        {
            var b = new Border { Width = 56, Height = 36, BorderThickness = new Thickness(1), Child = Ui.Text(label, 11, null, "OnSecondaryContainer", false, false) };
            ((TextBlock)b.Child).HorizontalAlignment = HorizontalAlignment.Center;
            b.SetResourceReference(Border.BackgroundProperty, "SecondaryContainer");
            b.SetResourceReference(Border.BorderBrushProperty, "Outline");
            b.SetResourceReference(Border.CornerRadiusProperty, key);
            return b;
        }
        rows.Add("radius", "Corner radius", "Scales the rounding of cards, buttons, fields, chips and menus from 0.5x (squarer) to 1.5x (rounder).",
            Ui.VStack(10, Resettable(Ui.HStack(12, radius, radiusText), "corner radius", (m, d) => m.RadiusScale = d.RadiusScale), Ui.HStack(8, Shape("RadiusSmall", "Field"), Shape("RadiusMedium", "Menu"), Shape("RadiusLarge", "Card"), Shape("RadiusPill", "Button"))),
            s => s.CornerScale, null, false, v => $"{v:0.00}x");

        // ----- Density -----
        rows.Add("density", "Density", "Sets the height of buttons, fields and segmented controls and their side padding.",
            Resettable(Synced(new StudioChoice(AppearanceState.Densities, () => AppearanceRuntime.Current.Density, v => Look("density set to " + v, m => m.Density = v)), syncs), "density", (m, d) => m.Density = d.Density), s => s.Density,
            "Compact uses 36-pixel targets, Comfortable 40 and Spacious 44. Larger targets raises every target to at least 48 pixels whatever the density.");
        rows.Add("targets", "Larger targets", "Raises the shared target height to at least 48 pixels so controls are easier to hit.",
            SyncedToggle(syncs, () => S.LargerTargets, v => { S.LargerTargets = v; Commit("Larger targets " + (v ? "on" : "off")); }), s => s.LargerTargets);

        // ----- Typography -----
        var uiFont = new AppearanceFontPicker(a.UiFont, AppearanceRuntime.DefaultTextFont, "Build, run and ship containers. 0123456789 · 建立映像", false);
        uiFont.Picked += name => Look(name.Length == 0 ? "interface font reset to default" : "interface font set to " + name, m => m.UiFont = name);
        rows.Add("uifont", "Interface font", "The font for labels, text and headings. Search every font installed on this computer; the sample updates live.",
            uiFont, s => s.UiFontFamily, "Fonts that lack a character fall back to Segoe UI for it. Icons always use the bundled Material Symbols font.", true, v => v is string { Length: > 0 } t ? t : "Segoe UI Variable");
        var monoFont = new AppearanceFontPicker(a.MonoFont, AppearanceRuntime.DefaultMonoFont, "FROM alpine:3.20 AS build\nRUN apk add --no-cache git  # 0O 1lI", true);
        monoFont.Picked += name => Look(name.Length == 0 ? "code font reset to default" : "code font set to " + name, m => m.MonoFont = name);
        rows.Add("monofont", "Code font", "The monospace font for Dockerfiles, identifiers, logs and command output.",
            monoFont, s => s.MonoFontFamily, "Choose a monospace font so columns line up. Proportional fonts work but make tables of identifiers harder to scan.", true, v => v is string { Length: > 0 } t ? t : "Cascadia Mono");
        var sizeText = Ui.Text($"{a.FontSize:0} px", 13, null, "OnSurfaceVariant", false, false);
        var size = StepSlider(12, 22, a.FontSize, 1);
        size.ValueChanged += (_, _) => { sizeText.Text = $"{size.Value:0} px"; if (!studioSyncing) Look("font size set to " + size.Value.ToString("0"), m => m.FontSize = size.Value, true); };
        syncs.Add(() => { var now = AppearanceRuntime.Current.FontSize; if (Math.Abs(size.Value - now) > 0.001) size.Value = now; });
        rows.Add("fontsize", "Font size", "Sets the base text size of the window, from 12 to 22 pixels.",
            Resettable(Ui.HStack(12, size, sizeText), "font size", (m, d) => m.FontSize = d.FontSize), s => s.FontSize, null, false, v => $"{v:0} px");
        var weights = new[] { ("Regular", 400), ("Medium", 500), ("SemiBold", 600), ("Bold", 700), ("Heavy", 800) };
        rows.Add("headingweight", "Heading weight", "How heavy page titles and section headings look.",
            Resettable(Ui.Segmented(weights.Select(w => w.Item1).ToList(), weights.First(w => w.Item2 == a.HeadingWeight).Item1, v => { var w = weights.First(x => x.Item1 == v).Item2; Look("heading weight set to " + v, m => m.HeadingWeight = w); }), "heading weight", (m, d) => m.HeadingWeight = d.HeadingWeight),
            s => s.HeadingWeight, null, true, v => v is int w ? weights.FirstOrDefault(x => x.Item2 == w).Item1 ?? w.ToString() : "");
        rows.Add("railicons", "Navigation icons", "Outline icons with a filled icon for the selected destination, or filled icons everywhere.",
            Resettable(Ui.Segmented(AppearanceState.RailIconStyles, a.RailIcons, v => Look("navigation icons set to " + v, m => m.RailIcons = v)), "navigation icons", (m, d) => m.RailIcons = d.RailIcons), s => s.RailIconStyle);

        // ----- Motion -----
        var motionNote = Ui.Caption("");
        void Note() => motionNote.Text = MotionNoteText();
        Note();
        syncs.Add(Note);
        rows.Add("motionlevel", "Motion", "How much the interface moves: page and tab transitions, dialogs, toasts, the rail indicator, presses, switches and list entrances.",
            Ui.VStack(6, Resettable(Synced(new StudioChoice(AppearanceMotion.Levels, () => AppearanceRuntime.Current.MotionLevel, v => Look("motion set to " + v, m => m.MotionLevel = v)), syncs), "motion", (m, d) => m.MotionLevel = d.MotionLevel), motionNote),
            s => s.MotionLevel,
            "Motion follows Material 3 easing (emphasized, emphasized decelerate and accelerate, standard) and duration tokens (short 50-200 ms, medium 250-400 ms, long 450-600 ms). Motion never carries information on its own: every state is also shown without it. Reduced motion, Low stimulation, the Windows animation setting and documentation captures always win.", true);
        var speedText = Ui.Text($"{a.MotionSpeed:0.00}x", 13, null, "OnSurfaceVariant", false, false);
        var speed = StepSlider(0.5, 2, a.MotionSpeed, 0.25);
        speed.ValueChanged += (_, _) => { speedText.Text = $"{speed.Value:0.00}x"; if (!studioSyncing) Look($"motion speed set to {speed.Value:0.00}x", m => m.MotionSpeed = speed.Value, true); };
        syncs.Add(() => { var now = AppearanceRuntime.Current.MotionSpeed; if (Math.Abs(speed.Value - now) > 0.001) speed.Value = now; });
        rows.Add("motionspeed", "Motion speed", "Speeds animations up (up to 2x, shorter) or slows them down (down to 0.5x, longer).",
            Resettable(Ui.HStack(12, speed, speedText), "motion speed", (m, d) => m.MotionSpeed = d.MotionSpeed), s => s.MotionSpeed, null, false, v => $"{v:0.00}x");
        rows.Add("motion", "Reduced motion", "Turns off all animations; the interface snaps to each final state.",
            ReducedMotionToggle(syncs, Note), s => s.ReducedMotion);
        var demoState = Ui.Caption("");
        var play = Ui.Button("Play motion demo", Icons.PlayArrow, ButtonKind.Tonal, () =>
        {
            preview.BringIntoView();
            preview.PlayDemo();
            demoState.Text = Motion.Enabled ? Loc.T("Playing on the live preview in the studio above.") : Loc.T("Motion is not running, so the preview shows the final state at once. ") + motionNote.Text;
        });
        rows.Add("motionpreview", "Motion demo", "Plays a page entrance, a staggered list, a progress bar and a notification on the studio's live preview with the current motion settings.",
            Ui.VStack(6, play, demoState), null);

        // ----- Effects -----
        var elevationText = Ui.Text($"{a.Elevation * 100:0}%", 13, null, "OnSurfaceVariant", false, false);
        var elevation = StepSlider(0, 2, a.Elevation, 0.1);
        elevation.ValueChanged += (_, _) => { elevationText.Text = $"{elevation.Value * 100:0}%"; Look($"elevation set to {elevation.Value * 100:0}%", m => m.Elevation = elevation.Value, true); };
        rows.Add("elevation", "Elevation", "Shadow strength of dialogs, popups and toasts, from none to double.",
            Resettable(Ui.HStack(12, elevation, elevationText), "elevation", (m, d) => m.Elevation = d.Elevation), s => s.ElevationScale, "Applies to surfaces opened after the change.", false, v => v is double d ? $"{d * 100:0}%" : "");
        var tintText = Ui.Text($"{a.SurfaceTint * 100:0}%", 13, null, "OnSurfaceVariant", false, false);
        var tint = StepSlider(0, 100, a.SurfaceTint * 100, 5);
        tint.ValueChanged += (_, _) => { tintText.Text = $"{tint.Value:0}%"; Look($"surface tint set to {tint.Value:0}%", m => m.SurfaceTint = tint.Value / 100, true); };
        rows.Add("tint", "Surface tint", "Blends a little of the primary colour into every surface, as Material tonal elevation does.",
            Resettable(Ui.HStack(12, tint, tintText), "surface tint", (m, d) => m.SurfaceTint = d.SurfaceTint), s => s.SurfaceTint, null, false, v => v is double d ? $"{d * 100:0}%" : "");
        var rainbowSwitch = SettingRows.Toggle(a.Rainbow, v => Look("rainbow accent " + (v ? "on" : "off"), m => m.Rainbow = v));
        syncs.Add(() => { var on = AppearanceRuntime.Current.Rainbow; if (rainbowSwitch.IsChecked != on) rainbowSwitch.IsChecked = on; });
        rows.Add("rainbow", "Animated rainbow accent", "Cycles the accent hue slowly (one cycle every 24 seconds at 1x speed).",
            rainbowSwitch, s => s.RainbowAccent, "With motion Off or Reduced, Reduced motion or Low stimulation, the accent settles on the seed hue and stays still. Captures never animate.");
    }

    // ----- Presets bar -----

    private FrameworkElement PresetsBar(List<Action> syncs)
    {
        var combo = new ComboBox { MinWidth = 220 };
        AutomationProperties.SetName(combo, Loc.T("Preset"));
        void FillPresets(string? select = null)
        {
            var current = select ?? (combo.SelectedItem as ComboBoxItem)?.Tag as string;
            combo.Items.Clear();
            foreach (var p in AppearanceRuntime.AllPresets)
                combo.Items.Add(new ComboBoxItem { Content = p.Name + (p.BuiltIn ? "  · " + Loc.T("built-in") : ""), Tag = p.Id });
            combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == current) ?? (combo.Items.Count > 0 ? combo.Items[0] : null);
        }
        AppearancePreset? Selected() => AppearanceRuntime.AllPresets.FirstOrDefault(p => p.Id == (combo.SelectedItem as ComboBoxItem)?.Tag as string);
        FillPresets();
        // Presets saved or imported from the studio appear here too.
        syncs.Add(() => { if (!combo.IsDropDownOpen && !AppearanceRuntime.AllPresets.Select(p => p.Id).SequenceEqual(combo.Items.OfType<ComboBoxItem>().Select(i => i.Tag as string))) FillPresets(); });
        var apply = Ui.Button("Apply", Icons.Check, ButtonKind.Filled, () => { if (Selected() is { } p) AppearanceRuntime.ApplyPreset(p); });
        var save = Ui.AsyncButton("Save current as…", Icons.Save, ButtonKind.Tonal, () => SaveCurrentPresetAsync(id => FillPresets(id)));
        var rename = Ui.AsyncButton("Rename…", Icons.DriveFileRenameOutline, ButtonKind.Text, async () =>
        {
            if (Selected() is not { BuiltIn: false } preset) { Host.Notify.Info("Built-in presets cannot be renamed. Save a copy with Save current as… after applying it."); return; }
            if (!await PresetFree(preset.Name)) return;
            var name = await Host.Overlay.PromptAsync("Rename preset", "New name", preset.Name, null, n => AppearancePresets.ValidateName(n) ?? (AppearanceRuntime.AllPresets.Any(p => p != preset && p.Name.Equals(n.Trim(), StringComparison.OrdinalIgnoreCase)) ? "Another preset already has that name." : null));
            if (name == null) return;
            var list = AppearanceRuntime.UserPresets.Select(p => p == preset ? p with { Name = name.Trim() } : p).ToList();
            if (AppearanceRuntime.SaveUserPresets(list, $"renamed {preset.Name} to {name.Trim()}")) FillPresets(list.First(p => p.Name == name.Trim()).Id);
        });
        var delete = Ui.AsyncButton("Delete…", Icons.Delete, ButtonKind.Text, async () =>
        {
            if (Selected() is not { BuiltIn: false } preset) { Host.Notify.Info("Built-in presets cannot be deleted."); return; }
            if (!await PresetFree(preset.Name)) return;
            if (!await Host.Overlay.ConfirmAsync($"Delete preset \"{preset.Name}\"?", "The preset is removed from appearance-presets.json. The file before the deletion is kept in local history, and your current appearance does not change.", "Delete preset", true)) return;
            if (AppearanceRuntime.SaveUserPresets(AppearanceRuntime.UserPresets.Where(p => p != preset).ToList(), "deleted " + preset.Name)) { FillPresets(); Host.Notify.Success($"Preset \"{preset.Name}\" deleted."); }
        });
        delete.SetResourceReference(Control.ForegroundProperty, "Error");
        var lockPreset = Ui.AsyncButton("Lock this preset…", Icons.Lock, ButtonKind.Text, async () =>
        {
            if (Selected() is not { BuiltIn: false } preset) { Host.Notify.Info("Built-in presets cannot be changed, so they need no lock."); return; }
            await LockUi.ToggleAsync(AppearanceLocks.PresetId(preset.Name), "Preset: " + preset.Name);
        });
        var export = Ui.Button("Export…", Icons.FileExport, ButtonKind.Text, () => { if (Selected() is { } preset) ExportPreset(preset); });
        var import = Ui.AsyncButton("Import…", Icons.Upload, ButtonKind.Text, () => ImportPresetAsync(id => FillPresets(id)));
        var copy = Ui.Button("Copy style", Icons.ContentCopy, ButtonKind.Text, () =>
        {
            ActionSounds.Copy(AppearancePresets.Export(new AppearancePreset("Copied style", AppearanceRuntime.Current)));
            Host.Notify.Info("The current appearance was copied as preset JSON. Paste style applies it, here or on another computer.");
        });
        var paste = Ui.Button("Paste style", Icons.ContentCopy, ButtonKind.Text, () =>
        {
            string text;
            try { text = Clipboard.ContainsText() ? Clipboard.GetText() : ""; } catch (System.Runtime.InteropServices.ExternalException) { text = ""; }
            if (text.Length == 0) { Host.Notify.Info("The clipboard has no text. Copy a style first."); return; }
            try { var preset = AppearancePresets.Import(text); AppearanceRuntime.Set(AppearanceRuntime.Current.WithLookOf(preset.State), "pasted style " + preset.Name); }
            catch (InvalidDataException e) { Host.Notify.Error(Loc.T("The clipboard does not hold a valid appearance style, nothing changed:") + " " + e.Message); }
        });
        var undo = Ui.Button("Undo", Icons.Undo, ButtonKind.Tonal, AppearanceRuntime.Undo);
        var redo = Ui.Button("Redo", Icons.Redo, ButtonKind.Tonal, AppearanceRuntime.Redo);
        var resetAll = Ui.AsyncButton("Reset appearance…", Icons.RestartAlt, ButtonKind.Danger, ResetAppearanceAsync);
        var history = Ui.Caption("");
        void UpdateHistory()
        {
            undo.IsEnabled = AppearanceRuntime.CanUndo; redo.IsEnabled = AppearanceRuntime.CanRedo;
            history.Text = $"{AppearanceRuntime.History.UndoCount} {Loc.T("undo step(s)")} · {AppearanceRuntime.History.RedoCount} {Loc.T("redo step(s)")} · {Loc.T("up to 100 kept for this session")}";
        }
        UpdateHistory();
        syncs.Add(UpdateHistory);
        var problem = AppearanceRuntime.PresetProblem is { } p ? Ui.Text("appearance-presets.json could not be read, so saving presets is paused: " + p, 12, null, "Error") : null;
        return Ui.VStack(10,
            Ui.Wrap(8, combo, apply, save, rename, delete, lockPreset, export, import),
            Ui.Wrap(8, undo, redo, copy, paste, resetAll),
            history, problem);
    }

    // ----- Preset actions shared by the presets bar and the studio -----

    /// <summary>A locked preset cannot be replaced, renamed or deleted until it is unlocked.</summary>
    private static async Task<bool> PresetFree(string presetName)
    {
        var host = AppHost.Current;
        if (AppearanceLocks.PresetBlocker(host.Settings, presetName) is not { } block) return true;
        await LockUi.UnlockThenAsync(block.Id, block.Label);
        return AppearanceLocks.PresetBlocker(host.Settings, presetName) == null;
    }

    /// <summary>Asks for a name and saves the current appearance as a user preset (replacing one of that name after confirmation).</summary>
    private async Task SaveCurrentPresetAsync(Action<string>? saved)
    {
        var name = await Host.Overlay.PromptAsync("Save appearance preset", "Preset name", "My look", "Saves the current colours, shape, density, fonts and effects as a preset on this computer.", n => AppearancePresets.ValidateName(n) ?? (AppearancePresets.BuiltIn.Any(b => b.Name.Equals(n.Trim(), StringComparison.OrdinalIgnoreCase)) ? "That is a built-in preset name. Choose another." : null));
        if (name == null) return;
        name = name.Trim();
        var list = AppearanceRuntime.UserPresets.ToList();
        var existing = list.FindIndex(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0 && !await PresetFree(list[existing].Name)) return;
        if (existing >= 0 && !await Host.Overlay.ConfirmAsync("Replace preset?", $"A preset named \"{list[existing].Name}\" exists. Replace it with the current appearance? The old version stays in local history.", "Replace", true)) return;
        var preset = new AppearancePreset(name, AppearanceRuntime.Current);
        if (existing >= 0) list[existing] = preset; else list.Add(preset);
        if (list.Count > AppearancePresets.MaxUserPresets) { Host.Notify.Warning($"You can keep up to {AppearancePresets.MaxUserPresets} presets. Delete one first."); return; }
        if (AppearanceRuntime.SaveUserPresets(list, "saved " + name)) { saved?.Invoke(preset.Id); Host.Notify.Success($"Preset \"{name}\" saved."); }
    }

    /// <summary>Writes one preset to a file the person chooses.</summary>
    private void ExportPreset(AppearancePreset preset)
    {
        var dialog = new SaveFileDialog { Filter = "Appearance preset|*.json", FileName = string.Concat(preset.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)) + ".appearance.json", Title = Loc.T("Export appearance preset") };
        if (dialog.ShowDialog() != true) return;
        try { File.WriteAllText(dialog.FileName, AppearancePresets.Export(preset)); Host.Notify.Success($"Preset \"{preset.Name}\" exported to {Path.GetFileName(dialog.FileName)}."); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Host.Notify.Error(Loc.T("The preset could not be exported:") + " " + e.Message); }
    }

    /// <summary>Reads a preset file, checks it in full, adds it to the user presets and applies it after confirmation.</summary>
    private async Task ImportPresetAsync(Action<string>? imported)
    {
        var dialog = new OpenFileDialog { Filter = "Appearance preset|*.json|All files|*.*", Title = Loc.T("Import appearance preset") };
        if (dialog.ShowDialog() != true) return;
        AppearancePreset preset;
        try
        {
            if (new FileInfo(dialog.FileName).Length > AppearancePresets.MaxPresetBytes) throw new InvalidDataException("The file is larger than 64 KiB, so it is not an appearance preset.");
            preset = AppearancePresets.Import(await File.ReadAllTextAsync(dialog.FileName));
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException) { Host.Notify.Error(Loc.T("The preset was not imported, nothing changed:") + " " + e.Message); return; }
        var list = AppearanceRuntime.UserPresets.ToList();
        var name = preset.Name; var n = 2;
        while (AppearanceRuntime.AllPresets.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) name = $"{preset.Name} ({n++})";
        preset = preset with { Name = name };
        if (!await Host.Overlay.ConfirmAsync($"Import preset \"{name}\"?", "The file was checked in full. The preset is added to your presets and applied now; undo returns to your current appearance.", "Add and apply")) return;
        list.Add(preset);
        if (!AppearanceRuntime.SaveUserPresets(list, "imported " + name)) return;
        imported?.Invoke(preset.Id);
        AppearanceRuntime.ApplyPreset(preset);
    }

    /// <summary>Returns every appearance value to its default after confirmation (one undo step).</summary>
    private async Task ResetAppearanceAsync()
    {
        if (!await Host.Overlay.ConfirmAsync("Reset the appearance?", "Theme, seed colour, role overrides, shape, density, fonts, motion and effects return to their built-in defaults. You can undo this, and the previous values stay in local history. High contrast, larger targets and reduced motion are accessibility settings and are kept.", "Reset appearance", true)) return;
        AppearanceRuntime.ResetAll();
    }

    /// <summary>Registers a segmented choice for synchronisation and returns it.</summary>
    private static StudioChoice Synced(StudioChoice choice, List<Action> syncs) { syncs.Add(choice.Sync); return choice; }

    /// <summary>A row switch that follows its setting when it changes elsewhere (for example in the studio).</summary>
    private CheckBox SyncedToggle(List<Action> syncs, Func<bool> read, Action<bool> write)
    {
        var toggle = SettingRows.Toggle(read(), v => { if (!studioSyncing && read() != v) write(v); });
        syncs.Add(() => { if (toggle.IsChecked != read()) toggle.IsChecked = read(); });
        return toggle;
    }

    private CheckBox ReducedMotionToggle(List<Action> syncs, Action note) =>
        SyncedToggle(syncs, () => S.ReducedMotion, v => { S.ReducedMotion = v; Commit("Reduced motion " + (v ? "on" : "off")); note(); });

    // ----- Role overrides -----

    private FrameworkElement RoleEditor(List<Action> syncs)
    {
        var filter = new SearchFilter("Search colour roles", 260);
        var list = new ListBox { Height = 240 };
        AutomationProperties.SetName(list, Loc.T("Colour roles"));
        var pickerHost = new ContentControl { Focusable = false };
        var status = Ui.Caption("");
        AppearanceColorPicker? picker = null;
        var ownChange = false;

        FrameworkElement RoleRow(AppearanceRole role)
        {
            var swatch = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1) };
            swatch.SetResourceReference(Border.BackgroundProperty, role.Key);
            swatch.SetResourceReference(Border.BorderBrushProperty, "Outline");
            var overrides = AppearanceRuntime.Current.Roles;
            var badge = overrides.TryGetValue(role.Key, out var v) ? Ui.Badge(v == AppearanceRoles.Rainbow ? "rainbow" : v, Tone.Primary) : null;
            UIElement tail = Host.IsLocked(AppearanceLocks.RoleId(role.Key)) ? Ui.Icon(Icons.Lock, 18, false, "Primary") : badge ?? (UIElement)new Border();
            return Ui.Columns(10, [0, 1, 0], swatch, Ui.VStack(0, Ui.Text(role.Name, 13, null, "OnSurface", false), Ui.Mono(role.Key + " · " + role.Group, 11, "OnSurfaceVariant")), tail);
        }
        void Fill()
        {
            var keep = selectedRole;
            list.Items.Clear();
            foreach (var role in AppearanceRoles.All.Where(r => filter.Matches(r.Key + " " + r.Name + " " + r.Group)))
            {
                var item = new ListBoxItem { Content = RoleRow(role), Tag = role.Key, Padding = new Thickness(8, 4, 8, 4), ContextMenu = new ContextMenu() };
                var roleId = AppearanceLocks.RoleId(role.Key);
                LockUi.AddLockItems(item.ContextMenu, roleId, "Colour role: " + role.Name, "Lock this colour role…", "Unlock this colour role…");
                item.ContextMenu.Closed += (_, _) => Fill();
                list.Items.Add(item);
            }
            list.SelectedItem = list.Items.OfType<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == keep);
            var count = AppearanceRuntime.Current.Roles.Count;
            status.Text = (filter.Status.Length > 0 ? filter.Status + " · " : list.Items.Count == 0 ? Loc.T("No roles match.") + " · " : "") + (count == 0 ? Loc.T("No roles are overridden.") : $"{count} {Loc.T("role(s) overridden")}");
        }
        AppearanceRgba Resolved(string key) => Application.Current.TryFindResource(key) is SolidColorBrush b ? AppearanceColorPicker.FromWpf(b.Color) : new AppearanceRgba(0, 0, 0);
        void ShowPicker()
        {
            if (selectedRole is not { } key || AppearanceRoles.Find(key) is not { } role) { pickerHost.Content = Ui.Caption("Choose a role in the list to edit its colour."); return; }
            var overrides = AppearanceRuntime.Current.Roles;
            picker = new AppearanceColorPicker(Resolved(key), true, [AppearanceRoles.ContrastPartner(key), "Surface"], overrides.TryGetValue(key, out var v) && v == AppearanceRoles.Rainbow);
            picker.ColorChanged += c => { ownChange = true; Look($"{role.Key} colour set to {c.ToStorageHex()}", m => m.Roles[key] = c.ToStorageHex(), true); ownChange = false; };
            picker.RainbowChanged += on => { ownChange = true; Look($"{role.Key} rainbow {(on ? "on" : "off")}", m => { if (on) m.Roles[key] = AppearanceRoles.Rainbow; else m.Roles.Remove(key); }); ownChange = false; Fill(); };
            var reset = Ui.Button("Reset this role", Icons.RestartAlt, ButtonKind.Text, () => { ownChange = true; AppearanceRuntime.Change(role.Key + " reset to the theme colour", m => m.Roles.Remove(key), false, rows.Guard); ownChange = false; Fill(); ShowPicker(); });
            reset.IsEnabled = overrides.ContainsKey(key);
            // A locked colour role (or a locked roles row) cannot be edited; clicking its picker asks for the unlock.
            if (AppearanceLocks.RoleBlocker(Host.Settings, key) is { } roleBlock)
            {
                picker.IsEnabled = false; reset.IsEnabled = false;
                LockUi.SetClickTarget(picker, roleBlock.Id, roleBlock.Label);
            }
            pickerHost.Content = Ui.Card(Ui.VStack(10, Ui.Columns(8, [1, 0], Ui.Text(role.Name, 15, FontWeights.SemiBold), reset), picker), 14, "SurfaceContainer", 12);
        }
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is ListBoxItem { Tag: string key } && key != selectedRole) { selectedRole = key; ShowPicker(); } };
        filter.Changed += Fill;
        syncs.Add(() =>
        {
            if (ownChange) return;
            Fill();
            if (picker != null && selectedRole is { } key) picker.SetColor(Resolved(key));
        });
        Fill();
        ShowPicker();
        var resetAll = Ui.AsyncButton("Reset all roles…", Icons.RestartAlt, ButtonKind.Text, async () =>
        {
            if (AppearanceRuntime.Current.Roles.Count == 0) { Host.Notify.Info("No roles are overridden."); return; }
            if (!await Host.Overlay.ConfirmAsync("Reset every colour role?", "All role overrides are removed so the theme and seed colours apply again. You can undo this.", "Reset roles", true)) return;
            AppearanceRuntime.Change("all colour roles reset", m => m.Roles.Clear());
        });
        return Ui.VStack(8, Ui.Columns(12, [1, 0], filter, resetAll), status, list, pickerHost);
    }
}
