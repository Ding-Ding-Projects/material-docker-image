using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>
/// A help page for one kind of token: what it is, how it is written, how .NET treats it, and a worked example. The example pattern is run against
/// the example text by the checks, so a page cannot describe a pattern that fails to match its own sample.
/// </summary>
public sealed record RegexHelpPage(string Key, string Title, string Syntax, string Explanation, string ExamplePattern, string ExampleText, string ExampleMatch, string NetNote);

/// <summary>Per-token help: the explanation tree gives each token a one-line description; this adds a full page for the token under the caret or the selected row.</summary>
public static class RegexTokenHelp
{
    private static RegexHelpPage P(string key, string title, string syntax, string explanation, string pattern, string text, string match, string note = "") => new(key, title, syntax, explanation, pattern, text, match, note);

    public static readonly IReadOnlyList<RegexHelpPage> Pages =
    [
        P("literal", "Literal text", "abc", "Ordinary characters match themselves. A character that has a special meaning must be escaped with a backslash to be read literally.", "cat", "the cat sat", "cat", "Case is respected unless IgnoreCase is on."),
        P("class.shorthand", "Shorthand class", @"\d  \w  \s  \D  \W  \S", @"\d is a digit, \w a word character, \s white space; the capital letter is the opposite. They stand for one character.", @"\d\w", "x 7a", "7a", @".NET reads \d and \w as Unicode, so \d also matches digits of other scripts. RegexOptions.ECMAScript would make them ASCII but cannot be combined with most other options."),
        P("class.escape", "Escaped character", @"\n  \t  \.  \xHH  \uHHHH", @"A backslash before a symbol makes it literal; before a letter it can name a control character, a hexadecimal code or a class.", @"A\.", "A.", "A.", ""),
        P("class.set", "Character set", "[abc]  [^abc]  [a-z]  [a-z-[aeiou]]", "Matches one character from the list. A leading ^ negates the set, a hyphen between two characters is a range, and a set can subtract another set.", "[a-c]+", "xxabcabx", "abcab", "Inside a set most symbols are literal; only \\, ], ^ at the start and - between characters need care."),
        P("any", "Any character", ".", "Matches any one character except a newline. With Singleline on it matches the newline too.", "a.c", "abc a\nc", "abc", ""),
        P("unicode", "Unicode category", @"\p{L}  \P{Lu}  \p{IsGreek}", @"\p{...} matches a character of a Unicode general category or block, \P{...} everything else.", @"\p{Lu}\p{Ll}+", "see Paris", "Paris", ".NET has categories and blocks but no script properties."),
        P("anchor.line", "Line or text anchor", "^  $  \\A  \\z  \\Z", "^ and $ match at the start and end of the text, or of every line when Multiline is on. \\A and \\z always mean the whole text.", "^go$", "go", "go", "$ also matches just before a final newline; \\z does not."),
        P("anchor.boundary", "Word boundary", @"\b  \B  \G", @"\b is the position between a word character and a non-word character, \B any other position, \G the end of the previous match.", @"\bcat\b", "a cat sat", "cat", "A word boundary is a position, so it has no width."),
        P("group.capturing", "Capturing group", "(...)", "Groups items together and saves what they matched as a numbered group, which a backreference or a replacement can use.", "(ab)+", "xababx", "abab", "Groups are numbered by their opening parenthesis, left to right. Named groups are numbered after the unnamed ones."),
        P("group.named", "Named group", "(?<name>...)  (?'name'...)", "A capturing group with a name. Use it as \\k<name> in the pattern and ${name} in a replacement.", "(?<year>\\d{4})", "in 2026", "2026", "A name starts with a letter or underscore."),
        P("group.noncapturing", "Non-capturing group", "(?:...)", "Groups items for a quantifier or an alternation without saving the match, which keeps group numbers stable.", "(?:ab)+", "ababab", "ababab", ""),
        P("group.atomic", "Atomic group", "(?>...)", "Once the group has matched, the engine never backtracks into it. This prevents catastrophic backtracking and stands in for the possessive quantifiers that .NET lacks.", "(?>a+)b", "aaab", "aaab", "Uses the backtracking engine."),
        P("group.options", "Inline options", "(?i)  (?i-s:...)", "Switches options on or off from here, or only inside the group when a colon and a part follow. The letters are i, m, n, s and x.", "(?i:a)b", "Ab", "Ab", ""),
        P("group.balancing", "Balancing group", "(?<push-pop>...)", "Pops one capture of the pop group and pushes the text between onto the push group. This is how .NET matches nested pairs.", "^(?:(?<o>\\()|(?<-o>\\)))*$", "(()())", "(()())", "Uses the backtracking engine."),
        P("lookahead", "Lookahead", "(?=...)  (?!...)", "Checks what follows without consuming it: positive (?=) requires a match, negative (?!) forbids one.", @"\w+(?=!)", "stop! go", "stop", "Uses the backtracking engine."),
        P("lookbehind", "Lookbehind", "(?<=...)  (?<!...)", "Checks what precedes the position. .NET allows a lookbehind of variable length.", @"(?<=\$)\d+", "cost $42", "42", "Uses the backtracking engine."),
        P("alternation", "Alternation", "a|b|c", "Matches the first alternative that fits, tried from left to right. Put the longer alternative first when one is the start of another.", "cat|dog", "a dog", "dog", ""),
        P("quantifier.greedy", "Greedy quantifier", "?  *  +  {n}  {n,}  {n,m}", "Repeats the previous item. A greedy quantifier takes as many repetitions as it can and gives some back if the rest of the pattern needs them.", "a+", "caaat", "aaa", ""),
        P("quantifier.lazy", "Lazy quantifier", "??  *?  +?  {n,m}?", "The same repetition counts, but as few repetitions as possible are taken first.", "a+?", "caaat", "a", ""),
        P("backreference", "Backreference", @"\1  \k<name>", "Matches the same text that an earlier group matched, not the same pattern.", @"(\w)\1", "hello", "ll", "A reference to a group that does not exist is an error."),
        P("conditional", "Conditional", "(?(1)yes|no)  (?(?=x)yes|no)", "Chooses between two alternatives depending on whether a group has captured, or on a lookaround.", "(a)?(?(1)b|c)", "ab", "ab", "Uses the backtracking engine."),
        P("comment", "Comment", "(?#...)  # ... (with IgnorePatternWhitespace)", "Text the engine ignores. After the x option, white space is ignored too and a # starts a comment up to the end of the line.", "a(?#note)b", "ab", "ab", ""),
        P("branch", "Alternative", "one branch of a|b", "One of the choices of an alternation. A branch may be empty, which makes the whole alternation optional.", "a(b|)c", "ac", "ac", ""),
        P("error", "Invalid token", "", "The framework reports a pattern as invalid when it is not well-formed; this row marks the place where the explanation could not continue. The message beside the pattern says what is wrong.", "a", "a", "a", ""),
    ];

    public static RegexHelpPage? Page(string key) => Pages.FirstOrDefault(p => p.Key == key);

    /// <summary>The help page for a token, chosen by its family and, for groups, classes and anchors, by how it is written.</summary>
    public static RegexHelpPage For(RegexExplainNode node)
    {
        var text = node.Text;
        string key = node.Family switch
        {
            RegexTokenFamily.Literal => "literal",
            RegexTokenFamily.Any => "any",
            RegexTokenFamily.UnicodeCategory => "unicode",
            RegexTokenFamily.Alternation => "alternation",
            RegexTokenFamily.Branch => "branch",
            RegexTokenFamily.Backreference => "backreference",
            RegexTokenFamily.Comment => "comment",
            RegexTokenFamily.Conditional => "conditional",
            RegexTokenFamily.InlineOptions => "group.options",
            RegexTokenFamily.Error => "error",
            RegexTokenFamily.Anchor => text is "^" or "$" or @"\A" or @"\z" or @"\Z" ? "anchor.line" : "anchor.boundary",
            RegexTokenFamily.Lookaround => text.StartsWith("(?<", StringComparison.Ordinal) ? "lookbehind" : "lookahead",
            RegexTokenFamily.Quantifier => node.Children.Count > 0 && node.Children[0].Length < text.Length && text[node.Children[0].Length..] is { Length: > 1 } suffix && suffix.EndsWith('?') ? "quantifier.lazy" : "quantifier.greedy",
            RegexTokenFamily.CharacterClass => text.StartsWith('[') ? "class.set" : text.Length == 2 && "dDwWsS".Contains(text[1]) ? "class.shorthand" : "class.escape",
            RegexTokenFamily.Group => text.StartsWith("(?>", StringComparison.Ordinal) ? "group.atomic"
                : text.StartsWith("(?:", StringComparison.Ordinal) ? "group.noncapturing"
                : (text.StartsWith("(?<", StringComparison.Ordinal) || text.StartsWith("(?'", StringComparison.Ordinal)) ? (text.IndexOf('-') is var dash and > 0 && dash < NameEnd(text) ? "group.balancing" : "group.named")
                : text.StartsWith("(?", StringComparison.Ordinal) ? "group.options" : "group.capturing",
            _ => "literal",
        };
        return Page(key) ?? Pages[0];
    }

    private static int NameEnd(string text)
    {
        var close = text.IndexOfAny(['>', '\''], 3);
        return close < 0 ? text.Length : close;
    }

    /// <summary>The full help text for a token: title, how it is written, what it does, an example and the .NET note.</summary>
    public static string Describe(RegexExplainNode node)
    {
        var page = For(node);
        var lines = new List<string> { page.Title };
        if (page.Syntax.Length > 0) lines.Add("Written as: " + page.Syntax);
        lines.Add(page.Explanation);
        lines.Add($"Example: {page.ExamplePattern} finds {page.ExampleMatch} in {page.ExampleText.Replace("\n", "\\n")}");
        if (page.NetNote.Length > 0) lines.Add(page.NetNote);
        return string.Join("\n", lines);
    }

    /// <summary>Whether the page's own example holds on .NET, used by the checks.</summary>
    public static bool ExampleHolds(RegexHelpPage page)
    {
        try
        {
            var m = Regex.Match(page.ExampleText, page.ExamplePattern, RegexOptions.None, TimeSpan.FromMilliseconds(500));
            return m.Success && m.Value == page.ExampleMatch;
        }
        catch (Exception e) when (e is ArgumentException or RegexMatchTimeoutException) { return false; }
    }
}
