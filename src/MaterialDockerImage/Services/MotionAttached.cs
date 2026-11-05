using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace MaterialDockerImage.Services;

/// <summary>
/// Attaches motion to control templates without storyboards, so every effect checks the motion level at the moment it
/// runs and snaps when motion is off. Set on a template part, for example
/// <c>m:MotionAttached.Effect="Ripple"</c>. Effects: Ripple (state layer pulse on press), RailPill (selection grows from
/// the centre), SwitchThumb (thumb slides between ends), Reveal (expander content), TabContent (the chosen page transition between tabs).
/// </summary>
public static class MotionAttached
{
    public static readonly DependencyProperty EffectProperty = DependencyProperty.RegisterAttached("Effect", typeof(string), typeof(MotionAttached), new PropertyMetadata(null, OnEffectChanged));
    private static readonly DependencyProperty HookedProperty = DependencyProperty.RegisterAttached("Hooked", typeof(bool), typeof(MotionAttached), new PropertyMetadata(false));
    public static string? GetEffect(DependencyObject d) => (string?)d.GetValue(EffectProperty);
    public static void SetEffect(DependencyObject d, string? value) => d.SetValue(EffectProperty, value);

    private static void OnEffectChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not string) return;
        if (element.IsLoaded) Hook(element); else element.Loaded += OnLoaded;
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        element.Loaded -= OnLoaded;
        Hook(element);
    }

    private static void Hook(FrameworkElement element)
    {
        if ((bool)element.GetValue(HookedProperty)) return;
        element.SetValue(HookedProperty, true);
        var parent = element.TemplatedParent;
        switch (GetEffect(element))
        {
            case "Ripple" when parent is ButtonBase button:
                button.PreviewMouseLeftButtonDown += (_, _) => Motion.Pulse(element);
                button.PreviewKeyDown += (_, args) => { if (args.Key is Key.Space or Key.Enter && !args.IsRepeat) Motion.Pulse(element); };
                break;
            case "RailPill" when parent is ToggleButton toggle:
                toggle.Checked += (_, _) => { if (toggle.IsLoaded) Motion.GrowFromCentre(element); };
                break;
            case "SwitchThumb" when parent is ToggleButton toggle:
                // The template moves the thumb between ends with a trigger; slide it from where it was.
                toggle.Checked += (_, _) => { if (toggle.IsLoaded) Motion.SlideFrom(element, -20); };
                toggle.Unchecked += (_, _) => { if (toggle.IsLoaded) Motion.SlideFrom(element, 20); };
                break;
            case "Reveal" when parent is Expander expander:
                expander.Expanded += (_, args) => { if (ReferenceEquals(args.OriginalSource, expander)) Motion.Reveal(element); };
                break;
            case "TabContent" when parent is TabControl tabs:
                var last = tabs.SelectedIndex;
                tabs.SelectionChanged += (_, args) =>
                {
                    if (!ReferenceEquals(args.OriginalSource, tabs)) return; // selection changes bubble up from lists inside tabs
                    var now = tabs.SelectedIndex;
                    if (now >= 0 && last >= 0 && now != last) Motion.PageTransition(element, now > last);
                    last = now;
                };
                break;
        }
    }
}
