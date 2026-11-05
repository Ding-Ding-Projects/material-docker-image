using System.Text;
using MaterialDockerImage.UI;

/// <summary>
/// Checks of the headless folder search (<c>--regex-run</c>): argument parsing, the exit codes, the results file reading back through Load results, refusal of an invalid
/// action, no writes without <c>--regex-apply</c>, apply with an undo snapshot that restores the bytes, and the action arriving on standard input. Temporary folders only; the
/// application window and the process are not involved (the built executable is run separately).
/// </summary>
public static class RegexAutomationChecks
{
    static int count;

    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        count++;
        Console.WriteLine("PASS " + name);
    }

    sealed record Outcome(int Code, string Out, string Err);

    static Outcome Run(RegexAutomationRequest request, string undoRoot, string stdin = "")
    {
        var o = new StringWriter(); var e = new StringWriter();
        var code = RegexBuilderAutomation.Run(request, new StringReader(stdin), o, e, undoRoot);
        return new Outcome(code, o.ToString(), e.ToString());
    }

    static string Temp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "regex-automation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    static string ActionJson(string folder, string pattern, string replacement = "") =>
        new RegexSearchAction { Pattern = pattern, Folder = folder, Globs = "*.txt", Replacement = replacement, Flags = new RegexBuilderFlags { IgnoreCase = false } }.Serialize();

    public static int Run()
    {
        count = 0;
        var root = Temp();
        try
        {
            var data = Path.Combine(root, "data"); Directory.CreateDirectory(data);
            var a = Path.Combine(data, "a.txt"); var b = Path.Combine(data, "b.txt");
            File.WriteAllText(a, "alpha foo beta\nfoo again\n");
            File.WriteAllText(b, "nothing here\n");
            var undo = Path.Combine(root, "undo");
            var actionFile = Path.Combine(root, "action.json");
            var results = Path.Combine(root, "out", "results.json");

            // ----- argument parsing -----
            Check(!RegexBuilderAutomation.Requested(["--regex-pattern", "x"]) && RegexBuilderAutomation.Requested(["--regex-run", "a.json"]), "headless mode is requested only by --regex-run");
            Check(RegexBuilderAutomation.Parse(["--regex-run", "a.json"]).Problem is { } p1 && p1.Contains("--regex-output"), "missing --regex-output is refused");
            Check(RegexBuilderAutomation.Parse(["--regex-run", "--regex-output", "r.json"]).Problem is { } p2 && p2.Contains("--regex-run needs a value"), "--regex-run without a value is refused");
            Check(RegexBuilderAutomation.Parse(["--regex-run", "a", "--regex-output", "r", "--regex-pattern", "x"]).Problem != null, "builder start-up options cannot be mixed with a headless run");
            Check(RegexBuilderAutomation.Parse(["--regex-run", "a", "--regex-run", "b", "--regex-output", "r"]).Problem != null, "a repeated --regex-run is refused");
            var (dash, dashProblem) = RegexBuilderAutomation.Parse(["--regex-run", "-", "--regex-output", results, "--regex-apply"]);
            Check(dashProblem == null && dash!.FromStandardInput && dash.Apply && dash.OutputPath == Path.GetFullPath(results), "dash means standard input and --regex-apply is read");
            Check(RegexBuilderAutomation.Parse(["--regex-run", "a", "--regex-output", root]).Problem != null, "an output path that is a folder is refused");

            // ----- matches: exit 0, results read back through Load results, nothing else written -----
            File.WriteAllText(actionFile, ActionJson(data, "foo"));
            var before = (File.ReadAllText(a), File.ReadAllText(b));
            var r1 = Run(new RegexAutomationRequest(actionFile, results, false), undo);
            Check(r1.Code == RegexBuilderAutomation.ExitMatches && r1.Out.Contains("2 matches in 1 of 2 files"), "matches give exit code 0 and a summary");
            var saved = RegexGrepResults.Parse(File.ReadAllText(results));
            Check(saved.Pattern == "foo" && saved.Matches.Count == 2 && saved.Matches.All(m => m.File == a && m.Text == "foo") && saved.Matches[0].Line == 1 && saved.Matches[0].Column == 7 && saved.Matches[1].Line == 2, "the results file loads back with the same matches");
            Check(saved.Folder == data && saved.Globs == "*.txt", "the results file records the folder and file patterns");
            Check((File.ReadAllText(a), File.ReadAllText(b)) == before && !Directory.Exists(undo) && !File.Exists(results + ".tmp"), "a search without --regex-apply writes nothing but the output file");

            // ----- same results as the window's own search -----
            var direct = RegexGrep.Search("foo", new RegexBuilderFlags { IgnoreCase = false }, new RegexSearchAction { Folder = data, Globs = "*.txt" }.ToGrepOptions());
            Check(direct.Matches.Count == saved.Matches.Count && direct.Matches.Zip(saved.Matches).All(t => t.First.File == t.Second.File && t.First.Line == t.Second.Line && t.First.Column == t.Second.Column), "headless matches equal the built-in folder search");

            // ----- no matches: exit 1 and an empty results file -----
            File.WriteAllText(actionFile, ActionJson(data, "zzz-not-there"));
            var r2 = Run(new RegexAutomationRequest(actionFile, results, false), undo);
            Check(r2.Code == RegexBuilderAutomation.ExitNoMatches && RegexGrepResults.Parse(File.ReadAllText(results)).Matches.Count == 0, "no match gives exit code 1 and an empty, loadable results file");

            // ----- invalid actions: exit 2, no output written -----
            File.Delete(results);
            File.WriteAllText(actionFile, "{ not json");
            var r3 = Run(new RegexAutomationRequest(actionFile, results, false), undo);
            Check(r3.Code == RegexBuilderAutomation.ExitError && r3.Err.Contains("Invalid action") && !File.Exists(results), "a malformed action file gives exit code 2 and writes no results");
            File.WriteAllText(actionFile, ActionJson(data, "(unclosed"));
            Check(Run(new RegexAutomationRequest(actionFile, results, false), undo).Code == RegexBuilderAutomation.ExitError && !File.Exists(results), "an action whose pattern does not compile gives exit code 2");
            File.WriteAllText(actionFile, ActionJson(Path.Combine(root, "missing-folder"), "foo"));
            Check(Run(new RegexAutomationRequest(actionFile, results, false), undo).Code == RegexBuilderAutomation.ExitError && !File.Exists(results), "a folder that does not exist gives exit code 2");
            Check(Run(new RegexAutomationRequest(Path.Combine(root, "no-such-action.json"), results, false), undo).Code == RegexBuilderAutomation.ExitError, "a missing action file gives exit code 2");
            File.WriteAllText(actionFile, ActionJson(data, "foo").Replace("\"version\": 1", "\"version\": 7"));
            Check(Run(new RegexAutomationRequest(actionFile, results, false), undo).Code == RegexBuilderAutomation.ExitError, "an unsupported action version gives exit code 2");

            // ----- --regex-apply without a replacement changes nothing -----
            File.WriteAllText(actionFile, ActionJson(data, "foo"));
            var r4 = Run(new RegexAutomationRequest(actionFile, results, true), undo);
            Check(r4.Code == RegexBuilderAutomation.ExitMatches && r4.Out.Contains("no replacement") && File.ReadAllText(a) == before.Item1 && !Directory.Exists(undo), "--regex-apply with no replacement in the action writes nothing");

            // ----- a replacement is dry-run only unless --regex-apply is given -----
            File.WriteAllText(actionFile, ActionJson(data, "foo", "BAR"));
            Run(new RegexAutomationRequest(actionFile, results, false), undo);
            Check(File.ReadAllText(a) == before.Item1 && !Directory.Exists(undo), "an action with a replacement still writes nothing without --regex-apply");

            // ----- the output file may not sit inside the folder that would be changed -----
            var inside = Path.Combine(data, "results.json");
            var r5 = Run(new RegexAutomationRequest(actionFile, inside, true), undo);
            Check(r5.Code == RegexBuilderAutomation.ExitError && r5.Err.Contains("inside the folder") && File.ReadAllText(a) == before.Item1 && !File.Exists(inside), "--regex-apply refuses an output file inside the changed folder");

            // ----- apply: replacement written, snapshot printed, undo restores the bytes -----
            var r6 = Run(new RegexAutomationRequest(actionFile, results, true), undo);
            var snapLine = r6.Out.Split('\n').FirstOrDefault(l => l.StartsWith("Snapshot: "))?.Trim();
            Check(r6.Code == RegexBuilderAutomation.ExitMatches && File.ReadAllText(a) == "alpha BAR beta\nBAR again\n" && File.ReadAllText(b) == before.Item2, "--regex-apply writes the replacement and leaves other files alone");
            Check(snapLine != null && RegexGrep.ListSnapshots(undo).Any(s => s.Id == snapLine["Snapshot: ".Length..] && s.State == "applied" && s.Replacements == 2), "the snapshot id is printed and names an applied undo snapshot");
            Check(RegexGrepResults.Parse(File.ReadAllText(results)).Matches.Count == 2, "the results file keeps the matches found before the replacement");
            var undone = RegexGrep.Undo(undo, snapLine!["Snapshot: ".Length..]);
            Check(undone.Success && undone.FilesRestored == 1 && File.ReadAllText(a) == before.Item1, "the undo snapshot restores the original bytes");

            // ----- a replacement that matches nothing is not an error -----
            File.WriteAllText(actionFile, ActionJson(data, "zzz-not-there", "Q"));
            var r7 = Run(new RegexAutomationRequest(actionFile, results, true), undo);
            Check(r7.Code == RegexBuilderAutomation.ExitNoMatches && r7.Out.Contains("Nothing to replace"), "apply with no match gives exit code 1 and writes nothing");

            // ----- standard input -----
            var viaInput = Run(new RegexAutomationRequest("-", results, false), undo, ActionJson(data, "again"));
            Check(viaInput.Code == RegexBuilderAutomation.ExitMatches && RegexGrepResults.Parse(File.ReadAllText(results)).Matches.Single().Line == 2, "the action can arrive on standard input");
            var badInput = Run(new RegexAutomationRequest("-", results, false), undo, "[]");
            Check(badInput.Code == RegexBuilderAutomation.ExitError && badInput.Err.Contains("Invalid action"), "an invalid action on standard input gives exit code 2");
            var huge = Run(new RegexAutomationRequest("-", results, false), undo, new string(' ', (int)RegexSearchAction.MaxFileBytes + 10) + ActionJson(data, "foo"));
            Check(huge.Code == RegexBuilderAutomation.ExitError, "an oversized action on standard input is refused");
            var applyInput = Run(new RegexAutomationRequest("-", results, true), undo, ActionJson(data, "again", "AGAIN"));
            Check(applyInput.Code == RegexBuilderAutomation.ExitMatches && File.ReadAllText(a).Contains("AGAIN") && applyInput.Out.Contains("Snapshot: "), "standard input can carry a replacement that --regex-apply writes");

            // ----- atomic write: an existing results file is replaced whole -----
            File.WriteAllText(results, "stale");
            RegexBuilderAutomation.WriteAtomic(results, "{\"ok\":1}");
            Check(File.ReadAllText(results) == "{\"ok\":1}" && !File.Exists(results + ".tmp"), "the output is written atomically over an existing file");
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
        return count;
    }
}
