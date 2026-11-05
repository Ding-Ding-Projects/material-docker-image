using System;
using System.Collections.Generic;
using System.Linq;

namespace MaterialDockerImage.UI;

/// <summary>
/// The page and tab transition pattern the person chose, and what each pattern does at each effective motion level.
/// Pure data and logic, so the rules can be checked without a window. Off never moves; Reduced (including the
/// Reduced motion and Low stimulation settings) never moves either and uses at most a short fade.
/// </summary>
public static class AppearanceTransitions
{
    public const string SharedAxis = "SharedAxis";
    public const string FadeThrough = "FadeThrough";
    public const string ContainerTransform = "ContainerTransform";
    public const string Slide = "Slide";
    public const string Default = SharedAxis;
    public static readonly string[] Patterns = [SharedAxis, FadeThrough, ContainerTransform, Slide];

    /// <summary>The readable name of a pattern (English; the interface translates it).</summary>
    public static string Name(string pattern) => pattern switch
    {
        FadeThrough => "Fade through",
        ContainerTransform => "Container transform",
        Slide => "Slide",
        _ => "Shared axis"
    };

    public static string Describe(string pattern) => pattern switch
    {
        FadeThrough => "The new page fades in while growing slightly.",
        ContainerTransform => "The new page grows out of a slightly smaller container.",
        Slide => "The new page slides in from the side it comes from.",
        _ => "The new page moves along the axis of travel: right for later destinations, left for earlier ones."
    };

    public static bool IsKnown(string? pattern) => pattern != null && Patterns.Contains(pattern);

    /// <summary>
    /// One page transition: where the incoming page starts (opacity, scale and offset) and how long it takes to reach its
    /// normal state. <see cref="Spatial"/> says whether the movement uses the emphasized (and possibly spring) easing.
    /// </summary>
    public readonly record struct Plan(bool Animates, double FromOpacity, double FromScale, double Dx, double Dy, double DurationMs, bool Spatial)
    {
        public bool Moves => Math.Abs(Dx) > 0.01 || Math.Abs(Dy) > 0.01 || Math.Abs(FromScale - 1) > 0.001;
        public static Plan None => new(false, 1, 1, 0, 0, 0, false);
    }

    /// <summary>The transition for a page change at an effective motion level and speed (0.5x to 2x).</summary>
    public static Plan For(string? pattern, string effective, bool forward, double speed)
    {
        if (!IsKnown(pattern)) pattern = Default;
        speed = double.IsFinite(speed) ? Math.Clamp(speed, 0.5, 2) : 1;
        if (effective == "Reduced") return new(true, 0, 1, 0, 0, AppearanceMotion.Short4 / speed, false); // a short fade, no movement
        if (!AppearanceMotion.Animates(effective)) return Plan.None;
        var expressive = effective == "Expressive";
        var sign = forward ? 1 : -1;
        return pattern switch
        {
            FadeThrough => new(true, 0, expressive ? 0.88 : 0.94, 0, 0, AppearanceMotion.Duration(AppearanceMotion.Medium2, effective, speed), true),
            ContainerTransform => new(true, 0, expressive ? 0.82 : 0.9, 0, expressive ? 16 : 8, AppearanceMotion.Duration(AppearanceMotion.Medium3, effective, speed), true),
            Slide => new(true, 0, 1, sign * (expressive ? 160 : 96), 0, AppearanceMotion.Duration(AppearanceMotion.Medium4, effective, speed), true),
            _ => new(true, 0, 1, sign * (expressive ? 48 : 30), 0, AppearanceMotion.Duration(AppearanceMotion.Medium2, effective, speed), true)
        };
    }
}

/// <summary>
/// The spring bounce of Expressive motion: an ease-out-back curve whose overshoot grows with the bounce percentage.
/// It only applies at the Expressive level; every other level keeps the plain Material easing.
/// </summary>
public static class AppearanceSpring
{
    public const int Default = 30;
    public const int Minimum = 0, Maximum = 100;
    private const double MaxAmplitude = 2.6; // about 20% overshoot at 100%

    public static int Clamp(int bounce) => Math.Clamp(bounce, Minimum, Maximum);

    public static double Amplitude(int bounce) => Clamp(bounce) / 100.0 * MaxAmplitude;

    /// <summary>True when the bounce changes the easing: only at the Expressive level and above 0%.</summary>
    public static bool Applies(string effective, int bounce) => effective == "Expressive" && Clamp(bounce) > 0;

    /// <summary>Eased progress for linear time <paramref name="t"/> (0 to 1). At 0% this is a plain cubic ease-out.</summary>
    public static double Ease(double t, int bounce)
    {
        if (!double.IsFinite(t) || t <= 0) return 0;
        if (t >= 1) return 1;
        var s = Amplitude(bounce);
        var u = t - 1;
        return 1 + (s + 1) * u * u * u + s * u * u;
    }

    /// <summary>The largest value the curve reaches above its end, as a fraction (0.2 is 20% past the target).</summary>
    public static double Overshoot(int bounce)
    {
        var s = Amplitude(bounce);
        if (s <= 0) return 0;
        var u = -2 * s / (3 * (s + 1)); // where the derivative is zero
        return Math.Max(0, 1 + (s + 1) * u * u * u + s * u * u - 1);
    }

    /// <summary>Evenly spaced samples of the curve, for drawing it.</summary>
    public static IReadOnlyList<(double T, double Value)> Sample(int bounce, int count)
    {
        count = Math.Clamp(count, 2, 400);
        return Enumerable.Range(0, count).Select(i => { var t = i / (double)(count - 1); return (t, Ease(t, bounce)); }).ToList();
    }
}
