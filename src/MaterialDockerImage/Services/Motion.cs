using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Services;

public enum MotionAxis { X, Y, Z }

/// <summary>
/// Cubic-bezier easing (the curve Material 3 specifies its easing tokens with). Control points are dependency
/// properties so the curve survives the freeze-and-clone WPF performs when an animation starts.
/// </summary>
public sealed class CubicBezierEase : EasingFunctionBase
{
    public static readonly DependencyProperty X1Property = DependencyProperty.Register(nameof(X1), typeof(double), typeof(CubicBezierEase), new PropertyMetadata(0.2));
    public static readonly DependencyProperty Y1Property = DependencyProperty.Register(nameof(Y1), typeof(double), typeof(CubicBezierEase), new PropertyMetadata(0.0));
    public static readonly DependencyProperty X2Property = DependencyProperty.Register(nameof(X2), typeof(double), typeof(CubicBezierEase), new PropertyMetadata(0.0));
    public static readonly DependencyProperty Y2Property = DependencyProperty.Register(nameof(Y2), typeof(double), typeof(CubicBezierEase), new PropertyMetadata(1.0));
    public double X1 { get => (double)GetValue(X1Property); set => SetValue(X1Property, value); }
    public double Y1 { get => (double)GetValue(Y1Property); set => SetValue(Y1Property, value); }
    public double X2 { get => (double)GetValue(X2Property); set => SetValue(X2Property, value); }
    public double Y2 { get => (double)GetValue(Y2Property); set => SetValue(Y2Property, value); }

    public CubicBezierEase() => EasingMode = EasingMode.EaseIn; // EaseIn runs the curve exactly as specified.
    public CubicBezierEase((double X1, double Y1, double X2, double Y2) points) : this() { X1 = points.X1; Y1 = points.Y1; X2 = points.X2; Y2 = points.Y2; }

    protected override double EaseInCore(double t)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        double x1 = X1, y1 = Y1, x2 = X2, y2 = Y2;
        double Bx(double s) => 3 * x1 * s * (1 - s) * (1 - s) + 3 * x2 * s * s * (1 - s) + s * s * s;
        double By(double s) => 3 * y1 * s * (1 - s) * (1 - s) + 3 * y2 * s * s * (1 - s) + s * s * s;
        double Dx(double s) => 3 * x1 * (1 - s) * (1 - s) + 6 * (x2 - x1) * s * (1 - s) + 3 * (1 - x2) * s * s;
        var u = t;
        for (var i = 0; i < 8; i++)
        {
            var error = Bx(u) - t;
            if (Math.Abs(error) < 1e-6) return By(u);
            var d = Dx(u);
            if (Math.Abs(d) < 1e-6) break;
            u = Math.Clamp(u - error / d, 0, 1);
        }
        double lo = 0, hi = 1; u = t;
        for (var i = 0; i < 30; i++) { if (Bx(u) < t) lo = u; else hi = u; u = (lo + hi) / 2; }
        return By(u);
    }

    protected override Freezable CreateInstanceCore() => new CubicBezierEase();
}

/// <summary>
/// The spring easing of Expressive motion (<see cref="AppearanceSpring"/>): an ease-out curve that overshoots its end by an
/// amount set with the Spring bounce setting, then settles. The bounce is a dependency property so it survives freezing.
/// </summary>
public sealed class SpringEase : EasingFunctionBase
{
    public static readonly DependencyProperty BounceProperty = DependencyProperty.Register(nameof(Bounce), typeof(int), typeof(SpringEase), new PropertyMetadata(AppearanceSpring.Default));
    public int Bounce { get => (int)GetValue(BounceProperty); set => SetValue(BounceProperty, value); }
    public SpringEase() => EasingMode = EasingMode.EaseIn; // EaseIn runs the curve exactly as defined.
    public SpringEase(int bounce) : this() => Bounce = bounce;
    protected override double EaseInCore(double t) => AppearanceSpring.Ease(t, Bounce);
    protected override Freezable CreateInstanceCore() => new SpringEase();
}

/// <summary>
/// Material 3 motion for the whole app. Every helper snaps to the final state when motion is Off or Reduced, when
/// Reduced motion or Low stimulation is on, when Windows client-area animations are off, and in capture mode.
/// Animations use FillBehavior.Stop and end on the element's normal value, so an interrupted animation can never
/// leave anything hidden, moved or half-drawn: motion never carries state on its own.
/// </summary>
public static class Motion
{
    public static readonly IEasingFunction Emphasized = Ease(AppearanceMotion.Emphasized);
    public static readonly IEasingFunction EmphasizedDecelerate = Ease(AppearanceMotion.EmphasizedDecelerate);
    public static readonly IEasingFunction EmphasizedAccelerate = Ease(AppearanceMotion.EmphasizedAccelerate);
    public static readonly IEasingFunction Standard = Ease(AppearanceMotion.Standard);
    public static readonly IEasingFunction StandardDecelerate = Ease(AppearanceMotion.StandardDecelerate);
    private static IEasingFunction Ease((double, double, double, double) points) { var e = new CubicBezierEase(points); e.Freeze(); return e; }

    private static UserSettings? Settings => AppHost.Current?.Settings;

    /// <summary>The level that applies right now after accessibility, Windows and capture rules.</summary>
    public static string EffectiveLevel
    {
        get
        {
            var host = AppHost.Current;
            if (host == null) return "Off";
            bool windows;
            try { windows = SystemParameters.ClientAreaAnimation; } catch (InvalidOperationException) { windows = true; }
            return AppearanceMotion.Effective(host.Settings.MotionLevel, host.Settings.ReducedMotion, host.Settings.LowStimulation, windows, host.IsCapture);
        }
    }

    public static bool Enabled => AppearanceMotion.Animates(EffectiveLevel);
    public static bool Expressive => EffectiveLevel == "Expressive";
    public static bool WindowsAnimationsOff { get { try { return !SystemParameters.ClientAreaAnimation; } catch (InvalidOperationException) { return false; } } }

    /// <summary>
    /// The easing for movement and scale: the given emphasized curve, or the spring curve when motion is Expressive and the
    /// Spring bounce setting is above 0%. Opacity never uses the spring.
    /// </summary>
    public static IEasingFunction Spatial(IEasingFunction emphasized)
    {
        var bounce = Settings?.SpringBounce ?? 0;
        if (!AppearanceSpring.Applies(EffectiveLevel, bounce)) return emphasized;
        var spring = new SpringEase(bounce); spring.Freeze();
        return spring;
    }

    /// <summary>A duration token scaled by level and speed.</summary>
    public static TimeSpan Duration(int token) => TimeSpan.FromMilliseconds(AppearanceMotion.Duration(token, EffectiveLevel, Settings?.MotionSpeed ?? 1));

    // ----- Building blocks -----

    private static (ScaleTransform Scale, TranslateTransform Move)? Transforms(UIElement element)
    {
        if (element.RenderTransform is TransformGroup { IsFrozen: false } group && group.Children.Count == 2 && group.Children[0] is ScaleTransform { IsFrozen: false } s && group.Children[1] is TranslateTransform { IsFrozen: false } t)
            return (s, t);
        var current = element.RenderTransform;
        if (current != null && current != Transform.Identity && !current.Value.IsIdentity) return null; // never replace a transform someone else set
        var scale = new ScaleTransform(); var move = new TranslateTransform();
        element.RenderTransform = new TransformGroup { Children = { scale, move } };
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        return (scale, move);
    }

    /// <summary>Animates from a start value to the element's normal value, holding the start during the delay.</summary>
    private static AnimationTimeline From(double from, double to, TimeSpan duration, IEasingFunction ease, TimeSpan delay = default)
    {
        var a = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop, Duration = delay + duration };
        a.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        if (delay > TimeSpan.Zero) a.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(delay)));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(delay + duration), ease));
        a.Freeze();
        return a;
    }

    /// <summary>Stops every motion this class may have started on the element and restores normal values.</summary>
    public static void Snap(UIElement element)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        if (element.RenderTransform is TransformGroup { IsFrozen: false } g && g.Children.Count == 2 && g.Children[0] is ScaleTransform s && g.Children[1] is TranslateTransform t && !s.IsFrozen && !t.IsFrozen)
        {
            s.BeginAnimation(ScaleTransform.ScaleXProperty, null); s.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            t.BeginAnimation(TranslateTransform.XProperty, null); t.BeginAnimation(TranslateTransform.YProperty, null);
        }
    }

    private static void Enter(UIElement element, double fromOpacity, double fromScale, double dx, double dy, int token, IEasingFunction ease, TimeSpan delay = default)
    {
        Snap(element);
        if (!Enabled) return;
        var d = Duration(token);
        element.BeginAnimation(UIElement.OpacityProperty, From(fromOpacity, 1, d, Standard, delay));
        if (Transforms(element) is not { } parts) return;
        var (scale, move) = parts;
        ease = Spatial(ease);
        if (Math.Abs(fromScale - 1) > 0.001)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, From(fromScale, 1, d, ease, delay));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, From(fromScale, 1, d, ease, delay));
        }
        if (Math.Abs(dx) > 0.01) move.BeginAnimation(TranslateTransform.XProperty, From(dx, 0, d, ease, delay));
        if (Math.Abs(dy) > 0.01) move.BeginAnimation(TranslateTransform.YProperty, From(dy, 0, d, ease, delay));
    }

    // ----- Transition patterns -----

    /// <summary>
    /// The page and tab transition the person chose in Appearance (shared axis, fade through, container transform or slide).
    /// Off snaps; Reduced, Reduced motion and Low stimulation get a short fade with no movement; captures never animate.
    /// </summary>
    public static void PageTransition(UIElement element, bool forward) => PlayPlan(element, AppearanceTransitions.For(Settings?.TransitionPattern, EffectiveLevel, forward, Settings?.MotionSpeed ?? 1));

    /// <summary>Runs one transition plan (used by page changes and by the studio's pattern demonstration).</summary>
    public static void PlayPlan(UIElement element, AppearanceTransitions.Plan plan)
    {
        Snap(element);
        if (!plan.Animates || plan.DurationMs <= 0) return;
        var d = TimeSpan.FromMilliseconds(plan.DurationMs);
        element.BeginAnimation(UIElement.OpacityProperty, From(plan.FromOpacity, 1, d, Standard));
        if (!plan.Moves || Transforms(element) is not { } parts) return;
        var ease = plan.Spatial ? Spatial(Emphasized) : Standard;
        var (scale, move) = parts;
        if (Math.Abs(plan.FromScale - 1) > 0.001)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, From(plan.FromScale, 1, d, ease));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, From(plan.FromScale, 1, d, ease));
        }
        if (Math.Abs(plan.Dx) > 0.01) move.BeginAnimation(TranslateTransform.XProperty, From(plan.Dx, 0, d, ease));
        if (Math.Abs(plan.Dy) > 0.01) move.BeginAnimation(TranslateTransform.YProperty, From(plan.Dy, 0, d, ease));
    }

    /// <summary>Fade through: the incoming content fades in while growing slightly (unrelated destinations).</summary>
    public static void FadeThrough(UIElement element) => Enter(element, 0, Expressive ? 0.88 : 0.94, 0, 0, AppearanceMotion.Medium2, EmphasizedDecelerate);

    /// <summary>Shared axis: content slides along an axis in the direction of travel (related or ordered destinations).</summary>
    public static void SharedAxis(UIElement element, MotionAxis axis, bool forward)
    {
        var distance = (Expressive ? 48 : 30) * (forward ? 1 : -1);
        if (axis == MotionAxis.Z) Enter(element, 0, forward ? 0.8 : 1.1, 0, 0, AppearanceMotion.Medium2, Emphasized);
        else Enter(element, 0, 1, axis == MotionAxis.X ? distance : 0, axis == MotionAxis.Y ? distance : 0, AppearanceMotion.Medium2, Emphasized);
    }

    /// <summary>Container transform for dialogs and panels: grows from slightly smaller while fading in.</summary>
    public static void ContainerTransform(UIElement element) => Enter(element, 0, Expressive ? 0.82 : 0.9, 0, Expressive ? 16 : 8, AppearanceMotion.Medium3, EmphasizedDecelerate);

    /// <summary>Plain fade in (scrims and overlays).</summary>
    public static void FadeIn(UIElement element, int token = AppearanceMotion.Short4) => Enter(element, 0, 1, 0, 0, token, Standard);

    /// <summary>Toasts and side sheets enter from the right edge.</summary>
    public static void SlideInFromRight(UIElement element) => Enter(element, 0, 1, Expressive ? 120 : 64, 0, AppearanceMotion.Medium4, EmphasizedDecelerate);

    /// <summary>Expander and section content reveals downward.</summary>
    public static void Reveal(UIElement element) => Enter(element, 0, 1, 0, -10, AppearanceMotion.Medium1, EmphasizedDecelerate);

    /// <summary>Entrance for a set of children, one after another (lists, cards).</summary>
    public static void Stagger(IEnumerable<UIElement> children, int maximum = 14)
    {
        var step = TimeSpan.FromMilliseconds(Math.Max(12, AppearanceMotion.Duration(28, EffectiveLevel, Settings?.MotionSpeed ?? 1)));
        var i = 0;
        foreach (var child in children)
        {
            if (i >= maximum) { Snap(child); continue; }
            Enter(child, 0, 1, 0, Expressive ? 16 : 10, AppearanceMotion.Medium2, EmphasizedDecelerate, step * i);
            i++;
        }
    }

    /// <summary>Staggers the realised item containers of a list after its next layout pass.</summary>
    public static void StaggerItems(ItemsControl list)
    {
        if (!Enabled) return;
        list.Dispatcher.BeginInvoke(() =>
        {
            var containers = new List<UIElement>();
            for (var i = 0; i < Math.Min(list.Items.Count, 14); i++) if (list.ItemContainerGenerator.ContainerFromIndex(i) is UIElement c) containers.Add(c);
            Stagger(containers);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>State-layer pulse for presses: the layer flashes in quickly and fades out (base opacity stays 0).</summary>
    public static void Pulse(UIElement layer)
    {
        layer.BeginAnimation(UIElement.OpacityProperty, null);
        if (!Enabled) return;
        var a = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop, Duration = Duration(AppearanceMotion.Short2) + Duration(AppearanceMotion.Medium2) };
        a.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(Duration(AppearanceMotion.Short2)), Standard));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(Duration(AppearanceMotion.Short2) + Duration(AppearanceMotion.Medium2)), Standard));
        a.Freeze();
        layer.BeginAnimation(UIElement.OpacityProperty, a);
    }

    /// <summary>Selection indicator grows from its centre (navigation rail pill, active tab).</summary>
    public static void GrowFromCentre(UIElement element)
    {
        Snap(element);
        if (!Enabled || Transforms(element) is not { } parts) return;
        var scale = parts.Scale;
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, From(Expressive ? 0.2 : 0.4, 1, Duration(AppearanceMotion.Medium2), EmphasizedDecelerate));
    }

    /// <summary>Slides an element that just jumped to a new layout position from where it was (switch thumb).</summary>
    public static void SlideFrom(UIElement element, double dx)
    {
        Snap(element);
        if (!Enabled || Transforms(element) is not { } parts) return;
        parts.Move.BeginAnimation(TranslateTransform.XProperty, From(dx, 0, Duration(AppearanceMotion.Short4), Emphasized));
    }

    /// <summary>Counts a numeric text up to its value. The final text is set first, so readers and captures see it.</summary>
    public static void CountUp(TextBlock text, double target, Func<double, string> format)
    {
        text.Text = format(target);
        if (!Enabled || !double.IsFinite(target)) return;
        var duration = Duration(AppearanceMotion.Long2);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        EventHandler? tick = null;
        tick = (_, _) =>
        {
            var t = Math.Min(1, clock.Elapsed.TotalMilliseconds / Math.Max(1, duration.TotalMilliseconds));
            text.Text = format(target * EmphasizedDecelerate.Ease(t));
            if (t >= 1 || !text.IsLoaded) { CompositionTarget.Rendering -= tick; text.Text = format(target); }
        };
        CompositionTarget.Rendering += tick;
    }

    /// <summary>Counts up the first number found in a TextBlock (for stat tiles), keeping the surrounding text.</summary>
    public static void CountUp(TextBlock text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text.Text, @"\d+(?:[.,]\d+)?");
        if (!match.Success || !double.TryParse(match.Value.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)) return;
        var decimals = match.Value.Contains('.') || match.Value.Contains(',') ? match.Value.Length - match.Value.IndexOfAny(['.', ',']) - 1 : 0;
        var (before, after) = (text.Text[..match.Index], text.Text[(match.Index + match.Length)..]);
        CountUp(text, value, v => before + v.ToString("F" + decimals, System.Globalization.CultureInfo.CurrentCulture) + after);
    }

    /// <summary>Moves a progress bar smoothly to a value; the value is set immediately when motion is off.</summary>
    public static void SmoothProgress(RangeBase bar, double value)
    {
        bar.BeginAnimation(RangeBase.ValueProperty, null);
        var from = bar.Value;
        bar.Value = value;
        if (!Enabled || Math.Abs(from - value) < 1e-9) return;
        bar.BeginAnimation(RangeBase.ValueProperty, From(from, value, Duration(AppearanceMotion.Medium4), Standard));
    }

    /// <summary>Animates an element's height between two values, then runs <paramref name="done"/>. Snaps when motion is off.</summary>
    public static void AnimateHeight(FrameworkElement element, double from, double to, Action? done = null)
    {
        element.BeginAnimation(FrameworkElement.HeightProperty, null);
        if (!Enabled || !double.IsFinite(from) || !double.IsFinite(to)) { done?.Invoke(); return; }
        var ease = to > from ? EmphasizedDecelerate : EmphasizedAccelerate;
        var a = new DoubleAnimation(from, to, Duration(to > from ? AppearanceMotion.Medium2 : AppearanceMotion.Short4)) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };
        a.Freeze();
        var clock = a.CreateClock();
        // Hold the end value until the caller has applied the final state, then release the animation.
        clock.Completed += (_, _) => { done?.Invoke(); element.BeginAnimation(FrameworkElement.HeightProperty, null); };
        element.ApplyAnimationClock(FrameworkElement.HeightProperty, clock);
    }
}
