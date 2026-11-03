using System.Globalization;
using System.Text.Json;

namespace MaterialDockerImage.UI;

/// <summary>Fields read from a buildx <c>--metadata-file</c>.</summary>
public sealed record BuildKitMetadata(string? ImageDigest, string? ConfigDigest, IReadOnlyList<string> ImageNames, string? BuildRef, bool HasProvenance, IReadOnlyList<string> Keys)
{
    public static BuildKitMetadata? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            string? S(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var names = (S("image.name") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var keys = root.EnumerateObject().Select(p => p.Name).ToList();
            var digest = S("containerimage.digest");
            if (digest == null && root.TryGetProperty("containerimage.descriptor", out var d) && d.ValueKind == JsonValueKind.Object && d.TryGetProperty("digest", out var dd) && dd.ValueKind == JsonValueKind.String) digest = dd.GetString();
            return new BuildKitMetadata(digest, S("containerimage.config.digest"), names, S("buildx.build.ref"), keys.Contains("buildx.build.provenance"), keys);
        }
        catch (JsonException) { return null; }
    }
}

public sealed record BuildKitTarget(string Name, string Description, bool IsDefault, string Base, string Platform, int? Line);
public sealed record BuildKitOutlineArg(string Name, string Value, string Description);
public sealed record BuildKitOutlineItem(string Name, bool Required);
public sealed record BuildKitOutline(string Name, string Description, IReadOnlyList<BuildKitOutlineArg> Args, IReadOnlyList<BuildKitOutlineItem> Secrets, IReadOnlyList<BuildKitOutlineItem> Ssh);

/// <summary>Parsers for the JSON that <c>--call=targets,format=json</c> and <c>--call=outline,format=json</c> print.</summary>
public static class BuildKitCallResult
{
    public static IReadOnlyList<BuildKitTarget>? ParseTargets(string text)
    {
        if (FindJson(text) is not { } root) return null;
        using var doc = root;
        var r = doc.RootElement;
        var list = Get(r, "targets") is { ValueKind: JsonValueKind.Array } arr ? arr : r.ValueKind == JsonValueKind.Array ? r : default;
        if (list.ValueKind != JsonValueKind.Array) return null;
        var result = new List<BuildKitTarget>();
        foreach (var t in list.EnumerateArray())
        {
            if (t.ValueKind != JsonValueKind.Object) continue;
            result.Add(new BuildKitTarget(Str(t, "name"), Str(t, "description"), Get(t, "default") is { ValueKind: JsonValueKind.True }, Str(t, "base"), Str(t, "platform"), Line(t)));
        }
        return result;
    }

    public static BuildKitOutline? ParseOutline(string text)
    {
        if (FindJson(text) is not { } root) return null;
        using var doc = root;
        var r = doc.RootElement;
        if (r.ValueKind != JsonValueKind.Object) return null;
        var args = new List<BuildKitOutlineArg>();
        if (Get(r, "args") is { ValueKind: JsonValueKind.Array } a)
            foreach (var x in a.EnumerateArray()) if (x.ValueKind == JsonValueKind.Object) args.Add(new BuildKitOutlineArg(Str(x, "name"), Str(x, "value"), Str(x, "description")));
        return new BuildKitOutline(Str(r, "name"), Str(r, "description"), args, Items(r, "secrets"), Items(r, "ssh"));
    }

    private static List<BuildKitOutlineItem> Items(JsonElement r, string name)
    {
        var list = new List<BuildKitOutlineItem>();
        if (Get(r, name) is { ValueKind: JsonValueKind.Array } a)
            foreach (var x in a.EnumerateArray()) if (x.ValueKind == JsonValueKind.Object) list.Add(new BuildKitOutlineItem(Str(x, "name"), Get(x, "required") is { ValueKind: JsonValueKind.True }));
        return list;
    }

    private static int? Line(JsonElement t)
    {
        if (Get(t, "location") is not { ValueKind: JsonValueKind.Object } loc) return null;
        if (Get(loc, "ranges") is { ValueKind: JsonValueKind.Array } ranges)
            foreach (var range in ranges.EnumerateArray())
                if (Get(range, "start") is { ValueKind: JsonValueKind.Object } start && Get(start, "line") is { ValueKind: JsonValueKind.Number } line && line.TryGetInt32(out var n)) return n;
        return null;
    }

    /// <summary>Finds the first JSON object or array in mixed output (progress text may precede it).</summary>
    internal static JsonDocument? FindJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        foreach (var start in new[] { text.IndexOf('{'), text.IndexOf('[') }.Where(i => i >= 0).Order())
        {
            try { return JsonDocument.Parse(text[start..], new JsonDocumentOptions { MaxDepth = 64, AllowTrailingCommas = true }); }
            catch (JsonException) { }
        }
        return null;
    }

    internal static JsonElement? Get(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in e.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }

    internal static string Str(JsonElement e, string name) => Get(e, name) is { } v ? v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? "",
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => v.GetRawText(),
        _ => ""
    } : "";
}

/// <summary>One row of <c>docker buildx history ls --format json</c>.</summary>
public sealed record BuildKitHistoryRecord(string Ref, string Name, string Status, DateTimeOffset? Created, double? Seconds, int? TotalSteps, int? CachedSteps)
{
    /// <summary>Parses a JSON array or one JSON object per line. Unknown fields are ignored.</summary>
    public static IReadOnlyList<BuildKitHistoryRecord> ParseList(string text)
    {
        var result = new List<BuildKitHistoryRecord>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith('['))
        {
            using var doc = JsonDocument.Parse(trimmed);
            foreach (var e in doc.RootElement.EnumerateArray()) if (From(e) is { } r) result.Add(r);
            return result;
        }
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith('{')) continue;
            using var doc = JsonDocument.Parse(line);
            if (From(doc.RootElement) is { } r) result.Add(r);
        }
        return result;
    }

    private static BuildKitHistoryRecord? From(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        var reference = BuildKitCallResult.Str(e, "ref");
        if (reference.Length == 0) reference = BuildKitCallResult.Str(e, "id");
        var created = Time(BuildKitCallResult.Str(e, "created_at"));
        var completed = Time(BuildKitCallResult.Str(e, "completed_at"));
        double? seconds = created != null && completed != null ? Math.Max(0, (completed.Value - created.Value).TotalSeconds) : null;
        if (seconds == null && double.TryParse(BuildKitCallResult.Str(e, "duration"), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) seconds = d;
        return new BuildKitHistoryRecord(reference, BuildKitCallResult.Str(e, "name"), BuildKitCallResult.Str(e, "status"), created, seconds, Int(e, "total_steps"), Int(e, "cached_steps"));
    }

    private static DateTimeOffset? Time(string value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) && t.Year > 1 ? t : null;
    private static int? Int(JsonElement e, string name) => int.TryParse(BuildKitCallResult.Str(e, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    /// <summary>True when an error text means this Buildx has no history command or JSON format.</summary>
    public static bool IsUnsupported(string error)
    {
        var e = error.ToLowerInvariant();
        return e.Contains("unknown command") || e.Contains("unknown flag") || e.Contains("unknown shorthand") || e.Contains("is not a docker command") || e.Contains("unsupported") && e.Contains("history");
    }
}
