using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>Kinds of typed values that have an interactive picker.</summary>
public enum EditorValueKind { None, Path, Colour, Port, Duration, Chmod, User, Platform, Image, Secret, Choice }

/// <summary>The value at the caret, its exact text span and what the picker needs to know.</summary>
public sealed record EditorValueSpan(EditorValueKind Kind, int Start, int Length, string Text, string Label)
{
    public IReadOnlyList<string> Options { get; init; } = Array.Empty<string>();
    /// <summary>Paths must stay inside the build context (COPY and ADD sources, bind mount sources).</summary>
    public bool ContextOnly { get; init; }
    public bool FolderOnly { get; init; }
    /// <summary>Compose syntax (ports with host side and IP, Compose paths relative to the project folder).</summary>
    public bool Compose { get; init; }
    /// <summary>Symbolic permissions (u=rwX) are allowed (COPY/ADD --chmod).</summary>
    public bool SymbolicAllowed { get; init; }
    public string SecretName { get; init; } = "";
    /// <summary>Absolute range of the whole instruction or line (for rewrites).</summary>
    public int ScopeStart { get; init; }
    public int ScopeLength { get; init; }
    public IReadOnlyList<string> KnownUsers { get; init; } = Array.Empty<string>();
}

/// <summary>Detects the kind of value at the caret in a Dockerfile (pure logic, no UI).</summary>
public static class DockerfileValueKinds
{
    private static readonly Regex SecretNameRx = new(@"(?i)(pass(word|wd)?|secret|token|api[_-]?key|private[_-]?key|access[_-]?key|credential|auth)", RegexOptions.CultureInvariant);

    public static bool LooksSecret(string name) => name.Length > 0 && SecretNameRx.IsMatch(name);

    public static EditorValueSpan? Detect(string text, int caret) => Detect(DockerfileCaretScan.Run(text, caret));

    public static EditorValueSpan? Detect(DockerfileCaretScan s)
    {
        if (EditorValueFormats.TryFindColour(s.Text, s.Caret, out var cs, out var cl)) return new(EditorValueKind.Colour, cs, cl, s.Text.Substring(cs, cl), "Colour");
        if (s.CommentLine || s.InHeredoc || s.TypingKeyword || s.Keyword == null || s.Current == null) return null;
        var token = s.Text[s.TokenStart..s.TokenEnd];
        var keyword = s.Keyword;
        var scopeStart = s.Current.Tokens[0].Start;
        var lastToken = s.Current.Tokens[^1];
        var scope = (Start: scopeStart, Length: lastToken.End - scopeStart);
        var users = s.Users.Concat(["root", "nobody"]).Distinct().ToList();

        if (token.StartsWith("--", StringComparison.Ordinal))
        {
            var eq = token.IndexOf('=');
            if (eq < 0 || s.Caret <= s.TokenStart + eq) return null;
            var flag = token[2..eq].ToLowerInvariant();
            var vStart = s.TokenStart + eq + 1;
            var value = token[(eq + 1)..];
            EditorValueSpan Span(EditorValueKind kind, string label) => new(kind, vStart, value.Length, value, label) { ScopeStart = scope.Start, ScopeLength = scope.Length, KnownUsers = users };
            if (flag == "mount") return MountPart(s, vStart, value, users);
            return flag switch
            {
                "chmod" => Span(EditorValueKind.Chmod, "Permissions") with { SymbolicAllowed = true },
                "chown" => Span(EditorValueKind.User, "Owner"),
                "platform" => Span(EditorValueKind.Platform, "Platform"),
                "interval" or "timeout" or "start-period" or "start-interval" when keyword == "HEALTHCHECK" => Span(EditorValueKind.Duration, "Health check " + flag.Replace('-', ' ')),
                "network" => Span(EditorValueKind.Choice, "Network") with { Options = DockerfileCompletionData.NetworkModes.Values },
                "security" => Span(EditorValueKind.Choice, "Security") with { Options = DockerfileCompletionData.SecurityModes.Values },
                _ when DockerfileCompletionData.FlagAllowed(keyword, flag) is { } allowed => Span(EditorValueKind.Choice, "--" + flag) with { Options = allowed.Values },
                _ => null,
            };
        }

        var positional = s.Args.Where(t => !t.Text.StartsWith("--", StringComparison.Ordinal)).ToList();
        var index = positional.FindIndex(t => t.Start == s.TokenStart);
        if (index < 0) return null;
        EditorValueSpan Token(EditorValueKind kind, string label) => new(kind, s.TokenStart, token.Length, token, label) { ScopeStart = scope.Start, ScopeLength = scope.Length, KnownUsers = users };
        switch (keyword)
        {
            case "FROM" when index == 0:
                return Token(EditorValueKind.Image, "Base image");
            case "COPY" or "ADD":
            {
                if (s.Args.Any(a => a.Text.StartsWith("--from=", StringComparison.OrdinalIgnoreCase))) return null;
                if (token.StartsWith("<<", StringComparison.Ordinal) || token.Contains("://", StringComparison.Ordinal) || token.StartsWith('[')) return null;
                var all = s.Current.Args.Where(t => !t.Text.StartsWith("--", StringComparison.Ordinal)).ToList();
                var isDestination = all.Count >= 2 && all[^1].Start == s.TokenStart;
                if (isDestination || token.StartsWith('/')) return null;
                return Token(EditorValueKind.Path, keyword + " source") with { ContextOnly = true };
            }
            case "EXPOSE":
                return Token(EditorValueKind.Port, "Exposed port");
            case "STOPSIGNAL" when index == 0:
                return Token(EditorValueKind.Choice, "Stop signal") with { Options = DockerfileCompletionData.Signals.Select(x => x.Name).ToArray() };
            case "USER" when index == 0:
                return Token(EditorValueKind.User, "User");
            case "ARG" or "ENV":
            {
                var name = token.Split('=')[0];
                if (keyword == "ENV" && !token.Contains('=') && index > 0 && positional.Count > 0 && !positional[0].Text.Contains('=')) name = positional[0].Text;
                if (LooksSecret(name)) return Token(EditorValueKind.Secret, keyword + " " + name) with { SecretName = name };
                return null;
            }
        }
        return null;
    }

    private static EditorValueSpan? MountPart(DockerfileCaretScan s, int valueStart, string value, IReadOnlyList<string> users)
    {
        var parts = value.Split(',');
        var offset = 0;
        var type = "bind";
        var hasFrom = false;
        foreach (var p in parts)
        {
            var kv = p.Split('=', 2);
            if (kv[0] == "type" && kv.Length == 2) type = kv[1];
            if (kv[0] == "from" && kv.Length == 2) hasFrom = true;
        }
        foreach (var part in parts)
        {
            var partStart = valueStart + offset;
            var partEnd = partStart + part.Length;
            offset += part.Length + 1;
            if (s.Caret < partStart || s.Caret > partEnd) continue;
            var eq = part.IndexOf('=');
            if (eq < 0 || s.Caret <= partStart + eq) return null;
            var key = part[..eq].ToLowerInvariant();
            var vs = partStart + eq + 1;
            var v = part[(eq + 1)..];
            EditorValueSpan Span(EditorValueKind kind, string label) => new(kind, vs, v.Length, v, label) { KnownUsers = users };
            return key switch
            {
                "type" => Span(EditorValueKind.Choice, "Mount type") with { Options = DockerfileCompletionData.MountTypeSet.Values },
                "sharing" => Span(EditorValueKind.Choice, "Cache sharing") with { Options = DockerfileCompletionData.CacheSharing.Values },
                _ when DockerfileCompletionData.MountAllowed(type, key) is { } allowed => Span(EditorValueKind.Choice, key) with { Options = allowed.Values },
                "mode" => Span(EditorValueKind.Chmod, "Mount mode"),
                "uid" or "gid" => Span(EditorValueKind.User, "Mount " + key) with { Options = ["numeric"] },
                "source" or "src" when type == "bind" && !hasFrom => Span(EditorValueKind.Path, "Bind mount source") with { ContextOnly = true },
                _ => null,
            };
        }
        return null;
    }

    /// <summary>The picker items offered for a detected value.</summary>
    public static IEnumerable<SuggestionItem> PickerItems(EditorValueSpan value)
    {
        SuggestionItem P(string label, string doc, string variant = "") => new(label, "", SuggestionKind.Picker, value.Label, doc) { PickerKind = value.Kind, PickerVariant = variant, Rank = -10 };
        switch (value.Kind)
        {
            case EditorValueKind.Path:
                if (!value.FolderOnly) yield return P("Browse file…", value.ContextOnly ? "Choose a file inside the build context. The path is inserted relative to the context with forward slashes." : "Choose a file. The path is inserted relative to the project folder.", "file");
                yield return P("Browse folder…", value.ContextOnly ? "Choose a folder inside the build context." : "Choose a folder. The path is inserted relative to the project folder.", "folder");
                break;
            case EditorValueKind.Colour: yield return P("Pick colour…", "Opens a colour picker and replaces this colour in place."); break;
            case EditorValueKind.Port: yield return P("Edit port…", value.Compose ? "Container port, protocol and the optional host port and IP." : "Container port and protocol."); break;
            case EditorValueKind.Duration: yield return P("Pick duration…", "Presets and a number with a unit (ms, s, m, h)."); break;
            case EditorValueKind.Chmod: yield return P("Edit permissions…", "Read, write and execute for owner, group and others."); break;
            case EditorValueKind.User: yield return P("Pick user…", "Common users and a numeric uid:gid editor."); break;
            case EditorValueKind.Platform: yield return P("Pick platform…", "Common operating system and architecture pairs."); break;
            case EditorValueKind.Image: yield return P("Pick local image…", "Local images with tags and digests. Pin digest adds @sha256 from the local repository digest."); break;
            case EditorValueKind.Secret: yield return P("Use a build secret instead", "Values of ARG and ENV stay in the image history. This rewrites the instruction to a RUN --mount=type=secret so the value is passed with docker build --secret and never stored. The current value is removed and not shown."); break;
            case EditorValueKind.Choice: yield return P("Pick…", "Shows every allowed value as a chip."); break;
        }
    }
}
