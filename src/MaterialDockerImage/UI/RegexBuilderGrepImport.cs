using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>The outcome of reading the output of an external search tool. Nothing was run to produce it.</summary>
public sealed record RegexGrepImport(string Format, IReadOnlyList<RegexGrepMatch> Matches, int Malformed, IReadOnlyList<string> MalformedSamples, int LinesRead, bool Truncated, string? Problem)
{
    public bool Ok => Problem == null;

    public string Summary()
    {
        if (Problem != null) return Problem;
        var text = $"Imported {Matches.Count} result{(Matches.Count == 1 ? "" : "s")} from {Format} output ({LinesRead} line{(LinesRead == 1 ? "" : "s")} read)";
        if (Malformed > 0) text += $"; {Malformed} line{(Malformed == 1 ? "" : "s")} could not be read and {(Malformed == 1 ? "was" : "were")} left out";
        if (Truncated) text += $"; the list was cut at {RegexGrepResults.MaxSavedMatches} results";
        return text + ".";
    }
}

/// <summary>
/// Reads the output of an external search tool into the same match list the built-in folder search uses: <c>grep -n</c> (<c>line:text</c>), <c>grep -nH</c> and ripgrep without
/// headings (<c>file:line:text</c>), ripgrep <c>--vimgrep</c> (<c>file:line:column:text</c>) and ripgrep <c>--json</c>. Text is only parsed, never run, and is bounded in size,
/// line count and result count. A line that does not fit the format is counted and left out, never guessed at.
/// </summary>
public static class RegexGrepToolImport
{
    public const int MaxChars = 16 * 1024 * 1024;
    public const int MaxLines = 250_000;
    public const int MaxLineChars = 64 * 1024;
    public const int MaxSamples = 5;
    public const string Auto = "Auto-detect";
    public const string GrepN = "grep -n";
    public const string GrepNH = "grep -nH";
    public const string Vimgrep = "ripgrep --vimgrep";
    public const string Json = "ripgrep --json";
    public static readonly IReadOnlyList<string> Formats = [Auto, GrepN, GrepNH, Vimgrep, Json];
    public const string NoFile = "(standard input)";

    // A drive letter and its separator may start a path; after that a path runs up to the first colon, so a path containing another colon is not supported.
    private static readonly Regex VimgrepLine = new(@"^(?<file>(?:[A-Za-z]:[\\/])?[^:]+?):(?<line>\d{1,9}):(?<col>\d{1,9}):(?<text>.*)$", RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(250));
    private static readonly Regex HeaderLine = new(@"^(?<file>(?:[A-Za-z]:[\\/])?[^:]+?):(?<line>\d{1,9}):(?<text>.*)$", RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(250));
    private static readonly Regex PlainLine = new(@"^(?<line>\d{1,9}):(?<text>.*)$", RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(250));

    /// <param name="baseFolder">Relative paths are made absolute against this folder when it is given.</param>
    /// <param name="defaultFile">The file name used for <c>grep -n</c> output, which has none.</param>
    public static RegexGrepImport Parse(string? text, string format = Auto, string? baseFolder = null, string? defaultFile = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return Fail(format, "There is no tool output to read.");
        if (text.Length > MaxChars) return Fail(format, "The tool output is larger than 16 MiB and was not read.");
        if (!Formats.Contains(format)) return Fail(format, "Choose one of the listed output formats.");
        var lines = text.Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0) lines = lines[..^1];
        if (lines.Length > MaxLines) return Fail(format, $"The tool output has more than {MaxLines} lines and was not read.");

        var rows = lines.Select(l => l.TrimEnd('\r')).ToArray();
        var chosen = format == Auto ? Detect(rows) : format;
        var matches = new List<RegexGrepMatch>();
        var samples = new List<string>();
        var malformed = 0; var read = 0; var truncated = false;
        void Bad(int number, string reason) { malformed++; if (samples.Count < MaxSamples) samples.Add($"line {number}: {reason}"); }

        for (var n = 0; n < rows.Length; n++)
        {
            var row = rows[n];
            if (row.Length == 0 || row == "--") continue; // blank lines and grep's context-group separators carry no result
            read++;
            if (row.Length > MaxLineChars) { Bad(n + 1, "the line is longer than 65536 characters"); continue; }
            if (matches.Count >= RegexGrepResults.MaxSavedMatches) { truncated = true; break; }
            if (chosen == Json) { ReadJsonLine(row, n + 1, matches, Bad, baseFolder); continue; }
            Match m;
            try { m = chosen switch { Vimgrep => VimgrepLine.Match(row), GrepNH => HeaderLine.Match(row), _ => PlainLine.Match(row) }; }
            catch (RegexMatchTimeoutException) { Bad(n + 1, "the line took too long to read"); continue; }
            if (!m.Success) { Bad(n + 1, "it does not look like " + chosen + " output"); continue; }
            if (!int.TryParse(m.Groups["line"].Value, out var lineNo) || lineNo < 1) { Bad(n + 1, "the line number is not valid"); continue; }
            var column = 1;
            if (chosen == Vimgrep && (!int.TryParse(m.Groups["col"].Value, out column) || column < 1)) { Bad(n + 1, "the column is not valid"); continue; }
            var file = chosen == GrepN ? (string.IsNullOrWhiteSpace(defaultFile) ? NoFile : defaultFile!) : m.Groups["file"].Value;
            var resolved = ResolvePath(file, baseFolder);
            if (resolved == null) { Bad(n + 1, "the file name is not a valid path"); continue; }
            matches.Add(new RegexGrepMatch(resolved, lineNo, column, 0, "", m.Groups["text"].Value));
        }
        if (matches.Count == 0 && malformed > 0) return new(chosen, [], malformed, samples, read, truncated, $"No line could be read as {chosen} output ({malformed} line{(malformed == 1 ? "" : "s")} did not fit). {string.Join("; ", samples.Take(2))}");
        if (matches.Count == 0) return new(chosen, [], 0, samples, read, false, "The tool output contains no results.");
        return new(chosen, matches, malformed, samples, read, truncated, null);
    }

    private static RegexGrepImport Fail(string format, string problem) => new(format, [], 0, [], 0, false, problem);

    /// <summary>The format that fits the most lines. On a tie ripgrep's vimgrep form wins over the header form, which wins over the plain form, because the forms nest.</summary>
    private static string Detect(string[] rows)
    {
        var first = rows.FirstOrDefault(r => r.Trim().Length > 0);
        if (first != null && first.TrimStart().StartsWith('{')) return Json;
        var sample = rows.Where(r => r.Length > 0 && r != "--" && r.Length <= MaxLineChars).Take(2000).ToArray();
        int Fit(Regex r) => sample.Count(l => { try { return r.IsMatch(l); } catch (RegexMatchTimeoutException) { return false; } });
        var candidates = new (string Name, int Fit)[] { (Vimgrep, Fit(VimgrepLine)), (GrepNH, Fit(HeaderLine)), (GrepN, Fit(PlainLine)) };
        return candidates.OrderByDescending(c => c.Fit).First().Name;
    }

    private static string? ResolvePath(string file, string? baseFolder)
    {
        if (file.Length == 0 || file.Length > 4096 || file.IndexOfAny(['\0', '<', '>', '"', '|', '?', '*']) >= 0) return null;
        if (file == NoFile) return file;
        try
        {
            if (Path.IsPathRooted(file) || string.IsNullOrWhiteSpace(baseFolder)) return file;
            return Path.GetFullPath(Path.Combine(baseFolder.Trim().Trim('"'), file));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    private static void ReadJsonLine(string row, int number, List<RegexGrepMatch> matches, Action<int, string> bad, string? baseFolder)
    {
        try
        {
            using var doc = JsonDocument.Parse(row, new JsonDocumentOptions { MaxDepth = 16 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) { bad(number, "it is not a ripgrep JSON message"); return; }
            if (type.GetString() != "match") return; // begin, end, context and summary messages are not results
            var data = root.GetProperty("data");
            var file = Field(data.GetProperty("path"));
            var lineText = Field(data.GetProperty("lines"));
            if (file == null || lineText == null) { bad(number, "the path or the line is missing"); return; }
            var lineNo = data.GetProperty("line_number").GetInt32();
            if (lineNo < 1) { bad(number, "the line number is not valid"); return; }
            var resolved = ResolvePath(file, baseFolder);
            if (resolved == null) { bad(number, "the file name is not a valid path"); return; }
            var shown = lineText.TrimEnd('\n', '\r');
            var bytes = Encoding.UTF8.GetBytes(lineText);
            var any = false;
            foreach (var sub in data.GetProperty("submatches").EnumerateArray())
            {
                var start = sub.GetProperty("start").GetInt32(); var end = sub.GetProperty("end").GetInt32();
                if (start < 0 || end < start || end > bytes.Length) { bad(number, "a match lies outside its line"); any = true; continue; }
                var column = Encoding.UTF8.GetCharCount(bytes, 0, start) + 1;
                var length = Encoding.UTF8.GetCharCount(bytes, start, end - start);
                matches.Add(new RegexGrepMatch(resolved, lineNo, column, length, Field(sub.GetProperty("match")) ?? "", shown));
                any = true;
            }
            if (!any) matches.Add(new RegexGrepMatch(resolved, lineNo, 1, 0, "", shown));
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {
            bad(number, "it is not valid ripgrep JSON");
        }

        // ripgrep writes {"text": ...} for valid UTF-8 and {"bytes": base64} otherwise; the second form is decoded with replacement characters.
        static string? Field(JsonElement e)
        {
            if (e.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String) return t.GetString();
            if (e.TryGetProperty("bytes", out var b) && b.ValueKind == JsonValueKind.String)
            {
                try { return Encoding.UTF8.GetString(Convert.FromBase64String(b.GetString() ?? "")); } catch (FormatException) { return null; }
            }
            return null;
        }
    }

    /// <summary>Wraps an import as a saved result list so the existing result grid, Open selected result and Save results handle it unchanged.</summary>
    public static RegexGrepSavedList ToSavedList(RegexGrepImport import, string? folder, DateTimeOffset now) =>
        new("(imported " + import.Format + ")", new RegexBuilderFlags(), folder ?? "", "", now, import.Matches);
}
