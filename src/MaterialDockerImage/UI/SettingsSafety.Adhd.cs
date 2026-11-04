using System;

namespace MaterialDockerImage.UI;

/// <summary>What the window shows for the current attention modes. The shell reads this one decision so every mode has a single consumer.</summary>
public sealed record AdhdChrome(bool ShowProvenance, bool ShowClock, bool ShowAccessibilityButton, bool ShowMomentumChip, bool ShowExitFocus, double RailWidth, bool RailLabels, bool SoundsAllowed, bool MotionAllowed);

public static class AdhdRules
{
    /// <summary>
    /// Focus hides nonessential chrome (version line, accessibility button) and shows one obvious way out. Nothing is removed that cannot be
    /// brought back with that one action. Low stimulation silences cues and animation and composes with, never overrides, reduced motion.
    /// </summary>
    public static AdhdChrome Resolve(UserSettings s, bool windowWideEnoughForProvenance = true) => new(
        ShowProvenance: !s.FocusMode && windowWideEnoughForProvenance,
        ShowClock: s.TimeAwareness,
        ShowAccessibilityButton: s.AccessibilityMenuVisible && !s.FocusMode,
        ShowMomentumChip: s.Momentum && !s.LowStimulation,
        ShowExitFocus: s.FocusMode,
        RailWidth: s.FocusMode ? 72 : 96,
        RailLabels: !s.FocusMode,
        SoundsAllowed: s.Sounds && !s.LowStimulation,
        MotionAllowed: !s.ReducedMotion && !s.LowStimulation);
}

/// <summary>
/// Momentum: counts consecutive completed operations in this session and offers a short encouragement. A failure or a cancel resets the streak,
/// and the streak is session-only, so it never pressures a person across days.
/// </summary>
public sealed class MomentumTracker
{
    public int Streak { get; private set; }
    public int Best { get; private set; }
    public event Action? Changed;

    public void Record(bool succeeded)
    {
        Streak = succeeded ? Streak + 1 : 0;
        if (Streak > Best) Best = Streak;
        Changed?.Invoke();
    }

    public void Reset() { Streak = 0; Best = 0; Changed?.Invoke(); }

    public string ChipText => Streak == 0 ? "" : Streak == 1 ? "1 step done" : $"{Streak} steps in a row";
    public string Encouragement => Streak switch { 0 => "", < 3 => "Good start.", < 6 => "You are on a roll.", _ => "Steady and strong." };
}
