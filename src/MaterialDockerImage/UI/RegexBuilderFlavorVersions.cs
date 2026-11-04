using System.Text;

namespace MaterialDockerImage.UI;

/// <summary>One selectable version of an engine. Ranks order the versions of one engine from oldest to newest.</summary>
public sealed record RegexFlavorVersion(string Id, string Label, int Rank);

/// <summary>
/// Engine version picker for the flavor comparison. Only the differences that the flavor table already records as version dependent are modelled: the newest
/// version of each engine is the table itself, and an older version of Python or JavaScript changes the cells that the table notes as appearing in a later release.
/// The version facts come from the languages' documentation and release notes, not from running old engines; the application never runs an engine except .NET.
/// An engine with only one version row has nothing to pick.
/// </summary>
public static class RegexFlavorVersions
{
    private sealed record Rule(string Flavor, string Construct, int FromRank, FlavorSupport Support, string Note);

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<RegexFlavorVersion>> versions = new Dictionary<string, IReadOnlyList<RegexFlavorVersion>>
    {
        ["net"] = [new("net10", ".NET 10 (the one this builder runs)", 1)],
        ["pcre2"] = [new("pcre2-10", "PCRE2 10.x", 1)],
        ["js"] = [new("es2017", "ECMAScript 2017 and earlier (older browsers and Node)", 1), new("es2018", "ECMAScript 2018 and later (current browsers and Node)", 2)],
        ["python"] = [new("py39", "Python 3.9 and 3.10", 1), new("py311", "Python 3.11 to 3.13", 2), new("py314", "Python 3.14 and later", 3)],
        ["go"] = [new("go122", "Go 1.22 and later", 1)],
        ["rust"] = [new("rust-regex", "the regex crate used by ripgrep", 1)],
        ["ere"] = [new("gnu-ere", "GNU grep -E", 1)],
    };

    // Each rule says what a construct is from a given rank upward (until a later rule for the same construct). Where the newest version differs from nothing,
    // there is no rule and the table itself applies.
    private static readonly IReadOnlyList<Rule> rules =
    [
        new("python", "possessive", 1, FlavorSupport.No, "added in Python 3.11"), new("python", "possessive", 2, FlavorSupport.Yes, "Python 3.11 and later"),
        new("python", "atomic", 1, FlavorSupport.No, "added in Python 3.11"), new("python", "atomic", 2, FlavorSupport.Yes, "Python 3.11 and later"),
        new("python", "anchor-z", 1, FlavorSupport.No, "added in Python 3.14; write \\Z, which Python reads as the very end"), new("python", "anchor-z", 2, FlavorSupport.No, "added in Python 3.14; write \\Z, which Python reads as the very end"),
        new("python", "anchor-z", 3, FlavorSupport.Yes, "Python 3.14 and later"),
        new("js", "lookbehind", 1, FlavorSupport.No, "added in ECMAScript 2018"), new("js", "lookbehind", 2, FlavorSupport.Yes, ""),
        new("js", "lookbehind-variable", 1, FlavorSupport.No, "added in ECMAScript 2018 together with lookbehind"), new("js", "lookbehind-variable", 2, FlavorSupport.Yes, ""),
        new("js", "named-angle", 1, FlavorSupport.No, "named groups were added in ECMAScript 2018"), new("js", "named-angle", 2, FlavorSupport.Yes, ""),
        new("js", "named-backref", 1, FlavorSupport.No, "named groups were added in ECMAScript 2018"), new("js", "named-backref", 2, FlavorSupport.Yes, ""),
        new("js", "unicode-category", 1, FlavorSupport.No, "property escapes were added in ECMAScript 2018"), new("js", "unicode-category", 2, FlavorSupport.Yes, "needs the u flag"),
        new("js", "flag-s", 1, FlavorSupport.No, "the s flag was added in ECMAScript 2018"), new("js", "flag-s", 2, FlavorSupport.Yes, "as the s flag, not inline"),
    ];

    public static IReadOnlyList<RegexFlavorVersion> Versions(string flavorId) => versions.TryGetValue(flavorId, out var v) ? v : [];

    /// <summary>The newest version of an engine, which is the one the flavor table describes.</summary>
    public static RegexFlavorVersion Latest(string flavorId) => Versions(flavorId).OrderByDescending(v => v.Rank).First();

    /// <summary>True when the engine has more than one version to choose from.</summary>
    public static bool HasChoice(string flavorId) => Versions(flavorId).Count > 1;

    /// <summary>The version with this id for the engine, or the newest when the id is missing or unknown.</summary>
    public static RegexFlavorVersion Resolve(string flavorId, string? versionId) =>
        Versions(flavorId).FirstOrDefault(v => v.Id == versionId) ?? Latest(flavorId);

    /// <summary>How an engine at a given version treats a construct.</summary>
    public static (FlavorSupport Support, string Note) Cell(string constructId, string flavorId, string? versionId)
    {
        var version = Resolve(flavorId, versionId);
        var rule = rules.Where(r => r.Flavor == flavorId && r.Construct == constructId && r.FromRank <= version.Rank).OrderByDescending(r => r.FromRank).FirstOrDefault();
        return rule != null ? (rule.Support, rule.Note) : RegexFlavors.Construct(constructId).Flavors[flavorId];
    }

    /// <summary>The constructs whose treatment depends on the version of an engine, for the help text beside the picker.</summary>
    public static IReadOnlyList<string> Dependent(string flavorId) =>
        rules.Where(r => r.Flavor == flavorId).Select(r => RegexFlavors.Construct(r.Construct).Label).Distinct().ToList();

    /// <summary>The comparison of <see cref="RegexFlavors.Compare"/> with a chosen version per engine (an engine missing from the map uses its newest version).</summary>
    public static IReadOnlyList<RegexFlavorVerdict> Compare(string? pattern, RegexBuilderFlags flags, IEnumerable<string>? flavorIds, IReadOnlyDictionary<string, string>? chosen)
    {
        var uses = RegexFlavors.Detect(pattern, flags);
        var flavors = flavorIds == null ? RegexFlavors.Flavors : RegexFlavors.Flavors.Where(f => flavorIds.Contains(f.Id)).ToList();
        var verdicts = new List<RegexFlavorVerdict>();
        foreach (var f in flavors)
        {
            var version = Resolve(f.Id, chosen != null && chosen.TryGetValue(f.Id, out var id) ? id : null);
            var findings = new List<(RegexConstruct, FlavorSupport, string)>();
            foreach (var u in uses)
            {
                var construct = RegexFlavors.Construct(u.ConstructId);
                var (support, note) = Cell(u.ConstructId, f.Id, version.Id);
                findings.Add((construct, support, note));
            }
            var shown = HasChoice(f.Id) ? f with { Version = version.Label } : f;
            verdicts.Add(new(shown, findings.Count(x => x.Item2 == FlavorSupport.No), findings.Count(x => x.Item2 == FlavorSupport.Different), findings));
        }
        return verdicts;
    }

    /// <summary>The plain-text table at the chosen versions, the same shape as <see cref="RegexFlavors.ToText"/>.</summary>
    public static string ToText(string? pattern, RegexBuilderFlags flags, IEnumerable<string>? flavorIds, IReadOnlyDictionary<string, string>? chosen)
    {
        var verdicts = Compare(pattern, flags, flavorIds, chosen);
        var sb = new StringBuilder();
        sb.AppendLine("Pattern: " + pattern);
        sb.AppendLine("Static comparison from a hand-written table; the pattern was not run in any engine except .NET. Version differences come from documentation, not from running old engines.");
        if (verdicts.Count == 0 || verdicts[0].Findings.Count == 0) { sb.AppendLine("The pattern uses no construct the table tracks, so every flavor reads it the same way."); return sb.ToString(); }
        sb.AppendLine();
        foreach (var v in verdicts)
        {
            sb.AppendLine($"{v.Flavor.Name} ({v.Flavor.Version}): " + (v.Compatible ? "every tracked construct is supported" : $"{v.Unsupported} unsupported, {v.Different} different"));
            foreach (var (construct, support, note) in v.Findings.Where(f => f.Support != FlavorSupport.Yes))
                sb.AppendLine($"  {(support == FlavorSupport.No ? "NO " : "DIFF")} {construct.Label}" + (note.Length > 0 ? ": " + note : ""));
        }
        return sb.ToString();
    }
}
