using System.Windows;
using System.Windows.Input;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Shell;

/// <summary>Palette commands for appearance: open the editor, edit the focused element, undo and redo, presets and motion.</summary>
public static class AppearanceCommands
{
    /// <summary>
    /// The element the command applies to: keyboard focus when the window has it, otherwise the window's logical focus.
    /// A window that is not active (for example one driven by assistive technology through UI Automation) still has a
    /// logically focused element even though keyboard focus is elsewhere.
    /// </summary>
    internal static DependencyObject? FocusedTarget()
    {
        if (Keyboard.FocusedElement is DependencyObject keyboard && Application.Current?.MainWindow is { } main && Window.GetWindow(keyboard) == main) return keyboard;
        return Application.Current?.MainWindow is { } w ? FocusManager.GetFocusedElement(w) as DependencyObject : Keyboard.FocusedElement as DependencyObject;
    }

    public static void Register(CommandRegistry c, Action openEditor, Action editFocused)
    {
        ElementAppearance.Initialize();
        c.Register(new AppCommand { Id = "appearance.customize", Title = "Customize the focused element…", Group = "Appearance", Icon = Icons.Palette, Keywords = "element colour color radius corner font size weight padding hide visibility override",
            Run = () => { if (ElementAppearance.Climb(FocusedTarget()) is { } fe) ElementEditor.Show(fe); else AppHost.Current?.Notify.Info("Focus an element first, or use Pick an element to customize."); } });
        c.Register(new AppCommand { Id = "appearance.pick", Title = "Pick an element to customize…", Group = "Appearance", Icon = Icons.Edit, Keywords = "element click choose select pointer customise", Run = AppearancePicker.Start });
        c.Register(new AppCommand { Id = "appearance.overrides", Title = "Show customized elements", Group = "Appearance", Icon = Icons.Tune, Keywords = "element overrides list reset",
            Run = () => { if (Application.Current?.MainWindow is { } w) ElementEditor.Show(w, true); } });
        c.Register(new AppCommand { Id = "appearance.overrides.reset", Title = "Reset all customized elements", Group = "Appearance", Icon = Icons.RestartAlt, Keywords = "element overrides reset clear", CanRun = () => ElementAppearance.Store.Count > 0,
            Run = () => { if (ElementAppearance.ResetAll(out var error)) AppHost.Current?.Notify.Success("All customized elements were reset."); else AppHost.Current?.Notify.Error(error); } });
        c.Register(new AppCommand { Id = "appearance.editor", Title = "Open appearance editor", Group = "Appearance", Icon = Icons.Palette, Keywords = "theme colour color font motion animation density shape preset", Run = openEditor });
        c.Register(new AppCommand { Id = "appearance.element", Title = "Edit appearance of the focused element…", Group = "Appearance", Icon = Icons.Edit, Keywords = "colour role style picker", Gesture = new KeyGesture(Key.E, ModifierKeys.Control | ModifierKeys.Shift), Run = editFocused });
        c.Register(new AppCommand { Id = "appearance.undo", Title = "Undo appearance change", Group = "Appearance", Icon = Icons.Undo, CanRun = () => AppearanceRuntime.CanUndo, Run = AppearanceRuntime.Undo });
        c.Register(new AppCommand { Id = "appearance.redo", Title = "Redo appearance change", Group = "Appearance", Icon = Icons.Redo, CanRun = () => AppearanceRuntime.CanRedo, Run = AppearanceRuntime.Redo });
        c.Register(new AppCommand { Id = "appearance.motionlevel", Title = "Motion level", Group = "Appearance", Icon = Icons.AutoAwesomeMotion, Keywords = "animation transitions off reduced standard expressive",
            LiveControl = () => Ui.Segmented(AppearanceMotion.Levels, AppearanceRuntime.Current.MotionLevel, v => AppearanceRuntime.Change("motion set to " + v, m => m.MotionLevel = v)) });
        c.Register(new AppCommand { Id = "appearance.density", Title = "Density", Group = "Appearance", Icon = Icons.Tune, Keywords = "compact comfortable spacious spacing",
            LiveControl = () => Ui.Segmented(AppearanceState.Densities, AppearanceRuntime.Current.Density, v => AppearanceRuntime.Change("density set to " + v, m => m.Density = v)) });
        c.Register(new AppCommand { Id = "appearance.rainbow", Title = "Animated rainbow accent", Group = "Appearance", Icon = Icons.Palette, Keywords = "colour hue cycle",
            LiveControl = () => Ui.Switch("Animated rainbow accent", AppearanceRuntime.Current.Rainbow, v => AppearanceRuntime.Change("rainbow accent " + (v ? "on" : "off"), m => m.Rainbow = v)) });
        foreach (var preset in AppearancePresets.BuiltIn)
        {
            var p = preset;
            c.Register(new AppCommand { Id = "appearance.preset." + p.Id, Title = "Apply appearance preset: " + p.Name, Group = "Appearance", Icon = Icons.Palette, Keywords = "theme preset look", Run = () => AppearanceRuntime.ApplyPreset(p) });
        }
    }
}
