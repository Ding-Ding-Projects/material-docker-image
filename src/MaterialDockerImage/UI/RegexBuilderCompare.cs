using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>One pattern's row in a comparison: whether it gives the expected answers and how long it took.</summary>
public sealed record RegexCompareRow(string Pattern, bool Valid, string? Error, string Engine, int MatchingPassed, int MatchingTotal, int NonMatchingPassed, int NonMatchingTotal, double MatchingMs, double NonMatchingMs, bool TimedOut)
{
    public bool AllCorrect => Valid && !TimedOut && MatchingPassed == MatchingTotal && NonMatchingPassed == NonMatchingTotal;
    public double TotalMs => MatchingMs + NonMatchingMs;

    /// <summary>
    /// The row as one line of the comparison table. <paramref name="translate"/> turns the fixed words into the chosen language; the pattern, the numbers and the error text
    /// that the .NET framework itself wrote for an invalid pattern stay as they are, after a translated lead-in. English is produced when it is null.
    /// </summary>
    public string Format(Func<string, string>? translate = null)
    {
        var t = translate ?? (s => s);
        if (!Valid) return $"✗ {Pattern}   {t("invalid:")} {Error}";
        var unit = t("ms");
        return $"{(AllCorrect ? "✓" : "✗")} {Pattern}   {t($"must match {MatchingPassed}/{MatchingTotal}, must not match {NonMatchingPassed}/{NonMatchingTotal}")}   {MatchingMs:0.000} {unit} / {NonMatchingMs:0.000} {unit}   {(TimedOut ? t("timed out") : Engine)}";
    }
}

/// <summary>The comparison of several patterns on the same matching and non-matching subjects.</summary>
public sealed record RegexCompareResult(bool Valid, string? Error, IReadOnlyList<RegexCompareRow> Rows, string Verdict, bool Cancelled);

/// <summary>
/// Compares several patterns against the subjects of a test-case suite: every "+" line is a subject that must match and every "-" line a subject that must not.
/// Each pattern is checked for correctness first and then timed on both groups. The timings are wall-clock measurements on this machine and vary from run to run.
/// </summary>
public static class RegexCompare
{
    public const int MaxPatterns = 8;
    public const int Repeats = 20;
    public static readonly TimeSpan TotalBudget = TimeSpan.FromSeconds(4);

    /// <summary>One pattern per non-empty line, at most <see cref="MaxPatterns"/>.</summary>
    public static List<string> ParsePatterns(string? text) =>
        (text ?? "").Replace("\r", "").Split('\n').Where(l => l.Length > 0).Take(MaxPatterns).ToList();

    /// <param name="translate">Optional translation of each sentence of the verdict; English is produced when it is null.</param>
    public static RegexCompareResult Run(IReadOnlyList<string> patterns, RegexBuilderFlags flags, string? suite, CancellationToken cancellation = default, Func<string, string>? translate = null)
    {
        if (patterns.Count < 2) return new(false, "Enter at least two patterns, one per line.", [], "", false);
        var (cases, _) = RegexBuilderOperations.ParseSuite(suite);
        var matching = cases.Where(c => c.ExpectMatch).Select(c => RegexBuilderEngine.BoundSample(c.Input, out _)).ToList();
        var non = cases.Where(c => !c.ExpectMatch).Select(c => RegexBuilderEngine.BoundSample(c.Input, out _)).ToList();
        if (matching.Count == 0 && non.Count == 0) return new(false, "Add test cases first: a line starting with + is a subject that must match, a line starting with - a subject that must not.", [], "", false);
        var rows = new List<RegexCompareRow>();
        var clock = Stopwatch.StartNew();
        var cancelled = false;
        foreach (var pattern in patterns)
        {
            if (cancellation.IsCancellationRequested || clock.Elapsed > TotalBudget) { cancelled = true; break; }
            var compiled = RegexBuilderEngine.Compile(pattern, flags);
            if (!compiled.Valid) { rows.Add(new(pattern, false, compiled.Error, "", 0, matching.Count, 0, non.Count, 0, 0, false)); continue; }
            var regex = compiled.Regex!;
            var timedOut = false;
            int mp = 0, np = 0;
            double mt = 0, nt = 0;
            try
            {
                foreach (var s in matching) if (regex.IsMatch(s)) mp++;
                foreach (var s in non) if (!regex.IsMatch(s)) np++;
                mt = Time(regex, matching);
                nt = Time(regex, non);
            }
            catch (RegexMatchTimeoutException) { timedOut = true; }
            rows.Add(new(pattern, true, null, compiled.Engine, mp, matching.Count, np, non.Count, mt, nt, timedOut));
        }
        return new(true, null, rows, Verdict(rows, cancelled, translate ?? (s => s)), cancelled);
    }

    private static double Time(Regex regex, List<string> subjects)
    {
        if (subjects.Count == 0) return 0;
        var t = Stopwatch.StartNew();
        for (var r = 0; r < Repeats; r++) foreach (var s in subjects) regex.IsMatch(s);
        return t.Elapsed.TotalMilliseconds / Repeats;
    }

    private static string Verdict(List<RegexCompareRow> rows, bool cancelled, Func<string, string> t)
    {
        if (cancelled) return t("Stopped at the time budget; later patterns were not compared.");
        var good = rows.Where(r => r.AllCorrect).ToList();
        if (good.Count == 0) return t("No pattern gave the expected answer for every subject, so none can be called the fastest.");
        var best = good.OrderBy(r => r.TotalMs).First();
        var text = t($"Fastest of the {good.Count} pattern{(good.Count == 1 ? "" : "s")} that are right for every subject: {best.Pattern} ({best.TotalMs:0.000} ms per pass).");
        if (good.Count < rows.Count) text += " " + t($"{rows.Count - good.Count} other pattern{(rows.Count - good.Count == 1 ? " is" : "s are")} wrong for some subject, invalid or timed out.");
        return text + " " + t("These are wall-clock times on this machine and change from run to run.");
    }
}

/// <summary>Estimates how running time grows with input size from the timing probe's measurements.</summary>
public static class RegexGrowth
{
    /// <summary>
    /// Fits the growth exponent between the smallest and largest probe of one family ("sample" or "a-run"): 1 is linear, 2 quadratic. Returns a plain sentence, or says that the
    /// times were too small to tell, because a timer reads noise below about a millisecond.
    /// </summary>
    public static string Describe(IReadOnlyList<RegexBenchmarkRow> rows, string family, Func<string, string>? t = null)
    {
        t ??= s => s;
        var set = rows.Where(r => r.Subject.StartsWith(family, StringComparison.Ordinal) && !r.TimedOut).OrderBy(r => r.Size).ToList();
        if (set.Count < 2) return t($"Not enough {family} probes to estimate growth.");
        var small = set[0]; var large = set[^1];
        if (large.Milliseconds < 1) return t($"The {family} probes finished in under a millisecond, too fast to estimate how time grows.");
        var floor = Math.Max(small.Milliseconds, 0.05);
        var exponent = Math.Log(large.Milliseconds / floor) / Math.Log((double)large.Size / small.Size);
        var kind = exponent < 1.3 ? "about linear" : exponent < 1.8 ? "between linear and quadratic" : exponent < 2.5 ? "about quadratic" : "worse than quadratic";
        return t($"{char.ToUpperInvariant(family[0])}{family[1..]} probes: time grew {kind} (exponent about {exponent:0.0}) from {small.Size} to {large.Size} characters.");
    }
}
