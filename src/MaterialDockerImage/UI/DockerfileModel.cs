using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>Instruction kinds the Creator edits with structured forms. Raw keeps any line the parser cannot model.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<InstructionKind>))]
public enum InstructionKind { Arg, Env, Workdir, Copy, Add, Run, User, Expose, Volume, Label, Entrypoint, Cmd, Healthcheck, StopSignal, Shell, OnBuild, Comment, Raw }

/// <summary>One NAME=value entry (ENV, LABEL).</summary>
public sealed class NameValue
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public NameValue() { }
    public NameValue(string name, string value) { Name = name; Value = value; }
}

/// <summary>
/// A single Dockerfile instruction. Only the properties relevant to <see cref="Kind"/> are used; the others keep
/// their defaults so the JSON draft stays readable.
/// </summary>
public sealed class DockerInstruction
{
    public InstructionKind Kind { get; set; }
    /// <summary>ARG name.</summary>
    public string Name { get; set; } = "";
    /// <summary>ARG default, WORKDIR path, USER, STOPSIGNAL, ONBUILD instruction, comment text or raw source.</summary>
    public string Value { get; set; } = "";
    /// <summary>ARG has a default value (also true for an explicit empty default, "ARG NAME=").</summary>
    public bool HasValue { get; set; }
    /// <summary>ENV and LABEL entries.</summary>
    public List<NameValue> Pairs { get; set; } = new();
    /// <summary>COPY/ADD sources, VOLUME paths.</summary>
    public List<string> Sources { get; set; } = new();
    public string Destination { get; set; } = "";
    /// <summary>COPY --from stage or image.</summary>
    public string From { get; set; } = "";
    public string Chown { get; set; } = "";
    public string Chmod { get; set; } = "";
    public bool Link { get; set; }
    /// <summary>RUN, CMD, ENTRYPOINT and HEALTHCHECK use the exec (JSON array) form when true. SHELL is always exec form.</summary>
    public bool ExecForm { get; set; }
    /// <summary>Shell-form command text. RUN keeps line continuations exactly as written.</summary>
    public string Command { get; set; } = "";
    /// <summary>Exec-form arguments.</summary>
    public List<string> Arguments { get; set; } = new();
    /// <summary>Legacy RUN cache mount targets from older drafts. <see cref="DockerfileModel.Normalize"/> moves them into <see cref="Mounts"/>; rendering still honours them.</summary>
    public List<string> CacheTargets { get; set; } = new();
    /// <summary>Legacy RUN secret mount ids from older drafts (see <see cref="CacheTargets"/>).</summary>
    public List<string> SecretIds { get; set; } = new();
    /// <summary>RUN --mount entries of every type, in source order.</summary>
    public List<RunMount> Mounts { get; set; } = new();
    /// <summary>RUN --network: empty (not written), default, none or host.</summary>
    public string Network { get; set; } = "";
    /// <summary>RUN --security: empty (not written), sandbox or insecure.</summary>
    public string Security { get; set; } = "";
    /// <summary>RUN script or COPY file content given as a here-document instead of a command or sources.</summary>
    public bool Heredoc { get; set; }
    public string HeredocBody { get; set; } = "";
    public string HeredocDelimiter { get; set; } = "EOF";
    /// <summary>Quoted delimiter (&lt;&lt;"EOF"): variables in the body are not expanded at build time.</summary>
    public bool HeredocQuoted { get; set; }
    /// <summary>&lt;&lt;-EOF: leading tabs are removed from body lines.</summary>
    public bool HeredocStripTabs { get; set; }
    /// <summary>RUN heredoc interpreter written after the delimiter, for example bash or python3. Empty uses the default shell.</summary>
    public string Interpreter { get; set; } = "";
    /// <summary>COPY --parents.</summary>
    public bool Parents { get; set; }
    /// <summary>COPY/ADD --exclude patterns.</summary>
    public List<string> Excludes { get; set; } = new();
    /// <summary>ADD --checksum for URL and Git sources.</summary>
    public string Checksum { get; set; } = "";
    /// <summary>ADD --keep-git-dir=true for Git sources.</summary>
    public bool KeepGitDir { get; set; }
    /// <summary>ONBUILD: the wrapped instruction. When null, <see cref="Value"/> holds the instruction text.</summary>
    public DockerInstruction? Inner { get; set; }
    public string Port { get; set; } = "";
    public string Protocol { get; set; } = "tcp";
    public string Interval { get; set; } = "";
    public string Timeout { get; set; } = "";
    public string StartPeriod { get; set; } = "";
    public string Retries { get; set; } = "";
    /// <summary>HEALTHCHECK NONE.</summary>
    public bool Disabled { get; set; }

    public DockerInstruction Clone() => DockerfileModel.Clone(this);
}

public sealed class DockerfileStage
{
    public string Name { get; set; } = "";
    public string BaseImage { get; set; } = "";
    public string Platform { get; set; } = "";
    public List<DockerInstruction> Instructions { get; set; } = new();
    public DockerfileStage Clone() => DockerfileModel.Clone(this);
}

public sealed class DockerfileDocument
{
    public bool SyntaxHeader { get; set; } = true;
    public string Syntax { get; set; } = "docker/dockerfile:1";
    /// <summary># escape= parser directive; empty means the default backslash.</summary>
    public string Escape { get; set; } = "";
    /// <summary># check= parser directive value, for example skip=JSONArgsRecommended;error=true. Empty means not written.</summary>
    public string CheckDirective { get; set; } = "";
    /// <summary>.dockerignore patterns edited alongside the Dockerfile (one pattern or comment per entry).</summary>
    public List<string> IgnorePatterns { get; set; } = new();
    /// <summary>Instructions before the first FROM (global ARGs, comments and raw lines).</summary>
    public List<DockerInstruction> Preamble { get; set; } = new();
    public List<DockerfileStage> Stages { get; set; } = new();
    /// <summary>Build target stage name chosen in the Creator; empty means the final stage.</summary>
    public string Target { get; set; } = "";
    /// <summary>Human description of where the draft came from (starter or imported file).</summary>
    public string Origin { get; set; } = "";
    public DockerfileDocument Clone() => DockerfileModel.Clone(this);
}

/// <summary>
/// Source lines (zero-based, inclusive) of one model element. StageIndex -1 is the global preamble; InstructionIndex -1
/// is the stage's FROM line.
/// </summary>
public sealed record DockerfileLineSpan(int StageIndex, int InstructionIndex, int StartLine, int EndLine)
{
    public bool Contains(int line) => line >= StartLine && line <= EndLine;
}

/// <summary>Rendered text with the line span of every stage FROM line and instruction.</summary>
public sealed record DockerfileRenderResult(string Text, IReadOnlyList<DockerfileLineSpan> Lines);

public sealed record DockerfileParseResult(DockerfileDocument Document, IReadOnlyList<string> Warnings, IReadOnlyList<DockerfileLineSpan>? Lines = null)
{
    /// <summary>Line spans of the parsed elements in the source text (empty when not recorded).</summary>
    public IReadOnlyList<DockerfileLineSpan> LineMap => Lines ?? Array.Empty<DockerfileLineSpan>();

    /// <summary>The one-based source line a warning refers to ("Line 12: …"), or 0 when it is document-wide.</summary>
    public static int WarningLine(string warning)
    {
        var m = Regex.Match(warning, @"^Line (\d+):", RegexOptions.None, TimeSpan.FromMilliseconds(100));
        return m.Success && int.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
    }
}

/// <summary>Pure rendering, parsing, validation and serialisation for the structured Dockerfile model.</summary>
public static class DockerfileModel
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    public const string DefaultSyntax = "docker/dockerfile:1";
    public const int MaxSourceBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions)!;
    public static string Serialize(DockerfileDocument document) => JsonSerializer.Serialize(document, JsonOptions);
    public static DockerfileDocument Deserialize(string json)
    {
        var document = JsonSerializer.Deserialize<DockerfileDocument>(json, JsonOptions) ?? throw new JsonException("The draft is empty.");
        Normalize(document);
        return document;
    }
    /// <summary>Replaces null collections from hand-edited or older drafts with empty ones.</summary>
    public static void Normalize(DockerfileDocument document)
    {
        document.Preamble ??= new(); document.Stages ??= new(); document.IgnorePatterns ??= new();
        document.Syntax ??= DefaultSyntax; document.Target ??= ""; document.Origin ??= ""; document.Escape ??= ""; document.CheckDirective ??= "";
        foreach (var stage in document.Stages)
        {
            stage.Instructions ??= new(); stage.Name ??= ""; stage.BaseImage ??= ""; stage.Platform ??= "";
            foreach (var i in stage.Instructions) NormalizeInstruction(i);
        }
        foreach (var i in document.Preamble) NormalizeInstruction(i);
    }
    private static void NormalizeInstruction(DockerInstruction i)
    {
        i.Pairs ??= new(); i.Sources ??= new(); i.Arguments ??= new(); i.CacheTargets ??= new(); i.SecretIds ??= new();
        i.Mounts ??= new(); i.Excludes ??= new();
        i.Name ??= ""; i.Value ??= ""; i.Destination ??= ""; i.From ??= ""; i.Chown ??= ""; i.Chmod ??= ""; i.Command ??= "";
        i.Port ??= ""; i.Protocol ??= "tcp"; i.Interval ??= ""; i.Timeout ??= ""; i.StartPeriod ??= ""; i.Retries ??= "";
        i.Network ??= ""; i.Security ??= ""; i.HeredocBody ??= ""; i.HeredocDelimiter ??= "EOF"; i.Interpreter ??= ""; i.Checksum ??= "";
        foreach (var m in i.Mounts) RunMounts.Normalize(m);
        MigrateLegacyMounts(i);
        if (i.Inner != null) NormalizeInstruction(i.Inner);
    }

    /// <summary>Moves legacy cache targets and secret ids into <see cref="DockerInstruction.Mounts"/> without changing the rendered text.</summary>
    public static void MigrateLegacyMounts(DockerInstruction i)
    {
        if (i.CacheTargets.Count == 0 && i.SecretIds.Count == 0) return;
        var legacy = LegacyMounts(i).ToList();
        i.Mounts.InsertRange(0, legacy);
        i.CacheTargets.Clear(); i.SecretIds.Clear();
    }

    private static IEnumerable<RunMount> LegacyMounts(DockerInstruction i) =>
        i.CacheTargets.Where(t => t.Trim().Length > 0).Select(t => new RunMount { Type = RunMounts.Cache, Target = t.Trim() })
            .Concat(i.SecretIds.Where(t => t.Trim().Length > 0).Select(id => new RunMount { Type = RunMounts.Secret, Id = id.Trim() }));

    /// <summary>Every RUN mount: legacy entries first, then <see cref="DockerInstruction.Mounts"/>.</summary>
    public static IReadOnlyList<RunMount> AllMounts(DockerInstruction i) => LegacyMounts(i).Concat(i.Mounts).ToList();

    /// <summary>The shell text a RUN executes: the heredoc body, the exec arguments joined, or the shell command.</summary>
    public static string RunScript(DockerInstruction i) => i.Heredoc ? i.HeredocBody : i.ExecForm ? string.Join(" ", i.Arguments) : i.Command;

    public static string Keyword(InstructionKind kind) => kind switch
    {
        InstructionKind.StopSignal => "STOPSIGNAL",
        InstructionKind.OnBuild => "ONBUILD",
        InstructionKind.Comment => "#",
        InstructionKind.Raw => "RAW",
        _ => kind.ToString().ToUpperInvariant()
    };

    // ---------------------------------------------------------------- Rendering

    /// <summary>Renders the whole document. Output is deterministic and always ends with a single newline.</summary>
    public static string Render(DockerfileDocument document) => RenderWithMap(document).Text;

    /// <summary>Renders the document and records which lines each FROM and instruction occupies.</summary>
    public static DockerfileRenderResult RenderWithMap(DockerfileDocument document)
    {
        var sb = new StringBuilder();
        var map = new List<DockerfileLineSpan>();
        var line = 0;
        void Emit(int stage, int instruction, string text)
        {
            var count = 1 + text.Count(c => c == '\n');
            map.Add(new DockerfileLineSpan(stage, instruction, line, line + count - 1));
            sb.Append(text).Append('\n');
            line += count;
        }
        if (document.SyntaxHeader) sb.Append("# syntax=").Append(string.IsNullOrWhiteSpace(document.Syntax) ? DefaultSyntax : document.Syntax.Trim()).Append('\n');
        if (document.Escape.Trim().Length > 0) sb.Append("# escape=").Append(document.Escape.Trim()).Append('\n');
        if (document.CheckDirective.Trim().Length > 0) sb.Append("# check=").Append(document.CheckDirective.Trim()).Append('\n');
        line = sb.ToString().Count(c => c == '\n');
        for (var p = 0; p < document.Preamble.Count; p++) Emit(-1, p, RenderInstruction(document.Preamble[p]));
        for (var i = 0; i < document.Stages.Count; i++)
        {
            if (sb.Length > 0) { sb.Append('\n'); line++; }
            Emit(i, -1, RenderFrom(document.Stages[i]));
            for (var k = 0; k < document.Stages[i].Instructions.Count; k++) Emit(i, k, RenderInstruction(document.Stages[i].Instructions[k]));
        }
        return new DockerfileRenderResult(sb.ToString(), map);
    }

    /// <summary>The span whose lines contain <paramref name="line"/> (zero-based), preferring the innermost; null for blank lines.</summary>
    public static DockerfileLineSpan? SpanAt(IReadOnlyList<DockerfileLineSpan> map, int line) => map.LastOrDefault(s => s.Contains(line));

    public static string RenderFrom(DockerfileStage stage)
    {
        var sb = new StringBuilder("FROM ");
        if (!string.IsNullOrWhiteSpace(stage.Platform)) sb.Append("--platform=").Append(stage.Platform.Trim()).Append(' ');
        sb.Append(stage.BaseImage.Trim());
        if (!string.IsNullOrWhiteSpace(stage.Name)) sb.Append(" AS ").Append(stage.Name.Trim());
        return sb.ToString();
    }

    public static string RenderInstruction(DockerInstruction i)
    {
        switch (i.Kind)
        {
            case InstructionKind.Arg:
                return "ARG " + i.Name.Trim() + (i.HasValue ? "=" + QuoteValue(i.Value) : "");
            case InstructionKind.Env:
            case InstructionKind.Label:
                return Keyword(i.Kind) + " " + string.Join(" ", i.Pairs.Select(p => QuoteKey(p.Name.Trim(), i.Kind == InstructionKind.Label) + "=" + QuoteValue(p.Value)));
            case InstructionKind.Workdir:
            case InstructionKind.User:
            case InstructionKind.StopSignal:
                return Keyword(i.Kind) + " " + i.Value.Trim();
            case InstructionKind.OnBuild:
                return "ONBUILD " + (i.Inner != null ? RenderInstruction(i.Inner) : i.Value.Trim());
            case InstructionKind.Copy:
            case InstructionKind.Add:
            {
                var sb = new StringBuilder(Keyword(i.Kind));
                if (i.Kind == InstructionKind.Copy && i.From.Trim().Length > 0) sb.Append(" --from=").Append(i.From.Trim());
                if (i.Kind == InstructionKind.Add && i.Checksum.Trim().Length > 0) sb.Append(" --checksum=").Append(i.Checksum.Trim());
                if (i.Kind == InstructionKind.Add && i.KeepGitDir) sb.Append(" --keep-git-dir=true");
                if (i.Chown.Trim().Length > 0) sb.Append(" --chown=").Append(i.Chown.Trim());
                if (i.Chmod.Trim().Length > 0) sb.Append(" --chmod=").Append(i.Chmod.Trim());
                if (i.Link) sb.Append(" --link");
                if (i.Kind == InstructionKind.Copy && i.Parents) sb.Append(" --parents");
                foreach (var pattern in i.Excludes.Select(e => e.Trim()).Where(e => e.Length > 0)) sb.Append(" --exclude=").Append(pattern);
                if (i.Kind == InstructionKind.Copy && i.Heredoc)
                    return sb.Append(' ').Append(HeredocOpener(i)).Append(' ').Append(i.Destination.Trim()).Append('\n').Append(HeredocTail(i)).ToString();
                var paths = i.Sources.Select(s => s.Trim()).Where(s => s.Length > 0).Append(i.Destination.Trim()).ToList();
                if (paths.Any(NeedsJsonPath)) sb.Append(' ').Append(ExecJson(paths));
                else sb.Append(' ').Append(string.Join(" ", paths));
                return sb.ToString();
            }
            case InstructionKind.Run:
            {
                var sb = new StringBuilder("RUN");
                foreach (var mount in AllMounts(i)) sb.Append(" --mount=").Append(RunMounts.Render(mount));
                if (i.Network.Trim().Length > 0) sb.Append(" --network=").Append(i.Network.Trim());
                if (i.Security.Trim().Length > 0) sb.Append(" --security=").Append(i.Security.Trim());
                if (i.Heredoc)
                {
                    sb.Append(' ').Append(HeredocOpener(i));
                    if (i.Interpreter.Trim().Length > 0) sb.Append(' ').Append(i.Interpreter.Trim());
                    return sb.Append('\n').Append(HeredocTail(i)).ToString();
                }
                sb.Append(' ').Append(i.ExecForm ? ExecJson(i.Arguments) : i.Command.Trim());
                return sb.ToString();
            }
            case InstructionKind.Expose:
            {
                var protocol = i.Protocol.Trim().ToLowerInvariant();
                return "EXPOSE " + i.Port.Trim() + (protocol.Length == 0 || protocol == "tcp" ? "" : "/" + protocol);
            }
            case InstructionKind.Volume:
                return "VOLUME " + ExecJson(i.Sources.Select(s => s.Trim()).Where(s => s.Length > 0).ToList());
            case InstructionKind.Entrypoint:
            case InstructionKind.Cmd:
                return Keyword(i.Kind) + " " + (i.ExecForm ? ExecJson(i.Arguments) : i.Command.Trim());
            case InstructionKind.Healthcheck:
            {
                if (i.Disabled) return "HEALTHCHECK NONE";
                var sb = new StringBuilder("HEALTHCHECK");
                if (i.Interval.Trim().Length > 0) sb.Append(" --interval=").Append(i.Interval.Trim());
                if (i.Timeout.Trim().Length > 0) sb.Append(" --timeout=").Append(i.Timeout.Trim());
                if (i.StartPeriod.Trim().Length > 0) sb.Append(" --start-period=").Append(i.StartPeriod.Trim());
                if (i.Retries.Trim().Length > 0) sb.Append(" --retries=").Append(i.Retries.Trim());
                sb.Append(" CMD ").Append(i.ExecForm ? ExecJson(i.Arguments) : i.Command.Trim());
                return sb.ToString();
            }
            case InstructionKind.Shell:
                return "SHELL " + ExecJson(i.Arguments);
            case InstructionKind.Comment:
                return string.Join("\n", i.Value.Replace("\r\n", "\n").Split('\n').Select(line => line.Length == 0 ? "#" : "# " + line));
            case InstructionKind.Raw:
                return i.Value.Replace("\r\n", "\n").TrimEnd('\n');
            default:
                throw new ArgumentOutOfRangeException(nameof(i), i.Kind, "Unknown instruction kind.");
        }
    }

    private static string HeredocOpener(DockerInstruction i)
    {
        var delimiter = i.HeredocDelimiter.Trim().Length > 0 ? i.HeredocDelimiter.Trim() : "EOF";
        return "<<" + (i.HeredocStripTabs ? "-" : "") + (i.HeredocQuoted ? "\"" + delimiter + "\"" : delimiter);
    }

    /// <summary>The heredoc body lines followed by the terminating delimiter line.</summary>
    private static string HeredocTail(DockerInstruction i)
    {
        var body = i.HeredocBody.Replace("\r\n", "\n");
        if (body.EndsWith('\n')) body = body[..^1];
        var delimiter = i.HeredocDelimiter.Trim().Length > 0 ? i.HeredocDelimiter.Trim() : "EOF";
        return (body.Length > 0 ? body + "\n" : "") + delimiter;
    }

    private static bool NeedsJsonPath(string path) => path.Any(c => char.IsWhiteSpace(c) || c == '"');

    /// <summary>Exec-form JSON array with the spacing Docker documentation uses: ["a", "b"].</summary>
    public static string ExecJson(IEnumerable<string> values) => "[" + string.Join(", ", values.Select(JsonString)) + "]";

    /// <summary>Minimal JSON string escaping: only what JSON requires, so non-ASCII text stays readable.</summary>
    public static string JsonString(string value)
    {
        var sb = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    /// <summary>Quotes an ENV/LABEL/ARG value when Dockerfile word splitting would otherwise change it.</summary>
    public static string QuoteValue(string value)
    {
        if (value.Length > 0 && !value.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'' or '\\')) return value;
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
    private static string QuoteKey(string key, bool label) => label && key.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'' or '\\') ? QuoteValue(key) : key;

    // ---------------------------------------------------------------- Card text

    /// <summary>One-line summary for an instruction card (the rendered text without the keyword).</summary>
    public static string Summary(DockerInstruction i)
    {
        if (i.Kind == InstructionKind.Comment) return i.Value.Replace("\r\n", "\n").Split('\n')[0];
        if (i.Kind == InstructionKind.Raw) return Shorten(i.Value.Replace("\r\n", "\n").Split('\n')[0], 120);
        var text = RenderInstruction(i);
        var first = text.Split('\n')[0];
        var space = first.IndexOf(' ');
        var rest = space < 0 ? "" : first[(space + 1)..];
        if (text.Contains('\n')) rest += " …";
        return Shorten(rest, 120);
    }

    /// <summary>A short plain-language explanation shown under the summary.</summary>
    public static string Hint(DockerInstruction i) => i.Kind switch
    {
        InstructionKind.Arg => i.HasValue ? "Build argument with a default" : "Build argument without a default",
        InstructionKind.Env => i.Pairs.Count == 1 ? "1 variable" : $"{i.Pairs.Count} variables",
        InstructionKind.Label => i.Pairs.Count == 1 ? "1 label" : $"{i.Pairs.Count} labels",
        InstructionKind.Workdir => "Working directory for the steps below",
        InstructionKind.Copy => (i.Heredoc ? "Inline file (heredoc)" : i.From.Trim().Length > 0 ? "From stage " + i.From.Trim() : $"{i.Sources.Count} source(s) from the build context")
            + (i.Parents ? " · keeps parent folders" : "") + (i.Excludes.Count > 0 ? $" · {i.Excludes.Count} exclusion(s)" : ""),
        InstructionKind.Add => AddSourceKinds.Describe(i),
        InstructionKind.Run => RunHint(i),
        InstructionKind.User => IsRootUser(i.Value) ? "Runs as root" : "Drops root before the command runs",
        InstructionKind.Expose => "Documents the port; publishing happens at run time",
        InstructionKind.Volume => "Declares mount points",
        InstructionKind.Entrypoint or InstructionKind.Cmd => i.ExecForm ? $"Exec form, {i.Arguments.Count} argument(s)" : "Shell form (runs through /bin/sh -c)",
        InstructionKind.Healthcheck => i.Disabled ? "Disables the inherited health check" : "Lets the engine report readiness",
        InstructionKind.StopSignal => "Signal sent to stop the container",
        InstructionKind.Shell => "Shell used by later shell-form instructions",
        InstructionKind.OnBuild => "Runs when another image builds FROM this one" + (i.Inner != null ? " (" + Keyword(i.Inner.Kind) + ")" : ""),
        InstructionKind.Comment => "Comment",
        InstructionKind.Raw => "Kept exactly as written (not modelled)",
        _ => ""
    };

    private static string RunHint(DockerInstruction i)
    {
        var parts = new List<string> { i.Heredoc ? "Heredoc script" + (i.Interpreter.Trim().Length > 0 ? " (" + i.Interpreter.Trim() + ")" : "") : i.ExecForm ? "Exec form" : "Shell form" };
        foreach (var group in AllMounts(i).GroupBy(m => m.Type.Trim().ToLowerInvariant()))
            parts.Add($"{group.Count()} {group.Key} mount(s)");
        if (i.Network.Trim().Length > 0) parts.Add("network " + i.Network.Trim());
        if (string.Equals(i.Security.Trim(), "insecure", StringComparison.OrdinalIgnoreCase)) parts.Add("insecure mode");
        return string.Join(" · ", parts);
    }

    public static bool IsRootUser(string user)
    {
        var name = user.Trim().Split(':')[0];
        return name.Length == 0 || name == "root" || name == "0";
    }

    private static string Shorten(string text, int max) => text.Length > max ? text[..max] + "…" : text;

    // ---------------------------------------------------------------- Factory

    /// <summary>A new instruction of the given kind with sensible editable defaults.</summary>
    public static DockerInstruction Create(InstructionKind kind) => kind switch
    {
        InstructionKind.Arg => new() { Kind = kind, Name = "VERSION", HasValue = true, Value = "1.0" },
        InstructionKind.Env => new() { Kind = kind, Pairs = { new("APP_ENV", "production") } },
        InstructionKind.Label => new() { Kind = kind, Pairs = { new("org.opencontainers.image.source", "") } },
        InstructionKind.Workdir => new() { Kind = kind, Value = "/app" },
        InstructionKind.Copy => new() { Kind = kind, Sources = { "." }, Destination = "." },
        InstructionKind.Add => new() { Kind = kind, Sources = { "https://example.com/file.tar.gz" }, Destination = "/tmp/" },
        InstructionKind.Run => new() { Kind = kind, Command = "echo ready" },
        InstructionKind.User => new() { Kind = kind, Value = "10001" },
        InstructionKind.Expose => new() { Kind = kind, Port = "8080", Protocol = "tcp" },
        InstructionKind.Volume => new() { Kind = kind, Sources = { "/data" } },
        InstructionKind.Entrypoint => new() { Kind = kind, ExecForm = true, Arguments = { "/app/start" } },
        InstructionKind.Cmd => new() { Kind = kind, ExecForm = true, Arguments = { "/app/start" } },
        InstructionKind.Healthcheck => new() { Kind = kind, Interval = "30s", Timeout = "5s", StartPeriod = "10s", Retries = "3", Command = "wget -qO- http://localhost:8080/ || exit 1" },
        InstructionKind.StopSignal => new() { Kind = kind, Value = "SIGTERM" },
        InstructionKind.Shell => new() { Kind = kind, ExecForm = true, Arguments = { "/bin/sh", "-c" } },
        InstructionKind.OnBuild => new() { Kind = kind, Inner = new DockerInstruction { Kind = InstructionKind.Copy, Sources = { "." }, Destination = "/app" } },
        InstructionKind.Comment => new() { Kind = kind, Value = "Explain the next step" },
        _ => new() { Kind = InstructionKind.Raw, Value = "" }
    };

    // ---------------------------------------------------------------- Validation

    /// <summary>Problems that make an instruction invalid or unsafe to render. Empty when the instruction is usable.</summary>
    public static IReadOnlyList<string> Validate(DockerInstruction i)
    {
        var problems = new List<string>();
        void NoControl(string label, string value, bool allowContinuation = false)
        {
            for (var k = 0; k < value.Length; k++)
            {
                var c = value[k];
                if (c == '\t') continue;
                if (allowContinuation && c is '\n' or '\r') continue;
                if (char.IsControl(c)) { problems.Add(label + " cannot contain line breaks or control characters."); return; }
            }
        }
        switch (i.Kind)
        {
            case InstructionKind.Arg:
                if (!IsVariableName(i.Name)) problems.Add("Argument name must start with a letter or underscore and use letters, digits and underscores.");
                NoControl("Default value", i.Value);
                break;
            case InstructionKind.Env:
            case InstructionKind.Label:
                if (i.Pairs.Count == 0) problems.Add("Add at least one entry.");
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in i.Pairs)
                {
                    var key = p.Name.Trim();
                    if (i.Kind == InstructionKind.Env && !IsVariableName(key)) problems.Add($"\"{key}\" is not a valid variable name.");
                    if (i.Kind == InstructionKind.Label && (key.Length == 0 || key.Contains('='))) problems.Add("Label keys are required and cannot contain '='.");
                    if (key.Length > 0 && !seen.Add(key)) problems.Add($"\"{key}\" is listed twice.");
                    NoControl("Values", p.Value); NoControl("Keys", p.Name);
                }
                break;
            case InstructionKind.Workdir:
                if (i.Value.Trim().Length == 0) problems.Add("Enter a directory path.");
                NoControl("Directory", i.Value);
                break;
            case InstructionKind.Copy:
            case InstructionKind.Add:
                if (i.Heredoc && i.Kind == InstructionKind.Copy) ValidateHeredoc(i, problems);
                else if (!i.Sources.Any(s => s.Trim().Length > 0)) problems.Add("Add at least one source path.");
                if (i.Destination.Trim().Length == 0) problems.Add("Enter a destination path.");
                foreach (var pattern in i.Excludes)
                    if (pattern.Trim().Length == 0 || pattern.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) problems.Add("Exclude patterns cannot be empty or contain spaces.");
                if (i.Kind == InstructionKind.Add)
                {
                    var remote = i.Sources.Where(x => x.Trim().Length > 0).Select(x => AddSourceKinds.Of(x)).ToList();
                    if (i.Checksum.Trim().Length > 0)
                    {
                        if (remote.Any(k => k == AddSourceKind.Local)) problems.Add("--checksum only applies to URL and Git sources.");
                        if (!Regex.IsMatch(i.Checksum.Trim(), @"^(sha(256|384|512):[0-9a-fA-F]{32,128}|[0-9a-f]{7,64}|\$\{?[A-Za-z_][A-Za-z0-9_]*\}?)$", RegexOptions.None, RegexTimeout))
                            problems.Add("Checksum is sha256: followed by the hexadecimal digest for URLs, or a commit hash for Git sources.");
                    }
                    if (i.KeepGitDir && remote.Any(k => k != AddSourceKind.Git)) problems.Add("--keep-git-dir only applies to Git repository sources.");
                }
                if (i.Chmod.Trim().Length > 0 && !IsChmod(i.Chmod.Trim())) problems.Add("Permissions use octal notation such as 755 or 0644, or symbolic notation such as u=rwx,go=rx (symbolic needs docker/dockerfile:1.14).");
                if (i.Chown.Trim().Any(char.IsWhiteSpace) || i.From.Trim().Any(char.IsWhiteSpace)) problems.Add("Owner and source stage cannot contain spaces.");
                foreach (var s in i.Sources) NoControl("Sources", s);
                NoControl("Destination", i.Destination);
                break;
            case InstructionKind.Run:
                if (i.Heredoc)
                {
                    ValidateHeredoc(i, problems);
                    NoControl("Interpreter", i.Interpreter);
                    if (i.Interpreter.Contains("<<", StringComparison.Ordinal)) problems.Add("The interpreter cannot contain another heredoc.");
                }
                else
                {
                    if (i.ExecForm ? i.Arguments.Count == 0 : i.Command.Trim().Length == 0) problems.Add("Enter the command to run.");
                    if (!i.ExecForm) ValidateContinuations(i.Command, problems);
                    foreach (var a in i.Arguments) NoControl("Arguments", a);
                }
                foreach (var mount in AllMounts(i)) problems.AddRange(RunMounts.Validate(mount));
                if (i.Network.Trim().ToLowerInvariant() is not ("" or "default" or "none" or "host")) problems.Add("Network must be default, none or host.");
                if (i.Security.Trim().ToLowerInvariant() is not ("" or "sandbox" or "insecure")) problems.Add("Security must be sandbox or insecure.");
                break;
            case InstructionKind.User:
                if (i.Value.Trim().Length == 0 || i.Value.Trim().Any(char.IsWhiteSpace)) problems.Add("Enter one user name or UID (optionally user:group) without spaces.");
                break;
            case InstructionKind.Expose:
                if (!Regex.IsMatch(i.Port.Trim(), @"^(\$\{?[A-Za-z_][A-Za-z0-9_]*\}?|\d{1,5}(-\d{1,5})?)$", RegexOptions.None, RegexTimeout) || i.Port.Trim().Split('-').Any(p => !p.StartsWith('$') && (!int.TryParse(p, out var n) || n < 1 || n > 65535)))
                    problems.Add("Port must be a number from 1 to 65535, a range such as 8000-8010, or a variable such as ${PORT}.");
                if (i.Protocol.Trim().ToLowerInvariant() is not ("" or "tcp" or "udp")) problems.Add("Protocol must be tcp or udp.");
                break;
            case InstructionKind.Volume:
                if (!i.Sources.Any(s => s.Trim().Length > 0)) problems.Add("Add at least one mount path.");
                foreach (var s in i.Sources) NoControl("Paths", s);
                break;
            case InstructionKind.Entrypoint:
            case InstructionKind.Cmd:
                if (i.ExecForm ? i.Arguments.Count == 0 : i.Command.Trim().Length == 0) problems.Add("Enter the command.");
                foreach (var a in i.Arguments) NoControl("Arguments", a);
                NoControl("Command", i.Command);
                break;
            case InstructionKind.Healthcheck:
                if (i.Disabled) break;
                if (i.ExecForm ? i.Arguments.Count == 0 : i.Command.Trim().Length == 0) problems.Add("Enter the health check command.");
                foreach (var (label, value) in new[] { ("Interval", i.Interval), ("Timeout", i.Timeout), ("Start period", i.StartPeriod) })
                    if (value.Trim().Length > 0 && !IsDuration(value.Trim())) problems.Add(label + " uses a duration such as 30s, 1m or 1m30s.");
                if (i.Retries.Trim().Length > 0 && (!int.TryParse(i.Retries.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var r) || r < 0)) problems.Add("Retries must be a whole number.");
                NoControl("Command", i.Command);
                break;
            case InstructionKind.StopSignal:
                if (!Regex.IsMatch(i.Value.Trim(), @"^(SIG[A-Z0-9+-]+|[0-9]+|\$\{?[A-Za-z_][A-Za-z0-9_]*\}?)$", RegexOptions.None, RegexTimeout)) problems.Add("Use a signal name such as SIGTERM or a signal number.");
                break;
            case InstructionKind.Shell:
                if (i.Arguments.Count == 0) problems.Add("Enter the shell executable and its arguments, for example /bin/sh and -c.");
                break;
            case InstructionKind.OnBuild:
                if (i.Inner != null)
                {
                    if (i.Inner.Kind is InstructionKind.OnBuild or InstructionKind.Comment or InstructionKind.Raw) problems.Add("ONBUILD wraps one instruction such as RUN or COPY (not ONBUILD, a comment or raw text).");
                    else problems.AddRange(Validate(i.Inner));
                    break;
                }
                var inner = i.Value.Trim();
                if (inner.Length == 0) problems.Add("Enter the instruction to run in downstream builds.");
                else if (Regex.IsMatch(inner, @"^(ONBUILD|FROM|MAINTAINER)\b", RegexOptions.IgnoreCase, RegexTimeout)) problems.Add("ONBUILD cannot wrap ONBUILD, FROM or MAINTAINER.");
                NoControl("Instruction", i.Value);
                break;
        }
        return problems;
    }

    public static IReadOnlyList<string> ValidateStage(DockerfileStage stage)
    {
        var problems = new List<string>();
        if (stage.BaseImage.Trim().Length == 0) problems.Add("Choose a base image.");
        else if (stage.BaseImage.Trim().Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) problems.Add("The base image reference cannot contain spaces.");
        if (stage.Name.Trim().Length > 0 && !Regex.IsMatch(stage.Name.Trim(), "^[A-Za-z][A-Za-z0-9_.-]*$", RegexOptions.None, RegexTimeout)) problems.Add("Stage names start with a letter and use letters, digits, '.', '_' and '-'.");
        if (stage.Platform.Trim().Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) problems.Add("The platform cannot contain spaces.");
        return problems;
    }

    private static void ValidateHeredoc(DockerInstruction i, List<string> problems)
    {
        var delimiter = i.HeredocDelimiter.Trim();
        if (!Regex.IsMatch(delimiter, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.None, RegexTimeout)) problems.Add("The heredoc delimiter uses letters, digits and underscores, for example EOF.");
        var body = i.HeredocBody.Replace("\r\n", "\n");
        if (i.Kind == InstructionKind.Run && body.Trim().Length == 0) problems.Add("Enter the script for the heredoc.");
        if (delimiter.Length > 0 && body.Split('\n').Any(line => (i.HeredocStripTabs ? line.TrimStart('\t') : line).TrimEnd() == delimiter))
            problems.Add($"A body line equals the delimiter {delimiter}, which would end the heredoc early. Choose another delimiter.");
        if (body.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'))) problems.Add("The heredoc body cannot contain control characters.");
    }

    private static void ValidateContinuations(string command, List<string> problems)
    {
        var lines = command.Replace("\r\n", "\n").Split('\n');
        for (var k = 0; k < lines.Length - 1; k++)
            if (!lines[k].TrimEnd().EndsWith('\\')) { problems.Add("Each line of a multi-line command must end with a backslash (\\)."); return; }
        if (command.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'))) problems.Add("The command cannot contain control characters.");
    }

    /// <summary>Octal (755) or symbolic (u+x,go=rx) --chmod values, or a variable.</summary>
    public static bool IsChmod(string value) => Regex.IsMatch(value, @"^([0-7]{3,4}|([ugoa]*[-+=]([rwxXst]*|[ugo]))(,[ugoa]*[-+=]([rwxXst]*|[ugo]))*|\$\{?[A-Za-z_][A-Za-z0-9_]*\}?)$", RegexOptions.None, RegexTimeout);
    public static bool IsSymbolicChmod(string value) => value.Trim().Length > 0 && !Regex.IsMatch(value.Trim(), @"^([0-7]{3,4}|\$.*)$", RegexOptions.None, RegexTimeout);

    public static bool IsVariableName(string name) => Regex.IsMatch(name.Trim(), "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.None, RegexTimeout);
    public static bool IsDuration(string value) => Regex.IsMatch(value, @"^(\d+(\.\d+)?(ns|us|µs|ms|s|m|h))+$", RegexOptions.None, RegexTimeout);

    // ---------------------------------------------------------------- Parsing

    /// <summary>
    /// Imports a Dockerfile into the model. Lines the model cannot represent exactly (heredocs, unknown flags,
    /// unknown instructions) are kept as Raw instructions so rendering preserves them.
    /// </summary>
    public static DockerfileParseResult Parse(string source)
    {
        var warnings = new List<string>();
        var map = new List<DockerfileLineSpan>();
        var document = new DockerfileDocument { SyntaxHeader = false, Syntax = DefaultSyntax };
        if (Encoding.UTF8.GetByteCount(source) > MaxSourceBytes) throw new ArgumentException("The Dockerfile is larger than the 1 MiB import limit.");
        var lines = source.Replace("\r\n", "\n").Split('\n');
        var index = 0;
        // Parser directives are only recognised before any other line.
        while (index < lines.Length)
        {
            var directive = Regex.Match(lines[index], @"^\s*#\s*([A-Za-z]+)\s*=\s*(.*?)\s*$", RegexOptions.None, RegexTimeout);
            if (!directive.Success) break;
            var key = directive.Groups[1].Value.ToLowerInvariant();
            if (key == "syntax") { document.SyntaxHeader = true; document.Syntax = directive.Groups[2].Value; }
            else if (key == "escape")
            {
                if (directive.Groups[2].Value != "\\") warnings.Add("The escape directive changes the continuation character; the Creator always uses a backslash. Review imported multi-line instructions.");
                document.Escape = directive.Groups[2].Value;
            }
            else if (key == "check") document.CheckDirective = directive.Groups[2].Value;
            else
            {
                map.Add(new DockerfileLineSpan(-1, document.Preamble.Count, index, index));
                document.Preamble.Add(new DockerInstruction { Kind = InstructionKind.Raw, Value = lines[index].Trim() });
            }
            index++;
        }

        DockerfileStage? stage = null;
        List<DockerInstruction> Target() => stage?.Instructions ?? document.Preamble;
        void Emit(DockerInstruction instruction, int first, int last)
        {
            var list = Target();
            map.Add(new DockerfileLineSpan(stage == null ? -1 : document.Stages.Count - 1, list.Count, first, Math.Max(first, last)));
            list.Add(instruction);
        }
        while (index < lines.Length)
        {
            var line = lines[index];
            var trimmed = line.Trim();
            if (trimmed.Length == 0) { index++; continue; }
            if (trimmed.StartsWith('#'))
            {
                var comment = new List<string>();
                var commentStart = index;
                while (index < lines.Length && lines[index].Trim().StartsWith('#'))
                {
                    var text = lines[index].Trim()[1..];
                    comment.Add(text.StartsWith(' ') ? text[1..] : text);
                    index++;
                }
                Emit(new DockerInstruction { Kind = InstructionKind.Comment, Value = string.Join("\n", comment) }, commentStart, index - 1);
                continue;
            }

            var startLine = index;
            var physical = new List<string> { line };
            while (physical[^1].TrimEnd().EndsWith('\\') && index + 1 < lines.Length)
            {
                index++;
                // Comment lines inside a continuation are ignored by Docker; keep them only in the raw text.
                physical.Add(lines[index]);
            }
            index++;
            var rawText = string.Join("\n", physical.Select(p => p.TrimEnd()));

            var heredoc = Regex.Match(rawText, @"<<(-?)\s*[""']?([A-Za-z_][A-Za-z0-9_]*)[""']?", RegexOptions.None, RegexTimeout);
            if (heredoc.Success)
            {
                var terminator = heredoc.Groups[2].Value;
                var strip = heredoc.Groups[1].Value == "-";
                var bodyLines = new List<string>();
                string? terminatorLine = null;
                while (index < lines.Length)
                {
                    var bodyLine = lines[index]; index++;
                    if ((strip ? bodyLine.TrimStart('\t') : bodyLine).TrimEnd() == terminator) { terminatorLine = bodyLine; break; }
                    bodyLines.Add(bodyLine);
                }
                var single = physical.Count == 1 && Regex.Matches(physical[0], "<<", RegexOptions.None, RegexTimeout).Count == 1;
                var structured = terminatorLine != null && single && stage != null ? ParseHeredocInstruction(physical[0], bodyLines) : null;
                if (structured != null) { Emit(structured, startLine, index - 1); continue; }
                var rawBody = new List<string>(physical);
                rawBody.AddRange(bodyLines);
                if (terminatorLine != null) rawBody.Add(terminatorLine);
                Emit(new DockerInstruction { Kind = InstructionKind.Raw, Value = string.Join("\n", rawBody) }, startLine, index - 1);
                warnings.Add($"Line {startLine + 1}: heredoc kept as raw source" + (terminatorLine == null ? " (no closing delimiter was found)." : "."));
                continue;
            }

            var collapsed = CollapseContinuations(physical);
            var keywordMatch = Regex.Match(collapsed, @"^\s*([A-Za-z]+)\b\s*(.*)$", RegexOptions.Singleline, RegexTimeout);
            if (!keywordMatch.Success) { Emit(Raw(rawText), startLine, index - 1); warnings.Add($"Line {startLine + 1}: not an instruction; kept as raw source."); continue; }
            var keyword = keywordMatch.Groups[1].Value.ToUpperInvariant();
            var rest = keywordMatch.Groups[2].Value.Trim();

            if (keyword == "FROM")
            {
                var from = Regex.Match(rest, @"^(?:--platform=(\S+)\s+)?(\S+)(?:\s+AS\s+(\S+))?$", RegexOptions.IgnoreCase, RegexTimeout);
                if (!from.Success) { warnings.Add($"Line {startLine + 1}: FROM uses options the Creator does not model; the base image was imported as written."); from = Regex.Match(rest, @"(\S+)\s*$", RegexOptions.None, RegexTimeout); }
                stage = new DockerfileStage
                {
                    Platform = from.Groups.Count > 3 && from.Groups[1].Success ? from.Groups[1].Value : "",
                    BaseImage = from.Groups.Count > 3 ? from.Groups[2].Value : from.Groups[1].Value,
                    Name = from.Groups.Count > 3 && from.Groups[3].Success ? from.Groups[3].Value : ""
                };
                document.Stages.Add(stage);
                map.Add(new DockerfileLineSpan(document.Stages.Count - 1, -1, startLine, index - 1));
                continue;
            }
            if (stage == null && keyword != "ARG")
            {
                Emit(Raw(rawText), startLine, index - 1);
                warnings.Add($"Line {startLine + 1}: {keyword} before the first FROM was kept as raw source.");
                continue;
            }
            var parsed = ParseInstruction(keyword, rest, rawText);
            if (parsed.Count == 1 && parsed[0].Kind == InstructionKind.Raw && keyword is not ("MAINTAINER")) warnings.Add($"Line {startLine + 1}: {keyword} uses syntax the Creator does not model; kept as raw source.");
            if (keyword == "MAINTAINER") warnings.Add($"Line {startLine + 1}: MAINTAINER is deprecated; consider a LABEL. Kept as raw source.");
            foreach (var instruction in parsed) Emit(instruction, startLine, index - 1);
        }
        if (document.Stages.Count == 0) warnings.Add("No FROM instruction was found, so the draft has no stages.");
        return new(document, warnings, map);
    }

    private static DockerInstruction Raw(string text) => new() { Kind = InstructionKind.Raw, Value = text };

    private static string CollapseContinuations(IReadOnlyList<string> physical)
    {
        var parts = new List<string>();
        for (var k = 0; k < physical.Count; k++)
        {
            var part = physical[k].TrimEnd();
            if (k > 0 && part.TrimStart().StartsWith('#')) continue;
            if (k < physical.Count - 1 && part.EndsWith('\\')) part = part[..^1];
            parts.Add(k == 0 ? part.TrimEnd() : part.Trim());
        }
        return string.Join(" ", parts.Where(p => p.Length > 0));
    }

    private static List<DockerInstruction> ParseInstruction(string keyword, string rest, string rawText)
    {
        var one = new List<DockerInstruction>();
        DockerInstruction? result = keyword switch
        {
            "ARG" => ParseArg(rest),
            "ENV" => ParsePairs(InstructionKind.Env, rest),
            "LABEL" => ParsePairs(InstructionKind.Label, rest),
            "WORKDIR" => rest.Length > 0 ? new() { Kind = InstructionKind.Workdir, Value = rest } : null,
            "USER" => rest.Length > 0 && !rest.Any(char.IsWhiteSpace) ? new() { Kind = InstructionKind.User, Value = rest } : null,
            "STOPSIGNAL" => rest.Length > 0 ? new() { Kind = InstructionKind.StopSignal, Value = rest } : null,
            "ONBUILD" => ParseOnBuild(rest),
            "COPY" => ParseCopy(InstructionKind.Copy, rest),
            "ADD" => ParseCopy(InstructionKind.Add, rest),
            "RUN" => ParseRun(rawText),
            "VOLUME" => ParseVolume(rest),
            "CMD" => ParseCommand(InstructionKind.Cmd, rest),
            "ENTRYPOINT" => ParseCommand(InstructionKind.Entrypoint, rest),
            "SHELL" => TryExec(rest) is { } shell ? new() { Kind = InstructionKind.Shell, ExecForm = true, Arguments = shell } : null,
            "HEALTHCHECK" => ParseHealthcheck(rest),
            "EXPOSE" => null,
            _ => null
        };
        if (keyword == "EXPOSE")
        {
            var ports = rest.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in ports)
            {
                var slash = p.IndexOf('/');
                one.Add(new DockerInstruction { Kind = InstructionKind.Expose, Port = slash < 0 ? p : p[..slash], Protocol = slash < 0 ? "tcp" : p[(slash + 1)..].ToLowerInvariant() });
            }
            if (one.Count == 0 || one.Any(e => Validate(e).Count > 0)) return [Raw(rawText)];
            return one;
        }
        return [result ?? Raw(rawText)];
    }

    private static DockerInstruction? ParseArg(string rest)
    {
        var words = ShellWords(rest);
        if (words == null || words.Count != 1) return null;
        var word = words[0];
        var eq = word.IndexOf('=');
        var name = eq < 0 ? word : word[..eq];
        if (!IsVariableName(name)) return null;
        return new DockerInstruction { Kind = InstructionKind.Arg, Name = name, HasValue = eq >= 0, Value = eq < 0 ? "" : word[(eq + 1)..] };
    }

    private static DockerInstruction? ParsePairs(InstructionKind kind, string rest)
    {
        var words = ShellWords(rest);
        if (words == null || words.Count == 0) return null;
        var instruction = new DockerInstruction { Kind = kind };
        if (!words[0].Contains('='))
        {
            // Legacy "ENV NAME value with spaces" form.
            if (kind != InstructionKind.Env) return null;
            var m = Regex.Match(rest, @"^(\S+)\s+(.*)$", RegexOptions.Singleline, RegexTimeout);
            if (!m.Success) return null;
            instruction.Pairs.Add(new(m.Groups[1].Value, m.Groups[2].Value.Trim()));
            return instruction;
        }
        foreach (var word in words)
        {
            var eq = word.IndexOf('=');
            if (eq <= 0) return null;
            instruction.Pairs.Add(new(word[..eq], word[(eq + 1)..]));
        }
        return instruction;
    }

    /// <summary>Consumes leading COPY/ADD flags from <paramref name="remainder"/>. False when a flag is not modelled.</summary>
    private static bool ParseCopyFlags(DockerInstruction instruction, ref string remainder)
    {
        var kind = instruction.Kind;
        while (true)
        {
            var flag = Regex.Match(remainder, @"^--([a-z-]+)(?:=(\S*))?\s+", RegexOptions.None, RegexTimeout);
            if (!flag.Success) return true;
            var name = flag.Groups[1].Value; var value = flag.Groups[2].Value;
            var bare = !flag.Groups[2].Success;
            switch (name)
            {
                case "from" when kind == InstructionKind.Copy && value.Length > 0: instruction.From = value; break;
                case "chown" when value.Length > 0: instruction.Chown = value; break;
                case "chmod" when value.Length > 0: instruction.Chmod = value; break;
                case "link" when bare || value == "true": instruction.Link = true; break;
                case "link" when value == "false": break;
                case "parents" when kind == InstructionKind.Copy && (bare || value == "true"): instruction.Parents = true; break;
                case "parents" when kind == InstructionKind.Copy && value == "false": break;
                case "exclude" when value.Length > 0: instruction.Excludes.Add(value); break;
                case "checksum" when kind == InstructionKind.Add && value.Length > 0: instruction.Checksum = value; break;
                case "keep-git-dir" when kind == InstructionKind.Add && (bare || value == "true"): instruction.KeepGitDir = true; break;
                case "keep-git-dir" when kind == InstructionKind.Add && value == "false": break;
                default: return false;
            }
            remainder = remainder[flag.Length..];
        }
    }

    private static DockerInstruction? ParseCopy(InstructionKind kind, string rest)
    {
        var instruction = new DockerInstruction { Kind = kind };
        var remainder = rest;
        if (!ParseCopyFlags(instruction, ref remainder)) return null;
        List<string> paths;
        if (remainder.StartsWith('[')) { var json = TryExec(remainder); if (json == null) return null; paths = json; }
        else paths = remainder.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (paths.Count < 2) return null;
        instruction.Sources = paths.Take(paths.Count - 1).ToList();
        instruction.Destination = paths[^1];
        return instruction;
    }

    private static DockerInstruction? ParseRun(string rawText)
    {
        // Work on the raw text so the command keeps its continuation lines exactly.
        var text = rawText.TrimStart();
        var position = 3; // "RUN"
        var instruction = new DockerInstruction { Kind = InstructionKind.Run };
        while (true)
        {
            var skip = Regex.Match(text[position..], @"^(?:\s|\\\n)*", RegexOptions.None, RegexTimeout);
            position += skip.Length;
            if (!text[position..].StartsWith("--", StringComparison.Ordinal)) break;
            var token = Regex.Match(text[position..], @"^--(\S+)", RegexOptions.None, RegexTimeout);
            if (!ApplyRunFlag(instruction, token.Groups[1].Value)) return null;
            position += token.Length;
        }
        var command = text[position..].TrimEnd();
        if (command.Length == 0) return null;
        if (command.StartsWith('[') && TryExec(CollapseContinuations(command.Split('\n'))) is { } exec) { instruction.ExecForm = true; instruction.Arguments = exec; }
        else instruction.Command = command;
        return instruction;
    }

    /// <summary>Applies one RUN flag (without the leading dashes). False when the flag or its value is not modelled.</summary>
    private static bool ApplyRunFlag(DockerInstruction instruction, string flag)
    {
        if (flag.StartsWith("mount=", StringComparison.Ordinal))
        {
            var mount = RunMounts.Parse(flag["mount=".Length..]);
            if (mount == null) return false;
            instruction.Mounts.Add(mount);
            return true;
        }
        if (flag.StartsWith("network=", StringComparison.Ordinal))
        {
            var value = flag["network=".Length..];
            if (value is not ("default" or "none" or "host") || instruction.Network.Length > 0) return false;
            instruction.Network = value;
            return true;
        }
        if (flag.StartsWith("security=", StringComparison.Ordinal))
        {
            var value = flag["security=".Length..];
            if (value is not ("sandbox" or "insecure") || instruction.Security.Length > 0) return false;
            instruction.Security = value;
            return true;
        }
        return false;
    }

    private static readonly Regex HeredocToken = new(@"<<(?<dash>-?)(?<q>[""']?)(?<d>[A-Za-z_][A-Za-z0-9_]*)\k<q>", RegexOptions.None, RegexTimeout);

    /// <summary>
    /// Structured RUN or COPY with exactly one here-document on a single first line. Null when the form is not
    /// modelled (the caller keeps the source as raw text).
    /// </summary>
    private static DockerInstruction? ParseHeredocInstruction(string firstLine, List<string> body)
    {
        var head = Regex.Match(firstLine, @"^\s*(RUN|COPY)\s+(.*?)\s*$", RegexOptions.IgnoreCase, RegexTimeout);
        if (!head.Success) return null;
        var keyword = head.Groups[1].Value.ToUpperInvariant();
        var remainder = head.Groups[2].Value + " ";
        var instruction = new DockerInstruction { Kind = keyword == "RUN" ? InstructionKind.Run : InstructionKind.Copy, Heredoc = true, HeredocBody = string.Join("\n", body) };
        if (instruction.Kind == InstructionKind.Run)
        {
            while (true)
            {
                var flag = Regex.Match(remainder, @"^--(\S+)\s+", RegexOptions.None, RegexTimeout);
                if (!flag.Success) break;
                if (!ApplyRunFlag(instruction, flag.Groups[1].Value)) return null;
                remainder = remainder[flag.Length..];
            }
        }
        else if (!ParseCopyFlags(instruction, ref remainder)) return null;
        remainder = remainder.Trim();
        var token = HeredocToken.Match(remainder);
        if (!token.Success) return null;
        instruction.HeredocDelimiter = token.Groups["d"].Value;
        instruction.HeredocQuoted = token.Groups["q"].Value.Length > 0;
        instruction.HeredocStripTabs = token.Groups["dash"].Value.Length > 0;
        var before = remainder[..token.Index].Trim();
        var after = remainder[(token.Index + token.Length)..].Trim();
        if (after.Length > 0 && !char.IsWhiteSpace(remainder[token.Index + token.Length])) return null;
        if (instruction.Kind == InstructionKind.Copy)
        {
            if (before.Length > 0 || after.Length == 0 || after.Any(char.IsWhiteSpace)) return null;
            instruction.Destination = after;
            return instruction;
        }
        if (before.Length > 0 && after.Length > 0) return null;
        instruction.Interpreter = before.Length > 0 ? before : after;
        return instruction;
    }

    private static DockerInstruction? ParseOnBuild(string rest)
    {
        if (rest.Length == 0) return null;
        var m = Regex.Match(rest, @"^([A-Za-z]+)\b\s*(.*)$", RegexOptions.Singleline, RegexTimeout);
        if (!m.Success) return new DockerInstruction { Kind = InstructionKind.OnBuild, Value = rest };
        var keyword = m.Groups[1].Value.ToUpperInvariant();
        if (keyword is "ONBUILD" or "FROM" or "MAINTAINER") return null;
        var inner = ParseInstruction(keyword, m.Groups[2].Value.Trim(), rest);
        if (inner.Count == 1 && inner[0].Kind != InstructionKind.Raw) return new DockerInstruction { Kind = InstructionKind.OnBuild, Inner = inner[0] };
        return new DockerInstruction { Kind = InstructionKind.OnBuild, Value = rest };
    }

    private static DockerInstruction? ParseVolume(string rest)
    {
        var paths = rest.StartsWith('[') ? TryExec(rest) : rest.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
        return paths is { Count: > 0 } ? new DockerInstruction { Kind = InstructionKind.Volume, Sources = paths } : null;
    }

    private static DockerInstruction? ParseCommand(InstructionKind kind, string rest)
    {
        if (rest.Length == 0) return null;
        if (rest.StartsWith('[') && TryExec(rest) is { } exec) return new DockerInstruction { Kind = kind, ExecForm = true, Arguments = exec };
        return new DockerInstruction { Kind = kind, Command = rest };
    }

    private static DockerInstruction? ParseHealthcheck(string rest)
    {
        if (string.Equals(rest.Trim(), "NONE", StringComparison.OrdinalIgnoreCase)) return new DockerInstruction { Kind = InstructionKind.Healthcheck, Disabled = true };
        var instruction = new DockerInstruction { Kind = InstructionKind.Healthcheck };
        var remainder = rest;
        while (true)
        {
            var flag = Regex.Match(remainder, @"^--([a-z-]+)=(\S+)\s+", RegexOptions.None, RegexTimeout);
            if (!flag.Success) break;
            switch (flag.Groups[1].Value)
            {
                case "interval": instruction.Interval = flag.Groups[2].Value; break;
                case "timeout": instruction.Timeout = flag.Groups[2].Value; break;
                case "start-period": instruction.StartPeriod = flag.Groups[2].Value; break;
                case "retries": instruction.Retries = flag.Groups[2].Value; break;
                default: return null;
            }
            remainder = remainder[flag.Length..];
        }
        var cmd = Regex.Match(remainder, @"^CMD\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout);
        if (!cmd.Success) return null;
        var body = cmd.Groups[1].Value.Trim();
        if (body.StartsWith('[') && TryExec(body) is { } exec) { instruction.ExecForm = true; instruction.Arguments = exec; }
        else instruction.Command = body;
        return instruction;
    }

    /// <summary>Parses an exec-form JSON array of strings; null when the text is not one.</summary>
    public static List<string>? TryExec(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 2 });
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            var list = new List<string>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) return null;
                list.Add(item.GetString()!);
            }
            return list;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Dockerfile word splitting for ARG/ENV/LABEL: whitespace separates words, double quotes allow backslash escapes,
    /// single quotes are literal, and an unquoted backslash escapes the next character. Null on an unterminated quote.
    /// </summary>
    public static List<string>? ShellWords(string text)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        var inWord = false;
        for (var k = 0; k < text.Length; k++)
        {
            var c = text[k];
            if (char.IsWhiteSpace(c)) { if (inWord) { words.Add(current.ToString()); current.Clear(); inWord = false; } continue; }
            inWord = true;
            if (c == '\\' && k + 1 < text.Length) { current.Append(text[++k]); continue; }
            if (c == '"')
            {
                k++;
                while (k < text.Length && text[k] != '"') { if (text[k] == '\\' && k + 1 < text.Length && text[k + 1] is '"' or '\\') k++; current.Append(text[k]); k++; }
                if (k >= text.Length) return null;
                continue;
            }
            if (c == '\'')
            {
                k++;
                while (k < text.Length && text[k] != '\'') { current.Append(text[k]); k++; }
                if (k >= text.Length) return null;
                continue;
            }
            current.Append(c);
        }
        if (inWord) words.Add(current.ToString());
        return words;
    }

    /// <summary>Splits a simple shell command into argv for exec form. Null when it uses shell features (pipes, variables, redirects).</summary>
    public static List<string>? SimpleArgv(string command)
    {
        if (Regex.IsMatch(command, @"[|&;<>()$`*?\n]", RegexOptions.None, RegexTimeout)) return null;
        var words = ShellWords(command);
        return words is { Count: > 0 } ? words : null;
    }

    // ---------------------------------------------------------------- Helpers used by the UI and lint

    /// <summary>The stage name used for references (the alias, or its index when unnamed).</summary>
    public static string StageReference(DockerfileDocument document, int index) => document.Stages[index].Name.Trim().Length > 0 ? document.Stages[index].Name.Trim() : index.ToString(CultureInfo.InvariantCulture);

    /// <summary>Index of the stage the build produces: the named target when it exists, otherwise the last stage.</summary>
    public static int TargetStageIndex(DockerfileDocument document)
    {
        if (document.Target.Trim().Length > 0)
        {
            var found = document.Stages.FindIndex(s => string.Equals(s.Name.Trim(), document.Target.Trim(), StringComparison.OrdinalIgnoreCase));
            if (found >= 0) return found;
        }
        return document.Stages.Count - 1;
    }

    /// <summary>Unique stage name based on <paramref name="baseName"/>.</summary>
    public static string UniqueStageName(DockerfileDocument document, string baseName)
    {
        var name = baseName; var n = 2;
        while (document.Stages.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))) name = baseName + "-" + n++;
        return name;
    }

    public static int InstructionCount(DockerfileDocument document) => document.Stages.Sum(s => s.Instructions.Count) + document.Preamble.Count;

    /// <summary>Splits an image reference into repository, tag and digest. Tags are only recognised after the last slash.</summary>
    public static (string Repository, string Tag, string Digest) SplitImage(string reference)
    {
        var digest = "";
        var at = reference.IndexOf('@');
        if (at >= 0) { digest = reference[(at + 1)..]; reference = reference[..at]; }
        var slash = reference.LastIndexOf('/');
        var colon = reference.LastIndexOf(':');
        if (colon > slash) return (reference[..colon], reference[(colon + 1)..], digest);
        return (reference, "", digest);
    }
}
