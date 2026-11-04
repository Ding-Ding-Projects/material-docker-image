using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

public enum RegexScope { Whole, Line, Page }

/// <summary>The matches of one scope (a line or a page) of the subject. Offsets are into the whole subject.</summary>
public sealed record RegexScopeSegment(int Number, int Start, string Preview, IReadOnlyList<(int Index, string Value)> Matches);

/// <summary>The outcome of testing a pattern scope by scope.</summary>
public sealed record RegexScopeResult(bool Valid, string? Error, RegexScope Scope, IReadOnlyList<RegexScopeSegment> Segments, int ScopeCount, int TotalMatches, bool Truncated, bool TimedOut)
{
    public int ScopesWithMatches => Segments.Count(s => s.Matches.Count > 0);
}

/// <summary>A hex dump of the subject in one encoding, with the byte count and whether it was cut.</summary>
public sealed record RegexHexView(string Text, int ByteCount, bool Truncated, string Encoding);

/// <summary>The kinds of line ending found in a subject.</summary>
public sealed record RegexLineEndings(int Crlf, int Lf, int Cr)
{
    public bool Mixed => (Crlf > 0 ? 1 : 0) + (Lf > 0 ? 1 : 0) + (Cr > 0 ? 1 : 0) > 1;
    public string Summary => Crlf + Lf + Cr == 0 ? "No line breaks." : $"{Crlf} CRLF, {Lf} LF, {Cr} CR" + (Mixed ? " (mixed line endings)." : ".");
}

/// <summary>The result of loading a file as the subject.</summary>
public sealed record RegexSubjectFile(bool Success, string Text, string Encoding, bool Truncated, long FileBytes, string? Error);

/// <summary>
/// The subject the pattern is tested against: scope-by-scope testing (whole text, every line, every page), a hex view in a chosen encoding, line-ending
/// detection and conversion, and bounded loading and saving of a file. Pure logic; every operation is bounded.
/// </summary>
public static class RegexSubject
{
    public const int MaxScopes = 5000;
    public const int MaxScopeMatches = 2000;
    public const int MaxHexBytes = 4096;
    public const int MaxPreview = 60;
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);

    public static readonly IReadOnlyList<string> Encodings = ["UTF-8", "UTF-16 LE", "UTF-16 BE", "Latin-1", "ASCII"];
    public static readonly IReadOnlyList<string> LineEndingNames = ["LF", "CRLF", "CR"];

    public static Encoding EncodingByName(string? name) => name switch
    {
        "UTF-16 LE" => new UnicodeEncoding(false, false),
        "UTF-16 BE" => new UnicodeEncoding(true, false),
        "Latin-1" => Encoding.Latin1,
        "ASCII" => Encoding.ASCII,
        _ => new UTF8Encoding(false),
    };

    // ----- scopes -----

    /// <summary>Splits the subject into scopes: lines (the line break is not part of the line) or pages (separated by a form feed), with their start offsets.</summary>
    public static List<(int Start, string Text)> Split(string text, RegexScope scope)
    {
        var parts = new List<(int, string)>();
        if (scope == RegexScope.Whole) { parts.Add((0, text)); return parts; }
        var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            var atEnd = i == text.Length;
            if (!atEnd && (scope == RegexScope.Page ? text[i] != '\f' : text[i] is not ('\n' or '\r'))) continue;
            if (!atEnd || start < text.Length || parts.Count == 0) parts.Add((start, text[start..i]));
            if (!atEnd && scope == RegexScope.Line && text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            start = i + 1;
        }
        return parts;
    }

    /// <summary>
    /// Tests the pattern on each scope separately, so that ^ and $ anchor at the scope and no match crosses a scope boundary. The whole-text scope is the
    /// ordinary test. Bounded by the sample size, <see cref="MaxScopes"/> scopes, <see cref="MaxScopeMatches"/> matches and a two second budget.
    /// </summary>
    public static RegexScopeResult RunScopes(string pattern, RegexBuilderFlags flags, string? sample, RegexScope scope, CancellationToken cancellation = default)
    {
        var compiled = RegexBuilderEngine.Compile(pattern, flags);
        if (!compiled.Valid) return new(false, compiled.Error, scope, [], 0, 0, false, false);
        var text = RegexBuilderEngine.BoundSample(sample ?? "", out _);
        var parts = Split(text, scope);
        var segments = new List<RegexScopeSegment>();
        var total = 0;
        var truncated = parts.Count > MaxScopes;
        var timedOut = false;
        var clock = Stopwatch.StartNew();
        var number = 0;
        foreach (var (start, part) in parts.Take(MaxScopes))
        {
            number++;
            if (cancellation.IsCancellationRequested || clock.Elapsed > Budget) { truncated = true; break; }
            var found = new List<(int, string)>();
            try
            {
                foreach (Match m in compiled.Regex!.Matches(part))
                {
                    if (total >= MaxScopeMatches) { truncated = true; break; }
                    found.Add((start + m.Index, m.Value.Length > 200 ? m.Value[..200] + "…" : m.Value));
                    total++;
                }
            }
            catch (RegexMatchTimeoutException) { timedOut = true; }
            segments.Add(new(number, start, part.Length > MaxPreview ? part[..MaxPreview] + "…" : part, found));
            if (total >= MaxScopeMatches) break;
        }
        return new(true, null, scope, segments, parts.Count, total, truncated, timedOut);
    }

    /// <summary>A readable report: one line per scope that has matches, then the totals.</summary>
    /// <param name="t">Optional translation of each fixed sentence; English is produced when it is null.</param>
    public static string Report(RegexScopeResult result, Func<string, string>? t = null)
    {
        t ??= s => s;
        if (!result.Valid) return t(result.Error ?? "");
        var label = result.Scope switch { RegexScope.Line => "line", RegexScope.Page => "page", _ => "text" };
        var sb = new StringBuilder();
        foreach (var s in result.Segments.Where(s => s.Matches.Count > 0))
            sb.AppendLine(t($"{(result.Scope == RegexScope.Whole ? "Text" : char.ToUpperInvariant(label[0]) + label[1..] + " " + s.Number)}: {s.Matches.Count} match{(s.Matches.Count == 1 ? "" : "es")}:") + " " + string.Join(", ", s.Matches.Take(8).Select(m => $"{Visible(m.Value)} {t($"at {m.Index}")}")) + (s.Matches.Count > 8 ? ", …" : ""));
        sb.Append(t($"{result.TotalMatches} match{(result.TotalMatches == 1 ? "" : "es")} in {result.ScopesWithMatches} of {result.ScopeCount} {label}{(result.ScopeCount == 1 ? "" : "s")}."));
        if (result.TimedOut) sb.Append(" " + t($"Stopped on a {label} that exceeded the {RegexBuilderEngine.MatchTimeout.TotalMilliseconds:0} ms limit."));
        if (result.Truncated) sb.Append(" " + t("The result was cut at the bounds."));
        return sb.ToString();
    }

    /// <summary>The line under the hex view: byte count, encoding, what was shown, and the line-ending counts.</summary>
    public static string HexStatus(RegexHexView view, string? sample, Func<string, string>? t = null)
    {
        t ??= s => s;
        return t($"{view.ByteCount} bytes in {view.Encoding}") + (view.Truncated ? ", " + t($"the first {MaxHexBytes} shown") + ". " : ". ") + t(CountLineEndings(sample).Summary);
    }

    private static string Visible(string value) => value.Length == 0 ? "(empty)" : value.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");

    // ----- hex view and encodings -----

    /// <summary>The subject as bytes in the chosen encoding, sixteen bytes a row with offsets and a text column. At most <see cref="MaxHexBytes"/> bytes are shown.</summary>
    public static RegexHexView Hex(string? text, string? encodingName)
    {
        var encoding = EncodingByName(encodingName);
        var all = encoding.GetBytes(text ?? "");
        var shown = Math.Min(all.Length, MaxHexBytes);
        var sb = new StringBuilder();
        for (var row = 0; row < shown; row += 16)
        {
            sb.Append(row.ToString("X8")).Append("  ");
            for (var i = 0; i < 16; i++)
            {
                if (row + i < shown) sb.Append(all[row + i].ToString("X2")).Append(' '); else sb.Append("   ");
                if (i == 7) sb.Append(' ');
            }
            sb.Append(' ');
            for (var i = 0; i < 16 && row + i < shown; i++) { var b = all[row + i]; sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '.'); }
            sb.AppendLine();
        }
        return new(sb.ToString().TrimEnd(), all.Length, all.Length > shown, encodingName ?? "UTF-8");
    }

    public static RegexLineEndings CountLineEndings(string? text)
    {
        int crlf = 0, lf = 0, cr = 0;
        var t = text ?? "";
        for (var i = 0; i < t.Length; i++)
        {
            if (t[i] == '\r') { if (i + 1 < t.Length && t[i + 1] == '\n') { crlf++; i++; } else cr++; }
            else if (t[i] == '\n') lf++;
        }
        return new(crlf, lf, cr);
    }

    /// <summary>Rewrites every line break as the chosen ending ("LF", "CRLF" or "CR"). A subject without line breaks is returned unchanged.</summary>
    public static string NormalizeLineEndings(string? text, string ending)
    {
        var eol = ending switch { "CRLF" => "\r\n", "CR" => "\r", "LF" => "\n", _ => throw new ArgumentException("Line ending must be LF, CRLF or CR.") };
        return Regex.Replace(text ?? "", @"\r\n|\r|\n", eol, RegexOptions.NonBacktracking);
    }

    // ----- files -----

    /// <summary>The encoding named by a byte order mark at the start of the data, and the length of that mark; null when there is none.</summary>
    public static (string Name, int BomLength)? DetectBom(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) return ("UTF-8", 3);
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE) return ("UTF-16 LE", 2);
        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF) return ("UTF-16 BE", 2);
        return null;
    }

    /// <summary>
    /// Reads up to the sample limit from a file. With no encoding named, a byte order mark decides; otherwise valid UTF-8 is read as UTF-8 and anything else as Latin-1.
    /// Without a byte order mark a file with zero bytes is refused as binary unless an encoding was named.
    /// </summary>
    public static RegexSubjectFile LoadFile(string? path, string? encodingName = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return new(false, "", "", false, 0, "Choose a file.");
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return new(false, "", "", false, 0, "The file does not exist.");
            var limit = RegexBuilderEngine.MaxSampleBytes;
            var buffer = new byte[limit + 4];
            int read;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                read = stream.Read(buffer, 0, buffer.Length);
            var truncated = info.Length > limit;
            var data = buffer.AsSpan(0, Math.Min(read, limit));
            var bom = DetectBom(data);
            string name;
            if (!string.IsNullOrEmpty(encodingName) && encodingName != "Auto") name = encodingName;
            else if (bom != null) name = bom.Value.Name;
            else if (data.IndexOf((byte)0) >= 0) return new(false, "", "", truncated, info.Length, "The file contains zero bytes and no byte order mark, so it looks binary. Pick an encoding to read it anyway.");
            else name = IsValidUtf8(data, truncated) ? "UTF-8" : "Latin-1";
            var skip = bom != null && bom.Value.Name == name ? bom.Value.BomLength : 0;
            var decoder = EncodingByName(name).GetDecoder();
            var chars = new char[EncodingByName(name).GetMaxCharCount(data.Length)];
            var count = decoder.GetChars(data[skip..], chars, flush: !truncated);
            return new(true, new string(chars, 0, count), name, truncated, info.Length, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new(false, "", "", false, 0, "The file could not be read: " + e.Message);
        }
    }

    private static bool IsValidUtf8(ReadOnlySpan<byte> data, bool mayEndMidCharacter)
    {
        for (var cut = 0; cut <= (mayEndMidCharacter ? 3 : 0) && cut <= data.Length; cut++)
            if (System.Text.Unicode.Utf8.IsValid(data[..(data.Length - cut)])) return true;
        return false;
    }

    /// <summary>Writes the subject to a file in the chosen encoding and line ending, through a temporary file so a failed write leaves the old file alone. UTF-16 gets a byte order mark so that loading reads it back.</summary>
    public static string? SaveFile(string? path, string? text, string? encodingName, string lineEnding)
    {
        if (string.IsNullOrWhiteSpace(path)) return "Choose a file name.";
        try
        {
            var encoding = EncodingByName(encodingName);
            var content = NormalizeLineEndings(text, lineEnding);
            if (encoding.GetByteCount(content) > 4 * RegexBuilderEngine.MaxSampleBytes) return "The subject is too large to save.";
            var bytes = encoding.GetBytes(content);
            var preamble = encodingName is "UTF-16 LE" ? new byte[] { 0xFF, 0xFE } : encodingName is "UTF-16 BE" ? new byte[] { 0xFE, 0xFF } : [];
            var temp = path + ".tmp";
            File.WriteAllBytes(temp, [.. preamble, .. bytes]);
            File.Move(temp, path, overwrite: true);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return "The file could not be saved: " + e.Message; }
    }
}
