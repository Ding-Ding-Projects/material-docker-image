namespace MaterialDockerImage.UI;

/// <summary>Next and previous match navigation over a bounded evaluation. Pure logic; the view highlights the current match.</summary>
public static class RegexMatchNavigator
{
    /// <summary>The next match after <paramref name="current"/>, wrapping to the first. -1 means no current match yet. Returns -1 when there are no matches.</summary>
    public static int Next(int count, int current) => count <= 0 ? -1 : current < 0 || current >= count - 1 ? 0 : current + 1;

    /// <summary>The match before <paramref name="current"/>, wrapping to the last.</summary>
    public static int Previous(int count, int current) => count <= 0 ? -1 : current <= 0 || current >= count ? count - 1 : current - 1;

    /// <summary>The first match that starts at or after the caret, or the first match when the caret is past the last one.</summary>
    public static int FromCaret(IReadOnlyList<RegexMatchResult> matches, int caret)
    {
        for (var i = 0; i < matches.Count; i++) if (matches[i].Index >= caret) return i;
        return matches.Count > 0 ? 0 : -1;
    }

    /// <summary>"Match 3 of 12" for the status line, or a plain statement when there is none.</summary>
    public static string Describe(IReadOnlyList<RegexMatchResult> matches, int index, bool truncated)
    {
        if (matches.Count == 0 || index < 0 || index >= matches.Count) return "No match to go to.";
        var m = matches[index];
        return $"Match {index + 1} of {matches.Count}{(truncated ? "+" : "")} at position {m.Index}" + (m.Length == 0 ? " (zero-width)" : $", {m.Length} character{(m.Length == 1 ? "" : "s")}") + ".";
    }
}
