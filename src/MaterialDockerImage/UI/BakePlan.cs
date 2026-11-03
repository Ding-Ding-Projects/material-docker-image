using System.Text.Json;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

public sealed record BakeGroup(string Name, IReadOnlyList<string> Targets);

public sealed record BakeTarget(string Name, string Context, string Dockerfile, string Stage, IReadOnlyList<string> Tags, IReadOnlyList<string> Platforms,
    IReadOnlyList<KeyValuePair<string, string>> Args, IReadOnlyList<KeyValuePair<string, string>> Labels, IReadOnlyList<string> Outputs, string Json);

/// <summary>
/// The resolved plan printed by <c>docker buildx bake --print</c>: groups and fully resolved targets. Pure parsing so
/// it can be tested without an engine.
/// </summary>
public sealed record BakePlan(IReadOnlyList<BakeGroup> Groups, IReadOnlyList<BakeTarget> Targets)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>File names bake looks for, in its own lookup order.</summary>
    public static readonly string[] DefaultFiles = ["compose.yaml", "compose.yml", "docker-compose.yml", "docker-compose.yaml", "docker-bake.json", "docker-bake.hcl", "docker-bake.override.json", "docker-bake.override.hcl"];

    public static BakePlan Parse(string text)
    {
        var start = text.IndexOf('{');
        if (start < 0) throw new FormatException("bake --print did not return a JSON plan.");
        using var doc = JsonDocument.Parse(text[start..], new JsonDocumentOptions { MaxDepth = 64, AllowTrailingCommas = true });
        var root = doc.RootElement;
        var groups = new List<BakeGroup>();
        if (BuildKitCallResult.Get(root, "group") is { ValueKind: JsonValueKind.Object } g)
            foreach (var p in g.EnumerateObject())
                groups.Add(new BakeGroup(p.Name, Strings(p.Value, "targets")));
        var targets = new List<BakeTarget>();
        if (BuildKitCallResult.Get(root, "target") is { ValueKind: JsonValueKind.Object } t)
            foreach (var p in t.EnumerateObject())
            {
                var v = p.Value;
                targets.Add(new BakeTarget(p.Name, BuildKitCallResult.Str(v, "context"), BuildKitCallResult.Str(v, "dockerfile"), BuildKitCallResult.Str(v, "target"),
                    Strings(v, "tags"), Strings(v, "platforms"), Map(v, "args"), Map(v, "labels"), Outputs(v), JsonSerializer.Serialize(v, Indented)));
            }
        return new BakePlan(groups, targets);
    }

    /// <summary>Merges a later plan into this one (targets and groups by name; later definitions win).</summary>
    public BakePlan Merge(BakePlan other)
    {
        var groups = Groups.ToDictionary(x => x.Name, StringComparer.Ordinal);
        foreach (var g in other.Groups) groups[g.Name] = g;
        var targets = Targets.ToDictionary(x => x.Name, StringComparer.Ordinal);
        foreach (var t in other.Targets) targets[t.Name] = t;
        return new BakePlan(groups.Values.OrderBy(x => x.Name == "default" ? 0 : 1).ThenBy(x => x.Name, StringComparer.Ordinal).ToList(), targets.Values.OrderBy(x => x.Name, StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// Reads target and group names from <c>bake --list=targets</c> output, either a JSON array of objects with a name
    /// or the text table whose first column is the name.
    /// </summary>
    public static IReadOnlyList<string> ParseNames(string text)
    {
        var names = new List<string>();
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith('['))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                foreach (var e in doc.RootElement.EnumerateArray())
                {
                    var name = e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : BuildKitCallResult.Str(e, "name");
                    if (IsName(name) && !names.Contains(name)) names.Add(name);
                }
                return names;
            }
            catch (JsonException) { }
        }
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var first = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (first is "TARGET" or "GROUP" or "NAME" || !IsName(first) || names.Contains(first)) continue;
            names.Add(first);
        }
        return names;
    }

    public static bool IsName(string name) => Regex.IsMatch(name, "^[A-Za-z0-9_-]+$", RegexOptions.None, Timeout);

    /// <summary>Validates a <c>--set</c> key such as <c>app.platform</c>, <c>*.args.VERSION</c> or <c>app.output</c>.</summary>
    public static string? ValidateSetKey(string key) =>
        Regex.IsMatch(key, @"^(?:\*|[A-Za-z0-9_-]+\*?)\.[a-z-]+(?:\.[A-Za-z0-9_.-]+)?$", RegexOptions.None, Timeout) ? null : "Use target.key or target.key.name, for example app.platform or *.args.VERSION.";

    private static IReadOnlyList<string> Strings(JsonElement e, string name)
    {
        if (BuildKitCallResult.Get(e, name) is not { ValueKind: JsonValueKind.Array } a) return [];
        return a.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? "").ToList();
    }

    private static IReadOnlyList<KeyValuePair<string, string>> Map(JsonElement e, string name)
    {
        if (BuildKitCallResult.Get(e, name) is not { ValueKind: JsonValueKind.Object } o) return [];
        return o.EnumerateObject().Select(p => new KeyValuePair<string, string>(p.Name, p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.ValueKind == JsonValueKind.Null ? "" : p.Value.GetRawText())).ToList();
    }

    /// <summary>Outputs are strings in older Buildx and objects (type, dest, …) in newer releases.</summary>
    private static IReadOnlyList<string> Outputs(JsonElement e)
    {
        if (BuildKitCallResult.Get(e, "output") is not { ValueKind: JsonValueKind.Array } a) return [];
        var list = new List<string>();
        foreach (var x in a.EnumerateArray())
        {
            if (x.ValueKind == JsonValueKind.String) list.Add(x.GetString() ?? "");
            else if (x.ValueKind == JsonValueKind.Object) list.Add(string.Join(",", x.EnumerateObject().Select(p => p.Name + "=" + (p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText()))));
        }
        return list;
    }
}
