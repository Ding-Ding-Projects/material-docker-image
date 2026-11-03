using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>
/// One RUN --mount entry. Only the fields that apply to <see cref="Type"/> are rendered; see
/// <see cref="RunMounts.OptionsFor"/> for the option list of each type.
/// </summary>
public sealed class RunMount
{
    /// <summary>cache, bind, tmpfs, secret or ssh.</summary>
    public string Type { get; set; } = RunMounts.Cache;
    public string Target { get; set; } = "";
    public string Id { get; set; } = "";
    /// <summary>Cache sharing mode: shared, private or locked (empty means the default, shared).</summary>
    public string Sharing { get; set; } = "";
    /// <summary>Build stage, named context or image used as the mount source (cache and bind).</summary>
    public string From { get; set; } = "";
    /// <summary>Path inside <see cref="From"/> (or the build context for bind mounts).</summary>
    public string Source { get; set; } = "";
    public string Mode { get; set; } = "";
    public string Uid { get; set; } = "";
    public string Gid { get; set; } = "";
    /// <summary>tmpfs size limit, for example 64m.</summary>
    public string Size { get; set; } = "";
    /// <summary>Secret exposed as this environment variable (Dockerfile 1.10 and later).</summary>
    public string Env { get; set; } = "";
    /// <summary>Cache mount is read-only.</summary>
    public bool ReadOnly { get; set; }
    /// <summary>Bind mount allows writes (discarded after the step).</summary>
    public bool ReadWrite { get; set; }
    /// <summary>Secret or ssh mount fails the step when the value is not provided.</summary>
    public bool Required { get; set; }

    public RunMount Clone() => (RunMount)MemberwiseClone();
}

/// <summary>Pure rendering, parsing and validation of RUN --mount values.</summary>
public static class RunMounts
{
    public const string Cache = "cache", Bind = "bind", Tmpfs = "tmpfs", Secret = "secret", Ssh = "ssh";
    public static readonly IReadOnlyList<string> Types = [Cache, Bind, Tmpfs, Secret, Ssh];
    public static readonly IReadOnlyList<string> SharingModes = ["", "shared", "private", "locked"];
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>The option names each type accepts, in the order they are rendered.</summary>
    public static IReadOnlyList<string> OptionsFor(string type) => type switch
    {
        Cache => ["target", "id", "sharing", "from", "source", "mode", "uid", "gid", "readonly"],
        Bind => ["target", "source", "from", "rw"],
        Tmpfs => ["target", "size"],
        Secret => ["id", "target", "env", "required", "mode", "uid", "gid"],
        Ssh => ["id", "target", "required", "mode", "uid", "gid"],
        _ => []
    };

    public static void Normalize(RunMount m)
    {
        m.Type ??= Cache; m.Target ??= ""; m.Id ??= ""; m.Sharing ??= ""; m.From ??= ""; m.Source ??= "";
        m.Mode ??= ""; m.Uid ??= ""; m.Gid ??= ""; m.Size ??= ""; m.Env ??= "";
    }

    /// <summary>A new mount of the given type with useful defaults.</summary>
    public static RunMount Create(string type) => type switch
    {
        Bind => new RunMount { Type = Bind, Source = "package.json", Target = "package.json" },
        Tmpfs => new RunMount { Type = Tmpfs, Target = "/tmp", Size = "64m" },
        Secret => new RunMount { Type = Secret, Id = "npmrc", Target = "/root/.npmrc" },
        Ssh => new RunMount { Type = Ssh },
        _ => new RunMount { Type = Cache, Target = "/root/.cache" }
    };

    /// <summary>The value after --mount=, for example type=cache,target=/root/.npm,sharing=locked.</summary>
    public static string Render(RunMount m)
    {
        var type = m.Type.Trim().ToLowerInvariant();
        var sb = new StringBuilder("type=").Append(type);
        void Add(string key, string value) { if (value.Trim().Length > 0) sb.Append(',').Append(key).Append('=').Append(value.Trim()); }
        foreach (var option in OptionsFor(type))
        {
            switch (option)
            {
                case "target": Add("target", m.Target); break;
                case "id": Add("id", m.Id); break;
                case "sharing": Add("sharing", m.Sharing); break;
                case "from": Add("from", m.From); break;
                case "source": Add("source", m.Source); break;
                case "mode": Add("mode", m.Mode); break;
                case "uid": Add("uid", m.Uid); break;
                case "gid": Add("gid", m.Gid); break;
                case "size": Add("size", m.Size); break;
                case "env": Add("env", m.Env); break;
                case "readonly": if (m.ReadOnly) sb.Append(",readonly"); break;
                case "rw": if (m.ReadWrite) sb.Append(",rw"); break;
                case "required": if (m.Required) sb.Append(",required=true"); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>One-line description for the mount editor and hints.</summary>
    public static string Describe(RunMount m) => m.Type.Trim().ToLowerInvariant() switch
    {
        Cache => "Cache " + Or(m.Target, "(no target)") + (m.Sharing.Length > 0 ? " · " + m.Sharing : ""),
        Bind => "Bind " + Or(m.Source, "context root") + (m.From.Length > 0 ? " from " + m.From : "") + " → " + Or(m.Target, "(no target)"),
        Tmpfs => "tmpfs " + Or(m.Target, "(no target)") + (m.Size.Length > 0 ? " · " + m.Size : ""),
        Secret => "Secret " + Or(m.Id, Or(m.Target, "(no id)")) + (m.Env.Length > 0 ? " as $" + m.Env : ""),
        Ssh => "SSH agent " + Or(m.Id, "default"),
        _ => m.Type
    };
    private static string Or(string value, string fallback) => value.Trim().Length > 0 ? value.Trim() : fallback;

    /// <summary>
    /// Parses the value after --mount=. Accepts the documented aliases (dst, destination, src, ro, readwrite) and
    /// bare boolean options. Returns null for unknown types, unknown or duplicate options and quoted values, so the
    /// caller keeps the instruction as raw source.
    /// </summary>
    public static RunMount? Parse(string value)
    {
        if (value.Length == 0 || value.Contains('"') || value.Contains('\'')) return null;
        var parts = value.Split(',');
        var mount = new RunMount { Type = Bind };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var typePart = parts.FirstOrDefault(p => p.StartsWith("type=", StringComparison.OrdinalIgnoreCase));
        if (typePart != null) mount.Type = typePart[5..].ToLowerInvariant();
        if (!Types.Contains(mount.Type)) return null;
        var allowed = OptionsFor(mount.Type);
        foreach (var part in parts)
        {
            if (part.Length == 0) return null;
            var eq = part.IndexOf('=');
            var key = (eq < 0 ? part : part[..eq]).ToLowerInvariant();
            var hasValue = eq >= 0;
            var v = eq < 0 ? "" : part[(eq + 1)..];
            key = key switch { "dst" or "destination" => "target", "src" => "source", "ro" => "readonly", "readwrite" => "rw", _ => key };
            if (!seen.Add(key)) return null;
            if (key == "type") continue;
            if (!allowed.Contains(key)) return null;
            switch (key)
            {
                case "readonly": case "rw": case "required":
                    var flag = !hasValue || v.Length == 0 ? true : ParseBool(v);
                    if (flag == null) return null;
                    if (key == "readonly") mount.ReadOnly = flag.Value; else if (key == "rw") mount.ReadWrite = flag.Value; else mount.Required = flag.Value;
                    break;
                default:
                    if (!hasValue || v.Length == 0) return null;
                    switch (key)
                    {
                        case "target": mount.Target = v; break;
                        case "id": mount.Id = v; break;
                        case "sharing": if (v is not ("shared" or "private" or "locked")) return null; mount.Sharing = v; break;
                        case "from": mount.From = v; break;
                        case "source": mount.Source = v; break;
                        case "mode": mount.Mode = v; break;
                        case "uid": mount.Uid = v; break;
                        case "gid": mount.Gid = v; break;
                        case "size": mount.Size = v; break;
                        case "env": mount.Env = v; break;
                        default: return null;
                    }
                    break;
            }
        }
        return mount;
    }

    private static bool? ParseBool(string value) => value.ToLowerInvariant() switch { "true" or "1" => true, "false" or "0" => false, _ => null };

    /// <summary>Problems with one mount; empty when it renders to a valid --mount value.</summary>
    public static IReadOnlyList<string> Validate(RunMount m)
    {
        var problems = new List<string>();
        var type = m.Type.Trim().ToLowerInvariant();
        if (!Types.Contains(type)) { problems.Add($"Mount type \"{m.Type}\" is not one of cache, bind, tmpfs, secret or ssh."); return problems; }
        var label = type + " mount";
        foreach (var (name, value) in new[] { ("target", m.Target), ("id", m.Id), ("from", m.From), ("source", m.Source), ("mode", m.Mode), ("uid", m.Uid), ("gid", m.Gid), ("size", m.Size), ("env", m.Env) })
            if (value.Any(c => c is ',' or '"' or '\'' || char.IsWhiteSpace(c) || char.IsControl(c))) problems.Add($"The {label} {name} cannot contain commas, quotes or spaces.");
        if (type is Cache or Bind or Tmpfs && m.Target.Trim().Length == 0) problems.Add($"Enter a target path for the {label}.");
        if (type == Secret && m.Id.Trim().Length == 0 && m.Target.Trim().Length == 0) problems.Add("Enter a secret id (or a target path whose file name becomes the id).");
        if (m.Id.Trim().Length > 0 && type is Secret or Ssh or Cache && !Regex.IsMatch(m.Id.Trim(), @"^([A-Za-z0-9_][A-Za-z0-9_.\-/]*|\$\{?[A-Za-z_][A-Za-z0-9_]*\}?)$", RegexOptions.None, RegexTimeout))
            problems.Add($"The {label} id \"{m.Id.Trim()}\" may use letters, digits, '.', '_', '-' and '/'.");
        if (m.Mode.Trim().Length > 0 && !Regex.IsMatch(m.Mode.Trim(), "^0?[0-7]{3,4}$", RegexOptions.None, RegexTimeout)) problems.Add($"The {label} mode uses octal notation, for example 0755.");
        foreach (var (name, value) in new[] { ("uid", m.Uid), ("gid", m.Gid) })
            if (value.Trim().Length > 0 && !Regex.IsMatch(value.Trim(), @"^(\d{1,10}|\$\{?[A-Za-z_][A-Za-z0-9_]*\}?)$", RegexOptions.None, RegexTimeout)) problems.Add($"The {label} {name} is a number.");
        if (type == Tmpfs && m.Size.Trim().Length > 0 && !Regex.IsMatch(m.Size.Trim(), @"^\d+(\.\d+)?([kKmMgGtT][bB]?|[bB])?$", RegexOptions.None, RegexTimeout)) problems.Add("The tmpfs size is a number with an optional unit, for example 64m or 1g.");
        if (type == Secret && m.Env.Trim().Length > 0 && !DockerfileModel.IsVariableName(m.Env)) problems.Add("The secret environment variable name uses letters, digits and underscores.");
        if (type == Cache && m.Sharing.Trim().Length > 0 && m.Sharing.Trim() is not ("shared" or "private" or "locked")) problems.Add("Cache sharing is shared, private or locked.");
        return problems;
    }
}

public enum AddSourceKind { Local, Url, Git }

/// <summary>Classifies ADD sources the way BuildKit does: Git repositories, HTTP(S) URLs, or local paths.</summary>
public static class AddSourceKinds
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public static AddSourceKind Of(string source)
    {
        var s = source.Trim();
        if (Regex.IsMatch(s, @"^(git@[^:\s]+:|git://|ssh://)", RegexOptions.IgnoreCase, RegexTimeout)) return AddSourceKind.Git;
        if (Regex.IsMatch(s, @"^https?://", RegexOptions.IgnoreCase, RegexTimeout))
            return Regex.IsMatch(s, @"\.git(#.*)?$|\.git/|#[^/]*$", RegexOptions.IgnoreCase, RegexTimeout) && !Regex.IsMatch(s, @"\.(tar|tgz|gz|zip|xz|bz2|zst)(#|$)", RegexOptions.IgnoreCase, RegexTimeout) ? AddSourceKind.Git : AddSourceKind.Url;
        return AddSourceKind.Local;
    }

    /// <summary>The kind shared by all sources, or Local when there are none or they differ.</summary>
    public static AddSourceKind Common(DockerInstruction i)
    {
        var kinds = i.Sources.Where(s => s.Trim().Length > 0).Select(Of).Distinct().ToList();
        return kinds.Count == 1 ? kinds[0] : AddSourceKind.Local;
    }

    public static string Describe(DockerInstruction i) => i.Sources.Count == 0 ? "Adds files; prefer COPY for local files" : Common(i) switch
    {
        AddSourceKind.Git => "Git repository" + (i.KeepGitDir ? " with .git" : "") + (i.Checksum.Trim().Length > 0 ? " · checksum pinned" : ""),
        AddSourceKind.Url => "Remote file" + (i.Checksum.Trim().Length > 0 ? " · checksum pinned" : " · no checksum"),
        _ => "Adds files; prefer COPY for local files"
    };
}
