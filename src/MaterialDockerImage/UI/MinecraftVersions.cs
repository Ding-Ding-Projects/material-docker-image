using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

public enum MinecraftEdition { Java, Bedrock }

/// <summary>One entry from Mojang's public version manifest.</summary>
public sealed record MinecraftVersionEntry(string Id, string Type, DateTimeOffset? ReleaseTime)
{
    public bool IsRelease => Type == "release";
    public string DateText => ReleaseTime?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "date unknown";
}

/// <summary>Parsed manifest: the latest release and snapshot ids plus every listed version, newest first as published.</summary>
public sealed record MinecraftManifest(string? LatestRelease, string? LatestSnapshot, IReadOnlyList<MinecraftVersionEntry> Versions);

/// <summary>
/// Version names, validation and runtime selection for the Minecraft server manager. Pure logic with no WPF references
/// so it can be unit tested.
/// </summary>
public static class MinecraftVersions
{
    /// <summary>Public Mojang version manifest. Fetched on request through a short-lived curl container.</summary>
    public const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    /// <summary>Pinned curl image used to fetch the manifest through the engine (the app itself does not open network connections).</summary>
    public const string CurlImage = "curlimages/curl:8.10.1";
    public static readonly IReadOnlyList<string> JavaKeywords = ["LATEST", "SNAPSHOT"];
    public static readonly IReadOnlyList<string> BedrockKeywords = ["LATEST", "PREVIEW"];

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);
    // Releases (1.20.4, 26.1, 26.1.2), pre-releases and release candidates in both naming styles (1.21-pre4, 26.3-pre-3,
    // 1.21.11-rc3, 26.3-rc-3), the newer "-snapshot-N" style and the old "1.14 Pre-Release 5" style.
    private static readonly Regex JavaNumbered = new(@"^\d{1,2}\.\d{1,2}(\.\d{1,2})?(-(pre|rc)-?\d{1,2}|-snapshot-\d{1,2}| Pre-Release \d{1,2})?$", RegexOptions.CultureInvariant, Timeout);
    // Weekly snapshots such as 24w14a or 25w36b.
    private static readonly Regex JavaWeekly = new(@"^(\d{2})w(\d{2})[a-z]$", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex Bedrock = new(@"^\d{1,2}\.\d{1,3}\.\d{1,3}(\.\d{1,3})?$", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex Leading = new(@"^(\d{1,2})\.(\d{1,2})(?:\.(\d{1,2}))?", RegexOptions.CultureInvariant, Timeout);

    /// <summary>Returns null when the text is LATEST, SNAPSHOT or a well-formed Java Edition version id; otherwise the problem.</summary>
    public static string? ValidateJava(string? version)
    {
        var v = (version ?? "").Trim();
        if (v.Length == 0) return "Choose LATEST, SNAPSHOT or type a version such as 1.21.4.";
        if (JavaKeywords.Contains(v.ToUpperInvariant())) return null;
        if (JavaNumbered.IsMatch(v) || JavaWeekly.IsMatch(v)) return null;
        return "Use a Java Edition version id such as 1.21.4, 1.20.5-pre1, 1.21-rc1, 26.1-snapshot-2 or 24w14a.";
    }

    /// <summary>Returns null when the text is LATEST, PREVIEW or a numeric Bedrock version such as 1.21.44.01.</summary>
    public static string? ValidateBedrock(string? version)
    {
        var v = (version ?? "").Trim();
        if (v.Length == 0) return "Choose LATEST, PREVIEW or type a version such as 1.21.44.01.";
        if (BedrockKeywords.Contains(v.ToUpperInvariant())) return null;
        return Bedrock.IsMatch(v) ? null : "Use a Bedrock version such as 1.21.44.01 (three or four numbers separated by dots).";
    }

    public static string? Validate(MinecraftEdition edition, string? version) => edition == MinecraftEdition.Java ? ValidateJava(version) : ValidateBedrock(version);

    /// <summary>
    /// Image tag of itzg/minecraft-server whose bundled Java runtime suits the version. LATEST and SNAPSHOT use the
    /// image's own default ("latest"). Versions from 26.1 (year-based numbering) need Java 25; 1.20.5 and newer need
    /// Java 21; 1.17 to 1.20.4 need Java 17; older versions run on Java 8. Weekly snapshots follow the same boundaries
    /// by their year and week (24w14a introduced the Java 21 requirement, 21w19a the Java 16+ requirement).
    /// </summary>
    public static string JavaRuntimeTag(string? version)
    {
        var v = (version ?? "").Trim();
        if (v.Length == 0 || JavaKeywords.Contains(v.ToUpperInvariant())) return "latest";
        var weekly = JavaWeekly.Match(v);
        if (weekly.Success)
        {
            var year = int.Parse(weekly.Groups[1].Value, CultureInfo.InvariantCulture);
            var week = int.Parse(weekly.Groups[2].Value, CultureInfo.InvariantCulture);
            var key = year * 100 + week;
            if (year >= 26) return "java25";
            if (key >= 2414) return "java21";
            if (key >= 2119) return "java17";
            return "java8";
        }
        var m = Leading.Match(v);
        if (!m.Success) return "latest";
        var major = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var minor = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        var patch = m.Groups[3].Success ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
        if (major >= 26) return "java25";
        if (major != 1) return "latest";
        if (minor > 20 || (minor == 20 && patch >= 5)) return "java21";
        if (minor >= 17) return "java17";
        return "java8";
    }

    /// <summary>Full image reference for a server of this edition and version.</summary>
    public static string Image(MinecraftEdition edition, string? version) =>
        edition == MinecraftEdition.Bedrock ? "itzg/minecraft-bedrock-server:latest" : "itzg/minecraft-server:" + JavaRuntimeTag(version);

    /// <summary>Arguments that fetch the manifest through a short-lived container.</summary>
    public static IReadOnlyList<string> ManifestFetchArguments() => ["run", "--rm", CurlImage, "-fsSL", ManifestUrl];

    /// <summary>
    /// Parses Mojang's version_manifest_v2.json. Unknown fields are ignored; entries without an id are skipped. Throws
    /// <see cref="FormatException"/> when the document is not a manifest.
    /// </summary>
    public static MinecraftManifest ParseManifest(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("versions", out var versions) || versions.ValueKind != JsonValueKind.Array)
                throw new FormatException("The response is not a Minecraft version manifest (no versions array).");
            string? latestRelease = null, latestSnapshot = null;
            if (root.TryGetProperty("latest", out var latest) && latest.ValueKind == JsonValueKind.Object)
            {
                latestRelease = Str(latest, "release");
                latestSnapshot = Str(latest, "snapshot");
            }
            var list = new List<MinecraftVersionEntry>();
            foreach (var v in versions.EnumerateArray())
            {
                if (v.ValueKind != JsonValueKind.Object) continue;
                var id = Str(v, "id");
                if (string.IsNullOrWhiteSpace(id)) continue;
                var type = Str(v, "type") ?? "unknown";
                DateTimeOffset? time = DateTimeOffset.TryParse(Str(v, "releaseTime"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null;
                list.Add(new MinecraftVersionEntry(id, type, time));
            }
            return new MinecraftManifest(latestRelease, latestSnapshot, list);
        }
        catch (JsonException e) { throw new FormatException("The version manifest is not valid JSON: " + e.Message, e); }
    }

    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
