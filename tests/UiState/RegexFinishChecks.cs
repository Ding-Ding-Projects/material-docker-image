using System.Text;
using System.Text.RegularExpressions;
using MaterialDockerImage.UI;

/// <summary>
/// Checks for the last buildable parity rows: editing through the explanation tree, editable code templates, panel layout, preferences and the keyboard map. All of it is
/// framework-free logic; none of it proves the popover's layout, focus or interaction.
/// </summary>
public static class RegexFinishChecks
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
        TreeEditing();
        Templates();
        Layout();
        Preferences();
        Keyboard();
        RegexFinishMoreChecks.Run(Check);
        return count;
    }

    // ----- editing through the tree -----

    static RegexTreeEditResult SetQ(string pattern, string nodeText, string q, int occurrence = 0)
    {
        var node = RegexExplainer.Flatten(RegexExplainer.Explain(pattern, Cs)).Select(x => x.Node).Where(n => n.Text == nodeText).Skip(occurrence).First();
        return RegexTreeEditor.SetQuantifier(pattern, Cs, node.Start, node.Length, q);
    }

    static RegexTreeEditResult Tree(string pattern, string nodeText, Func<string, RegexBuilderFlags, int, int, RegexTreeEditResult> edit)
    {
        var node = RegexExplainer.Flatten(RegexExplainer.Explain(pattern, Cs)).Select(x => x.Node).First(n => n.Text == nodeText);
        return edit(pattern, Cs, node.Start, node.Length);
    }

    static void TreeEditing()
    {
        var a = SetQ("ab+c", "b+", "*");
        Check(a.Success && a.Pattern == "ab*c" && Match(a.Pattern, "xabbbc") == "abbbc", "changing a quantifier rewrites only that quantifier and the new pattern matches as the new quantifier says");
        var b = SetQ("ab+c", "b+", "{2,3}?");
        Check(b.Success && b.Pattern == "ab{2,3}?c" && Match(b.Pattern, "abbbc") == "abbbc", "a counted lazy quantifier can be set from the tree");
        var c = SetQ("ab+c", "b+", "");
        Check(c.Success && c.Pattern == "abc", "an empty quantifier removes the quantifier from the tree");
        var d = SetQ("ab+c", "c", "?");
        Check(d.Success && d.Pattern == "ab+c?" && Match(d.Pattern, "ab") == "ab" && Match(d.Pattern, "a") == "<none>" && Match(d.Pattern, "abc") == "abc", "a quantifier can be added to a single character");
        var e = SetQ("xabcx", "xabcx", "+");
        Check(e.Success && e.Pattern == "(?:xabcx)+" && e.Message.Contains("non-capturing"), "a quantifier on a multi-character literal wraps it in a non-capturing group so all of it repeats");
        var f = SetQ("cat|dog", "cat|dog", "*");
        Check(f.Success && f.Pattern == "(?:cat|dog)*", "a quantifier on an alternation wraps the whole alternation");
        Check(!SetQ("ab+c", "b+", "+++").Success && !SetQ("ab+c", "b+", "{x}").Success && !SetQ("ab+c", "b+", "*;").Success, "text that is not a quantifier is refused");
        Check(!SetQ("ab+c", "b+", "{5,2}").Success && SetQ("ab+c", "b+", "{5,2}").Message.Contains("smaller"), "a reverse repeat range is refused with the reason");
        Check(!SetQ("ab+c", "b", "").Success, "removing a quantifier from a token that has none is refused");
        var stale = RegexTreeEditor.SetQuantifier("ab+c", Cs, 2, 5, "*");
        Check(!stale.Success && stale.Pattern == "ab+c", "a span that is no longer a node is refused and the pattern is returned unchanged");
        var anchor = Tree("^ab", "^", (p, fl, s, l) => RegexTreeEditor.SetQuantifier(p, fl, s, l, "+"));
        Check(!anchor.Success, "an anchor cannot be given a quantifier");

        var w = Tree("a(b)c", "(b)", (p, fl, s, l) => RegexTreeEditor.Wrap(p, fl, s, l, RegexTreeWrap.NonCapturing));
        Check(w.Success && w.Pattern == "a(?:(b))c", "a node can be wrapped in a non-capturing group");
        var w2 = Tree("ab", "ab", (p, fl, s, l) => RegexTreeEditor.Wrap(p, fl, s, l, RegexTreeWrap.Capturing));
        Check(w2.Success && w2.Pattern == "(ab)" && w2.Message.Contains("numbered"), "wrapping in a capturing group says that later groups are renumbered");
        var w3 = Tree("price", "price", (p, fl, s, l) => RegexTreeEditor.Wrap(p, fl, s, l, RegexTreeWrap.PositiveLookahead));
        Check(w3.Success && w3.Pattern == "(?=price)", "a node can be wrapped in a lookahead");
        var w4 = Tree("x", "x", (p, fl, s, l) => RegexTreeEditor.Wrap(p, fl, s, l, RegexTreeWrap.NegativeLookbehind));
        Check(w4.Success && w4.Pattern == "(?<!x)", "a node can be wrapped in a negative lookbehind");
        var w5 = Tree("(?>a+)b", "(?>a+)", (p, fl, s, l) => RegexTreeEditor.Wrap(p, fl, s, l, RegexTreeWrap.Atomic));
        Check(w5.Success && w5.Pattern == "(?>(?>a+))b", "wrapping a group in an atomic group keeps the pattern valid");
        var wq = Tree("ab+c", "b+", (p, fl, s, l) => RegexTreeEditor.Wrap(p, fl, s, l, RegexTreeWrap.NonCapturing));
        Check(wq.Success && wq.Pattern == "a(?:b+)c", "wrapping a quantified token wraps the token with its quantifier");

        var u = Tree("x(?:a)+y", "(?:a)+", (p, fl, s, l) => RegexTreeEditor.Unwrap(p, fl, s, l));
        Check(!u.Success, "the quantified group node is the quantifier row, which is not a group to unwrap");
        var node = RegexExplainer.Flatten(RegexExplainer.Explain("x(?:a)+y", Cs)).Select(n => n.Node).First(n => n.Text == "(?:a)");
        var u1 = RegexTreeEditor.Unwrap("x(?:a)+y", Cs, node.Start, node.Length);
        Check(u1.Success && u1.Pattern == "xa+y", "a quantified group with a single character inside can be unwrapped");
        var u2 = Tree("(ab)+", "(ab)", (p, fl, s, l) => RegexTreeEditor.Unwrap(p, fl, s, l));
        Check(!u2.Success && u2.Message.Contains("quantifier"), "a quantified group with a multi-character literal is not unwrapped, because the quantifier would repeat only the last character");
        var u3 = Tree("x(a|b)y", "(a|b)", (p, fl, s, l) => RegexTreeEditor.Unwrap(p, fl, s, l));
        Check(!u3.Success && u3.Message.Contains("alternatives"), "a group with alternatives inside a longer pattern is not unwrapped");
        var u4 = Tree("(a|b)", "(a|b)", (p, fl, s, l) => RegexTreeEditor.Unwrap(p, fl, s, l));
        Check(u4.Success && u4.Pattern == "a|b", "a group with alternatives that is the whole pattern can be unwrapped");
        var u5 = Tree("x(?:ab)y", "(?:ab)", (p, fl, s, l) => RegexTreeEditor.Unwrap(p, fl, s, l));
        Check(u5.Success && u5.Pattern == "xaby", "a non-capturing group is replaced by its contents");
        var u6 = Tree("x(?<n>ab)y", "(?<n>ab)", (p, fl, s, l) => RegexTreeEditor.Unwrap(p, fl, s, l));
        Check(u6.Success && u6.Pattern == "xaby" && u6.Message.Contains("numbered"), "a named group is unwrapped and the renumbering is mentioned");
        var u7 = Tree("(a)\\1", "(a)", (p, fl, s, l) => RegexTreeEditor.Unwrap(p, fl, s, l));
        Check(!u7.Success && u7.Message.Contains("invalid"), "unwrapping a group that a backreference needs is refused because the pattern would become invalid");
        var u8 = Tree("ab", "ab", (p, fl, s, l) => RegexTreeEditor.Unwrap(p, fl, s, l));
        Check(!u8.Success, "only a group can be unwrapped");
        var u9 = Tree("a(?=b)", "(?=b)", (p, fl, s, l) => RegexTreeEditor.Unwrap(p, fl, s, l));
        Check(u9.Success && u9.Pattern == "ab", "a lookaround can be removed, and the message says its contents now consume text");

        var x = Tree("a(b)c", "(b)", (p, fl, s, l) => RegexTreeEditor.Delete(p, fl, s, l));
        Check(x.Success && x.Pattern == "ac", "a node is deleted from the pattern");
        var x2 = Tree("ab+c", "b+", (p, fl, s, l) => RegexTreeEditor.Delete(p, fl, s, l));
        Check(x2.Success && x2.Pattern == "ac", "deleting a quantified token deletes its quantifier too");
        var branches = RegexExplainer.Flatten(RegexExplainer.Explain("aa|bb|cc", Cs)).Select(n => n.Node).Where(n => n.Family == RegexTokenFamily.Branch).ToList();
        var x3 = RegexTreeEditor.Delete("aa|bb|cc", Cs, branches[1].Start, branches[1].Length);
        Check(x3.Success && x3.Pattern == "aa|cc", "deleting a middle branch removes one separating bar");
        var x4 = RegexTreeEditor.Delete("aa|bb|cc", Cs, branches[2].Start, branches[2].Length);
        Check(x4.Success && x4.Pattern == "aa|bb", "deleting the last branch removes the bar before it");
        var x5 = RegexTreeEditor.Delete("aa|bb|cc", Cs, branches[0].Start, branches[0].Length);
        Check(x5.Success && x5.Pattern == "bb|cc", "deleting the first branch removes the bar after it");
        var x6 = Tree("(a)\\1", "(a)", (p, fl, s, l) => RegexTreeEditor.Delete(p, fl, s, l));
        Check(!x6.Success && x6.Pattern == "(a)\\1", "a deletion that would make the pattern invalid is refused and changes nothing");
        var x7 = Tree("abc", "abc", (p, fl, s, l) => RegexTreeEditor.Delete(p, fl, s, l));
        Check(!x7.Success && x7.Message.Contains("empty"), "deleting the whole pattern is refused");
        var long1 = new string('a', RegexBuilderEngine.MaxPatternLength - 1) + "b";
        var tooLong = RegexTreeEditor.SetQuantifier(long1, Cs, long1.Length - 1, 1, "{1000}");
        Check(!tooLong.Success || tooLong.Pattern.Length <= RegexBuilderEngine.MaxPatternLength, "an edit cannot grow the pattern past the pattern length limit");
        // Repeating edits on regenerated text: the tree is read again after each one.
        var step1 = SetQ("(ab)+c", "(ab)+", "*");
        var step2 = step1.Success ? SetQ(step1.Pattern, "c", "?") : step1;
        Check(step2.Success && step2.Pattern == "(ab)*c?", "edits can follow each other on the regenerated pattern");
    }

    // ----- code templates -----

    static string Norm(string s) => s.Replace("\r\n", "\n");

    static void Templates()
    {
        var patterns = new (string Pattern, RegexBuilderFlags Flags, string Replacement)[]
        {
            (@"\d+", Ci, "<$1>"), (@"a""b'c", Cs, @"x\y"), ("^(?<n>[a-z]+)$", new RegexBuilderFlags { Multiline = true, Singleline = true }, "${n}"), (@"\bfoo\b", Cs with { IgnorePatternWhitespace = true }, ""),
        };
        var checkedCount = 0;
        foreach (var language in RegexCodeTemplates.Languages)
            foreach (var kind in Enum.GetValues<RegexCodeKind>())
                foreach (var (pattern, flags, replacement) in patterns)
                {
                    var baseline = RegexConverter.Convert(language, pattern, flags, kind, replacement);
                    var rendered = RegexCodeTemplates.Render(language, pattern, flags, kind, replacement, null);
                    if (!baseline.Converted) { if (rendered.Converted) throw new Exception($"{language} {kind} rendered a pattern the converter refused"); continue; }
                    if (Norm(rendered.Text) != Norm(baseline.Text)) throw new Exception($"{language} {kind} default template differs from the converter for {pattern}:\n{rendered.Text}\n---\n{baseline.Text}");
                    checkedCount++;
                }
        Check(checkedCount >= 100, $"the built-in templates reproduce the converter output exactly for every language and kind ({checkedCount} cases)");
        Check(RegexCodeTemplates.Languages.All(l => Enum.GetValues<RegexCodeKind>().All(k => RegexCodeTemplates.Default(l, k) != null)), "every templated language has a built-in template for every kind of code");
        Check(RegexCodeTemplates.Languages.Count * 4 == RegexCodeTemplates.MaxTemplates, "the template limit equals the number of editable templates");

        var custom = new Dictionary<string, string> { [RegexCodeTemplates.Key("JavaScript", RegexCodeKind.IsMatch)] = "// {{language}} {{kind}}\nconst ok = {{pattern}}.test(s);" };
        var js = RegexCodeTemplates.Render("JavaScript", @"a""b", Ci, RegexCodeKind.IsMatch, "", custom);
        Check(js.Converted && js.Text.StartsWith("// JavaScript IsMatch\nconst ok = /a\"b/iu.test(s);") && js.Notes[0].Contains("your template"), "a user template replaces the built-in one, is filled with the quoted pattern and is labelled");
        var other = RegexCodeTemplates.Render("JavaScript", "x", Ci, RegexCodeKind.Split, "", custom);
        Check(Norm(other.Text) == Norm(RegexConverter.Convert("JavaScript", "x", Ci, RegexCodeKind.Split).Text), "a template for one kind leaves the other kinds on their built-in templates");
        var refused = RegexCodeTemplates.Render("JavaScript", "a++", Ci, RegexCodeKind.IsMatch, "", custom);
        Check(!refused.Converted && refused.Text.Length == 0, "a pattern the language cannot express stays refused even when a user template exists");
        var quoted = RegexCodeTemplates.Render("Python", "it's \"q\"\\d", Ci, RegexCodeKind.IsMatch, "", new Dictionary<string, string> { [RegexCodeTemplates.Key("Python", RegexCodeKind.IsMatch)] = "x = {{pattern}}" });
        Check(quoted.Text == "x = " + RegexConverter.PythonString("it's \"q\"\\d"), "editing a template cannot change how the pattern is quoted");
        Check(RegexCodeTemplates.Render("Perl", "x", Ci, RegexCodeKind.IsMatch, "", custom).Text == RegexConverter.Convert("Perl", "x", Ci, RegexCodeKind.IsMatch).Text, "a language without editable templates keeps its fixed output");

        Check(RegexCodeTemplates.Validate("") != null && RegexCodeTemplates.Validate("   ") != null, "an empty template is refused");
        Check(RegexCodeTemplates.Validate("no placeholder here")!.Contains("placeholder"), "a template without any placeholder is refused, because the code would not hold the pattern");
        Check(RegexCodeTemplates.Validate("{{pattern}} {{nope}}")!.Contains("{{nope}}"), "an unknown placeholder is named in the refusal");
        Check(RegexCodeTemplates.Validate(new string('x', RegexCodeTemplates.MaxTemplateLength) + "{{pattern}}") != null, "a template over the length limit is refused");
        Check(RegexCodeTemplates.Validate("a {{ pattern }} b {{raw_pattern}}") == null, "spaces inside the braces are allowed");
        Check(RegexCodeTemplates.UnknownPlaceholders("{{a}}{{pattern}}{{a}}").Count == 1, "an unknown placeholder is reported once");
        Check(RegexCodeTemplates.Placeholders.All(p => RegexCodeTemplates.Values("C# code", "a", Ci, RegexCodeKind.Matches, "b")!.ContainsKey(p.Name)), "every documented placeholder has a value");

        var set = RegexCodeTemplates.With(new Dictionary<string, string>(), "Go", RegexCodeKind.Split, "re := regexp.MustCompile({{pattern}})\nx := re.Split(s, 3)");
        Check(set.Count == 1 && RegexCodeTemplates.Without(set, "Go", RegexCodeKind.Split).Count == 0, "a template is added and removed (reset) without touching the others");
        var threw = false;
        try { RegexCodeTemplates.With(set, "Perl", RegexCodeKind.Split, "{{pattern}}"); } catch (ArgumentException) { threw = true; }
        var threw2 = false;
        try { RegexCodeTemplates.With(set, "Go", RegexCodeKind.Split, "bad"); } catch (ArgumentException) { threw2 = true; }
        Check(threw && threw2, "a template for a language without templates, or an invalid one, is not stored");

        var dir = Path.Combine(Path.GetTempPath(), "regex-templates-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "t.json");
            Check(RegexCodeTemplates.Load(path).Count == 0, "a missing templates file is an empty set");
            RegexCodeTemplates.Save(path, set);
            var back = RegexCodeTemplates.Load(path);
            Check(back.Count == 1 && back[RegexCodeTemplates.Key("Go", RegexCodeKind.Split)].Contains("re.Split(s, 3)") && !File.Exists(path + ".tmp"), "saved templates are written atomically and read back exactly");
            File.WriteAllText(path, RegexCodeTemplates.Serialize(set).Replace("regex-templates", "something-else"));
            var badFormat = false; try { RegexCodeTemplates.Load(path); } catch (InvalidDataException) { badFormat = true; }
            File.WriteAllText(path, RegexCodeTemplates.Serialize(set).Replace("Go|Split", "Cobol|Split"));
            var badKey = false; try { RegexCodeTemplates.Load(path); } catch (InvalidDataException) { badKey = true; }
            File.WriteAllText(path, RegexCodeTemplates.Serialize(set).Replace("{{pattern}}", "{{bogus}}"));
            var badTemplate = false; try { RegexCodeTemplates.Load(path); } catch (InvalidDataException) { badTemplate = true; }
            File.WriteAllText(path, "not json");
            var badJson = false; try { RegexCodeTemplates.Load(path); } catch (InvalidDataException) { badJson = true; }
            Check(badFormat && badKey && badTemplate && badJson, "a templates file with a wrong format, an unknown template, an invalid template or broken JSON is refused as a whole");
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    // ----- layout -----

    static void Layout()
    {
        var all = RegexPanelLayout.Panels.Select(p => p.Id).ToList();
        Check(all.Distinct().Count() == all.Count && RegexPanelLayout.Default.Visible().SequenceEqual(all), "the default layout shows every panel once in the built-in order");
        Check(RegexPanelLayout.Panels.Where(p => p.Required).Select(p => p.Id).SequenceEqual(["pattern", "preferences"]), "exactly the pattern and the preferences panel are required");
        var (hidden, problem) = RegexPanelLayout.Default.SetHidden("convert", true);
        Check(problem == null && !hidden.Visible().Contains("convert") && hidden.IsHidden("convert") && hidden.Visible().Count == all.Count - 1, "a panel can be hidden");
        var (shown, _) = hidden.SetHidden("convert", false);
        Check(shown.Visible().Contains("convert"), "a hidden panel can be shown again");
        var (stay, why) = RegexPanelLayout.Default.SetHidden("pattern", true);
        Check(why != null && stay.Visible().Contains("pattern") && RegexPanelLayout.Default.SetHidden("preferences", true).Problem != null, "the required panels cannot be hidden and the refusal says why");
        Check(RegexPanelLayout.Default.SetHidden("nope", true).Problem != null, "hiding an unknown panel is refused");
        var moved = RegexPanelLayout.Default.Move("test", -1);
        Check(moved.Visible().ToList().IndexOf("test") == all.IndexOf("test") - 1 && moved.Visible().ToList().IndexOf("explanation") == all.IndexOf("explanation") + 1, "a panel moves up past its neighbour");
        Check(RegexPanelLayout.Default.Move("pattern", -1).Visible().SequenceEqual(all) && RegexPanelLayout.Default.Move("preferences", 1).Visible().SequenceEqual(all), "moving past either end changes nothing");
        var stored = new RegexPanelLayout(["files", "bogus", "pattern", "files"], ["convert"]);
        var normalized = stored.Normalize();
        Check(normalized.Count == all.Count && normalized[0] == "files" && normalized[1] == "pattern" && normalized.Distinct().Count() == all.Count && !normalized.Contains("bogus"), "a stored order drops unknown and duplicate panels and appends the ones it does not mention");
        Check(new RegexPanelLayout([], ["pattern", "preferences"]).Visible().Contains("pattern") && new RegexPanelLayout([], ["pattern", "preferences"]).Visible().Contains("preferences"), "a stored layout cannot hide a required panel");
    }

    // ----- preferences -----

    static void Preferences()
    {
        var before = RegexBuilderPreferences.Current;
        var timeout = RegexBuilderEngine.MatchTimeout;
        try
        {
            Check(new RegexBuilderPreferences().DefaultFlags == new RegexBuilderFlags() && new RegexBuilderPreferences().MatchTimeoutMs == 250 && new RegexBuilderPreferences().PatternFontSize == 13, "the preferences start at the built-in defaults");
            var next = new RegexBuilderPreferences
            {
                DefaultFlags = new RegexBuilderFlags { IgnoreCase = false, Multiline = true }, MatchTimeoutMs = 400, PatternFontSize = 16, ExplanationDetail = "Standard",
                Layout = new RegexPanelLayout(["files", "pattern"], ["convert", "split"]),
            };
            var round = RegexBuilderPreferences.Parse(next.Serialize());
            Check(round.DefaultFlags == next.DefaultFlags && round.MatchTimeoutMs == 400 && round.PatternFontSize == 16 && round.ExplanationDetail == "Standard" && round.Layout.Hidden.SequenceEqual(["convert", "split"]) && round.Layout.Normalize()[0] == "files", "preferences survive a save and load exactly");
            RegexBuilderPreferences.Apply(next);
            Check(new RegexFieldState().Flags == next.DefaultFlags && !new RegexFieldState().Flags.IgnoreCase && new RegexFieldState().Flags.Multiline, "a new search field starts with the default flags that were saved");
            Check(RegexBuilderEngine.MatchTimeout == TimeSpan.FromMilliseconds(400), "the saved match time limit is the limit of the engine");
            var slow = RegexBuilderEngine.Compile(@"(?=x)x", Cs).Regex;
            Check(slow != null && slow.MatchTimeout == TimeSpan.FromMilliseconds(400), "a pattern compiled after the change uses the new limit");
            Check(RegexBuilderPreferences.Current.Precision == RegexExplainPrecision.Standard, "the saved explanation detail selects the precision");
            RegexBuilderPreferences.Apply(new RegexBuilderPreferences { MatchTimeoutMs = 5, PatternFontSize = 400, ExplanationDetail = "weird" });
            Check(RegexBuilderEngine.MatchTimeout == TimeSpan.FromMilliseconds(RegexBuilderEngine.MinTimeoutMs) && RegexBuilderPreferences.Current.PatternFontSize == RegexBuilderPreferences.MaxFontSize && RegexBuilderPreferences.Current.ExplanationDetail == "Full", "applied values are clamped to their ranges");
            RegexBuilderPreferences.Apply(new RegexBuilderPreferences());
            Check(new RegexFieldState().Flags == new RegexBuilderFlags() && RegexBuilderEngine.MatchTimeout == TimeSpan.FromMilliseconds(250), "resetting restores the built-in defaults for new fields and for the engine");

            string Bad(Func<string, string> edit) { try { RegexBuilderPreferences.Parse(edit(next.Serialize())); return ""; } catch (InvalidDataException e) { return e.Message; } }
            Check(Bad(t => t.Replace("regex-preferences", "x")).Contains("format") && Bad(t => t.Replace("\"version\": 1", "\"version\": 2")).Contains("version"), "a wrong format or version is refused");
            Check(Bad(t => t.Replace("\"matchTimeoutMs\": 400", "\"matchTimeoutMs\": 5000")).Contains("time limit") && Bad(t => t.Replace("\"patternFontSize\": 16", "\"patternFontSize\": 99")).Contains("font size"), "an out-of-range time limit or font size is refused, not clamped silently");
            Check(Bad(t => t.Replace("\"explanationDetail\": \"Standard\"", "\"explanationDetail\": \"Huge\"")).Contains("detail") && Bad(t => t.Replace("\"convert\"", "\"nowhere\"")).Contains("nowhere"), "an unknown detail or panel is refused");
            Check(Bad(t => t.Replace("\"hiddenPanels\": [", "\"hiddenPanels\": [\"pattern\",")).Contains("cannot be hidden"), "a stored layout that hides a required panel is refused");
            Check(Bad(t => t.Replace("\"NonBacktracking\": false", "\"NonBacktracking\": true").Replace("\"RightToLeft\": false", "\"RightToLeft\": true")).Contains("NonBacktracking"), "default flags that combine NonBacktracking with RightToLeft are refused");
            Check(Bad(t => "{broken").Contains("not valid"), "broken JSON is refused");

            var dir = Path.Combine(Path.GetTempPath(), "regex-prefs-" + Guid.NewGuid().ToString("N"));
            try
            {
                var path = Path.Combine(dir, "p.json");
                Check(RegexBuilderPreferences.Load(path).MatchTimeoutMs == 250, "a missing preferences file gives the defaults");
                next.Save(path);
                Check(RegexBuilderPreferences.Load(path).PatternFontSize == 16 && !File.Exists(path + ".tmp"), "preferences are written atomically and read back");
            }
            finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
        }
        finally { RegexBuilderPreferences.Apply(before); RegexBuilderEngine.SetMatchTimeout((int)timeout.TotalMilliseconds); }
    }

    // ----- keyboard -----

    static void Keyboard()
    {
        var shown = RegexPanelLayout.Default.Visible();
        Check(RegexKeyboard.Target("F6", shown, "pattern") == shown[1] && RegexKeyboard.Target("F6", shown, shown[^1]) == shown[0], "F6 moves to the next panel and wraps from the last to the first");
        Check(RegexKeyboard.Target("Shift+F6", shown, shown[0]) == shown[^1] && RegexKeyboard.Target("Shift+F6", shown, shown[3]) == shown[2], "Shift+F6 moves to the previous panel and wraps");
        Check(RegexKeyboard.Target("Ctrl+1", shown, null) == shown[0] && RegexKeyboard.Target("Ctrl+9", shown, null) == shown[8] && RegexKeyboard.Target("Ctrl+0", shown, null) == shown[9], "Ctrl+1 to Ctrl+9 and Ctrl+0 jump to the first ten panels");
        Check(RegexKeyboard.Target("Alt+Home", shown, shown[5]) == shown[0], "Alt+Home goes back to the pattern panel");
        Check(RegexKeyboard.Target("Ctrl+7", ["pattern", "flags"], null) == null && RegexKeyboard.Target("Ctrl+A", shown, null) == null && RegexKeyboard.Target("F6", [], null) == null, "a number beyond the panels shown, another key or no panels does nothing");
        var seen = new List<string>(); string? at = null;
        for (var i = 0; i < shown.Count; i++) { at = RegexKeyboard.Target("F6", shown, at); seen.Add(at!); }
        Check(seen.OrderBy(x => x).SequenceEqual(shown.OrderBy(x => x)), "pressing F6 repeatedly reaches every panel that is shown");
        var fewer = RegexPanelLayout.Default.SetHidden("split", true).Layout.SetHidden("convert", true).Layout.Visible();
        var seen2 = new List<string>(); at = null;
        for (var i = 0; i < fewer.Count; i++) { at = RegexKeyboard.Target("F6", fewer, at); seen2.Add(at!); }
        Check(seen2.OrderBy(x => x).SequenceEqual(fewer.OrderBy(x => x)) && !seen2.Contains("split"), "F6 follows the layout: a hidden panel is skipped and all the others are reached");
        var text = RegexKeyboard.Describe(shown);
        Check(RegexKeyboard.Map.All(m => text.Contains(m.Gesture)) && shown.All(id => text.Contains(RegexPanelLayout.Panel(id)!.Title)), "the keyboard help lists every shortcut and every panel that is shown");
        Check(Enumerable.Range(0, RegexKeyboard.DirectPanels).All(i => RegexKeyboard.Target(RegexKeyboard.DirectGesture(i)!, shown, null) == shown[i]) && RegexKeyboard.DirectGesture(10) == null, "the direct gesture listed for a panel really goes to that panel");
    }
}
