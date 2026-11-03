using System.Text.Json;

namespace MaterialDockerImage.UI;

/// <summary>One package from a software bill of materials.</summary>
public sealed record ScoutSbomPackage(string Type, string Name, string Version, string Purl, string Licenses);

/// <summary>
/// Reads package lists from SBOM JSON: the Docker Scout native format (<c>docker scout sbom --format json</c>, an
/// "artifacts" array), SPDX JSON ("packages") and CycloneDX JSON ("components"). Values are copied as reported.
/// </summary>
public static class ScoutSbom
{
    public static IReadOnlyList<ScoutSbomPackage> Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true }); }
        catch (JsonException e) { throw new FormatException("The SBOM is not valid JSON: " + e.Message, e); }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new FormatException("The SBOM JSON is not an object.");
            var list = new List<ScoutSbomPackage>();
            if (root.TryGetProperty("artifacts", out var artifacts) && artifacts.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in artifacts.EnumerateArray())
                {
                    var purl = ScoutReport.Str(a, "purl");
                    var type = First(ScoutReport.Str(a, "package_type"), ScoutReport.Str(a, "type"), ScoutReport.ParsePurl(purl).Type);
                    var ns = ScoutReport.Str(a, "namespace");
                    var name = ScoutReport.Str(a, "name");
                    if (type is "npm" or "golang" or "maven" && ns.Length > 0 && !name.Contains('/')) name = ns + (type == "maven" ? ":" : "/") + name;
                    list.Add(new ScoutSbomPackage(type, name, ScoutReport.Str(a, "version"), purl, Licenses(a, "licenses")));
                }
            }
            else if (root.TryGetProperty("packages", out var packages) && packages.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in packages.EnumerateArray())
                {
                    var purl = "";
                    if (p.TryGetProperty("externalRefs", out var refs) && refs.ValueKind == JsonValueKind.Array)
                        purl = refs.EnumerateArray().Where(r => ScoutReport.Str(r, "referenceType") == "purl").Select(r => ScoutReport.Str(r, "referenceLocator")).FirstOrDefault() ?? "";
                    var license = First(Clean(ScoutReport.Str(p, "licenseConcluded")), Clean(ScoutReport.Str(p, "licenseDeclared")));
                    list.Add(new ScoutSbomPackage(ScoutReport.ParsePurl(purl).Type, ScoutReport.Str(p, "name"), ScoutReport.Str(p, "versionInfo"), purl, license));
                }
            }
            else if (root.TryGetProperty("components", out var components) && components.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in components.EnumerateArray())
                {
                    var purl = ScoutReport.Str(c, "purl");
                    var group = ScoutReport.Str(c, "group");
                    var name = ScoutReport.Str(c, "name");
                    list.Add(new ScoutSbomPackage(First(ScoutReport.ParsePurl(purl).Type, ScoutReport.Str(c, "type")), group.Length > 0 ? group + "/" + name : name, ScoutReport.Str(c, "version"), purl, CycloneLicenses(c)));
                }
            }
            else
            {
                throw new FormatException("The SBOM JSON has no \"artifacts\", \"packages\" or \"components\" list.");
            }
            return list.Where(p => p.Name.Length > 0)
                .OrderBy(p => p.Type, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Version, StringComparer.Ordinal)
                .ToList();
        }
    }

    /// <summary>Package counts per type, largest first.</summary>
    public static IReadOnlyList<(string Type, int Count)> CountByType(IEnumerable<ScoutSbomPackage> packages) =>
        packages.GroupBy(p => p.Type.Length == 0 ? "other" : p.Type, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, g.Count())).OrderByDescending(x => x.Item2).ThenBy(x => x.Key, StringComparer.Ordinal).ToList();

    private static string Licenses(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return "";
        if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? "";
        if (value.ValueKind != JsonValueKind.Array) return "";
        return string.Join(", ", value.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : First(ScoutReport.Str(v, "value"), ScoutReport.Str(v, "name"), ScoutReport.Str(v, "id"))).Where(v => v.Length > 0).Distinct());
    }

    private static string CycloneLicenses(JsonElement component)
    {
        if (!component.TryGetProperty("licenses", out var licenses) || licenses.ValueKind != JsonValueKind.Array) return "";
        return string.Join(", ", licenses.EnumerateArray().Select(l =>
            l.TryGetProperty("license", out var lic) ? First(ScoutReport.Str(lic, "id"), ScoutReport.Str(lic, "name")) : ScoutReport.Str(l, "expression")).Where(v => v.Length > 0).Distinct());
    }

    private static string Clean(string license) => license is "NOASSERTION" or "NONE" ? "" : license;
    private static string First(params string[] values) => values.FirstOrDefault(v => v.Length > 0) ?? "";
}
