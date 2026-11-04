namespace MaterialDockerImage.UI;

/// <summary>Outcome of importing a library file: what was added and what was left out, with reasons.</summary>
public sealed record RegexImportResult(List<RegexSavedPattern> Library, int Added, IReadOnlyList<string> Skipped);

public static partial class RegexPatternStore
{
    /// <summary>
    /// Merges imported patterns into <paramref name="existing"/>. A pattern is skipped, never replaced, when its name
    /// already exists, when it does not compile, or when the library is full. Nothing executes; imported text is data.
    /// </summary>
    public static RegexImportResult Import(IReadOnlyList<RegexSavedPattern> existing, string text, bool replaceSameName = false)
    {
        var incoming = Parse(text);
        var library = existing.ToList();
        var skipped = new List<string>();
        var added = 0;
        foreach (var entry in incoming)
        {
            var name = entry.Name.Trim();
            var clash = library.FindIndex(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (clash >= 0 && !replaceSameName) { skipped.Add($"{name}: a pattern with this name already exists."); continue; }
            var check = RegexBuilderEngine.Validate(entry.Pattern, entry.Flags);
            if (!check.Valid) { skipped.Add($"{name}: {check.Error}"); continue; }
            if (clash >= 0) library.RemoveAt(clash);
            if (library.Count >= MaxEntries) { skipped.Add($"{name}: the library already holds {MaxEntries} entries."); continue; }
            library.Insert(0, entry with { Name = name });
            added++;
        }
        return new(library, added, skipped);
    }

    /// <summary>Saved patterns whose name, pattern or description contains the text (ignoring case); an empty query returns everything.</summary>
    public static List<RegexSavedPattern> Search(IReadOnlyList<RegexSavedPattern> library, string? query) =>
        string.IsNullOrWhiteSpace(query) ? library.ToList()
        : library.Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || p.Pattern.Contains(query, StringComparison.OrdinalIgnoreCase) || p.Description.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>
    /// Saved patterns whose name, pattern or description match a filter field's query (plain text or regular expression, with that field's own flags).
    /// An empty query returns everything; an invalid or timed-out one returns nothing, and the query reports why.
    /// </summary>
    public static List<RegexSavedPattern> Search(IReadOnlyList<RegexSavedPattern> library, RegexSearchQuery query) =>
        query.IsEmpty ? library.ToList() : library.Where(p => query.MatchesFields([p.Name, p.Pattern, p.Description])).ToList();

    /// <summary>Built-in snippets that match a filter field's query on their name, description or pattern. Same rules as the saved-pattern overload.</summary>
    public static List<RegexSnippet> SearchSnippets(RegexSearchQuery query) =>
        query.IsEmpty ? RegexBuilderLibrary.Snippets.ToList() : RegexBuilderLibrary.Snippets.Where(s => query.MatchesFields([s.Name, s.Description, s.Pattern])).ToList();

    /// <summary>Built-in snippets whose name, description or pattern contains the text (ignoring case).</summary>
    public static List<RegexSnippet> SearchSnippets(string? query) =>
        string.IsNullOrWhiteSpace(query) ? RegexBuilderLibrary.Snippets.ToList()
        : RegexBuilderLibrary.Snippets.Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || s.Description.Contains(query, StringComparison.OrdinalIgnoreCase) || s.Pattern.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
}
