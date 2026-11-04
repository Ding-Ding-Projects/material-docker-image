using System.Text.RegularExpressions;
using MaterialDockerImage.UI;
using Loc = MaterialDockerImage.Services.Loc;

/// <summary>
/// Checks that the generated and fixed wording of the last builder features has Cantonese: the help pages, the style tips, the flavor table, the comparison
/// verdicts, the scope and hex reports, the growth sentences and the labels of the new sections. Each check builds the real English text with the real code and
/// asks the language lookup for it, so a sentence added later without a translation turns a check red instead of staying English unnoticed.
/// </summary>
public static class RegexFinishLocalizationChecks
{
    static readonly RegexBuilderFlags Cs = new() { IgnoreCase = false };

    static bool HasCjk(string s) => s.Any(c => c >= 0x2E80);

    /// <summary>Strings that have no translation (or only an English echo), for the failure message.</summary>
    static List<string> Untranslated(IEnumerable<string> english, bool sentences = false) =>
        english.Where(e => e.Length > 0).Distinct().Where(e => { var t = sentences ? RegexText.Sentences(e, Loc.T) : Loc.T(e); return t == e || !HasCjk(t) || (sentences && !e.Contains("Invalid pattern") && Regex.IsMatch(t, @"(?<![w.])[A-Z][a-z]+ [a-z]+ [a-z]+ [a-z]+ [a-z]+")); }).ToList();

    public static void Run(Action<bool, string> checkIn)
    {
        var listing = Environment.GetEnvironmentVariable("REGEX_LOC_LIST") == "1";
        Action<bool, string> check = (v, n) => checkIn(v || listing, n);
        var language = Loc.Language; var school = Loc.SchoolMode;
        try
        {
            Loc.SchoolMode = false; Loc.Language = "Cantonese";
            void Covered(IEnumerable<string> english, string name, bool sentences = false)
            {
                var missing = Untranslated(english, sentences);
                if (Environment.GetEnvironmentVariable("REGEX_LOC_LIST") == "1" && missing.Count > 0) foreach (var m in missing) Console.WriteLine("MISSING[" + name + "] " + m);
                check(missing.Count == 0, name + (missing.Count == 0 ? "" : $" ({missing.Count} without Cantonese, first: {missing[0]})"));
            }

            Covered(ViewStrings, "every label, caption and message of the new builder sections has Cantonese wording");
            Covered(RegexPanelLayout.Panels.Select(p => p.Title), "every panel title has Cantonese wording");
            Covered(RegexKeyboard.Map.Select(m => m.Action).Append("Panels shown, in order:"), "every shortcut description has Cantonese wording");
            Covered(RegexCodeTemplates.Placeholders.Select(p => p.Meaning), "every template placeholder description has Cantonese wording");
            Covered(RegexCodeTemplates.Validate("") is { } a ? new[] { a, RegexCodeTemplates.Validate("x")!, RegexCodeTemplates.Validate(new string('x', 5000) + "{{pattern}}")!, "A template cannot be empty. Use Reset to go back to the built-in one." } : [], "template refusals have Cantonese wording");

            // Help pages: every line of every page.
            var helpLines = new List<string>();
            foreach (var page in RegexTokenHelp.Pages)
            {
                helpLines.Add(page.Title); helpLines.Add(page.Explanation); if (page.NetNote.Length > 0) helpLines.Add(page.NetNote);
                helpLines.Add("Written as: " + page.Syntax);
                helpLines.Add($"Example: {page.ExamplePattern} finds {page.ExampleMatch} in {page.ExampleText.Replace("\n", "\\n")}");
            }
            Covered(helpLines.Where(l => l != "Written as: "), "every help page (title, explanation, note, syntax line and example line) has Cantonese wording");
            var node = RegexExplainer.Explain("a+?", Cs)[0];
            var described = RegexTokenHelp.Describe(node, Loc.T);
            check(described != RegexTokenHelp.Describe(node) && described.Split('\n').All(l => HasCjk(l) || l.Length == 0) && RegexTokenHelp.Describe(node, null) == RegexTokenHelp.Describe(node), "a help page described with the language lookup is Cantonese line by line and unchanged without it");

            // Style tips: one pattern for every tip kind.
            var tips = new List<string>();
            foreach (var p in new[] { @"(a)(b)(c)", ".*abc", "abc.*", "a.b", "(a|)", "(a|b|)", "a|b|c", "ab|ab", "(?:a)", "[A-Za-z]" })
                tips.AddRange(RegexTips.Analyze(p, new RegexBuilderFlags()).Select(t => t.Message));
            tips.AddRange(RegexTips.Analyze("xx|xx|yy", new RegexBuilderFlags()).Select(t => t.Message));
            check(tips.Select(t => RegexTips.Analyze("", Cs).Count).Count() >= 0 && tips.Count >= 9, "the sample patterns trigger the tip kinds");
            Covered(tips, "every style tip, including those with numbers and characters in them, has Cantonese wording");

            // Backtracking findings.
            var findings = new List<string>();
            foreach (var p in new[] { "(a+)+", "(a|a)*", "(.*)*x", ".*.*x", "(a|ab)*c", @"(\w+\s?)*$" }) findings.AddRange(RegexDiagnostics.Analyze(p, new RegexBuilderFlags()).Select(f => f.Message));
            Covered(findings, "every backtracking finding has Cantonese wording");

            // Flavor table.
            var flavorText = new List<string>();
            foreach (var c in RegexFlavors.Constructs)
            {
                flavorText.Add(c.Label);
                foreach (var f in RegexFlavors.Flavors)
                {
                    flavorText.AddRange(RegexFlavorVersions.Versions(f.Id).Select(v => RegexFlavorVersions.Cell(c.Id, f.Id, v.Id).Note));
                    flavorText.Add(c.Flavors[f.Id].Note);
                }
            }
            Covered(flavorText, "every construct label and every flavor and version note has Cantonese wording");
            // "PCRE2 10.x" and "GNU grep -E" are product names with nothing to translate.
            Covered(RegexFlavors.Flavors.SelectMany(f => RegexFlavorVersions.Versions(f.Id)).Select(v => v.Label).Where(l => l is not ("PCRE2 10.x" or "GNU grep -E")), "every engine version label has Cantonese wording");

            // Comparison verdicts and growth.
            var verdicts = new List<string>
            {
                "Stopped at the time budget; later patterns were not compared.",
                "No pattern gave the expected answer for every subject, so none can be called the fastest.",
                "Fastest of the 1 pattern that are right for every subject: ab (0.001 ms per pass).", "Fastest of the 3 patterns that are right for every subject: a|b (0.012 ms per pass).",
                "1 other pattern is wrong for some subject, invalid or timed out.", "2 other patterns are wrong for some subject, invalid or timed out.",
                "These are wall-clock times on this machine and change from run to run.",
                "Not enough sample probes to estimate growth.", "The a-run probes finished in under a millisecond, too fast to estimate how time grows.",
                "Sample probes: time grew about linear (exponent about 1.0) from 100 to 1600 characters.", "A-run probes: time grew worse than quadratic (exponent about 3.1) from 8 to 64 characters.",
                "Sample probes: time grew between linear and quadratic (exponent about 1.5) from 100 to 1600 characters.", "Sample probes: time grew about quadratic (exponent about 2.0) from 100 to 1600 characters.",
            };
            Covered(verdicts, "the comparison verdict and growth sentences have Cantonese wording");
            var live = RegexCompare.Run(["^a$", "^b$"], Cs, "+ a\n- b", default, Loc.T);
            check(live.Valid && HasCjk(live.Verdict) && !live.Verdict.Contains("Fastest of") && !live.Verdict.Contains("wall-clock") && !live.Verdict.Contains("other pattern"),"a real pattern comparison reports its verdict in Cantonese when given the language lookup");
            check(RegexCompare.Run(["^a$", "^b$"], Cs, "+ a\n- b", default).Verdict.StartsWith("Fastest of the 1 pattern"), "without the language lookup the verdict stays English");

            // Scope and hex reports.
            var scopes = new List<string>();
            foreach (var scope in new[] { RegexScope.Line, RegexScope.Page, RegexScope.Whole })
            {
                var result = RegexSubject.RunScopes("ab", Cs, "ab ab\nab\n\fab", scope);
                var english = RegexSubject.Report(result);
                var cantonese = RegexSubject.Report(result, Loc.T);
                check(english != cantonese && HasCjk(cantonese) && !Regex.IsMatch(cantonese, @"\bmatch(es)?\b") && !Regex.IsMatch(cantonese, @"\bof\b|\bin \d"), "the " + scope + " scope report has no English words left in Cantonese");
                check(RegexSubject.Report(result, null) == english, "the " + scope + " scope report is unchanged English without the language lookup");
            }
            var cut = RegexSubject.RunScopes("a", Cs, string.Concat(Enumerable.Repeat("a\n", 6000)), RegexScope.Line);
            var cutReport = RegexSubject.Report(cut, Loc.T);
            check(cut.Truncated && HasCjk(cutReport) && !cutReport.Contains("bounds") && RegexSubject.Report(cut).Contains("cut at the bounds"), "the sentence that says a scope report was cut is Cantonese with the language lookup and English without it");
            var one = RegexSubject.RunScopes("ab", Cs, "ab", RegexScope.Line);
            scopes.Add(RegexSubject.Report(one));
            var many = RegexSubject.RunScopes("a", Cs, new string('a', 3) + "\n" + new string('a', 3), RegexScope.Line);
            scopes.Add(RegexSubject.Report(many));
            var lineEndings = new[] { "No line breaks.", "1 CRLF, 0 LF, 0 CR.", "2 CRLF, 1 LF, 0 CR (mixed line endings).", "0 CRLF, 3 LF, 0 CR." };
            Covered(lineEndings, "the line-ending summaries have Cantonese wording");
            var hex = RegexSubject.Hex("héllo", "UTF-8");
            var status = RegexSubject.HexStatus(hex, "a\r\nb\nc", Loc.T);
            check(HasCjk(status) && !status.Contains("bytes in") && RegexSubject.HexStatus(hex, "a\r\nb\nc").Contains("bytes in UTF-8"), "the hex status line is Cantonese with the language lookup and English without it");
            var big = RegexSubject.Hex(new string('x', 5000), "UTF-8");
            check(HasCjk(RegexSubject.HexStatus(big, "x", Loc.T)) && !RegexSubject.HexStatus(big, "x", Loc.T).Contains("shown"), "the status line of a truncated hex view is Cantonese too");
            Covered(["Stopped on a line that exceeded the 250 ms limit.", "Stopped on a page that exceeded the 250 ms limit.", "Stopped on a text that exceeded the 250 ms limit.", "The result was cut at the bounds."], "the scope report's timeout and cut sentences have Cantonese wording");

            // Messages that come from the logic classes and are shown through the language lookup, collected by running the real code.
            var logic = new List<string>();
            void Edit(RegexTreeEditResult r) => logic.Add(r.Message);
            RegexExplainNode N(string pattern, string text) => RegexExplainer.Flatten(RegexExplainer.Explain(pattern, Cs)).Select(x => x.Node).First(n => n.Text == text);
            foreach (var (p, t, q) in new[] { ("ab+c", "b+", "*"), ("ab+c", "b+", ""), ("ab+c", "c", "?"), ("xabcx", "xabcx", "+"), ("cat|dog", "cat|dog", "{2,5}?"), ("ab+c", "b+", "+++"), ("ab+c", "b+", "{5,2}"), ("ab+c", "b", ""), ("^ab", "^", "+") })
            { var n = N(p, t); Edit(RegexTreeEditor.SetQuantifier(p, Cs, n.Start, n.Length, q)); }
            Edit(RegexTreeEditor.SetQuantifier("ab+c", Cs, 2, 5, "*"));
            foreach (var kind in Enum.GetValues<RegexTreeWrap>()) { var n = N("ab", "ab"); Edit(RegexTreeEditor.Wrap("ab", Cs, n.Start, n.Length, kind)); }
            foreach (var (p, t) in new[] { ("x(?:ab)y", "(?:ab)"), ("x(?<n>ab)y", "(?<n>ab)"), ("(ab)+", "(ab)"), ("x(a|b)y", "(a|b)"), ("(a|b)", "(a|b)"), ("(a)\\1", "(a)"), ("ab", "ab"), ("a(?=b)", "(?=b)") })
            { var n = N(p, t); Edit(RegexTreeEditor.Unwrap(p, Cs, n.Start, n.Length)); }
            foreach (var (p, t) in new[] { ("a(b)c", "(b)"), ("(a)\\1", "(a)"), ("abc", "abc") }) { var n = N(p, t); Edit(RegexTreeEditor.Delete(p, Cs, n.Start, n.Length)); }
            Edit(RegexTreeEditor.Delete("aa|bb", Cs, 99, 1));
            foreach (var template in new[] { "", "no placeholder", "{{pattern}} {{nope}}", new string('x', 5000) + "{{pattern}}" }) logic.Add(RegexCodeTemplates.Validate(template)!);
            logic.Add(RegexCodeTemplates.Render("JavaScript", "a", Cs, RegexCodeKind.IsMatch, "", new Dictionary<string, string> { [RegexCodeTemplates.Key("JavaScript", RegexCodeKind.IsMatch)] = "{{pattern}}" }).Notes[0]);
            logic.Add(RegexGrepResults.ParseDate("04/11/2026", false).Problem!);
            logic.Add(RegexGrep.Search("a", Cs, new RegexGrepOptions { Folder = Path.GetTempPath(), ModifiedAfterUtc = new DateTime(2026, 1, 1), ModifiedBeforeUtc = new DateTime(2020, 1, 1) }).Error!);
            logic.Add(RegexPanelLayout.Default.SetHidden("pattern", true).Problem!);
            logic.Add(RegexPanelLayout.Default.SetHidden("zzz", true).Problem!);
            foreach (var action in new[] { new RegexSearchAction(), new RegexSearchAction { Pattern = "(" }, new RegexSearchAction { Pattern = "a", Description = new string('d', 301) }, new RegexSearchAction { Pattern = "a", ContextLines = 9 }, new RegexSearchAction { Pattern = "a", Folder = new string('f', 3000) } })
                logic.Add(action.Problem()!);
            Covered(logic, "the results and refusals of the editing, template, date, layout and action logic have Cantonese wording, sentence by sentence where a message holds several", sentences: true);
        }
        finally { Loc.Language = language; Loc.SchoolMode = school; }
    }

    // The fixed wording of the new sections, exactly as the view passes it to the language lookup.
    static readonly string[] ViewStrings =
    [
        "Preferences and layout", "Code templates", "Edit through the tree", "Action file and command line", "Keyboard shortcuts", "Panels", "always shown",
        "Default flags for new search fields", "Time limit per match (ms)", "Font size of the pattern box", "Default explanation detail", "Save preferences", "Reset preferences",
        "Preferences saved.", "Preferences were reset.", "Layout saved.", "Template saved.", "The built-in template is used again.", "Template preview copied.", "Command line copied.", "The editor was started.", "Opening the editor…",
        "Choose which panels are shown and in what order, and the defaults for new search fields. Everything is remembered for this user on this computer. The pattern and these two panels cannot be hidden.",
        "Between 50 and 1000. A match that takes longer is stopped and reported.", "Applies to the pattern box of this builder.", "Used by the explanation of a builder opened from now on.",
        "Remembers these preferences for every search field, from the next search field that is created. The match time limit applies to patterns compiled from now on.",
        "Goes back to the built-in defaults, including the panel layout, and removes the saved preferences.",
        "NonBacktracking and RightToLeft cannot both be default flags.", "The time limit must be a whole number between 50 and 1000.",
        "The preferences could not be saved:", "The saved preferences could not be removed:", "Saved preferences could not be read, so the defaults are used:", "Saved templates could not be read, so the built-in ones are used:",
        "Keyboard shortcuts of the builder", "Font size of the pattern box", "Default explanation detail",
        "Select a row, then change the pattern from here. Each edit rewrites the pattern text, checks that it still compiles, and keeps your selection.",
        "Quantifier for the selected row", "One of ? * + {n} {n,} {n,m}, optionally followed by ? for lazy. Leave it empty and press Set to remove the quantifier.",
        "Optional (zero or one)", "Zero or more", "One or more", "Set quantifier", "Applies the quantifier typed above to the selected row.", "Remove quantifier", "Removes the quantifier of the selected row.",
        "Wrap in", "Wraps the selected row, with its quantifier, in a group or lookaround.", "Wrap selected row", "Wraps the selected row in the chosen kind of group.", "Unwrap group", "Removes the selected group and keeps what is inside it.",
        "Delete row", "Deletes the selected row from the pattern.", "Capturing group", "Non-capturing group", "Atomic group", "Lookahead", "Negative lookahead", "Lookbehind", "Negative lookbehind",
        "Turn on regular expression mode to edit the pattern through the tree.", "Select a row of the explanation first.",
        "The shape of the code the Convert section generates for C#, PowerShell, JavaScript, Python, Java, Go and PHP. Your template replaces the built-in one for that language and kind; the pattern is still quoted for the language, and a pattern the language cannot express is still refused.",
        "Template text", "Plain text with placeholders. At least one placeholder is needed, usually {{pattern}}.", "Save template", "Stores this template for the language and kind, replacing the built-in one.", "Reset to built-in", "Removes your template for the language and kind.",
        "Copy preview", "Preview with the current pattern", "Preview of the template with the current pattern", "Language",
        "Modified after", "Modified after (optional)", "Modified before", "Modified before (optional)", "Modified after:", "Modified before:", "Context lines",
        "Only files last written at or after this local time are searched. Write 2026-11-04 or 2026-11-04 13:30. Empty means no limit.",
        "Only files last written at or before this local time. A date without a time covers that whole day.",
        "Lines shown before and after each match when you select a result, and written to saved result lists. At most 5.",
        "Save results…", "Saves the result list as JSON that can be loaded again, as text with context lines, or as CSV.", "Load results…", "Shows a result list that was saved earlier. The files are not read again.",
        "Open selected result", "Opens the selected result in the external editor from Settings, or in Visual Studio Code when none is set, at its line when the editor is known. Double-click or Enter on a result does the same.",
        "Save results", "Load results", "Select a result first.", "There are no results to save. Run Search files first.", "Saved result list:", "save time unknown",
        "An action file describes this search (pattern, flags, replacement, folder and file patterns) so it can be kept, shared and reopened. Opening one only fills in the builder: nothing is searched or replaced until you press the buttons.",
        "Save action file…", "Saves this search as a small JSON file.", "Open action file…", "Fills in the builder from an action file.", "Copy command line", "Copies the arguments that start the application with this search already in the builder.",
        "Save action file", "Open action file", "version", "Panels shown, in order:",
        "Copies the comparison as text, at the engine versions chosen above.",
        "Correct the expression to filter the library.", "Correct the expression to filter my patterns.", "The filter is not a valid pattern, so nothing is listed.",
        "Filter the library", "Filter my patterns", "Filter the blocks",
        "Find all matches", "Test for a match", "Code to generate",
        "Folder search results", "Quantifier",
    ];
}
