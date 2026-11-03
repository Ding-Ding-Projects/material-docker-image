using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>Severity of a Docker Scout finding. Unspecified is used when the report carries no severity, so nothing is guessed.</summary>
public enum ScoutSeverity { Critical, High, Medium, Low, Unspecified }

/// <summary>One vulnerability affecting one package, as reported by Docker Scout.</summary>
public sealed record ScoutFinding(
    string Id,
    ScoutSeverity Severity,
    string Package,
    string PackageType,
    string InstalledVersion,
    string FixedVersion,
    string AffectedRange,
    string Description,
    string Url,
    double? Score,
    string Purl)
{
    /// <summary>True when the report names a version that fixes the vulnerability.</summary>
    public bool Fixable => ScoutReport.IsFixedVersion(FixedVersion);
    public string SeverityText => Severity.ToString();
}

/// <summary>Finding totals by severity.</summary>
public sealed record ScoutSeverityCounts(int Critical, int High, int Medium, int Low, int Unspecified, int Fixable)
{
    public int Total => Critical + High + Medium + Low + Unspecified;
    public static readonly ScoutSeverityCounts Empty = new(0, 0, 0, 0, 0, 0);
    public ScoutSeverityCounts Add(ScoutSeverityCounts other) => new(Critical + other.Critical, High + other.High, Medium + other.Medium, Low + other.Low, Unspecified + other.Unspecified, Fixable + other.Fixable);
    public int Of(ScoutSeverity severity) => severity switch
    {
        ScoutSeverity.Critical => Critical,
        ScoutSeverity.High => High,
        ScoutSeverity.Medium => Medium,
        ScoutSeverity.Low => Low,
        _ => Unspecified
    };
}

/// <summary>A finding together with the image it was reported for, used for exports.</summary>
public sealed record ScoutExportRow(string Image, string ImageId, DateTime AnalysedUtc, ScoutFinding Finding);

/// <summary>
/// Parses the SARIF 2.1.0 report written by <c>docker scout cves --format sarif</c>. Values are taken from the rule
/// properties, the result properties and the "Key : value" lines of the result message, in that order of preference
/// (result values win over rule values). Fields that are absent stay empty; nothing is inferred beyond the CVSS
/// score-to-severity bands when only a numeric score is present.
/// </summary>
public static class ScoutReport
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly Regex FieldLine = new(@"^\s*([A-Za-z][A-Za-z ]{1,40}?)\s*:\s*(.*?)\s*$", RegexOptions.None, RegexTimeout);
    private static readonly Regex NpmScope = new(@"^@[a-z0-9][a-z0-9._-]*/[a-z0-9][a-z0-9._-]*$", RegexOptions.None, RegexTimeout);
    private static readonly Regex Ansi =new(@"\x1B(?:\[[0-?]*[ -/]*[@-~]|\][^\x07\x1B]*(?:\x07|\x1B\\)|[@-Z\\-_])", RegexOptions.None, RegexTimeout);

    /// <summary>Parses a SARIF document. Throws <see cref="FormatException"/> when the text is not SARIF JSON.</summary>
    public static IReadOnlyList<ScoutFinding> ParseSarif(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); }
        catch (JsonException e) { throw new FormatException("The report is not valid JSON: " + e.Message, e); }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("runs", out var runs) || runs.ValueKind != JsonValueKind.Array)
                throw new FormatException("The report has no SARIF \"runs\" array.");
            var findings = new List<ScoutFinding>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var run in runs.EnumerateArray())
            {
                var rules = new List<JsonElement>();
                var byId = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                if (Path(run, "tool", "driver", "rules") is { ValueKind: JsonValueKind.Array } ruleArray)
                {
                    foreach (var rule in ruleArray.EnumerateArray())
                    {
                        rules.Add(rule);
                        if (Str(rule, "id") is { Length: > 0 } id) byId.TryAdd(id, rule);
                    }
                }
                if (!run.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) continue;
                foreach (var result in results.EnumerateArray())
                {
                    JsonElement? rule = null;
                    var ruleId = Str(result, "ruleId");
                    if (ruleId.Length > 0 && byId.TryGetValue(ruleId, out var r)) rule = r;
                    else if (result.TryGetProperty("ruleIndex", out var index) && index.TryGetInt32(out var i) && i >= 0 && i < rules.Count) { rule = rules[i]; if (ruleId.Length == 0) ruleId = Str(rules[i], "id"); }
                    foreach (var finding in FromResult(result, rule, ruleId))
                    {
                        if (seen.Add(finding.Id + "|" + finding.Purl + "|" + finding.Package + "|" + finding.InstalledVersion)) findings.Add(finding);
                    }
                }
            }
            return findings;
        }
    }

    private static IEnumerable<ScoutFinding> FromResult(JsonElement result, JsonElement? rule, string ruleId)
    {
        var message = Text(Path(result, "message", "text"));
        var fields = MessageFields(message);
        var ruleProps = rule is { } rp ? Path(rp, "properties") : null;
        var resultProps = Path(result, "properties");
        string Prop(params string[] names)
        {
            foreach (var source in new[] { resultProps, ruleProps })
            {
                if (source is not { ValueKind: JsonValueKind.Object } s) continue;
                foreach (var name in names) if (Str(s, name) is { Length: > 0 } v) return v;
            }
            return "";
        }
        string Field(params string[] names)
        {
            foreach (var name in names) if (fields.TryGetValue(name, out var v) && v.Length > 0) return v;
            return "";
        }

        var id = ruleId.Length > 0 ? ruleId : Field("vulnerability", "cve");
        if (id.Length == 0) yield break;

        double? score = null;
        var scoreText = Prop("security-severity", "cvssV3_score", "cvss_score");
        if (double.TryParse(scoreText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedScore)) score = parsedScore;
        var severity = ParseSeverity(Prop("cvssV3_severity", "severity"));
        if (severity == ScoutSeverity.Unspecified) severity = ParseSeverity(Field("severity"));
        if (severity == ScoutSeverity.Unspecified && score is { } s2) severity = FromScore(s2);

        var fixedVersion = Prop("fixed_version", "fixedVersion");
        if (fixedVersion.Length == 0) fixedVersion = Field("fixed version", "fixed versions", "fix version");
        var affected = Prop("affected_version", "affected_range");
        if (affected.Length == 0) affected = Field("affected range", "affected version", "affected versions");

        var description = rule is { } rd ? FirstNonEmpty(Text(Path(rd, "fullDescription", "text")), Text(Path(rd, "shortDescription", "text")), Text(Path(rd, "help", "text"))) : "";
        if (description.Length == 0) description = message;
        description = TrimIdPrefix(description, id).Trim();

        var url = rule is { } ru ? Str(ru, "helpUri") : "";
        if (url.Length == 0) url = Field("url", "advisory");
        if (!IsWebUrl(url)) url = "";

        var purls = new List<string>();
        if (Field("package") is { Length: > 0 } fromMessage) purls.Add(fromMessage);
        else
        {
            foreach (var source in new[] { resultProps, ruleProps })
            {
                if (source is { ValueKind: JsonValueKind.Object } so && so.TryGetProperty("purls", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    purls.AddRange(list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString() ?? "").Where(p => p.Length > 0));
                    if (purls.Count > 0) break;
                }
            }
            if (purls.Count == 0 && Path(result, "locations") is { ValueKind: JsonValueKind.Array } locations)
            {
                foreach (var location in locations.EnumerateArray())
                {
                    if (Path(location, "logicalLocations") is { ValueKind: JsonValueKind.Array } logical)
                        purls.AddRange(logical.EnumerateArray().Select(l => FirstNonEmpty(Str(l, "fullyQualifiedName"), Str(l, "name"))).Where(n => n.StartsWith("pkg:", StringComparison.Ordinal)));
                }
            }
        }
        if (purls.Count == 0) purls.Add("");

        foreach (var purl in purls.Distinct(StringComparer.Ordinal))
        {
            var (type, name, version) = ParsePurl(purl);
            if (name.Length == 0) name = purl;
            yield return new ScoutFinding(id, severity, name, type, version, fixedVersion, affected, description, url, score, purl.StartsWith("pkg:", StringComparison.Ordinal) ? purl : "");
        }
    }

    /// <summary>Reads "Key : value" lines (as printed in Scout result messages) into a case-insensitive map.</summary>
    public static IReadOnlyDictionary<string, string> MessageFields(string message)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in message.Replace("\r\n", "\n").Split('\n'))
        {
            var m = FieldLine.Match(line);
            if (!m.Success) continue;
            var key = Regex.Replace(m.Groups[1].Value.Trim(), @"\s+", " ", RegexOptions.None, RegexTimeout).ToLowerInvariant();
            map.TryAdd(key, m.Groups[2].Value.Trim());
        }
        return map;
    }

    public static ScoutSeverity ParseSeverity(string text) => text.Trim().ToUpperInvariant() switch
    {
        "CRITICAL" or "C" => ScoutSeverity.Critical,
        "HIGH" or "H" => ScoutSeverity.High,
        "MEDIUM" or "MODERATE" or "M" => ScoutSeverity.Medium,
        "LOW" or "L" or "NEGLIGIBLE" or "MINIMAL" => ScoutSeverity.Low,
        _ => ScoutSeverity.Unspecified
    };

    /// <summary>CVSS v3 qualitative bands: 9.0+ critical, 7.0+ high, 4.0+ medium, above 0 low.</summary>
    public static ScoutSeverity FromScore(double score) => score >= 9 ? ScoutSeverity.Critical : score >= 7 ? ScoutSeverity.High : score >= 4 ? ScoutSeverity.Medium : score > 0 ? ScoutSeverity.Low : ScoutSeverity.Unspecified;

    public static bool IsFixedVersion(string text)
    {
        var t = text.Trim();
        return t.Length > 0 && !t.Equals("not fixed", StringComparison.OrdinalIgnoreCase) && !t.Equals("none", StringComparison.OrdinalIgnoreCase)
            && !t.Equals("n/a", StringComparison.OrdinalIgnoreCase) && !t.Equals("unknown", StringComparison.OrdinalIgnoreCase) && t != "-";
    }

    /// <summary>Splits a package URL (pkg:type/namespace/name@version?qualifiers#subpath) into type, name and version.</summary>
    public static (string Type, string Name, string Version) ParsePurl(string purl)
    {
        if (!purl.StartsWith("pkg:", StringComparison.Ordinal)) return ("", "", "");
        var body = purl[4..];
        var hash = body.IndexOf('#'); if (hash >= 0) body = body[..hash];
        var query = body.IndexOf('?'); if (query >= 0) body = body[..query];
        var version = "";
        var at = body.LastIndexOf('@');
        if (at > 0 && at > body.LastIndexOf('/')) { version = body[(at + 1)..]; body = body[..at]; }
        var slash = body.IndexOf('/');
        var type = slash > 0 ? body[..slash] : "";
        var path = slash > 0 ? body[(slash + 1)..] : body;
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToList();
        var name = segments.Count == 0 ? "" : type is "npm" or "golang" or "maven" or "composer" && segments.Count > 1 ? string.Join(type == "maven" ? ":" : "/", segments) : segments[^1];
        return (Uri.UnescapeDataString(type).ToLowerInvariant(), name, Uri.UnescapeDataString(version));
    }

    public static ScoutSeverityCounts Count(IEnumerable<ScoutFinding> findings)
    {
        int c = 0, h = 0, m = 0, l = 0, u = 0, f = 0;
        foreach (var x in findings)
        {
            switch (x.Severity)
            {
                case ScoutSeverity.Critical: c++; break;
                case ScoutSeverity.High: h++; break;
                case ScoutSeverity.Medium: m++; break;
                case ScoutSeverity.Low: l++; break;
                default: u++; break;
            }
            if (x.Fixable) f++;
        }
        return new ScoutSeverityCounts(c, h, m, l, u, f);
    }

    /// <summary>Most severe first, then by identifier and package.</summary>
    public static IReadOnlyList<ScoutFinding> Sort(IEnumerable<ScoutFinding> findings) =>
        findings.OrderBy(f => (int)f.Severity).ThenByDescending(f => f.Score ?? -1).ThenBy(f => f.Id, StringComparer.Ordinal).ThenBy(f => f.Package, StringComparer.Ordinal).ToList();

    public static bool IsWebUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) && uri.Host.Length > 0;

    /// <summary>Removes terminal colour and cursor escape sequences so command output can be shown as plain text.</summary>
    public static string StripAnsi(string text) => Ansi.Replace(text, "");

    public static string ToCsv(IEnumerable<ScoutExportRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("image,image_id,analysed_utc,id,severity,score,package,package_type,installed_version,fixed_version,affected_range,url,purl,description");
        foreach (var r in rows)
        {
            var f = r.Finding;
            sb.AppendLine(string.Join(",", new[]
            {
                r.Image, r.ImageId, r.AnalysedUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture), f.Id, f.SeverityText,
                f.Score?.ToString("0.0", CultureInfo.InvariantCulture) ?? "", f.Package, f.PackageType, f.InstalledVersion, f.FixedVersion, f.AffectedRange, f.Url, f.Purl, f.Description
            }.Select(Csv)));
        }
        return sb.ToString();
    }

    public static string ToJson(IEnumerable<ScoutExportRow> rows)
    {
        var list = rows.Select(r => new Dictionary<string, object?>
        {
            ["image"] = r.Image,
            ["imageId"] = r.ImageId,
            ["analysedUtc"] = r.AnalysedUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["id"] = r.Finding.Id,
            ["severity"] = r.Finding.SeverityText,
            ["score"] = r.Finding.Score,
            ["package"] = r.Finding.Package,
            ["packageType"] = r.Finding.PackageType,
            ["installedVersion"] = r.Finding.InstalledVersion,
            ["fixedVersion"] = r.Finding.FixedVersion,
            ["fixable"] = r.Finding.Fixable,
            ["affectedRange"] = r.Finding.AffectedRange,
            ["url"] = r.Finding.Url,
            ["purl"] = r.Finding.Purl,
            ["description"] = r.Finding.Description,
        }).ToList();
        return JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private static string Csv(string value)
    {
        var v = value.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
        // Neutralise spreadsheet formula injection while keeping the visible text.
        // Scoped npm package names (@scope/name) are plain identifiers and stay unchanged.
        if (v.Length > 0 && "=+-@\t".Contains(v[0]) && !NpmScope.IsMatch(v)) v = "'" + v;
        return v.IndexOfAny([',', '"']) >= 0 || v != v.Trim() ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }

    private static string TrimIdPrefix(string text, string id)
    {
        var t = text.TrimStart();
        if (id.Length > 0 && t.StartsWith(id, StringComparison.OrdinalIgnoreCase))
        {
            var rest = t[id.Length..].TrimStart();
            if (rest.StartsWith(':') || rest.StartsWith('-')) return rest[1..];
        }
        return text;
    }

    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(v => v.Trim().Length > 0) ?? "";

    internal static JsonElement? Path(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var name in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current)) return null;
        }
        return current.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : current;
    }

    internal static string Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) ? Text(v) : "";

    internal static string Text(JsonElement? value) => value switch
    {
        { ValueKind: JsonValueKind.String } s => s.GetString() ?? "",
        { ValueKind: JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False } n => n.ToString(),
        _ => ""
    };
}
