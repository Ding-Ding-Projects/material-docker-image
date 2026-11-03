using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>
/// Context-aware Dockerfile suggestions (pure logic): instructions and directives at line start, flags after a
/// keyword, RUN --mount option keys and values, stage names, ARG and ENV names in scope, images and tags, build
/// context paths, and values for EXPOSE, STOPSIGNAL, SHELL, USER and HEALTHCHECK. Items needing a newer
/// # syntax than the file declares carry a badge.
/// </summary>
public static class DockerfileCompletion
{
    private static readonly Regex DirectiveNameRx = new(@"^#\s*([A-Za-z]*)$", RegexOptions.CultureInvariant);
    private static readonly Regex DirectiveValueRx = new(@"^#\s*([A-Za-z]+)\s*=\s*(\S*)$", RegexOptions.CultureInvariant);
    private static readonly Regex VariableRx = new(@"\$(\{?)([A-Za-z0-9_]*)$", RegexOptions.CultureInvariant);

    public static SuggestionResult Complete(string text, int caret, SuggestionSources? sources = null)
    {
        sources ??= SuggestionSources.None;
        var s = DockerfileCaretScan.Run(text ?? "", caret);
        var result = Core(s, sources);
        var value = DockerfileValueKinds.Detect(s);
        var items = result.Items.Select(i => Badge(i, s.Syntax)).ToList();
        if (value != null) items.InsertRange(0, DockerfileValueKinds.PickerItems(value));
        return result with { Items = items, Value = value, Syntax = s.Syntax };
    }

    private static SuggestionItem Badge(SuggestionItem item, DockerfileSyntaxVersion? syntax)
    {
        if (item.MinSyntax == null) return item;
        var ok = syntax != null && syntax.Satisfies(item.MinSyntax, item.LabsMinSyntax);
        return ok ? item : item with { Badge = item.MinSyntax + "+", NeedsSyntaxUpgrade = true };
    }

    private static SuggestionResult Core(DockerfileCaretScan s, SuggestionSources sources)
    {
        var caret = s.Caret;
        if (s.CommentLine) return s.DirectiveRegion ? Directive(s) : SuggestionResult.Empty;

        var variable = VariableRx.Match(s.Text[s.TokenStart..caret]);
        if (variable.Success && !EscapedDollar(s, s.TokenStart + variable.Index))
        {
            var braced = variable.Groups[1].Value == "{";
            var nameStart = s.TokenStart + variable.Groups[2].Index;
            var close = braced && (caret >= s.Text.Length || s.Text[caret] != '}') ? "}" : "";
            var vars = Variables(s).Select(v => v with { Insert = v.Label + close });
            return new SuggestionResult(nameStart, caret - nameStart, variable.Groups[2].Value, vars.ToList()) { AutoTrigger = true, Signature = Signature(s) };
        }
        if (s.InHeredoc) return SuggestionResult.Empty;

        if (s.TypingKeyword) return Keywords(s);
        var info = DockerfileCompletionData.Find(s.Keyword!);
        var signature = info?.Signature ?? "";
        var token = s.Token;
        var positional = s.Args.Where(t => !t.Text.StartsWith("--", StringComparison.Ordinal)).ToList();
        var positionalIndex = positional.FindIndex(t => t.Start == s.TokenStart);

        if (token.StartsWith('-') && info != null)
        {
            var eq = token.IndexOf('=');
            if (eq < 0 || !token.StartsWith("--", StringComparison.Ordinal))
            {
                if (positional.Count > 1 || (positional.Count == 1 && positionalIndex != 0)) return SuggestionResult.Empty;
                var used = s.Args.Where(a => a.Start != s.TokenStart && a.Text.StartsWith("--", StringComparison.Ordinal)).Select(a => a.Text.Split('=')[0][2..]).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var flags = info.Flags.Where(f => f.Repeatable || !used.Contains(f.Name)).Select(f => FlagItem(info, f)).ToList();
                return new SuggestionResult(s.TokenStart, caret - s.TokenStart, token, flags, signature) { AutoTrigger = true };
            }
            var flagName = token[2..eq].ToLowerInvariant();
            var flag = info.Flags.FirstOrDefault(f => f.Name == flagName);
            var valueStart = s.TokenStart + eq + 1;
            var valueText = s.Text[valueStart..caret];
            var flagSignature = flag == null ? signature : "--" + flag.Name + (flag.TakesValue ? "=<value>" : "[=true|false]") + "   " + flag.Doc;
            var values = flagName switch
            {
                "mount" => null,
                "from" => StageItems(s, true).Concat(LocalImageItems(sources)).ToList(),
                "platform" => PlatformItems(s.Keyword == "FROM"),
                "chmod" => [Value("755", "rwxr-xr-x: executables and folders."), Value("644", "rw-r--r--: regular files."), Value("0755", "Octal with a leading zero."), Value("600", "rw-------: private files."),
                    Value("u=rwX,go=rX", "Symbolic form: X adds execute only to folders and files already executable.") with { MinSyntax = "1.14" }],
                "chown" => UserItems(s),
                "checksum" => [Value("sha256:", "SHA-256 of an HTTP source, followed by the hex digest.")],
                "interval" or "timeout" or "start-period" or "start-interval" => DurationItems(),
                _ => (flag?.Values ?? []).Select(v => Value(v, flag!.Doc)).ToList(),
            };
            if (flagName == "mount") return Mount(s, valueStart, valueText, sources) with { Signature = "--mount=type=<bind|cache|tmpfs|secret|ssh>[,<option>=<value>...]" };
            return new SuggestionResult(valueStart, caret - valueStart, valueText, values!, flagSignature) { AutoTrigger = true };
        }

        List<SuggestionItem> items = [];
        var start = s.TokenStart;
        var query = token;
        switch (s.Keyword)
        {
            case "FROM":
                if (positionalIndex == 0)
                {
                    var colon = token.LastIndexOf(':');
                    if (colon > 0 && !token[(colon + 1)..].Contains('/') && !token.Contains('@'))
                    {
                        var repo = token[..colon];
                        start = s.TokenStart + colon + 1; query = token[(colon + 1)..];
                        items = sources.Images.Where(i => i.Repository.Equals(repo, StringComparison.Ordinal) && i.Tag is { Length: > 0 } and not "<none>")
                            .Select(i => new SuggestionItem(i.Tag, i.Tag, SuggestionKind.Value, "Local tag", $"{i.Reference} is on this engine ({i.Size}).", "FROM " + i.Reference)).ToList();
                    }
                    else
                        items = StageItems(s, false).Concat(LocalImageItems(sources)).Concat(PopularImageItems()).Concat(GlobalArgRefs(s)).ToList();
                }
                else if (positionalIndex == 1) items = [new("AS", "AS ", SuggestionKind.Value, "Name this stage", "Names the stage so COPY --from and later FROM lines can refer to it.", "FROM node:22 AS build")];
                break;
            case "COPY" or "ADD":
                if (!s.Args.Any(a => a.Text.StartsWith("--from=", StringComparison.OrdinalIgnoreCase)) && !token.StartsWith("<<", StringComparison.Ordinal))
                    items = PathItems(token, sources);
                break;
            case "EXPOSE":
                var slash = token.IndexOf('/');
                if (slash > 0) { start = s.TokenStart + slash + 1; query = token[(slash + 1)..]; items = [Value("tcp", "TCP (the default)."), Value("udp", "UDP.")]; }
                else items = DockerfileCompletionData.CommonPorts.Select(p => new SuggestionItem(p.Port, p.Port, SuggestionKind.Value, "Common port", p.Doc)).ToList();
                break;
            case "STOPSIGNAL" when positionalIndex == 0:
                items = DockerfileCompletionData.Signals.Select(x => Value(x.Name, x.Doc)).ToList();
                break;
            case "SHELL" when positionalIndex == 0:
                items = DockerfileCompletionData.Shells.Select(x => Value(x.Value, x.Doc)).ToList();
                break;
            case "USER" when positionalIndex == 0:
                items = UserItems(s);
                break;
            case "HEALTHCHECK" when positionalIndex == 0:
                items = [new("CMD", "CMD ", SuggestionKind.Value, "Check command", "The command whose exit status decides health (0 healthy, 1 unhealthy)."), new("NONE", "NONE", SuggestionKind.Value, "Disable", "Disables any health check inherited from the base image.")];
                break;
            case "ARG" when positionalIndex >= 0 && !token.Contains('='):
                items = DockerfileCompletionData.PlatformArgs.Select(a => new SuggestionItem(a.Name, a.Name, SuggestionKind.Variable, "Automatic platform ARG", a.Doc + (s.StageIndex >= 0 ? " Declaring it in a stage makes the value available there." : "")))
                    .Concat(DockerfileCompletionData.BuildKitArgs.Select(a => new SuggestionItem(a.Name, a.Name, SuggestionKind.Variable, "BuildKit built-in ARG", a.Doc) { Rank = 2, MinSyntax = a.Name == "SOURCE_DATE_EPOCH" ? "1.5" : null }))
                    .Concat(s.StageIndex >= 0 ? s.GlobalArgs.Select(a => new SuggestionItem(a, a, SuggestionKind.Variable, "Global ARG", "Redeclare the global ARG to use its value in this stage.") { Rank = -1 }) : [])
                    .ToList();
                break;
            case "LABEL" when !token.Contains('='):
                items = DockerfileCompletionData.OciLabels.Select(l => new SuggestionItem(l.Name, l.Name + "=", SuggestionKind.Key, "OCI annotation", l.Doc)).ToList();
                break;
        }
        // Flags are offered right after the keyword even before typing the dashes.
        if (token.Length == 0 && info != null && info.Flags.Length > 0 && positional.Count == 1)
        {
            var used = s.Args.Select(a => a.Text).Where(t => t.StartsWith("--", StringComparison.Ordinal)).Select(t => t.Split('=')[0][2..]).ToHashSet(StringComparer.OrdinalIgnoreCase);
            items.AddRange(info.Flags.Where(f => f.Repeatable || !used.Contains(f.Name)).Select(f => FlagItem(info, f) with { Rank = 5 }));
        }
        return new SuggestionResult(start, caret - start, query, items, signature) { AutoTrigger = items.Count > 0 };
    }

    // ------------------------------------------------------------------ contexts

    private static SuggestionResult Keywords(DockerfileCaretScan s)
    {
        var items = new List<SuggestionItem>();
        foreach (var i in DockerfileCompletionData.Instructions)
        {
            if (s.Onbuild && i.Name is "ONBUILD" or "FROM" or "MAINTAINER") continue;
            var retrigger = i.Flags.Length > 0 || i.Name is "FROM" or "EXPOSE" or "STOPSIGNAL" or "SHELL" or "USER" or "HEALTHCHECK" or "COPY" or "ADD" or "ONBUILD" or "LABEL" or "ARG";
            items.Add(new SuggestionItem(i.Name, i.Name + " ", SuggestionKind.Instruction, i.Name == "MAINTAINER" ? "Deprecated" : "Instruction", i.Doc, i.Example) { Retrigger = retrigger, Rank = i.Name == "MAINTAINER" ? 9 : 0 });
        }
        if (!s.Onbuild)
            foreach (var snip in DockerfileCompletionData.Snippets)
                items.Add(new SuggestionItem(snip.Label, snip.Insert, SuggestionKind.Snippet, "Snippet", snip.Doc, snip.Insert.Replace("‸", ""), snip.MinSyntax) { Rank = 3 });
        if (s.DirectiveRegion && !s.Onbuild)
            foreach (var d in DockerfileCompletionData.Directives.Where(d => !s.Directives.ContainsKey(d.Name)))
                items.Add(new SuggestionItem("# " + d.Name + "=", "# " + d.Name + "=", SuggestionKind.Directive, "Parser directive", d.Doc, d.Example, d.MinSyntax) { Retrigger = true, Rank = 1, FilterOverride = d.Name });
        var start = s.TokenStart;
        return new SuggestionResult(start, s.Caret - start, s.Token, items, s.Onbuild ? DockerfileCompletionData.Find("ONBUILD")!.Signature : "") { AutoTrigger = true };
    }

    private static SuggestionResult Directive(DockerfileCaretScan s)
    {
        var name = DirectiveNameRx.Match(s.LineBefore);
        if (name.Success)
        {
            var start = s.LineStart + name.Groups[1].Index;
            var items = DockerfileCompletionData.Directives.Where(d => !s.Directives.ContainsKey(d.Name) || d.Name.Equals(name.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
                .Select(d => new SuggestionItem(d.Name + "=", d.Name + "=", SuggestionKind.Directive, "Parser directive", d.Doc, d.Example, d.MinSyntax) { Retrigger = true }).ToList();
            return new SuggestionResult(start, s.Caret - start, name.Groups[1].Value, items, "# <directive>=<value>   Parser directives must come before any other line.") { AutoTrigger = true };
        }
        var value = DirectiveValueRx.Match(s.LineBefore);
        if (!value.Success) return SuggestionResult.Empty;
        var directive = DockerfileCompletionData.Directives.FirstOrDefault(d => d.Name.Equals(value.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
        if (directive.Name == null) return SuggestionResult.Empty;
        var vStart = s.LineStart + value.Groups[2].Index;
        var values = directive.Values.Select(v => new SuggestionItem(v, v, SuggestionKind.Value, "# " + directive.Name, directive.Doc, "# " + directive.Name + "=" + v)).ToList();
        return new SuggestionResult(vStart, s.Caret - vStart, value.Groups[2].Value, values, "# " + directive.Name + "=<value>") { AutoTrigger = true };
    }

    private static SuggestionResult Mount(DockerfileCaretScan s, int valueStart, string valueText, SuggestionSources sources)
    {
        var comma = valueText.LastIndexOf(',');
        var previous = comma < 0 ? Array.Empty<string>() : valueText[..comma].Split(',');
        var part = valueText[(comma + 1)..];
        var partStart = valueStart + comma + 1;
        var type = previous.Select(p => p.Split('=', 2)).Where(kv => kv[0] == "type" && kv.Length == 2).Select(kv => kv[1]).FirstOrDefault();
        var options = DockerfileCompletionData.MountOptions(type ?? "bind");
        var eq = part.IndexOf('=');
        if (eq < 0)
        {
            var used = previous.Select(p => p.Split('=')[0]).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var keys = new List<SuggestionItem>();
            if (type == null && !used.Contains("type"))
                keys.Add(new SuggestionItem("type=", "type=", SuggestionKind.Key, "Mount type", "bind (default), cache, tmpfs, secret or ssh.", "--mount=type=cache,target=/root/.npm") { Retrigger = true, Rank = -1 });
            keys.AddRange(options.Where(o => !used.Contains(o.Key)).Select(o => new SuggestionItem(o.Key + "=", o.Key + "=", SuggestionKind.Key, (type ?? "bind") + " option", o.Doc, $"--mount=type={type ?? "bind"},{o.Key}=…") { Retrigger = true }));
            return new SuggestionResult(partStart, s.Caret - partStart, part, keys) { AutoTrigger = true };
        }
        var key = part[..eq].ToLowerInvariant();
        var vStart = partStart + eq + 1;
        var query = part[(eq + 1)..];
        List<SuggestionItem> values;
        if (key == "type")
            values = DockerfileCompletionData.MountTypes.Select(t => new SuggestionItem(t.Key, t.Key + ",", SuggestionKind.Value, "Mount type", t.Doc) { Retrigger = true, MinSyntax = "1.2" }).ToList();
        else if (key == "from")
            values = StageItems(s, true).Concat(LocalImageItems(sources)).ToList();
        else if (key == "env" && type == "secret")
            values = [];
        else
        {
            var option = options.FirstOrDefault(o => o.Key == key);
            values = option == null ? [] : option.Values.Select(v => new SuggestionItem(v, v, SuggestionKind.Value, key, option.Doc)).ToList();
        }
        return new SuggestionResult(vStart, s.Caret - vStart, query, values) { AutoTrigger = true };
    }

    // ------------------------------------------------------------------ item builders

    private static SuggestionItem FlagItem(DockerfileInstructionInfo info, DockerfileFlagInfo f) =>
        new("--" + f.Name + (f.TakesValue ? "=" : ""), f.Insert, SuggestionKind.Flag, info.Name + " flag", f.Doc, f.Example, f.MinSyntax) { LabsMinSyntax = f.LabsMin, Retrigger = f.TakesValue };

    private static SuggestionItem Value(string value, string doc) => new(value, value, SuggestionKind.Value, "Value", doc);

    private static List<SuggestionItem> StageItems(DockerfileCaretScan s, bool excludeCurrent) =>
        s.Stages.Where(st => !(excludeCurrent && st.Index == s.StageIndex && s.Keyword != "FROM"))
            .Select(st => st.Name != null
                ? new SuggestionItem(st.Name, st.Name, SuggestionKind.Stage, $"Stage {st.Index} (line {st.Line + 1})", $"Build stage \"{st.Name}\" based on {st.Base}.") { Rank = -2 }
                : new SuggestionItem(st.Index.ToString(System.Globalization.CultureInfo.InvariantCulture), st.Index.ToString(System.Globalization.CultureInfo.InvariantCulture), SuggestionKind.Stage, $"Unnamed stage (line {st.Line + 1})", $"Stage number {st.Index}, based on {st.Base}. Name stages with AS to make references readable.") { Rank = -1 })
            .ToList();

    private static IEnumerable<SuggestionItem> LocalImageItems(SuggestionSources sources) =>
        sources.Images.Where(i => i.Repository is { Length: > 0 } and not "<none>")
            .Select(i => new SuggestionItem(i.Reference, i.Reference, SuggestionKind.Image, "Local image", $"On this engine: {i.Reference} ({i.Size}), image ID {Short(i.Id)}.", "FROM " + i.Reference) { Rank = 0 });

    private static IEnumerable<SuggestionItem> PopularImageItems() =>
        DockerfileCompletionData.PopularImages.Select(p => new SuggestionItem(p.Name, p.Name, SuggestionKind.Image, "Popular official images", p.Doc + " From the built-in curated list of popular official images; pin a tag before relying on it.", "FROM " + p.Name + (p.Name == "scratch" ? "" : ":<tag>")) { Rank = 2 });

    private static IEnumerable<SuggestionItem> GlobalArgRefs(DockerfileCaretScan s) =>
        s.GlobalArgs.Select(a => new SuggestionItem("${" + a + "}", "${" + a + "}", SuggestionKind.Variable, "Global ARG", "Uses the global build argument in the image reference.") { Rank = 1, FilterOverride = "$" + a });

    private static List<SuggestionItem> PlatformItems(bool fromLine)
    {
        var items = DockerfileCompletionData.Platforms.Select(p => new SuggestionItem(p, p, SuggestionKind.Value, "Platform", "Operating system and architecture.")).ToList();
        items.Insert(0, new SuggestionItem("$BUILDPLATFORM", "$BUILDPLATFORM", SuggestionKind.Variable, "Build machine platform", "Runs this stage natively on the build machine; use it for cross-compiling stages.", "FROM --platform=$BUILDPLATFORM golang:1.23 AS build") { Rank = -1 });
        items.Insert(1, new SuggestionItem("$TARGETPLATFORM", "$TARGETPLATFORM", SuggestionKind.Variable, "Target platform", "The platform being built.") { Rank = -1 });
        return items;
    }

    private static List<SuggestionItem> UserItems(DockerfileCaretScan s)
    {
        var items = s.Users.Select(u => new SuggestionItem(u, u, SuggestionKind.Value, "Created in this stage", "A user created by useradd or adduser earlier in this stage.") { Rank = -1 }).ToList();
        items.Add(new SuggestionItem("root", "root", SuggestionKind.Value, "Superuser", "User ID 0. Prefer a non-root user for the final stage.") { Rank = 2 });
        items.Add(new SuggestionItem("nobody", "nobody", SuggestionKind.Value, "Unprivileged", "Exists in most Linux base images."));
        items.Add(new SuggestionItem("65532:65532", "65532:65532", SuggestionKind.Value, "nonroot", "The non-root user of distroless and Docker Hardened Images."));
        return items;
    }

    private static List<SuggestionItem> DurationItems() =>
        new[] { ("5s", "5 seconds"), ("10s", "10 seconds"), ("30s", "30 seconds (default for interval and timeout)"), ("1m", "1 minute"), ("2s", "2 seconds") }
            .Select(d => Value(d.Item1, d.Item2)).ToList();

    private static List<SuggestionItem> PathItems(string token, SuggestionSources sources)
    {
        var slash = token.LastIndexOf('/');
        var dir = slash < 0 ? "" : token[..(slash + 1)];
        var dirKey = dir.StartsWith("./", StringComparison.Ordinal) ? dir[2..] : dir;
        var items = new List<SuggestionItem>();
        foreach (var entry in sources.ContextEntries)
        {
            var trimmed = entry.TrimEnd('/');
            var parentEnd = trimmed.LastIndexOf('/');
            var parent = parentEnd < 0 ? "" : trimmed[..(parentEnd + 1)];
            if (!parent.Equals(dirKey, StringComparison.Ordinal)) continue;
            var isDir = entry.EndsWith('/');
            var name = entry[parent.Length..];
            var insert = dir + name;
            items.Add(new SuggestionItem(name, insert, SuggestionKind.Path, isDir ? "Folder in the build context" : "File in the build context", "Relative to the build context folder.") { FilterOverride = insert, Retrigger = isDir, Rank = isDir ? 0 : 1 });
        }
        return items;
    }

    private static List<SuggestionItem> Variables(DockerfileCaretScan s)
    {
        var items = new List<SuggestionItem>();
        void Add(string name, string detail, string doc, int rank) { if (!items.Any(i => i.Label == name)) items.Add(new SuggestionItem(name, name, SuggestionKind.Variable, detail, doc) { Rank = rank }); }
        if (s.StageIndex < 0 || s.InFromLine)
        {
            foreach (var a in s.GlobalArgs) Add(a, "Global ARG", "Declared before the first FROM; usable in FROM lines.", 0);
            foreach (var a in DockerfileCompletionData.PlatformArgs) Add(a.Name, "Automatic platform ARG", a.Doc, 1);
            return items;
        }
        foreach (var a in s.StageArgs) Add(a, "ARG in this stage", "Build argument declared in this stage.", 0);
        foreach (var e in s.StageEnv) Add(e, "ENV", "Environment variable set in this stage or inherited from its parent stage.", 0);
        foreach (var a in DockerfileCompletionData.ProxyArgs) Add(a, "Predefined proxy ARG", "Available without an ARG instruction and excluded from the image history.", 2);
        foreach (var a in DockerfileCompletionData.PlatformArgs) Add(a.Name, "Declare ARG " + a.Name + " first", a.Doc + " Platform ARGs are global; add ARG " + a.Name + " in this stage to use it here.", 3);
        foreach (var a in s.GlobalArgs) Add(a, "Declare ARG " + a + " first", "Global ARG: add ARG " + a + " in this stage to use its value here.", 3);
        return items;
    }

    private static bool EscapedDollar(DockerfileCaretScan s, int dollar) => dollar > 0 && s.Text[dollar - 1] == s.Escape;

    private static string Short(string id) => id.StartsWith("sha256:", StringComparison.Ordinal) ? id[7..Math.Min(id.Length, 19)] : id[..Math.Min(id.Length, 12)];

    private static string Signature(DockerfileCaretScan s) => s.Keyword == null ? "" : DockerfileCompletionData.Find(s.Keyword)?.Signature ?? "";

    // ------------------------------------------------------------------ hover, signature, syntax header

    /// <summary>Documentation for the keyword, flag, mount option or directive at <paramref name="index"/>, or null.</summary>
    public static SuggestionItem? Hover(string text, int index)
    {
        if (index < 0 || index >= text.Length || char.IsWhiteSpace(text[index])) return null;
        var start = index;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1])) start--;
        var end = index;
        while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
        var word = text[start..end];
        var scan = DockerfileCaretScan.Run(text, start);
        var line = text[SuggestionText.LineStart(text, start)..SuggestionText.LineEnd(text, start)].TrimStart();
        if (line.StartsWith('#'))
        {
            var m = Regex.Match(line, @"^#\s*([A-Za-z]+)\s*=");
            if (!m.Success || !scan.DirectiveRegion) return null;
            var d = DockerfileCompletionData.Directives.FirstOrDefault(x => x.Name.Equals(m.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
            return d.Name == null ? null : new SuggestionItem("# " + d.Name, "", SuggestionKind.Directive, "Parser directive", d.Doc, d.Example, d.MinSyntax);
        }
        var instruction = scan.Current;
        if (instruction == null || instruction.Tokens.Count == 0) return null;
        if (instruction.Tokens[0].Start == start || (instruction.Tokens.Count > 1 && instruction.Tokens[1].Start == start && instruction.Keyword.Equals("ONBUILD", StringComparison.OrdinalIgnoreCase)))
        {
            var info = DockerfileCompletionData.Find(word);
            return info == null ? null : new SuggestionItem(info.Name, "", SuggestionKind.Instruction, info.Signature, info.Doc, info.Example);
        }
        if (word.StartsWith("--", StringComparison.Ordinal))
        {
            var info = DockerfileCompletionData.Find(instruction.EffectiveKeyword);
            var name = word[2..].Split('=')[0];
            var flag = info?.Flags.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (flag == null) return null;
            if (flag.Name == "mount")
            {
                // Hovering a mount option shows that option's documentation.
                var offset = index - start - (name.Length + 3);
                var spec = word[(name.Length + 3)..];
                var type = spec.Split(',').Select(p => p.Split('=', 2)).Where(kv => kv[0] == "type" && kv.Length == 2).Select(kv => kv[1]).FirstOrDefault() ?? "bind";
                if (offset >= 0)
                {
                    var position = 0;
                    foreach (var part in spec.Split(','))
                    {
                        if (offset >= position && offset <= position + part.Length)
                        {
                            var key = part.Split('=')[0];
                            if (key == "type") return new SuggestionItem("type=" + type, "", SuggestionKind.Key, "Mount type", DockerfileCompletionData.MountTypes.FirstOrDefault(t => t.Key == type)?.Doc ?? "bind, cache, tmpfs, secret or ssh.", "", "1.2");
                            var option = DockerfileCompletionData.MountOptions(type).FirstOrDefault(o => o.Key == key);
                            if (option != null) return new SuggestionItem(key, "", SuggestionKind.Key, type + " mount option", option.Doc, "", "1.2");
                        }
                        position += part.Length + 1;
                    }
                }
            }
            return new SuggestionItem("--" + flag.Name, "", SuggestionKind.Flag, info!.Name + " flag", flag.Doc, flag.Example, flag.MinSyntax) { LabsMinSyntax = flag.LabsMin };
        }
        return null;
    }

    /// <summary>The instruction form for the caret position, or an empty string.</summary>
    public static string SignatureAt(string text, int caret)
    {
        var s = DockerfileCaretScan.Run(text, caret);
        return s.TypingKeyword || s.CommentLine || s.InHeredoc ? "" : Signature(s);
    }

    /// <summary>
    /// The edit that inserts "# syntax=docker/dockerfile:1" at the top, or upgrades an existing docker/dockerfile
    /// syntax line to it. Returns null when the file already declares docker/dockerfile:1 (or a custom frontend).
    /// </summary>
    public static SuggestionEdit? SyntaxUpgradeEdit(string text)
    {
        var s = DockerfileCaretScan.Run(text, 0);
        var header = "# syntax=" + DockerfileCompletionData.RecommendedSyntax;
        if (s.SyntaxLine < 0)
        {
            var newline = text.Contains("\r\n") ? "\r\n" : "\n";
            return new SuggestionEdit(0, 0, header + newline, header.Length + newline.Length);
        }
        if (s.Syntax is { Latest: true, Major: 1 }) return null;
        if (s.Syntax is { Major: 0 } || s.Syntax is { Major: 99 }) return null;
        var start = 0;
        for (var i = 0; i < s.SyntaxLine; i++) start = text.IndexOf('\n', start) + 1;
        var end = SuggestionText.LineEnd(text, start);
        var replacement = s.Syntax is { Labs: true } ? "# syntax=docker/dockerfile:1-labs" : header;
        return new SuggestionEdit(start, end - start, replacement, start + replacement.Length);
    }
}
