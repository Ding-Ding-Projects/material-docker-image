using System.Text.Json;

namespace MaterialDockerImage.UI;

/// <summary>One entry of an OCI image index or Docker manifest list.</summary>
public sealed record ImageManifestEntry(
    string MediaType,
    string Digest,
    long Size,
    string Os,
    string Architecture,
    string Variant,
    string OsVersion,
    IReadOnlyDictionary<string, string> Annotations)
{
    public const string ReferenceTypeKey = "vnd.docker.reference.type";
    public const string ReferenceDigestKey = "vnd.docker.reference.digest";

    /// <summary>True for BuildKit attestation manifests (provenance and SBOM), which are not runnable platforms.</summary>
    public bool IsAttestation => Annotations.TryGetValue(ReferenceTypeKey, out var t) && t == "attestation-manifest";

    /// <summary>The platform manifest digest an attestation describes, when annotated.</summary>
    public string AttestsDigest => Annotations.TryGetValue(ReferenceDigestKey, out var d) ? d : "";

    public string Platform
    {
        get
        {
            if (Os.Length == 0 && Architecture.Length == 0) return "unknown";
            var p = Os + "/" + Architecture + (Variant.Length > 0 ? "/" + Variant : "");
            return OsVersion.Length > 0 ? p + " (" + OsVersion + ")" : p;
        }
    }
}

/// <summary>What a registry returned for a reference: an index with platforms and attestations, or a single manifest.</summary>
public sealed record ImageManifestReport(
    bool IsIndex,
    string MediaType,
    IReadOnlyList<ImageManifestEntry> Platforms,
    IReadOnlyList<ImageManifestEntry> Attestations,
    IReadOnlyDictionary<string, string> Annotations,
    string ConfigDigest,
    int LayerCount,
    long LayerBytes)
{
    public string KindText => IsIndex
        ? (MediaType.Contains("docker.distribution.manifest.list", StringComparison.Ordinal) ? "Docker manifest list" : "OCI image index")
        : (MediaType.Contains("docker.distribution.manifest.v2", StringComparison.Ordinal) ? "Docker image manifest (single platform)" : MediaType.Length > 0 ? "Image manifest (single platform)" : "Manifest (single platform)");
}

/// <summary>Parses the raw JSON printed by <c>docker buildx imagetools inspect --raw</c>.</summary>
public static class ImageManifest
{
    public static ImageManifestReport Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException e) { throw new FormatException("The manifest is not valid JSON: " + e.Message, e); }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new FormatException("The manifest JSON is not an object.");
            var mediaType = ScoutReport.Str(root, "mediaType");
            var annotations = Map(root, "annotations");
            if (root.TryGetProperty("manifests", out var manifests) && manifests.ValueKind == JsonValueKind.Array)
            {
                var entries = manifests.EnumerateArray().Select(Entry).ToList();
                return new ImageManifestReport(true, mediaType, entries.Where(e => !e.IsAttestation).ToList(), entries.Where(e => e.IsAttestation).ToList(), annotations, "", 0, 0);
            }
            if (root.TryGetProperty("layers", out var layers) && layers.ValueKind == JsonValueKind.Array)
            {
                var config = ScoutReport.Path(root, "config") is { } c ? ScoutReport.Str(c, "digest") : "";
                long total = 0;
                foreach (var layer in layers.EnumerateArray()) if (layer.TryGetProperty("size", out var s) && s.TryGetInt64(out var n)) total += n;
                return new ImageManifestReport(false, mediaType, [], [], annotations, config, layers.GetArrayLength(), total);
            }
            throw new FormatException("The JSON is neither an image index (\"manifests\") nor an image manifest (\"layers\").");
        }
    }

    private static ImageManifestEntry Entry(JsonElement e)
    {
        var platform = ScoutReport.Path(e, "platform");
        string P(string name) => platform is { } p ? ScoutReport.Str(p, name) : "";
        var size = e.TryGetProperty("size", out var s) && s.TryGetInt64(out var n) ? n : 0;
        return new ImageManifestEntry(ScoutReport.Str(e, "mediaType"), ScoutReport.Str(e, "digest"), size, P("os"), P("architecture"), P("variant"), P("os.version"), Map(e, "annotations"));
    }

    private static IReadOnlyDictionary<string, string> Map(JsonElement element, string name)
    {
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (element.TryGetProperty(name, out var obj) && obj.ValueKind == JsonValueKind.Object)
            foreach (var p in obj.EnumerateObject()) map[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.ToString();
        return map;
    }

    /// <summary>Shortens sha256:abcdef… digests for display.</summary>
    public static string ShortDigest(string digest, int hex = 12)
    {
        var colon = digest.IndexOf(':');
        var value = colon >= 0 ? digest[(colon + 1)..] : digest;
        return (colon >= 0 ? digest[..(colon + 1)] : "") + (value.Length > hex ? value[..hex] : value);
    }
}
