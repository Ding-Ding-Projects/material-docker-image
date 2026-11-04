using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>Bounded split preview. With capture groups in the pattern, .NET also returns the captured text between the pieces.</summary>
public sealed record RegexSplitPreview(bool Valid, IReadOnlyList<string> Pieces, bool Truncated, bool TimedOut, bool Cancelled, string? Error, IReadOnlyList<string> Notes);

/// <summary>One expected outcome of a test-case suite: a sample line that must match or must not match.</summary>
public sealed record RegexTestCase(string Input, bool ExpectMatch);

/// <summary>The outcome of one test case.</summary>
public sealed record RegexTestCaseResult(RegexTestCase Case, bool Passed, bool Matched, bool TimedOut);

/// <summary>Whole-suite outcome.</summary>
public sealed record RegexTestSuiteResult(bool Valid, string? Error, IReadOnlyList<RegexTestCaseResult> Results, IReadOnlyList<string> ParseProblems)
{
    public int Passed => Results.Count(r => r.Passed);
    public int Failed => Results.Count(r => !r.Passed);
}

/// <summary>Split preview and expected-outcome test suites. Pure logic; every operation is bounded and cancellable.</summary>
public static class RegexBuilderOperations
{
    public const int MaxPieces = 2000;
    public const int MaxTestCases = 200;

    public static RegexSplitPreview Split(string pattern, RegexBuilderFlags flags, string sample, CancellationToken cancellation = default)
    {
        var compiled = RegexBuilderEngine.Compile(pattern, flags);
        if (!compiled.Valid) return new(false, [], false, false, false, compiled.Error, []);
        var text = RegexBuilderEngine.BoundSample(sample ?? "", out _);
        var notes = new List<string>();
        var regex = compiled.Regex!;
        if (regex.GetGroupNumbers().Length > 1) notes.Add("The pattern has capture groups, so the captured text is returned between the pieces (this is how .NET splits).");
        try
        {
            if (cancellation.IsCancellationRequested) return new(true, [], false, false, true, "Preview cancelled.", notes);
            var pieces = regex.Split(text, MaxPieces + 1);
            var cut = pieces.Length > MaxPieces;
            if (cut) pieces = pieces[..MaxPieces];
            if (pieces.Length == 1 && pieces[0] == text && text.Length > 0) notes.Add("The pattern did not match, so the text stays in one piece.");
            return new(true, pieces.Select(p => p.Length > RegexBuilderEngine.MaxGroupValueChars ? p[..RegexBuilderEngine.MaxGroupValueChars] + "…" : p).ToList(), cut, false, false, null, notes);
        }
        catch (RegexMatchTimeoutException) { return new(true, [], false, true, false, $"Stopped: the pattern exceeded the {RegexBuilderEngine.MatchTimeout.TotalMilliseconds:0} ms limit.", notes); }
    }

    /// <summary>
    /// Parses a suite: a line starting with "+" must match, a line starting with "-" must not match. Blank lines
    /// are ignored and other lines are reported as problems.
    /// </summary>
    public static (List<RegexTestCase> Cases, List<string> Problems) ParseSuite(string? text)
    {
        var cases = new List<RegexTestCase>();
        var problems = new List<string>();
        var number = 0;
        foreach (var raw in (text ?? "").Replace("\r", "").Split('\n'))
        {
            number++;
            if (raw.Length == 0) continue;
            if (cases.Count >= MaxTestCases) { problems.Add($"Only the first {MaxTestCases} cases are used."); break; }
            if (raw[0] is '+' or '-') cases.Add(new(raw.Length > 1 && raw[1] == ' ' ? raw[2..] : raw[1..], raw[0] == '+'));
            else problems.Add($"Line {number} must start with + (must match) or - (must not match).");
        }
        return (cases, problems);
    }

    public static RegexTestSuiteResult RunSuite(string pattern, RegexBuilderFlags flags, string? suite, CancellationToken cancellation = default)
    {
        var (cases, problems) = ParseSuite(suite);
        var compiled = RegexBuilderEngine.Compile(pattern, flags);
        if (!compiled.Valid) return new(false, compiled.Error, [], problems);
        var results = new List<RegexTestCaseResult>();
        foreach (var c in cases)
        {
            if (cancellation.IsCancellationRequested) break;
            var input = RegexBuilderEngine.BoundSample(c.Input, out _);
            try
            {
                var matched = compiled.Regex!.IsMatch(input);
                results.Add(new(c, matched == c.ExpectMatch, matched, false));
            }
            catch (RegexMatchTimeoutException) { results.Add(new(c, false, false, true)); }
        }
        return new(true, null, results, problems);
    }
}

/// <summary>A static finding about backtracking risk.</summary>
public sealed record RegexDiagnostic(string Code, string Message, int Start, int Length);

/// <summary>One growth-probe measurement.</summary>
public sealed record RegexBenchmarkRow(string Subject, int Size, double Milliseconds, bool TimedOut, bool Matched);

/// <summary>Bounded growth measurements with a plain verdict.</summary>
public sealed record RegexBenchmarkResult(bool Valid, string? Error, IReadOnlyList<RegexBenchmarkRow> Rows, string Verdict, bool Suspicious, bool Cancelled);

/// <summary>
/// Backtracking-risk diagnostics. .NET does not expose engine step counts or a match trace, so this reports a static
/// analysis of the pattern plus measured wall-clock timings on growing inputs, and says so.
/// </summary>
public static class RegexDiagnostics
{
    public const string StepCountNotice = "The .NET engine does not expose a step count or a match trace. The figures below are measured times on growing inputs, not engine steps.";
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan TotalBudget = TimeSpan.FromSeconds(3);

    private static readonly Regex unboundedSuffix = new(@"^(?:[*+]|\{\d+,\})\??$", RegexOptions.NonBacktracking);

    /// <summary>Static findings: nested unbounded quantifiers, repeated alternatives under a quantifier and adjacent wildcards.</summary>
    public static IReadOnlyList<RegexDiagnostic> Analyze(string pattern, RegexBuilderFlags flags)
    {
        var found = new List<RegexDiagnostic>();
        if (string.IsNullOrEmpty(pattern)) return found;
        Walk(RegexExplainer.Explain(pattern, flags), found);
        foreach (var use in RegexFlavors.Detect(pattern, flags).Where(u => u.ConstructId == "posix-class"))
            found.Add(new("posix-class", "[:name:] is a POSIX class in other engines, but .NET reads [[:alpha:]] as a set of ordinary characters followed by a stray bracket. Use Convert POSIX classes (More tab) to rewrite it.", use.Position, 2));
        return found;
    }

    private static bool Unbounded(RegexExplainNode q)
    {
        if (q.Family != RegexTokenFamily.Quantifier || q.Children.Count == 0) return false;
        var atomLength = q.Children[0].Length;
        return atomLength <= q.Text.Length && unboundedSuffix.IsMatch(q.Text[atomLength..]);
    }

    private static bool ContainsUnbounded(RegexExplainNode node) => RegexExplainer.Flatten(node.Children).Any(x => Unbounded(x.Node));

    private static void Walk(IEnumerable<RegexExplainNode> nodes, List<RegexDiagnostic> found)
    {
        RegexExplainNode? last = null;
        foreach (var n in nodes)
        {
            if (Unbounded(n))
            {
                var atom = n.Children[0];
                if (atom.Family is RegexTokenFamily.Group or RegexTokenFamily.Lookaround && ContainsUnbounded(atom))
                    found.Add(new("nested-quantifier", "A repeated group contains another unbounded repeat (for example (a+)+). On input that almost matches, a backtracking engine can take exponential time. The builder prefers the non-backtracking engine when it can.", n.Start, n.Length));
                foreach (var (alt, _) in RegexExplainer.Flatten(atom.Children))
                {
                    if (alt.Family != RegexTokenFamily.Alternation) continue;
                    var branches = alt.Children.Select(b => b.Text).ToList();
                    if (branches.Count != branches.Distinct().Count())
                        found.Add(new("repeated-alternative", "Two alternatives under a repeat are identical, so the same text can be matched in more than one way. That multiplies the work when the match fails.", alt.Start, alt.Length));
                    else if (branches.Any(b => b.Length > 0 && branches.Any(o => o != b && o.StartsWith(b, StringComparison.Ordinal))))
                        found.Add(new("overlapping-alternative", "One alternative is the start of another under a repeat, so the engine may try both for the same text. Put the longer alternative first or make the alternatives distinct.", alt.Start, alt.Length));
                }
                if (last != null && Unbounded(last) && last.Children[0].Family == RegexTokenFamily.Any && atom.Family == RegexTokenFamily.Any && last.Start + last.Length == n.Start)
                    found.Add(new("adjacent-wildcards", "Two unbounded wildcards in a row (.*.*) split the same text in many ways, so a failing match takes polynomial time.", last.Start, last.Length + n.Length));
            }
            Walk(n.Children, found);
            last = n;
        }
    }

    /// <summary>
    /// Times the pattern on the sample repeated 1, 2, 4, 8 and 16 times and on an adversarial probe (a run of "a" then
    /// "!"). Uses the backtracking engine on purpose, with a 250 ms limit per measurement and a 3 s total budget.
    /// </summary>
    public static RegexBenchmarkResult Benchmark(string pattern, RegexBuilderFlags flags, string sample, CancellationToken cancellation = default)
    {
        var compiled = RegexBuilderEngine.Compile(pattern, flags);
        if (!compiled.Valid) return new(false, compiled.Error, [], "", false, false);
        Regex regex;
        try { regex = new Regex(pattern, flags.ToOptions() & ~RegexOptions.NonBacktracking, ProbeTimeout); }
        catch (ArgumentException e) { return new(false, e.Message, [], "", false, false); }
        var text = RegexBuilderEngine.BoundSample(sample ?? "", out _);
        var rows = new List<RegexBenchmarkRow>();
        var clock = Stopwatch.StartNew();
        var cancelled = false;
        var overBudget = false;
        void Probe(string subject, string input)
        {
            if (cancellation.IsCancellationRequested) { cancelled = true; return; }
            if (clock.Elapsed > TotalBudget) { overBudget = true; return; }
            var t = Stopwatch.StartNew();
            bool matched = false, timedOut = false;
            try { matched = regex.IsMatch(input); } catch (RegexMatchTimeoutException) { timedOut = true; }
            rows.Add(new(subject, input.Length, t.Elapsed.TotalMilliseconds, timedOut, matched));
        }
        if (text.Length > 0)
            foreach (var k in new[] { 1, 2, 4, 8, 16 })
            {
                if (text.Length * k > RegexBuilderEngine.MaxSampleBytes) break;
                Probe("sample x" + k, string.Concat(Enumerable.Repeat(text, k)));
            }
        foreach (var n in new[] { 16, 32, 64 }) Probe("a-run " + n, new string('a', n) + "!");
        var timedOutRow = rows.FirstOrDefault(r => r.TimedOut);
        var suspicious = false;
        string verdict;
        if (cancelled) verdict = "Cancelled.";
        else if (timedOutRow != null)
        {
            suspicious = true;
            verdict = $"Warning: {timedOutRow.Subject} ({timedOutRow.Size} characters) exceeded the {ProbeTimeout.TotalMilliseconds:0} ms limit. The pattern may backtrack catastrophically on similar text.";
        }
        else
        {
            var run = rows.Where(r => r.Subject.StartsWith("a-run", StringComparison.Ordinal)).ToList();
            if (run.Count == 3 && run[1].Milliseconds >= 5 && run[2].Milliseconds > run[1].Milliseconds * 6)
            {
                suspicious = true;
                verdict = "Warning: time grew more than six times when the probe doubled. That is super-linear growth.";
            }
            else verdict = overBudget ? "Stopped at the 3 s budget; later probes were not run." : "No super-linear growth was seen on these inputs. This is a measurement on a few inputs, not a proof.";
        }
        return new(true, null, rows, verdict, suspicious, cancelled);
    }
}
