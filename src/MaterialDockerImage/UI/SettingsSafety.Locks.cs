using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>
/// Element locks for individual settings rows. A lock is a self-imposed speed bump: it makes the row read-only until the
/// person confirms an unlock. It is not a security boundary. The rule class is framework-free so it can be checked without a window.
/// </summary>
public static class ElementLocks
{
    public const int MaxLocks = 200;
    private static readonly Regex IdPattern = new("^[a-z0-9._-]{1,40}$", RegexOptions.Compiled);

    /// <summary>The settings properties each lockable row owns. Import and reset keep these values while the row is locked.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Properties = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["language"] = ["Language"], ["funnyen"] = ["FunnyEnglish"], ["funnyyue"] = ["FunnyCantonese"], ["emoji"] = ["DialogEmoji"],
        ["vocabulary"] = ["PersonalVocabularyPath"], ["accessmenu"] = ["AccessibilityMenuVisible"], ["focusmode"] = ["FocusMode"],
        ["lowstim"] = ["LowStimulation"], ["time"] = ["TimeAwareness"], ["onething"] = ["OneThingAtATime"], ["momentum"] = ["Momentum"],
        ["sounds"] = ["Sounds"], ["narration"] = ["Narration"], ["voice"] = ["NarrationVoice"], ["rate"] = ["NarrationRate"],
        ["monitorinterval"] = ["MonitorIntervalSeconds"], ["editor"] = ["ExternalEditor"], ["suggestions"] = ["SuggestionsWhileTyping"],
        ["school"] = ["SchoolMode"], ["displayname"] = ["DisplayName"], ["theme"] = ["Theme"], ["seed"] = ["SeedColor"],
        ["roles"] = ["RoleOverrides"], ["contrast"] = ["HighContrast"], ["radius"] = ["CornerScale"], ["density"] = ["Density"],
        ["targets"] = ["LargerTargets"], ["uifont"] = ["UiFontFamily"], ["monofont"] = ["MonoFontFamily"], ["fontsize"] = ["FontSize"],
        ["headingweight"] = ["HeadingWeight"], ["railicons"] = ["RailIconStyle"], ["motionlevel"] = ["MotionLevel"],
        ["motionspeed"] = ["MotionSpeed"], ["motion"] = ["ReducedMotion"], ["elevation"] = ["ElevationScale"], ["tint"] = ["SurfaceTint"],
        ["rainbow"] = ["RainbowAccent"], ["logo"] = ["LogoKind", "LogoSymbol", "LogoImageFile"],
        ["schedules"] = ["ScheduleRules", "ScheduleSourceFile"],
        ["schedulehttps"] = ["ScheduleHttpsUrl", "ScheduleHttpsPollMinutes"], ["schedulehomeassistant"] = ["ScheduleHaBaseUrl", "ScheduleHaEntity", "ScheduleHaPollMinutes"],
        ["soundvolume"] = ["SoundVolume"], ["quiethours"] = ["QuietHoursEnabled", "QuietHoursStart", "QuietHoursEnd"]
    };

    public static bool IsValidId(string? id) => id != null && IdPattern.IsMatch(id);
    public static bool IsLocked(UserSettings settings, string id) => settings.LockedSettings.Contains(id, StringComparer.Ordinal);

    /// <summary>Locks one row. Returns false when the id is invalid, already locked or the lock list is full.</summary>
    public static bool Lock(UserSettings settings, string id)
    {
        if (!IsValidId(id) || IsLocked(settings, id) || settings.LockedSettings.Count >= MaxLocks) return false;
        settings.LockedSettings.Add(id);
        return true;
    }

    /// <summary>Unlocks one row. The caller has already obtained the confirmation; a refused confirmation never reaches this method.</summary>
    public static bool Unlock(UserSettings settings, string id) => settings.LockedSettings.RemoveAll(x => string.Equals(x, id, StringComparison.Ordinal)) > 0;

    public static List<string> Normalize(IEnumerable<string>? ids) =>
        (ids ?? Array.Empty<string>()).Where(IsValidId).Distinct(StringComparer.Ordinal).Take(MaxLocks).ToList();

    /// <summary>Copies the locked rows' values from the current settings onto the target (an import or a reset), so locked rows do not change.</summary>
    public static IReadOnlyList<string> Preserve(UserSettings current, UserSettings target)
    {
        var kept = new List<string>();
        foreach (var id in current.LockedSettings)
        {
            if (!Properties.TryGetValue(id, out var names)) continue;
            foreach (var name in names)
            {
                var property = typeof(UserSettings).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (property is not { CanRead: true, CanWrite: true }) continue;
                property.SetValue(target, property.GetValue(current));
                kept.Add(name);
            }
        }
        target.LockedSettings = current.LockedSettings.ToList();
        return kept;
    }

    public static string HistorySummary(string label, bool locked) => (locked ? "Locked setting: " : "Unlocked setting: ") + label;
    public const string Disclosure = "A lock is a speed bump you set for yourself, not security. It does not protect anything from another person who has this computer.";
}
