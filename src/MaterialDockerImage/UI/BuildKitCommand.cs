using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

public enum BuildKitDestination { Load, Push, Oci, Local }
public enum BuildKitCall { Build, Check, Outline, Targets }
public enum BuildKitProvenance { Default, Off, Min, Max }
public enum BuildKitNetwork { Default, None, Host }

/// <summary>Everything the Build page can ask buildx for. Plain data so the argument list can be tested.</summary>
public sealed record BuildKitRequest
{
    public string ContextPath { get; init; } = "";
    public string Dockerfile { get; init; } = "";
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<string> Platforms { get; init; } = [];
    public string? Target { get; init; }
    public IReadOnlyList<KeyValuePair<string, string>> BuildArguments { get; init; } = [];
    public IReadOnlyList<KeyValuePair<string, string>> Labels { get; init; } = [];
    public bool NoCache { get; init; }
    public bool Pull { get; init; }
    public BuildKitDestination Destination { get; init; } = BuildKitDestination.Load;
    /// <summary>OCI archive file or local export folder, depending on the destination.</summary>
    public string? OutputPath { get; init; }
    public IReadOnlyList<string> CacheFrom { get; init; } = [];
    public IReadOnlyList<string> CacheTo { get; init; } = [];
    public IReadOnlyList<KeyValuePair<string, string>> SecretFiles { get; init; } = [];
    public IReadOnlyList<KeyValuePair<string, string>> SecretEnvironment { get; init; } = [];
    public IReadOnlyList<KeyValuePair<string, string>> NamedContexts { get; init; } = [];
    public bool SshDefault { get; init; }
    public IReadOnlyList<KeyValuePair<string, string>> SshKeys { get; init; } = [];
    public bool Sbom { get; init; }
    public BuildKitProvenance Provenance { get; init; }
    public IReadOnlyList<KeyValuePair<string, string>> Annotations { get; init; } = [];
    public BuildKitCall Call { get; init; }
    public BuildKitNetwork Network { get; init; }
    public string? ShmSize { get; init; }
    public IReadOnlyList<KeyValuePair<string, string>> Ulimits { get; init; } = [];
    public IReadOnlyList<KeyValuePair<string, string>> ExtraHosts { get; init; } = [];
    /// <summary>Metadata file path; only added for a real build call.</summary>
    public string? MetadataFile { get; init; }
}

/// <summary>
/// Builds and validates the <c>docker buildx build</c> argument list. File checks are injected so the logic stays
/// testable; the caller passes real checks for a run and permissive ones for a preview. Secret contents and
/// environment values are never read here.
/// </summary>
public static class BuildKitCommand
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);
    public static readonly string[] KnownPlatforms = ["linux/amd64", "linux/arm64", "linux/arm/v7", "linux/riscv64", "windows/amd64"];
    public static readonly string[] UlimitNames = ["core", "cpu", "data", "fsize", "locks", "memlock", "msgqueue", "nice", "nofile", "nproc", "rss", "rtprio", "rttime", "sigpending", "stack"];

    public static List<string> Create(BuildKitRequest r, Func<string, bool> fileExists, Func<string, bool> directoryExists)
    {
        var build = r.Call == BuildKitCall.Build;
        var args = new List<string> { "buildx", "build", "--progress", "plain" };
        switch (r.Call)
        {
            case BuildKitCall.Check: args.Add("--call=check"); break;
            case BuildKitCall.Outline: args.Add("--call=outline,format=json"); break;
            case BuildKitCall.Targets: args.Add("--call=targets,format=json"); break;
        }
        foreach (var tag in r.Tags) { Operand(tag, "Tag"); args.AddRange(["--tag", tag]); }
        if (build && r.Destination == BuildKitDestination.Load && r.Platforms.Count > 1)
            throw new ArgumentException("Load locally supports one platform. Choose Push, OCI archive or Export files for a multi-platform build.");
        foreach (var p in r.Platforms) if (ValidatePlatform(p) is { } problem) throw new ArgumentException(problem);
        if (r.Platforms.Count > 0) args.AddRange(["--platform", string.Join(',', r.Platforms)]);
        foreach (var (k, v) in r.BuildArguments) { if (ValidateArgName(k) is { } p) throw new ArgumentException("Build arguments: " + p); Token(v); args.AddRange(["--build-arg", k + "=" + v]); }
        foreach (var (k, v) in r.Labels) { if (k.Length == 0 || k.StartsWith('-') || k.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == '=')) throw new ArgumentException("Image labels: label keys cannot be empty, begin with a hyphen or contain spaces or '='."); Token(v); args.AddRange(["--label", k + "=" + v]); }
        if (!string.IsNullOrWhiteSpace(r.Target)) { Operand(r.Target, "Target stage"); args.AddRange(["--target", r.Target.Trim()]); }
        if (string.IsNullOrWhiteSpace(r.Dockerfile)) throw new ArgumentException("Open or save a Dockerfile first.");
        Token(r.Dockerfile);
        if (!fileExists(r.Dockerfile)) throw new ArgumentException("The Dockerfile does not exist on disk. Save it first.");
        args.AddRange(["--file", r.Dockerfile]);
        if (r.NoCache) args.Add("--no-cache");
        if (r.Pull) args.Add("--pull");
        if (build)
        {
            switch (r.Destination)
            {
                case BuildKitDestination.Load: args.Add("--load"); break;
                case BuildKitDestination.Push:
                    if (r.Tags.Count == 0) throw new ArgumentException("Registry push requires at least one explicit destination tag.");
                    args.Add("--push"); break;
                case BuildKitDestination.Oci:
                    args.AddRange(["--output", OutputSpec("oci", r.OutputPath, "Choose where to write the OCI archive.")]); break;
                case BuildKitDestination.Local:
                    if (!string.IsNullOrWhiteSpace(r.OutputPath) && !directoryExists(r.OutputPath)) throw new ArgumentException("Choose an existing folder for exported files.");
                    args.AddRange(["--output", OutputSpec("local", r.OutputPath, "Choose the folder that receives the exported files.")]); break;
            }
        }
        foreach (var spec in r.CacheFrom) args.AddRange(["--cache-from", Cache(spec)]);
        if (build) foreach (var spec in r.CacheTo) args.AddRange(["--cache-to", Cache(spec)]);
        var secretIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, path) in r.SecretFiles)
        {
            if (ValidateSecretId(id) is { } p) throw new ArgumentException(p);
            if (!secretIds.Add(id)) throw new ArgumentException("BuildKit secret identifiers must be unique.");
            Token(path);
            if (string.IsNullOrWhiteSpace(path) || path.Contains(',')) throw new ArgumentException("Choose a secret source file with no comma in its path.");
            if (!fileExists(path)) throw new ArgumentException("A selected BuildKit secret file is unavailable. Replace it or remove its row.");
            args.AddRange(["--secret", "type=file,id=" + id + ",src=" + path]);
        }
        foreach (var (id, variable) in r.SecretEnvironment)
        {
            if (ValidateSecretId(id) is { } p) throw new ArgumentException(p);
            if (!secretIds.Add(id)) throw new ArgumentException("BuildKit secret identifiers must be unique.");
            if (ValidateEnvironmentName(variable) is { } q) throw new ArgumentException(q);
            args.AddRange(["--secret", "id=" + id + ",env=" + variable]);
        }
        var contextNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, value) in r.NamedContexts)
        {
            if (ValidateContextName(name) is { } p) throw new ArgumentException("Named contexts: " + p);
            if (!contextNames.Add(name)) throw new ArgumentException("Named contexts: " + name + " is listed twice.");
            args.AddRange(["--build-context", name + "=" + ContextSource(value, directoryExists)]);
        }
        if (r.SshDefault) args.AddRange(["--ssh", "default"]);
        foreach (var (id, path) in r.SshKeys)
        {
            if (ValidateSecretId(id) is { } p) throw new ArgumentException("SSH: " + p);
            if (id == "default" && r.SshDefault) throw new ArgumentException("SSH: the id default is already forwarded from the agent.");
            Token(path);
            if (string.IsNullOrWhiteSpace(path) || path.Contains(',')) throw new ArgumentException("SSH: choose a key or socket path with no comma.");
            if (!fileExists(path)) throw new ArgumentException("SSH: the key file " + path + " does not exist.");
            args.AddRange(["--ssh", id + "=" + path]);
        }
        foreach (var (key, value) in r.Annotations)
        {
            if (ValidateAnnotationKey(key) is { } p) throw new ArgumentException("Annotations: " + p);
            Token(value);
            args.AddRange(["--annotation", key + "=" + value]);
        }
        if (build && r.Sbom) args.Add("--sbom=true");
        if (build)
            switch (r.Provenance)
            {
                case BuildKitProvenance.Off: args.Add("--provenance=false"); break;
                case BuildKitProvenance.Min: args.Add("--provenance=mode=min"); break;
                case BuildKitProvenance.Max: args.Add("--provenance=mode=max"); break;
            }
        switch (r.Network)
        {
            case BuildKitNetwork.None: args.AddRange(["--network", "none"]); break;
            case BuildKitNetwork.Host: args.AddRange(["--network", "host", "--allow", "network.host"]); break;
        }
        if (!string.IsNullOrWhiteSpace(r.ShmSize))
        {
            if (!Regex.IsMatch(r.ShmSize.Trim(), "^[1-9][0-9]*[bkmgBKMG]?$", RegexOptions.None, Timeout)) throw new ArgumentException("Shared memory size uses positive bytes or b/k/m/g units, for example 256m.");
            args.AddRange(["--shm-size", r.ShmSize.Trim()]);
        }
        foreach (var (name, value) in r.Ulimits)
        {
            if (ValidateUlimitName(name) is { } p) throw new ArgumentException("Ulimits: " + p);
            if (ValidateUlimitValue(value) is { } q) throw new ArgumentException("Ulimits: " + q);
            args.AddRange(["--ulimit", name + "=" + value.Trim()]);
        }
        foreach (var (host, address) in r.ExtraHosts)
        {
            if (ValidateHostName(host) is { } p) throw new ArgumentException("Extra hosts: " + p);
            if (ValidateHostAddress(address) is { } q) throw new ArgumentException("Extra hosts: " + q);
            args.AddRange(["--add-host", host + ":" + address.Trim()]);
        }
        if (build && !string.IsNullOrWhiteSpace(r.MetadataFile)) { Token(r.MetadataFile); args.AddRange(["--metadata-file", r.MetadataFile]); }
        if (string.IsNullOrWhiteSpace(r.ContextPath)) throw new ArgumentException("Choose an existing build context folder.");
        Token(r.ContextPath);
        if (!directoryExists(r.ContextPath)) throw new ArgumentException("Choose an existing build context folder.");
        args.Add(r.ContextPath);
        return args;
    }

    /// <summary>Formats an argument list the way a PowerShell or cmd user could paste it (double quotes where needed).</summary>
    public static string Format(IEnumerable<string> args)
    {
        var sb = new StringBuilder("docker");
        foreach (var arg in args)
        {
            sb.Append(' ');
            if (arg.Length > 0 && !arg.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'' or '&' or '|' or '<' or '>' or '^' or '(' or ')' or ';' or '`' or '$')) sb.Append(arg);
            else sb.Append('"').Append(arg.Replace("\"", "\\\"")).Append('"');
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------- validators (null means valid)

    public static string? ValidatePlatform(string value) =>
        Regex.IsMatch(value, "^[a-z0-9]+/[a-z0-9_]+(?:/[a-z0-9]+)?$", RegexOptions.None, Timeout) ? null : $"\"{value}\" is not a platform. Use os/architecture[/variant], for example linux/arm64.";
    public static string? ValidateArgName(string name) => Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.None, Timeout) ? null : "Use letters, digits and underscores, starting with a letter or underscore.";
    public static string? ValidateSecretId(string id) => Regex.IsMatch(id, @"^[A-Za-z_][A-Za-z0-9_.-]*$", RegexOptions.None, Timeout) ? null : "Ids start with a letter or underscore and use letters, digits, '.', '_' and '-'.";
    public static string? ValidateEnvironmentName(string name) => Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.None, Timeout) ? null : "Environment variable names use letters, digits and underscores.";
    public static string? ValidateContextName(string name) => Regex.IsMatch(name, @"^[A-Za-z0-9][A-Za-z0-9_.\-/:@]*$", RegexOptions.None, Timeout) ? null : "Use a stage or image name such as base or alpine:3.20.";
    public static string? ValidateAnnotationKey(string key) =>
        Regex.IsMatch(key, @"^(?:(?:manifest|index|manifest-descriptor|index-descriptor)(?:\[[A-Za-z0-9/_,-]+\])?:)?[A-Za-z0-9][A-Za-z0-9._\-/]*$", RegexOptions.None, Timeout) ? null : "Use a key such as org.opencontainers.image.title, optionally prefixed with manifest:, index: or a descriptor type.";
    public static string? ValidateUlimitName(string name) => UlimitNames.Contains(name) ? null : "Use one of " + string.Join(", ", UlimitNames) + ".";
    public static string? ValidateUlimitValue(string value) => Regex.IsMatch(value.Trim(), "^-?[0-9]+(?::-?[0-9]+)?$", RegexOptions.None, Timeout) ? null : "Use soft or soft:hard, for example 1024:2048.";
    public static string? ValidateHostName(string host) => Regex.IsMatch(host, @"^[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?$", RegexOptions.None, Timeout) ? null : "Host names use letters, digits, '.' and '-'.";
    public static string? ValidateHostAddress(string address) => address.Trim() == "host-gateway" || IPAddress.TryParse(address.Trim(), out _) ? null : "Use an IP address or host-gateway.";

    /// <summary>Normalises a named-context source: a URL scheme BuildKit understands, or an existing local folder.</summary>
    public static string ContextSource(string value, Func<string, bool> directoryExists)
    {
        var v = value.Trim();
        Token(v);
        if (v.Length == 0) throw new ArgumentException("Named contexts: enter a folder, docker-image://reference or URL.");
        foreach (var prefix in new[] { "docker-image://", "oci-layout://", "target:" })
            if (v.StartsWith(prefix, StringComparison.Ordinal))
                return v.Length > prefix.Length ? v : throw new ArgumentException("Named contexts: the source after " + prefix + " is empty.");
        if (v.StartsWith("https://", StringComparison.Ordinal) || v.StartsWith("http://", StringComparison.Ordinal) || v.StartsWith("git@", StringComparison.Ordinal)) return v;
        if (v.Contains("://", StringComparison.Ordinal)) throw new ArgumentException("Named contexts: use a folder, docker-image://, oci-layout://, https:// or git@ source.");
        if (!directoryExists(v)) throw new ArgumentException("Named contexts: the folder " + v + " does not exist.");
        return v;
    }

    public static string Cache(string spec)
    {
        Token(spec);
        if (!Regex.IsMatch(spec, @"^type=(?:inline|local|registry|gha|s3|azblob)(?:,[A-Za-z0-9_-]+=[^,\r\n]+)*$", RegexOptions.None, Timeout))
            throw new ArgumentException("Cache specification must use type=inline/local/registry/gha/s3/azblob and comma-separated key=value options.");
        return spec;
    }

    private static string OutputSpec(string type, string? path, string missing)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException(missing);
        Token(path);
        if (path.Contains(',')) throw new ArgumentException("Output paths cannot contain commas.");
        return "type=" + type + ",dest=" + path;
    }

    private static void Operand(string value, string what)
    {
        if (string.IsNullOrWhiteSpace(value) || value.TrimStart().StartsWith('-')) throw new ArgumentException(what + ": a value that does not begin with a hyphen is required.");
        Token(value);
    }

    private static void Token(string value)
    {
        if (value.Length > 32768 || value.Any(char.IsControl)) throw new ArgumentException("Values cannot contain control characters and must fit within 32 KiB.");
    }
}
