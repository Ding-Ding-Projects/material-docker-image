using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MaterialDockerImage.UI;

/// <summary>One lockable appearance value: the lock id, a readable name and the appearance member it guards.</summary>
public sealed record AppearanceLockItem(string Id, string Name, string StateMember);

/// <summary>
/// Locks on individual appearance values, at the scopes the appearance editor edits at: one global value (theme, seed colour, font size and so on), one colour role,
/// a whole saved preset, one property of one customized element, or a whole customized element. Every lock has its own id and so its own policy and credentials; a locked
/// colour role inside a locked "roles" row is two locks. Framework-free so the rules can be checked without a window.
/// </summary>
public static class AppearanceLocks
{
    /// <summary>Every appearance value of the editor that has a settings-row lock. Each member of <c>AppearanceState</c> appears exactly once, so none is left out.</summary>
    public static readonly IReadOnlyList<AppearanceLockItem> Values =
    [
        new("theme", "Theme", nameof(AppearanceState.Theme)),
        new("seed", "Seed colour", nameof(AppearanceState.Seed)),
        new("roles", "Colour roles (all)", nameof(AppearanceState.Roles)),
        new("radius", "Corner radius scale", nameof(AppearanceState.RadiusScale)),
        new("density", "Density", nameof(AppearanceState.Density)),
        new("uifont", "Interface font", nameof(AppearanceState.UiFont)),
        new("monofont", "Code font", nameof(AppearanceState.MonoFont)),
        new("fontsize", "Font size", nameof(AppearanceState.FontSize)),
        new("headingweight", "Heading weight", nameof(AppearanceState.HeadingWeight)),
        new("railicons", "Rail icon style", nameof(AppearanceState.RailIcons)),
        new("elevation", "Elevation", nameof(AppearanceState.Elevation)),
        new("tint", "Surface tint", nameof(AppearanceState.SurfaceTint)),
        new("rainbow", "Rainbow accent", nameof(AppearanceState.Rainbow)),
        new("motionlevel", "Motion level", nameof(AppearanceState.MotionLevel)),
        new("motionspeed", "Motion speed", nameof(AppearanceState.MotionSpeed))
    ];

    /// <summary>The element-style properties that can be locked one at a time, with the short key used in their lock ids.</summary>
    public static readonly IReadOnlyList<(string Property, string Key, string Name)> ElementProperties =
    [
        (nameof(ElementStyle.Background), "bg", "Background colour"),
        (nameof(ElementStyle.Foreground), "fg", "Text colour"),
        (nameof(ElementStyle.CornerRadius), "radius", "Corner radius"),
        (nameof(ElementStyle.FontSize), "size", "Font size"),
        (nameof(ElementStyle.FontWeight), "weight", "Font weight"),
        (nameof(ElementStyle.Padding), "padding", "Padding"),
        (nameof(ElementStyle.Hidden), "hidden", "Visibility")
    ];

    public static string? IdForStateMember(string member) => Values.FirstOrDefault(v => v.StateMember == member)?.Id;

    private static string Hash(string text, int length) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..length].ToLowerInvariant();

    /// <summary>The lock for one colour role: "role." plus the lower-case role key.</summary>
    public static string RoleId(string roleKey) => "role." + roleKey.ToLowerInvariant();

    /// <summary>The lock for a whole saved preset. The exact name is hashed so two presets never share a lock.</summary>
    public static string PresetId(string presetName)
    {
        var slug = ElementIdentity.Slug(presetName);
        if (slug.Length > 18) slug = slug[..18].TrimEnd('-');
        return "preset." + (slug.Length == 0 ? "preset" : slug) + "-" + Hash(presetName, 8);
    }

    /// <summary>The lock for one property of one customized element, found again by the element's stable key.</summary>
    public static string ElementPropertyId(string elementKey, string property)
    {
        var key = ElementProperties.FirstOrDefault(p => p.Property == property).Key ?? ElementIdentity.Slug(property);
        return "ap." + Hash(elementKey, 8) + "." + key;
    }

    /// <summary>The lock for a whole customized element: every property of it.</summary>
    public static string ElementId(string elementKey) => "ap." + Hash(elementKey, 8) + ".element";

    /// <summary>Every lock id that guards a property of this element, narrowest first.</summary>
    public static IReadOnlyList<string> ElementLockIds(string elementKey, string property)
    {
        var ids = new List<string> { ElementPropertyId(elementKey, property), ElementId(elementKey) };
        if (ElementStyle.LockIdFor(property) is { } global) ids.Add(global);
        return ids;
    }

    public static LockBlock? ElementPropertyBlocker(UserSettings settings, string elementKey, string property)
    {
        foreach (var id in ElementLockIds(elementKey, property))
            if (ElementLocks.IsLocked(settings, id)) return new LockBlock(id, LabelFor(id) ?? settings.LockLabels.GetValueOrDefault(id) ?? id, false);
        return null;
    }

    public static LockBlock? PresetBlocker(UserSettings settings, string presetName) =>
        ElementLocks.IsLocked(settings, PresetId(presetName)) ? new LockBlock(PresetId(presetName), "Preset: " + presetName, false) : null;

    public static LockBlock? RoleBlocker(UserSettings settings, string roleKey)
    {
        if (ElementLocks.IsLocked(settings, RoleId(roleKey))) return new LockBlock(RoleId(roleKey), "Colour role: " + (AppearanceRoles.Find(roleKey)?.Name ?? roleKey), false);
        return ElementLocks.IsLocked(settings, "roles") ? new LockBlock("roles", "Appearance: Colour roles (all)", false) : null;
    }

    /// <summary>A readable name for an appearance lock id, or null when the id is not one of them.</summary>
    public static string? LabelFor(string id)
    {
        if (Values.FirstOrDefault(v => v.Id == id) is { } value) return "Appearance: " + value.Name;
        if (id.StartsWith("role.", StringComparison.Ordinal) && AppearanceRoles.All.FirstOrDefault(r => RoleId(r.Key) == id) is { } role) return "Colour role: " + role.Name;
        if (id.StartsWith("ap.", StringComparison.Ordinal))
        {
            var last = id[(id.LastIndexOf('.') + 1)..];
            if (last == "element") return "Customized element (all properties)";
            foreach (var item in ElementProperties) if (item.Key == last) return "Customized element: " + item.Name;
        }
        return null;
    }

    /// <summary>Comparable text of an appearance lock's current value, so an unlock for one action can tell when the person changed it. Null for ids that are not roles.</summary>
    public static string? SnapshotOf(UserSettings settings, string id)
    {
        var role = AppearanceRoles.All.FirstOrDefault(r => RoleId(r.Key) == id);
        if (role == null) return null;
        return settings.RoleOverrides.TryGetValue(role.Key, out var value) ? value : "";
    }

    // ----- Guards -------------------------------------------------------------------------------------------------

    private static bool Same(object? a, object? b) => Equals(a, b);

    private static object? Read(ElementStyle style, string property) => property switch
    {
        nameof(ElementStyle.Background) => style.Background,
        nameof(ElementStyle.Foreground) => style.Foreground,
        nameof(ElementStyle.CornerRadius) => style.CornerRadius,
        nameof(ElementStyle.FontSize) => style.FontSize,
        nameof(ElementStyle.FontWeight) => style.FontWeight,
        nameof(ElementStyle.Padding) => style.Padding,
        _ => style.Hidden
    };

    private static void Write(ElementStyle target, ElementStyle from, string property)
    {
        switch (property)
        {
            case nameof(ElementStyle.Background): target.Background = from.Background; break;
            case nameof(ElementStyle.Foreground): target.Foreground = from.Foreground; break;
            case nameof(ElementStyle.CornerRadius): target.CornerRadius = from.CornerRadius; break;
            case nameof(ElementStyle.FontSize): target.FontSize = from.FontSize; break;
            case nameof(ElementStyle.FontWeight): target.FontWeight = from.FontWeight; break;
            case nameof(ElementStyle.Padding): target.Padding = from.Padding; break;
            default: target.Hidden = from.Hidden; break;
        }
    }

    /// <summary>
    /// The style that may actually be stored: every property a lock covers keeps its existing value, whatever the proposal says (so editing, resetting and importing are all refused for
    /// locked properties). <paramref name="refused"/> lists the properties that were held back, so the caller can say so. The proposal itself is never changed.
    /// </summary>
    public static ElementStyle Guard(UserSettings settings, string elementKey, ElementStyle? existing, ElementStyle proposed, out IReadOnlyList<string> refused)
    {
        var current = existing ?? new ElementStyle();
        var result = proposed.Clone();
        var held = new List<string>();
        foreach (var (property, _, _) in ElementProperties)
        {
            if (Same(Read(current, property), Read(proposed, property))) continue;
            if (ElementPropertyBlocker(settings, elementKey, property) == null) continue;
            Write(result, current, property);
            held.Add(property);
        }
        refused = held;
        return result;
    }

    /// <summary>Colour role overrides that may be stored: every locked role (or all roles, when the whole row is locked) keeps its existing value or stays absent.</summary>
    public static Dictionary<string, string> GuardRoles(UserSettings settings, IReadOnlyDictionary<string, string> existing, IReadOnlyDictionary<string, string> proposed, out IReadOnlyList<string> refused)
    {
        var result = new Dictionary<string, string>(proposed, StringComparer.Ordinal);
        var held = new List<string>();
        foreach (var key in existing.Keys.Concat(proposed.Keys).Distinct(StringComparer.Ordinal))
        {
            existing.TryGetValue(key, out var before); proposed.TryGetValue(key, out var after);
            if (before == after || RoleBlocker(settings, key) == null) continue;
            if (before == null) result.Remove(key); else result[key] = before;
            held.Add(key);
        }
        refused = held;
        return result;
    }

    /// <summary>
    /// The appearance that may actually be applied: every locked global value (and every locked colour role) keeps what it was before the change, whoever asked for it
    /// (an editor control, a preset, a paste, an undo or an import). <paramref name="refused"/> lists the lock ids that held something back.
    /// </summary>
    public static AppearanceState GuardState(UserSettings settings, AppearanceState before, AppearanceState proposed, out IReadOnlyList<string> refused)
    {
        var result = proposed.Clone();
        var held = new List<string>();
        foreach (var item in Values)
        {
            if (item.Id == "roles" || !ElementLocks.IsLocked(settings, item.Id)) continue;
            var property = typeof(AppearanceState).GetProperty(item.StateMember);
            if (property == null) continue;
            var was = property.GetValue(before);
            if (Equals(was, property.GetValue(result))) continue;
            property.SetValue(result, was);
            held.Add(item.Id);
        }
        var roles = GuardRoles(settings, before.Roles, result.Roles, out var heldRoles);
        if (heldRoles.Count > 0) { result.Roles = roles; held.AddRange(heldRoles.Select(RoleId)); }
        refused = held;
        return result;
    }

    /// <summary>Copies locked colour roles from the current settings onto a target settings object (an import, a preset or a reset), so they do not change. Returns the roles kept.</summary>
    public static IReadOnlyList<string> PreserveRoles(UserSettings current, UserSettings target)
    {
        var kept = new List<string>();
        foreach (var role in AppearanceRoles.All)
        {
            if (!ElementLocks.IsLocked(current, RoleId(role.Key))) continue;
            if (current.RoleOverrides.TryGetValue(role.Key, out var value)) target.RoleOverrides[role.Key] = value; else target.RoleOverrides.Remove(role.Key);
            kept.Add(role.Key);
        }
        return kept;
    }

    /// <summary>
    /// Bulk locking: which of <paramref name="ids"/> would get a lock. Each one still gets its own wizard, policy and credentials; this only works out which are valid, new and fit in the list.
    /// </summary>
    public static (IReadOnlyList<string> ToLock, IReadOnlyList<(string Id, string Reason)> Skipped) PlanBulk(UserSettings settings, IEnumerable<string> ids)
    {
        var toLock = new List<string>(); var skipped = new List<(string, string)>();
        var room = ElementLocks.MaxLocks - settings.LockedSettings.Count;
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
        {
            if (!ElementLocks.IsValidId(id)) skipped.Add((id, "not a valid lock id"));
            else if (ElementLocks.IsLocked(settings, id)) skipped.Add((id, "already locked"));
            else if (room <= 0) skipped.Add((id, "the lock list is full"));
            else { toLock.Add(id); room--; }
        }
        return (toLock, skipped);
    }

    /// <summary>Every colour role lock id, for the bulk "lock every role" command.</summary>
    public static IReadOnlyList<string> AllRoleIds() => AppearanceRoles.All.Select(r => RoleId(r.Key)).ToList();
}
