using System;
using System.Collections.Generic;

namespace MaterialDockerImage.UI;

public enum SoundCue { Success, Failure, Warning, Removal, Copy, Drop, DropRefused }

public sealed record SoundMapping(SoundCue Cue, string Trigger, string System);

/// <summary>The action-to-cue mapping and the one rule that decides whether a cue may play.</summary>
public static class SoundCueRules
{
    /// <summary>Documented in docs/features/settings-safety.md. Cues are the Windows system sounds; no audio file ships with the app.</summary>
    public static readonly IReadOnlyList<SoundMapping> Mapping =
    [
        new(SoundCue.Success, "An operation or build finished successfully", "Asterisk"),
        new(SoundCue.Failure, "An operation or build failed", "Hand"),
        new(SoundCue.Warning, "A warning notification, or a canceled operation", "Exclamation"),
        new(SoundCue.Removal, "A remove, prune or delete operation finished successfully", "Question"),
        new(SoundCue.Copy, "Text was copied to the clipboard", "Beep"),
        new(SoundCue.Drop, "A dropped wording file was applied", "tada.wav or Asterisk"),
        new(SoundCue.DropRefused, "A dropped file was refused", "Beep"),
    ];

    /// <summary>
    /// A cue plays only when the Settings switch is on, low stimulation is off, the window is not producing capture images,
    /// the volume is above zero and the clock is outside the quiet hours. <paramref name="now"/> defaults to the local time.
    /// </summary>
    public static bool MayPlay(UserSettings settings, bool capture, DateTime? now = null) =>
        !capture && AdhdRules.Resolve(settings).SoundsAllowed && settings.SoundVolume > 0
        && !QuietHours.IsQuiet(settings.QuietHoursEnabled, settings.QuietHoursStart, settings.QuietHoursEnd, now ?? DateTime.Now);

    /// <summary>Chooses the cue for a finished Docker operation from its argument words. Only a successful run can earn Removal.</summary>
    public static SoundCue ForOperation(IReadOnlyList<string> args, bool succeeded, bool cancelled)
    {
        if (cancelled) return SoundCue.Warning;
        if (!succeeded) return SoundCue.Failure;
        foreach (var word in args)
        {
            if (word.StartsWith('-')) continue;
            if (word is "rm" or "rmi" or "remove" or "prune" or "kill" or "delete" or "disconnect") return SoundCue.Removal;
        }
        return SoundCue.Success;
    }
}

/// <summary>Quiet hours: a daily window, in the computer's local time, in which no sound cue plays.</summary>
public static class QuietHours
{
    /// <summary>
    /// The window is [start, end). When start is later than end it crosses midnight (22:00 to 07:00 is quiet from 22:00 until just before 07:00 the next morning).
    /// Equal times describe no window, so they never silence anything. Unreadable times and a disabled switch are never quiet.
    /// </summary>
    public static bool IsQuiet(bool enabled, string? start, string? end, DateTime now)
    {
        if (!enabled || !ScheduleEvaluator.TryParseTime(start, out var from) || !ScheduleEvaluator.TryParseTime(end, out var to) || from == to) return false;
        var minute = now.Hour * 60 + now.Minute;
        return from < to ? minute >= from && minute < to : minute >= from || minute < to;
    }

    /// <summary>The volume percentage as a gain from 0 to 1. Anything outside 0 to 100 is clamped, so a bad value is never louder than the system level.</summary>
    public static double Gain(int volumePercent) => Math.Clamp(volumePercent, 0, 100) / 100.0;
}

/// <summary>Per-surface collapsed state for filter chip bars and statistic strips. Only collapsed surfaces are stored.</summary>
public static class CollapseFilterState
{
    public const int MaxEntries = 64;
    public static bool IsCollapsed(IReadOnlyDictionary<string, bool> state, string key) => state.TryGetValue(key, out var value) && value;

    public static bool Set(IDictionary<string, bool> state, string key, bool collapsed)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 60) return false;
        if (!collapsed) return state.Remove(key);
        if (state.TryGetValue(key, out var existing) && existing) return false;
        if (state.Count >= MaxEntries) return false;
        state[key] = true;
        return true;
    }

    public static Dictionary<string, bool> Normalize(IDictionary<string, bool>? state)
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (state == null) return result;
        foreach (var pair in state) if (pair.Value && !string.IsNullOrWhiteSpace(pair.Key) && pair.Key.Length <= 60 && result.Count < MaxEntries) result[pair.Key] = true;
        return result;
    }

    public static string Summary(int total, int active) => active > 0 ? $"Filters · {active} on" : total == 1 ? "Filters · 1 filter" : $"Filters · {total} filters";
}
