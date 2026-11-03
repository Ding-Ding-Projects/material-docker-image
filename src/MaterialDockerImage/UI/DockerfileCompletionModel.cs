using System;
using System.Collections.Generic;
using System.Linq;

namespace MaterialDockerImage.UI;

/// <summary>What a suggestion inserts; decides the icon in the popup.</summary>
public enum SuggestionKind { Instruction, Directive, Flag, Value, Stage, Variable, Image, Path, Snippet, Key, Picker }

/// <summary>
/// One suggestion. <see cref="Insert"/> may contain <see cref="SuggestionText.CaretMark"/> where the caret goes after
/// accepting; multi-line inserts are re-indented to the current line. <see cref="FilterText"/> is what the typed
/// query is matched against (defaults to the label).
/// </summary>
public sealed record SuggestionItem(string Label, string Insert, SuggestionKind Kind, string Detail = "", string Doc = "", string Example = "", string? MinSyntax = null)
{
    public string? FilterOverride { get; init; }
    public string FilterText => FilterOverride ?? Label;
    /// <summary>Minimum labs frontend for features that were labs-only before <see cref="MinSyntax"/>.</summary>
    public string? LabsMinSyntax { get; init; }
    /// <summary>Lower ranks sort first when match scores are equal.</summary>
    public int Rank { get; init; }
    /// <summary>Short badge shown at the right of the row (for example "1.4+").</summary>
    public string Badge { get; init; } = "";
    /// <summary>True when the file's declared # syntax is older than <see cref="MinSyntax"/> (or there is none).</summary>
    public bool NeedsSyntaxUpgrade { get; init; }
    /// <summary>For <see cref="SuggestionKind.Picker"/> items: which interactive editor opens.</summary>
    public EditorValueKind PickerKind { get; init; }
    /// <summary>For picker items: a variant such as "file" or "folder" for path browsing.</summary>
    public string PickerVariant { get; init; } = "";
    /// <summary>True when accepting should immediately ask for the next suggestions (the insert ends in = or / for example).</summary>
    public bool Retrigger { get; init; }
}

/// <summary>The completion answer for one caret position.</summary>
public sealed record SuggestionResult(int Start, int Length, string Query, IReadOnlyList<SuggestionItem> Items, string Signature = "")
{
    public static readonly SuggestionResult Empty = new(0, 0, "", Array.Empty<SuggestionItem>());
    /// <summary>True when typing a space or trigger character here should open the popup without Ctrl+Space.</summary>
    public bool AutoTrigger { get; init; }
    /// <summary>The typed value at the caret when it has a known kind (offers a picker).</summary>
    public EditorValueSpan? Value { get; init; }
    /// <summary>The declared # syntax of the file (Dockerfile only).</summary>
    public DockerfileSyntaxVersion? Syntax { get; init; }
}

/// <summary>A local image tag from docker image ls --digests.</summary>
public sealed record SuggestionLocalImage(string Repository, string Tag, string Digest, string Id, string Size)
{
    public string Reference => Tag.Length == 0 || Tag == "<none>" ? Repository : Repository + ":" + Tag;
    public bool HasDigest => Digest.StartsWith("sha256:", StringComparison.Ordinal);
}

/// <summary>Everything outside the text that suggestions may use. All lists are real data supplied by the page.</summary>
public sealed record SuggestionSources(IReadOnlyList<SuggestionLocalImage> Images, IReadOnlyList<string> ContextEntries)
{
    public static readonly SuggestionSources None = new(Array.Empty<SuggestionLocalImage>(), Array.Empty<string>());
}

/// <summary>A text replacement computed from an accepted suggestion.</summary>
public readonly record struct SuggestionEdit(int Start, int Length, string Insert, int CaretAfter);

public static class SuggestionText
{
    /// <summary>Marks where the caret goes inside a snippet. Removed before insertion.</summary>
    public const char CaretMark = '‸';

    /// <summary>Builds the edit for an accepted item: replaces the query span, re-indents extra lines and places the caret.</summary>
    public static SuggestionEdit Apply(string text, SuggestionResult result, SuggestionItem item)
    {
        var start = Math.Clamp(result.Start, 0, text.Length);
        var length = Math.Clamp(result.Length, 0, text.Length - start);
        var lineStart = start == 0 ? 0 : text.LastIndexOf('\n', start - 1) + 1;
        var indent = new string(text.Skip(lineStart).TakeWhile(c => c is ' ' or '\t').ToArray());
        var insert = item.Insert.Replace("\r\n", "\n");
        if (indent.Length > 0 && insert.Contains('\n')) insert = insert.Replace("\n", "\n" + indent);
        if (text.Contains("\r\n")) insert = insert.Replace("\n", "\r\n");
        var mark = insert.IndexOf(CaretMark);
        if (mark >= 0) insert = insert.Remove(mark, 1);
        return new SuggestionEdit(start, length, insert, start + (mark >= 0 ? mark : insert.Length));
    }

    /// <summary>The start of the line holding <paramref name="index"/>.</summary>
    public static int LineStart(string text, int index) => index <= 0 ? 0 : text.LastIndexOf('\n', Math.Min(index, text.Length) - 1) + 1;

    /// <summary>The end of the line holding <paramref name="index"/> (before any \r\n).</summary>
    public static int LineEnd(string text, int index)
    {
        var end = text.IndexOf('\n', Math.Clamp(index, 0, text.Length));
        if (end < 0) end = text.Length;
        if (end > 0 && text[end - 1] == '\r') end--;
        return end;
    }
}

/// <summary>A matched item with the matched character positions (for bold rendering).</summary>
public sealed record SuggestionMatch(SuggestionItem Item, int[] Positions, int Score);

/// <summary>
/// Fuzzy prefix filtering: the first query character must match the first character of the filter text and the
/// rest must appear in order. Contiguous prefixes score highest. Picker items always stay on top.
/// </summary>
public static class SuggestionFuzzy
{
    public const int MaxItems = 200;

    public static bool TryMatch(string text, string query, out int[] positions, out int score)
    {
        positions = Array.Empty<int>();
        score = 0;
        if (query.Length == 0) return true;
        if (text.Length == 0 || char.ToLowerInvariant(text[0]) != char.ToLowerInvariant(query[0])) return false;
        var found = new int[query.Length];
        var t = 0;
        var gaps = 0;
        var exactCase = 0;
        for (var q = 0; q < query.Length; q++)
        {
            var c = char.ToLowerInvariant(query[q]);
            var start = t;
            while (t < text.Length && char.ToLowerInvariant(text[t]) != c) t++;
            if (t >= text.Length) return false;
            if (t != start) gaps++;
            if (text[t] == query[q]) exactCase++;
            found[q] = t++;
        }
        positions = found;
        var contiguous = found[^1] == query.Length - 1;
        score = 1000 - gaps * 40 - (found[^1] - query.Length + 1) * 3 - Math.Max(0, text.Length - query.Length) + (contiguous ? 500 : 0) + exactCase;
        if (contiguous && text.Length == query.Length) score += 200;
        return true;
    }

    public static IReadOnlyList<SuggestionMatch> Filter(IEnumerable<SuggestionItem> items, string query)
    {
        var matches = new List<SuggestionMatch>();
        foreach (var item in items)
        {
            if (item.Kind == SuggestionKind.Picker) { matches.Add(new(item, Array.Empty<int>(), int.MaxValue)); continue; }
            if (TryMatch(item.FilterText, query, out var positions, out var score))
            {
                // Bold positions refer to the label; when filtering on another text only bold a label prefix match.
                if (!ReferenceEquals(item.FilterText, item.Label) && item.FilterText != item.Label)
                    positions = item.Label.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? Enumerable.Range(0, query.Length).ToArray() : Array.Empty<int>();
                matches.Add(new(item, positions, score));
            }
        }
        return matches
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.Item.Rank)
            .ThenBy(m => m.Item.Label, StringComparer.OrdinalIgnoreCase)
            .GroupBy(m => (m.Item.Label, m.Item.Insert, m.Item.Kind)).Select(g => g.First())
            .Take(MaxItems)
            .ToList();
    }
}

/// <summary>The declared Dockerfile frontend from <c># syntax=</c>.</summary>
public sealed record DockerfileSyntaxVersion(string Raw, int Major, int Minor, bool Labs, bool Latest)
{
    /// <summary>Parses the image reference of a syntax directive. Returns null for references that are not docker/dockerfile.</summary>
    public static DockerfileSyntaxVersion? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var raw = value.Trim();
        var reference = raw;
        var at = reference.IndexOf('@');
        if (at >= 0) reference = reference[..at];
        foreach (var prefix in new[] { "docker.io/", "index.docker.io/" })
            if (reference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) reference = reference[prefix.Length..];
        var colon = reference.LastIndexOf(':');
        var name = colon < 0 ? reference : reference[..colon];
        var tag = colon < 0 ? "latest" : reference[(colon + 1)..];
        if (name.Equals("docker/dockerfile-upstream", StringComparison.OrdinalIgnoreCase)) return new(raw, 99, 0, true, true);
        if (!name.Equals("docker/dockerfile", StringComparison.OrdinalIgnoreCase)) return new(raw, 0, 0, false, true);
        var labs = tag.EndsWith("-labs", StringComparison.OrdinalIgnoreCase) || tag.Equals("labs", StringComparison.OrdinalIgnoreCase);
        if (labs) tag = tag.Length > 5 ? tag[..^5] : "latest";
        if (tag is "latest" or "1") return new(raw, 1, 999, labs, true);
        var parts = tag.Split('.');
        if (!int.TryParse(parts[0], out var major)) return new(raw, 0, 0, labs, true);
        var minor = parts.Length > 1 && int.TryParse(parts[1], out var m) ? m : 999;
        return new(raw, major, minor, labs, minor == 999);
    }

    /// <summary>True when this frontend supports a feature with the given minimum (for example "1.4"), or a labs minimum on a labs frontend.</summary>
    public bool Satisfies(string? minimum, string? labsMinimum = null)
    {
        if (minimum == null) return true;
        if (Major == 0) return true; // A custom frontend: its features are unknown, so no badge.
        if (Compare(minimum)) return true;
        return Labs && labsMinimum != null && Compare(labsMinimum);
    }

    private bool Compare(string version)
    {
        var parts = version.Split('.');
        var major = int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
        var minor = parts.Length > 1 ? int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 0;
        return Major > major || (Major == major && Minor >= minor);
    }
}
