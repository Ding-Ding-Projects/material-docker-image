using System.Text;
using System.Text.RegularExpressions;
using MaterialDockerImage.UI;

/// <summary>
/// Checks for the work that turned partial parity rows into implemented ones: negated POSIX classes, the Unicode block table, style tips, per-token help, the Perl, Ruby and
/// PowerShell script conversions, scope-by-scope testing, the hex view, files as subjects, pattern comparison, growth estimation and descriptions in saved patterns. All of it is
/// framework-free logic; none of it proves the popover's layout or interaction.
/// </summary>
public static class RegexPartialChecks
{
    static int count;
    static readonly RegexBuilderFlags Ci = new();
    static readonly RegexBuilderFlags Cs = new() { IgnoreCase = false };

    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        count++;
        Console.WriteLine("PASS " + name);
    }

    static string Match(string pattern, string text) => RegexBuilderEngine.Evaluate(pattern, Cs, text).Matches.FirstOrDefault()?.Value ?? "<none>";

    public static int Run()
    {
        count = 0;
        PosixNegated();
        UnicodeBlocks();
        Tips();
        Help();
        Scripting();
        Scopes();
        Hex();
        Files();
        Compare();
        Growth();
        Metadata();
        Localization();
        return count;
    }

    // ----- negated POSIX classes -----

    static void PosixNegated()
    {
        var a = RegexPosixTranslator.Translate("[[:^digit:]]+");
        Check(a.Success && a.Replaced == 1 && a.Pattern == "(?:[^0-9])+" && Match(a.Pattern, "12ab34") == "ab", "a negated class alone becomes a group that matches the complement");
        var b = RegexPosixTranslator.Translate("[x[:^digit:]]+");
        Check(b.Success && Match(b.Pattern, "12x-34") == "x-" && Match("^" + b.Pattern + "$", "5") == "<none>", "a negated class beside members is a union of the two");
        var c = RegexPosixTranslator.Translate("^[^[:^digit:]]+$");
        Check(c.Success && Match(c.Pattern, "2026") == "2026" && Match(c.Pattern, "20a6") == "<none>", "a negated set holding a negated class keeps only the class members");
        var d = RegexPosixTranslator.Translate("^[^x[:^digit:]]+$");
        Check(d.Success && Match(d.Pattern, "123") == "123" && Match(d.Pattern, "1x3") == "<none>" && Match(d.Pattern, "1a3") == "<none>", "a negated set with members and a negated class excludes both");
        var e = RegexPosixTranslator.Translate("[[:^alpha:][:^digit:]]");
        Check(e.Success && e.Replaced == 2 && Match("^" + e.Pattern + "$", "a") == "a" && Match("^" + e.Pattern + "$", "5") == "5", "two negated classes in one set are both applied");
        Check(!RegexPosixTranslator.Translate("[[:^bogus:]]").Success && RegexPosixTranslator.Translate("[[:^bogus:]]").Error!.Contains("bogus"), "an unknown negated class is still refused with its name");
        foreach (var name in RegexPosixTranslator.Classes.Keys)
        {
            var t = RegexPosixTranslator.Translate("[[:^" + name + ":]]");
            Check(t.Success && RegexBuilderEngine.Validate(t.Pattern, Cs).Valid, "negated POSIX class " + name + " converts to a valid pattern");
        }
        var complementOfAlpha = RegexPosixTranslator.Translate("[[:^alpha:]]").Pattern;
        Check(Match(complementOfAlpha, "ab1") == "1" && Match(complementOfAlpha, "ï!") == "!", "the complement of alpha is everything that is not a Unicode letter");
    }

    // ----- Unicode blocks -----

    static void UnicodeBlocks()
    {
        Check(RegexUnicodeBlocks.All.Count == 108 && RegexUnicodeBlocks.All.Select(b => b.Name).Distinct().Count() == 108, "the block table holds 108 distinct names");
        foreach (var block in RegexUnicodeBlocks.All)
        {
            var re = new Regex(block.Positive, RegexOptions.None, TimeSpan.FromSeconds(1));
            var inside = re.IsMatch(((char)block.Start).ToString()) && re.IsMatch(((char)block.End).ToString());
            var outside = (block.Start == 0 || !re.IsMatch(((char)(block.Start - 1)).ToString())) && (block.End == 0xFFFF || !re.IsMatch(((char)(block.End + 1)).ToString()));
            if (!inside || !outside) throw new Exception("block range does not match the engine: " + block.Name);
        }
        count++; Console.WriteLine("PASS every block pattern matches its first and last character and not the characters beside it");
        Check(RegexUnicodeBlocks.All.Single(b => b.Name == "Cyrillic") is { Start: 0x400, End: 0x4FF } && RegexUnicodeBlocks.All.Single(b => b.Name == "Hiragana") is { Start: 0x3040, End: 0x309F } && RegexUnicodeBlocks.All.Single(b => b.Name == "BasicLatin") is { Start: 0, End: 0x7F }, "known ranges agree with the Unicode standard");
        Check(RegexUnicodeBlocks.Search("greek").Count == 3 && RegexUnicodeBlocks.Search("U+0400").Any(b => b.Name == "Cyrillic") && RegexUnicodeBlocks.Search("").Count == 108 && RegexUnicodeBlocks.Search("zzzz").Count == 0, "searching blocks by name or range");
        Check(RegexUnicodeBlocks.For(0x3042)?.Name == "Hiragana" && RegexUnicodeBlocks.For(0x1F600) == null && RegexUnicodeBlocks.For('a')?.Name == "BasicLatin", "the block of a character, and none outside the Basic Multilingual Plane");
        Check(RegexUnicodeBlocks.Pattern("cyrillic", true) == @"\P{IsCyrillic}" && Match(RegexUnicodeBlocks.Pattern("Cyrillic", false) + "+", "abc жук def") == "жук", "the pattern text, positive and negated, matches Cyrillic text");
        Check(RegexUnicodeBlocks.All.Single(b => b.Name == "GreekandCoptic").Title == "Greek and Coptic" && RegexUnicodeBlocks.All.Single(b => b.Name == "BasicLatin").Title == "Basic Latin" && RegexUnicodeBlocks.All.Single(b => b.Name == "CJKUnifiedIdeographs").Title == "CJK Unified Ideographs", "display titles are readable");
        bool refused = false;
        try { RegexUnicodeBlocks.Pattern("Klingon", false); } catch (ArgumentException) { refused = true; }
        Check(refused && RegexUnicodeBlocks.ScriptNotice.Contains("script"), "an unknown block is refused, and the script limitation is stated");
        Check(!RegexBuilderEngine.Validate(@"\p{Greek}", Cs).Valid, @".NET really has no \p{Greek} script property (the reason scripts are not offered)");
    }

    // ----- style tips -----

    static IReadOnlyList<string> TipCodes(string pattern, RegexBuilderFlags? flags = null) => RegexTips.Analyze(pattern, flags ?? Cs).Select(t => t.Code).ToList();

    static void Tips()
    {
        Check(TipCodes(".*foo").Contains("tip-leading-wildcard") && !TipCodes("^.*foo").Contains("tip-leading-wildcard") && TipCodes("foo.*").Contains("tip-trailing-wildcard"), "leading and trailing wildcards are flagged");
        Check(TipCodes("example.com").Contains("tip-unescaped-dot") && !TipCodes(@"example\.com").Contains("tip-unescaped-dot") && !TipCodes("a.+b").Contains("tip-unescaped-dot"), "an unescaped dot between letters is flagged, an escaped or repeated one is not");
        Check(TipCodes("x(a|)y").Contains("tip-empty-alternative") && TipCodes("(a|b|)").Contains("tip-empty-alternative"), "an empty alternative is flagged");
        Check(TipCodes("(a|b|c)").Contains("tip-single-char-alternation") && !TipCodes("(ab|c)").Contains("tip-single-char-alternation") && !TipCodes(@"(a|\||c)").Contains("tip-single-char-alternation"), "an alternation of single characters should be a set");
        Check(TipCodes("(cat|dog|cat)").Contains("tip-duplicate-alternative"), "a duplicate alternative is flagged");
        Check(TipCodes("(?:a)").Contains("tip-redundant-group") && !TipCodes("(?:a)+").Contains("tip-redundant-group") && !TipCodes("(?:ab)").Contains("tip-redundant-group"), "a group around a single item is redundant unless a quantifier follows it");
        Check(TipCodes("[A-Za-z]+", Ci).Contains("tip-case-set") && !TipCodes("[A-Za-z]+", Cs).Contains("tip-case-set"), "both letter cases in a set matter only without IgnoreCase");
        Check(TipCodes("(a)(b)(c)").Contains("tip-many-captures") && !TipCodes("(a)(b)(c)\\1").Contains("tip-many-captures") && !TipCodes("(a)(b)(c)", Cs with { ExplicitCapture = true }).Contains("tip-many-captures") && !TipCodes("(a)(b)").Contains("tip-many-captures"), "unused capturing groups are mentioned from three upward");
        Check(TipCodes(@"^\d{3}-\d{4}$").Count == 0 && TipCodes("").Count == 0 && TipCodes("(").Count == 0, "a clean pattern, an empty pattern and an invalid pattern give no tips");
        Check(RegexTips.Analyze("example.com", Cs).Single(t => t.Code == "tip-unescaped-dot") is { Start: 7, Length: 1 }, "a tip names the span it is about");
        Check(RegexDiagnostics.Analyze("example.com", Cs).Count == 0, "tips stay separate from the backtracking findings");
    }

    // ----- per-token help -----

    static void Help()
    {
        Check(RegexTokenHelp.Pages.Select(p => p.Key).Distinct().Count() == RegexTokenHelp.Pages.Count && RegexTokenHelp.Pages.Count >= 24, "the help pages have distinct keys");
        foreach (var page in RegexTokenHelp.Pages)
            Check(RegexTokenHelp.ExampleHolds(page) && page.Explanation.Length > 30, "help page example holds on .NET: " + page.Key);
        string Key(string pattern, int index = 0) => RegexTokenHelp.For(RegexExplainer.Flatten(RegexExplainer.Explain(pattern, Cs)).ElementAt(index).Node).Key;
        Check(Key("(a)") == "group.capturing" && Key("(?:a)") == "group.noncapturing" && Key("(?<y>a)") == "group.named" && Key("(?'y'a)") == "group.named" && Key("(?<a-b>x)") == "group.balancing" && Key("(?>a)") == "group.atomic" && Key("(?i:a)") == "group.options" && Key("(?i)a") == "group.options", "groups map to their own pages");
        Check(Key("a+") == "quantifier.greedy" && Key("a+?") == "quantifier.lazy" && Key("a??") == "quantifier.lazy" && Key("a{2,3}?") == "quantifier.lazy" && Key("a{2}") == "quantifier.greedy" && Key("a?") == "quantifier.greedy", "greedy and lazy quantifiers map to their own pages");
        Check(Key("^") == "anchor.line" && Key(@"\A") == "anchor.line" && Key(@"\b") == "anchor.boundary" && Key(@"\G") == "anchor.boundary" && Key("[a-z]") == "class.set" && Key(@"\d") == "class.shorthand" && Key(@"\.") != "class.shorthand" && Key(".") == "any" && Key(@"\p{L}") == "unicode", "anchors, classes and categories map to their own pages");
        Check(Key("(?=a)") == "lookahead" && Key("(?!a)") == "lookahead" && Key("(?<=a)") == "lookbehind" && Key("(?<!a)") == "lookbehind" && Key("a|b") == "alternation" && Key("a|b", 1) == "branch" && Key(@"(a)\1", 2) == "backreference" && Key("(a)?(?(1)b|c)", 3) == "conditional" && Key("a(?#x)") == "literal", "lookarounds, alternation, references and conditionals map to their own pages");
        foreach (var rich in new[] { @"^(?<year>\d{4})-(?:0[1-9]|1[0-2])\b.*?(?=\s|$)", @"(?i)(a)\1[^\W\d_]+(?<!x)\p{Lu}{2,3}?", "(a)?(?(1)b|c)(?<o>\\()(?<-o>\\))" })
            foreach (var (node, _) in RegexExplainer.Flatten(RegexExplainer.Explain(rich, Cs)))
                if (RegexTokenHelp.For(node).Title.Length == 0) throw new Exception("token without help in " + rich);
        count++; Console.WriteLine("PASS every token of three rich patterns has a help page");
        var described = RegexTokenHelp.Describe(RegexExplainer.Explain("a+?", Cs)[0]);
        Check(described.Contains("Lazy quantifier") && described.Contains("Written as:") && described.Contains("Example:"), "the full help text names the page, the syntax and an example");
    }

    // ----- conversions -----

    static void Scripting()
    {
        Check(RegexConverter.Targets.Count == 17 && RegexConverter.Targets.Contains("Perl") && RegexConverter.Targets.Contains("Ruby") && RegexConverter.Targets.Contains("PowerShell script"), "Perl, Ruby and PowerShell script are offered as targets");
        Check(RegexConverter.SingleQuoted(@"it's \d") == @"'it\'s \\d'" && RegexConverter.SingleQuoted("") == "''", "single-quoted strings escape only the backslash and the quote");

        var perl = RegexConverter.Convert("Perl", @"\d+(?=x)", Ci with { Multiline = true }, RegexCodeKind.IsMatch);
        Check(perl.Converted && perl.Text == "my $pattern = '\\\\d+(?=x)';\nmy $re = qr/$pattern/im;\nmy $found = $input =~ $re;", "Perl keeps the pattern in a single-quoted string and passes the flags to qr");
        var perlAll = RegexConverter.Convert("Perl", "a$b@c", Cs with { Singleline = true, IgnorePatternWhitespace = true, ExplicitCapture = true }, RegexCodeKind.Matches);
        Check(perlAll.Text.Contains("qr/$pattern/sxn;") && perlAll.Text.Contains("'a$b@c'") && perlAll.Text.Contains("$-[0]") && perlAll.Notes.Any(n => n.Contains("5.22")), "Perl flags s x n, interpolation characters stay inside the quoted string, and the n flag version is noted");
        var perlReplace = RegexConverter.Convert("Perl", "a", Cs, RegexCodeKind.Replace, "x/y@z\\n");
        Check(perlReplace.Text.EndsWith("s/$re/x\\/y\\@z\\\\n/g;") && perlReplace.Notes.Any(n => n.Contains("$+{name}")), "a Perl replacement escapes the slash, the at sign and the backslash");
        Check(RegexConverter.Convert("Perl", "x", Cs, RegexCodeKind.Split).Text.EndsWith("my @pieces = split $re, $input;") && RegexConverter.Convert("Perl", "x", Cs, RegexCodeKind.Split).Notes.Any(n => n.Contains("trailing empty")), "Perl split is noted for dropping trailing empty pieces");
        Check(!RegexConverter.Convert("Perl", "(?<a-b>x)", Ci).Converted && !RegexConverter.Convert("Perl", "x", Ci with { RightToLeft = true }).Converted, "Perl refuses balancing groups and right-to-left matching");

        var ruby = RegexConverter.Convert("Ruby", @"a\d", Ci with { Singleline = true, IgnorePatternWhitespace = true }, RegexCodeKind.Replace, @"[\1]");
        Check(ruby.Converted && ruby.Text == "re = Regexp.new('a\\\\d', Regexp::IGNORECASE | Regexp::MULTILINE | Regexp::EXTENDED)\noutput = input.gsub(re, '[\\\\1]')", "Ruby maps IgnoreCase, Singleline and IgnorePatternWhitespace to the Regexp options");
        Check(ruby.Notes.Any(n => n.Contains("^ and $ always match")) && RegexConverter.Convert("Ruby", "a", Cs, RegexCodeKind.Matches).Text.Contains("input.scan(re)") && !RegexConverter.Convert("Ruby", "(?<a-b>x)", Ci).Converted && !RegexConverter.Convert("Ruby", "x", Ci with { RightToLeft = true }).Converted, "Ruby notes its line anchors, scans for matches, and refuses balancing groups and right-to-left matching");

        var script = RegexConverter.Convert("PowerShell script", "it's", Ci with { Multiline = true }, RegexCodeKind.Matches);
        Check(script.Converted && script.Text.StartsWith("[CmdletBinding()]\nparam(".Replace("\n", Environment.NewLine)) && script.Text.Contains("[regex]::new('it''s', 'IgnoreCase, Multiline', [TimeSpan]::FromMilliseconds(250))") && script.Text.Contains("Get-Content -LiteralPath $Path -Raw") && script.Text.Contains("RegexMatchTimeoutException") && script.Text.Contains("foreach ($match in $regex.Matches($text))"), "the PowerShell script has a parameter, a timeout, the quoted pattern and options, a file read and a timeout handler");
        Check(RegexConverter.Convert("PowerShell script", "a", Cs, RegexCodeKind.Replace, "x'y").Text.Contains("$regex.Replace($text, 'x''y')") && RegexConverter.Convert("PowerShell script", "a", Cs, RegexCodeKind.Split).Text.Contains("$regex.Split($text)") && RegexConverter.Convert("PowerShell script", "a", Cs, RegexCodeKind.IsMatch).Text.Contains("$regex.IsMatch($text)"), "the PowerShell script follows the chosen kind of code");
    }

    // ----- scopes, hex, files -----

    static void Scopes()
    {
        var sample = "ab\ncd\nab ab\n";
        Check(RegexSubject.Split(sample, RegexScope.Line).Select(p => p.Text).SequenceEqual(["ab", "cd", "ab ab"]) && RegexSubject.Split("a\r\nb\rc", RegexScope.Line).Select(p => p.Text).SequenceEqual(["a", "b", "c"]) && RegexSubject.Split("", RegexScope.Line).Count == 1 && RegexSubject.Split("a\n\nb", RegexScope.Line).Count == 3, "lines are split at LF, CRLF and CR without a trailing empty line");
        Check(RegexSubject.Split("p1\fp2\f", RegexScope.Page).Select(p => p.Text).SequenceEqual(["p1", "p2"]) && RegexSubject.Split(sample, RegexScope.Whole).Single().Text == sample, "pages are split at a form feed, the whole scope is one piece");
        var whole = RegexSubject.RunScopes("^ab$", Cs, sample, RegexScope.Whole);
        var line = RegexSubject.RunScopes("^ab$", Cs, sample, RegexScope.Line);
        Check(whole.Valid && whole.TotalMatches == 0 && line.TotalMatches == 1 && line.ScopeCount == 3 && line.Segments[0].Matches[0].Index == 0, "an anchored pattern matches line by line but not in the whole text");
        var every = RegexSubject.RunScopes("ab", Cs, sample, RegexScope.Line);
        Check(every.TotalMatches == 3 && every.ScopesWithMatches == 2 && every.Segments[2].Matches.Select(m => m.Index).SequenceEqual([6, 9]), "match offsets are positions in the whole subject");
        Check(RegexSubject.RunScopes(@"b\nc", Cs, "ab\ncd", RegexScope.Whole).TotalMatches == 1 && RegexSubject.RunScopes(@"b\nc", Cs, "ab\ncd", RegexScope.Line).TotalMatches == 0, "a match never crosses a scope boundary");
        var page = RegexSubject.RunScopes("^p2", Cs with { Multiline = false }, "p1 ab\fp2 ab ab", RegexScope.Page);
        Check(page.ScopeCount == 2 && page.TotalMatches == 1 && page.Segments[1].Start == 6, "page scope anchors at each page");
        var report = RegexSubject.Report(every);
        Check(report.Contains("Line 1: 1 match: ab at 0") && report.Contains("3 matches in 2 of 3 lines.") && RegexSubject.Report(whole).Contains("0 matches in 0 of 1 text."), "the report lists lines with matches and the totals");
        Check(!RegexSubject.RunScopes("(", Cs, "x", RegexScope.Line).Valid && RegexSubject.RunScopes("a", Cs, "", RegexScope.Line).ScopeCount == 1, "an invalid pattern is reported and an empty subject is one empty line");
        var many = RegexSubject.RunScopes("a", Cs, string.Concat(Enumerable.Repeat("a\n", 6000)), RegexScope.Line);
        Check(many.ScopeCount == 6000 && many.Segments.Count <= RegexSubject.MaxScopes && many.Truncated && RegexSubject.Report(many).Contains("cut at the bounds"), "scopes and matches are bounded and the cut is reported");
        Check(RegexSubject.RunScopes("a", Cs, "a\na", RegexScope.Line, new CancellationToken(true)).Truncated, "a cancelled run reports that it was cut");
    }

    static void Hex()
    {
        var utf8 = RegexSubject.Hex("Aé€", "UTF-8");
        Check(utf8.ByteCount == 6 && utf8.Text.StartsWith("00000000  41 C3 A9 E2 82 AC") && utf8.Text.EndsWith("A....."), "the UTF-8 hex view shows offsets, bytes and a text column");
        Check(RegexSubject.Hex("Aé€", "UTF-16 LE").Text.Contains("41 00 E9 00 AC 20") && RegexSubject.Hex("Aé€", "UTF-16 BE").Text.Contains("00 41 00 E9 20 AC"), "UTF-16 little and big endian differ in byte order");
        Check(RegexSubject.Hex("é€", "Latin-1").Text.Contains("E9 3F") && RegexSubject.Hex("é", "ASCII").Text.Contains("3F") && RegexSubject.Hex("", "UTF-8").Text == "", "characters an encoding cannot hold become a question mark, and an empty subject has an empty view");
        var big = RegexSubject.Hex(new string('x', 5000), "UTF-8");
        Check(big.Truncated && big.ByteCount == 5000 && big.Text.Split('\n').Length == RegexSubject.MaxHexBytes / 16, "the hex view is bounded and says so");
        var endings = RegexSubject.CountLineEndings("a\r\nb\nc\rd\r\n");
        Check(endings is { Crlf: 2, Lf: 1, Cr: 1 } && endings.Mixed && endings.Summary.Contains("mixed") && !RegexSubject.CountLineEndings("a\nb").Mixed && RegexSubject.CountLineEndings("abc").Summary == "No line breaks.", "line endings are counted and mixed endings are reported");
        Check(RegexSubject.NormalizeLineEndings("a\r\nb\nc\rd", "CRLF") == "a\r\nb\r\nc\r\nd" && RegexSubject.NormalizeLineEndings("a\r\nb\nc\rd", "LF") == "a\nb\nc\nd" && RegexSubject.NormalizeLineEndings("a\nb", "CR") == "a\rb" && !RegexSubject.CountLineEndings(RegexSubject.NormalizeLineEndings("a\r\nb\nc\rd", "LF")).Mixed, "line endings are rewritten to one kind");
        bool refused = false;
        try { RegexSubject.NormalizeLineEndings("a", "NEL"); } catch (ArgumentException) { refused = true; }
        Check(refused, "an unknown line ending is refused");
    }

    static void Files()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mdi-subject-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string P(string name) => Path.Combine(dir, name);
            File.WriteAllBytes(P("utf8.txt"), new UTF8Encoding(false).GetBytes("héllo\nwörld\n"));
            var u = RegexSubject.LoadFile(P("utf8.txt"));
            Check(u.Success && u.Encoding == "UTF-8" && u.Text == "héllo\nwörld\n" && !u.Truncated, "a UTF-8 file is detected and read");
            File.WriteAllBytes(P("bom.txt"), [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("bom")]);
            Check(RegexSubject.LoadFile(P("bom.txt")) is { Success: true, Text: "bom", Encoding: "UTF-8" }, "a UTF-8 byte order mark is skipped");
            File.WriteAllBytes(P("latin.txt"), [0x68, 0xE9, 0x6C, 0x6C, 0x6F]);
            Check(RegexSubject.LoadFile(P("latin.txt")) is { Success: true, Text: "héllo", Encoding: "Latin-1" }, "bytes that are not valid UTF-8 are read as Latin-1");
            File.WriteAllBytes(P("le.txt"), [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("wide")]);
            File.WriteAllBytes(P("be.txt"), [0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes("wide")]);
            Check(RegexSubject.LoadFile(P("le.txt")) is { Success: true, Text: "wide", Encoding: "UTF-16 LE" } && RegexSubject.LoadFile(P("be.txt")) is { Success: true, Text: "wide", Encoding: "UTF-16 BE" }, "UTF-16 files are recognised by their byte order mark");
            File.WriteAllBytes(P("bin.dat"), [0x61, 0x00, 0x62, 0x00, 0x01]);
            var bin = RegexSubject.LoadFile(P("bin.dat"));
            Check(!bin.Success && bin.Error!.Contains("binary") && RegexSubject.LoadFile(P("bin.dat"), "Latin-1").Success, "a file with zero bytes is refused as binary unless an encoding is chosen");
            Check(!RegexSubject.LoadFile(P("missing.txt")).Success && !RegexSubject.LoadFile("").Success && !RegexSubject.LoadFile(dir).Success, "a missing file, no name and a folder are reported");
            File.WriteAllText(P("big.txt"), new string('a', 100_000));
            var big = RegexSubject.LoadFile(P("big.txt"));
            Check(big.Success && big.Truncated && big.Text.Length == RegexBuilderEngine.MaxSampleBytes && big.FileBytes == 100_000, "a large file is cut at the sample limit and says so");
            File.WriteAllText(P("euro.txt"), new string('€', 40_000));
            var euro = RegexSubject.LoadFile(P("euro.txt"));
            Check(euro.Success && euro.Encoding == "UTF-8" && euro.Truncated && euro.Text.Length == RegexBuilderEngine.MaxSampleBytes / 3 && euro.Text.All(c => c == '€'), "a cut inside a multi-byte character does not turn a UTF-8 file into Latin-1");

            Check(RegexSubject.SaveFile(P("out.txt"), "a\nb", "UTF-16 LE", "CRLF") == null && File.ReadAllBytes(P("out.txt")) is [0xFF, 0xFE, ..] && RegexSubject.LoadFile(P("out.txt")) is { Success: true, Text: "a\r\nb", Encoding: "UTF-16 LE" } && !File.Exists(P("out.txt.tmp")), "a saved UTF-16 subject carries a byte order mark and reads back with its line endings");
            Check(RegexSubject.SaveFile(P("out.txt"), "é", "UTF-8", "LF") == null && File.ReadAllBytes(P("out.txt")).SequenceEqual(new byte[] { 0xC3, 0xA9 }), "saving replaces an existing file");
            Check(RegexSubject.SaveFile(dir, "x", "UTF-8", "LF") is { Length: > 0 } && RegexSubject.SaveFile("", "x", "UTF-8", "LF") is { Length: > 0 }, "a save that cannot be written reports why");
        }
        finally { Directory.Delete(dir, true); }
    }

    // ----- comparison and growth -----

    static void Compare()
    {
        var suite = "+ a1\n+ b22\n- abc\n- xyz";
        var r = RegexCompare.Run([@"\d+", "[0-9]+", "x"], Cs, suite);
        Check(r.Valid && r.Rows.Count == 3 && r.Rows[0].AllCorrect && r.Rows[1].AllCorrect && !r.Rows[2].AllCorrect, "patterns are checked against the expected answers");
        Check(r.Rows[0].MatchingPassed == 2 && r.Rows[0].MatchingTotal == 2 && r.Rows[0].NonMatchingPassed == 2 && r.Rows[0].NonMatchingTotal == 2 && r.Rows[2].MatchingPassed == 0 && r.Rows[2].NonMatchingPassed == 1, "the matching and non-matching subjects are counted separately");
        var wrongOnNon = RegexCompare.Run([@"\d+", "q"], Cs, "+ a1\n- 42x");
        Check(wrongOnNon.Rows[0].MatchingPassed == 1 && wrongOnNon.Rows[0].NonMatchingPassed == 0 && !wrongOnNon.Rows[0].AllCorrect, "a pattern that matches a subject it must not match is wrong");
        var good = RegexCompare.Run([@"\d+", "[0-9]+", "x"], Cs, "+ a1\n+ b22\n- abc\n- xyz");
        Check(good.Rows[0].AllCorrect && good.Rows[1].AllCorrect && !good.Rows[2].AllCorrect && good.Verdict.StartsWith("Fastest of the 2 patterns") && good.Verdict.Contains("1 other pattern is wrong") && good.Rows[0].TotalMs >= 0 && good.Rows[0].Engine.Length > 0, "the fastest right pattern is named, and a wrong one is called out");
        Check(!RegexCompare.Run([@"\d+"], Cs, suite).Valid && !RegexCompare.Run(["a", "b"], Cs, "").Valid, "one pattern or no subjects cannot be compared");
        var withBad = RegexCompare.Run(["(", "a"], Cs, "+ a");
        Check(withBad.Valid && !withBad.Rows[0].Valid && withBad.Rows[0].Error!.Length > 0 && withBad.Rows[1].AllCorrect, "an invalid pattern is reported in its row and the others still run");
        Check(RegexCompare.Run(["x", "y"], Cs, "+ a", new CancellationToken(true)).Cancelled && RegexCompare.ParsePatterns("a\r\n\r\nb\nc").SequenceEqual(["a", "b", "c"]) && RegexCompare.ParsePatterns(string.Join("\n", Enumerable.Range(0, 20).Select(i => "p" + i))).Count == RegexCompare.MaxPatterns, "a cancelled comparison says so, and the pattern list is parsed and bounded");
        Check(RegexCompare.Run(["x", "y"], Cs, "+ q\n- r").Verdict.StartsWith("No pattern gave the expected answer"), "when every pattern is wrong no pattern is called fastest");
    }

    static void Growth()
    {
        RegexBenchmarkRow R(string s, int size, double ms, bool timedOut = false) => new(s, size, ms, timedOut, true);
        var linear = RegexGrowth.Describe([R("sample x1", 100, 1), R("sample x2", 200, 2), R("sample x4", 400, 4), R("sample x16", 1600, 16)], "sample");
        Check(linear.Contains("about linear") && linear.Contains("exponent about 1.0"), "doubling the input doubles the time: linear");
        var quadratic = RegexGrowth.Describe([R("a-run 16", 16, 1), R("a-run 32", 32, 4), R("a-run 64", 64, 16)], "a-run");
        Check(quadratic.Contains("about quadratic") && quadratic.Contains("exponent about 2.0"), "doubling the input quadruples the time: quadratic");
        Check(RegexGrowth.Describe([R("sample x1", 100, 0.01), R("sample x16", 1600, 0.2)], "sample").Contains("too fast") && RegexGrowth.Describe([R("sample x1", 100, 1)], "sample").Contains("Not enough") && RegexGrowth.Describe([R("sample x1", 100, 1), R("sample x16", 1600, 400, true)], "sample").Contains("Not enough"), "times below a millisecond, a single probe and timed-out probes give no estimate");
        var real = RegexDiagnostics.Benchmark(@"\d+", Cs, "abc 123 def 4567", default);
        Check(real.Valid && RegexGrowth.Describe(real.Rows, "sample").Length > 0, "the estimate runs on the real probe output");
    }

    // ----- saved pattern descriptions -----

    static void Metadata()
    {
        var entry = new RegexSavedPattern("Ports", @"\b\d{2,5}\b", Ci, DateTimeOffset.UnixEpoch) { Description = "Finds port numbers in a log line", Replacement = "$0", TestCases = "+ 8080\n- 1" };
        var library = RegexPatternStore.Add([], entry);
        Check(RegexPatternStore.Search(library, "log line").Count == 1 && RegexPatternStore.Search(library, "ports").Count == 1 && RegexPatternStore.Search(library, "zzz").Count == 0, "saved patterns are found by their description as well as name and pattern");
        var path = Path.Combine(Path.GetTempPath(), "mdi-meta-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            RegexPatternStore.Save(path, library);
            var back = RegexPatternStore.Load(path).Single();
            Check(back.Description == entry.Description && back.Replacement == "$0" && back.TestCases == "+ 8080\n- 1", "description, replacement and test cases survive a save and load");
        }
        finally { File.Delete(path); }
        bool refused = false;
        try { RegexPatternStore.Add([], entry with { Description = new string('d', RegexSavedPattern.MaxDescriptionLength + 1) }); } catch (ArgumentException) { refused = true; }
        Check(refused, "a description over the limit is refused");
    }

    // ----- localization -----

    static void Localization()
    {
        var language = MaterialDockerImage.Services.Loc.Language;
        var school = MaterialDockerImage.Services.Loc.SchoolMode;
        try
        {
            MaterialDockerImage.Services.Loc.SchoolMode = false; MaterialDockerImage.Services.Loc.Language = "Cantonese";
            var samples = new[]
            {
                "Select a row of the explanation to read the full help for that token.", "Unicode block", "Filter the blocks", "Not in this block", "Insert block", "Subject tools", "Scope", "Encoding", "Line ending",
                "Whole text", "Each line", "Each page", "Test by scope", "Show hex view", "Convert line endings", "Load file…", "Save sample…", "Compare patterns", "Add the current pattern", "Compare", "Comparing…",
                "Description", "Description (optional)", "Pattern comparison", "Hex view of the sample", "No block matches the filter.", "Enter a pattern to test.",
                "The same pattern as C# or PowerShell code, a JSON string, a command line for grep, ripgrep or Select-String (using the folder and file patterns of Search in files), or code for JavaScript, Python, Java, Go, PHP, Perl or Ruby, or a complete PowerShell script. Anything the target cannot express is reported, never guessed.",
            };
            Check(samples.All(x => MaterialDockerImage.Services.Loc.T(x) != x && MaterialDockerImage.Services.Loc.T(x).Any(c => c > 0x2E80)), "the new builder labels, captions and messages have Cantonese wording");
            Check(!MaterialDockerImage.Services.Loc.T("Select a row of the explanation to read the full help for that token.").StartsWith("Select"), "the token help hint is shown in Cantonese");
        }
        finally { MaterialDockerImage.Services.Loc.Language = language; MaterialDockerImage.Services.Loc.SchoolMode = school; }
    }
}
