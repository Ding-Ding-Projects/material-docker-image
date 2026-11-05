using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>Vulnerability counts as Docker Scout prints them (for example <c>0C 3H 0M 9L 1?</c>).</summary>
public sealed record ScoutVulnerabilityCounts(int Critical, int High, int Medium, int Low, int Unknown)
{
    public int Total => Critical + High + Medium + Low + Unknown;
}

/// <summary>
/// Change against the current base image for each severity, read from the second line of the Vulnerabilities column.
/// Docker Scout leaves a severity blank when it did not change, so a null value means "no change printed".
/// </summary>
public sealed record ScoutVulnerabilityDelta(int? Critical, int? High, int? Medium, int? Low, int? Unknown)
{
    public bool Any => Critical is not null || High is not null || Medium is not null || Low is not null || Unknown is not null;
}

/// <summary>The base image Docker Scout detected, with the key/value facts it printed.</summary>
public sealed record ScoutBaseImage(string Reference, IReadOnlyList<KeyValuePair<string, string>> Facts, ScoutVulnerabilityCounts? Counts)
{
    public string Fact(string key) => Facts.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase)).Value ?? "";
}

/// <summary>One recommended tag from the Refresh or Change table.</summary>
public sealed record ScoutRecommendation(
    string Kind, string Tag, IReadOnlyList<string> Reasons, IReadOnlyList<string> AlsoKnownAs, IReadOnlyList<string> Benefits,
    IReadOnlyList<KeyValuePair<string, string>> Details, string Pushed, ScoutVulnerabilityCounts? Counts, ScoutVulnerabilityDelta? Delta)
{
    public const string RefreshKind = "Refresh";
    public const string UpdateKind = "Update";
    public string Detail(string key) => Details.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase)).Value ?? "";
}

/// <summary>Everything read from one <c>docker scout recommendations</c> text report. Nothing is filled in that the report did not print.</summary>
public sealed record ScoutRecommendationReport(
    string Target, string TargetDigest, ScoutBaseImage? BaseImage, bool NoBaseImage, string Problem,
    bool RefreshShown, bool RefreshUpToDate, bool UpdateShown, bool NoUpdates,
    IReadOnlyList<ScoutRecommendation> Recommendations, bool BaseAutoDetected)
{
    public IEnumerable<ScoutRecommendation> Refresh => Recommendations.Where(r => r.Kind == ScoutRecommendation.RefreshKind);
    public IEnumerable<ScoutRecommendation> Updates => Recommendations.Where(r => r.Kind == ScoutRecommendation.UpdateKind);
}

/// <summary>
/// Parses the human-readable output of <c>docker scout recommendations</c> (Docker Scout has no structured format for
/// this command). The parser is strict about the table shape: rows start where the Vulnerabilities column holds a count
/// line, and the change line is aligned to those counts by column position. Anything it cannot read is left out.
/// </summary>
public static class ScoutRecommendationParser
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly Regex Ansi = new(@"\x1B\[[0-9;?]*[ -/]*[@-~]|\x1B\][^\x07]*\x07", RegexOptions.None, RegexTimeout);
    private static readonly Regex CountToken = new(@"(\d+)([CHML?])", RegexOptions.None, RegexTimeout);
    private static readonly Regex DeltaToken = new(@"[+-]\d+", RegexOptions.None, RegexTimeout);
    private static readonly Regex CountsLine = new(@"^\s*\d+C\s+\d+H\s+\d+M\s+\d+L(\s+\d+\?)?\s*$", RegexOptions.None, RegexTimeout);
    private const char Bar = '│';

    /// <summary>Reads standard output plus standard error (for the "auto-detected" note and failure lines).</summary>
    public static ScoutRecommendationReport Parse(string standardOutput, string standardError = "")
    {
        var lines = Ansi.Replace(standardOutput ?? "", "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var errors = Ansi.Replace(standardError ?? "", "");
        string target = "", digest = "", baseReference = "", problem = "";
        var noBase = false;
        var facts = new List<KeyValuePair<string, string>>();
        var recommendations = new List<ScoutRecommendation>();
        bool refreshShown = false, refreshUpToDate = false, updateShown = false, noUpdates = false;
        string? section = null;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            if (section == null && KeyValue(line) is { } kv && (kv.Key == "Target" || kv.Key == "digest"))
            {
                if (kv.Key == "Target") target = kv.Value; else digest = kv.Value;
                continue;
            }
            if (trimmed.StartsWith("Could not display recommendations", StringComparison.OrdinalIgnoreCase))
            {
                var rest = string.Join("\n", lines.Skip(i + 1).Select(l => l.Trim()).Where(l => l.Length > 0));
                if (rest.Contains("has no base image", StringComparison.OrdinalIgnoreCase)) noBase = true;
                problem = (trimmed + (rest.Length > 0 ? "\n" + rest : "")).Trim();
                break;
            }
            if (trimmed.StartsWith("Base image is ", StringComparison.Ordinal))
            {
                baseReference = trimmed["Base image is ".Length..].Trim();
                section = "base";
                continue;
            }
            if (trimmed == "Refresh base image") { section = ScoutRecommendation.RefreshKind; refreshShown = true; continue; }
            if (trimmed == "Change base image") { section = ScoutRecommendation.UpdateKind; updateShown = true; continue; }
            if (section == "base")
            {
                if (KeyValue(line) is { } fact) facts.Add(fact);
                continue;
            }
            if (section is ScoutRecommendation.RefreshKind or ScoutRecommendation.UpdateKind)
            {
                if (trimmed.Contains("This image version is up to date", StringComparison.OrdinalIgnoreCase)) { refreshUpToDate = true; continue; }
                if (trimmed.Contains("no tag recommendations", StringComparison.OrdinalIgnoreCase)) { noUpdates = true; continue; }
                if (IsSeparator(trimmed))
                {
                    var end = i + 1;
                    while (end < lines.Length && lines[end].Contains(Bar)) end++;
                    recommendations.AddRange(ReadTable(section, lines[(i + 1)..end]));
                    i = end - 1;
                }
            }
        }
        ScoutBaseImage? baseImage = null;
        if (baseReference.Length > 0)
        {
            var vulnerabilities = facts.FirstOrDefault(f => f.Key == "Vulnerabilities").Value;
            baseImage = new ScoutBaseImage(baseReference, facts.Where(f => f.Key != "Vulnerabilities").ToList(), vulnerabilities == null ? null : Counts(vulnerabilities));
        }
        var autoDetected = errors.Contains("Base image was auto-detected", StringComparison.OrdinalIgnoreCase);
        return new ScoutRecommendationReport(target, digest, baseImage, noBase, problem, refreshShown, refreshUpToDate, updateShown, noUpdates, recommendations, autoDetected);
    }

    /// <summary>A "Key │ Value" line outside the recommendation tables.</summary>
    private static KeyValuePair<string, string>? KeyValue(string line)
    {
        var bar = line.IndexOf(Bar);
        if (bar <= 0 || line.IndexOf(Bar, bar + 1) >= 0) return null;
        var key = line[..bar].Trim();
        var value = line[(bar + 1)..].Trim();
        return key.Length == 0 ? null : new KeyValuePair<string, string>(key, value);
    }

    private static bool IsSeparator(string trimmed) => trimmed.Length > 3 && trimmed.Contains('┼') && trimmed.All(c => c is '─' or '┼');

    /// <summary>Parses <c>0C 3H 0M 9L</c> (with an optional <c>N?</c>); null when the text is not a count line.</summary>
    public static ScoutVulnerabilityCounts? Counts(string text)
    {
        if (!CountsLine.IsMatch(text)) return null;
        int c = 0, h = 0, m = 0, l = 0, u = 0;
        foreach (Match match in CountToken.Matches(text))
        {
            var n = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            switch (match.Groups[2].Value) { case "C": c = n; break; case "H": h = n; break; case "M": m = n; break; case "L": l = n; break; default: u = n; break; }
        }
        return new ScoutVulnerabilityCounts(c, h, m, l, u);
    }

    /// <summary>
    /// Aligns the change line under the count line: each change ends in the same column as the count it modifies (number and severity letter).
    /// Returns null when any change cannot be placed, so a misaligned report never shows a wrong number.
    /// </summary>
    public static ScoutVulnerabilityDelta? Delta(string countLine, string deltaLine)
    {
        if (deltaLine.Trim().Length == 0) return null;
        var ends = new Dictionary<int, string>();
        foreach (Match match in CountToken.Matches(countLine)) ends[match.Index + match.Length] = match.Groups[2].Value;
        int? c = null, h = null, m = null, l = null, u = null;
        var placed = 0;
        foreach (Match match in DeltaToken.Matches(deltaLine))
        {
            if (!ends.TryGetValue(match.Index + match.Length, out var severity)) return null;
            var n = int.Parse(match.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            switch (severity) { case "C": c = n; break; case "H": h = n; break; case "M": m = n; break; case "L": l = n; break; default: u = n; break; }
            placed++;
        }
        if (placed == 0 || deltaLine.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length != placed) return null;
        return new ScoutVulnerabilityDelta(c, h, m, l, u);
    }

    private static IEnumerable<ScoutRecommendation> ReadTable(string kind, string[] rows)
    {
        var groups = new List<List<string[]>>();
        foreach (var row in rows)
        {
            var cells = row.Split(Bar);
            if (cells.Length != 4) continue;
            if (Counts(cells[3]) != null) groups.Add([]);
            if (groups.Count > 0) groups[^1].Add(cells);
        }
        foreach (var group in groups)
        {
            var tagLines = group.Select(c => c[0].Trim()).Where(t => t.Length > 0).ToList();
            if (tagLines.Count == 0) continue;
            var tag = tagLines[0];
            var reasons = new List<string>();
            var aliases = new List<string>();
            var inAliases = false;
            foreach (var t in tagLines.Skip(1))
            {
                if (t == "Also known as:") { inAliases = true; continue; }
                if (inAliases && t.StartsWith('•')) aliases.Add(t[1..].Trim());
                else if (!inAliases) reasons.Add(t);
            }
            var benefits = new List<string>();
            var details = new List<KeyValuePair<string, string>>();
            string? part = null;
            foreach (var d in group.Select(c => c[1].Trim()).Where(t => t.Length > 0))
            {
                if (d == "Benefits:") { part = "benefits"; continue; }
                if (d == "Image details:") { part = "details"; continue; }
                if (!d.StartsWith('•')) continue;
                var item = d[1..].Trim();
                if (part == "benefits") benefits.Add(item);
                else if (part == "details")
                {
                    var colon = item.IndexOf(':');
                    if (colon > 0) details.Add(new(item[..colon].Trim(), item[(colon + 1)..].Trim()));
                }
            }
            var pushed = group.Select(c => c[2].Trim()).FirstOrDefault(t => t.Length > 0) ?? "";
            var counts = Counts(group[0][3]);
            var delta = group.Count > 1 ? Delta(group[0][3], group[1][3]) : null;
            yield return new ScoutRecommendation(kind, tag, reasons, aliases, benefits, details, pushed, counts, delta);
        }
    }

    /// <summary>Parses a size such as "30 MB" or "5.8 MB" into bytes (decimal units, as Docker Scout prints them); null when unreadable.</summary>
    public static double? SizeBytes(string text)
    {
        var m = Regex.Match(text.Trim(), @"^([0-9]+(?:\.[0-9]+)?)\s*([kKMGT]?B)$", RegexOptions.None, RegexTimeout);
        if (!m.Success || !double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return null;
        var power = char.ToUpperInvariant(m.Groups[2].Value[0]) switch { 'K' => 1, 'M' => 2, 'G' => 3, 'T' => 4, _ => 0 };
        return value * Math.Pow(1000, power);
    }
}
