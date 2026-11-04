using System;
using System.Collections.Generic;
using System.Linq;

namespace MaterialDockerImage.UI;

/// <summary>The regions of the workspace tab strip and the navigation rail that can be locked as a group.</summary>
public enum TabGroup { Pinned, Open, Rail }

/// <summary>What a person can do with a tab or a rail destination that a lock can refuse.</summary>
public enum TabAction { Activate, Close, Move, Pin }

/// <summary>The lock that is refusing an action: its id, a readable label and whether it is a group lock.</summary>
public sealed record LockBlock(string Id, string Label, bool IsGroup);

/// <summary>
/// Locks on workspace tabs and on tab groups. A tab lock is per tab (keyed by its destination) and refuses switching to the tab, closing it, moving it and pinning or unpinning it.
/// A group lock is separate and has its own credentials: it refuses closing, moving and pinning for every tab in the group (and, for the navigation rail, switching to a rail destination).
/// Locking a group never relocks its members, and a locked tab inside a locked group is two locks answered separately. The class is framework-free so it can be checked without a window.
/// </summary>
public static class TabLocks
{
    public static string TabId(string destination)
    {
        var slug = ElementIdentity.Slug(destination);
        var id = "tab." + (slug.Length == 0 ? "tab" : slug);
        if (id.Length <= 40) return id;
        // A long destination name keeps a readable start and a hash of the whole name, so two long names never share a lock.
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(destination)))[..8].ToLowerInvariant();
        return id[..31].TrimEnd('-', '.') + "-" + hash;
    }

    public static string GroupId(TabGroup group) => group switch { TabGroup.Pinned => "tabgroup.pinned", TabGroup.Open => "tabgroup.open", _ => "tabgroup.rail" };
    public static string TabLabel(string destination) => "Tab: " + destination;
    public static string GroupLabel(TabGroup group) => group switch { TabGroup.Pinned => "Tab group: Pinned tabs", TabGroup.Open => "Tab group: Open tabs", _ => "Tab group: Navigation rail" };

    /// <summary>Every group, so the Locks manager and the checks list them from one place.</summary>
    public static readonly IReadOnlyList<TabGroup> Groups = [TabGroup.Pinned, TabGroup.Open, TabGroup.Rail];

    /// <summary>The region a tab sits in: the workspace tab and pinned tabs share the pinned region, which keeps a stable place at the start of the strip.</summary>
    public static TabGroup GroupOf(bool pinned, bool workspace) => pinned || workspace ? TabGroup.Pinned : TabGroup.Open;

    public static bool IsTabLocked(UserSettings settings, string destination) => ElementLocks.IsLocked(settings, TabId(destination));
    public static bool IsGroupLocked(UserSettings settings, TabGroup group) => ElementLocks.IsLocked(settings, GroupId(group));

    /// <summary>
    /// The lock refusing <paramref name="action"/> on this tab, or null when it is allowed. The tab's own lock is answered first; a group lock refuses the structural actions
    /// (close, move, pin) for its members and, on the navigation rail, switching to a destination.
    /// </summary>
    public static LockBlock? Blocker(UserSettings settings, string destination, TabGroup group, TabAction action)
    {
        if (IsTabLocked(settings, destination)) return new LockBlock(TabId(destination), TabLabel(destination), false);
        var groupRefuses = group == TabGroup.Rail ? action == TabAction.Activate : action != TabAction.Activate;
        if (groupRefuses && IsGroupLocked(settings, group)) return new LockBlock(GroupId(group), GroupLabel(group), true);
        return null;
    }

    /// <summary>The lock that refuses a navigation request for this destination, or null. A locked destination must be unlocked first, so a shortcut, the palette or a rail click cannot walk around the lock.</summary>
    public static LockBlock? NavigationBlocker(UserSettings settings, string destination, bool viaRail) =>
        Blocker(settings, destination, viaRail ? TabGroup.Rail : TabGroup.Open, TabAction.Activate);

    /// <summary>The readable name of a tab group lock id, or null for any other id.</summary>
    public static string? LabelFor(string id)
    {
        foreach (var group in Groups) if (GroupId(group) == id) return GroupLabel(group);
        return null;
    }

    /// <summary>
    /// Bulk close candidates: locked tabs (and tabs in a locked group) are excluded exactly as pinned tabs are, unless the person explicitly includes them.
    /// Returns the indexes that stay open because of a lock.
    /// </summary>
    public static IReadOnlySet<int> BulkCloseProtected(UserSettings settings, IReadOnlyList<(string Destination, TabGroup Group)> tabs, bool includeLocked)
    {
        var protectedIndexes = new HashSet<int>();
        if (includeLocked) return protectedIndexes;
        for (var i = 0; i < tabs.Count; i++)
            if (Blocker(settings, tabs[i].Destination, tabs[i].Group, TabAction.Close) != null) protectedIndexes.Add(i);
        return protectedIndexes;
    }

    /// <summary>Marks the bulk-close candidates that a lock protects, so the selector keeps them open and says why.</summary>
    public static IReadOnlyList<TabCloseCandidate> MarkLocked(UserSettings settings, IReadOnlyList<TabCloseCandidate> candidates, IReadOnlyList<(string Destination, TabGroup Group)> tabs, bool includeLocked)
    {
        var locked = BulkCloseProtected(settings, tabs, includeLocked);
        return candidates.Select((c, i) => locked.Contains(i) ? c with { ToyLocked = true } : c).ToList();
    }
}

/// <summary>
/// Which lock, if any, guards a command-palette entry. A locked entry is still listed and labelled as locked; choosing it asks for the unlock
/// instead of running, so the palette cannot teleport past a lock.
/// </summary>
public static class PaletteLocks
{
    private static readonly IReadOnlyDictionary<string, string> Settings = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["set.theme"] = "theme", ["set.motion"] = "motion", ["set.contrast"] = "contrast", ["set.language"] = "language", ["set.sounds"] = "sounds",
        ["adhd.focus"] = "focusmode", ["adhd.lowstim"] = "lowstim", ["adhd.momentum"] = "momentum", ["adhd.time"] = "time", ["adhd.onething"] = "onething",
        ["settings.school"] = "school"
    };

    /// <summary>The lock id a command answers to: a go-to command to that destination's tab lock, a settings command to its settings row, otherwise null.</summary>
    public static string? LockIdFor(string commandId)
    {
        if (commandId.StartsWith("go.", StringComparison.Ordinal) && commandId.Length > 3) return TabLocks.TabId(commandId[3..]);
        return Settings.TryGetValue(commandId, out var id) ? id : null;
    }

    public static IReadOnlyCollection<string> SettingCommandIds => Settings.Keys.ToList();
}

/// <summary>Locked items in searches: they are labelled, matched by the word "locked", and may be hidden by an explicit, announced choice.</summary>
public static class LockedResults
{
    public const string Marker = "Locked";

    /// <summary>The text a search matches for an item; the word "Locked" is part of it so typing it finds every locked item.</summary>
    public static string SearchText(string text, bool locked) => locked ? text + " " + Marker : text;

    /// <summary>The visible label of an item: the text, with the locked marker appended when it is locked.</summary>
    public static string Label(string text, bool locked) => locked ? text + " · " + Marker : text;

    public static IReadOnlyList<T> Apply<T>(IEnumerable<T> items, Func<T, bool> isLocked, bool hideLocked, out int hidden)
    {
        var all = items.ToList();
        if (!hideLocked) { hidden = 0; return all; }
        var kept = all.Where(i => !isLocked(i)).ToList();
        hidden = all.Count - kept.Count;
        return kept;
    }

    /// <summary>The fixed sentence after the count, so it can be translated on its own.</summary>
    public const string HiddenSuffix = "locked item(s) are hidden from these results. Turn off Hide locked items in searches in Settings to see them.";

    /// <summary>Said beside a result list whenever hiding locked items made it shorter; empty when nothing was hidden. <paramref name="translate"/> translates the fixed sentence.</summary>
    public static string HiddenStatement(int hidden, Func<string, string>? translate = null) =>
        hidden <= 0 ? "" : hidden + " " + (translate ?? (x => x))(HiddenSuffix);
}
