using System.Text;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>How an engine treats a construct.</summary>
public enum FlavorSupport { Yes, Different, No }

/// <summary>One engine in the comparison.</summary>
public sealed record RegexFlavor(string Id, string Name, string Version);

/// <summary>One construct with a probe (a small pattern and a text it matches) and the support of every flavor.</summary>
public sealed record RegexConstruct(string Id, string Label, string Probe, string ProbeSubject, IReadOnlyDictionary<string, (FlavorSupport Support, string Note)> Flavors);

/// <summary>The constructs a pattern uses, with where each first appears.</summary>
public sealed record RegexConstructUse(string ConstructId, int Position);

/// <summary>Per-flavor verdict for one pattern.</summary>
public sealed record RegexFlavorVerdict(RegexFlavor Flavor, int Unsupported, int Different, IReadOnlyList<(RegexConstruct Construct, FlavorSupport Support, string Note)> Findings)
{
    public bool Compatible => Unsupported == 0 && Different == 0;
}

/// <summary>
/// Flavor comparison. A hand-written table of constructs against six engines, and a scanner that finds which constructs a pattern uses.
/// The table was written from each engine's documentation and each probe was run against the real engine during development; the
/// application does not run any engine except .NET, so the comparison states what the table says and never that a pattern was tested
/// elsewhere. A construct that the pattern does not use is never listed.
/// </summary>
public static class RegexFlavors
{
    public static readonly IReadOnlyList<RegexFlavor> Flavors =
    [
        new("net", ".NET", ".NET 10"),
        new("pcre2", "PCRE2", "PCRE2 10.x (PHP, grep -P, rg -P)"),
        new("js", "JavaScript", "ECMAScript, current browsers and Node"),
        new("python", "Python re", "Python 3.14"),
        new("go", "Go RE2", "Go regexp 1.22 and later"),
        new("rust", "Rust regex", "the default engine of ripgrep"),
        new("ere", "grep -E", "GNU grep extended expressions"),
    ];

    private static (FlavorSupport, string) Cell(string code)
    {
        var support = code[0] switch { 'Y' => FlavorSupport.Yes, 'D' => FlavorSupport.Different, 'N' => FlavorSupport.No, _ => throw new ArgumentException("Bad support code " + code) };
        return (support, code.Length > 2 ? code[2..] : "");
    }

    /// <summary>The Rust regex (ripgrep) column, kept apart because it was added after the other six. Same codes as the rows below.</summary>
    private static readonly IReadOnlyDictionary<string, string> RustColumn = new Dictionary<string, string>
    {
        ["digit"] = "Y matches any Unicode decimal digit", ["word-space"] = "Y", ["word-boundary"] = "Y", ["lazy"] = "Y",
        ["possessive"] = "D accepted, but read as a repeated repeat, not possessive", ["atomic"] = "N", ["lookahead"] = "N", ["lookbehind"] = "N", ["lookbehind-variable"] = "N",
        ["named-angle"] = "Y", ["named-p"] = "Y", ["noncapturing"] = "Y", ["backref"] = "N", ["named-backref"] = "N", ["inline-flags"] = "Y", ["conditional"] = "N",
        ["balancing"] = "N", ["recursion"] = "N", ["class-subtraction"] = "D a nested set is a union, so the vowels match too; Rust writes subtraction as [a-z--[aeiou]]",
        ["unicode-category"] = "Y", ["unicode-block"] = "Y reads it as the Greek script", ["anchor-A"] = "Y", ["anchor-z"] = "Y", ["anchor-Z"] = "N", ["anchor-G"] = "N",
        ["hex-escape"] = "Y", ["unicode-escape"] = "Y", ["quote-literal"] = "N", ["posix-class"] = "Y", ["comment"] = "N", ["backslash-R"] = "N", ["branch-reset"] = "N",
        ["flag-x"] = "Y", ["flag-s"] = "Y", ["flag-m"] = "Y", ["flag-n"] = "N", ["flag-rtl"] = "N",
    };

    private static RegexConstruct R(string id, string label, string probe, string subject, string net, string pcre2, string js, string python, string go, string ere) =>
        new(id, label, probe, subject, new Dictionary<string, (FlavorSupport, string)>
        {
            ["net"] = Cell(net), ["pcre2"] = Cell(pcre2), ["js"] = Cell(js), ["python"] = Cell(python), ["go"] = Cell(go), ["rust"] = Cell(RustColumn[id]), ["ere"] = Cell(ere),
        });

    // Columns: .NET, PCRE2, JavaScript, Python, Go, grep -E. Y = same meaning, D = accepted with a different meaning or a condition, N = not supported.
    public static readonly IReadOnlyList<RegexConstruct> Constructs =
    [
        R("digit", @"\d digit class", @"\d+", "a12", "Y matches any Unicode decimal digit", "D ASCII digits only unless Unicode properties are on", "D ASCII digits only", "Y", "D ASCII digits only", "N use [0-9]"),
        R("word-space", @"\w \W \s \S classes", @"\w+\s\w+", "ab cd", "Y", "D \\w is ASCII only unless Unicode properties are on", "D \\w is ASCII only", "Y", "D \\w is ASCII only", "D GNU extension, not POSIX"),
        R("word-boundary", @"\b \B boundaries", @"\bab\b", "x ab y", "Y", "D word characters are ASCII only unless Unicode properties are on", "D word characters are ASCII only", "Y", "D word characters are ASCII only", "D GNU extension, not POSIX"),
        R("lazy", "Lazy quantifier (*? +? ??)", "a.*?b", "axxb", "Y", "Y", "Y", "Y", "Y", "D accepted, but it means an optional repeat, not lazy"),
        R("possessive", "Possessive quantifier (*+ ++ ?+)", "a++b", "aab", "N", "Y", "N", "Y Python 3.11 and later", "N", "D accepted, but read as a repeated repeat, not possessive"),
        R("atomic", "Atomic group (?>...)", "(?>a+)b", "aab", "Y", "Y", "N", "Y Python 3.11 and later", "N", "N"),
        R("lookahead", "Lookahead (?=...) (?!...)", "a(?=b)", "ab", "Y", "Y", "Y", "Y", "N", "N"),
        R("lookbehind", "Lookbehind (?<=...) (?<!...)", "(?<=a)b", "ab", "Y", "Y", "Y", "Y", "N", "N"),
        R("lookbehind-variable", "Variable-length lookbehind", "(?<=a+)b", "aab", "Y", "N the length must be fixed or bounded", "Y", "N the length must be fixed", "N", "N"),
        R("named-angle", "Named group (?<name>...)", "(?<n>a)b", "ab", "Y", "Y", "Y", "N Python writes (?P<name>...)", "Y", "N"),
        R("named-p", "Named group (?P<name>...)", "(?P<n>a)b", "ab", "N", "Y", "N", "Y", "Y", "N"),
        R("noncapturing", "Non-capturing group (?:...)", "(?:a)b", "ab", "Y", "Y", "Y", "Y", "Y", "N"),
        R("backref", @"Numbered backreference \1", @"(a)\1", "aa", "Y", "Y", "Y", "Y", "N", "D GNU extension, not POSIX"),
        R("named-backref", @"Named backreference \k<name>", @"(?<n>a)\k<n>", "aa", "Y", "Y", "Y", "N Python writes (?P=name)", "N", "N"),
        R("inline-flags", "Inline options (?i) (?i:...)", "(?i)a", "A", "Y", "Y", "N", "Y the global form must start the pattern", "Y", "N"),
        R("conditional", "Conditional (?(1)yes|no)", "(a)?(?(1)b|c)", "ab", "Y", "Y", "N", "Y", "N", "N"),
        R("balancing", "Balancing group (?<a-b>...)", "(?<o>a)(?<-o>b)", "ab", "Y", "N", "N", "N", "N", "N"),
        R("recursion", "Recursion (?R) (?1)", @"^(\((?:a|(?1))*\))$", "(a(a))", "N", "Y", "N", "N", "N", "N"),
        R("class-subtraction", "Class subtraction [a-z-[aeiou]]", "^[a-z-[aeiou]]$", "b", "Y", "N", "N", "N", "N", "N"),
        R("unicode-category", @"Unicode category \p{L}", @"\p{L}+", "é", "Y", "Y", "Y needs the u flag", "N the re module has no \\p", "Y", "N"),
        R("unicode-block", @"Unicode block \p{IsGreek}", @"\p{IsGreek}", "α", "Y", "N PCRE2 names scripts, for example \\p{Greek}", "N", "N", "N", "N"),
        R("anchor-A", @"\A start of text", @"\Aab", "ab", "Y", "Y", "N", "Y", "Y", "N"),
        R("anchor-z", @"\z end of text", @"ab\z", "ab", "Y", "Y", "N", "Y Python 3.14 and later", "Y", "N"),
        R("anchor-Z", @"\Z end, or before a final newline", @"ab\Z", "ab", "Y", "Y", "N", "D means the very end, like \\z", "N", "N"),
        R("anchor-G", @"\G end of previous match", @"\Ga", "a", "Y", "Y", "N", "N", "N", "N"),
        R("hex-escape", @"\xHH escape", @"\x41", "A", "Y", "Y", "Y", "Y", "Y", "N"),
        R("unicode-escape", @"\uHHHH escape", @"\u00" + "41", "A", "Y", "N PCRE2 writes \\x{41}", "Y", "Y", "N Go writes \\x{41}", "N"),
        R("quote-literal", @"\Q...\E literal span", @"\Qa.b\E", "a.b", "N", "Y", "N", "N", "Y", "N"),
        R("posix-class", "POSIX class [[:alpha:]]", "[[:alpha:]]+", "ab", "N compiles, but as a different set", "Y", "N", "N", "Y", "Y"),
        R("comment", "Comment (?#...)", "a(?#note)b", "ab", "Y", "Y", "N", "Y", "N", "N"),
        R("backslash-R", @"\R line break, \K reset, \X cluster", @"a\Rb", "a\nb", "N", "Y", "N", "N", "N", "N"),
        R("branch-reset", "Branch reset (?|...)", @"(?|(a)|(b))(c)\2", "acc", "N", "Y", "N", "N", "N", "N"),
        R("flag-x", "Option x: ignore white space and # comments", "(?x)a b", "ab", "Y", "Y", "N", "Y", "N", "N"),
        R("flag-s", "Option s: the dot matches a newline", @"(?s)a.b", "a\nb", "Y", "Y", "Y as the s flag, not inline", "Y", "Y", "N grep matches one line at a time"),
        R("flag-m", "Option m: ^ and $ at every line", @"(?m)^b", "a\nb", "Y", "Y", "Y as the m flag, not inline", "Y", "Y", "D grep is already line by line"),
        R("flag-n", "Option n: only named groups capture", "(?n)(?<x>a)b", "ab", "Y", "Y", "N", "N", "N", "N"),
        R("flag-rtl", "Right-to-left matching", "", "", "Y", "N", "N", "N", "N", "N"),
    ];

    public static RegexConstruct Construct(string id) => Constructs.First(c => c.Id == id);

    // ----- scanner -----

    private static readonly Regex quantifierBraces = new(@"\G\{\d+(?:,\d*)?\}", RegexOptions.None, TimeSpan.FromMilliseconds(100));

    /// <summary>The constructs a pattern uses, in order of first appearance, from a scan that understands escapes and sets. Never throws.</summary>
    public static IReadOnlyList<RegexConstructUse> Detect(string? pattern, RegexBuilderFlags flags)
    {
        var p = pattern ?? "";
        var found = new List<RegexConstructUse>();
        var seen = new HashSet<string>();
        void Add(string id, int at) { if (seen.Add(id)) found.Add(new(id, at)); }
        if (flags.IgnorePatternWhitespace) Add("flag-x", 0);
        if (flags.Singleline) Add("flag-s", 0);
        if (flags.Multiline) Add("flag-m", 0);
        if (flags.ExplicitCapture) Add("flag-n", 0);
        if (flags.RightToLeft) Add("flag-rtl", 0);

        var quantifiable = false;
        var i = 0;
        while (i < p.Length)
        {
            var c = p[i];
            if (c == '\\') { i = Escape(p, i, Add); quantifiable = true; continue; }
            if (flags.IgnorePatternWhitespace && c == '#') { while (i < p.Length && p[i] != '\n') i++; continue; }
            if (c == '[') { i = Set(p, i, Add); quantifiable = true; continue; }
            if (c == '(') { i = Group(p, i, Add); quantifiable = false; continue; }
            if (c == ')') { quantifiable = true; i++; continue; }
            if (c is '|' or '^' or '$') { quantifiable = false; i++; continue; }
            var quantifier = 0;
            if (quantifiable && c is '*' or '+' or '?') quantifier = 1;
            else if (quantifiable && c == '{' && quantifierBraces.Match(p, i) is { Success: true } braces) quantifier = braces.Length;
            if (quantifier > 0)
            {
                i += quantifier;
                if (i < p.Length && p[i] == '?') { Add("lazy", i); i++; }
                else if (i < p.Length && p[i] == '+') { Add("possessive", i); i++; }
                quantifiable = false;
                continue;
            }
            quantifiable = true; i++;
        }
        return found;
    }

    private static int Escape(string p, int i, Action<string, int> add)
    {
        if (i + 1 >= p.Length) return p.Length;
        var e = p[i + 1];
        switch (e)
        {
            case 'd' or 'D': add("digit", i); break;
            case 'w' or 'W' or 's' or 'S': add("word-space", i); break;
            case 'b' or 'B': add("word-boundary", i); break;
            case 'A': add("anchor-A", i); break;
            case 'z': add("anchor-z", i); break;
            case 'Z': add("anchor-Z", i); break;
            case 'G': add("anchor-G", i); break;
            case 'x': add("hex-escape", i); break;
            case 'u': add("unicode-escape", i); break;
            case 'Q': add("quote-literal", i); break;
            case 'R' or 'K' or 'X': add("backslash-R", i); break;
            case 'k' when i + 2 < p.Length && p[i + 2] is '<' or '\'' or '{': add("named-backref", i); break;
            case >= '1' and <= '9': add("backref", i); break;
            case 'p' or 'P' when i + 2 < p.Length && p[i + 2] == '{':
                {
                    var close = p.IndexOf('}', i + 3);
                    var name = close > 0 ? p[(i + 3)..close] : "";
                    add(name.StartsWith("Is", StringComparison.Ordinal) ? "unicode-block" : "unicode-category", i);
                    break;
                }
        }
        return i + 2;
    }

    private static int Set(string p, int open, Action<string, int> add)
    {
        var i = open + 1;
        if (i < p.Length && p[i] == '^') i++;
        if (i < p.Length && p[i] == ']') i++;
        while (i < p.Length)
        {
            var c = p[i];
            if (c == '\\') { i = Escape(p, i, add); continue; }
            if (c == '[' && i + 1 < p.Length && p[i + 1] == ':') { var end = p.IndexOf(":]", i + 2, StringComparison.Ordinal); if (end > 0) { add("posix-class", i); i = end + 2; continue; } }
            if (c == '-' && i + 1 < p.Length && p[i + 1] == '[') { add("class-subtraction", i); }
            if (c == ']') return i + 1;
            i++;
        }
        return p.Length;
    }

    private static int Group(string p, int i, Action<string, int> add)
    {
        if (i + 1 >= p.Length || p[i + 1] != '?') return i + 1;
        if (i + 2 >= p.Length) return p.Length;
        var d = p[i + 2];
        switch (d)
        {
            case '#': add("comment", i); var close = p.IndexOf(')', i + 3); return close < 0 ? p.Length : close + 1;
            case ':': add("noncapturing", i); return i + 3;
            case '=' or '!': add("lookahead", i); return i + 3;
            case '>': add("atomic", i); return i + 3;
            case '(': add("conditional", i); return i + 3;
            case '|': add("branch-reset", i); return i + 3;
            case 'R': add("recursion", i); return i + 3;
            case >= '0' and <= '9': add("recursion", i); return i + 3;
            case '\'': add("named-angle", i); return i + 3;
            case 'P':
                if (i + 3 < p.Length && p[i + 3] == '<') add("named-p", i);
                else if (i + 3 < p.Length && p[i + 3] == '=') add("named-backref", i);
                else if (i + 3 < p.Length && p[i + 3] == '>') add("recursion", i);
                return i + 3;
            case '<':
                if (i + 3 < p.Length && p[i + 3] is '=' or '!') { add("lookbehind", i); return i + 4; }
                {
                    var end = p.IndexOf('>', i + 3);
                    var name = end > 0 ? p[(i + 3)..end] : "";
                    add(name.Contains('-') ? "balancing" : "named-angle", i);
                }
                return i + 3;
            default:
                if (char.IsAsciiLetter(d) || d == '-') { add("inline-flags", i); return i + 2; }
                return i + 2;
        }
    }

    // ----- comparison -----

    /// <summary>Compares the constructs a pattern uses across the chosen flavors (all of them when none are named).</summary>
    public static IReadOnlyList<RegexFlavorVerdict> Compare(string? pattern, RegexBuilderFlags flags, IEnumerable<string>? flavorIds = null)
    {
        var uses = Detect(pattern, flags);
        var chosen = (flavorIds == null ? Flavors : Flavors.Where(f => flavorIds.Contains(f.Id))).ToList();
        var verdicts = new List<RegexFlavorVerdict>();
        foreach (var f in chosen)
        {
            var findings = new List<(RegexConstruct, FlavorSupport, string)>();
            foreach (var u in uses)
            {
                var construct = Construct(u.ConstructId);
                var (support, note) = construct.Flavors[f.Id];
                findings.Add((construct, support, note));
            }
            verdicts.Add(new(f, findings.Count(x => x.Item2 == FlavorSupport.No), findings.Count(x => x.Item2 == FlavorSupport.Different), findings));
        }
        return verdicts;
    }

    /// <summary>A plain table as text: one row per construct used, one column per flavor.</summary>
    public static string ToText(string? pattern, RegexBuilderFlags flags, IEnumerable<string>? flavorIds = null)
    {
        var verdicts = Compare(pattern, flags, flavorIds);
        var sb = new StringBuilder();
        sb.AppendLine("Pattern: " + pattern);
        sb.AppendLine("Static comparison from a hand-written table; the pattern was not run in any engine except .NET.");
        if (verdicts.Count == 0 || verdicts[0].Findings.Count == 0) { sb.AppendLine("The pattern uses no construct the table tracks, so every flavor reads it the same way."); return sb.ToString(); }
        sb.AppendLine();
        foreach (var v in verdicts)
        {
            sb.AppendLine($"{v.Flavor.Name} ({v.Flavor.Version}): " + (v.Compatible ? "every tracked construct is supported" : $"{v.Unsupported} unsupported, {v.Different} different"));
            foreach (var (construct, support, note) in v.Findings.Where(f => f.Support != FlavorSupport.Yes))
                sb.AppendLine($"  {(support == FlavorSupport.No ? "NO " : "DIFF")} {construct.Label}" + (note.Length > 0 ? ": " + note : ""));
        }
        return sb.ToString();
    }
}
