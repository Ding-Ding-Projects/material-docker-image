using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>One change: the full commit SHA, its calendar date, the original subject line and a derived category.</summary>
public sealed record ChangelogEntry(string Sha, string Date, string Subject, string Category);

/// <summary>One version heading (a git tag) or the unreleased tail, with its changes newest first.</summary>
public sealed record ChangelogGroup(string Version, string Date, bool Unreleased, IReadOnlyList<ChangelogEntry> Entries);

public sealed record ChangelogData(string Repository, string Note, IReadOnlyList<ChangelogGroup> Groups)
{
    public int EntryCount => Groups.Sum(g => g.Entries.Count);
}

/// <summary>
/// Framework-free changelog logic. The bundled JSON is produced at build time by build/make-changelog.ps1 straight from git log,
/// so nothing here is invented: entries are original subject lines, dated by commit, grouped under the tag that first contained them.
/// </summary>
public static class ChangelogModel
{
    public const string SchemaName = "material-docker-image/changelog";
    public const int SchemaVersion = 1;
    public const int MaxChars = 4 * 1024 * 1024;
    private static readonly Regex Sha = new("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);

    public static ChangelogData Parse(string json)
    {
        if (json.Length > MaxChars) throw new InvalidDataException("The bundled changelog is larger than the 4 MB limit.");
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = doc.RootElement;
            if (root.GetProperty("schema").GetString() != SchemaName || root.GetProperty("version").GetInt32() != SchemaVersion)
                throw new InvalidDataException("The bundled changelog has an unsupported schema.");
            var repository = root.GetProperty("repository").GetString() ?? "";
            if (!Regex.IsMatch(repository, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")) throw new InvalidDataException("The changelog repository name is not valid.");
            var groups = new List<ChangelogGroup>();
            foreach (var g in root.GetProperty("groups").EnumerateArray())
            {
                var entries = new List<ChangelogEntry>();
                foreach (var e in g.GetProperty("entries").EnumerateArray())
                {
                    var sha = e.GetProperty("sha").GetString() ?? "";
                    var date = e.GetProperty("date").GetString() ?? "";
                    var subject = e.GetProperty("subject").GetString() ?? "";
                    if (!Sha.IsMatch(sha)) throw new InvalidDataException("A changelog entry has a malformed commit SHA.");
                    if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) throw new InvalidDataException("A changelog entry has a malformed date.");
                    entries.Add(new ChangelogEntry(sha, date, subject, Categorize(subject)));
                }
                var unreleased = g.TryGetProperty("unreleased", out var u) && u.GetBoolean();
                groups.Add(new ChangelogGroup(g.GetProperty("version").GetString() ?? "", g.GetProperty("date").GetString() ?? "", unreleased, entries));
            }
            return new ChangelogData(repository, root.GetProperty("note").GetString() ?? "", groups);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("The bundled changelog could not be read: " + e.Message);
        }
    }

    /// <summary>Category from the verb the subject starts with; anything else is "Other" rather than a guess.</summary>
    public static string Categorize(string subject)
    {
        var first = Regex.Match(subject.TrimStart(), @"^[A-Za-z]+").Value.ToLowerInvariant();
        return first switch
        {
            "add" or "adds" or "added" or "introduce" or "create" or "ship" or "implement" => "Added",
            "fix" or "fixes" or "fixed" or "repair" or "correct" or "resolve" => "Fixed",
            "remove" or "removes" or "removed" or "drop" or "delete" => "Removed",
            "update" or "change" or "improve" or "make" or "move" or "rename" or "replace" or "harden" or "refactor" or "split" or "extend" or "rework" => "Changed",
            "document" or "docs" or "record" => "Documentation",
            _ => "Other",
        };
    }

    public static string CommitUrl(string repository, string sha) => "https://github.com/" + repository + "/commit/" + sha;
    public static string ShortSha(string sha) => sha.Length > 7 ? sha[..7] : sha;

    /// <summary>Accepts an ISO date or the current culture's short date.</summary>
    public static bool TryParseDate(string text, out DateOnly date)
    {
        date = default;
        text = text.Trim();
        if (text.Length == 0) return false;
        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
            || DateOnly.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out date);
    }

    /// <summary>Validates the two typed bounds (empty is no bound). Returns an inline message, empty when fine; typed text is never discarded.</summary>
    public static string ValidateRange(string from, string to, out DateOnly? start, out DateOnly? end)
    {
        start = end = null;
        if (from.Trim().Length > 0) { if (!TryParseDate(from, out var s)) return "The start date is not a valid date. Use YYYY-MM-DD."; start = s; }
        if (to.Trim().Length > 0) { if (!TryParseDate(to, out var e)) return "The end date is not a valid date. Use YYYY-MM-DD."; end = e; }
        if (start is { } a && end is { } b && a > b) return "The start date is after the end date.";
        return "";
    }

    /// <summary>Search and date range compose: an entry must satisfy both. The matcher sees version, category, subject, date and SHA.</summary>
    public static IReadOnlyList<ChangelogGroup> Filter(ChangelogData data, Func<string, bool> matches, DateOnly? from, DateOnly? to)
    {
        var result = new List<ChangelogGroup>();
        foreach (var group in data.Groups)
        {
            var kept = new List<ChangelogEntry>();
            foreach (var entry in group.Entries)
            {
                TryParseDate(entry.Date, out var day);
                if (from is { } f && day < f) continue;
                if (to is { } t && day > t) continue;
                if (!matches(group.Version + " " + entry.Category + " " + entry.Subject + " " + entry.Date + " " + entry.Sha)) continue;
                kept.Add(entry);
            }
            if (kept.Count > 0) result.Add(group with { Entries = kept });
        }
        return result;
    }

    private static readonly string[] CategoryOrder = ["Added", "Changed", "Fixed", "Removed", "Documentation", "Other"];

    public static IEnumerable<IGrouping<string, ChangelogEntry>> ByCategory(ChangelogGroup group) =>
        group.Entries.GroupBy(e => e.Category).OrderBy(g => { var i = Array.IndexOf(CategoryOrder, g.Key); return i < 0 ? 99 : i; });

    /// <summary>Markdown for the filtered view. It states the range and search and keeps full SHAs as text so it stays traceable.</summary>
    public static string ToMarkdown(ChangelogData data, IReadOnlyList<ChangelogGroup> shown, string rangeText, string searchText)
    {
        var sb = new StringBuilder();
        sb.Append("# Changelog\n\n");
        sb.Append("Repository: ").Append(data.Repository).Append('\n');
        sb.Append("Range: ").Append(rangeText.Length > 0 ? rangeText : "all dates").Append('\n');
        sb.Append("Search: ").Append(searchText.Length > 0 ? searchText : "none").Append('\n');
        sb.Append("Shown: ").Append(shown.Sum(g => g.Entries.Count)).Append(" of ").Append(data.EntryCount).Append(" changes\n\n");
        sb.Append("> ").Append(data.Note).Append("\n\n");
        foreach (var group in shown)
        {
            sb.Append("## ").Append(group.Version);
            if (group.Date.Length > 0) sb.Append(" (").Append(group.Date).Append(')');
            sb.Append("\n\n");
            foreach (var category in ByCategory(group))
            {
                sb.Append("### ").Append(category.Key).Append("\n\n");
                foreach (var entry in category) sb.Append("- ").Append(entry.Subject.Replace("\r", " ").Replace("\n", " ")).Append(" (").Append(entry.Date).Append(", `").Append(entry.Sha).Append("`)\n");
                sb.Append('\n');
            }
        }
        if (shown.Count == 0) sb.Append("No changes match the range and search.\n");
        return sb.ToString();
    }
}
