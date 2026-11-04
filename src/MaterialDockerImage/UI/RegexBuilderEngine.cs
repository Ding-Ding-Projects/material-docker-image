using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>
/// Regular expression options offered by the builder. Plain-text matching only uses <see cref="IgnoreCase"/>.
/// Pure logic: no WPF references, so the test project links this file directly.
/// </summary>
public sealed record RegexBuilderFlags
{
    public bool IgnoreCase { get; init; } = true;
    public bool Multiline { get; init; }
    public bool Singleline { get; init; }
    public bool IgnorePatternWhitespace { get; init; }
    public bool ExplicitCapture { get; init; }
    public bool CultureInvariant { get; init; }
    /// <summary>Scan from the end of the text towards the start. Needs the backtracking engine.</summary>
    public bool RightToLeft { get; init; }
    /// <summary>Require the linear-time engine: patterns that need backtracking are reported instead of falling back.</summary>
    public bool NonBacktracking { get; init; }

    /// <summary>Every flag with its .NET option name and a plain description, in display order.</summary>
    public static readonly IReadOnlyList<(string Name, string Inline, string Description)> All =
    [
        ("IgnoreCase", "i", "Letters match regardless of case."),
        ("Multiline", "m", "^ and $ match at the start and end of every line, not only of the whole text."),
        ("Singleline", "s", "The dot (.) also matches a newline character."),
        ("IgnorePatternWhitespace", "x", "Unescaped white space in the pattern is ignored and # starts a comment."),
        ("ExplicitCapture", "n", "Only named groups (?<name>...) capture; plain parentheses only group."),
        ("CultureInvariant", "", "Case-insensitive matching uses the invariant culture instead of the current one."),
        ("RightToLeft", "", "The search starts at the end of the text and moves left. Needs the backtracking engine."),
        ("NonBacktracking", "", "Only use the linear-time engine. Patterns with lookarounds, backreferences, atomic groups or conditionals are then reported as unsupported instead of falling back."),
    ];

    public bool Get(string name) => name switch
    {
        "IgnoreCase" => IgnoreCase,
        "Multiline" => Multiline,
        "Singleline" => Singleline,
        "IgnorePatternWhitespace" => IgnorePatternWhitespace,
        "ExplicitCapture" => ExplicitCapture,
        "CultureInvariant" => CultureInvariant,
        "RightToLeft" => RightToLeft,
        "NonBacktracking" => NonBacktracking,
        _ => throw new ArgumentException("Unknown flag " + name, nameof(name)),
    };

    public RegexBuilderFlags With(string name, bool value) => name switch
    {
        "IgnoreCase" => this with { IgnoreCase = value },
        "Multiline" => this with { Multiline = value },
        "Singleline" => this with { Singleline = value },
        "IgnorePatternWhitespace" => this with { IgnorePatternWhitespace = value },
        "ExplicitCapture" => this with { ExplicitCapture = value },
        "CultureInvariant" => this with { CultureInvariant = value },
        "RightToLeft" => this with { RightToLeft = value },
        "NonBacktracking" => this with { NonBacktracking = value },
        _ => throw new ArgumentException("Unknown flag " + name, nameof(name)),
    };

    public RegexOptions ToOptions()
    {
        var o = RegexOptions.None;
        if (IgnoreCase) o |= RegexOptions.IgnoreCase;
        if (Multiline) o |= RegexOptions.Multiline;
        if (Singleline) o |= RegexOptions.Singleline;
        if (IgnorePatternWhitespace) o |= RegexOptions.IgnorePatternWhitespace;
        if (ExplicitCapture) o |= RegexOptions.ExplicitCapture;
        if (CultureInvariant) o |= RegexOptions.CultureInvariant;
        if (RightToLeft) o |= RegexOptions.RightToLeft;
        if (NonBacktracking) o |= RegexOptions.NonBacktracking;
        return o;
    }

    /// <summary>Comma-separated option names, or "None".</summary>
    public string Describe() => All.Where(f => Get(f.Name)).Select(f => f.Name).DefaultIfEmpty("None").Aggregate((a, b) => a + ", " + b);
}

/// <summary>A compiled pattern, or the reason it could not be compiled. Never throws to callers.</summary>
public sealed record RegexCompileResult(Regex? Regex, string Engine, string? Error, int ErrorIndex, string? FallbackReason)
{
    public bool Valid => Regex != null;
}

/// <summary>One capture group of one match.</summary>
public sealed record RegexGroupResult(int Number, string Name, bool Success, int Index, int Length, string Value);

/// <summary>One match: position, bounded value and its groups (group 0 excluded).</summary>
public sealed record RegexMatchResult(int Index, int Length, string Value, IReadOnlyList<RegexGroupResult> Groups)
{
    public bool ZeroWidth => Length == 0;
}

/// <summary>Bounded result of testing a pattern against sample text.</summary>
public sealed record RegexEvaluation
{
    public bool Valid { get; init; }
    public string? Error { get; init; }
    public int ErrorIndex { get; init; } = -1;
    public string Engine { get; init; } = "";
    public string? FallbackReason { get; init; }
    public string Sample { get; init; } = "";
    public bool SampleTruncated { get; init; }
    public IReadOnlyList<RegexMatchResult> Matches { get; init; } = [];
    public bool MatchesTruncated { get; init; }
    public bool TimedOut { get; init; }
    public bool Cancelled { get; init; }
    public double ElapsedMilliseconds { get; init; }

    /// <summary>One-line plain English summary for the status line.</summary>
    public string Summary()
    {
        if (!Valid) return Error ?? "Invalid pattern.";
        if (Cancelled) return "Evaluation cancelled.";
        var count = Matches.Count;
        var zero = Matches.Count(m => m.ZeroWidth);
        var text = count == 0 ? "No match in the sample" : $"{count}{(MatchesTruncated ? "+" : "")} match{(count == 1 ? "" : "es")}" + (zero > 0 ? $" ({zero} zero-width)" : "") + (MatchesTruncated ? $"; only the first {RegexBuilderEngine.MaxHighlightedMatches} are highlighted" : "");
        if (TimedOut) text += $". Stopped: the pattern exceeded the {RegexBuilderEngine.MatchTimeout.TotalMilliseconds:0} ms limit, so later matches are not shown";
        if (SampleTruncated) text += $". The sample was cut to {RegexBuilderEngine.MaxSampleBytes / 1024} KiB";
        return text + ".";
    }
}

/// <summary>Bounded replace preview.</summary>
public sealed record RegexReplacePreview(bool Valid, string Output, int Replacements, bool OutputTruncated, bool TimedOut, bool Cancelled, string? Error, IReadOnlyList<string> Warnings);

/// <summary>
/// Engine selection, validation and bounded evaluation for every search field. Uses the .NET non-backtracking
/// engine when the pattern allows it (linear time in the input); otherwise the backtracking engine with a
/// per-match timeout.
/// </summary>
public static class RegexBuilderEngine
{
    public const string NonBacktrackingEngine = "NonBacktracking";
    public const string BacktrackingEngine = "Backtracking";
    public const int MaxSampleBytes = 64 * 1024;
    public const int MaxHighlightedMatches = 2000;
    public const int MaxPatternLength = 4096;
    public const int MaxReplaceOutputChars = 256 * 1024;
    public const int MaxGroupValueChars = 512;
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);
    /// <summary>Wall-clock budget for a whole sample evaluation, on top of the per-match timeout.</summary>
    public static readonly TimeSpan EvaluationBudget = TimeSpan.FromSeconds(2);

    private static readonly object cacheLock = new();
    private static readonly Dictionary<(string, RegexOptions), RegexCompileResult> cache = new();
    private static readonly Queue<(string, RegexOptions)> cacheOrder = new();

    public static string DescribeEngine(string engine) => engine == NonBacktrackingEngine
        ? "Non-backtracking engine: matching time grows linearly with the text, so no pattern can hang the app."
        : $"Backtracking engine (the pattern uses a construct the non-backtracking engine does not support), limited to {MatchTimeout.TotalMilliseconds:0} ms per match.";

    /// <summary>Compiles with NonBacktracking when supported, otherwise backtracking with <see cref="MatchTimeout"/>.</summary>
    public static RegexCompileResult Compile(string pattern, RegexBuilderFlags flags)
    {
        var options = flags.ToOptions();
        lock (cacheLock) if (cache.TryGetValue((pattern, options), out var hit)) return hit;
        var result = CompileUncached(pattern, options);
        lock (cacheLock)
        {
            if (!cache.ContainsKey((pattern, options)))
            {
                cache[(pattern, options)] = result; cacheOrder.Enqueue((pattern, options));
                while (cacheOrder.Count > 64) cache.Remove(cacheOrder.Dequeue());
            }
        }
        return result;
    }

    private static RegexCompileResult CompileUncached(string pattern, RegexOptions options)
    {
        if (pattern.Length > MaxPatternLength) return new(null, "", $"The pattern is longer than {MaxPatternLength} characters.", MaxPatternLength, null);
        string? fallback = null;
        var required = (options & RegexOptions.NonBacktracking) != 0;
        // RightToLeft cannot run on the linear-time engine, so it goes straight to the backtracking one.
        if ((options & RegexOptions.RightToLeft) != 0)
        {
            if (required) return new(null, "", "NonBacktracking and RightToLeft cannot be combined: the linear-time engine only scans left to right.", -1, null);
            fallback = "RightToLeft scans from the end of the text, which the non-backtracking engine does not support.";
        }
        else
        {
            try { return new(new Regex(pattern, options | RegexOptions.NonBacktracking, MatchTimeout), NonBacktrackingEngine, null, -1, null); }
            catch (RegexParseException e) { return Invalid(e); }
            catch (NotSupportedException e) { fallback = e.Message; }
            catch (ArgumentException e) { fallback = e.Message; }
        }
        if (required) return new(null, "", "The non-backtracking engine does not support this pattern: " + fallback, -1, null);
        options &= ~RegexOptions.NonBacktracking;
        try { return new(new Regex(pattern, options, MatchTimeout), BacktrackingEngine, null, -1, fallback); }
        catch (RegexParseException e) { return Invalid(e); }
        catch (ArgumentException e) { return new(null, "", "Invalid pattern: " + e.Message, -1, null); }
    }

    private static RegexCompileResult Invalid(RegexParseException e)
    {
        var message = e.Message;
        // The framework message repeats the whole pattern; keep only the reason after the offset text.
        var marker = message.IndexOf(" - ", StringComparison.Ordinal);
        if (marker >= 0) message = message[(marker + 3)..];
        return new(null, "", $"Invalid pattern at position {e.Offset}: {message.Trim()}", e.Offset, null);
    }

    /// <summary>Validation only: null error means the pattern compiles.</summary>
    public static (bool Valid, string? Error, int ErrorIndex) Validate(string pattern, RegexBuilderFlags flags)
    {
        var c = Compile(pattern, flags);
        return (c.Valid, c.Error, c.ErrorIndex);
    }

    /// <summary>Cuts text to at most <see cref="MaxSampleBytes"/> UTF-8 bytes without splitting a surrogate pair.</summary>
    public static string BoundSample(string text, out bool truncated)
    {
        truncated = false;
        if (text.Length * 3 <= MaxSampleBytes || Encoding.UTF8.GetByteCount(text) <= MaxSampleBytes) return text;
        var bytes = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var width = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 4 : text[i] < 0x80 ? 1 : text[i] < 0x800 ? 2 : 3;
            if (bytes + width > MaxSampleBytes) { truncated = true; return text[..i]; }
            bytes += width;
            if (width == 4) i++;
        }
        return text;
    }

    /// <summary>Tests the pattern against a bounded sample. Safe to run on a worker thread; never throws.</summary>
    public static RegexEvaluation Evaluate(string pattern, RegexBuilderFlags flags, string sample, CancellationToken cancellation = default, int maxMatches = MaxHighlightedMatches)
    {
        var compiled = Compile(pattern, flags);
        var text = BoundSample(sample ?? "", out var sampleTruncated);
        if (!compiled.Valid) return new() { Valid = false, Error = compiled.Error, ErrorIndex = compiled.ErrorIndex, Sample = text, SampleTruncated = sampleTruncated };
        var regex = compiled.Regex!;
        var numbers = regex.GetGroupNumbers().Where(n => n != 0).ToArray();
        var matches = new List<RegexMatchResult>();
        bool timedOut = false, cancelled = false, truncated = false;
        var clock = Stopwatch.StartNew();
        try
        {
            if (cancellation.IsCancellationRequested) cancelled = true;
            else
            {
                var m = regex.Match(text);
                while (m.Success)
                {
                    if (cancellation.IsCancellationRequested) { cancelled = true; break; }
                    if (matches.Count >= maxMatches) { truncated = true; break; }
                    if (clock.Elapsed > EvaluationBudget) { timedOut = true; break; }
                    var groups = new List<RegexGroupResult>(numbers.Length);
                    foreach (var n in numbers)
                    {
                        var g = m.Groups[n];
                        groups.Add(new(n, regex.GroupNameFromNumber(n), g.Success, g.Success ? g.Index : -1, g.Success ? g.Length : 0, g.Success ? Cap(g.Value) : ""));
                    }
                    matches.Add(new(m.Index, m.Length, Cap(m.Value), groups));
                    m = m.NextMatch();
                }
            }
        }
        catch (RegexMatchTimeoutException) { timedOut = true; }
        return new()
        {
            Valid = true, Engine = compiled.Engine, FallbackReason = compiled.FallbackReason, Sample = text, SampleTruncated = sampleTruncated,
            Matches = matches, MatchesTruncated = truncated, TimedOut = timedOut, Cancelled = cancelled, ElapsedMilliseconds = clock.Elapsed.TotalMilliseconds,
        };
    }

    private static string Cap(string value) => value.Length > MaxGroupValueChars ? value[..MaxGroupValueChars] + "…" : value;

    private static readonly Regex replacementReference = new(@"\$(?:\{(?<name>[^}]*)\}|(?<number>\d+)|(?<other>[$&`'+_]))", RegexOptions.NonBacktracking);

    /// <summary>Replace preview with $1, ${name}, $$, $&amp; substitutions; bounded matches and output.</summary>
    public static RegexReplacePreview Replace(string pattern, RegexBuilderFlags flags, string sample, string replacement, CancellationToken cancellation = default)
    {
        var compiled = Compile(pattern, flags);
        if (!compiled.Valid) return new(false, "", 0, false, false, false, compiled.Error, []);
        var regex = compiled.Regex!;
        var text = BoundSample(sample ?? "", out _);
        var warnings = new List<string>();
        var names = new HashSet<string>(regex.GetGroupNames(), StringComparer.Ordinal);
        var numbers = new HashSet<int>(regex.GetGroupNumbers());
        foreach (Match r in replacementReference.Matches(replacement ?? ""))
        {
            if (r.Groups["name"].Success)
            {
                var name = r.Groups["name"].Value;
                if (!(int.TryParse(name, out var n) ? numbers.Contains(n) : names.Contains(name))) warnings.Add($"${{{name}}} does not name a group in the pattern, so it is inserted literally.");
            }
            else if (r.Groups["number"].Success && int.TryParse(r.Groups["number"].Value, out var number) && !numbers.Contains(number))
                warnings.Add($"${number} does not refer to a group in the pattern, so it is inserted literally.");
        }
        var count = 0;
        try
        {
            var output = regex.Replace(text, m =>
            {
                cancellation.ThrowIfCancellationRequested();
                count++;
                return m.Result(replacement ?? "");
            }, MaxHighlightedMatches);
            var cut = output.Length > MaxReplaceOutputChars;
            return new(true, cut ? output[..MaxReplaceOutputChars] : output, count, cut, false, false, null, warnings);
        }
        catch (RegexMatchTimeoutException) { return new(true, "", count, false, true, false, $"Stopped: the pattern exceeded the {MatchTimeout.TotalMilliseconds:0} ms limit.", warnings); }
        catch (OperationCanceledException) { return new(true, "", count, false, false, true, "Preview cancelled.", warnings); }
    }

    /// <summary>The pattern as a C# verbatim string literal.</summary>
    public static string ToCSharpVerbatim(string pattern) => "@\"" + pattern.Replace("\"", "\"\"") + "\"";

    /// <summary>Plain text as a pattern that matches it literally.</summary>
    public static string EscapeLiteral(string text) => Regex.Escape(text);

    /// <summary>Exchange format: pattern, flags, engine, dialect and the bounded sample text.</summary>
    public static string ExportJson(string pattern, RegexBuilderFlags flags, string sample, string? replacement = null)
    {
        var compiled = Compile(pattern, flags);
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("format", "material-docker-image.regex");
            w.WriteNumber("version", 1);
            w.WriteString("dialect", ".NET regular expressions");
            w.WriteString("pattern", pattern);
            w.WriteStartObject("flags");
            foreach (var f in RegexBuilderFlags.All) w.WriteBoolean(char.ToLowerInvariant(f.Name[0]) + f.Name[1..], flags.Get(f.Name));
            w.WriteEndObject();
            w.WriteString("options", flags.Describe());
            w.WriteBoolean("valid", compiled.Valid);
            if (compiled.Valid) w.WriteString("engine", compiled.Engine); else w.WriteString("error", compiled.Error);
            w.WriteString("sample", BoundSample(sample ?? "", out _));
            if (replacement != null) w.WriteString("replacement", replacement);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}

/// <summary>
/// The predicate behind every search field: plain text by default, regex when opted in. Built once per query
/// change and reused for every candidate.
/// </summary>
public sealed class RegexSearchQuery
{
    private readonly RegexCompileResult? compiled;
    public string Text { get; }
    public bool RegexMode { get; }
    public RegexBuilderFlags Flags { get; }
    public bool IsEmpty => Text.Length == 0;
    public bool IsValid => !RegexMode || IsEmpty || compiled!.Valid;
    public string? Error => RegexMode && !IsEmpty ? compiled!.Error : null;
    public int ErrorIndex => RegexMode && !IsEmpty ? compiled!.ErrorIndex : -1;
    public string Engine => RegexMode && !IsEmpty && compiled!.Valid ? compiled.Engine : "Plain text";
    public bool TimedOut { get; private set; }

    public RegexSearchQuery(string text, bool regexMode, RegexBuilderFlags flags)
    {
        Text = text ?? ""; RegexMode = regexMode; Flags = flags;
        if (RegexMode && Text.Length > 0) compiled = RegexBuilderEngine.Compile(Text, flags);
    }

    /// <summary>True when the candidate matches. An empty query matches everything; an invalid one matches nothing.</summary>
    public bool Matches(string? candidate)
    {
        if (IsEmpty) return true;
        candidate ??= "";
        if (!RegexMode) return candidate.Contains(Text, Flags.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        if (!compiled!.Valid) return false;
        // After one timeout the query stops matching, so a long list costs at most one timeout per query change.
        if (TimedOut) return false;
        try { return compiled.Regex!.IsMatch(candidate); }
        catch (RegexMatchTimeoutException) { TimedOut = true; return false; }
    }
}

/// <summary>Per-field, in-memory builder state. Nothing here is persisted.</summary>
public sealed class RegexFieldState
{
    public RegexBuilderFlags Flags { get; set; } = new();
    public string Sample { get; set; } = "";
    public string Replacement { get; set; } = "";
    /// <summary>Expected-outcome lines ("+ must match", "- must not match") for this field's test suite. In memory only.</summary>
    public string TestCases { get; set; } = "";
    /// <summary>Plain-language building blocks typed in this field's builder. In memory only.</summary>
    public string Blocks { get; set; } = "";
    /// <summary>Folder and file patterns of this field's folder search. In memory only.</summary>
    public string GrepFolder { get; set; } = "";
    public string GrepGlobs { get; set; } = "*";
    public RegexRecentPatterns Recent { get; } = new();
}

/// <summary>Session-only list of recently used patterns for one field.</summary>
public sealed class RegexRecentPatterns
{
    public const int Capacity = 12;
    private readonly List<string> items = new();
    public IReadOnlyList<string> Items => items;

    /// <summary>Adds a valid, non-empty pattern to the front; duplicates move to the front.</summary>
    public bool Add(string pattern, RegexBuilderFlags flags)
    {
        if (string.IsNullOrWhiteSpace(pattern) || !RegexBuilderEngine.Validate(pattern, flags).Valid) return false;
        items.Remove(pattern);
        items.Insert(0, pattern);
        if (items.Count > Capacity) items.RemoveAt(items.Count - 1);
        return true;
    }
}
