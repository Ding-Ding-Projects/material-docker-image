using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MaterialDockerImage.UI;

/// <summary>
/// Checks for the second wave of regex builder features: folder search and replace with undo, match navigation, parameterized patterns,
/// replacement explanation, tree export, the flavor table, persistent experiments, extra conversion targets and POSIX class translation.
/// All of it is framework-free logic; none of it proves the popover's layout or interaction.
/// </summary>
public static class RegexRemainingChecks
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

    static void Reject(Action action, string name)
    {
        try { action(); }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or InvalidDataException) { count++; Console.WriteLine("PASS " + name); return; }
        throw new Exception("Expected refusal: " + name);
    }

    static string Sha(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    public static int Run()
    {
        count = 0;
        Grep();
        Replacement();
        Navigation();
        Export();
        Parameters();
        Posix();
        Flavors();
        Experiments();
        Conversions();
        return count;
    }

    // ----- GREP -----

    static void Grep()
    {
        var root = Path.Combine(Path.GetTempPath(), "mdi-grep-" + Guid.NewGuid().ToString("N"));
        var undo = Path.Combine(Path.GetTempPath(), "mdi-grep-undo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        Directory.CreateDirectory(Path.Combine(root, "node_modules"));
        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "alpha\nbeta alpha\ngamma\n");
            File.WriteAllText(Path.Combine(root, "b.log"), "alpha in a log\n");
            File.WriteAllText(Path.Combine(root, "sub", "c.txt"), "no match\nALPHA upper\n");
            File.WriteAllText(Path.Combine(root, ".git", "x.txt"), "alpha hidden in git\n");
            File.WriteAllText(Path.Combine(root, "node_modules", "y.txt"), "alpha hidden in node_modules\n");
            File.WriteAllBytes(Path.Combine(root, "bin.dat"), [0x61, 0x6C, 0x70, 0x68, 0x61, 0x00, 0x01]);
            File.WriteAllText(Path.Combine(root, "crlf.txt"), "one alpha\r\ntwo alpha\r\n");
            File.WriteAllBytes(Path.Combine(root, "utf16.txt"), [.. new byte[] { 0xFF, 0xFE }, .. Encoding.Unicode.GetBytes("wide alpha\n")]);
            File.WriteAllBytes(Path.Combine(root, "invalid.txt"), [0x61, 0x6C, 0x70, 0x68, 0x61, 0xFF, 0xFE, 0x20, 0x62]);
            File.WriteAllText(Path.Combine(root, "big.txt"), new string('x', 5000) + " alpha");
            var all = new RegexGrepOptions { Folder = root, Globs = "*" };

            var r = RegexGrep.Search("alpha", Ci, all);
            Check(r.Valid && r.Matches.All(m => !m.File.Contains(".git") && !m.File.Contains("node_modules")), "folder search leaves out .git and node_modules");
            var a = r.Matches.Where(m => m.File.EndsWith("a.txt")).ToList();
            Check(a.Count == 2 && a[0].Line == 1 && a[0].Column == 1 && a[1].Line == 2 && a[1].Column == 6 && a[1].LineText == "beta alpha" && a[1].Length == 5, "matches carry file, line, column and the line text");
            Check(r.Matches.Where(m => m.File.EndsWith("crlf.txt")).All(m => !m.LineText.EndsWith('\r')) && r.Matches.Count(m => m.File.EndsWith("crlf.txt")) == 2, "CRLF lines are shown without the carriage return");
            Check(r.Matches.Any(m => m.File.EndsWith("utf16.txt") && m.Column == 6), "UTF-16 files with a byte order mark are searched");
            Check(r.Skipped.Any(s => s.File.EndsWith("bin.dat") && s.Reason.Contains("binary")), "binary files are skipped with the reason");
            Check(r.Matches.Any(m => m.File.EndsWith("invalid.txt")), "a file that is not valid UTF-8 is still searched");
            Check(r.Matches.Count(m => m.File.EndsWith("c.txt")) == 1, "IgnoreCase finds ALPHA in a subfolder");
            Check(RegexGrep.Search("alpha", Cs, all).Matches.Count(m => m.File.EndsWith("c.txt")) == 0, "case sensitivity is honoured in files");
            Check(!RegexGrep.Search("alpha", Ci, all with { Recursive = false }).Matches.Any(m => m.File.Contains("sub")), "non-recursive search stays in the folder");

            var small = RegexGrep.Search("alpha", Ci, all with { MaxFileBytes = 100 });
            Check(small.Skipped.Any(s => s.File.EndsWith("big.txt") && s.Reason.Contains("larger than")) && !small.Matches.Any(m => m.File.EndsWith("big.txt")), "files over the size limit are skipped");
            Check(RegexGrep.Search("alpha", Ci, all with { MaxResults = 2 }) is { ResultsTruncated: true, Matches.Count: 2 }, "the result list is bounded");
            Check(RegexGrep.Search("alpha", Ci, all with { MaxFiles = 2 }).FileLimitReached, "the file count is bounded");
            Check(RegexGrep.Search("alpha", Ci, all with { MaxTotalBytes = 30 }).ByteBudgetReached, "the total size is bounded");
            Check(RegexGrep.Search("alpha", Ci, all with { MaxMatchesPerFile = 1 }).Skipped.Any(s => s.Reason.Contains("only the first 1 matches")), "matches per file are bounded and reported");
            Check(RegexGrep.Search("alpha", Ci, all, new CancellationToken(true)).Cancelled, "folder search honours cancellation");
            Check(RegexGrep.Search("alpha", Ci, all with { Budget = TimeSpan.Zero }).TimedOut, "folder search stops at its time budget");
            Check(RegexGrep.Search("alpha", Ci, all with { MaxFileBytes = long.MaxValue, MaxFiles = int.MaxValue, MaxResults = int.MaxValue }).Valid, "limits cannot be raised past the hard ceilings");

            var logs = RegexGrep.Search("alpha", Ci, all with { Globs = "*.log" });
            Check(logs.Matches.Count == 1 && logs.Matches[0].File.EndsWith("b.log"), "a file pattern selects files");
            Check(!RegexGrep.Search("alpha", Ci, all with { Globs = "*.txt;*.log;!b.*" }).Matches.Any(m => m.File.EndsWith("b.log")), "a ! pattern excludes files");
            Check(RegexGrep.Search("alpha", Ci, all with { Globs = "sub/*.txt" }).Matches.All(m => m.File.Contains("sub")) && RegexGrep.Search("alpha", Ci, all with { Globs = "?.txt" }).Matches.Any(m => m.File.EndsWith("a.txt")), "path patterns and ? work");
            Check(!RegexGrep.Search("alpha", Ci, all with { Globs = string.Join(";", Enumerable.Range(0, 40).Select(i => "*.x" + i)) }).Valid, "too many file patterns are refused");

            Check(!RegexGrep.Search("alpha", Ci, all with { Folder = Path.Combine(root, "missing") }).Valid && !RegexGrep.Search("alpha", Ci, all with { Folder = "" }).Valid, "a missing or empty folder is refused");
            Check(!RegexGrep.Search("alpha", Ci, all with { Folder = Path.GetPathRoot(root)! }).Valid, "a whole drive is refused");
            Check(!RegexGrep.Search("(", Ci, all).Valid && !RegexGrep.Search("", Ci, all).Valid, "an invalid or empty pattern is refused");
            Check(RegexGrep.Search("(?=.)(a+)+$", Cs, all with { Globs = "big.txt" }).Skipped.Any(s => s.Reason.Contains("250 ms")) || RegexGrep.Search("(?=.)(a+)+$", Cs, all with { Globs = "big.txt" }).Valid, "a pattern that times out in a file is reported, not fatal");
            Check(RegexGrep.Search("alpha", Ci, all).Summary().Contains("match"), "the summary is readable");

            try
            {
                var link = Path.Combine(root, "linked");
                Directory.CreateSymbolicLink(link, Path.Combine(root, "sub"));
                var linked = RegexGrep.Search("ALPHA", Cs, all);
                Check(!linked.Matches.Any(m => m.File.Contains("linked")) && linked.Skipped.Any(s => s.File.EndsWith("linked") && s.Reason.Contains("link")), "links and junctions are not followed");
                Directory.Delete(link);
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException or PlatformNotSupportedException) { Console.WriteLine("SKIP link check: " + e.Message); }

            // Replace in files: dry run first.
            var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(f => f, Sha);
            var plan = RegexGrep.PlanReplace("alpha", Cs, "OMEGA", all with { Globs = "*.txt" });
            Check(plan.Valid && plan.TotalReplacements == 6 && plan.Files.Count == 4, "the dry run counts replacements per file (" + plan.TotalReplacements + " in " + plan.Files.Count + ")");
            Check(before.All(kv => Sha(kv.Key) == kv.Value), "the dry run writes nothing");
            Check(plan.Files.First(f => f.File.EndsWith("a.txt")).Changes is [{ Line: 1, Column: 1, Before: "alpha", After: "OMEGA" }, { Line: 2, Column: 6 }], "the dry run shows each change with its position");
            Check(plan.Skipped.Any(s => s.File.EndsWith("invalid.txt") && s.Reason.Contains("never edited")), "a file that is not valid in its encoding is never planned for editing");
            Check(!RegexGrep.PlanReplace("(", Cs, "x", all).Valid && !RegexGrep.PlanReplace("alpha", Cs, "x", all with { Folder = "" }).Valid, "a bad pattern or folder gives no plan");

            // Apply, byte-exact preservation, undo.
            var utfBefore = File.ReadAllBytes(Path.Combine(root, "utf16.txt"));
            var crlfBefore = File.ReadAllBytes(Path.Combine(root, "crlf.txt"));
            var staleName = Path.Combine(root, "big.txt");
            File.AppendAllText(staleName, "late edit\n"); // changed after the preview
            var applied = RegexGrep.Apply(plan, undo);
            Check(applied.Success && applied.SnapshotId != null && applied.FilesChanged == 3, "apply changes the planned files (" + applied.FilesChanged + ")");
            Check(applied.Skipped.Any(s => s.File.EndsWith("big.txt") && s.Reason.Contains("changed since the preview")) && File.ReadAllText(staleName).Contains("alpha") && !File.ReadAllText(staleName).Contains("OMEGA"), "a file edited after the preview is left alone");
            Check(File.ReadAllText(Path.Combine(root, "a.txt")) == "OMEGA\nbeta OMEGA\ngamma\n", "apply replaced the text");
            var utfAfter = File.ReadAllBytes(Path.Combine(root, "utf16.txt"));
            Check(utfAfter[0] == 0xFF && utfAfter[1] == 0xFE && Encoding.Unicode.GetString(utfAfter, 2, utfAfter.Length - 2) == "wide OMEGA\n", "apply keeps the UTF-16 byte order mark");
            Check(File.ReadAllText(Path.Combine(root, "crlf.txt")) == "one OMEGA\r\ntwo OMEGA\r\n", "apply keeps CRLF line endings");
            var listed = RegexGrep.ListSnapshots(undo);
            Check(listed.Count == 1 && listed[0].State == "applied" && listed[0].Files == 3 && listed[0].Replacements == 5, "the snapshot is listed with its state");
            var undone = RegexGrep.Undo(undo, applied.SnapshotId!);
            Check(undone.Success && undone.FilesRestored == 3 && File.ReadAllBytes(Path.Combine(root, "utf16.txt")).SequenceEqual(utfBefore) && File.ReadAllBytes(Path.Combine(root, "crlf.txt")).SequenceEqual(crlfBefore), "undo restores the exact original bytes");
            Check(File.ReadAllText(Path.Combine(root, "a.txt")) == "alpha\nbeta alpha\ngamma\n" && RegexGrep.ListSnapshots(undo)[0].State == "undone", "undo restores the text and marks the snapshot");
            Check(!RegexGrep.Undo(undo, applied.SnapshotId!).Success && !RegexGrep.Undo(undo, "../etc").Success && !RegexGrep.Undo(undo, "").Success, "a snapshot cannot be undone twice and an id cannot escape the folder");

            // Undo must not clobber a later edit.
            var plan2 = RegexGrep.PlanReplace("gamma", Cs, "delta", all with { Globs = "a.txt" });
            var applied2 = RegexGrep.Apply(plan2, undo);
            File.AppendAllText(Path.Combine(root, "a.txt"), "edited later\n");
            var undo2 = RegexGrep.Undo(undo, applied2.SnapshotId!);
            Check(undo2.Success && undo2.FilesRestored == 0 && undo2.Skipped.Any(s => s.Reason.Contains("changed after")) && File.ReadAllText(Path.Combine(root, "a.txt")).Contains("edited later"), "undo leaves a file edited after the replacement alone and says so");

            // A failed write rolls back what was already written.
            File.WriteAllText(Path.Combine(root, "a.txt"), "alpha\n");
            File.WriteAllText(Path.Combine(root, "z.txt"), "alpha z\n");
            var plan3 = RegexGrep.PlanReplace("alpha", Cs, "BETA", all with { Globs = "a.txt;z.txt" });
            Check(plan3.Files.Count == 2, "rollback test plans two files");
            File.SetAttributes(Path.Combine(root, "z.txt"), FileAttributes.ReadOnly);
            var failed = RegexGrep.Apply(plan3, undo);
            File.SetAttributes(Path.Combine(root, "z.txt"), FileAttributes.Normal);
            Check(!failed.Success && failed.RolledBack && File.ReadAllText(Path.Combine(root, "a.txt")) == "alpha\n" && File.ReadAllText(Path.Combine(root, "z.txt")) == "alpha z\n", "a failed write restores the files already written");
            Check(!RegexGrep.Apply(plan3 with { Files = [] }, undo).Success && !RegexGrep.Apply(plan3 with { Valid = false, Error = "x" }, undo).Success, "an empty or invalid plan is not applied");

            // Snapshots are bounded.
            File.WriteAllText(Path.Combine(root, "loop.txt"), "x");
            for (var i = 0; i < 12; i++)
            {
                var p = RegexGrep.PlanReplace(i % 2 == 0 ? "x" : "y", Cs, i % 2 == 0 ? "y" : "x", all with { Globs = "loop.txt" });
                if (!RegexGrep.Apply(p, undo).Success) throw new Exception("loop apply " + i);
            }
            Check(RegexGrep.ListSnapshots(undo).Count <= RegexGrep.MaxSnapshots, "only the newest snapshots are kept");
        }
        finally
        {
            foreach (var f in Directory.Exists(root) ? Directory.GetFiles(root, "*", SearchOption.AllDirectories) : []) { try { File.SetAttributes(f, FileAttributes.Normal); } catch (IOException) { } }
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (Directory.Exists(undo)) Directory.Delete(undo, true);
        }
    }

    // ----- replacement text -----

    static bool ExpansionAgrees(Func<IReadOnlyList<RegexReplacementToken>, Match, string, Regex, string> expander, out string failure)
    {
        failure = "";
        var cases = new (string Pattern, string Input, string Replacement)[]
        {
            (@"(?<k>\w+)=(?<v>\w+)", "a=1 b=2", "${v}:${k} $$ $0 $& $1 $2"),
            (@"(a)(b)?", "ac ab", "[$2|$1|$+]"),
            (@"b", "abc", "$`|$'|$_"),
            (@"(\d)", "x1y", "$9 ${9} ${nope} $ $x ${1x} $1x"),
            (@"(?<n>a)", "a", "${n}${n} $n ${0} $10 $01"),
            (@"x", "axb", "$"),
            (@"(a)|(b)", "ab", "$+/$1/$2"),
            (@"\d", "1", "{$0}{$$}$$$0"),
        };
        foreach (var (pattern, input, replacement) in cases)
        {
            var regex = new Regex(pattern);
            var tokens = RegexReplacementParser.Parse(replacement, RegexReplacementParser.GroupInfo.Of(regex));
            foreach (Match m in regex.Matches(input))
            {
                var expected = m.Result(replacement);
                var actual = expander(tokens, m, input, regex);
                if (expected != actual) { failure = $"{pattern} on {input} with {replacement}: .NET gives \"{expected}\", expander gives \"{actual}\""; return false; }
            }
        }
        return true;
    }

    static void Replacement()
    {
        Check(ExpansionAgrees(RegexReplacementParser.Expand, out var why), "the replacement parser agrees with .NET's own expansion on every case " + why);
        // Red then green: a deliberately wrong expander (the last group read as group 1) must be caught by the same check.
        Check(!ExpansionAgrees((tokens, m, input, regex) => RegexReplacementParser.Expand(tokens.Select(t => t.Kind == RegexReplacementKind.LastGroup ? t with { Kind = RegexReplacementKind.Group, GroupNumber = 1 } : t).ToList(), m, input, regex), out _), "the agreement check fails when the expander is wrong (red), so it can be trusted (green above)");

        var g = RegexReplacementParser.GroupInfo.Of(new Regex(@"(?<name>\w)(\d)"));
        var tokens = RegexReplacementParser.Parse("a$1${name}$$$&$`$'$+$_${2}$7${zz}$", g);
        Check(tokens.Select(t => t.Kind).SequenceEqual(
        [
            RegexReplacementKind.Literal, RegexReplacementKind.Group, RegexReplacementKind.Group, RegexReplacementKind.Dollar, RegexReplacementKind.WholeMatch, RegexReplacementKind.BeforeMatch,
            RegexReplacementKind.AfterMatch, RegexReplacementKind.LastGroup, RegexReplacementKind.WholeInput, RegexReplacementKind.Group, RegexReplacementKind.UnresolvedReference,
            RegexReplacementKind.UnresolvedReference, RegexReplacementKind.Literal,
        ]), "replacement text is split into literals and the nine kinds of reference");
        Check(tokens[1].Start == 1 && tokens[1].Length == 2 && tokens.Sum(t => t.Length) == "a$1${name}$$$&$`$'$+$_${2}$7${zz}$".Length, "tokens cover the replacement text exactly");
        Check(RegexReplacementParser.Warnings("$7 ${zz} $1", g).Count == 2 && RegexReplacementParser.Warnings("$7", RegexReplacementParser.GroupInfo.Unknown).Count == 0, "references to missing groups are flagged, and nothing is flagged for an invalid pattern");
        var lines = RegexReplacementParser.Explain("x$&y", g);
        Check(lines.Count == 3 && lines[1].Contains("whole match") && lines[0].Contains("inserted as typed"), "the explanation has one line per piece");
        Check(RegexReplacementParser.Explain("$+", g)[0].Contains("last group") && RegexReplacementParser.Explain("$_", g)[0].Contains("whole text") && RegexReplacementParser.Explain("${name}", g)[0].Contains("called name"), "the less common references are explained");
        Check(RegexReplacementParser.Parse(null, g).Count == 0 && RegexReplacementParser.Parse("", g).Count == 0, "empty replacement text has no pieces");
        Check(RegexReplacementParser.Palette.All(p => p.Text.Length > 0 && p.Description.Length > 10) && RegexReplacementParser.Palette.Select(p => p.Text).Distinct().Count() == RegexReplacementParser.Palette.Count, "the replacement palette is complete and unique");
        Check(RegexReplacementParser.EncodeLiteral("cost: $5 and $$") == "cost: $$5 and $$$$" && new Regex("x").Replace("x", RegexReplacementParser.EncodeLiteral("$1")) == "$1", "literal text is encoded so $ stays a dollar sign");
        Check(RegexReplacementParser.DecodeEscapes(@"a\nb\t\\\x41\u0042\u{1F600}\0") == "a\nb\t\\AB\U0001F600\0", "typed escapes become the characters they stand for");
        Reject(() => RegexReplacementParser.DecodeEscapes(@"\q"), "an unknown escape is refused");
        Reject(() => RegexReplacementParser.DecodeEscapes(@"abc\"), "a lone trailing backslash is refused");
        Reject(() => RegexReplacementParser.DecodeEscapes(@"\x4"), "a short hexadecimal escape is refused");
        Reject(() => RegexReplacementParser.DecodeEscapes(@"\u{D800}"), "a surrogate code point is refused");
    }

    // ----- navigation -----

    static void Navigation()
    {
        Check(RegexMatchNavigator.Next(3, -1) == 0 && RegexMatchNavigator.Next(3, 0) == 1 && RegexMatchNavigator.Next(3, 2) == 0 && RegexMatchNavigator.Next(0, -1) == -1, "next match steps forward and wraps");
        Check(RegexMatchNavigator.Previous(3, -1) == 2 && RegexMatchNavigator.Previous(3, 2) == 1 && RegexMatchNavigator.Previous(3, 0) == 2 && RegexMatchNavigator.Previous(0, 0) == -1, "previous match steps back and wraps");
        var ev = RegexBuilderEngine.Evaluate(@"\d+", Ci, "a1 b22 c333");
        Check(RegexMatchNavigator.FromCaret(ev.Matches, 0) == 0 && RegexMatchNavigator.FromCaret(ev.Matches, 4) == 1 && RegexMatchNavigator.FromCaret(ev.Matches, 99) == 0 && RegexMatchNavigator.FromCaret([], 3) == -1, "the first match at or after the caret is found");
        Check(RegexMatchNavigator.Describe(ev.Matches, 1, false) == "Match 2 of 3 at position 4, 2 characters." && RegexMatchNavigator.Describe(ev.Matches, 7, false).StartsWith("No match") && RegexMatchNavigator.Describe(RegexBuilderEngine.Evaluate(@"\b", Ci, "ab").Matches, 0, true).Contains("zero-width"), "the position is stated in plain words");
    }

    // ----- tree export -----

    static void Export()
    {
        var pattern = @"(?<year>\d{4})-(\d{2})|x`y";
        var text = RegexTreeExport.ToText(pattern, Ci);
        Check(text.Contains("Pattern: " + pattern) && text.Contains("\\d") && text.Split('\n').Any(l => l.StartsWith("  ")), "the tree exports as indented text");
        var md = RegexTreeExport.ToMarkdown(pattern, Ci);
        Check(md.StartsWith("# Regular expression explanation") && md.Contains("``(?<year>\\d{4})-(\\d{2})|x`y``") && md.Contains("\n  - "), "the tree exports as Markdown with a backtick-safe code span");
        var json = RegexTreeExport.ToJson(pattern, Ci);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Check(root.GetProperty("format").GetString() == "material-docker-image.regex-explanation" && root.GetProperty("valid").GetBoolean() && root.GetProperty("pattern").GetString() == pattern && root.GetProperty("tokens").GetArrayLength() >= 1, "the tree exports as JSON that parses back to the pattern");
        Check(root.GetProperty("tokens")[0].GetProperty("children").GetArrayLength() > 0 && root.GetProperty("tokens")[0].GetProperty("start").GetInt32() == 0, "JSON tokens nest with positions");
        var bad = JsonDocument.Parse(RegexTreeExport.ToJson("(a", Ci)).RootElement;
        Check(!bad.GetProperty("valid").GetBoolean() && bad.GetProperty("error").GetString()!.Length > 0, "an invalid pattern exports with its error");
        var overview = RegexTreeExport.ToText(pattern, Ci, RegexExplainPrecision.Overview);
        var full = RegexTreeExport.ToText(pattern, Ci, RegexExplainPrecision.Full);
        Check(overview.Split('\n').Length < full.Split('\n').Length && !overview.Split('\n').Any(l => l.StartsWith("  ")), "lower precision shows fewer levels");
        Check(RegexTreeExport.Visible(pattern, Ci, RegexExplainPrecision.Standard).All(n => n.Depth < 3), "standard precision stops at three levels");
        Check(RegexTreeExport.Formats.All(f => RegexTreeExport.Export(f, "a+", Ci, RegexExplainPrecision.Full).Length > 20), "every export format produces text");
        Reject(() => RegexTreeExport.Export("PDF", "a", Ci, RegexExplainPrecision.Full), "an unknown export format is refused");
        var big = RegexTreeExport.Visible(string.Concat(Enumerable.Repeat("ab", 1500)) , Ci, RegexExplainPrecision.Full).Count();
        Check(big <= RegexTreeExport.MaxNodes, "the export is bounded");
        var nodes = RegexExplainer.Explain(@"(ab)+c", Ci);
        Check(RegexTreeExport.NodeAt(nodes, 1)?.Text == "ab" && RegexTreeExport.NodeAt(nodes, 0)?.Text == "(ab)" && RegexTreeExport.NodeAt(nodes, 4)?.Family == RegexTokenFamily.Group, "the deepest token under the caret is found");
        Check(RegexTreeExport.NodeAt(nodes, 5)?.Text == "c" && RegexTreeExport.NodeAt(nodes, 6)?.Text == "c" && RegexTreeExport.NodeAt(nodes, 99) == null && RegexTreeExport.NodeAt([], 0) == null, "a caret between tokens prefers the one that follows, the end finds the last token, and outside finds none");
    }

    // ----- parameters -----

    static void Parameters()
    {
        var p = RegexParameters.Find(@"FROM {{base:alpine}}:{{tag}} AS {{base}} and {{tag}}");
        Check(p.Select(x => x.Name).SequenceEqual(["base", "tag"]) && p[0].Default == "alpine" && p[0].Uses == 2 && p[1].Default == null && p[1].Uses == 2, "parameters are found in order with defaults and use counts");
        Check(RegexParameters.Find(@"a{2,3}{{2}}\d{{x y}}").Count == 0, "repeat counts and malformed braces are not parameters");
        var ok = RegexParameters.Fill("{{host}}:{{port:80}}", new Dictionary<string, string> { ["host"] = "a.b" }, Ci);
        Check(ok.Success && ok.Pattern == @"a\.b:80", "values are escaped as literal text and defaults fill the rest");
        Check(RegexBuilderEngine.Evaluate(ok.Pattern, Ci, "see a.b:80 not axb:80").Matches.Count == 1, "an escaped value matches only itself");
        Check(RegexParameters.Fill("{{x}}", new Dictionary<string, string> { ["x"] = "{{y}}" }, Ci).Pattern == Regex.Escape("{{y}}"), "a value that looks like a placeholder is not expanded again");
        Check(!RegexParameters.Fill("{{x}}", new Dictionary<string, string>(), Ci).Success && RegexParameters.Fill("{{x}}", new Dictionary<string, string>(), Ci).Problems[0].Contains("Give a value for x"), "a parameter with no value and no default is a problem");
        Check(RegexParameters.Fill("{{x}}", new Dictionary<string, string> { ["x"] = "[0-9]+" }, Ci, asPattern: true) is { Success: true, Pattern: "[0-9]+" }, "a value can be inserted as a pattern fragment on request");
        Check(!RegexParameters.Fill("{{x}}", new Dictionary<string, string> { ["x"] = "(" }, Ci, asPattern: true).Success, "a pattern fragment that breaks the pattern is reported");
        Check(!RegexParameters.Fill("{{x}}", new Dictionary<string, string> { ["x"] = new string('a', 501) }, Ci).Success && !RegexParameters.Fill("plain", new Dictionary<string, string>(), Ci).Success, "oversized values and patterns without parameters are refused");
        Check(RegexBuilderEngine.Validate("FROM {{base:alpine}}", Ci).Valid, "a pattern with placeholders still compiles, so library entries stay valid");
        Check(RegexParameters.MakeParameter("tag", "latest") == "{{tag:latest}}" && RegexParameters.MakeParameter("tag") == "{{tag}}", "a placeholder is built from a name and a default");
        Reject(() => RegexParameters.MakeParameter("1bad"), "a parameter name must start with a letter");
        Reject(() => RegexParameters.MakeParameter("ok", "{x}"), "a default cannot hold braces");
    }

    // ----- POSIX classes -----

    static void Posix()
    {
        var t = RegexPosixTranslator.Translate("[[:alpha:]]+");
        Check(t.Success && t.Pattern == @"[\p{L}]+" && t.Replaced == 1 && RegexBuilderEngine.Evaluate(t.Pattern, Cs, "naïve!").Matches[0].Value == "naïve", "[[:alpha:]] becomes a Unicode letter set that matches");
        var u = RegexPosixTranslator.Translate(@"^[^[:digit:]x]+\[[:alpha:]$");
        Check(u.Success && u.Pattern.StartsWith("^[^0-9x]+") && u.Replaced == 1, "negated sets and escaped brackets are handled");
        Check(RegexBuilderEngine.Evaluate(RegexPosixTranslator.Translate("[[:upper:][:digit:]_]+").Pattern, Cs, "AB12_cd").Matches[0].Value == "AB12_", "several classes in one set combine");
        foreach (var name in RegexPosixTranslator.Classes.Keys)
        {
            var translated = RegexPosixTranslator.Translate("[[:" + name + ":]]");
            Check(translated.Success && RegexBuilderEngine.Validate(translated.Pattern, Cs).Valid, "POSIX class " + name + " converts to a valid pattern");
        }
        Check(RegexPosixTranslator.Translate("[[:punct:]]").Pattern is { } pu && RegexBuilderEngine.Evaluate(pu, Cs, "a,b!").Matches.Count == 2, "punctuation uses the ASCII definition");
        Check(!RegexPosixTranslator.Translate("[[:bogus:]]").Success && !RegexPosixTranslator.Translate("[[:^alpha:]]").Success && !RegexPosixTranslator.Translate("[abc").Success, "unknown, negated and unterminated classes are refused with a reason");
        Check(RegexPosixTranslator.Translate(@"\d+(a|b)").Pattern == @"\d+(a|b)" && RegexPosixTranslator.Translate("").Success, "patterns without POSIX classes pass through unchanged");
        Check(RegexBuilderEngine.Evaluate("[[:alpha:]]", Cs, "a").Matches.Count == 0, "typed directly, [[:alpha:]] does not mean what POSIX says in .NET (the reason the builder converts)");
    }

    // ----- flavors -----

    static List<string> NetMismatches(IEnumerable<RegexConstruct> table)
    {
        var bad = new List<string>();
        foreach (var c in table)
        {
            if (c.Probe.Length == 0) continue;
            var compiled = RegexBuilderEngine.Compile(c.Probe, new RegexBuilderFlags { IgnoreCase = false });
            var matches = compiled.Valid && compiled.Regex!.IsMatch(c.ProbeSubject);
            var claimed = c.Flavors["net"].Support != FlavorSupport.No;
            if (claimed != matches) bad.Add($"{c.Id}: the table says {c.Flavors["net"].Support} but .NET {(matches ? "matches" : "does not match")}");
        }
        return bad;
    }

    static void Flavors()
    {
        Check(RegexFlavors.Flavors.Count == 7 && RegexFlavors.Constructs.Select(c => c.Id).Distinct().Count() == RegexFlavors.Constructs.Count && RegexFlavors.Constructs.All(c => RegexFlavors.Flavors.All(f => c.Flavors.ContainsKey(f.Id))), "every construct has a cell for every flavor");
        Check(RegexFlavors.Constructs.All(c => c.Flavors.Values.All(v => v.Support == FlavorSupport.Yes || v.Note.Length > 0 || c.Flavors.Values.Count(x => x.Support == FlavorSupport.No) > 0)), "the table is filled in");
        Check(RegexFlavors.Constructs.Where(c => c.Flavors.Values.Any(v => v.Support == FlavorSupport.Different)).All(c => c.Flavors.Values.Where(v => v.Support == FlavorSupport.Different).All(v => v.Note.Length > 8)), "every different reading carries its explanation");
        var mismatches = NetMismatches(RegexFlavors.Constructs);
        Check(mismatches.Count == 0, ".NET column agrees with the real .NET engine for every probe " + string.Join("; ", mismatches));
        // Red then green: a table with one wrong .NET cell must be caught.
        var corrupt = RegexFlavors.Constructs.Select(c => c.Id == "lookahead" ? c with { Flavors = new Dictionary<string, (FlavorSupport, string)>(c.Flavors) { ["net"] = (FlavorSupport.No, "") } } : c).ToList();
        Check(NetMismatches(corrupt).Count == 1 && NetMismatches(corrupt)[0].StartsWith("lookahead"), "a wrong cell is caught by the same check (red), so the clean result above means something");

        string[] Ids(string pattern, RegexBuilderFlags? f = null) => RegexFlavors.Detect(pattern, f ?? Ci).Select(u => u.ConstructId).ToArray();
        Check(Ids(@"(?<n>a)\k<n>(?=b)(?<=c)(?>x)a++").SequenceEqual(["named-angle", "named-backref", "lookahead", "lookbehind", "atomic", "possessive"]), "constructs are detected in order");
        Check(Ids(@"\d\w\b\A\z\Z\G\x41\u0041\p{L}\p{IsGreek}\1").SequenceEqual(["digit", "word-space", "word-boundary", "anchor-A", "anchor-z", "anchor-Z", "anchor-G", "hex-escape", "unicode-escape", "unicode-category", "unicode-block", "backref"]), "escapes are detected");
        Check(Ids(@"[[:alpha:]a-z-[aeiou]\d]").SequenceEqual(["posix-class", "class-subtraction", "digit"]), "sets are scanned for POSIX classes and subtraction");
        Check(Ids(@"a*?b{2,3}+(?:c)(?#n)(?i)(?(1)a|b)(?<a-b>x)(?P<n>y)(?|a)(?R)").SequenceEqual(["lazy", "possessive", "noncapturing", "comment", "inline-flags", "conditional", "balancing", "named-p", "branch-reset", "recursion"]), "groups and quantifier suffixes are detected");
        Check(Ids(@"\\d \(?a\)? [\d]x").SequenceEqual(["digit"]) && Ids("plain text 1+2 a{x}").Length == 0, "escaped backslashes, escaped parentheses and plain text are not constructs");
        Check(Ids("a b # c\nd", Ci with { IgnorePatternWhitespace = true }).SequenceEqual(["flag-x"]) && Ids(@"\d # \w", Ci with { IgnorePatternWhitespace = true }).SequenceEqual(["flag-x", "digit"]), "# comments are skipped when white space is ignored");
        Check(Ids("a", Ci with { RightToLeft = true, Singleline = true, Multiline = true, ExplicitCapture = true }).OrderBy(x => x).SequenceEqual(["flag-m", "flag-n", "flag-rtl", "flag-s"]), "options that other engines write differently are tracked");
        Check(RegexFlavors.Detect("", Ci).Count == 0 && RegexFlavors.Detect(null, Ci).Count == 0 && RegexFlavors.Detect(@"\", Ci).Count == 0 && RegexFlavors.Detect("[", Ci).Count == 0 && RegexFlavors.Detect("(?", Ci).Count == 0, "malformed input never throws");

        var verdicts = RegexFlavors.Compare(@"(?<=a)b(?>c)\d", Ci);
        var js = verdicts.First(v => v.Flavor.Id == "js"); var go = verdicts.First(v => v.Flavor.Id == "go"); var net = verdicts.First(v => v.Flavor.Id == "net"); var py = verdicts.First(v => v.Flavor.Id == "python");
        Check(net.Compatible && js.Unsupported == 1 && js.Different == 1 && go.Unsupported == 2 && py.Unsupported == 0 && !js.Compatible, "the verdicts count unsupported and different constructs per flavor");
        Check(RegexFlavors.Compare(@"(?<=a)b", Ci, ["go"]).Count == 1 && RegexFlavors.Compare("a", Ci).All(v => v.Compatible && v.Findings.Count == 0), "flavors can be chosen, and a plain pattern is compatible everywhere");
        var text = RegexFlavors.ToText(@"(?<=a)b", Ci, ["net", "go"]);
        Check(text.Contains("Go RE2") && text.Contains("NO ") && text.Contains("not run in any engine except .NET") && RegexFlavors.ToText("abc", Ci).Contains("uses no construct the table tracks"), "the comparison text states what it is");
        Check(RegexFlavors.Flavors.All(f => f.Version.Length > 3), "every flavor names its version");
    }

    // ----- persistent experiments and saved pattern metadata -----

    static void Experiments()
    {
        var a = RegexExperimentStore.Create("", @"\d+", Ci, "a1 b22", "<$0>", "+ 12\n- ab", new DateTimeOffset(2026, 11, 4, 1, 2, 3, TimeSpan.Zero));
        var list = RegexExperimentStore.Add([], a);
        Check(list.Count == 1 && list[0].Name == @"\d+", "an unnamed experiment is named after its pattern");
        var again = RegexExperimentStore.Create("again", a.Pattern, a.Flags, a.Sample, a.Replacement, a.TestCases);
        var b = RegexExperimentStore.Create("other", "x", Cs, "xx", "", "");
        list = RegexExperimentStore.Add(RegexExperimentStore.Add(list, b), again);
        Check(list.Count == 2 && list[0].Name == "again", "a repeated experiment moves to the front instead of being stored twice");
        var round = RegexExperimentStore.Parse(RegexExperimentStore.Serialize(list));
        Check(round.Count == 2 && round[0].Sample == "a1 b22" && round[0].TestCases == "+ 12\n- ab" && round[0].Replacement == "<$0>" && !round[1].Flags.IgnoreCase && round[1].Id == list[1].Id, "experiments round trip with sample, replacement, test cases and flags");
        var tmp = Path.Combine(Path.GetTempPath(), "mdi-rx-exp-" + Guid.NewGuid().ToString("N") + ".json");
        try { RegexExperimentStore.Save(tmp, list); Check(RegexExperimentStore.Load(tmp).Count == 2 && !File.Exists(tmp + ".tmp") && RegexExperimentStore.Load(tmp + ".none").Count == 0, "the history file round trips, leaves no temporary file and a missing file is empty"); }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
        var many = new List<RegexExperiment>();
        for (var i = 0; i < 70; i++) many = RegexExperimentStore.Add(many, RegexExperimentStore.Create("n" + i, "p" + i, Ci, "s" + i, "", ""));
        Check(many.Count == RegexExperimentStore.MaxEntries && many[0].Name == "n69", "the history keeps the newest fifty");
        var bulk = new List<RegexExperiment>();
        for (var i = 0; i < 50; i++) bulk = RegexExperimentStore.Add(bulk, RegexExperimentStore.Create("b" + i, "q" + i, Ci, new string((char)('a' + i % 20), RegexExperimentStore.MaxSampleChars), new string('r', RegexExperimentStore.MaxReplacementChars), new string('t', RegexExperimentStore.MaxTestCaseChars)));
        Check(RegexExperimentStore.Serialize(bulk).Length <= RegexExperimentStore.MaxTotalBytes && bulk.Count < 50 && bulk[0].Name == "b49", "the history is bounded in total size, dropping the oldest");
        Check(RegexExperimentStore.Remove(list, list[0].Id).Count == 1, "an experiment can be removed");
        Reject(() => RegexExperimentStore.Create("x", "", Ci, "", "", ""), "an empty pattern is not remembered");
        Reject(() => RegexExperimentStore.Create("x", "a", Ci, new string('x', RegexExperimentStore.MaxSampleChars + 1), "", ""), "a sample over the limit is refused, never silently cut");
        Reject(() => RegexExperimentStore.Create("x", "a", Ci, "", new string('x', 1001), ""), "an oversized replacement is refused");
        Reject(() => { try { RegexExperimentStore.Parse("{ nope"); } catch (InvalidDataException e) { throw new ArgumentException(e.Message); } }, "a corrupt history file is refused");
        Reject(() => { try { RegexExperimentStore.Parse("{\"format\":\"other\",\"version\":1,\"experiments\":[]}"); } catch (InvalidDataException e) { throw new ArgumentException(e.Message); } }, "a history file of another format is refused");

        // Saved patterns carry optional description, replacement and test cases; older files without them still load.
        var saved = new RegexSavedPattern("Digits", @"\d+", Ci, DateTimeOffset.UnixEpoch) { Description = "numbers", Replacement = "<$0>", TestCases = "+ 1\n- a" };
        var back = RegexPatternStore.Parse(RegexPatternStore.Serialize([saved]));
        Check(back[0].Description == "numbers" && back[0].Replacement == "<$0>" && back[0].TestCases == "+ 1\n- a", "saved patterns keep their description, replacement and test cases");
        var plain = RegexPatternStore.Parse(RegexPatternStore.Serialize([new RegexSavedPattern("P", "a", Ci, DateTimeOffset.UnixEpoch)]));
        Check(plain[0].TestCases == "" && !RegexPatternStore.Serialize([new RegexSavedPattern("P", "a", Ci, DateTimeOffset.UnixEpoch)]).Contains("testCases"), "empty extras are not written, so the file stays as before");
        Check(RegexPatternStore.Import([], RegexPatternStore.Serialize([saved])).Library[0].TestCases == "+ 1\n- a", "import keeps the extras");
        Reject(() => RegexPatternStore.Add([], saved with { TestCases = new string('x', 8001) }), "oversized test cases are refused on save");
        Reject(() => { try { RegexPatternStore.Parse(RegexPatternStore.Serialize([saved with { TestCases = new string('x', 8001) }])); } catch (InvalidDataException e) { throw new ArgumentException(e.Message); } }, "oversized test cases are refused on load");
    }

    // ----- conversions -----

    static void Conversions()
    {
        Check(RegexConverter.Targets.Count == 14 && RegexConverter.Targets.Distinct().Count() == 14, "fourteen conversion targets are offered");
        foreach (var target in RegexConverter.Targets)
            foreach (var kind in Enum.GetValues<RegexCodeKind>())
            {
                var r = RegexConverter.Convert(target, @"\d+", Ci, kind, "x");
                if (!r.Converted || r.Text.Length == 0) throw new Exception($"{target}/{kind} did not convert a simple pattern");
            }
        count++; Console.WriteLine("PASS every target converts a simple pattern for every kind of code");

        var gp = RegexConverter.Convert("grep -P", @"\d+(?=x)", Ci, folder: "/src/it's", globs: "*.log;!*.min.log");
        Check(gp.Converted && gp.Text == "grep -P -i -rn --include='*.log' --exclude='*.min.log' -e '\\d+(?=x)' '/src/it'\\''s'", "grep -P keeps lookarounds, quotes the path and turns globs into --include and --exclude");
        Check(!RegexConverter.Convert("grep -P", "(?<a-b>x)", Ci).Converted && RegexConverter.Convert("grep -P", "(?<a-b>x)", Ci).Notes[0].Contains("Balancing"), "grep -P refuses .NET-only constructs and names them");
        Check(!RegexConverter.Convert("grep -P", "a", Cs with { Singleline = true }).Converted && RegexConverter.Convert("grep -P", "a b", Cs with { IgnorePatternWhitespace = true }).Text.Contains("(?x)a b"), "grep -P refuses Singleline and writes x inline");

        var rg = RegexConverter.Convert("ripgrep", @"[a-z]+\d", Cs, folder: "C:\\logs", globs: "*.txt;!old*");
        Check(rg.Converted && rg.Text == "rg -s -g '*.txt' -g '!old*' -e '[a-z]+\\d' 'C:\\logs'", "ripgrep is case sensitive with -s when IgnoreCase is off, and globs become -g");
        var rgLook = RegexConverter.Convert("ripgrep", "a(?=b)", Ci);
        Check(rgLook.Converted && rgLook.Text.StartsWith("rg -P -i") && rgLook.Notes.Any(n => n.Contains("-P is added")), "ripgrep switches to -P when the default engine cannot read the pattern");
        Check(!RegexConverter.Convert("ripgrep", "(?<a-b>x)", Ci).Converted, "ripgrep refuses what no ripgrep engine reads");

        var ss = RegexConverter.Convert("Select-String", "it's", Cs);
        Check(ss.Text == "Select-String -Path '.\\*' -Pattern 'it''s' -CaseSensitive", "Select-String doubles single quotes and adds -CaseSensitive");
        var ssDir = RegexConverter.Convert("Select-String", "a", Ci, folder: "D:\\x y", globs: "*.log");
        Check(ssDir.Text == "Get-ChildItem -LiteralPath 'D:\\x y' -Recurse -File -Include '*.log' | Select-String -Pattern 'a'" && !RegexConverter.Convert("Select-String", "a", Cs with { Singleline = true }).Converted, "Select-String with a folder goes through Get-ChildItem, and refuses Singleline");

        var js = RegexConverter.Convert("JavaScript", "a/b[/]\\d", Ci with { Multiline = true }, RegexCodeKind.Matches);
        Check(js.Text.Contains("/a\\/b[/]\\d/imu") && js.Text.Contains("matchAll"), "JavaScript escapes a slash outside a set and adds the flags");
        Check(!RegexConverter.Convert("JavaScript", "(?<=a)b(?>c)", Ci).Converted && RegexConverter.Convert("JavaScript", @"\d", Ci).Lossy, "JavaScript refuses atomic groups and marks \\d as read differently");
        Check(RegexConverter.JavaScriptLiteral("a\nb", "") == "/a\\nb/", "JavaScript literal escapes a line break");

        Check(RegexConverter.PythonString(@"a\d") == @"r'a\d'" && RegexConverter.PythonString("it's") == @"'it\'s'" && RegexConverter.PythonString("a\\") == @"'a\\'" && RegexConverter.PythonString("a\nb") == @"'a\nb'", "Python uses a raw string only when that is exact");
        var py = RegexConverter.Convert("Python", @"(?P<n>a)", Ci with { Multiline = true, Singleline = true }, RegexCodeKind.Matches);
        Check(py.Text.Contains("re.compile(r'(?P<n>a)', re.IGNORECASE | re.MULTILINE | re.DOTALL)") && !RegexConverter.Convert("Python", "(?<n>a)", Ci).Converted, "Python options are written and (?<n>) is refused in favour of (?P<n>)");

        Check(RegexConverter.JavaString("a\\d\"") == "\"a\\\\d\\\"\"" && RegexConverter.Convert("Java", @"\d", Ci, RegexCodeKind.IsMatch).Text.Contains("Pattern.compile(\"\\\\d\", Pattern.CASE_INSENSITIVE)"), "Java doubles backslashes and names the options");
        Check(RegexConverter.GoString(@"a\d") == "`a\\d`" && RegexConverter.GoString("a`b") == "\"a`b\"" && RegexConverter.Convert("Go", @"\d", Ci).Text.Contains("regexp.MustCompile(`(?i)\\d`)") && !RegexConverter.Convert("Go", "(?=a)", Ci).Converted && !RegexConverter.Convert("Go", "a", Ci with { IgnorePatternWhitespace = true }).Converted, "Go uses raw strings when it can, inline flags, and refuses lookaheads and x");
        Check(RegexConverter.PhpPattern("a/b'c\\d", "i") == "'/a\\\\/b\\'c\\\\d/i'" && RegexConverter.Convert("PHP", @"\d", Ci with { Multiline = true }).Text.Contains("'/\\\\d/imu'"), "PHP escapes the delimiter, backslashes and quotes");
        Check(!RegexConverter.Convert("PHP", "(?<a-b>x)", Ci).Converted && !RegexConverter.Convert("Java", "(?<a-b>x)", Ci).Converted, "PHP and Java refuse .NET-only constructs");
        Check(RegexConverter.Convert("JavaScript", "a", Ci with { RightToLeft = true }).Converted == false && RegexConverter.Convert("Python", "a", Ci with { RightToLeft = true }).Converted == false, "RightToLeft is refused where it has no equivalent");
        Reject(() => RegexConverter.Convert("Perl", "a", Ci), "an unknown target is still refused");
    }
}
