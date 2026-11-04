namespace MaterialDockerImage.UI;

/// <summary>A tab as seen by the bulk close commands.</summary>
public sealed record TabCloseCandidate(int Index, string Label, bool Pinned, bool Locked, bool HasUnsavedWork, bool ToyLocked = false);

/// <summary>Result of a bulk close preview: what would close, what stays and why.</summary>
public sealed record TabCloseSelection(bool Valid, string Message, IReadOnlyList<TabCloseCandidate> ToClose, IReadOnlyList<(TabCloseCandidate Tab, string Reason)> Kept);

/// <summary>
/// "Close tabs containing text" and "Close tabs not containing text": one predicate (the field's plain-text or
/// regex query), with an empty and invalid query guard. The workspace tab never closes; tabs protected by a toy lock stay open unless locked tabs are explicitly included; pinned tabs and tabs with
/// unsaved work are kept unless explicitly included.
/// </summary>
public static class TabCloseSelector
{
    public static TabCloseSelection Select(IEnumerable<TabCloseCandidate> tabs, RegexSearchQuery query, bool containing, bool includePinned = false, bool includeLocked = false)
    {
        var all = tabs.ToList();
        if (query.IsEmpty) return new(false, "Type the text to match first. An empty query closes nothing.", [], []);
        if (!query.IsValid) return new(false, (query.Error ?? "Invalid pattern.") + " Nothing will close until the pattern is valid.", [], []);
        var close = new List<TabCloseCandidate>();
        var kept = new List<(TabCloseCandidate, string)>();
        foreach (var t in all)
        {
            if (query.Matches(t.Label) != containing) continue;
            if (t.Locked) kept.Add((t, "workspace tab, never closed"));
            else if (t.ToyLocked && !includeLocked) kept.Add((t, "locked"));
            else if (t.Pinned && !includePinned) kept.Add((t, "pinned"));
            else if (t.HasUnsavedWork) kept.Add((t, "has unsaved work"));
            else close.Add(t);
        }
        if (query.TimedOut) return new(false, "The pattern exceeded the match time limit. Simplify it; nothing will close.", [], []);
        var message = close.Count == 0 ? "No tab would close." : $"{close.Count} tab{(close.Count == 1 ? "" : "s")} will close.";
        if (kept.Count > 0) message += $" {kept.Count} matching tab{(kept.Count == 1 ? " stays" : "s stay")} open.";
        return new(true, message, close, kept);
    }
}
