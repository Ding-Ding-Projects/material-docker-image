using System;
using System.Collections.Generic;
using System.Linq;

namespace MaterialDockerImage.UI;

/// <summary>
/// What happens to a lock over time: unlocking under the chosen duration, locking again, the launch pass and the timer pass.
/// The caller supplies the settings and the store; nothing here touches a window.
/// </summary>
public static class LockLifecycle
{
    /// <summary>Unlocks one lock and starts its duration. Returns false when it was not locked. The caller has already obtained the confirmation or credential.</summary>
    public static bool Unlock(UserSettings settings, LockStore store, string id, out DateTimeOffset? until)
    {
        until = null;
        if (!ElementLocks.Unlock(settings, id)) return false;
        until = store.StartGrant(id);
        return true;
    }

    /// <summary>Locks one lock again and ends any running grant. Returns false when the id is invalid, already locked or the list is full.</summary>
    public static bool Relock(UserSettings settings, LockStore store, string id)
    {
        if (!ElementLocks.Lock(settings, id)) return false;
        store.EndGrant(id);
        return true;
    }

    /// <summary>"This action only": once the protected action has run, a lock unlocked for one action closes again. Returns true when it did.</summary>
    public static bool ConsumeOnce(UserSettings settings, LockStore store, string id) =>
        store.DurationOf(id) == UnlockDuration.Once && store.GrantIsActive(id) && !ElementLocks.IsLocked(settings, id) && Relock(settings, store, id);

    /// <summary>
    /// Locks again everything whose unlock has ended. At <paramref name="startup"/> every unlock that was only meant to last until the app closes, or for one action, ends,
    /// and a timed unlock ends if its time has passed. While the app runs only timed unlocks whose time has passed end. Returns the ids that were locked again.
    /// </summary>
    public static IReadOnlyList<string> Reconcile(UserSettings settings, LockStore store, bool startup)
    {
        var relocked = new List<string>();
        var now = store.Now;
        foreach (var record in store.Records.Where(r => r.GrantActive || r.GrantUntilUnix > 0).ToList())
        {
            if (ElementLocks.IsLocked(settings, record.Id)) { store.EndGrant(record.Id); continue; }
            var timed = UnlockDurations.Span(record.Duration) != null;
            var ended = timed ? record.GrantUntilUnix <= now.ToUnixTimeSeconds() : startup;
            if (ended && Relock(settings, store, record.Id)) relocked.Add(record.Id);
        }
        return relocked;
    }
}

/// <summary>One line of the Locks manager.</summary>
public sealed record LockRow(string Id, string Label, LockPolicy Policy, UnlockDuration Duration, bool Locked, DateTimeOffset? UnlockedUntil, int RecoveryLeft, bool HasPolicyRecord);

/// <summary>The Locks manager's list: every lock the app knows, searchable, exportable and removable one at a time or in bulk.</summary>
public static class LockManager
{
    public static IReadOnlyList<LockRow> Rows(UserSettings settings, LockStore store)
    {
        var ids = settings.LockedSettings.Concat(store.Records.Select(r => r.Id)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal);
        var rows = new List<LockRow>();
        foreach (var id in ids)
        {
            var record = store.Get(id);
            var locked = ElementLocks.IsLocked(settings, id);
            DateTimeOffset? until = !locked && record is { GrantActive: true, GrantUntilUnix: > 0 } ? DateTimeOffset.FromUnixTimeSeconds(record.GrantUntilUnix) : null;
            rows.Add(new LockRow(id, LabelOf(settings, id), record?.Policy ?? LockPolicy.None, record?.Duration ?? UnlockDuration.Manual, locked, until, record?.RecoveryHashes.Count ?? 0, record != null));
        }
        return rows;
    }

    /// <summary>The readable name of a lock: its stored label, else the settings row it belongs to, else the id itself.</summary>
    public static string LabelOf(UserSettings settings, string id) =>
        settings.LockLabels.TryGetValue(id, out var label) ? label
        : TabLocks.LabelFor(id) ?? AppearanceLocks.LabelFor(id)
        ?? (id.StartsWith("tab.", StringComparison.Ordinal) ? "Tab: " + id[4..] : ElementLocks.Properties.ContainsKey(id) ? "Setting: " + id : id);

    /// <summary>The text a search matches: id, label, policy, duration and the locked or unlocked state, so a locked result is always labelled as locked.</summary>
    public static string SearchText(LockRow row) =>
        $"{row.Id} {row.Label} {LockPolicies.Label(row.Policy)} {UnlockDurations.Label(row.Duration)} {(row.Locked ? "Locked" : "Unlocked")}";

    public static IReadOnlyList<LockRow> Filter(IEnumerable<LockRow> rows, Func<string, bool> matches) => rows.Where(r => matches(SearchText(r))).ToList();

    /// <summary>Remembers what a lock is for. Only valid ids and non-empty labels are kept.</summary>
    public static void SetLabel(UserSettings settings, string id, string label)
    {
        if (!ElementLocks.IsValidId(id)) return;
        var one = ElementLocks.NormalizeLabels(new Dictionary<string, string> { [id] = label });
        if (one.TryGetValue(id, out var clean) && (settings.LockLabels.ContainsKey(id) || settings.LockLabels.Count < ElementLocks.MaxLocks)) settings.LockLabels[id] = clean;
    }

    /// <summary>
    /// Removes a lock completely: the lock, its credentials, its recovery codes and its label. A lock that is currently locked is refused,
    /// because it has to be unlocked first like any other protected action. Returns true when something was removed.
    /// </summary>
    public static bool Remove(UserSettings settings, LockStore store, string id)
    {
        if (ElementLocks.IsLocked(settings, id)) return false;
        var had = store.HasPolicy(id) || settings.LockLabels.ContainsKey(id);
        store.Remove(id);
        settings.LockLabels.Remove(id);
        return had;
    }

    /// <summary>Removes every unlocked lock in <paramref name="ids"/>. Locked ones are skipped and reported, never removed by the bulk action.</summary>
    public static (IReadOnlyList<string> Removed, IReadOnlyList<string> Skipped) RemoveMany(UserSettings settings, LockStore store, IEnumerable<string> ids)
    {
        var removed = new List<string>(); var skipped = new List<string>();
        foreach (var id in ids.Distinct(StringComparer.Ordinal)) (Remove(settings, store, id) ? removed : skipped).Add(id);
        return (removed, skipped);
    }

    public static readonly IReadOnlyList<string> ExportHeaders = ["Id", "Label", "Policy", "Duration", "State", "Recovery codes left"];

    /// <summary>Export rows. Only descriptions: no credential, hash or secret is ever part of a row.</summary>
    public static IReadOnlyList<string[]> ExportRows(IEnumerable<LockRow> rows) =>
        rows.Select(r => new[] { r.Id, r.Label, LockPolicies.Label(r.Policy), UnlockDurations.Label(r.Duration), r.Locked ? "Locked" : "Unlocked", r.RecoveryLeft.ToString() }).ToList();
}

/// <summary>
/// Stable lock ids for elements that are not settings rows. An element with an automation id keeps the same lock when it moves; otherwise the id comes from its page,
/// type, name and position among identical siblings. Ids always fit the lock id pattern (lower case, at most 40 characters).
/// </summary>
public static class ElementIdentity
{
    public static string Slug(string? text)
    {
        var sb = new System.Text.StringBuilder();
        var dash = false;
        foreach (var c in (text ?? "").ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c)) { sb.Append(c); dash = false; }
            else if (!dash && sb.Length > 0) { sb.Append('-'); dash = true; }
        }
        return sb.ToString().Trim('-');
    }

    /// <summary>
    /// Derives the id. <paramref name="duplicateIndex"/> is the zero-based position among elements of the same page, type and name, so two identical "OK" buttons get different locks.
    /// </summary>
    public static string Derive(string pageKey, string? automationId, string? automationName, string typeName, int duplicateIndex = 0)
    {
        var page = Slug(pageKey);
        string basis, key;
        if (!string.IsNullOrWhiteSpace(automationId)) { basis = Slug(automationId); key = $"{page}|id|{automationId!.Trim()}"; }
        else if (!string.IsNullOrWhiteSpace(automationName)) { basis = Slug(typeName) + "-" + Slug(automationName); key = $"{page}|name|{typeName}|{automationName!.Trim()}|{duplicateIndex}"; }
        else { basis = Slug(typeName); key = $"{page}|type|{typeName}|{duplicateIndex}"; }
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..8].ToLowerInvariant();
        static string Cut(string text, int max) => text.Length > max ? text[..max].TrimEnd('-') : text;
        return $"el.{Cut(page, 8)}.{Cut(basis, 15)}-{hash}";
    }

    /// <summary>A readable name for the manager: the element kind and its accessible name.</summary>
    public static string Describe(string typeName, string? automationName, string? automationId)
    {
        var name = !string.IsNullOrWhiteSpace(automationName) ? automationName!.Trim() : !string.IsNullOrWhiteSpace(automationId) ? automationId!.Trim() : "(unnamed)";
        return typeName + ": " + name;
    }
}
