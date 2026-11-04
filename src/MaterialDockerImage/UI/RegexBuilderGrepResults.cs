using System.Text;
using System.Text.Json;

namespace MaterialDockerImage.UI;

/// <summary>A saved folder-search result list read back from a file. The matches are as they were when saved; the files are not read again.</summary>
public sealed record RegexGrepSavedList(string Pattern, RegexBuilderFlags Flags, string Folder, string Globs, DateTimeOffset Saved, IReadOnlyList<RegexGrepMatch> Matches);

/// <summary>
/// Saving a folder-search result list (JSON, which can be loaded again, or plain text with context lines, or CSV), the date filter text and the command line
/// that opens a result in an editor. Pure logic; the dialogs and the process start are the view's.
/// </summary>
public static class RegexGrepResults
{
    public const string Format = "material-docker-image/regex-grep-results";
    public const int MaxSavedMatches = RegexGrepOptions.HardMaxResults;
    public static readonly IReadOnlyList<string> Formats = ["JSON (can be loaded again)", "Text with context", "CSV"];

    // ----- date window -----

    /// <summary>
    /// Reads a date filter typed as <c>yyyy-MM-dd</c> or <c>yyyy-MM-dd HH:mm</c> in local time. An empty text means no bound. For an upper bound a date without a
    /// time covers the whole day.
    /// </summary>
    public static (DateTime? Utc, string? Problem) ParseDate(string? text, bool endOfDay)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null);
        var value = text.Trim();
        var dateOnly = DateTime.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d);
        if (!dateOnly && !DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out d))
            return (null, "Write the date as 2026-11-04 or 2026-11-04 13:30.");
        if (dateOnly && endOfDay) d = d.AddDays(1).AddTicks(-1);
        return (DateTime.SpecifyKind(d, DateTimeKind.Local).ToUniversalTime(), null);
    }

    // ----- saving -----

    public static string Export(string format, string pattern, RegexBuilderFlags flags, RegexGrepOptions options, RegexGrepResult result, DateTimeOffset? now = null) => format switch
    {
        "CSV" => ToCsv(result.Matches),
        "Text with context" => ToText(pattern, result),
        _ => ToJson(pattern, flags, options, result, now ?? DateTimeOffset.UtcNow),
    };

    public static string ToText(string pattern, RegexGrepResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Pattern: " + pattern);
        sb.AppendLine(result.Summary());
        foreach (var group in result.Matches.GroupBy(m => m.File))
        {
            sb.AppendLine().AppendLine(group.Key);
            foreach (var m in group)
            {
                var first = m.Line - m.ContextBefore.Count;
                for (var i = 0; i < m.ContextBefore.Count; i++) sb.AppendLine($"  {first + i,5}  {m.ContextBefore[i]}");
                sb.AppendLine($"  {m.Line,5}: {m.LineText}   [column {m.Column}, length {m.Length}]");
                for (var i = 0; i < m.ContextAfter.Count; i++) sb.AppendLine($"  {m.Line + 1 + i,5}  {m.ContextAfter[i]}");
                if (m.ContextBefore.Count + m.ContextAfter.Count > 0) sb.AppendLine("  --");
            }
        }
        return sb.ToString();
    }

    private static string Csv(string value)
    {
        // A cell that starts like a formula is prefixed so a spreadsheet shows it as text.
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    public static string ToCsv(IReadOnlyList<RegexGrepMatch> matches)
    {
        var sb = new StringBuilder("File,Line,Column,Length,Match,Line text\r\n");
        foreach (var m in matches) sb.Append(string.Join(',', Csv(m.File), m.Line, m.Column, m.Length, Csv(m.Text), Csv(m.LineText))).Append("\r\n");
        return sb.ToString();
    }

    public static string ToJson(string pattern, RegexBuilderFlags flags, RegexGrepOptions options, RegexGrepResult result, DateTimeOffset saved)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("format", Format);
            w.WriteNumber("version", 1);
            w.WriteString("saved", saved);
            w.WriteString("pattern", pattern);
            w.WriteStartObject("flags");
            foreach (var (name, _, _) in RegexBuilderFlags.All) w.WriteBoolean(name, flags.Get(name));
            w.WriteEndObject();
            w.WriteString("folder", options.Folder);
            w.WriteString("globs", options.Globs);
            w.WriteString("summary", result.Summary());
            w.WriteStartArray("matches");
            foreach (var m in result.Matches.Take(MaxSavedMatches))
            {
                w.WriteStartObject();
                w.WriteString("file", m.File); w.WriteNumber("line", m.Line); w.WriteNumber("column", m.Column); w.WriteNumber("length", m.Length);
                w.WriteString("text", m.Text); w.WriteString("lineText", m.LineText);
                if (m.ContextBefore.Count > 0) { w.WriteStartArray("before"); foreach (var l in m.ContextBefore) w.WriteStringValue(l); w.WriteEndArray(); }
                if (m.ContextAfter.Count > 0) { w.WriteStartArray("after"); foreach (var l in m.ContextAfter) w.WriteStringValue(l); w.WriteEndArray(); }
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Reads a saved result list. Throws InvalidDataException with the reason for anything that is not one.</summary>
    public static RegexGrepSavedList Parse(string text)
    {
        if (text.Length > 16 * 1024 * 1024) throw new InvalidDataException("The result list is larger than 16 MiB and was not read.");
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var f) || f.GetString() != Format) throw new InvalidDataException("This is not a saved result list.");
            if (!root.TryGetProperty("version", out var v) || v.GetInt32() != 1) throw new InvalidDataException("The result list has an unsupported version.");
            var flags = new RegexBuilderFlags();
            if (root.TryGetProperty("flags", out var fl))
                foreach (var (name, _, _) in RegexBuilderFlags.All)
                    if (fl.TryGetProperty(name, out var b)) flags = flags.With(name, b.GetBoolean());
            string Text(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
            List<string> Lines(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Array ? p.EnumerateArray().Select(x => x.GetString() ?? "").Take(RegexGrep.MaxContextLines).ToList() : [];
            var matches = new List<RegexGrepMatch>();
            foreach (var e in root.GetProperty("matches").EnumerateArray())
            {
                if (matches.Count >= MaxSavedMatches) throw new InvalidDataException($"The result list has more than {MaxSavedMatches} matches.");
                var line = e.GetProperty("line").GetInt32(); var column = e.GetProperty("column").GetInt32(); var length = e.GetProperty("length").GetInt32();
                if (line < 1 || column < 1 || length < 0) throw new InvalidDataException("A match has a line, column or length that is not valid.");
                var file = Text(e, "file");
                if (file.Length == 0) throw new InvalidDataException("A match has no file.");
                matches.Add(new RegexGrepMatch(file, line, column, length, Text(e, "text"), Text(e, "lineText")) { ContextBefore = Lines(e, "before"), ContextAfter = Lines(e, "after") });
            }
            var saved = root.TryGetProperty("saved", out var s) && s.TryGetDateTimeOffset(out var when) ? when : DateTimeOffset.MinValue;
            return new RegexGrepSavedList(Text(root, "pattern"), flags, Text(root, "folder"), Text(root, "globs"), saved, matches);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("The result list is not valid: " + e.Message, e);
        }
    }

    // ----- open in editor -----

    /// <summary>
    /// The arguments that open a file at a line in an editor, chosen by the editor's file name. Visual Studio Code takes <c>--goto file:line:column</c>,
    /// Notepad++ takes <c>-nLINE -cCOLUMN file</c>, Sublime Text takes <c>file:line:column</c>. Any other editor is given the file alone, because guessing its
    /// line syntax would open the wrong thing.
    /// </summary>
    public static (IReadOnlyList<string> Arguments, bool PositionKnown) EditorArguments(string editorExecutable, string file, int line, int column)
    {
        var name = Path.GetFileNameWithoutExtension(editorExecutable ?? "").ToLowerInvariant();
        line = Math.Max(1, line); column = Math.Max(1, column);
        return name switch
        {
            "code" or "code - insiders" or "codium" => (["--goto", $"{file}:{line}:{column}"], true),
            "notepad++" => ([$"-n{line}", $"-c{column}", file], true),
            "subl" or "sublime_text" => ([$"{file}:{line}:{column}"], true),
            _ => ([file], false),
        };
    }
}
