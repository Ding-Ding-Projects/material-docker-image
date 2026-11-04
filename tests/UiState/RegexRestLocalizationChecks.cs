using System.Text.RegularExpressions;
using MaterialDockerImage.UI;
using Loc = MaterialDockerImage.Services.Loc;

/// <summary>
/// Checks the generated wording that still stayed English after the last localization wave: the reasons and notes of the code and command-line conversions, the descriptions
/// of the built-in snippets and palettes, the rows of the pattern comparison table and the rows of the timing table. Every sentence is built with the real code and shown
/// through the same display path the builder uses, so a sentence added later without a Cantonese translation turns a check red. Error text that the .NET framework itself
/// writes (for an invalid pattern) is not translated: it is shown as the framework wrote it, after a translated lead-in, and these checks only demand that lead-in.
/// </summary>
public static class RegexRestLocalizationChecks
{
    static readonly RegexBuilderFlags Cs = new() { IgnoreCase = false };

    static bool HasCjk(string s) => s.Any(c => c >= 0x2E80);

    /// <summary>A run of five plain English words, or two different English function words, is a sentence that was not translated; product names and short technical terms do not form one.</summary>
    static bool EnglishSentence(string s) =>
        Regex.IsMatch(s, @"(?<![\w.\\])[A-Za-z]+ [a-z]+ [a-z]+ [a-z]+ [a-z]+(?![\w])") ||
        Regex.Matches(s, @"(?<![\w\\$-])(is|are|not|the|of|to|by|and|or|has|have|with|for|use|uses|so|which|it|that|in|as|only|an|be|can|if|then|from|at|on|than)(?![\w-])").Select(m => m.Value).Distinct().Count() >= 2;

    static bool Done(string english, string shown) => shown != english && HasCjk(shown) && !EnglishSentence(shown);

    public static void Run(Action<bool, string> checkIn)
    {
        var listing = Environment.GetEnvironmentVariable("REGEX_LOC_LIST") == "1";
        Action<bool, string> Check = (value, name) => checkIn(value || listing, name);
        var language = Loc.Language; var school = Loc.SchoolMode;
        try
        {
            Loc.SchoolMode = false; Loc.Language = "Cantonese";
            void Covered(IEnumerable<string> english, Func<string, string> show, string name)
            {
                var missing = english.Where(e => e.Length > 0).Distinct().Where(e => !Done(e, show(e))).ToList();
                if (listing) foreach (var m in missing) Console.WriteLine("MISSING[" + name + "] " + m);
                Check(missing.Count == 0, name + (missing.Count == 0 ? "" : $" ({missing.Count} without Cantonese, first: {missing[0]})"));
            }

            // ----- why Search in files, a replacement or an undo skipped a file -----
            Covered(RegexGrep.FixedSkipReasons, r => RegexGrep.TranslateSkipReason(r, Loc.T), "every fixed folder-search skip reason has Cantonese wording");
            Covered(RegexGrep.NumberedSkipTemplates, Loc.T, "every numbered folder-search skip reason has Cantonese wording");
            Check(RegexGrep.TranslateSkipReason("larger than 512 KiB", Loc.T) == "大過 512 KiB", "a numbered skip reason keeps its number when translated");
            Check(RegexGrep.TranslateSkipReason("could not be restored: Access is denied.", Loc.T) == "還原唔到： Access is denied.", "the system's own restore error is kept after the translated lead-in");
            Check(RegexGrep.TranslateSkipReason("larger than 512 KiB", s => s) == "larger than 512 KiB", "skip reasons stay unchanged in English");

            // ----- conversion reasons and notes: every target, every kind, patterns and flags that provoke every kind of note -----
            var patterns = new[]
            {
                @"a", @"\d+", @"\w\s\D\W\S", @"\bx\B", @"\Aa\z", @"a\Z", @"\x41", @"\t", @"\u0041", @"\cA", @"\p{L}", @"\P{L}", @"\k<n>", @"\G", @"\e", @"\Q",
                @"(?<n>a)\k<n>", @"(a)\1", @"(?:a)", @"(?=a)", @"(?!a)", @"(?<=a)", @"(?<!a)", @"(?>a)", @"(?i)a", @"(?(1)a|b)", @"(?#note)a", @"a+?", @"a*?", @"a??", @"a{2,3}?", @"a++", @"a*+",
                @"[\d]", @"[\w-]", @"[\s]", @"[\x41]", @"[\p{L}]", @"[a-z-[aeiou]]", @"[[:alpha:]]", @"[a", @"a\", @"x{", @"a{,3}", @"(?'n'a)", @"(?<a-b>x)", @"\p{IsGreek}", @"[^\W\d_]",
                "a\nb", "it's", @"$1 ${n}", @"(?<=\d)(?=(?:\d{3})+$)", @"^\s*#.*$", @"(a|b)*?c",
            };
            var flagSets = new[]
            {
                new RegexBuilderFlags(), new() { IgnoreCase = false }, new() { Multiline = true }, new() { Singleline = true }, new() { RightToLeft = true }, new() { IgnorePatternWhitespace = true },
                new() { ExplicitCapture = true }, new() { CultureInvariant = true }, new() { NonBacktracking = true }, new() { IgnoreCase = false, Multiline = true, Singleline = true, ExplicitCapture = true, CultureInvariant = true },
            };
            var notes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var target in RegexConverter.Targets)
                foreach (var kind in Enum.GetValues<RegexCodeKind>())
                    foreach (var flags in flagSets)
                        foreach (var pattern in patterns)
                        {
                            RegexConversion result;
                            try { result = RegexConverter.Convert(target, pattern, flags, kind, "$1-${n}\\1", pattern.Length % 2 == 0 ? @"C:\logs" : null, pattern.Length % 3 == 0 ? "*.log;!bin" : null); }
                            catch (ArgumentException) { continue; }
                            foreach (var n in result.Notes) notes.Add(n);
                        }
            Check(notes.Count >= 60, "the sample patterns provoke many different conversion notes and reasons");
            Covered(notes, n => RegexConverter.TranslateNote(n, Loc.T), "every conversion reason and note (grep, ripgrep, Select-String and the code targets) has Cantonese wording");
            Check(notes.All(n => RegexConverter.TranslateNote(n, s => s) == n), "a conversion note shown without the language lookup is exactly the English note");
            Covered(RegexConverter.NotePieces, Loc.T, "the lead-ins and tails that wrap the comparison sentences of a conversion refusal have Cantonese wording");
            var refusal = RegexConverter.Convert("grep -E", @"(?=a)", Cs).Notes[0];
            var shown = RegexConverter.TranslateNote(refusal, Loc.T);
            Check(HasCjk(shown) && !shown.StartsWith("Not convertible") && refusal.StartsWith("Not convertible"), "a grep refusal reads in Cantonese in the builder while the conversion itself keeps its English reason");

            // Rendering with a user template adds a note of its own.
            var custom = new Dictionary<string, string> { [RegexCodeTemplates.Key("JavaScript", RegexCodeKind.IsMatch)] = "{{pattern}}" };
            var templateNote = RegexCodeTemplates.Render("JavaScript", "a", Cs, RegexCodeKind.IsMatch, "", custom).Notes[0];
            Covered([templateNote, .. Enum.GetValues<RegexCodeKind>().Select(k => RegexCodeTemplates.Render("C# code", "a", Cs, k, "", new Dictionary<string, string> { [RegexCodeTemplates.Key("C# code", k)] = "{{pattern}}" }).Notes[0])], n => RegexConverter.TranslateNote(n, Loc.T), "the note that says a code template of your own was used has Cantonese wording for every kind");

            // ----- built-in snippets, token palettes -----
            Covered(RegexBuilderLibrary.Snippets.Select(s => s.Name), Loc.T, "every built-in snippet name has Cantonese wording");
            Covered(RegexBuilderLibrary.Snippets.Select(s => s.Description), Loc.T, "every built-in snippet description has Cantonese wording");
            Covered(RegexBuilderLibrary.Tokens.Select(t => t.Description), Loc.T, "every description of the guided token palette has Cantonese wording");
            Covered(RegexBuilderLibrary.Tokens.Select(t => t.Family).Distinct(), Loc.T, "every family name of the guided token palette has Cantonese wording");
            Covered(RegexBuilderConstructs.Palette.Select(t => t.Description), Loc.T, "every description of the rarer-construct palette has Cantonese wording");
            Covered(RegexBuilderConstructs.Palette.Select(t => t.Family).Distinct(), Loc.T, "every family name of the rarer-construct palette has Cantonese wording");

            // ----- the rows of the pattern comparison table -----
            var compare = RegexCompare.Run(["^a$", "^b$", "(", "a|b"], Cs, "+ a\n- b\n- a1", default, Loc.T);
            Check(compare.Valid && compare.Rows.Count == 4, "a real comparison of four patterns produces four rows");
            var english = compare.Rows.Select(r => r.Format()).ToList();
            var cantonese = compare.Rows.Select(r => r.Format(Loc.T)).ToList();
            Check(english[0].Contains("must match 1/1, must not match 2/2") && english[0].Contains(" ms / ") && english[2].Contains("invalid: "), "the comparison rows keep their English wording without the language lookup");
            for (var i = 0; i < english.Count; i++)
            {
                var row = cantonese[i];
                Check(HasCjk(row) && !row.Contains("must match") && !row.Contains("must not") && !row.Contains(" ms") && !row.Contains("timed out") && !row.Contains("invalid:"), "comparison row " + (i + 1) + " is Cantonese except for the pattern, the numbers and the engine name");
            }
            var timedOutRow = new RegexCompareRow("(a+)+$", true, null, "Backtracking", 0, 1, 0, 1, 0, 0, true);
            Check(HasCjk(timedOutRow.Format(Loc.T)) && !timedOutRow.Format(Loc.T).Contains("timed out") && timedOutRow.Format().Contains("timed out"), "a comparison row that timed out says so in Cantonese");
            Check(compare.Rows[2].Format(Loc.T).EndsWith(compare.Rows[2].Error!) , "the .NET error text of an invalid pattern is shown as the framework wrote it, after the translated lead-in");

            // ----- the rows of the timing table, and its verdicts -----
            var timing = RegexDiagnostics.Benchmark("a+b", Cs, "aaab", default, Loc.T);
            Check(timing.Valid && timing.Rows.Count >= 8, "a real timing probe produces its sample and a-run rows");
            var plainTiming = RegexDiagnostics.Benchmark("a+b", Cs, "aaab");
            Check(plainTiming.Rows.Select(r => r.Format()).All(r => Regex.IsMatch(r, @"^(sample x\d+|a-run \d+)\s+\d+ chars  \S+ ms  (match|no match|timed out)$")), "the timing rows keep their English wording without the language lookup");
            foreach (var row in timing.Rows)
            {
                var line = row.Format(Loc.T);
                Check(HasCjk(line) && !line.Contains("chars") && !line.Contains(" ms") && !line.Contains("sample x") && !line.Contains("a-run") && !line.Contains("match"), "the timing row for " + row.Subject + " is Cantonese except for the numbers");
            }
            var slow = new RegexBenchmarkRow("a-run 64", 65, 250, true, false);
            Check(HasCjk(slow.Format(Loc.T)) && !slow.Format(Loc.T).Contains("timed out") && slow.Format().Contains(">250 ms  timed out"), "a timing row that timed out says so in Cantonese");
            var verdicts = new List<string> { timing.Verdict, RegexDiagnostics.Benchmark("(a+)+$", Cs, "aaaaaaaaaaaaaaaaaaaaaaaaaaaa!", default, Loc.T).Verdict, RegexDiagnostics.Benchmark("(a|aa)+$", Cs, "", default, Loc.T).Verdict };
            var cancel = new CancellationTokenSource(); cancel.Cancel();
            verdicts.Add(RegexDiagnostics.Benchmark("a", Cs, "a", cancel.Token, Loc.T).Verdict);
            var unfinished = verdicts.Where(v => !HasCjk(v) || EnglishSentence(v)).ToList();
            if (listing) foreach (var v in unfinished) Console.WriteLine("MISSING[the timing verdicts] " + v);
            Check(unfinished.Count == 0, "the timing verdicts (warning, no growth seen, cancelled) are Cantonese when built with the language lookup" + (unfinished.Count == 0 ? "" : " (first: " + unfinished[0] + ")"));
            Covered(["Warning: time grew more than six times when the probe doubled.", "That is super-linear growth.", "Stopped at the 3 s budget; later probes were not run.", "Warning: sample x16 (1600 characters) exceeded the 250 ms limit.", "The pattern may backtrack catastrophically on similar text.", "No super-linear growth was seen on these inputs.", "This is a measurement on a few inputs, not a proof."], Loc.T, "each sentence a timing verdict can be made of has Cantonese wording");
            Check(!RegexDiagnostics.Benchmark("a+b", Cs, "aaab").Verdict.Any(c => c >= 0x2E80), "a timing verdict built without the language lookup stays English");
        }
        finally { Loc.Language = language; Loc.SchoolMode = school; }

    }
}
