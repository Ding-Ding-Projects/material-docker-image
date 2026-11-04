using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>
/// Composers and palette for the rarer .NET constructs: escapes by code point, class subtraction, conditionals,
/// balancing groups, inline options, comments and the atomic stand-in for possessive quantifiers.
/// </summary>
public static class RegexBuilderConstructs
{
    /// <summary>Extra palette entries. Each one is valid on its own when wrapped as <c>Prefix + "a" + Suffix</c>.</summary>
    public static readonly IReadOnlyList<RegexToken> Palette =
    [
        new("Characters", @"\n", @"\n", "", "A newline (line feed)."),
        new("Characters", @"\r", @"\r", "", "A carriage return."),
        new("Characters", @"\t", @"\t", "", "A tab."),
        new("Characters", @"\f", @"\f", "", "A form feed."),
        new("Characters", @"\v", @"\v", "", "A vertical tab."),
        new("Characters", @"\e", @"\e", "", "The escape character (U+001B)."),
        new("Characters", @"\a", @"\a", "", "The bell character (U+0007)."),
        new("Characters", @"\0", @"\0", "", "The null character (U+0000)."),
        new("Anchors", @"\Z", @"\Z", "", "End of text, or just before a final newline."),
        new("Anchors", @"\G", @"\G", "", "Where the previous match ended (the start of the text for the first match)."),
        new("Classes", @"\p{L}", @"\p{L}", "", "Any letter in any script."),
        new("Classes", @"\p{Lu}", @"\p{Lu}", "", "An uppercase letter."),
        new("Classes", @"\p{N}", @"\p{N}", "", "Any kind of number."),
        new("Classes", @"\p{IsGreek}", @"\p{IsGreek}", "", "A character in the Greek Unicode block. .NET also offers other Is blocks, such as \\p{IsCyrillic}."),
        new("Classes", "[a-z-[aeiou]]", "[a-z-[aeiou]]", "", "Class subtraction: a to z except the vowels."),
        new("Quantifiers", "{n}", "{3}", "", "Exactly three times (change the number)."),
        new("Quantifiers", "{n,}", "{2,}", "", "Two or more times."),
        new("Quantifiers", "{n,m}", "{2,4}", "", "Two to four times."),
        new("Quantifiers", "{n,m}?", "{2,4}?", "", "Two to four times, lazy."),
        new("Groups", "(?<n> )", "(?<name>", ")", "Named capturing group around the selection."),
        new("Groups", "(?'n' )", "(?'name'", ")", "Named capturing group with the quote syntax."),
        new("Groups", "(?<n-m> )", "(?<push-pop>", ")", "Balancing group: pops group pop and pushes group push (for matching nested brackets)."),
        new("Groups", "(?(1)a|b)", "(?(1)", "|)", "Conditional: if group 1 matched use the first alternative, otherwise the second (uses the backtracking engine)."),
        new("Groups", "(?i: )", "(?i:", ")", "Inline option group: the options apply only inside the group."),
        new("Groups", "(?# )", "(?#", ")", "A comment inside the pattern; it is ignored."),
        new("References", @"\k<name>", @"\k<name>", "", "Backreference to a named group."),
        new("References", @"\1", @"\1", "", "Backreference to group 1."),
    ];

    /// <summary>Plain-language notes on syntax .NET does not have, shown beside the palette so the gap is explicit.</summary>
    public static readonly IReadOnlyList<(string Construct, string Reason)> Unavailable =
    [
        ("Possessive quantifiers (a*+)", ".NET has none; use an atomic group (?>a*) which behaves the same."),
        ("Recursion and subroutine calls ((?R), (?1))", ".NET does not support them; balancing groups can match nested pairs."),
        ("POSIX classes ([[:alpha:]])", ".NET has no POSIX classes; use \\p{L}, \\d or explicit ranges."),
        ("Branch reset groups ((?|...))", ".NET does not support them."),
        ("\\K (keep out), \\R, \\X", ".NET does not support them."),
        ("Character class intersection", ".NET only offers subtraction, [a-z-[aeiou]]."),
    ];

    /// <summary>\xHH, \uHHHH or a surrogate pair of \u escapes for a code point.</summary>
    public static string CodePoint(int codePoint)
    {
        if (codePoint is < 0 or > 0x10FFFF || (codePoint is >= 0xD800 and <= 0xDFFF)) throw new ArgumentException("Use a Unicode code point from 0 to 10FFFF that is not a surrogate.");
        if (codePoint <= 0xFF) return @"\x" + codePoint.ToString("X2", CultureInfo.InvariantCulture);
        if (codePoint <= 0xFFFF) return @"\u" + codePoint.ToString("X4", CultureInfo.InvariantCulture);
        var s = char.ConvertFromUtf32(codePoint);
        return @"\u" + ((int)s[0]).ToString("X4", CultureInfo.InvariantCulture) + @"\u" + ((int)s[1]).ToString("X4", CultureInfo.InvariantCulture);
    }

    /// <summary>Parses "U+00E9", "0xE9", "E9" or a single character.</summary>
    public static int ParseCodePoint(string text)
    {
        text = text.Trim();
        if (text.Length == 0) throw new ArgumentException("Enter a code point such as U+00E9 or a single character.");
        var hex = text.StartsWith("U+", StringComparison.OrdinalIgnoreCase) ? text[2..] : text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : null;
        if (hex != null || (text.Length > 1 && text.All(Uri.IsHexDigit)))
        {
            if (int.TryParse(hex ?? text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n)) return n;
            throw new ArgumentException("That is not a hexadecimal code point.");
        }
        var points = new List<int>();
        for (var i = 0; i < text.Length; i += char.IsSurrogatePair(text, i) ? 2 : 1) points.Add(char.ConvertToUtf32(text, i));
        return points.Count == 1 ? points[0] : throw new ArgumentException("Enter a single character or a code point such as U+00E9.");
    }

    /// <summary>\cA to \cZ for a control character.</summary>
    public static string ControlCharacter(char letter) => letter is >= 'A' and <= 'Z' or >= 'a' and <= 'z' ? @"\c" + char.ToUpperInvariant(letter) : throw new ArgumentException("Control characters use a letter A to Z.");

    /// <summary>[baseSet-[removed]] class subtraction. Both arguments are set bodies such as "a-z" and "aeiou".</summary>
    public static string Subtraction(string baseMembers, string removedMembers, bool negated = false)
    {
        if (string.IsNullOrEmpty(baseMembers) || string.IsNullOrEmpty(removedMembers)) throw new ArgumentException("Enter members for both the set and the part to remove.");
        var left = RegexBuilderLibrary.CharacterSet(Split(baseMembers), negated);
        var right = RegexBuilderLibrary.CharacterSet(Split(removedMembers), false);
        return left[..^1] + "-" + right + "]";
    }

    private static IEnumerable<string> Split(string members) => members.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>(?(condition)yes|no). The condition is a group number, a group name, or a lookaround expression such as (?=a).</summary>
    public static string Conditional(string condition, string yes, string? no = null)
    {
        condition = condition.Trim();
        if (condition.Length == 0) throw new ArgumentException("Give a group number, a group name or a lookaround as the condition.");
        if (yes.Length == 0) throw new ArgumentException("The 'then' part cannot be empty.");
        var test = int.TryParse(condition, out var n) && n > 0 ? n.ToString(CultureInfo.InvariantCulture)
            : RegexBuilderLibrary.IsValidGroupName(condition) ? condition
            : condition.StartsWith("(?", StringComparison.Ordinal) && condition.EndsWith(')') ? condition
            : throw new ArgumentException("The condition must be a group number, a group name or a lookaround such as (?=a).");
        return $"(?({test}){yes}" + (string.IsNullOrEmpty(no) ? "" : "|" + no) + ")";
    }

    /// <summary>Balancing group (?&lt;push-pop&gt;inner), or (?&lt;-pop&gt;inner) when <paramref name="push"/> is empty.</summary>
    public static string Balancing(string push, string pop, string inner)
    {
        if (!RegexBuilderLibrary.IsValidGroupName(pop)) throw new ArgumentException("The group to pop needs a valid name.");
        if (push.Length > 0 && !RegexBuilderLibrary.IsValidGroupName(push)) throw new ArgumentException("The group to push needs a valid name.");
        return $"(?<{push}-{pop}>{inner})";
    }

    /// <summary>Inline options such as (?i-s) or, with <paramref name="inner"/>, (?i-s:inner). Allowed letters: i m n s x.</summary>
    public static string InlineOptions(string on, string off, string? inner = null)
    {
        const string allowed = "imnsx";
        if (on.Concat(off).Any(c => !allowed.Contains(c)) || on.Intersect(off).Any()) throw new ArgumentException("Use the letters i, m, n, s and x, each at most once and not in both lists.");
        if (on.Length == 0 && off.Length == 0) throw new ArgumentException("Choose at least one option.");
        var body = on + (off.Length > 0 ? "-" + off : "");
        return inner == null ? $"(?{body})" : $"(?{body}:{inner})";
    }

    /// <summary>A (?#...) comment. A closing parenthesis cannot appear inside.</summary>
    public static string Comment(string text) => text.Contains(')') ? throw new ArgumentException("A comment cannot contain a closing parenthesis.") : "(?#" + text + ")";

    /// <summary>The atomic-group equivalent of the possessive quantifier atom+: (?&gt;atom+).</summary>
    public static string Possessive(string atom, string quantifier)
    {
        if (atom.Length == 0 || quantifier.Length == 0) throw new ArgumentException("Give the item and its quantifier.");
        return "(?>" + atom + quantifier + ")";
    }

    public static string Lookaround(bool ahead, bool negative, string inner) => (ahead ? "(?" : "(?<") + (negative ? "!" : "=") + inner + ")";

    /// <summary>Group count the engine assigns, for the conditional hint: numbered groups first, then named ones.</summary>
    public static IReadOnlyList<(int Number, string Name)> GroupsOf(string pattern, RegexBuilderFlags flags)
    {
        var c = RegexBuilderEngine.Compile(pattern, flags);
        if (!c.Valid) return [];
        return c.Regex!.GetGroupNumbers().Where(n => n != 0).Select(n => (n, c.Regex.GroupNameFromNumber(n))).ToList();
    }
}

/// <summary>The kinds of plain-language block understood by <see cref="RegexBlocks"/>.</summary>
public sealed record RegexBlocksResult(bool Valid, string Pattern, string Sentence, IReadOnlyList<string> Problems);

/// <summary>
/// Builds a pattern from plain-language lines, one building block per line. Examples:
/// "start of line", "literal v", "digits 1 to 3 times", "capture major: digits one or more", "one of cat|dog",
/// "any of a-f 0-9", "optional literal s", "end of line". Pure logic; literal text is always escaped.
/// </summary>
public static class RegexBlocks
{
    public const int MaxLines = 100;

    public static readonly IReadOnlyList<string> Help =
    [
        "start of line · end of line · start of text · end of text · word boundary",
        "literal TEXT · any of CHARS · none of CHARS (ranges like a-z are allowed)",
        "digit · letter · word character · space · anything · non-space",
        "one of A|B|C (alternatives, escaped)",
        "Prefix a line with 'optional ' or 'capture NAME: ' (or 'capture: ' for an unnamed group).",
        "End a line with a repeat: once · N times · N to M times · N or more · zero or more · one or more · optional · and add 'lazy' for as few as possible. A trailing repeat word is always read as a repeat, so literal text that ends in one needs the Text tab instead.",
    ];

    private static readonly Regex repeatTail = new(
        @"\s+(?<rep>once|optional|zero or more|one or more|(?<min>\d+)\s+to\s+(?<max>\d+)\s+times|(?<exact>\d+)\s+times?|(?<atleast>\d+)\s+or\s+more)(?<lazy>\s+lazy)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly Regex captureHead = new(@"^capture(?:\s+(?<name>[A-Za-z_]\w*))?\s*:\s*(?<rest>.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static RegexBlocksResult Build(string? text)
    {
        var problems = new List<string>();
        var pattern = new StringBuilder();
        var sentence = new List<string>();
        var number = 0;
        foreach (var raw in (text ?? "").Replace("\r", "").Split('\n'))
        {
            number++;
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (number > MaxLines) { problems.Add($"Only the first {MaxLines} lines are used."); break; }
            try
            {
                var (part, said) = Line(line);
                pattern.Append(part); sentence.Add(said);
            }
            catch (ArgumentException e) { problems.Add($"Line {number}: {e.Message}"); }
        }
        var result = pattern.ToString();
        if (problems.Count == 0 && result.Length > 0 && !RegexBuilderEngine.Validate(result, new RegexBuilderFlags()).Valid) problems.Add("The blocks produced a pattern the engine rejects.");
        return new(problems.Count == 0, problems.Count == 0 ? result : "", string.Join(", then ", sentence), problems);
    }

    private static (string Pattern, string Sentence) Line(string line)
    {
        var optional = false;
        string? captureName = null;
        var capture = false;
        var rest = line;
        if (rest.StartsWith("optional ", StringComparison.OrdinalIgnoreCase)) { optional = true; rest = rest[9..].TrimStart(); }
        var cap = captureHead.Match(rest);
        if (cap.Success) { capture = true; captureName = cap.Groups["name"].Success ? cap.Groups["name"].Value : null; rest = cap.Groups["rest"].Value.Trim(); }
        if (rest.StartsWith("optional ", StringComparison.OrdinalIgnoreCase)) { optional = true; rest = rest[9..].TrimStart(); }

        string? quant = null; string? said = null;
        var tail = repeatTail.Match(rest);
        if (tail.Success)
        {
            var lazy = tail.Groups["lazy"].Success;
            var rep = tail.Groups["rep"].Value.ToLowerInvariant();
            if (tail.Groups["min"].Success) { quant = RegexBuilderLibrary.Quantifier(int.Parse(tail.Groups["min"].Value), int.Parse(tail.Groups["max"].Value), lazy); said = $"{tail.Groups["min"].Value} to {tail.Groups["max"].Value} times"; }
            else if (tail.Groups["exact"].Success) { var n = int.Parse(tail.Groups["exact"].Value); quant = RegexBuilderLibrary.Quantifier(n, n, lazy); said = $"{n} time{(n == 1 ? "" : "s")}"; }
            else if (tail.Groups["atleast"].Success) { var n = int.Parse(tail.Groups["atleast"].Value); quant = RegexBuilderLibrary.Quantifier(n, null, lazy); said = $"{n} or more times"; }
            else if (rep == "zero or more") { quant = RegexBuilderLibrary.Quantifier(0, null, lazy); said = "zero or more times"; }
            else if (rep == "one or more") { quant = RegexBuilderLibrary.Quantifier(1, null, lazy); said = "one or more times"; }
            else if (rep == "optional") { quant = RegexBuilderLibrary.Quantifier(0, 1, lazy); said = "optionally"; }
            rest = rest[..tail.Index].TrimEnd();
        }
        if (optional && quant != null) throw new ArgumentException("Use either 'optional' or a repeat on one line, not both.");

        var (atom, name, atomic) = Item(rest);
        var part = atom;
        if (quant != null) part = (atomic ? atom : Wrap(atom)) + quant;
        else if (optional) part = (atomic ? atom : Wrap(atom)) + "?";
        if (capture) part = captureName != null ? RegexBuilderLibrary.NamedGroup(captureName, part) : "(" + part + ")";
        var sentence = (capture ? (captureName != null ? $"capture '{captureName}': " : "capture: ") : "") + (optional ? "optionally " : "") + name + (said != null ? " " + said : "");
        return (part, sentence);
    }

    private static string Wrap(string atom) => atom.Length > 0 && IsSingleAtom(atom) ? atom : "(?:" + atom + ")";

    private static bool IsSingleAtom(string atom)
    {
        if (atom.Length == 1) return true;
        if (atom.Length == 2 && atom[0] == '\\') return true;
        if (atom[0] == '[' && atom.IndexOf(']', 1) == atom.Length - 1) return true;
        return atom.StartsWith(@"\p{", StringComparison.Ordinal) && atom.EndsWith('}') && atom.IndexOf('}') == atom.Length - 1;
    }

    private static (string Atom, string Sentence, bool Atomic) Item(string text)
    {
        var lower = text.ToLowerInvariant();
        switch (lower)
        {
            case "start of line": return ("^", "start of line", true);
            case "end of line": return ("$", "end of line", true);
            case "start of text": return (@"\A", "start of text", true);
            case "end of text": return (@"\z", "end of text", true);
            case "word boundary": return (@"\b", "a word boundary", true);
            case "digit" or "digits": return (@"\d", "a digit", false);
            case "letter" or "letters": return (@"\p{L}", "a letter", false);
            case "word character" or "word characters": return (@"\w", "a word character", false);
            case "space" or "spaces" or "white space": return (@"\s", "white space", false);
            case "non-space" or "non space": return (@"\S", "a character that is not white space", false);
            case "anything": return (".", "any character", false);
        }
        if (lower.StartsWith("literal ", StringComparison.Ordinal))
        {
            var value = text[8..];
            if (value.Length == 0) throw new ArgumentException("'literal' needs the text to match.");
            return (RegexBuilderLibrary.Literal(value), $"the text \"{value}\"", false);
        }
        if (lower.StartsWith("any of ", StringComparison.Ordinal) || lower.StartsWith("none of ", StringComparison.Ordinal))
        {
            var negated = lower.StartsWith("none", StringComparison.Ordinal);
            var members = text[(negated ? 8 : 7)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return (RegexBuilderLibrary.CharacterSet(members, negated), (negated ? "none of " : "any of ") + string.Join(" ", members), true);
        }
        if (lower.StartsWith("one of ", StringComparison.Ordinal))
        {
            var options = text[7..].Split('|').Select(o => o.Trim()).Where(o => o.Length > 0).ToList();
            return (RegexBuilderLibrary.Alternation(options), "one of " + string.Join(", ", options), true);
        }
        throw new ArgumentException($"I do not know '{text}'. See the list of blocks above.");
    }
}
