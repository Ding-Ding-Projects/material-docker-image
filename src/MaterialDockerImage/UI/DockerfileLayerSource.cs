using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>One Dockerfile instruction with the 1-based line it starts on. Continuation lines are joined.</summary>
public sealed record DockerfileSourceInstruction(int Line, string Keyword, string Arguments, bool Heredoc);

/// <summary>One build stage: its FROM line, base reference, optional name and the instructions that follow it.</summary>
public sealed record DockerfileSourceStage(int Index, int Line, string Name, string Base, IReadOnlyList<DockerfileSourceInstruction> Instructions);

/// <summary>Where one image history row came from: a line of the known Dockerfile, or the base image below the first FROM.</summary>
public sealed record LayerSourceNote(int Line, string FromStage, string FromImage, string BaseImage)
{
    public bool IsBase => Line == 0;
}

/// <summary>
/// Matches <c>docker image history</c> rows to a Dockerfile this application knows, without guessing. Each instruction
/// of the target stage (and the stages it is built on) is turned into the exact text BuildKit records for it; rows are
/// matched newest first and every instruction must be accounted for, otherwise no note is produced at all. Rows below
/// the matched instructions belong to the base image and are labelled with the root FROM reference when it is known.
/// </summary>
public static class DockerfileLayerSource
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.None, RegexTimeout);
    private static readonly Regex HeredocMarker = new(@"<<-?\s*[""']?[A-Za-z_][A-Za-z0-9_]*[""']?", RegexOptions.None, RegexTimeout);
    private static readonly Regex HeredocWord = new(@"<<(-?)\s*[""']?([A-Za-z_][A-Za-z0-9_]*)[""']?", RegexOptions.None, RegexTimeout);
    private static readonly Regex RunArgs = new(@"^RUN \|(\d+) ", RegexOptions.None, RegexTimeout);
    private static readonly Regex EscapeDirective = new(@"^#\s*escape\s*=\s*(\S)\s*$", RegexOptions.IgnoreCase, RegexTimeout);
    private static readonly Regex Directive = new(@"^#\s*[A-Za-z]+\s*=", RegexOptions.None, RegexTimeout);
    private static readonly string[][] DefaultShells = [["/bin/sh", "-c"], ["cmd", "/S", "/C"]];

    /// <summary>Splits a Dockerfile into stages. Lines are 1-based; comments and blank lines inside continuations are skipped as BuildKit does.</summary>
    public static IReadOnlyList<DockerfileSourceStage> ParseStages(string source, out IReadOnlyList<DockerfileSourceInstruction> globalArgs)
    {
        var lines = (source ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var escape = '\\';
        for (var i = 0; i < lines.Length; i++)
        {
            var t = lines[i].Trim();
            if (!Directive.IsMatch(t)) break;
            var m = EscapeDirective.Match(t);
            if (m.Success && m.Groups[1].Value is "`" or "\\") escape = m.Groups[1].Value[0];
        }
        var instructions = new List<DockerfileSourceInstruction>();
        for (var i = 0; i < lines.Length; i++)
        {
            var first = lines[i].Trim();
            if (first.Length == 0 || first.StartsWith('#')) continue;
            var start = i + 1;
            var text = new StringBuilder();
            var line = lines[i].TrimEnd();
            while (true)
            {
                if (line.EndsWith(escape))
                {
                    text.Append(line[..^1]);
                    i++;
                    while (i < lines.Length && (lines[i].Trim().Length == 0 || lines[i].TrimStart().StartsWith('#'))) i++;
                    if (i >= lines.Length) break;
                    line = lines[i].TrimEnd();
                    continue;
                }
                text.Append(line);
                break;
            }
            var joined = text.ToString().Trim();
            var space = joined.IndexOfAny([' ', '\t']);
            var keyword = (space > 0 ? joined[..space] : joined).ToUpperInvariant();
            var arguments = space > 0 ? joined[(space + 1)..].Trim() : "";
            var heredoc = false;
            if (keyword is "RUN" or "COPY" or "ADD" && HeredocMarker.IsMatch(arguments))
            {
                heredoc = true;
                foreach (Match m in HeredocWord.Matches(arguments))
                {
                    var strip = m.Groups[1].Value == "-";
                    var word = m.Groups[2].Value;
                    i++;
                    while (i < lines.Length && (strip ? lines[i].Trim() : lines[i].TrimEnd()) != word) i++;
                }
            }
            instructions.Add(new DockerfileSourceInstruction(start, keyword, arguments, heredoc));
        }

        var stages = new List<DockerfileSourceStage>();
        var global = new List<DockerfileSourceInstruction>();
        List<DockerfileSourceInstruction>? body = null;
        foreach (var instruction in instructions)
        {
            if (instruction.Keyword == "FROM")
            {
                var tokens = instruction.Arguments.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Where(t => !t.StartsWith("--", StringComparison.Ordinal)).ToList();
                var reference = tokens.Count > 0 ? tokens[0] : "";
                var name = tokens.Count >= 3 && tokens[1].Equals("AS", StringComparison.OrdinalIgnoreCase) ? tokens[2] : "";
                body = [];
                stages.Add(new DockerfileSourceStage(stages.Count, instruction.Line, name, reference, body));
            }
            else if (body != null) body.Add(instruction);
            else if (instruction.Keyword == "ARG") global.Add(instruction);
        }
        globalArgs = global;
        return stages;
    }

    /// <summary>
    /// Matches history rows (newest first, the <c>CreatedBy</c> text of <c>docker image history --no-trunc</c>) to the
    /// Dockerfile. Every stage is tried as the build target, last first; a result is returned only when exactly one
    /// set of notes accounts for all instructions of a target and its parent stages. Keys are row indexes.
    /// </summary>
    public static IReadOnlyDictionary<int, LayerSourceNote> Match(string dockerfile, IReadOnlyList<string> createdByNewestFirst)
    {
        var none = new Dictionary<int, LayerSourceNote>();
        if (string.IsNullOrWhiteSpace(dockerfile) || createdByNewestFirst.Count == 0) return none;
        var stages = ParseStages(dockerfile, out var globalArgs);
        if (stages.Count == 0) return none;
        var rows = createdByNewestFirst.Select(NormaliseRecorded).ToList();
        var variables = VariablesBefore(rows);
        Dictionary<int, LayerSourceNote>? chosen = null;
        for (var s = stages.Count - 1; s >= 0; s--)
        {
            var attempt = TryStage(stages, s, globalArgs, rows, variables);
            if (attempt == null) continue;
            if (chosen == null) { chosen = attempt; continue; }
            if (!Same(chosen, attempt)) return none;
        }
        return chosen ?? none;
    }

    private static bool Same(Dictionary<int, LayerSourceNote> a, Dictionary<int, LayerSourceNote> b) =>
        a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var other) && other == p.Value);

    private static Dictionary<int, LayerSourceNote>? TryStage(IReadOnlyList<DockerfileSourceStage> stages, int target, IReadOnlyList<DockerfileSourceInstruction> globalArgs,
        IReadOnlyList<string> rows, IReadOnlyList<Dictionary<string, string>> variables)
    {
        var chain = new List<DockerfileSourceStage>();
        var current = stages[target];
        while (true)
        {
            chain.Insert(0, current);
            var parent = FindStage(stages, current.Base, current.Index);
            if (parent == null) break;
            if (chain.Contains(parent)) return null;
            current = parent;
        }
        var root = chain[0];
        var names = stages.Where(s => s.Name.Length > 0).Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var steps = new List<(DockerfileSourceInstruction Instruction, string[]? Shell)>();
        string[]? shell = null;
        foreach (var instruction in chain.SelectMany(c => c.Instructions))
        {
            steps.Add((instruction, shell));
            if (instruction.Keyword == "SHELL" && JsonArray(instruction.Arguments) is { Count: > 0 } custom) shell = custom.ToArray();
        }

        var notes = new Dictionary<int, LayerSourceNote>();
        var row = 0;
        var step = steps.Count - 1;
        var matched = 0;
        while (step >= 0 && row < rows.Count)
        {
            var (instruction, stepShell) = steps[step];
            var outcome = Compare(instruction, stepShell, rows[row], variables[row]);
            if (outcome == Outcome.Exact)
            {
                var fromStage = "";
                var fromImage = "";
                if (instruction.Keyword is "COPY" or "ADD" && FlagValue(instruction.Arguments, "--from") is { Length: > 0 } from)
                {
                    if (names.Contains(from) || int.TryParse(from, out var n) && n >= 0 && n < stages.Count) fromStage = from; else fromImage = from;
                }
                notes[row] = new LayerSourceNote(instruction.Line, fromStage, fromImage, "");
                matched++;
                row++; step--;
            }
            else if (outcome == Outcome.Structural) { row++; step--; }
            else if (instruction.Keyword == "ARG") step--;
            else return null;
        }
        while (step >= 0 && steps[step].Instruction.Keyword == "ARG") step--;
        if (step >= 0 || matched == 0) return null;

        var baseImage = Expand(root.Base, GlobalDefaults(globalArgs));
        if (baseImage != null && baseImage.Length > 0 && !baseImage.Equals("scratch", StringComparison.OrdinalIgnoreCase) && !baseImage.Contains('$'))
            for (var r = row; r < rows.Count; r++) notes[r] = new LayerSourceNote(0, "", "", baseImage);
        return notes;
    }

    private static DockerfileSourceStage? FindStage(IReadOnlyList<DockerfileSourceStage> stages, string reference, int before) =>
        stages.Take(before).LastOrDefault(s => s.Name.Length > 0 && s.Name.Equals(reference, StringComparison.OrdinalIgnoreCase))
        ?? (int.TryParse(reference, out var n) && n >= 0 && n < before ? stages[n] : null);

    private static Dictionary<string, string> GlobalDefaults(IReadOnlyList<DockerfileSourceInstruction> globalArgs)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var arg in globalArgs)
            foreach (var (key, value) in Pairs(arg.Arguments, values, true) ?? [])
                if (value != null) values[key] = value;
        return values;
    }

    private enum Outcome { Mismatch, Exact, Structural }

    private static Outcome Compare(DockerfileSourceInstruction instruction, string[]? shell, string recorded, Dictionary<string, string> variables)
    {
        if (instruction.Keyword == "ARG")
        {
            if (!recorded.StartsWith("ARG ", StringComparison.Ordinal)) return Outcome.Mismatch;
            var want = instruction.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(a => a.Split('=')[0]).ToList();
            var have = recorded[4..].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(a => a.Split('=')[0]).ToList();
            return want.SequenceEqual(have, StringComparer.Ordinal) ? Outcome.Exact : Outcome.Mismatch;
        }
        var expected = Expected(instruction, shell, variables);
        if (expected == null)
            return recorded.StartsWith(instruction.Keyword + " ", StringComparison.Ordinal) || recorded == instruction.Keyword ? Outcome.Structural : Outcome.Mismatch;
        return expected.Any(e => Collapse(e) == recorded) ? Outcome.Exact : Outcome.Mismatch;
    }

    /// <summary>
    /// The text BuildKit records for an instruction, or null when the instruction kind is not reproduced exactly
    /// (HEALTHCHECK, ONBUILD, heredocs and similar), in which case only the keyword is compared and no note is shown.
    /// </summary>
    public static IReadOnlyList<string>? Expected(DockerfileSourceInstruction instruction, string[]? shell, IReadOnlyDictionary<string, string> variables)
    {
        if (instruction.Heredoc) return null;
        var args = instruction.Arguments;
        switch (instruction.Keyword)
        {
            case "RUN":
            {
                var body = StripFlags(args);
                if (body.StartsWith('['))
                    return JsonArray(body) is { } exec ? ["RUN " + string.Join(" ", exec)] : null;
                var shells = shell != null ? [shell] : DefaultShells;
                return shells.Select(s => "RUN " + string.Join(" ", s) + " " + body).ToList();
            }
            case "CMD":
            case "ENTRYPOINT":
            {
                if (args.StartsWith('['))
                    return JsonArray(args) is { } exec ? [instruction.Keyword + " " + GoQuoted(exec)] : null;
                var shells = shell != null ? [shell] : DefaultShells;
                return shells.Select(s => instruction.Keyword + " " + GoQuoted([.. s, args])).ToList();
            }
            case "COPY":
            case "ADD":
            {
                if (args.Contains('[')) return null;
                var tokens = Words(args, variables);
                if (tokens == null) return null;
                var kept = tokens.Where(t => !t.StartsWith("--from=", StringComparison.Ordinal)).ToList();
                return [instruction.Keyword + " " + string.Join(" ", kept)];
            }
            case "ENV":
            case "LABEL":
            {
                var pairs = Pairs(args, variables, instruction.Keyword == "ENV");
                if (pairs == null || pairs.Count == 0 || pairs.Any(p => p.Value == null)) return null;
                return [instruction.Keyword + " " + string.Join(" ", pairs.Select(p => p.Key + "=" + p.Value))];
            }
            case "WORKDIR":
            case "USER":
            case "STOPSIGNAL":
            {
                var value = Word(args, variables);
                return value == null ? null : [instruction.Keyword + " " + value];
            }
            case "EXPOSE":
            {
                var tokens = Words(args, variables);
                if (tokens == null || tokens.Count == 0) return null;
                return ["EXPOSE [" + string.Join(" ", tokens.Select(t => t.Contains('/') ? t[..t.IndexOf('/')] + "/" + t[(t.IndexOf('/') + 1)..].ToLowerInvariant() : t + "/tcp")) + "]"];
            }
            case "VOLUME":
            {
                var list = args.StartsWith('[') ? JsonArray(args) : Words(args, variables);
                return list == null || list.Count == 0 ? null : ["VOLUME [" + string.Join(" ", list) + "]"];
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// Normalises a recorded <c>CreatedBy</c> value: whitespace collapsed, BuildKit's <c># buildkit</c> suffix and the
    /// <c>|N NAME=value</c> build-argument prefix removed, and the legacy builder's <c>/bin/sh -c #(nop)</c> form unwrapped.
    /// </summary>
    public static string NormaliseRecorded(string createdBy)
    {
        var text = Collapse(createdBy ?? "");
        if (text.EndsWith(" # buildkit", StringComparison.Ordinal)) text = text[..^" # buildkit".Length].TrimEnd();
        else if (text == "# buildkit") text = "";
        const string nop = "/bin/sh -c #(nop) ";
        if (text.StartsWith(nop, StringComparison.Ordinal)) text = text[nop.Length..].Trim();
        else if (text.StartsWith("/bin/sh -c ", StringComparison.Ordinal) || text.StartsWith('|')) text = "RUN " + text;
        var args = RunArgs.Match(text);
        if (args.Success)
        {
            var count = int.Parse(args.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            var rest = text[args.Length..];
            for (var k = 0; k < count; k++)
            {
                var space = rest.IndexOf(' ');
                if (space < 0 || !rest[..space].Contains('=')) return text;
                rest = rest[(space + 1)..];
            }
            text = "RUN " + rest;
        }
        return text;
    }

    /// <summary>
    /// Variables visible to each row: build arguments and environment recorded by older rows (ARG NAME=value and
    /// ENV NAME=value), so a Dockerfile reference such as $VERSION expands to the value the build actually used.
    /// </summary>
    private static List<Dictionary<string, string>> VariablesBefore(IReadOnlyList<string> rows)
    {
        var result = new Dictionary<string, string>[rows.Count];
        var running = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = rows.Count - 1; i >= 0; i--)
        {
            result[i] = new Dictionary<string, string>(running, StringComparer.Ordinal);
            var row = rows[i];
            if (!row.StartsWith("ARG ", StringComparison.Ordinal) && !row.StartsWith("ENV ", StringComparison.Ordinal)) continue;
            string? key = null;
            foreach (var token in row[4..].Split(' '))
            {
                var eq = token.IndexOf('=');
                if (eq > 0 && Regex.IsMatch(token[..eq], "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.None, RegexTimeout)) { key = token[..eq]; running[key] = token[(eq + 1)..]; }
                else if (key != null) running[key] += " " + token;
            }
        }
        return [.. result];
    }

    private static string Collapse(string text) => Spaces.Replace(text, " ").Trim();

    private static string StripFlags(string args)
    {
        var rest = args.TrimStart();
        while (rest.StartsWith("--", StringComparison.Ordinal))
        {
            var space = rest.IndexOfAny([' ', '\t']);
            if (space < 0) return "";
            rest = rest[(space + 1)..].TrimStart();
        }
        return rest;
    }

    private static string? FlagValue(string args, string flag)
    {
        foreach (var token in args.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!token.StartsWith("--", StringComparison.Ordinal)) break;
            if (token.StartsWith(flag + "=", StringComparison.Ordinal)) return token[(flag.Length + 1)..];
        }
        return null;
    }

    private static List<string>? JsonArray(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            var list = new List<string>();
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.String) return null;
                list.Add(e.GetString() ?? "");
            }
            return list;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Go's %q formatting of a string slice, which BuildKit uses for CMD and ENTRYPOINT: ["a" "b"].</summary>
    private static string GoQuoted(IEnumerable<string> values) =>
        "[" + string.Join(" ", values.Select(v => "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t") + "\"")) + "]";

    /// <summary>Splits on unquoted whitespace, removes quotes and expands variables outside single quotes. Null on unsupported syntax.</summary>
    private static List<string>? Words(string text, IReadOnlyDictionary<string, string> variables)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        var started = false;
        char quote = '\0';
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote == '\0' && char.IsWhiteSpace(c))
            {
                if (started) { words.Add(current.ToString()); current.Clear(); started = false; }
                continue;
            }
            started = true;
            if (quote == '\0' && c is '"' or '\'') { quote = c; continue; }
            if (quote != '\0' && c == quote) { quote = '\0'; continue; }
            if (c == '\\' && quote != '\'' && i + 1 < text.Length) { current.Append(text[++i]); continue; }
            if (c == '$' && quote != '\'')
            {
                var expanded = Variable(text, ref i, variables);
                if (expanded == null) return null;
                current.Append(expanded);
                continue;
            }
            current.Append(c);
        }
        if (quote != '\0') return null;
        if (started) words.Add(current.ToString());
        return words;
    }

    private static string? Word(string text, IReadOnlyDictionary<string, string> variables) =>
        Words(text, variables) is { Count: > 0 } words ? string.Join(" ", words) : null;

    private static string? Expand(string text, IReadOnlyDictionary<string, string> variables) => Word(text, variables);

    /// <summary>KEY=value pairs (or the legacy "KEY value" form). Values are unquoted and expanded.</summary>
    private static List<KeyValuePair<string, string?>>? Pairs(string text, IReadOnlyDictionary<string, string> variables, bool legacyForm)
    {
        var words = Words(text, variables);
        if (words == null || words.Count == 0) return null;
        var pairs = new List<KeyValuePair<string, string?>>();
        if (legacyForm && !words[0].Contains('='))
        {
            if (words.Count == 1) { pairs.Add(new(words[0], null)); return pairs; }
            pairs.Add(new(words[0], string.Join(" ", words.Skip(1))));
            return pairs;
        }
        foreach (var word in words)
        {
            var eq = word.IndexOf('=');
            if (eq <= 0) { pairs.Add(new(word, null)); continue; }
            pairs.Add(new(word[..eq], word[(eq + 1)..]));
        }
        return pairs;
    }

    /// <summary>Expands $NAME, ${NAME}, ${NAME:-word} and ${NAME:+word}; unset names expand to nothing, as in BuildKit. Null for other forms.</summary>
    private static string? Variable(string text, ref int i, IReadOnlyDictionary<string, string> variables)
    {
        if (i + 1 >= text.Length) return "$";
        var next = text[i + 1];
        if (next == '{')
        {
            var close = text.IndexOf('}', i + 2);
            if (close < 0) return null;
            var inner = text[(i + 2)..close];
            i = close;
            var op = inner.IndexOf(':');
            if (op < 0) return IsName(inner) ? variables.GetValueOrDefault(inner, "") : null;
            var name = inner[..op];
            if (!IsName(name) || op + 1 >= inner.Length) return null;
            var word = inner[(op + 2)..];
            var set = variables.TryGetValue(name, out var value) && value.Length > 0;
            return inner[op + 1] switch { '-' => set ? value : word, '+' => set ? word : "", _ => null };
        }
        if (char.IsLetter(next) || next == '_')
        {
            var end = i + 1;
            while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_')) end++;
            var name = text[(i + 1)..end];
            i = end - 1;
            return variables.GetValueOrDefault(name, "");
        }
        return "$";
    }

    private static bool IsName(string name) => Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.None, RegexTimeout);
}
