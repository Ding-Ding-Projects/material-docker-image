using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>One Compose YAML line reduced to what indentation-based suggestions need.</summary>
public readonly record struct ComposeYamlLine(int Start, string Text, int Indent, bool Dash, int ContentIndent, string? Key, bool HasValue, bool Blank);

/// <summary>Where the caret is in a Compose file: the key path above it and whether a key or a value is being typed.</summary>
public sealed record ComposeCaretContext(string Path, IReadOnlyList<string> Keys, bool TypingValue, string? Key, int Start, string Query, bool ListItem, string ServiceName)
{
    public string Parent => Keys.Count == 0 ? "" : Keys[^1];
}

/// <summary>
/// Pragmatic, indentation-based Compose YAML suggestions (pure logic): top-level keys, service keys and nested
/// sections by context, value sets, service names for depends_on and links, and named volumes and networks.
/// </summary>
public static class ComposeCompletion
{
    private static readonly Regex KeyRx = new(@"^(""[^""]*""|'[^']*'|[A-Za-z0-9_.\-/]+)\s*:(?=\s|$)", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> NamedSections = new(StringComparer.Ordinal) { "services", "volumes", "networks", "secrets", "configs" };

    public static IReadOnlyList<ComposeYamlLine> Lines(string text)
    {
        var result = new List<ComposeYamlLine>();
        var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] != '\n') continue;
            var line = text[start..i].TrimEnd('\r');
            result.Add(Parse(line, start));
            start = i + 1;
        }
        return result;
    }

    public static ComposeYamlLine Parse(string line, int start)
    {
        var indent = line.TakeWhile(c => c == ' ').Count();
        var rest = line[indent..];
        if (rest.Trim().Length == 0 || rest.StartsWith('#')) return new(start, line, indent, false, indent, null, false, true);
        var dash = rest == "-" || rest.StartsWith("- ", StringComparison.Ordinal);
        var content = dash ? rest[1..].TrimStart() : rest;
        var contentIndent = dash ? line.Length - content.Length : indent;
        var m = KeyRx.Match(content);
        string? key = m.Success ? m.Groups[1].Value.Trim('"', '\'') : null;
        var after = m.Success ? content[m.Length..].Trim() : "";
        var hasValue = after.Length > 0 && !after.StartsWith('#');
        return new(start, line, indent, dash, contentIndent, key, hasValue, false);
    }

    /// <summary>Works out the key path and what is being typed at the caret.</summary>
    public static ComposeCaretContext Analyse(string text, int caret)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        var lines = Lines(text);
        var row = 0;
        for (var i = 0; i < lines.Count; i++) if (lines[i].Start <= caret) row = i;
        var lineStart = lines[row].Start;
        var before = lines[row].Text[..Math.Min(lines[row].Text.Length, caret - lineStart)];
        var current = Parse(before, lineStart);
        var indent = before.TakeWhile(c => c == ' ').Count();
        var rest = before[indent..];
        var dash = rest == "-" || rest.StartsWith("- ", StringComparison.Ordinal);
        var content = dash ? rest[1..].TrimStart() : rest;
        var contentStart = lineStart + before.Length - content.Length;
        var contentIndent = before.Length - content.Length;

        var keys = new List<string>();
        var target = dash ? indent : contentIndent;
        var compact = dash;
        var listItem = dash;
        if (dash) keys.Add("-");
        for (var k = row - 1; k >= 0 && target > 0; k--)
        {
            var ln = lines[k];
            if (ln.Blank) continue;
            if (ln.Dash && ln.Indent < target && target <= ln.ContentIndent && !(ln.Key != null && !ln.HasValue && ln.ContentIndent < target))
            {
                keys.Insert(0, "-");
                target = ln.Indent;
                compact = true;
                continue;
            }
            if (ln.Key != null && !ln.HasValue && (ln.ContentIndent < target || (compact && ln.ContentIndent == target && !ln.Dash)))
            {
                keys.Insert(0, ln.Key);
                target = ln.ContentIndent;
                compact = false;
                if (ln.Dash) { keys.Insert(0, "-"); target = ln.Indent; compact = true; }
            }
        }
        // Normalise user-defined names (service, volume, network names and depends_on entries) to "*".
        var normal = new List<string>(keys);
        var service = "";
        for (var i = 0; i < normal.Count; i++)
        {
            if (i == 1 && NamedSections.Contains(normal[0]) && normal[1] != "-") { if (normal[0] == "services") service = normal[1]; normal[1] = "*"; }
            if (i >= 3 && normal[0] == "services" && normal[i - 1] == "depends_on" && normal[i] != "-") normal[i] = "*";
        }
        var path = string.Join("/", normal);

        var keyMatch = KeyRx.Match(content);
        if (keyMatch.Success)
        {
            var valueText = content[keyMatch.Length..];
            var lead = valueText.TakeWhile(char.IsWhiteSpace).Count();
            var value = valueText[lead..];
            var quote = value.StartsWith('"') || value.StartsWith('\'') ? 1 : 0;
            var start = contentStart + keyMatch.Length + lead + quote;
            return new ComposeCaretContext(path, normal, true, keyMatch.Groups[1].Value.Trim('"', '\''), start, value[quote..], listItem, service);
        }
        var q = content.StartsWith('"') || content.StartsWith('\'') ? 1 : 0;
        return new ComposeCaretContext(path, normal, false, null, contentStart + q, content[q..], listItem, service);
    }

    /// <summary>Names defined directly under a top-level section (services, volumes, networks, secrets, configs).</summary>
    public static IReadOnlyList<string> SectionNames(string text, string section) => SectionNames(Lines(text), section);

    private static IReadOnlyList<string> SectionNames(IReadOnlyList<ComposeYamlLine> lines, string section)
    {
        var names = new List<string>();
        var inside = false;
        var childIndent = -1;
        foreach (var ln in lines)
        {
            if (ln.Blank) continue;
            if (ln.Indent == 0 && !ln.Dash) { inside = ln.Key == section; childIndent = -1; continue; }
            if (!inside || ln.Dash || ln.Key == null) continue;
            if (childIndent < 0) childIndent = ln.Indent;
            if (ln.Indent == childIndent && !names.Contains(ln.Key)) names.Add(ln.Key);
        }
        return names;
    }

    public static SuggestionResult Complete(string text, int caret, SuggestionSources? sources = null)
    {
        text ??= "";
        sources ??= SuggestionSources.None;
        var ctx = Analyse(text, caret);
        var lines = Lines(text);
        var items = ctx.TypingValue ? ValueItems(ctx, lines, sources) : ctx.ListItem && ctx.Keys.Count >= 1 && ctx.Keys[^1] == "-" && ListScalarItems(ctx, lines) is { } scalars ? scalars : KeyItems(ctx, lines);
        var value = ComposeValueKinds.Detect(text, caret, ctx);
        var all = items.ToList();
        if (value != null) all.InsertRange(0, DockerfileValueKinds.PickerItems(value));
        var signature = ctx.Path.Length == 0 ? "Top level" : "In " + ctx.Path.Replace("/*", "/<name>").Replace("/-", "[]");
        return new SuggestionResult(ctx.Start, Math.Max(0, caret - ctx.Start), text[ctx.Start..Math.Max(ctx.Start, caret)], all, signature) { AutoTrigger = items.Count > 0, Value = value };
    }

    private static SuggestionItem KeyItem(ComposeKeyInfo k)
    {
        var insert = k.Shape switch
        {
            ComposeKeyShape.Map => k.Key + ":\n  " + SuggestionText.CaretMark,
            ComposeKeyShape.List => k.Key + ":\n  - " + SuggestionText.CaretMark,
            _ => k.Key + ": ",
        };
        return new SuggestionItem(k.Key, insert, SuggestionKind.Key, k.Shape == ComposeKeyShape.Scalar ? "Key" : k.Shape == ComposeKeyShape.Map ? "Section" : "List", k.Doc, k.Example) { Retrigger = k.Shape == ComposeKeyShape.Scalar };
    }

    private static IReadOnlyList<SuggestionItem> KeyItems(ComposeCaretContext ctx, IReadOnlyList<ComposeYamlLine> lines)
    {
        ComposeKeyInfo[]? keys = ctx.Path switch
        {
            "" => ComposeCompletionData.TopLevel,
            "services/*" => ComposeCompletionData.Service,
            "services/*/build" => ComposeCompletionData.Build,
            "services/*/healthcheck" => ComposeCompletionData.Healthcheck,
            "services/*/depends_on/*" => ComposeCompletionData.DependsOnEntry,
            "services/*/develop" => ComposeCompletionData.Develop,
            "services/*/develop/watch/-" => ComposeCompletionData.WatchRule,
            "services/*/deploy" => ComposeCompletionData.Deploy,
            "services/*/deploy/resources" => ComposeCompletionData.Resources,
            "services/*/deploy/resources/limits" => ComposeCompletionData.Limits,
            "services/*/deploy/resources/reservations" => ComposeCompletionData.Reservations,
            "services/*/logging" => ComposeCompletionData.Logging,
            "services/*/ports/-" => ComposeCompletionData.PortLong,
            "services/*/volumes/-" => ComposeCompletionData.VolumeLong,
            "volumes/*" => ComposeCompletionData.VolumeTop,
            "networks/*" => ComposeCompletionData.NetworkTop,
            "secrets/*" => ComposeCompletionData.SecretTop,
            "configs/*" => ComposeCompletionData.ConfigTop,
            _ => null,
        };
        if (keys != null) return keys.Select(KeyItem).ToList();
        return ctx.Path switch
        {
            "services/*/depends_on" => SectionNames(lines, "services").Where(n => n != ctx.ServiceName)
                .Select(n => new SuggestionItem(n, n + ":\n  condition: " + SuggestionText.CaretMark, SuggestionKind.Stage, "Service", "Waits for this service before starting.") { Retrigger = true }).ToList(),
            "services/*/networks" => SectionNames(lines, "networks").Select(n => new SuggestionItem(n, n + ":", SuggestionKind.Value, "Network", "Defined in the top-level networks section.")).ToList(),
            _ => Array.Empty<SuggestionItem>(),
        };
    }

    private static IReadOnlyList<SuggestionItem>? ListScalarItems(ComposeCaretContext ctx, IReadOnlyList<ComposeYamlLine> lines)
    {
        if (ctx.Keys.Count < 2) return null;
        var owner = ctx.Keys[^2];
        var inService = ctx.Keys.Count == 4 && ctx.Keys[0] == "services";
        if (!inService) return owner == "platforms" ? DockerfileCompletionData.Platforms.Select(p => new SuggestionItem(p, p, SuggestionKind.Value, "Platform")).ToList() : null;
        return owner switch
        {
            "depends_on" or "links" => SectionNames(lines, "services").Where(n => n != ctx.ServiceName).Select(n => new SuggestionItem(n, n, SuggestionKind.Stage, "Service", "Another service in this file.")).ToList(),
            "volumes" => SectionNames(lines, "volumes").Select(n => new SuggestionItem(n + ":", n + ":", SuggestionKind.Value, "Named volume", "Defined in the top-level volumes section; type the container path after the colon.")).ToList(),
            "networks" => SectionNames(lines, "networks").Select(n => new SuggestionItem(n, n, SuggestionKind.Value, "Network", "Defined in the top-level networks section.")).ToList(),
            "secrets" => SectionNames(lines, "secrets").Select(n => new SuggestionItem(n, n, SuggestionKind.Value, "Secret", "Mounted at /run/secrets/" + n + ".")).ToList(),
            "configs" => SectionNames(lines, "configs").Select(n => new SuggestionItem(n, n, SuggestionKind.Value, "Config", "Defined in the top-level configs section.")).ToList(),
            "profiles" => ProfileLines(lines).Select(p => new SuggestionItem(p, p, SuggestionKind.Value, "Profile used in this file")).ToList(),
            "cap_add" or "cap_drop" => ComposeCompletionData.Capabilities.Select(c => new SuggestionItem(c, c, SuggestionKind.Value, "Linux capability")).ToList(),
            _ => null,
        };
    }

    private static HashSet<string> ProfileLines(IReadOnlyList<ComposeYamlLine> lines)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Key != "profiles") continue;
            var text = lines[i].Text;
            var colon = text.IndexOf(':');
            var inline = text[(colon + 1)..].Trim();
            if (inline.StartsWith('[')) foreach (var p in inline.Trim('[', ']').Split(',')) set.Add(p.Trim().Trim('"', '\''));
            for (var k = i + 1; k < lines.Count && (lines[k].Blank || lines[k].Dash && lines[k].Indent >= lines[i].Indent); k++)
                if (lines[k].Dash) set.Add(lines[k].Text.Trim()[1..].Trim().Trim('"', '\''));
        }
        set.Remove("");
        return set;
    }

    private static IReadOnlyList<SuggestionItem> ValueItems(ComposeCaretContext ctx, IReadOnlyList<ComposeYamlLine> lines, SuggestionSources sources)
    {
        var key = ctx.Key!;
        SuggestionItem V(string value, string doc) => new(value, value, SuggestionKind.Value, key, doc);
        if (ComposeCompletionData.BooleanKeys.Contains(key)) return [V("true", "Enabled."), V("false", "Disabled.")];
        switch (key)
        {
            case "image" when ctx.Path == "services/*":
                return sources.Images.Where(i => i.Repository is { Length: > 0 } and not "<none>").Select(i => new SuggestionItem(i.Reference, i.Reference, SuggestionKind.Image, "Local image", $"On this engine ({i.Size}).") { Rank = 0 })
                    .Concat(DockerfileCompletionData.PopularImages.Where(p => p.Name != "scratch").Select(p => new SuggestionItem(p.Name, p.Name, SuggestionKind.Image, "Popular official images", p.Doc + " From the built-in curated list; add a tag.") { Rank = 2 })).ToList();
            case "driver" when ctx.Path == "volumes/*": return [V("local", "The built-in local driver.")];
            case "driver" when ctx.Path == "networks/*": return new[] { "bridge", "overlay", "host", "none", "macvlan", "ipvlan" }.Select(d => V(d, "Network driver.")).ToList();
            case "driver" when ctx.Path == "services/*/logging": return new[] { "json-file", "local", "journald", "syslog", "fluentd", "gelf", "awslogs", "none" }.Select(d => V(d, "Logging driver.")).ToList();
            case "mode" when ctx.Path == "services/*/deploy": return [V("replicated", "A fixed number of replicas."), V("global", "One container per node.")];
            case "mode" when ctx.Path == "services/*/ports/-": return [V("host", "Publish on each node."), V("ingress", "Load-balanced (Swarm).")];
            case "type" when ctx.Path == "services/*/volumes/-": return new[] { "bind", "volume", "tmpfs", "npipe" }.Select(d => V(d, "Mount type.")).ToList();
            case "restart" when ctx.Path == "services/*/depends_on/*": return [V("true", "Restart when the dependency updates."), V("false", "Do not restart.")];
            case "network_mode":
                return new[] { V("bridge", "Default bridge network."), V("host", "Share the host's network stack."), V("none", "No networking.") }
                    .Concat(SectionNames(lines, "services").Where(n => n != ctx.ServiceName).Select(n => V("service:" + n, "Share the network stack of service " + n + "."))).ToList();
            case "platform": return DockerfileCompletionData.Platforms.Select(p => V(p, "Platform.")).ToList();
            case "target" when ctx.Path == "services/*/build": return Array.Empty<SuggestionItem>();
            case "stop_signal": return DockerfileCompletionData.Signals.Select(s => V(s.Name, s.Doc)).ToList();
            case "interval" or "timeout" or "start_period" or "start_interval" or "stop_grace_period": return new[] { "5s", "10s", "30s", "1m" }.Select(d => V(d, "Duration.")).ToList();
        }
        if (ComposeCompletionData.Values.TryGetValue(key, out var set)) return set.Select(v => V(v.Value, v.Doc)).ToList();
        return Array.Empty<SuggestionItem>();
    }
}

/// <summary>Detects the kind of value at the caret in a Compose file (pure logic).</summary>
public static class ComposeValueKinds
{
    public static EditorValueSpan? Detect(string text, int caret) => Detect(text, caret, ComposeCompletion.Analyse(text, caret));

    public static EditorValueSpan? Detect(string text, int caret, ComposeCaretContext ctx)
    {
        if (EditorValueFormats.TryFindColour(text, caret, out var cs, out var cl)) return new(EditorValueKind.Colour, cs, cl, text.Substring(cs, cl), "Colour") { Compose = true };
        var lineEnd = SuggestionText.LineEnd(text, caret);
        var start = Math.Min(ctx.Start, lineEnd);
        var raw = text[start..lineEnd];
        var hash = raw.IndexOf(" #", StringComparison.Ordinal);
        if (hash >= 0) raw = raw[..hash];
        raw = raw.TrimEnd();
        if (raw.EndsWith('"') || raw.EndsWith('\'')) raw = raw[..^1];
        EditorValueSpan Span(EditorValueKind kind, string label) => new(kind, start, raw.Length, raw, label) { Compose = true };
        var owner = ctx.Keys.Count >= 2 ? ctx.Keys[^2] : "";
        if (!ctx.TypingValue)
        {
            if (!ctx.ListItem || ctx.Keys.Count != 4 || ctx.Keys[0] != "services") return null;
            if (owner == "ports") return Span(EditorValueKind.Port, "Published port");
            if (owner == "env_file") return Span(EditorValueKind.Path, "Environment file");
            if (owner == "volumes")
            {
                var colon = raw.IndexOf(':');
                var host = colon < 0 ? raw : raw[..colon];
                if (host.StartsWith('.') || host.StartsWith('/') || host.StartsWith('~') || (host.Length > 1 && host[1] == ':'))
                    return host.Length >= caret - start || colon < 0 ? new EditorValueSpan(EditorValueKind.Path, start, host.Length, host, "Bind mount host path") { Compose = true } : null;
            }
            return null;
        }
        switch (ctx.Key)
        {
            case "image" when ctx.Path == "services/*": return Span(EditorValueKind.Image, "Service image");
            case "build" when ctx.Path == "services/*": return Span(EditorValueKind.Path, "Build context") with { FolderOnly = true };
            case "context" when ctx.Path == "services/*/build": return Span(EditorValueKind.Path, "Build context") with { FolderOnly = true };
            case "dockerfile" when ctx.Path == "services/*/build": return Span(EditorValueKind.Path, "Dockerfile");
            case "env_file" when ctx.Path == "services/*": return Span(EditorValueKind.Path, "Environment file");
            case "file" when ctx.Path is "secrets/*" or "configs/*": return Span(EditorValueKind.Path, "Secret or config file");
            case "path" when ctx.Path == "services/*/develop/watch/-": return Span(EditorValueKind.Path, "Watched path");
            case "interval" or "timeout" or "start_period" or "start_interval" when ctx.Path == "services/*/healthcheck": return Span(EditorValueKind.Duration, "Health check " + ctx.Key.Replace('_', ' '));
            case "stop_grace_period": return Span(EditorValueKind.Duration, "Stop grace period");
            case "user" when ctx.Path == "services/*": return Span(EditorValueKind.User, "User");
            case "platform": return Span(EditorValueKind.Platform, "Platform");
            case "published" or "target" when ctx.Path == "services/*/ports/-": return null;
        }
        var key = ctx.Key!;
        string[]? options = ComposeCompletionData.BooleanKeys.Contains(key) ? ["true", "false"]
            : key == "network_mode" ? ["bridge", "host", "none"]
            : ComposeCompletionData.Values.TryGetValue(key, out var set) ? set.Select(v => v.Value).ToArray() : null;
        if (key == "restart" && ctx.Path == "services/*/depends_on/*") options = ["true", "false"];
        return options == null ? null : Span(EditorValueKind.Choice, key) with { Options = options };
    }
}
