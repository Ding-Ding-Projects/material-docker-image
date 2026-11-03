using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>A copyable docker buildx build command and the notes that explain its placeholders.</summary>
public sealed record DockerfileBuildCommand(string Command, IReadOnlyList<string> Notes);

/// <summary>Pure helpers for global build arguments, cross-compilation and the equivalent build command.</summary>
public static class DockerfilePlatform
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>The automatic platform arguments BuildKit defines in the global scope.</summary>
    public static readonly IReadOnlyList<(string Name, string Meaning)> AutomaticArgs =
    [
        ("TARGETPLATFORM", "Platform of the build result, for example linux/arm64."),
        ("TARGETOS", "OS part of TARGETPLATFORM, for example linux."),
        ("TARGETARCH", "Architecture part of TARGETPLATFORM, for example arm64."),
        ("TARGETVARIANT", "Variant part of TARGETPLATFORM, for example v7."),
        ("BUILDPLATFORM", "Platform of the node running the build."),
        ("BUILDOS", "OS part of BUILDPLATFORM."),
        ("BUILDARCH", "Architecture part of BUILDPLATFORM."),
        ("BUILDVARIANT", "Variant part of BUILDPLATFORM."),
    ];

    public static bool IsAutomaticArg(string name) => AutomaticArgs.Any(a => string.Equals(a.Name, name.Trim(), StringComparison.Ordinal));

    /// <summary>Adds ARG declarations (without defaults) to the global preamble when missing. Returns the names added.</summary>
    public static IReadOnlyList<string> AddGlobalArgs(DockerfileDocument document, params string[] names)
    {
        var added = new List<string>();
        foreach (var name in names)
        {
            if (document.Preamble.Any(i => i.Kind == InstructionKind.Arg && string.Equals(i.Name.Trim(), name, StringComparison.Ordinal))) continue;
            var at = document.Preamble.FindLastIndex(i => i.Kind == InstructionKind.Arg) + 1;
            document.Preamble.Insert(at, new DockerInstruction { Kind = InstructionKind.Arg, Name = name });
            added.Add(name);
        }
        return added;
    }

    /// <summary>
    /// Turns a stage into a cross-compilation stage: FROM --platform=$BUILDPLATFORM so the compiler runs natively,
    /// plus ARG TARGETOS and ARG TARGETARCH declared in the stage so its commands can target the requested platform
    /// (for example GOOS=$TARGETOS GOARCH=$TARGETARCH). Returns a description of what changed.
    /// </summary>
    public static string UseBuildPlatform(DockerfileDocument document, int stageIndex)
    {
        var stage = document.Stages[stageIndex];
        var changes = new List<string>();
        if (stage.Platform.Trim() != "$BUILDPLATFORM") { stage.Platform = "$BUILDPLATFORM"; changes.Add("FROM --platform=$BUILDPLATFORM"); }
        var insertAt = 0;
        foreach (var name in new[] { "TARGETOS", "TARGETARCH" })
        {
            if (stage.Instructions.Any(i => i.Kind == InstructionKind.Arg && i.Name.Trim() == name)) continue;
            stage.Instructions.Insert(insertAt++, new DockerInstruction { Kind = InstructionKind.Arg, Name = name });
            changes.Add("ARG " + name);
        }
        return changes.Count == 0 ? "The stage already builds on $BUILDPLATFORM and declares TARGETOS and TARGETARCH." : "Added " + string.Join(", ", changes) + ".";
    }

    /// <summary>True when a stage uses the target platform arguments in its commands (a cross-compilation stage).</summary>
    public static bool UsesTargetArgs(DockerfileStage stage) =>
        stage.Instructions.Any(i => i.Kind == InstructionKind.Run && Regex.IsMatch(DockerfileModel.RunScript(i), @"\$\{?TARGET(OS|ARCH|PLATFORM|VARIANT)\b", RegexOptions.None, RegexTimeout));

    /// <summary>
    /// The docker buildx build command for the document. Secrets and SSH are listed with the flags the build needs
    /// but never with values; the notes say what to provide.
    /// </summary>
    public static DockerfileBuildCommand BuildCommand(DockerfileDocument document, string dockerfile = "Dockerfile", string context = ".", string tag = "")
    {
        var args = new List<string> { "docker", "buildx", "build", "-f", Quote(dockerfile) };
        var notes = new List<string>();
        var target = document.Target.Trim();
        if (target.Length > 0) { args.Add("--target"); args.Add(Quote(target)); }
        if (tag.Trim().Length > 0) { args.Add("-t"); args.Add(Quote(tag.Trim())); }
        var runs = document.Stages.SelectMany(s => s.Instructions).Concat(document.Preamble)
            .Select(i => i.Kind == InstructionKind.OnBuild && i.Inner != null ? i.Inner : i).Where(i => i.Kind == InstructionKind.Run).ToList();
        if (runs.Any(r => string.Equals(r.Security.Trim(), "insecure", StringComparison.OrdinalIgnoreCase)))
        {
            args.Add("--allow"); args.Add("security.insecure");
            notes.Add("--allow security.insecure needs a builder started with the security.insecure entitlement.");
        }
        if (runs.Any(r => string.Equals(r.Network.Trim(), "host", StringComparison.OrdinalIgnoreCase)))
        {
            args.Add("--allow"); args.Add("network.host");
            notes.Add("--allow network.host needs a builder started with the network.host entitlement.");
        }
        var mounts = runs.SelectMany(DockerfileModel.AllMounts).ToList();
        foreach (var id in mounts.Where(m => m.Type == RunMounts.Secret).Select(SecretId).Where(id => id.Length > 0).Distinct(StringComparer.Ordinal))
        {
            args.Add("--secret"); args.Add("id=" + id);
        }
        if (mounts.Any(m => m.Type == RunMounts.Secret))
            notes.Add("Each --secret id=ID reads the environment variable ID; add ,src=FILE to read a file instead. Secret values are never part of the command.");
        foreach (var id in mounts.Where(m => m.Type == RunMounts.Ssh).Select(m => m.Id.Trim().Length > 0 ? m.Id.Trim() : "default").Distinct(StringComparer.Ordinal))
        {
            args.Add("--ssh"); args.Add(id);
        }
        if (mounts.Any(m => m.Type == RunMounts.Ssh)) notes.Add("--ssh forwards your running SSH agent; start ssh-agent and add the key first.");
        if (document.Stages.Any(s => s.Platform.Contains("BUILDPLATFORM", StringComparison.Ordinal)))
            notes.Add("Add --platform linux/amd64,linux/arm64 to cross-compile for several platforms.");
        args.Add(Quote(context));
        return new(string.Join(" ", args), notes);
    }

    private static string SecretId(RunMount m)
    {
        if (m.Id.Trim().Length > 0) return m.Id.Trim();
        var target = m.Target.Trim().TrimEnd('/');
        var slash = target.LastIndexOf('/');
        return slash >= 0 ? target[(slash + 1)..] : target;
    }

    private static string Quote(string value) => value.Length > 0 && !value.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'' or '&' or '|' or ';' or '<' or '>' or '(' or ')' or '$' or '`')
        ? value : "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}

public enum DockerfileTokenKind { Plain, Keyword, Flag, String, Comment, Variable, Directive, HeredocBody }

/// <summary>A coloured span of Dockerfile text: start offset, length and kind.</summary>
public readonly record struct DockerfileToken(int Start, int Length, DockerfileTokenKind Kind);

/// <summary>Pure tokenizer for preview colouring. Every character of the input is covered by exactly one token.</summary>
public static class DockerfileHighlighter
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "FROM", "RUN", "CMD", "LABEL", "MAINTAINER", "EXPOSE", "ENV", "ADD", "COPY", "ENTRYPOINT", "VOLUME", "USER", "WORKDIR", "ARG", "ONBUILD", "STOPSIGNAL", "HEALTHCHECK", "SHELL"
    };
    private static readonly Regex Inline = new(@"(?<flag>--[a-z][a-z-]*(=\S*)?)|(?<str>""(\\.|[^""\\])*""|'[^']*')|(?<var>\$\{[^}\s]*\}|\$[A-Za-z_][A-Za-z0-9_]*)|(?<kw>\b(AS|CMD|NONE)\b)", RegexOptions.None, RegexTimeout);

    public static IReadOnlyList<DockerfileToken> Tokenize(string text)
    {
        var tokens = new List<DockerfileToken>();
        var position = 0;
        var directives = true;
        var continuation = false;
        string? heredoc = null;
        var heredocStrip = false;
        while (position < text.Length)
        {
            var end = text.IndexOf('\n', position);
            var lineEnd = end < 0 ? text.Length : end;
            var line = text[position..lineEnd];
            if (heredoc != null)
            {
                Add(tokens, position, line.Length, DockerfileTokenKind.HeredocBody);
                if ((heredocStrip ? line.TrimStart('\t') : line).TrimEnd() == heredoc) { heredoc = null; ReplaceLast(tokens, DockerfileTokenKind.Keyword); }
            }
            else if (line.TrimStart().StartsWith('#'))
            {
                var isDirective = directives && Regex.IsMatch(line, @"^\s*#\s*[A-Za-z]+\s*=", RegexOptions.None, RegexTimeout);
                Add(tokens, position, line.Length, isDirective ? DockerfileTokenKind.Directive : DockerfileTokenKind.Comment);
            }
            else
            {
                if (line.Trim().Length > 0) directives = false;
                var offset = 0;
                if (!continuation)
                {
                    var m = Regex.Match(line, @"^(\s*)([A-Za-z]+)", RegexOptions.None, RegexTimeout);
                    if (m.Success && Keywords.Contains(m.Groups[2].Value))
                    {
                        Add(tokens, position, m.Groups[1].Length, DockerfileTokenKind.Plain);
                        Add(tokens, position + m.Groups[1].Length, m.Groups[2].Length, DockerfileTokenKind.Keyword);
                        offset = m.Length;
                        var inner = Regex.Match(line[offset..], @"^(\s+)([A-Za-z]+)\b", RegexOptions.None, RegexTimeout);
                        if (m.Groups[2].Value.Equals("ONBUILD", StringComparison.OrdinalIgnoreCase) && inner.Success && Keywords.Contains(inner.Groups[2].Value))
                        {
                            Add(tokens, position + offset, inner.Groups[1].Length, DockerfileTokenKind.Plain);
                            Add(tokens, position + offset + inner.Groups[1].Length, inner.Groups[2].Length, DockerfileTokenKind.Keyword);
                            offset += inner.Length;
                        }
                    }
                }
                var rest = line[offset..];
                var cursor = 0;
                foreach (Match m in Inline.Matches(rest))
                {
                    Add(tokens, position + offset + cursor, m.Index - cursor, DockerfileTokenKind.Plain);
                    var kind = m.Groups["flag"].Success ? DockerfileTokenKind.Flag : m.Groups["str"].Success ? DockerfileTokenKind.String : m.Groups["var"].Success ? DockerfileTokenKind.Variable : DockerfileTokenKind.Keyword;
                    Add(tokens, position + offset + m.Index, m.Length, kind);
                    cursor = m.Index + m.Length;
                }
                Add(tokens, position + offset + cursor, rest.Length - cursor, DockerfileTokenKind.Plain);
                var token = Regex.Match(line, @"<<(-?)[""']?([A-Za-z_][A-Za-z0-9_]*)[""']?", RegexOptions.None, RegexTimeout);
                if (token.Success && !continuation && Regex.IsMatch(line, @"^\s*(RUN|COPY|ADD)\b", RegexOptions.IgnoreCase, RegexTimeout)) { heredoc = token.Groups[2].Value; heredocStrip = token.Groups[1].Value == "-"; }
                continuation = line.TrimEnd().EndsWith('\\');
            }
            if (end < 0) break;
            Add(tokens, end, 1, DockerfileTokenKind.Plain);
            position = end + 1;
        }
        return tokens;
    }

    private static void Add(List<DockerfileToken> tokens, int start, int length, DockerfileTokenKind kind)
    {
        if (length <= 0) return;
        if (tokens.Count > 0 && tokens[^1].Kind == kind && tokens[^1].Start + tokens[^1].Length == start)
            tokens[^1] = tokens[^1] with { Length = tokens[^1].Length + length };
        else tokens.Add(new DockerfileToken(start, length, kind));
    }

    private static void ReplaceLast(List<DockerfileToken> tokens, DockerfileTokenKind kind)
    {
        if (tokens.Count > 0) tokens[^1] = tokens[^1] with { Kind = kind };
    }
}
