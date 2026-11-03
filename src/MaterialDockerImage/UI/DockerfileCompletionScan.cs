using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>A whitespace-separated token with its absolute start offset.</summary>
public readonly record struct DockerfileCaretToken(string Text, int Start)
{
    public int End => Start + Text.Length;
}

/// <summary>One logical instruction (continuation lines joined; heredoc bodies attached).</summary>
public sealed record DockerfileScanInstruction(string Keyword, int StartLine, int EndLine, int BodyEndLine, IReadOnlyList<DockerfileCaretToken> Tokens)
{
    /// <summary>Tokens after the keyword (and after ONBUILD's inner keyword).</summary>
    public IReadOnlyList<DockerfileCaretToken> Args => Tokens.Count == 0 ? Tokens : Tokens.Skip(Keyword.Equals("ONBUILD", StringComparison.OrdinalIgnoreCase) && Tokens.Count > 1 ? 2 : 1).ToList();
    public string EffectiveKeyword => Keyword.Equals("ONBUILD", StringComparison.OrdinalIgnoreCase) && Tokens.Count > 1 ? Tokens[1].Text.ToUpperInvariant() : Keyword.ToUpperInvariant();
}

public sealed record DockerfileScanStage(string? Name, string Base, int Index, int Line);

/// <summary>
/// Scans a Dockerfile around a caret position: parser directives, the logical instruction at the caret, the token
/// being typed, and the names in scope (stages, global and stage ARGs, ENV, users created by RUN).
/// </summary>
public sealed class DockerfileCaretScan
{
    private static readonly Regex DirectiveRx = new(@"^#\s*([A-Za-z]+)\s*=\s*(.*?)\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex HeredocRx = new(@"<<(-?)([""']?)([A-Za-z_][A-Za-z0-9_]*)\2", RegexOptions.CultureInvariant);
    private static readonly Regex UserRx = new(@"\b(?:useradd|adduser)\b([^&;|\n]*)", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Known = new(DockerfileCompletionData.Instructions.Select(i => i.Name), StringComparer.OrdinalIgnoreCase);

    public string Text { get; private init; } = "";
    public int Caret { get; private init; }
    public char Escape { get; private set; } = '\\';
    public DockerfileSyntaxVersion? Syntax { get; private set; }
    public int SyntaxLine { get; private set; } = -1;
    public IReadOnlyDictionary<string, string> Directives => directives;
    private readonly Dictionary<string, string> directives = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when every line before the caret line is a parser directive.</summary>
    public bool DirectiveRegion { get; private set; }
    public bool CommentLine { get; private set; }
    public bool InHeredoc { get; private set; }
    public int LineStart { get; private set; }
    /// <summary>Text of the caret's physical line before the caret.</summary>
    public string LineBefore { get; private set; } = "";

    public IReadOnlyList<DockerfileScanInstruction> Instructions => instructions;
    private readonly List<DockerfileScanInstruction> instructions = new();
    public DockerfileScanInstruction? Current { get; private set; }

    /// <summary>The instruction keyword in upper case (ONBUILD's inner keyword when present), or null while typing it.</summary>
    public string? Keyword { get; private set; }
    public bool Onbuild { get; private set; }
    public bool TypingKeyword { get; private set; }
    /// <summary>Argument tokens up to the caret; the last one is the token being typed (possibly empty).</summary>
    public List<DockerfileCaretToken> Args { get; } = new();
    public string Token { get; private set; } = "";
    public int TokenStart { get; private set; }
    /// <summary>End of the token at the caret, including characters after the caret.</summary>
    public int TokenEnd { get; private set; }

    public List<DockerfileScanStage> Stages { get; } = new();
    public int StageIndex { get; private set; } = -1;
    public bool InFromLine => Keyword == "FROM" && !TypingKeyword;
    public List<string> GlobalArgs { get; } = new();
    public List<string> StageArgs { get; } = new();
    public List<string> StageEnv { get; } = new();
    public List<string> Users { get; } = new();

    public static DockerfileCaretScan Run(string text, int caret)
    {
        text ??= "";
        caret = Math.Clamp(caret, 0, text.Length);
        var scan = new DockerfileCaretScan { Text = text, Caret = caret };
        scan.Analyse();
        return scan;
    }

    private void Analyse()
    {
        var lines = new List<(int Start, string Text)>();
        var start = 0;
        for (var i = 0; i <= Text.Length; i++)
        {
            if (i == Text.Length || Text[i] == '\n')
            {
                var line = Text[start..i];
                if (line.EndsWith('\r')) line = line[..^1];
                lines.Add((start, line));
                start = i + 1;
            }
        }
        var caretLine = 0;
        for (var i = 0; i < lines.Count; i++) if (lines[i].Start <= Caret) caretLine = i;
        LineStart = lines[caretLine].Start;
        LineBefore = lines[caretLine].Text[..Math.Min(lines[caretLine].Text.Length, Caret - LineStart)];

        var directiveEnd = lines.Count;
        var row = 0;
        var directive = true;
        while (row < lines.Count)
        {
            var trimmed = lines[row].Text.TrimStart();
            if (trimmed.StartsWith('#'))
            {
                var m = directive ? DirectiveRx.Match(trimmed) : Match.Empty;
                if (m.Success && m.Groups[1].Value.ToLowerInvariant() is "syntax" or "escape" or "check" && !directives.ContainsKey(m.Groups[1].Value))
                {
                    directives[m.Groups[1].Value] = m.Groups[2].Value;
                    if (m.Groups[1].Value.Equals("escape", StringComparison.OrdinalIgnoreCase) && m.Groups[2].Value == "`") Escape = '`';
                    if (m.Groups[1].Value.Equals("syntax", StringComparison.OrdinalIgnoreCase)) { Syntax = DockerfileSyntaxVersion.Parse(m.Groups[2].Value); SyntaxLine = row; }
                }
                else if (directive) { directive = false; directiveEnd = row; }
                row++;
                continue;
            }
            if (directive) { directive = false; directiveEnd = row; }
            if (trimmed.Trim().Length == 0) { row++; continue; }
            var first = row;
            var last = row;
            while (EndsWithEscape(lines[last].Text) && last + 1 < lines.Count)
            {
                last++;
                while (lines[last].Text.TrimStart().StartsWith('#') && last + 1 < lines.Count) last++;
            }
            var tokens = new List<DockerfileCaretToken>();
            for (var l = first; l <= last; l++)
                if (l == first || !lines[l].Text.TrimStart().StartsWith('#')) Tokenise(lines[l].Text, lines[l].Start, tokens, int.MaxValue);
            var keyword = tokens.Count > 0 ? tokens[0].Text : "";
            var bodyEnd = last;
            var effective = keyword.Equals("ONBUILD", StringComparison.OrdinalIgnoreCase) && tokens.Count > 1 ? tokens[1].Text : keyword;
            if (effective.ToUpperInvariant() is "RUN" or "COPY" or "ADD")
            {
                var logical = string.Join("\n", lines.Skip(first).Take(last - first + 1).Select(x => x.Text));
                foreach (Match h in HeredocRx.Matches(logical))
                {
                    var word = h.Groups[3].Value;
                    var dash = h.Groups[1].Value == "-";
                    var k = bodyEnd + 1;
                    while (k < lines.Count && (dash ? lines[k].Text.TrimStart('\t') : lines[k].Text) != word) k++;
                    bodyEnd = Math.Min(k, lines.Count - 1);
                }
            }
            instructions.Add(new DockerfileScanInstruction(keyword, first, last, bodyEnd, tokens));
            row = bodyEnd + 1;
        }
        DirectiveRegion = caretLine <= directiveEnd;

        Current = instructions.FirstOrDefault(i => i.StartLine <= caretLine && caretLine <= i.BodyEndLine);
        var caretText = lines[caretLine].Text.TrimStart();
        if (Current != null && caretLine > Current.EndLine) { InHeredoc = true; }
        else if (caretText.StartsWith('#') && (Current == null || caretLine != Current.StartLine)) { CommentLine = true; Current = null; }

        ScanScope(Current?.StartLine ?? caretLine);
        TokenEnd = Caret;
        while (TokenEnd < Text.Length && !char.IsWhiteSpace(Text[TokenEnd])) TokenEnd++;
        if (CommentLine) return;
        if (InHeredoc)
        {
            var lineTokens = new List<DockerfileCaretToken>();
            Tokenise(LineBefore, LineStart, lineTokens, int.MaxValue);
            SetToken(lineTokens, LineBefore);
            return;
        }

        // Tokens of the current instruction up to the caret.
        var before = new List<DockerfileCaretToken>();
        string lastSegment = LineBefore;
        if (Current != null)
            for (var l = Current.StartLine; l <= caretLine; l++)
            {
                if (l != Current.StartLine && lines[l].Text.TrimStart().StartsWith('#')) continue;
                var segment = l == caretLine ? LineBefore : lines[l].Text;
                Tokenise(segment, lines[l].Start, before, int.MaxValue);
                if (l != caretLine && before.Count > 0 && before[^1].Text == Escape.ToString()) before.RemoveAt(before.Count - 1);
                else if (l != caretLine && before.Count > 0 && before[^1].Text.EndsWith(Escape)) before[^1] = before[^1] with { Text = before[^1].Text[..^1] };
                if (l == caretLine) lastSegment = segment;
            }
        var typingNew = lastSegment.Length == 0 || char.IsWhiteSpace(lastSegment[^1]);
        if (typingNew) before.Add(new DockerfileCaretToken("", Caret));
        if (before.Count == 0) before.Add(new DockerfileCaretToken("", Caret));
        var index = 0;
        if (before.Count == 1) { TypingKeyword = true; }
        else
        {
            Keyword = before[0].Text.ToUpperInvariant();
            index = 1;
            if (Keyword == "ONBUILD")
            {
                Onbuild = true;
                if (before.Count == 2) TypingKeyword = true;
                else { Keyword = before[1].Text.ToUpperInvariant(); index = 2; }
            }
        }
        if (TypingKeyword) Keyword = null;
        var current = before[^1];
        Token = current.Text;
        TokenStart = current.Start;
        if (!TypingKeyword) Args.AddRange(before.Skip(index));
        else Args.Add(current);
    }

    private void SetToken(List<DockerfileCaretToken> tokens, string segment)
    {
        if (segment.Length == 0 || char.IsWhiteSpace(segment[^1]) || tokens.Count == 0) { Token = ""; TokenStart = Caret; }
        else { Token = tokens[^1].Text; TokenStart = tokens[^1].Start; }
    }

    private bool EndsWithEscape(string line)
    {
        var t = line.TrimEnd();
        return t.Length > 0 && t[^1] == Escape && !t.TrimStart().StartsWith('#');
    }

    /// <summary>Splits on whitespace, keeping quoted runs together.</summary>
    public static void Tokenise(string segment, int offset, List<DockerfileCaretToken> into, int limit)
    {
        var i = 0;
        while (i < segment.Length && i < limit)
        {
            while (i < segment.Length && char.IsWhiteSpace(segment[i])) i++;
            if (i >= segment.Length) break;
            var s = i;
            char quote = '\0';
            while (i < segment.Length)
            {
                var c = segment[i];
                if (quote != '\0') { if (c == quote) quote = '\0'; }
                else if (c is '"' or '\'') quote = c;
                else if (char.IsWhiteSpace(c)) break;
                i++;
            }
            into.Add(new DockerfileCaretToken(segment[s..i], offset + s));
        }
    }

    private void ScanScope(int beforeLine)
    {
        var stageEnv = new List<List<string>>();
        var env = new List<string>();
        foreach (var instruction in instructions)
        {
            if (instruction.StartLine >= beforeLine) break;
            var keyword = instruction.EffectiveKeyword;
            if (instruction.Keyword.Equals("ONBUILD", StringComparison.OrdinalIgnoreCase)) continue;
            var args = instruction.Args;
            switch (keyword)
            {
                case "FROM":
                {
                    var positional = args.Where(t => !t.Text.StartsWith("--", StringComparison.Ordinal)).Select(t => t.Text).ToList();
                    var name = positional.Count >= 3 && positional[1].Equals("AS", StringComparison.OrdinalIgnoreCase) ? positional[2] : null;
                    var baseImage = positional.Count > 0 ? positional[0] : "";
                    Stages.Add(new DockerfileScanStage(name, baseImage, Stages.Count, instruction.StartLine));
                    StageIndex = Stages.Count - 1;
                    StageArgs.Clear();
                    env = new List<string>();
                    var parent = Stages.Take(Stages.Count - 1).LastOrDefault(s => s.Name != null && s.Name.Equals(baseImage, StringComparison.OrdinalIgnoreCase));
                    if (parent != null && parent.Index < stageEnv.Count) env.AddRange(stageEnv[parent.Index]);
                    stageEnv.Add(env);
                    Users.Clear();
                    break;
                }
                case "ARG":
                    foreach (var t in args)
                    {
                        var name = t.Text.Split('=')[0];
                        if (!IsName(name)) continue;
                        if (StageIndex < 0) Add(GlobalArgs, name); else Add(StageArgs, name);
                    }
                    break;
                case "ENV":
                    if (args.Count >= 1 && !args[0].Text.Contains('=')) { if (IsName(args[0].Text)) Add(env, args[0].Text); }
                    else foreach (var t in args) { var name = t.Text.Split('=')[0]; if (t.Text.Contains('=') && IsName(name)) Add(env, name); }
                    break;
                case "RUN":
                    foreach (Match m in UserRx.Matches(string.Join(" ", args.Select(a => a.Text))))
                    {
                        var parts = m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(p => p != "\\").ToList();
                        var candidate = parts.LastOrDefault();
                        if (candidate != null && Regex.IsMatch(candidate, "^[a-z_][a-z0-9_-]{0,31}$")) Add(Users, candidate);
                    }
                    break;
            }
        }
        if (StageIndex >= 0) StageEnv.AddRange(env);
    }

    private static void Add(List<string> list, string value) { if (!list.Contains(value, StringComparer.Ordinal)) list.Add(value); }
    public static bool IsName(string name) => Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$");
    public static bool IsInstruction(string word) => Known.Contains(word);
}
