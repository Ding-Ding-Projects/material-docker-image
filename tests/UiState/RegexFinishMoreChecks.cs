using System.Text;
using MaterialDockerImage.UI;

/// <summary>
/// Checks for the folder search extras (date window, context lines, saved result lists, opening a result in an editor), action files and the command line,
/// the filters of the library and my patterns, and the engine version picker. Framework-free logic only.
/// </summary>
public static class RegexFinishMoreChecks
{
    static Action<bool, string> check = (_, _) => { };
    static void Check(bool value, string name) => check(value, name);
    static readonly RegexBuilderFlags Cs = new() { IgnoreCase = false };

    public static void Run(Action<bool, string> checker)
    {
        check = checker;
        GrepExtras();
        GrepResults();
        Actions();
        CommandLine();
        NestedSearch();
        FlavorVersions();
    }

    static string TempFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "regex-finish-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    static void Cleanup(string dir) { try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    // ----- folder search extras -----

    static void GrepExtras()
    {
        var dir = TempFolder();
        try
        {
            File.WriteAllText(Path.Combine(dir, "old.txt"), "one\ntwo\nNEEDLE old\nfour\nfive\n");
            File.WriteAllText(Path.Combine(dir, "new.txt"), "NEEDLE first line\nsecond\nthird\n");
            File.SetLastWriteTimeUtc(Path.Combine(dir, "old.txt"), new DateTime(2020, 1, 1, 12, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(Path.Combine(dir, "new.txt"), new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc));
            var options = new RegexGrepOptions { Folder = dir };
            var all = RegexGrep.Search("NEEDLE", Cs, options);
            Check(all.Valid && all.Matches.Count == 2 && all.Matches.All(m => m.ContextBefore.Count == 0 && m.ContextAfter.Count == 0), "without context lines a match carries no context");
            var recent = RegexGrep.Search("NEEDLE", Cs, options with { ModifiedAfterUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
            Check(recent.Matches.Count == 1 && Path.GetFileName(recent.Matches[0].File) == "new.txt" && recent.FilesSearched == 1, "the modified-after filter leaves out older files and does not count them as searched");
            var older = RegexGrep.Search("NEEDLE", Cs, options with { ModifiedBeforeUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
            Check(older.Matches.Count == 1 && Path.GetFileName(older.Matches[0].File) == "old.txt", "the modified-before filter leaves out newer files");
            var window = RegexGrep.Search("NEEDLE", Cs, options with { ModifiedAfterUtc = new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc), ModifiedBeforeUtc = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
            Check(window.Matches.Count == 1 && Path.GetFileName(window.Matches[0].File) == "old.txt", "both date bounds together select the files inside the window");
            var inverted = RegexGrep.Search("NEEDLE", Cs, options with { ModifiedAfterUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ModifiedBeforeUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
            Check(!inverted.Valid && inverted.Error!.Contains("later"), "a window whose start is after its end is refused with the reason");
            var plan = RegexGrep.PlanReplace("NEEDLE", Cs, "FOUND", options with { ModifiedAfterUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
            Check(plan.Valid && plan.Files.Count == 1 && Path.GetFileName(plan.Files[0].File) == "new.txt", "a replacement plan honours the same date window, so a filtered-out file is never changed");

            var context = RegexGrep.Search("NEEDLE", Cs, options with { ContextLines = 2 });
            var oldHit = context.Matches.First(m => Path.GetFileName(m.File) == "old.txt");
            var newHit = context.Matches.First(m => Path.GetFileName(m.File) == "new.txt");
            Check(oldHit.ContextBefore.SequenceEqual(["one", "two"]) && oldHit.ContextAfter.SequenceEqual(["four", "five"]), "context lines list the lines before and after the match line in file order");
            Check(newHit.ContextBefore.Count == 0 && newHit.ContextAfter.SequenceEqual(["second", "third"]), "a match on the first line has no lines before it and the available lines after it");
            var capped = RegexGrep.Search("NEEDLE", Cs, options with { ContextLines = 500 });
            Check(capped.Matches.First(m => Path.GetFileName(m.File) == "old.txt").ContextBefore.Count <= RegexGrep.MaxContextLines, "the number of context lines has a hard ceiling");
            File.WriteAllText(Path.Combine(dir, "end.txt"), "a\nb\nNEEDLE");
            var end = RegexGrep.Search("NEEDLE", Cs, options with { ContextLines = 3, Globs = "end.txt" });
            Check(end.Matches.Count == 1 && end.Matches[0].ContextAfter.Count == 0 && end.Matches[0].ContextBefore.SequenceEqual(["a", "b"]), "a match on the last line has no lines after it");
        }
        finally { Cleanup(dir); }
    }

    static void GrepResults()
    {
        var dir = TempFolder();
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.txt"), "x\n=NEEDLE,\"q\"\ny\n");
            var options = new RegexGrepOptions { Folder = dir, ContextLines = 1 };
            var result = RegexGrep.Search("NEEDLE", Cs, options);
            var json = RegexGrepResults.Export("JSON (can be loaded again)", "NEEDLE", Cs, options, result, new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero));
            var back = RegexGrepResults.Parse(json);
            Check(back.Pattern == "NEEDLE" && back.Matches.Count == 1 && back.Matches[0].LineText == result.Matches[0].LineText && back.Matches[0].ContextBefore.SequenceEqual(["x"]) && back.Matches[0].ContextAfter.SequenceEqual(["y"]) && back.Saved.Year == 2026, "a saved result list is read back with its matches and context");
            Check(RegexGrepResults.ToCsv(result.Matches).Contains("\"'=NEEDLE,\"\"q\"\"\"") || RegexGrepResults.ToCsv(result.Matches).Contains("'=NEEDLE"), "a CSV cell that starts like a formula is neutralised and quotes are doubled");
            Check(RegexGrepResults.ToCsv(result.Matches).StartsWith("File,Line,Column,Length,Match,Line text\r\n"), "the CSV has a header row");
            var text = RegexGrepResults.Export("Text with context", "NEEDLE", Cs, options, result);
            Check(text.Contains("    1  x") && text.Contains("    2: =NEEDLE") && text.Contains("    3  y"), "the text export numbers the context lines around the match line");
            Check(RegexGrepResults.Formats.Count == 3, "three export formats are offered");
            string Bad(string t) { try { RegexGrepResults.Parse(t); return ""; } catch (InvalidDataException e) { return e.Message; } }
            Check(Bad(json.Replace("regex-grep-results", "x")).Contains("not a saved") && Bad(json.Replace("\"version\": 1", "\"version\": 9")).Contains("version") && Bad(json.Replace("\"line\": 2", "\"line\": 0")).Contains("not valid") && Bad("[1]").Length > 0, "a list with the wrong format, version, a bad line number or no object is refused");
            Check(Bad(json.Replace("\"file\": ", "\"filex\": ")).Contains("no file"), "a match without a file is refused");

            Check(RegexGrepResults.ParseDate("", false).Utc == null && RegexGrepResults.ParseDate("  ", true).Problem == null, "an empty date means no bound");
            var from = RegexGrepResults.ParseDate("2026-11-04", false);
            var to = RegexGrepResults.ParseDate("2026-11-04", true);
            Check(from.Problem == null && to.Problem == null && to.Utc!.Value - from.Utc!.Value > TimeSpan.FromHours(22) && to.Utc.Value - from.Utc.Value < TimeSpan.FromHours(25), "a date without a time covers the whole day as an upper bound");
            Check(RegexGrepResults.ParseDate("2026-11-04 13:30", false).Problem == null && RegexGrepResults.ParseDate("04/11/2026", false).Problem != null && RegexGrepResults.ParseDate("2026-13-40", false).Problem != null, "dates are written yyyy-MM-dd or yyyy-MM-dd HH:mm and anything else is refused");

            var code = RegexGrepResults.EditorArguments(@"C:\Program Files\Microsoft VS Code\Code.exe", @"C:\p\a b.txt", 12, 5);
            Check(code.PositionKnown && code.Arguments.SequenceEqual(["--goto", @"C:\p\a b.txt:12:5"]), "Visual Studio Code is given --goto with file, line and column as one argument");
            var npp = RegexGrepResults.EditorArguments(@"C:\x\notepad++.exe", @"C:\p\a.txt", 3, 9);
            Check(npp.PositionKnown && npp.Arguments.SequenceEqual(["-n3", "-c9", @"C:\p\a.txt"]), "Notepad++ is given its line and column switches");
            var sub = RegexGrepResults.EditorArguments(@"C:\x\subl.exe", @"C:\p\a.txt", 3, 9);
            Check(sub.PositionKnown && sub.Arguments.SequenceEqual([@"C:\p\a.txt:3:9"]), "Sublime Text is given file:line:column");
            var unknown = RegexGrepResults.EditorArguments(@"C:\x\mystery.exe", @"C:\p\a.txt", 3, 9);
            Check(!unknown.PositionKnown && unknown.Arguments.SequenceEqual([@"C:\p\a.txt"]), "an editor whose line syntax is unknown gets the file alone, never a guessed switch");
            Check(RegexGrepResults.EditorArguments("", "f", 0, 0).Arguments.Count == 1, "an empty editor name still gives the file");
        }
        finally { Cleanup(dir); }
    }

    // ----- action files -----

    static void Actions()
    {
        var action = new RegexSearchAction { Description = "find ports", Pattern = @"(?<p>\d{2,5})/tcp", Flags = new RegexBuilderFlags { IgnoreCase = false, Multiline = true }, Replacement = "${p}", Folder = @"C:\work\logs", Globs = "*.log;!*.min.log", Recursive = false, ContextLines = 2 };
        Check(action.Problem() == null, "a complete action describes a valid search");
        var back = RegexSearchAction.Parse(action.Serialize());
        Check(back.Pattern == action.Pattern && back.Flags == action.Flags && back.Replacement == "${p}" && back.Folder == action.Folder && back.Globs == action.Globs && !back.Recursive && back.ContextLines == 2 && back.Description == "find ports", "an action file round-trips every field");
        Check(back.ToGrepOptions().Folder == action.Folder && back.ToGrepOptions().ContextLines == 2 && !back.ToGrepOptions().Recursive, "the action converts to folder-search options");
        string Bad(Func<string, string> edit) { try { RegexSearchAction.Parse(edit(action.Serialize())); return ""; } catch (InvalidDataException e) { return e.Message; } }
        Check(Bad(t => t.Replace("regex-action", "x")).Contains("not a regular") && Bad(t => t.Replace("\"version\": 1", "\"version\": 3")).Contains("version"), "a file with the wrong format or version is refused");
        Check(Bad(t => t.Replace("(?<p>", "(?<p")).Contains("not valid"), "an action whose pattern does not compile is refused");
        Check(Bad(t => t.Replace("\"contextLines\": 2", "\"contextLines\": 99")).Contains("Context") && Bad(_ => "nope").Contains("not valid"), "out-of-range context lines or broken JSON are refused");
        Check(Bad(t => t.Replace("\"pattern\": ", "\"patternx\": ")).Contains("no pattern"), "an action without a pattern is refused");
        Check(new RegexSearchAction { Pattern = new string('a', RegexBuilderEngine.MaxPatternLength + 1) }.Problem() != null && new RegexSearchAction { Pattern = "a", Description = new string('d', 301) }.Problem() != null, "an over-long pattern or description is refused");
        Check(!action.Serialize().Contains("run") && !action.Serialize().Contains("command"), "an action file holds a search description only, with no field that names a command");

        var dir = TempFolder();
        try
        {
            var path = Path.Combine(dir, "a.json");
            action.Save(path);
            Check(RegexSearchAction.Load(path).Pattern == action.Pattern && !File.Exists(path + ".tmp"), "an action file is saved atomically and loaded back");
            File.WriteAllBytes(path, new byte[300 * 1024]);
            var big = false; try { RegexSearchAction.Load(path); } catch (InvalidDataException) { big = true; }
            var missing = false; try { RegexSearchAction.Load(Path.Combine(dir, "none.json")); } catch (InvalidDataException) { missing = true; }
            var invalidSave = false; try { new RegexSearchAction { Pattern = "(" }.Save(Path.Combine(dir, "bad.json")); } catch (InvalidOperationException) { invalidSave = true; }
            Check(big && missing && invalidSave && !File.Exists(Path.Combine(dir, "bad.json")), "an oversize or missing file is refused on load and an invalid action is never written");
        }
        finally { Cleanup(dir); }
    }

    /// <summary>Splits a command line the way CommandLineToArgvW does, so the quoting check does not trust the quoting code to read its own output.</summary>
    static List<string> Split(string line)
    {
        var args = new List<string>(); var sb = new StringBuilder(); var inQuotes = false; var started = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '\\')
            {
                var n = 0; while (i < line.Length && line[i] == '\\') { n++; i++; }
                if (i < line.Length && line[i] == '"') { sb.Append('\\', n / 2); if (n % 2 == 1) sb.Append('"'); else inQuotes = !inQuotes; started = true; }
                else { sb.Append('\\', n); i--; started = true; }
                continue;
            }
            if (ch == '"') { inQuotes = !inQuotes; started = true; continue; }
            if (char.IsWhiteSpace(ch) && !inQuotes) { if (started) { args.Add(sb.ToString()); sb.Clear(); started = false; } continue; }
            sb.Append(ch); started = true;
        }
        if (started) args.Add(sb.ToString());
        return args;
    }

    static void CommandLine()
    {
        Check(!RegexCommandLine.Requested(["--capture", "x"]) && RegexCommandLine.Requested(["--regex-pattern", "a"]) && RegexCommandLine.Parse(["--capture"]) is (null, null), "ordinary arguments do not ask for the builder");
        var (a, problem) = RegexCommandLine.Parse(["--regex-pattern", @"\d+", "--regex-flags", "im", "--regex-replacement", "<$0>", "--regex-folder", @"C:\logs", "--regex-globs", "*.log"]);
        Check(problem == null && a!.Pattern == @"\d+" && a.Flags.IgnoreCase && a.Flags.Multiline && !a.Flags.Singleline && a.Replacement == "<$0>" && a.Folder == @"C:\logs" && a.Globs == "*.log", "the pattern, flags, replacement, folder and file patterns are read from the arguments");
        var (b, _) = RegexCommandLine.Parse(["--regex-pattern", "x"]);
        Check(b!.Flags == new RegexBuilderFlags { IgnoreCase = false } && b.Globs == "*", "without flag letters no flag is on");
        Check(RegexCommandLine.Parse(["--regex-pattern", "x", "--regex-flags", "iz"]).Problem!.Contains("'z'") && RegexCommandLine.Parse(["--regex-pattern", "x", "--regex-flags", "rb"]).Problem!.Contains("cannot be combined"), "an unknown flag letter or an impossible combination is refused with the reason");
        Check(RegexCommandLine.Parse(["--regex-pattern", "("]).Problem!.Contains("not valid"), "a pattern that does not compile is refused");
        Check(RegexCommandLine.Parse(["--regex-pattern"]).Problem!.Contains("needs a value") && RegexCommandLine.Parse(["--regex-pattern", "--regex-flags", "i"]).Problem!.Contains("needs a value"), "an option without its value is refused");
        Check(RegexCommandLine.Parse(["--regex-flags", "i"]).Problem!.Contains("--regex-pattern is required"), "other options without a pattern are refused");
        Check(RegexCommandLine.Parse(["--regex-pattern", "x", "--regex-action", "f.json"]).Problem!.Contains("not both"), "a pattern and an action file together are refused");
        Check(RegexCommandLine.Parse(["--regex-action", Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N") + ".json")]).Problem!.Contains("does not exist"), "a missing action file is reported");
        var dir = TempFolder();
        try
        {
            var path = Path.Combine(dir, "a.json");
            new RegexSearchAction { Pattern = "hello", Folder = dir }.Save(path);
            var (fromFile, fileProblem) = RegexCommandLine.Parse(["--regex-action", path]);
            Check(fileProblem == null && fromFile!.Pattern == "hello", "an action file named on the command line is loaded");
        }
        finally { Cleanup(dir); }
        Check(RegexCommandLine.FlagsToLetters(new RegexBuilderFlags { IgnoreCase = true, Singleline = true }) == "is" && RegexCommandLine.FlagLetters.Count == RegexBuilderFlags.All.Count, "every flag has exactly one letter");
        foreach (var (letter, name) in RegexCommandLine.FlagLetters)
            if (RegexCommandLine.ParseFlags(letter.ToString()).Flags is not { } f || !f.Get(name)) throw new Exception("flag letter " + letter);
        Check(true, "each flag letter turns on its own flag");
        var tricky = new RegexSearchAction { Pattern = "a\"b\\\\c\\d", Flags = new RegexBuilderFlags { IgnoreCase = false, Singleline = true }, Replacement = "x \"y\"\\", Folder = @"C:\my folder\", Globs = "*.a;*.b" };
        var args = Split(RegexCommandLine.ToArguments(tricky));
        var (round, roundProblem) = RegexCommandLine.Parse(args);
        Check(roundProblem == null && round!.Pattern == tricky.Pattern && round.Replacement == tricky.Replacement && round.Folder == tricky.Folder && round.Globs == tricky.Globs && round.Flags.Singleline, "arguments written by ToArguments are read back exactly by Windows rules, including quotes, trailing backslashes and spaces");
    }

    // ----- library and my-patterns filters -----

    static void NestedSearch()
    {
        var library = new List<RegexSavedPattern>
        {
            new("Image id", @"^[a-f0-9]{12}$", new RegexBuilderFlags(), DateTimeOffset.UnixEpoch) { Description = "Short image id" },
            new("Port", @"\d{2,5}/tcp", new RegexBuilderFlags(), DateTimeOffset.UnixEpoch),
            new("Tag", @"[\w.-]+", new RegexBuilderFlags(), DateTimeOffset.UnixEpoch) { Description = "latest 1.2" },
        };
        var empty = new RegexSearchQuery("", false, new RegexBuilderFlags());
        Check(RegexPatternStore.Search(library, empty).Count == 3 && RegexPatternStore.SearchSnippets(empty).Count == RegexBuilderLibrary.Snippets.Count, "an empty filter lists everything in both lists");
        Check(RegexPatternStore.Search(library, new RegexSearchQuery("IMAGE", false, new RegexBuilderFlags())).Select(p => p.Name).SequenceEqual(["Image id"]), "a plain text filter still ignores case by default");
        Check(RegexPatternStore.Search(library, new RegexSearchQuery("IMAGE", false, new RegexBuilderFlags { IgnoreCase = false })).Count == 0, "the filter's own IgnoreCase flag applies to plain text");
        var regex = new RegexSearchQuery("^(port|tag)$", true, new RegexBuilderFlags());
        Check(RegexPatternStore.Search(library, regex).Select(p => p.Name).OrderBy(x => x).SequenceEqual(["Port", "Tag"]), "a regular expression filter anchors on a single field, as in every other list");
        Check(RegexPatternStore.Search(library, new RegexSearchQuery("tcp$", true, new RegexBuilderFlags())).Select(p => p.Name).SequenceEqual(["Port"]), "the filter can look inside the pattern text");
        var invalid = new RegexSearchQuery("(", true, new RegexBuilderFlags());
        Check(RegexPatternStore.Search(library, invalid).Count == 0 && !invalid.IsValid && RegexPatternStore.SearchSnippets(invalid).Count == 0, "an invalid filter shows nothing and reports why instead of showing everything");
        var snippetHit = RegexPatternStore.SearchSnippets(new RegexSearchQuery(@"^\w+", true, new RegexBuilderFlags { IgnoreCase = true }));
        Check(snippetHit.Count > 0 && snippetHit.Count <= RegexBuilderLibrary.Snippets.Count, "a regular expression filter works on the built-in snippets");
        Check(RegexPatternStore.Search(library, "image").Count == RegexPatternStore.Search(library, new RegexSearchQuery("image", false, new RegexBuilderFlags())).Count, "the text overload and the query overload agree for plain text");
    }

    // ----- engine versions -----

    static void FlavorVersions()
    {
        foreach (var flavor in RegexFlavors.Flavors)
        {
            if (RegexFlavorVersions.Versions(flavor.Id).Count == 0) throw new Exception("no versions for " + flavor.Id);
            foreach (var construct in RegexFlavors.Constructs)
            {
                var latest = RegexFlavorVersions.Cell(construct.Id, flavor.Id, RegexFlavorVersions.Latest(flavor.Id).Id);
                var table = construct.Flavors[flavor.Id];
                if (latest != table) throw new Exception($"{flavor.Id} {construct.Id}: the newest version differs from the table");
            }
        }
        Check(true, "for every engine and construct the newest version is exactly the flavor table");
        Check(RegexFlavorVersions.HasChoice("python") && RegexFlavorVersions.HasChoice("js") && !RegexFlavorVersions.HasChoice("net") && !RegexFlavorVersions.HasChoice("go"), "only the engines with version-dependent cells offer a choice");
        Check(RegexFlavorVersions.Cell("possessive", "python", "py39") is (FlavorSupport.No, _) && RegexFlavorVersions.Cell("possessive", "python", "py311").Support == FlavorSupport.Yes, "Python before 3.11 has no possessive quantifier and 3.11 has it");
        Check(RegexFlavorVersions.Cell("atomic", "python", "py39").Support == FlavorSupport.No && RegexFlavorVersions.Cell("anchor-z", "python", "py311").Support == FlavorSupport.No && RegexFlavorVersions.Cell("anchor-z", "python", "py314").Support == FlavorSupport.Yes, "Python atomic groups arrive in 3.11 and \\z in 3.14");
        Check(RegexFlavorVersions.Cell("lookbehind", "js", "es2017").Support == FlavorSupport.No && RegexFlavorVersions.Cell("lookbehind", "js", "es2018").Support == FlavorSupport.Yes && RegexFlavorVersions.Cell("flag-s", "js", "es2017").Support == FlavorSupport.No, "JavaScript lookbehind and the s flag arrive in ECMAScript 2018");
        Check(RegexFlavorVersions.Cell("possessive", "python", "unknown-id") == RegexFlavorVersions.Cell("possessive", "python", "py314"), "an unknown version id falls back to the newest version");
        Check(RegexFlavorVersions.Cell("digit", "python", "py39") == RegexFlavors.Construct("digit").Flavors["python"], "a construct with no version rule keeps the table cell at every version");
        var pattern = @"a++(?<=x)b";
        var old = RegexFlavorVersions.Compare(pattern, Cs, ["python", "js"], new Dictionary<string, string> { ["python"] = "py39", ["js"] = "es2017" });
        var current = RegexFlavorVersions.Compare(pattern, Cs, ["python", "js"], null);
        Check(old.Single(v => v.Flavor.Id == "python").Unsupported == current.Single(v => v.Flavor.Id == "python").Unsupported + 1 && old.Single(v => v.Flavor.Id == "js").Unsupported == current.Single(v => v.Flavor.Id == "js").Unsupported + 1, "choosing older versions of Python and JavaScript changes the verdict for a pattern that uses version-gated constructs");
        Check(old.Single(v => v.Flavor.Id == "python").Flavor.Version.Contains("3.9") && current.Single(v => v.Flavor.Id == "python").Flavor.Version.Contains("3.14"), "the verdict names the version that was compared");
        Check(RegexFlavorVersions.Compare("a", Cs, ["net"], null).Single().Flavor.Version == ".NET 10", "an engine without a choice keeps its table version name");
        Check(RegexFlavorVersions.ToText(pattern, Cs, ["python"], new Dictionary<string, string> { ["python"] = "py39" }).Contains("added in Python 3.11") && RegexFlavorVersions.ToText(pattern, Cs, ["python"], new Dictionary<string, string> { ["python"] = "py39" }).Contains("Python 3.9 and 3.10"), "the text table states the version and the reason for each difference");
        Check(RegexFlavorVersions.Dependent("python").Count == 3 && RegexFlavorVersions.Dependent("js").Count >= 5 && RegexFlavorVersions.Dependent("go").Count == 0, "the constructs that depend on a version are listed for the help text");
        Check(RegexFlavors.Compare(pattern, Cs).Select(v => (v.Flavor.Id, v.Unsupported, v.Different)).SequenceEqual(RegexFlavorVersions.Compare(pattern, Cs, null, null).Select(v => (v.Flavor.Id, v.Unsupported, v.Different))), "comparing without choosing a version gives the same counts as the original comparison");
    }
}
