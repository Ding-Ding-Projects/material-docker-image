using System.Globalization;
using System.Text;

namespace MaterialDockerImage.UI;

public enum RegexTokenFamily { Literal, CharacterClass, Any, UnicodeCategory, Anchor, Group, Lookaround, Alternation, Branch, Quantifier, Backreference, InlineOptions, Comment, Conditional, Error }

/// <summary>One token of a pattern with its source span and a plain English description.</summary>
public sealed class RegexExplainNode
{
    public RegexTokenFamily Family { get; init; }
    public int Start { get; init; }
    public int Length { get; set; }
    public string Text { get; set; } = "";
    public string Description { get; set; } = "";
    public List<RegexExplainNode> Children { get; } = new();
    /// <summary>Decoded literal text for literal runs (used to merge adjacent literals).</summary>
    internal string? Literal { get; set; }
}

/// <summary>
/// Hand-written tokenizer and parser for the .NET regular expression syntax, used only to explain a pattern.
/// It never decides validity (the framework parser does); malformed input produces Error nodes instead of throwing.
/// </summary>
public static class RegexExplainer
{
    public static IReadOnlyList<RegexExplainNode> Explain(string pattern, RegexBuilderFlags flags)
    {
        var p = new Parser(pattern ?? "", flags);
        try { return p.Run(); }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            return [new RegexExplainNode { Family = RegexTokenFamily.Error, Start = 0, Length = pattern?.Length ?? 0, Text = pattern ?? "", Description = "The pattern could not be explained." }];
        }
    }

    /// <summary>Depth-first list of nodes with their depth, for rendering as an indented tree or for tests.</summary>
    public static IEnumerable<(RegexExplainNode Node, int Depth)> Flatten(IEnumerable<RegexExplainNode> nodes, int depth = 0)
    {
        foreach (var n in nodes)
        {
            yield return (n, depth);
            foreach (var c in Flatten(n.Children, depth + 1)) yield return c;
        }
    }

    public static readonly IReadOnlyDictionary<string, string> UnicodeCategories = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["L"] = "letter", ["Lu"] = "uppercase letter", ["Ll"] = "lowercase letter", ["Lt"] = "title-case letter", ["Lm"] = "modifier letter", ["Lo"] = "other letter",
        ["M"] = "mark", ["Mn"] = "non-spacing mark", ["Mc"] = "spacing combining mark", ["Me"] = "enclosing mark",
        ["N"] = "number", ["Nd"] = "decimal digit", ["Nl"] = "letter number", ["No"] = "other number",
        ["P"] = "punctuation", ["Pc"] = "connector punctuation", ["Pd"] = "dash", ["Ps"] = "opening punctuation", ["Pe"] = "closing punctuation", ["Pi"] = "initial quote", ["Pf"] = "final quote", ["Po"] = "other punctuation",
        ["S"] = "symbol", ["Sm"] = "math symbol", ["Sc"] = "currency symbol", ["Sk"] = "modifier symbol", ["So"] = "other symbol",
        ["Z"] = "separator", ["Zs"] = "space separator", ["Zl"] = "line separator", ["Zp"] = "paragraph separator",
        ["C"] = "other (control or format)", ["Cc"] = "control character", ["Cf"] = "format character", ["Cs"] = "surrogate", ["Co"] = "private-use character", ["Cn"] = "unassigned code point",
    };

    internal static string ClassEscape(char c) => c switch
    {
        'd' => "a digit (Unicode decimal digit)",
        'D' => "any character that is not a digit",
        'w' => "a word character (letter, digit, connector such as _)",
        'W' => "any character that is not a word character",
        's' => "a white-space character",
        'S' => "any character that is not white space",
        _ => "",
    };

    private sealed class Parser
    {
        private readonly string p;
        private int pos;
        private bool extended;
        private bool explicitCapture;
        private readonly bool multiline, singleline;
        private int unnamed;
        private readonly List<(RegexExplainNode Node, string Name)> named = new();

        public Parser(string pattern, RegexBuilderFlags flags)
        {
            p = pattern; extended = flags.IgnorePatternWhitespace; explicitCapture = flags.ExplicitCapture; multiline = flags.Multiline; singleline = flags.Singleline;
        }

        public List<RegexExplainNode> Run()
        {
            var nodes = Sequence(0);
            while (pos < p.Length) // stray ')' at top level
            {
                nodes.Add(Error(pos, 1, "This ) has no matching opening parenthesis."));
                pos++;
                nodes.AddRange(Sequence(0));
            }
            // .NET numbers unnamed groups first, then named groups in order of first appearance.
            var next = unnamed + 1;
            var numbers = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (node, name) in named)
            {
                if (int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var explicitNumber)) { node.Description += $" (group number {explicitNumber})"; continue; }
                if (!numbers.TryGetValue(name, out var n)) numbers[name] = n = next++;
                node.Description += $" (group number {n})";
            }
            return nodes;
        }

        private static RegexExplainNode Error(int start, int length, string message) => new() { Family = RegexTokenFamily.Error, Start = start, Length = length, Text = "", Description = message };

        private string Slice(int start) => p[start..pos];

        private List<RegexExplainNode> Sequence(int depth)
        {
            var branches = new List<List<RegexExplainNode>> { new() };
            var branchStarts = new List<int> { pos };
            while (pos < p.Length)
            {
                var c = p[pos];
                if (c == ')') break; // closes the enclosing group, or is reported as stray by Run at depth 0
                if (c == '|') { pos++; branches.Add(new()); branchStarts.Add(pos); continue; }
                if (extended && char.IsWhiteSpace(c)) { pos++; continue; }
                if (extended && c == '#')
                {
                    var start = pos;
                    while (pos < p.Length && p[pos] != '\n') pos++;
                    branches[^1].Add(new RegexExplainNode { Family = RegexTokenFamily.Comment, Start = start, Length = pos - start, Text = Slice(start), Description = "Comment (ignored because IgnorePatternWhitespace is on)." });
                    continue;
                }
                var atom = Atom();
                if (atom == null) continue;
                atom = Quantify(atom);
                var list = branches[^1];
                if (atom.Literal != null && list.Count > 0 && list[^1].Literal != null && list[^1].Start + list[^1].Length == atom.Start)
                {
                    var prev = list[^1];
                    prev.Literal += atom.Literal; prev.Length += atom.Length; prev.Text = p.Substring(prev.Start, prev.Length);
                    prev.Description = DescribeLiteral(prev.Literal!);
                }
                else list.Add(atom);
            }
            if (branches.Count == 1) return branches[0];
            var alt = new RegexExplainNode { Family = RegexTokenFamily.Alternation, Start = branchStarts[0], Length = pos - branchStarts[0], Description = $"Either of {branches.Count} alternatives, tried from left to right (|)." };
            alt.Text = p.Substring(alt.Start, alt.Length);
            for (var i = 0; i < branches.Count; i++)
            {
                var start = branchStarts[i];
                var end = i + 1 < branches.Count ? branchStarts[i + 1] - 1 : pos;
                var b = new RegexExplainNode { Family = RegexTokenFamily.Branch, Start = start, Length = end - start, Text = p[start..end], Description = branches[i].Count == 0 ? $"Alternative {i + 1}: matches the empty string." : $"Alternative {i + 1}." };
                b.Children.AddRange(branches[i]);
                alt.Children.Add(b);
            }
            return [alt];
        }

        private static string DescribeLiteral(string value)
        {
            var shown = new StringBuilder();
            foreach (var ch in value) shown.Append(ch switch { '\n' => "\\n", '\r' => "\\r", '\t' => "\\t", ' ' => "·", _ => ch.ToString() });
            return value.Length == 1 ? $"The character '{shown}'." : $"The exact text \"{shown}\".";
        }

        private RegexExplainNode Literal(int start, string value) => new() { Family = RegexTokenFamily.Literal, Start = start, Length = pos - start, Text = Slice(start), Literal = value, Description = DescribeLiteral(value) };

        private RegexExplainNode? Atom()
        {
            var start = pos;
            var c = p[pos];
            switch (c)
            {
                case '(': return Group();
                case '[': return Set();
                case '\\': return Escape();
                case '.':
                    pos++;
                    return new() { Family = RegexTokenFamily.Any, Start = start, Length = 1, Text = ".", Description = singleline ? "Any character, including a newline (Singleline is on)." : "Any character except a newline." };
                case '^':
                    pos++;
                    return new() { Family = RegexTokenFamily.Anchor, Start = start, Length = 1, Text = "^", Description = multiline ? "Start of a line (Multiline is on)." : "Start of the text." };
                case '$':
                    pos++;
                    return new() { Family = RegexTokenFamily.Anchor, Start = start, Length = 1, Text = "$", Description = multiline ? "End of a line (Multiline is on)." : "End of the text, or just before a final newline." };
                case '*' or '+' or '?':
                    pos++;
                    return Error(start, 1, $"The quantifier {c} has nothing before it to repeat.");
                case '{' when QuantifierLength(pos) > 0:
                    pos += QuantifierLength(pos);
                    return Error(start, pos - start, "This quantifier has nothing before it to repeat.");
                default:
                    pos++;
                    if (char.IsHighSurrogate(c) && pos < p.Length && char.IsLowSurrogate(p[pos])) pos++;
                    return Literal(start, Slice(start));
            }
        }

        /// <summary>Length of a {n}, {n,} or {n,m} quantifier at <paramref name="at"/>, or 0 when the brace is a literal.</summary>
        private int QuantifierLength(int at)
        {
            var i = at + 1;
            var digits = i;
            while (i < p.Length && char.IsAsciiDigit(p[i])) i++;
            if (i == digits) return 0;
            if (i < p.Length && p[i] == ',') { i++; while (i < p.Length && char.IsAsciiDigit(p[i])) i++; }
            return i < p.Length && p[i] == '}' ? i + 1 - at : 0;
        }

        private RegexExplainNode Quantify(RegexExplainNode atom)
        {
            if (pos >= p.Length || atom.Family is RegexTokenFamily.Error or RegexTokenFamily.Comment or RegexTokenFamily.InlineOptions) return atom;
            var c = p[pos];
            var qStart = pos;
            string what;
            if (c == '*') { pos++; what = "zero or more times"; }
            else if (c == '+') { pos++; what = "one or more times"; }
            else if (c == '?') { pos++; what = "optionally (zero or one time)"; }
            else if (c == '{' && QuantifierLength(pos) is var len and > 0)
            {
                var body = p.Substring(pos + 1, len - 2);
                pos += len;
                var parts = body.Split(',');
                what = parts.Length == 1 ? $"exactly {parts[0]} time{(parts[0] == "1" ? "" : "s")}" : parts[1].Length == 0 ? $"at least {parts[0]} times" : $"between {parts[0]} and {parts[1]} times";
                if (parts.Length == 2 && parts[1].Length > 0 && long.TryParse(parts[0], out var lo) && long.TryParse(parts[1], out var hi) && hi < lo)
                    return Error(qStart, len, "Illegal {x,y} quantifier: the maximum is smaller than the minimum.");
            }
            else return atom;
            var lazy = pos < p.Length && p[pos] == '?';
            if (lazy) pos++;
            var node = new RegexExplainNode
            {
                Family = RegexTokenFamily.Quantifier, Start = atom.Start, Length = pos - atom.Start, Text = p[atom.Start..pos],
                Description = $"Repeats the previous item {what}, {(lazy ? "as few times as possible (lazy)" : "as many times as possible (greedy)")}.",
            };
            node.Children.Add(atom);
            if (pos < p.Length && (p[pos] is '*' or '+' or '?' || (p[pos] == '{' && QuantifierLength(pos) > 0)))
            {
                var e = pos; pos += p[pos] == '{' ? QuantifierLength(pos) : 1;
                node.Children.Add(Error(e, pos - e, "Nested quantifier: a quantifier cannot directly follow another quantifier."));
            }
            return node;
        }

        private RegexExplainNode Group()
        {
            var start = pos;
            pos++; // (
            RegexExplainNode node;
            var savedExtended = extended; var savedExplicit = explicitCapture;
            if (Peek("?#"))
            {
                var close = p.IndexOf(')', pos);
                pos = close < 0 ? p.Length : close + 1;
                return new() { Family = close < 0 ? RegexTokenFamily.Error : RegexTokenFamily.Comment, Start = start, Length = pos - start, Text = Slice(start), Description = close < 0 ? "Unterminated (?#...) comment." : "Inline comment (ignored)." };
            }
            if (Peek("?:")) { pos += 2; node = Make(RegexTokenFamily.Group, "Non-capturing group: groups items without saving the match."); }
            else if (Peek("?=")) { pos += 2; node = Make(RegexTokenFamily.Lookaround, "Positive lookahead: the following text must match this, without consuming it."); }
            else if (Peek("?!")) { pos += 2; node = Make(RegexTokenFamily.Lookaround, "Negative lookahead: the following text must not match this."); }
            else if (Peek("?<=")) { pos += 3; node = Make(RegexTokenFamily.Lookaround, "Positive lookbehind: the preceding text must match this."); }
            else if (Peek("?<!")) { pos += 3; node = Make(RegexTokenFamily.Lookaround, "Negative lookbehind: the preceding text must not match this."); }
            else if (Peek("?>")) { pos += 2; node = Make(RegexTokenFamily.Group, "Atomic group: once matched, the engine does not backtrack into it."); }
            else if (Peek("?(")) return Conditional(start);
            else if (Peek("?<") || Peek("?'"))
            {
                var closeChar = p[pos + 1] == '<' ? '>' : '\'';
                pos += 2;
                var nameStart = pos;
                while (pos < p.Length && p[pos] != closeChar && p[pos] != ')') pos++;
                var name = p[nameStart..pos];
                if (pos >= p.Length || p[pos] != closeChar) return Unclosed(start, "The group name is not terminated.");
                pos++;
                var dash = name.IndexOf('-');
                if (dash >= 0)
                    node = Make(RegexTokenFamily.Group, dash == 0 ? $"Balancing group: removes the most recent capture of group '{name[1..]}'." : $"Balancing group: captures into '{name[..dash]}' and removes the most recent capture of '{name[(dash + 1)..]}'.");
                else if (!ValidName(name)) node = Make(RegexTokenFamily.Error, $"'{name}' is not a valid group name. Use letters, digits and _ and do not start with a digit (or use a number only).");
                else { node = Make(RegexTokenFamily.Group, $"Named capturing group '{name}'"); named.Add((node, name)); }
            }
            else if (pos < p.Length && p[pos] == '?')
            {
                var optStart = pos + 1; var i = optStart;
                while (i < p.Length && "imnsx-".Contains(p[i])) i++;
                if (i < p.Length && (p[i] == ')' || p[i] == ':') && i > optStart)
                {
                    var spec = p[optStart..i];
                    ApplyInline(spec);
                    if (p[i] == ')')
                    {
                        pos = i + 1;
                        return new() { Family = RegexTokenFamily.InlineOptions, Start = start, Length = pos - start, Text = Slice(start), Description = "Inline options for the rest of the enclosing group: " + DescribeOptions(spec) + "." };
                    }
                    pos = i + 1;
                    node = Make(RegexTokenFamily.Group, "Non-capturing group with options " + DescribeOptions(spec) + ".");
                }
                else
                {
                    pos++;
                    node = Make(RegexTokenFamily.Error, "Unrecognized grouping construct after (?.");
                }
            }
            else if (explicitCapture) node = Make(RegexTokenFamily.Group, "Group that does not capture (ExplicitCapture is on).");
            else { unnamed++; node = Make(RegexTokenFamily.Group, $"Capturing group {unnamed}: saves the matched text as group {unnamed}."); }

            node.Children.AddRange(Sequence(1));
            if (pos < p.Length && p[pos] == ')') pos++;
            else node.Children.Add(Error(pos, 0, "Missing closing parenthesis for the group opened at position " + start + "."));
            extended = savedExtended; explicitCapture = savedExplicit;
            node.Length = pos - start; node.Text = Slice(start);
            return node;

            RegexExplainNode Make(RegexTokenFamily family, string description) => new() { Family = family, Start = start, Description = description };
        }

        private RegexExplainNode Unclosed(int start, string message) { pos = Math.Min(pos, p.Length); return Error(start, pos - start, message); }

        private RegexExplainNode Conditional(int start)
        {
            pos += 1; // at '(' of the condition
            var node = new RegexExplainNode { Family = RegexTokenFamily.Conditional, Start = start };
            var close = p.IndexOf(')', pos);
            var inner = close > pos ? p[(pos + 1)..close] : "";
            if (inner.Length > 0 && inner.All(ch => char.IsLetterOrDigit(ch) || ch == '_'))
            {
                node.Description = $"Conditional: if group '{inner}' has captured, match the first alternative, otherwise the second.";
                pos = close + 1;
            }
            else
            {
                node.Description = "Conditional: if the expression in the first parentheses matches here, match the first alternative, otherwise the second.";
                node.Children.Add(Group());
            }
            node.Children.AddRange(Sequence(1));
            if (pos < p.Length && p[pos] == ')') pos++;
            else node.Children.Add(Error(pos, 0, "Missing closing parenthesis for the conditional opened at position " + start + "."));
            node.Length = pos - start; node.Text = Slice(start);
            return node;
        }

        private void ApplyInline(string spec)
        {
            var on = true;
            foreach (var ch in spec)
            {
                if (ch == '-') { on = false; continue; }
                if (ch == 'x') extended = on;
                if (ch == 'n') explicitCapture = on;
            }
        }

        private static string DescribeOptions(string spec)
        {
            var parts = new List<string>();
            var on = true;
            foreach (var ch in spec)
            {
                if (ch == '-') { on = false; continue; }
                var name = ch switch { 'i' => "IgnoreCase", 'm' => "Multiline", 'n' => "ExplicitCapture", 's' => "Singleline", 'x' => "IgnorePatternWhitespace", _ => "?" };
                parts.Add((on ? "turn on " : "turn off ") + name);
            }
            return string.Join(", ", parts);
        }

        private static bool ValidName(string name) =>
            name.Length > 0 && (name.All(char.IsAsciiDigit) || ((char.IsLetter(name[0]) || name[0] == '_') && name.All(ch => char.IsLetterOrDigit(ch) || ch == '_')));

        private bool Peek(string s) => string.CompareOrdinal(p, pos, s, 0, s.Length) == 0;

        private RegexExplainNode Escape()
        {
            var start = pos;
            pos++;
            if (pos >= p.Length) return Error(start, 1, "Illegal \\ at the end of the pattern.");
            var c = p[pos++];
            RegexExplainNode Node(RegexTokenFamily family, string description) => new() { Family = family, Start = start, Length = pos - start, Text = Slice(start), Description = description };
            switch (c)
            {
                case 'd' or 'D' or 'w' or 'W' or 's' or 'S': return Node(RegexTokenFamily.CharacterClass, "Matches " + ClassEscape(c) + ".");
                case 'b': return Node(RegexTokenFamily.Anchor, "Word boundary: between a word character and a non-word character.");
                case 'B': return Node(RegexTokenFamily.Anchor, "Not a word boundary.");
                case 'A': return Node(RegexTokenFamily.Anchor, "Start of the text only (ignores Multiline).");
                case 'z': return Node(RegexTokenFamily.Anchor, "End of the text only.");
                case 'Z': return Node(RegexTokenFamily.Anchor, "End of the text, or just before a final newline.");
                case 'G': return Node(RegexTokenFamily.Anchor, "Where the previous match ended.");
                case 'p' or 'P': return Category(start, c == 'P');
                case 'k':
                    if (pos < p.Length && (p[pos] == '<' || p[pos] == '\''))
                    {
                        var closeChar = p[pos] == '<' ? '>' : '\'';
                        var close = p.IndexOf(closeChar, pos + 1);
                        if (close < 0) { pos = p.Length; return Error(start, pos - start, "Unterminated \\k<name> backreference."); }
                        var name = p[(pos + 1)..close];
                        pos = close + 1;
                        return Node(RegexTokenFamily.Backreference, $"Backreference: the same text that group '{name}' captured.");
                    }
                    return Node(RegexTokenFamily.Error, "\\k must be followed by <name> or 'name'.");
                case '0':
                    {
                        var n = 0; while (n < 2 && pos < p.Length && p[pos] is >= '0' and <= '7') { pos++; n++; }
                        var value = Convert.ToInt32(Slice(start)[1..], 8);
                        return LiteralNode(start, ((char)value).ToString(), $"The character with octal code {Slice(start)[1..]}.");
                    }
                case >= '1' and <= '9':
                    {
                        while (pos < p.Length && char.IsAsciiDigit(p[pos])) pos++;
                        return Node(RegexTokenFamily.Backreference, $"Backreference: the same text that group {Slice(start)[1..]} captured.");
                    }
                case 'n': return LiteralNode(start, "\n", "A newline character (\\n).");
                case 'r': return LiteralNode(start, "\r", "A carriage return (\\r).");
                case 't': return LiteralNode(start, "\t", "A tab character (\\t).");
                case 'f': return LiteralNode(start, "\f", "A form feed (\\f).");
                case 'v': return LiteralNode(start, "\v", "A vertical tab (\\v).");
                case 'e': return LiteralNode(start, "\u001B", "The escape character (\\e).");
                case 'a': return LiteralNode(start, "\a", "The bell character (\\a).");
                case 'x': return Hex(start, 2);
                case 'u': return Hex(start, 4);
                case 'c':
                    if (pos < p.Length && char.IsAsciiLetter(p[pos])) { pos++; return LiteralNode(start, ((char)(char.ToUpperInvariant(p[pos - 1]) - '@')).ToString(), $"The control character Ctrl+{char.ToUpperInvariant(p[pos - 1])}."); }
                    return Node(RegexTokenFamily.Error, "\\c must be followed by a letter.");
                default:
                    if (char.IsAsciiLetterOrDigit(c)) return Node(RegexTokenFamily.Error, $"Unrecognized escape sequence \\{c}.");
                    var escaped = Literal(start, c.ToString());
                    escaped.Description = DescribeLiteral(c.ToString()) + " (escaped)";
                    return escaped;
            }
        }

        private RegexExplainNode LiteralNode(int start, string value, string description) =>
            new() { Family = RegexTokenFamily.Literal, Start = start, Length = pos - start, Text = Slice(start), Description = description };

        private RegexExplainNode Hex(int start, int digits)
        {
            var from = pos;
            while (pos < p.Length && pos - from < digits && char.IsAsciiHexDigit(p[pos])) pos++;
            if (pos - from < digits) return Error(start, pos - start, $"\\{p[start + 1]} needs exactly {digits} hexadecimal digits.");
            var code = int.Parse(p[from..pos], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return LiteralNode(start, ((char)code).ToString(), $"The character U+{code:X4} ('{(char.IsControl((char)code) ? ' ' : (char)code)}').");
        }

        private RegexExplainNode Category(int start, bool negated)
        {
            if (pos >= p.Length || p[pos] != '{') return Error(start, pos - start, "\\p and \\P must be followed by a {Category} name.");
            var close = p.IndexOf('}', pos);
            if (close < 0) { pos = p.Length; return Error(start, pos - start, "Unterminated \\p{...} category."); }
            var name = p[(pos + 1)..close];
            pos = close + 1;
            var meaning = UnicodeCategories.TryGetValue(name, out var m) ? "Unicode category " + m + " (" + name + ")" : name.StartsWith("Is", StringComparison.Ordinal) ? "the Unicode block " + name[2..] : null;
            if (meaning == null) return Error(start, pos - start, $"Unknown Unicode category or block '{name}'.");
            return new() { Family = RegexTokenFamily.UnicodeCategory, Start = start, Length = pos - start, Text = Slice(start), Description = (negated ? "Any character that is not in " : "Any character in ") + meaning + "." };
        }

        private RegexExplainNode Set()
        {
            var start = pos;
            pos++;
            var negated = pos < p.Length && p[pos] == '^';
            if (negated) pos++;
            var node = new RegexExplainNode { Family = RegexTokenFamily.CharacterClass, Start = start };
            var items = new List<string>();
            var first = true;
            var closed = false;
            while (pos < p.Length)
            {
                var c = p[pos];
                if (c == ']' && !first) { pos++; closed = true; break; }
                first = false;
                if (c == '-' && pos + 1 < p.Length && p[pos + 1] == '[')
                {
                    pos++;
                    var sub = Set();
                    sub.Description = "Subtracted: " + sub.Description;
                    node.Children.Add(sub);
                    items.Add("minus the subtracted set");
                    continue;
                }
                var itemStart = pos;
                var (low, lowText) = SetChar();
                if (lowText == null) { node.Children.Add(Error(itemStart, pos - itemStart, "Unrecognized escape in the character set.")); continue; }
                if (low != null && pos + 1 < p.Length && p[pos] == '-' && p[pos + 1] != ']' && p[pos + 1] != '[')
                {
                    pos++;
                    var (high, highText) = SetChar();
                    if (high == null || highText == null) { node.Children.Add(Error(itemStart, pos - itemStart, "A range must end with a single character.")); continue; }
                    var bad = high.Value < low.Value;
                    node.Children.Add(new RegexExplainNode { Family = bad ? RegexTokenFamily.Error : RegexTokenFamily.CharacterClass, Start = itemStart, Length = pos - itemStart, Text = Slice(itemStart), Description = bad ? $"Range in reverse order: '{high}' comes before '{low}'." : $"Range from '{lowText}' to '{highText}'." });
                    items.Add(lowText + "–" + highText);
                }
                else
                {
                    node.Children.Add(new RegexExplainNode { Family = low == null ? RegexTokenFamily.CharacterClass : RegexTokenFamily.Literal, Start = itemStart, Length = pos - itemStart, Text = Slice(itemStart), Description = low == null ? "Includes " + lowText + "." : $"The character '{lowText}'." });
                    items.Add(lowText);
                }
            }
            node.Length = pos - start; node.Text = Slice(start);
            if (!closed) { node.Children.Add(Error(pos, 0, "Unterminated [] set: add the closing ].")); }
            var list = items.Count > 8 ? string.Join(", ", items.Take(8)) + ", …" : string.Join(", ", items);
            node.Description = (negated ? "Any one character except: " : "Any one character from: ") + (list.Length == 0 ? "(empty set)" : list) + ".";
            return node;
        }

        /// <summary>Reads one set member. Returns the character for single characters, null for class escapes.</summary>
        private (char? Char, string? Text) SetChar()
        {
            var c = p[pos++];
            if (c != '\\') return (c, c == ' ' ? "space" : c.ToString());
            if (pos >= p.Length) return (null, null);
            var e = p[pos++];
            switch (e)
            {
                case 'd' or 'D' or 'w' or 'W' or 's' or 'S': return (null, ClassEscape(e));
                case 'p' or 'P':
                    {
                        var start = pos - 2;
                        var cat = Category(start, e == 'P');
                        return (null, cat.Family == RegexTokenFamily.Error ? null : cat.Description.TrimEnd('.').Replace("Any character in ", "").Replace("Any character that is not in ", "not "));
                    }
                case 'n': return ('\n', "newline");
                case 'r': return ('\r', "carriage return");
                case 't': return ('\t', "tab");
                case 'b': return ('\b', "backspace");
                case 'f': return ('\f', "form feed");
                case 'v': return ('\v', "vertical tab");
                case 'e': return ('\u001B', "escape");
                case 'a': return ('\a', "bell");
                case 'x' or 'u':
                    {
                        var digits = e == 'x' ? 2 : 4; var from = pos;
                        while (pos < p.Length && pos - from < digits && char.IsAsciiHexDigit(p[pos])) pos++;
                        if (pos - from < digits) return (null, null);
                        var code = (char)int.Parse(p[from..pos], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        return (code, $"U+{(int)code:X4}");
                    }
                default:
                    if (char.IsAsciiLetterOrDigit(e)) return (null, null);
                    return (e, e.ToString());
            }
        }
    }
}
