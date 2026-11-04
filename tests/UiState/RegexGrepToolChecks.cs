using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using MaterialDockerImage.UI;

/// <summary>
/// Two checks of the regex builder's link with external search tools. First, the <c>grep -E</c> converter is run against the real GNU grep that ships with Git for Windows (when it is
/// installed) and its matching lines are compared with the .NET engine's on the same ASCII text. When that grep is not installed the check says SKIP in the output and counts nothing,
/// so a missing tool is never mistaken for a pass. Second, the parser for imported tool output (<c>grep -n</c>, <c>grep -nH</c>, ripgrep <c>--vimgrep</c> and <c>--json</c>) is checked on
/// well-formed, malformed and oversized input. Framework-free logic only; nothing here proves the popover's button or dialogs.
/// </summary>
public static class RegexGrepToolChecks
{
    static int count;
    static readonly RegexBuilderFlags Cs = new() { IgnoreCase = false };
    static readonly RegexBuilderFlags Ci = new() { IgnoreCase = true };

    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        count++;
        Console.WriteLine("PASS " + name);
    }

    public static int Run()
    {
        count = 0;
        RealGrep();
        Import();
        return count;
    }

    // ----- the converter against the real grep -----

    static readonly string[] SampleLines =
    [
        "foo bar baz", "foobar", "Foo Bar", "bar", "color colour colr", "ababab", "ab", "xx xxx xxxx x", "invoice 2026-11-04 total $45",
        "user@example.com", "no digits here", "12345", "9 lives", "The cat sat on the mat", "concatenate", "category", "cat", "a.c abc a-c",
        "report.txt", "report.txt.bak", "end of line   ", "  end", "aa bb cc", "hello  world", "tab\tseparated", "UPPER lower Mixed", "snake_case_name",
        "(parenthesised) text", "a|b", "price: $9", "x", "", "last line",
    ];

    static readonly (string Pattern, bool IgnoreCase)[] Table =
    [
        ("foo", false), ("^foo", false), ("bar$", false), ("a+b", false), ("colou?r", false), ("(ab)+", false), ("x{2,3}", false), ("\\d+", false),
        ("\\w+@\\w+", false), ("[A-Z][a-z]+", false), ("\\bcat\\b", false), ("(foo|bar)baz", false), ("[^0-9]+$", false), ("a.c", false),
        ("\\.txt$", false), ("\\s+end", false), ("(\\w)\\1", false), ("[[:alpha:]]+\\d", false), ("\\$\\d+", false), ("\\(par", false),
        ("(?:ab){2}", false), ("(?<w>aa) bb", false), ("a\\|b", false), ("snake_[a-z]+_name", false), ("[a-c]{3}", false), ("^$", false),
        ("^\\d+$", false), ("fo+bar", false), ("FOO", true), ("[a-z]+ [a-z]+ [a-z]+", true), ("^the\\b", true), ("\\Afoo", false), ("cat\\z", false),
    ];

    static string? FindGrep()
    {
        var candidates = new List<string>();
        foreach (var root in new[] { Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetEnvironmentVariable("LocalAppData") is { } l ? Path.Combine(l, "Programs") : null })
            if (!string.IsNullOrEmpty(root)) candidates.Add(Path.Combine(root, "Git", "usr", "bin", "grep.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>The pattern argument of a converted <c>grep -E [-i] 'pattern'</c> command line.</summary>
    static string Unquote(string commandLine)
    {
        var rest = commandLine["grep -E".Length..].TrimStart();
        if (rest.StartsWith("-i ")) rest = rest[3..];
        if (!rest.StartsWith('\'') || !rest.EndsWith('\'')) throw new Exception("unexpected grep -E command line: " + commandLine);
        return rest[1..^1].Replace("'\\''", "'");
    }

    static HashSet<int> RealMatches(string grep, string pattern, bool ignoreCase, string file, out string error)
    {
        var info = new ProcessStartInfo(grep) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("-E"); info.ArgumentList.Add("-n"); if (ignoreCase) info.ArgumentList.Add("-i");
        // The pattern goes in through a pattern file: grep.exe is an MSYS program, and the MSYS runtime re-reads a Windows command line and eats backslashes and braces in it,
        // which would test the command-line quoting instead of the converted pattern.
        var patternFile = file + ".pattern";
        File.WriteAllText(patternFile, pattern + "\n", new UTF8Encoding(false));
        info.ArgumentList.Add("-f"); info.ArgumentList.Add(patternFile); info.ArgumentList.Add(file);
        info.Environment["LC_ALL"] = "C";
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000)) { try { process.Kill(true); } catch (InvalidOperationException) { } throw new Exception("grep did not finish for " + pattern); }
        error = errors.Result.Trim() + (process.ExitCode > 1 ? $" (exit {process.ExitCode})" : "");
        return output.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => int.Parse(l[..l.IndexOf(':')])).ToHashSet();
    }

    static void RealGrep()
    {
        var grep = FindGrep();
        if (grep == null)
        {
            Console.WriteLine("SKIP the grep -E converter was not compared with real GNU grep: Git for Windows' usr\\bin\\grep.exe is not installed. Nothing was verified.");
            return;
        }
        var dir = Path.Combine(Path.GetTempPath(), "regex-grep-real-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "sample.txt");
            File.WriteAllText(file, string.Join("\n", SampleLines) + "\n", new UTF8Encoding(false));
            var disagreements = new List<string>(); var refused = new List<string>(); var usable = 0;
            foreach (var (pattern, ignoreCase) in Table)
            {
                var conversion = RegexConverter.Convert("grep -E", pattern, ignoreCase ? Ci : Cs);
                if (!conversion.Converted) { refused.Add($"{pattern} ({string.Join(" ", conversion.Notes)})"); continue; }
                var converted = Unquote(conversion.Text);
                var real = RealMatches(grep, converted, ignoreCase, file, out var error);
                if (error.Length > 0) { disagreements.Add($"{pattern} -> grep -E '{converted}' printed: {error}"); continue; }
                var options = ignoreCase ? RegexOptions.IgnoreCase | RegexOptions.CultureInvariant : RegexOptions.CultureInvariant;
                var expected = Enumerable.Range(0, SampleLines.Length).Where(i => Regex.IsMatch(SampleLines[i], pattern, options)).Select(i => i + 1).ToHashSet();
                if (!real.SetEquals(expected)) disagreements.Add($"{pattern} -> grep -E '{converted}': grep matched lines [{string.Join(",", real.Order())}], .NET matched [{string.Join(",", expected.Order())}]");
                else usable++;
            }
            Check(Table.Length >= 15, "the grep comparison table has at least 15 patterns");
            Check(refused.Count == 0, "every pattern of the comparison table converts to grep -E" + (refused.Count == 0 ? "" : ": refused " + string.Join("; ", refused)));
            Check(disagreements.Count == 0, $"real GNU grep -E and .NET match the same lines for all {Table.Length} converted patterns ({usable} agreed)" + (disagreements.Count == 0 ? "" : ": " + string.Join(" | ", disagreements)));

            // A conversion that the converter labels lossy must still be honest about it: \d is rewritten, and the note says so.
            var digits = RegexConverter.Convert("grep -E", "\\d+", Cs);
            Check(digits.Converted && digits.Lossy && digits.Notes.Any(n => n.Contains("ASCII")), "a lossy rewrite is labelled as lossy with its reason");
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    // ----- importing tool output -----

    static void Import()
    {
        var grepNH = RegexGrepToolImport.Parse("src/a.cs:12:var x = 1;\nsrc/b.cs:3:    return x;\n", RegexGrepToolImport.GrepNH, @"C:\proj");
        Check(grepNH.Ok && grepNH.Matches.Count == 2 && grepNH.Matches[0].Line == 12 && grepNH.Matches[0].LineText == "var x = 1;" && grepNH.Matches[0].Column == 1
              && Path.GetFileName(grepNH.Matches[1].File) == "b.cs" && Path.IsPathRooted(grepNH.Matches[1].File), "grep -nH lines become results with the file made absolute against the folder");

        var plain = RegexGrepToolImport.Parse("7:alpha\n9:beta: gamma\n", RegexGrepToolImport.GrepN, null, "notes.txt");
        Check(plain.Ok && plain.Matches.Count == 2 && plain.Matches.All(m => m.File == "notes.txt") && plain.Matches[1].LineText == "beta: gamma" && plain.Matches[1].Line == 9, "grep -n lines have no file name, so the given one is used and colons in the text stay in the text");
        var anonymous = RegexGrepToolImport.Parse("7:alpha\n", RegexGrepToolImport.GrepN);
        Check(anonymous.Ok && anonymous.Matches[0].File == RegexGrepToolImport.NoFile, "grep -n output pasted without a file name is attributed to standard input");

        var vim = RegexGrepToolImport.Parse("lib/x.rs:10:5:fn main() {\nlib/y.rs:2:14:let a = 1;\n", RegexGrepToolImport.Vimgrep);
        Check(vim.Ok && vim.Matches.Count == 2 && vim.Matches[0].Column == 5 && vim.Matches[1].Column == 14 && vim.Matches[1].LineText == "let a = 1;", "ripgrep --vimgrep lines carry their column");

        var drive = RegexGrepToolImport.Parse("C:\\x\\y.txt:3:1:text\nD:/data/z.log:40:2:more:text with colons\n", RegexGrepToolImport.Vimgrep);
        Check(drive.Ok && drive.Matches.Count == 2 && drive.Matches[0].File == @"C:\x\y.txt" && drive.Matches[0].Line == 3 && drive.Matches[0].Column == 1 && drive.Matches[0].LineText == "text"
              && drive.Matches[1].File == "D:/data/z.log" && drive.Matches[1].LineText == "more:text with colons", "Windows drive-letter paths keep their drive and the text keeps its colons");
        var driveH = RegexGrepToolImport.Parse("C:\\x\\y.txt:3:text here\n", RegexGrepToolImport.GrepNH);
        Check(driveH.Ok && driveH.Matches[0].File == @"C:\x\y.txt" && driveH.Matches[0].Line == 3 && driveH.Matches[0].LineText == "text here", "a drive-letter path also works for grep -nH");

        var json = string.Join("\n",
            "{\"type\":\"begin\",\"data\":{\"path\":{\"text\":\"C:\\\\p\\\\a.txt\"}}}",
            "{\"type\":\"match\",\"data\":{\"path\":{\"text\":\"C:\\\\p\\\\a.txt\"},\"lines\":{\"text\":\"h\u00e9llo needle and needle\\n\"},\"line_number\":4,\"absolute_offset\":0,\"submatches\":[{\"match\":{\"text\":\"needle\"},\"start\":7,\"end\":13},{\"match\":{\"text\":\"needle\"},\"start\":18,\"end\":24}]}}",
            "{\"type\":\"end\",\"data\":{\"path\":{\"text\":\"C:\\\\p\\\\a.txt\"}}}",
            "{\"type\":\"summary\",\"data\":{\"elapsed_total\":{\"secs\":0}}}");
        var parsedJson = RegexGrepToolImport.Parse(json, RegexGrepToolImport.Json);
        Check(parsedJson.Ok && parsedJson.Matches.Count == 2 && parsedJson.Malformed == 0 && parsedJson.Matches[0].File == @"C:\p\a.txt" && parsedJson.Matches[0].Line == 4
              && parsedJson.Matches[0].Column == 7 && parsedJson.Matches[1].Column == 18 && parsedJson.Matches[0].Length == 6 && parsedJson.Matches[0].Text == "needle" && parsedJson.Matches[0].LineText == "h\u00e9llo needle and needle",
              "ripgrep --json gives one result per submatch with columns in characters (not bytes) and ignores begin, end and summary messages");
        Check(RegexGrepToolImport.Parse(json).Format == RegexGrepToolImport.Json, "output that starts with a JSON object is detected as ripgrep --json");
        var bytesJson = RegexGrepToolImport.Parse("{\"type\":\"match\",\"data\":{\"path\":{\"bytes\":\"YS50eHQ=\"},\"lines\":{\"bytes\":\"bmVlZGxlCg==\"},\"line_number\":1,\"submatches\":[{\"match\":{\"text\":\"needle\"},\"start\":0,\"end\":6}]}}", RegexGrepToolImport.Json);
        Check(bytesJson.Ok && bytesJson.Matches[0].File == "a.txt" && bytesJson.Matches[0].LineText == "needle", "ripgrep's base64 form for non-text paths and lines is decoded");

        Check(RegexGrepToolImport.Parse("lib/x.rs:10:5:fn main() {\nlib/y.rs:2:14:x\n").Format == RegexGrepToolImport.Vimgrep, "auto-detect reads file:line:column:text as ripgrep --vimgrep");
        Check(RegexGrepToolImport.Parse("src/a.cs:12:var x\nsrc/b.cs:3:y\n").Format == RegexGrepToolImport.GrepNH, "auto-detect reads file:line:text as grep -nH");
        Check(RegexGrepToolImport.Parse("12:var x\n3:y\n").Format == RegexGrepToolImport.GrepN, "auto-detect reads line:text as grep -n");

        var mixed = RegexGrepToolImport.Parse("a.txt:1:ok\nthis line is not tool output\n--\nb.txt:x:bad\n\nb.txt:2:fine\n", RegexGrepToolImport.GrepNH);
        Check(mixed.Ok && mixed.Matches.Count == 2 && mixed.Malformed == 2 && mixed.MalformedSamples.Count == 2 && mixed.MalformedSamples[0].StartsWith("line 2") && mixed.Summary().Contains("2 lines could not be read"),
              "malformed lines are counted and named, the separator and blank lines are ignored, and good lines still import");
        var allBad = RegexGrepToolImport.Parse("nothing useful\nat all\n", RegexGrepToolImport.Vimgrep);
        Check(!allBad.Ok && allBad.Matches.Count == 0 && allBad.Problem!.Contains("No line could be read"), "output in which no line fits the format is refused with a reason");
        Check(!RegexGrepToolImport.Parse("", RegexGrepToolImport.Auto).Ok && !RegexGrepToolImport.Parse(null).Ok && !RegexGrepToolImport.Parse("   \n \n").Ok, "empty or blank output is refused");
        Check(!RegexGrepToolImport.Parse("a:1:x", "ripgrep --made-up").Ok, "an unknown format name is refused");
        var badJson = RegexGrepToolImport.Parse("{not json\n{\"type\":\"match\",\"data\":{\"path\":{\"text\":\"a\"},\"lines\":{\"text\":\"l\\n\"},\"line_number\":0,\"submatches\":[]}}\n[1]\n", RegexGrepToolImport.Json);
        Check(!badJson.Ok && badJson.Malformed == 3, "invalid JSON, a zero line number and a non-object are each rejected");
        var outside = RegexGrepToolImport.Parse("{\"type\":\"match\",\"data\":{\"path\":{\"text\":\"a\"},\"lines\":{\"text\":\"abc\\n\"},\"line_number\":1,\"submatches\":[{\"match\":{\"text\":\"zzzz\"},\"start\":2,\"end\":99}]}}", RegexGrepToolImport.Json);
        Check(!outside.Ok && outside.Malformed == 1, "a submatch that lies outside its line is rejected, not clamped");
        var weirdName = RegexGrepToolImport.Parse("bad|name.txt:1:x\nnul\0.txt:1:x\ngood.txt:1:x\n", RegexGrepToolImport.GrepNH);
        Check(weirdName.Ok && weirdName.Matches.Count == 1 && weirdName.Malformed == 2, "file names with characters Windows paths cannot hold are rejected");
        var zero = RegexGrepToolImport.Parse("a.txt:0:x\na.txt:1:0:x\na.txt:99999999999:x\n", RegexGrepToolImport.GrepNH);
        Check(!zero.Ok || zero.Matches.All(m => m.Line >= 1), "line number zero or one too large to read never becomes a result");

        var big = new string('x', RegexGrepToolImport.MaxChars + 1);
        Check(!RegexGrepToolImport.Parse(big).Ok && RegexGrepToolImport.Parse(big).Problem!.Contains("16 MiB"), "output larger than 16 MiB is not read");
        var manyLines = string.Concat(Enumerable.Repeat("1:x\n", RegexGrepToolImport.MaxLines + 1));
        Check(!RegexGrepToolImport.Parse(manyLines, RegexGrepToolImport.GrepN).Ok && RegexGrepToolImport.Parse(manyLines, RegexGrepToolImport.GrepN).Problem!.Contains("lines"), "output with more lines than the limit is not read");
        var longLine = RegexGrepToolImport.Parse("a.txt:1:" + new string('y', RegexGrepToolImport.MaxLineChars) + "\na.txt:2:short\n", RegexGrepToolImport.GrepNH);
        Check(longLine.Ok && longLine.Matches.Count == 1 && longLine.Malformed == 1, "a single over-long line is rejected without stopping the import");
        var flood = string.Join("\n", Enumerable.Range(1, RegexGrepResults.MaxSavedMatches + 50).Select(i => "f.txt:" + i + ":t"));
        var capped = RegexGrepToolImport.Parse(flood, RegexGrepToolImport.GrepNH);
        Check(capped.Ok && capped.Truncated && capped.Matches.Count == RegexGrepResults.MaxSavedMatches && capped.Summary().Contains("cut at"), "more results than the list limit are cut and the cut is stated");

        // The imported list is the same record the grid, Open selected result and Save results already use, so a round trip through them must keep it.
        var saved = RegexGrepToolImport.ToSavedList(vim, "C:\\proj", new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero));
        var result = new RegexGrepResult(true, null, saved.Matches, 2, 2, [], false, false, false, false, false, 0);
        var written = RegexGrepResults.Export("JSON (can be loaded again)", saved.Pattern, saved.Flags, new RegexGrepOptions { Folder = saved.Folder }, result, saved.Saved);
        var back = RegexGrepResults.Parse(written);
        Check(back.Matches.Count == 2 && back.Matches[1].File == "lib/y.rs" && back.Matches[1].Column == 14 && back.Pattern.Contains("ripgrep --vimgrep"), "an imported list survives Save results and Load results unchanged");
        Check(RegexGrepResults.ToCsv(saved.Matches).Contains("lib/x.rs,10,5"), "an imported list exports as CSV");
        var opened = RegexGrepResults.EditorArguments("code.exe", saved.Matches[0].File, saved.Matches[0].Line, saved.Matches[0].Column);
        Check(opened.PositionKnown && opened.Arguments[1] == "lib/x.rs:10:5", "an imported result opens in the editor at its line and column");
    }
}
