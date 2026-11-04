using System.Text;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

public enum RegexReplacementKind { Literal, Dollar, WholeMatch, Group, BeforeMatch, AfterMatch, LastGroup, WholeInput, UnresolvedReference }

/// <summary>One piece of replacement text: literal text, or a $ reference with its plain description.</summary>
public sealed record RegexReplacementToken(RegexReplacementKind Kind, int Start, int Length, string Text, string Description, int GroupNumber = -1)
{
    public bool IsReference => Kind is not (RegexReplacementKind.Literal or RegexReplacementKind.Dollar);
}

/// <summary>A palette entry for the replacement box.</summary>
public sealed record RegexReplacementInsert(string Label, string Text, string Description);

/// <summary>
/// Independent parser and explainer for .NET replacement text, following the framework's own rules: $number, ${name}, $$, $&amp;,
/// $`, $', $+ and $_. A reference to a group the pattern does not have is literal text, and the explanation says so. There are no
/// escapes and no conditionals in .NET replacement text.
/// </summary>
public static class RegexReplacementParser
{
    public static readonly IReadOnlyList<RegexReplacementInsert> Palette =
    [
        new("$1", "$1", "The text captured by group 1."),
        new("${name}", "${name}", "The text captured by the group called name."),
        new("$0", "$0", "The whole match."),
        new("$&", "$&", "The whole match (same as $0)."),
        new("$`", "$`", "Everything in the text before the match."),
        new("$'", "$'", "Everything in the text after the match."),
        new("$+", "$+", "The last group of the pattern, counting the highest group number."),
        new("$_", "$_", "The whole text that was searched."),
        new("$$", "$$", "A literal dollar sign."),
    ];

    /// <summary>The names and numbers the pattern defines. An invalid pattern gives <see cref="Unknown"/>, so nothing is flagged unresolved.</summary>
    public sealed record GroupInfo(IReadOnlySet<int> Numbers, IReadOnlySet<string> Names, bool Known)
    {
        public static GroupInfo Unknown { get; } = new(new HashSet<int>(), new HashSet<string>(), false);
        public static GroupInfo Of(Regex regex) => new(new HashSet<int>(regex.GetGroupNumbers()), new HashSet<string>(regex.GetGroupNames(), StringComparer.Ordinal), true);
    }

    public static GroupInfo GroupsOf(string pattern, RegexBuilderFlags flags)
    {
        var c = RegexBuilderEngine.Compile(pattern, flags);
        return c.Valid ? GroupInfo.Of(c.Regex!) : GroupInfo.Unknown;
    }

    private static bool WordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>Splits replacement text into literal pieces and references.</summary>
    public static IReadOnlyList<RegexReplacementToken> Parse(string? replacement, GroupInfo groups)
    {
        var text = replacement ?? "";
        var tokens = new List<RegexReplacementToken>();
        var literal = new StringBuilder();
        var literalStart = 0;
        void FlushLiteral(int end)
        {
            if (literal.Length == 0) return;
            tokens.Add(new(RegexReplacementKind.Literal, literalStart, end - literalStart, text[literalStart..end], "Inserted as typed."));
            literal.Clear();
        }
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] != '$') { if (literal.Length == 0) literalStart = i; literal.Append(text[i]); i++; continue; }
            var token = ReadReference(text, i, groups);
            if (token == null) { if (literal.Length == 0) literalStart = i; literal.Append('$'); i++; continue; } // a lone $ is literal text
            FlushLiteral(i);
            tokens.Add(token);
            i += token.Length;
        }
        FlushLiteral(text.Length);
        return tokens;
    }

    private static RegexReplacementToken? ReadReference(string text, int at, GroupInfo groups)
    {
        var i = at + 1;
        if (i >= text.Length) return null;
        var c = text[i];
        var angled = false;
        if (c == '{' && text.Length - i > 1) { angled = true; i++; c = text[i]; }
        if (c is >= '0' and <= '9')
        {
            var digitsStart = i;
            long number = 0;
            var overflow = false;
            while (i < text.Length && text[i] is >= '0' and <= '9') { number = number * 10 + (text[i] - '0'); if (number > int.MaxValue) { overflow = true; number = int.MaxValue; } i++; }
            if (angled)
            {
                if (i >= text.Length || text[i] != '}') return null; // ${12x} is not a reference; the $ is literal
                i++;
            }
            var raw = text[at..i];
            if (overflow || !Resolved(groups, (int)number)) return new(RegexReplacementKind.UnresolvedReference, at, i - at, raw, $"{raw} refers to group {(overflow ? "a very large number" : number.ToString())}, which the pattern does not have, so it is inserted as the text {raw}.");
            return new(number == 0 ? RegexReplacementKind.WholeMatch : RegexReplacementKind.Group, at, i - at, raw, number == 0 ? $"{raw} inserts the whole match." : $"{raw} inserts the text captured by group {number} (nothing if that group did not take part).", (int)number);
        }
        if (angled && WordChar(c))
        {
            var nameStart = i;
            while (i < text.Length && WordChar(text[i])) i++;
            if (i >= text.Length || text[i] != '}') return null;
            var name = text[nameStart..i];
            i++;
            var raw = text[at..i];
            if (!groups.Known || groups.Names.Contains(name)) return new(RegexReplacementKind.Group, at, i - at, raw, $"{raw} inserts the text captured by the group called {name} (nothing if it did not take part).");
            return new(RegexReplacementKind.UnresolvedReference, at, i - at, raw, $"{raw} names a group the pattern does not have, so it is inserted as the text {raw}.");
        }
        if (angled) return null;
        return c switch
        {
            '$' => new(RegexReplacementKind.Dollar, at, 2, "$$", "$$ inserts one literal dollar sign."),
            '&' => new(RegexReplacementKind.WholeMatch, at, 2, "$&", "$& inserts the whole match (same as $0).", 0),
            '`' => new(RegexReplacementKind.BeforeMatch, at, 2, "$`", "$` inserts everything in the text before the match."),
            '\'' => new(RegexReplacementKind.AfterMatch, at, 2, "$'", "$' inserts everything in the text after the match."),
            '+' => new(RegexReplacementKind.LastGroup, at, 2, "$+", "$+ inserts the text of the last group of the pattern (the highest group number), even when it is empty."),
            '_' => new(RegexReplacementKind.WholeInput, at, 2, "$_", "$_ inserts the whole text that was searched."),
            _ => null,
        };
    }

    private static bool Resolved(GroupInfo groups, int number) => groups.Known ? groups.Numbers.Contains(number) : true;

    /// <summary>Warnings for references the pattern cannot satisfy (inserted literally by .NET).</summary>
    public static IReadOnlyList<string> Warnings(string? replacement, GroupInfo groups) =>
        Parse(replacement, groups).Where(t => t.Kind == RegexReplacementKind.UnresolvedReference).Select(t => t.Description).ToList();

    /// <summary>Plain-language explanation, one line per piece.</summary>
    public static IReadOnlyList<string> Explain(string? replacement, GroupInfo groups)
    {
        var lines = new List<string>();
        foreach (var t in Parse(replacement, groups))
            lines.Add(t.Kind == RegexReplacementKind.Literal ? $"\"{Visible(t.Text)}\": inserted as typed." : t.Description);
        return lines;
    }

    private static string Visible(string s) => s.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");

    /// <summary>
    /// Produces the replacement for one match from the parsed pieces. This mirrors what the framework does, and the checks compare it with
    /// <see cref="Match.Result"/> so a divergence is caught.
    /// </summary>
    public static string Expand(IReadOnlyList<RegexReplacementToken> tokens, Match match, string input, Regex regex)
    {
        var sb = new StringBuilder();
        foreach (var t in tokens)
        {
            switch (t.Kind)
            {
                case RegexReplacementKind.Literal: sb.Append(t.Text); break;
                case RegexReplacementKind.Dollar: sb.Append('$'); break;
                case RegexReplacementKind.WholeMatch: sb.Append(match.Value); break;
                case RegexReplacementKind.BeforeMatch: sb.Append(input, 0, match.Index); break;
                case RegexReplacementKind.AfterMatch: sb.Append(input, match.Index + match.Length, input.Length - match.Index - match.Length); break;
                case RegexReplacementKind.WholeInput: sb.Append(input); break;
                case RegexReplacementKind.LastGroup: { var g = match.Groups[match.Groups.Count - 1]; if (g.Success) sb.Append(g.Value); break; }
                case RegexReplacementKind.UnresolvedReference: sb.Append(t.Text); break;
                case RegexReplacementKind.Group:
                    {
                        Group g;
                        if (t.GroupNumber >= 0) g = match.Groups[t.GroupNumber];
                        else g = match.Groups[t.Text[2..^1]];
                        if (g.Success) sb.Append(g.Value);
                        break;
                    }
            }
        }
        return sb.ToString();
    }

    /// <summary>Text that is inserted exactly as typed: every $ becomes $$.</summary>
    public static string EncodeLiteral(string? text) => (text ?? "").Replace("$", "$$");

    /// <summary>
    /// Turns typed escapes into the characters they stand for, so a line break or tab can be placed in replacement text:
    /// \n \r \t \\ \0, \xHH, \uHHHH and \u{H…H}. .NET replacement text itself has no escapes: the result is inserted as plain characters.
    /// </summary>
    public static string DecodeEscapes(string? text)
    {
        var s = text ?? "";
        var sb = new StringBuilder();
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] != '\\') { sb.Append(s[i]); continue; }
            if (++i >= s.Length) throw new ArgumentException("The text ends with a lone backslash.");
            switch (s[i])
            {
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case '0': sb.Append('\0'); break;
                case '\\': sb.Append('\\'); break;
                case 'x': sb.Append(Hex(s, ref i, 2)); break;
                case 'u':
                    if (i + 1 < s.Length && s[i + 1] == '{')
                    {
                        var close = s.IndexOf('}', i + 2);
                        if (close < 0 || close == i + 2 || close - i - 2 > 6) throw new ArgumentException("Use \\u{} with one to six hexadecimal digits.");
                        if (!int.TryParse(s.AsSpan(i + 2, close - i - 2), System.Globalization.NumberStyles.AllowHexSpecifier, null, out var cp) || cp > 0x10FFFF || cp is >= 0xD800 and <= 0xDFFF) throw new ArgumentException("That is not a valid Unicode code point.");
                        sb.Append(char.ConvertFromUtf32(cp));
                        i = close;
                    }
                    else sb.Append(Hex(s, ref i, 4));
                    break;
                default: throw new ArgumentException($"\\{s[i]} is not an escape this helper knows. Use \\n \\r \\t \\\\ \\0 \\xHH \\uHHHH or \\u{{code point}}.");
            }
        }
        return sb.ToString();

        static string Hex(string s, ref int i, int digits)
        {
            if (i + digits > s.Length - 1) throw new ArgumentException($"Expected {digits} hexadecimal digits.");
            var span = s.AsSpan(i + 1, digits);
            if (!int.TryParse(span, System.Globalization.NumberStyles.AllowHexSpecifier, null, out var value)) throw new ArgumentException($"Expected {digits} hexadecimal digits.");
            i += digits;
            return ((char)value).ToString();
        }
    }
}
