using System.Globalization;
using System.Text.Json;

namespace MaterialDockerImage.UI;

/// <summary>Facts read from <c>docker image inspect</c> that are useful when comparing two images. Environment values are never kept.</summary>
public sealed record ImageCompareFacts(
    string Reference,
    string Id,
    long Size,
    int Layers,
    string Platform,
    string Created,
    string User,
    string WorkingDir,
    string Entrypoint,
    string Command,
    IReadOnlyList<string> EnvNames,
    IReadOnlyList<string> Ports,
    IReadOnlyDictionary<string, string> Labels);

public enum ImageDiffKind { Same, Changed, OnlyLeft, OnlyRight }

public sealed record ImageDiffRow(string Section, string Name, string Left, string Right, ImageDiffKind Kind);

/// <summary>Builds a side-by-side comparison of two inspected images.</summary>
public static class ImageCompare
{
    /// <summary>Reads the first object of a docker image inspect array.</summary>
    public static ImageCompareFacts FromInspect(string json, string reference)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            if (root.GetArrayLength() == 0) throw new FormatException("docker image inspect returned no image.");
            root = root[0];
        }
        if (root.ValueKind != JsonValueKind.Object) throw new FormatException("docker image inspect returned unexpected JSON.");
        var config = ScoutReport.Path(root, "Config");
        string C(string name) => config is { } c ? ScoutReport.Str(c, name) : "";
        IReadOnlyList<string> Array(string name) => config is { } c && c.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : x.ToString()).ToList()
            : [];
        var env = Array("Env").Select(e => e.Split('=')[0]).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var ports = config is { } cp && cp.TryGetProperty("ExposedPorts", out var p) && p.ValueKind == JsonValueKind.Object ? p.EnumerateObject().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal).ToList() : new List<string>();
        var labels = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (config is { } cl && cl.TryGetProperty("Labels", out var l) && l.ValueKind == JsonValueKind.Object)
            foreach (var x in l.EnumerateObject()) labels[x.Name] = x.Value.ValueKind == JsonValueKind.String ? x.Value.GetString() ?? "" : x.Value.ToString();
        var layers = ScoutReport.Path(root, "RootFS", "Layers") is { ValueKind: JsonValueKind.Array } rl ? rl.GetArrayLength() : 0;
        var size = root.TryGetProperty("Size", out var s) && s.TryGetInt64(out var n) ? n : 0;
        var platform = ScoutReport.Str(root, "Os") + "/" + ScoutReport.Str(root, "Architecture") + (ScoutReport.Str(root, "Variant") is { Length: > 0 } v ? "/" + v : "");
        string Json(IReadOnlyList<string> list) => list.Count == 0 ? "" : JsonSerializer.Serialize(list);
        return new ImageCompareFacts(reference, ScoutReport.Str(root, "Id"), size, layers, platform, ScoutReport.Str(root, "Created"), C("User"), C("WorkingDir"),
            Json(Array("Entrypoint")), Json(Array("Cmd")), env, ports, labels);
    }

    public static IReadOnlyList<ImageDiffRow> Diff(ImageCompareFacts left, ImageCompareFacts right)
    {
        var rows = new List<ImageDiffRow>();
        void Scalar(string name, string a, string b) => rows.Add(new ImageDiffRow("Summary", name, a, b, a == b ? ImageDiffKind.Same : ImageDiffKind.Changed));
        Scalar("Size", left.Size.ToString(CultureInfo.InvariantCulture), right.Size.ToString(CultureInfo.InvariantCulture));
        Scalar("Layers", left.Layers.ToString(CultureInfo.InvariantCulture), right.Layers.ToString(CultureInfo.InvariantCulture));
        Scalar("Platform", left.Platform, right.Platform);
        Scalar("Created", left.Created, right.Created);
        Scalar("User", left.User, right.User);
        Scalar("Working directory", left.WorkingDir, right.WorkingDir);
        Scalar("Entrypoint", left.Entrypoint, right.Entrypoint);
        Scalar("Command", left.Command, right.Command);
        Sets("Environment names", left.EnvNames, right.EnvNames, rows);
        Sets("Exposed ports", left.Ports, right.Ports, rows);
        foreach (var key in left.Labels.Keys.Union(right.Labels.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            var inLeft = left.Labels.TryGetValue(key, out var a);
            var inRight = right.Labels.TryGetValue(key, out var b);
            var kind = inLeft && inRight ? (a == b ? ImageDiffKind.Same : ImageDiffKind.Changed) : inLeft ? ImageDiffKind.OnlyLeft : ImageDiffKind.OnlyRight;
            rows.Add(new ImageDiffRow("Labels", key, a ?? "", b ?? "", kind));
        }
        return rows;
    }

    private static void Sets(string section, IReadOnlyList<string> left, IReadOnlyList<string> right, List<ImageDiffRow> rows)
    {
        var l = left.ToHashSet(StringComparer.Ordinal);
        var r = right.ToHashSet(StringComparer.Ordinal);
        foreach (var name in l.Union(r).OrderBy(x => x, StringComparer.Ordinal))
        {
            var kind = l.Contains(name) && r.Contains(name) ? ImageDiffKind.Same : l.Contains(name) ? ImageDiffKind.OnlyLeft : ImageDiffKind.OnlyRight;
            rows.Add(new ImageDiffRow(section, name, l.Contains(name) ? "present" : "", r.Contains(name) ? "present" : "", kind));
        }
    }
}
