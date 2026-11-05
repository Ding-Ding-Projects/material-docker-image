using System.Text.Json;
using System.Text.RegularExpressions;
using MaterialDockerImage.UI;

/// <summary>Checks for table exports, the changelog model, menu shortcut derivation, info-icon coverage and update-feed handling.</summary>
public static class RecordsChecks
{
    static int count;

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

    static string Sha(char c) => new(c, 40);

    public static int Run()
    {
        count = 0;
        Exports();
        Changelog();
        Updates();
        Guidance();
        Shortcuts();
        Cantonese();
        return count;
    }

    // ---------------------------------------------------------------- exports
    static void Exports()
    {
        var when = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
        var headers = new[] { "Name", "Note" };
        var nasty = "a,b \"quoted\"\nline <b>&</b> | pipe \\ slash 'single'";
        var rows = new List<string[]> { new[] { "plain", nasty }, new[] { "short" } };

        var csv = TableExport.Render("CSV", "T", headers, rows, when);
        Check(csv == "Name,Note\r\nplain,\"a,b \"\"quoted\"\"\nline <b>&</b> | pipe \\ slash 'single'\"\r\nshort,\r\n", "CSV quotes commas, quotes and newlines and pads a short row");
        var tsv = TableExport.Render("TSV", "T", ["A\tB", "C"], [new[] { "x\ty", "z" }], when);
        Check(tsv == "\"A\tB\"\tC\r\n\"x\ty\"\tz\r\n", "TSV quotes tabs inside cells");

        var json = TableExport.Render("JSON", "T", headers, rows, when);
        using (var doc = JsonDocument.Parse(json))
        {
            var root = doc.RootElement;
            Check(root.GetProperty("schema").GetString() == TableExport.SchemaName && root.GetProperty("version").GetInt32() == 1 && root.GetProperty("count").GetInt32() == 2, "JSON carries schema name, version 1 and the row count");
            Check(root.GetProperty("rows")[0].GetProperty("Note").GetString() == nasty && root.GetProperty("rows")[1].GetProperty("Note").GetString() == "", "JSON round-trips hostile cell text and pads a short row");
            Check(root.GetProperty("exported").GetString()!.StartsWith("2026-11-04T12:00:00", StringComparison.Ordinal), "JSON exported time is the supplied time");
        }
        using (var dup = JsonDocument.Parse(TableExport.Render("JSON", "T", ["Name", "Name", "Name"], [new[] { "1", "2", "3" }], when)))
            Check(dup.RootElement.GetProperty("rows")[0].EnumerateObject().Select(p => p.Name + "=" + p.Value.GetString()).SequenceEqual(["Name=1", "Name (2)=2", "Name (3)=3"]), "JSON keeps columns that share a header by numbering them");

        var md = TableExport.Render("Markdown", "T", headers, rows, when);
        Check(md.Contains("| plain | a,b \"quoted\"<br>line <b>&</b> \\| pipe \\\\ slash 'single' |\n") && md.Contains("| short |  |\n"), "Markdown escapes pipes and backslashes and turns newlines into line breaks");
        Check(!TableExport.Render("Markdown", "T", ["a|b"], [new[] { "x|y" }], when).Contains("a|b"), "Markdown escapes a pipe in a header");

        var evil = "<script>alert(1)</script>";
        var html = TableExport.Render("HTML", "T <i>&", ["H\"1", evil], [new[] { evil, "it's \"quoted\" & <ok>" }], when);
        Check(!html.Contains("<script>") && !html.Contains("<i>") && html.Contains("&lt;script&gt;alert(1)&lt;/script&gt;"), "HTML escapes script tags in cells, headers and the title");
        Check(html.Contains("it&#39;s &quot;quoted&quot; &amp; &lt;ok&gt;") && html.Contains("H&quot;1"), "HTML escapes quotes, apostrophes and ampersands");
        Check(html.StartsWith("<!doctype html>") && html.Contains("<meta charset=\"utf-8\">") && html.Contains("<th scope=\"col\">"), "HTML is a complete UTF-8 document with column header scope");
        Reject(() => TableExport.Render("PDF", "T", headers, rows, when), "an unknown export format is refused");
        Check(TableExport.Formats.SequenceEqual(["CSV", "TSV", "JSON", "Markdown", "HTML"]) && TableExport.Extension("HTML") == "html" && TableExport.Extension("Markdown") == "md", "all five formats are offered with the right extensions");
    }

    // -------------------------------------------------------------- changelog
    static string SampleChangelog(string sha = "", string date = "2026-11-03") => $$"""
    {"schema":"material-docker-image/changelog","version":1,"repository":"Ding-Ding-Projects/material-docker-image","note":"Generated.","groups":[
      {"version":"Unreleased since v1.0.1","date":"","unreleased":true,"entries":[{"sha":"{{(sha.Length > 0 ? sha : Sha('a'))}}","date":"2026-11-04","subject":"Add export formats"}]},
      {"version":"v1.0.1","date":"2026-11-03","unreleased":false,"entries":[
        {"sha":"{{Sha('b')}}","date":"{{date}}","subject":"Fix the image list crash"},
        {"sha":"{{Sha('c')}}","date":"2026-10-30","subject":"Update the tab strip"},
        {"sha":"{{Sha('d')}}","date":"2026-10-01","subject":"Tidy things"}]}]}
    """;

    static void Changelog()
    {
        var data = ChangelogModel.Parse(SampleChangelog());
        Check(data.Groups.Count == 2 && data.EntryCount == 4 && data.Groups[0].Unreleased && data.Groups[1].Version == "v1.0.1", "changelog parses groups, entries and the unreleased flag");
        Check(ChangelogModel.Categorize("Add x") == "Added" && ChangelogModel.Categorize("Fix y") == "Fixed" && ChangelogModel.Categorize("Remove z") == "Removed" && ChangelogModel.Categorize("Update w") == "Changed" && ChangelogModel.Categorize("Tidy things") == "Other" && ChangelogModel.Categorize("") == "Other", "categories come from the leading verb and unknown verbs are Other");
        Reject(() => ChangelogModel.Parse(SampleChangelog(sha: "abc123")), "a short SHA is refused");
        Reject(() => ChangelogModel.Parse(SampleChangelog(sha: new string('G', 40))), "a non-hex SHA is refused");
        Reject(() => ChangelogModel.Parse(SampleChangelog(date: "yesterday")), "a malformed entry date is refused");
        Reject(() => ChangelogModel.Parse(SampleChangelog().Replace("changelog\"", "other\"")), "a different schema is refused");
        Reject(() => ChangelogModel.Parse("{}"), "a file missing its fields is refused");
        Reject(() => ChangelogModel.Parse(new string(' ', ChangelogModel.MaxChars + 1)), "an oversized file is refused");

        Check(ChangelogModel.CommitUrl("a/b", Sha('a')) == "https://github.com/a/b/commit/" + Sha('a') && ChangelogModel.ShortSha(Sha('a')) == "aaaaaaa", "commit links use the repository and the full SHA; the label is seven characters");

        var all = ChangelogModel.Filter(data, _ => true, null, null);
        Check(all.Sum(g => g.Entries.Count) == 4, "no search and no dates shows everything");
        var searched = ChangelogModel.Filter(data, t => t.Contains("crash", StringComparison.OrdinalIgnoreCase), null, null);
        Check(searched.Count == 1 && searched[0].Entries.Count == 1 && searched[0].Entries[0].Sha == Sha('b'), "search narrows to matching entries and drops empty versions");
        var regex = new Regex("^v1.*Fixed", RegexOptions.CultureInvariant);
        Check(ChangelogModel.Filter(data, t => regex.IsMatch(t), null, null).Single().Entries.Single().Subject == "Fix the image list crash", "the matcher sees version and category so a pattern can use them");
        var ranged = ChangelogModel.Filter(data, _ => true, new DateOnly(2026, 10, 30), new DateOnly(2026, 11, 3));
        Check(ranged.Sum(g => g.Entries.Count) == 2, "the date range is inclusive at both ends");
        Check(ChangelogModel.Filter(data, t => t.Contains("crash"), new DateOnly(2026, 11, 4), null).Count == 0, "search and dates compose (both must match)");
        Check(ChangelogModel.ValidateRange("2026-11-01", "2026-11-30", out var s, out var e) == "" && s == new DateOnly(2026, 11, 1) && e == new DateOnly(2026, 11, 30), "ISO dates are accepted");
        Check(ChangelogModel.ValidateRange("", "", out s, out e) == "" && s == null && e == null, "empty bounds mean no limit");
        Check(ChangelogModel.ValidateRange("next week", "", out _, out _).Contains("start date") && ChangelogModel.ValidateRange("", "nonsense", out _, out _).Contains("end date"), "a partial or invalid date is reported inline");
        Check(ChangelogModel.ValidateRange("2026-12-01", "2026-11-01", out _, out _).Contains("after"), "a reversed range is reported");

        var markdown = ChangelogModel.ToMarkdown(data, searched, "start to 2026-11-30", "crash");
        Check(markdown.Contains("Range: start to 2026-11-30") && markdown.Contains("Search: crash") && markdown.Contains("Shown: 1 of 4") && markdown.Contains(Sha('b')) && markdown.Contains("### Fixed"), "Markdown export states the range, search, counts and the full SHA");
        Check(ChangelogModel.ToMarkdown(data, [], "", "").Contains("No changes match"), "an empty export says so");

        var repoRoot = new DirectoryInfo(AppContext.BaseDirectory);
        while (repoRoot != null && !Directory.Exists(Path.Combine(repoRoot.FullName, "src", "MaterialDockerImage"))) repoRoot = repoRoot.Parent;
        var generated = Path.Combine(repoRoot!.FullName, "src", "MaterialDockerImage", "Assets", "changelog.json");
        if (File.Exists(generated))
        {
            var real = ChangelogModel.Parse(File.ReadAllText(generated));
            Check(real.EntryCount > 0 && real.Groups.All(g => g.Entries.All(x => Regex.IsMatch(x.Sha, "^[0-9a-f]{40}$"))), "the generated changelog parses and every entry carries a full SHA");
        }
        else Console.WriteLine("SKIP generated changelog not present (build/make-changelog.ps1 writes it)");
    }

    // ---------------------------------------------------------- update feed
    static void Updates()
    {
        Check(UpdateFeed.TryParseVersion("0.51.1", out var v) && v.ToString() == "0.51.1" && UpdateFeed.TryParseVersion("v1.2.3", out _) && UpdateFeed.TryParseVersion("1.0.0-beta1", out var pre) && pre.Pre == "beta1" && UpdateFeed.TryParseVersion("1.2.3+abc123", out var plus) && plus.ToString() == "1.2.3", "versions parse with a v prefix, a pre-release and build metadata");
        foreach (var bad in new[] { "", "abc", "1..2", "1.2.3.4.5", "1.2.x", " ", "1.2.3 beta" }) Check(!UpdateFeed.TryParseVersion(bad, out _), "version '" + bad + "' is refused");
        int Compare(string a, string b) { UpdateFeed.TryParseVersion(a, out var x); UpdateFeed.TryParseVersion(b, out var y); return x.CompareTo(y); }
        Check(Compare("0.10.1", "0.9.1") > 0 && Compare("0.9.1", "0.10.1") < 0 && Compare("1.0", "1.0.0") == 0 && Compare("2.0.0", "1.99.99") > 0, "versions compare numerically, not as text");
        Check(Compare("1.0.0", "1.0.0-beta") > 0 && Compare("1.0.0-beta", "1.0.0") < 0 && Compare("1.0.0-alpha", "1.0.0-beta") < 0, "a pre-release sorts below its release");

        var line = (string c, string f, long n) => new string(c[0], 40) + " " + f + " " + n;
        var feed = "﻿" + line("a", "MaterialDockerImage-0.50.1-full.nupkg", 1000) + "\r\n" + line("b", "MaterialDockerImage-0.51.1-delta.nupkg", 50) + "\r\n\r\n" + line("c", "MaterialDockerImage-0.51.1-full.nupkg", 1100) + "# 25%\r\n" + line("d", "Other-9.0.0-full.nupkg", 5) + "\n";
        var releases = UpdateFeed.ParseReleases(feed);
        Check(releases.Count == 4 && releases[1].IsDelta && !releases[0].IsDelta && releases[2].RolloutPercent == 25 && releases[0].Size == 1000 && releases[0].PackageId == "MaterialDockerImage", "RELEASES parses full, delta and staged-rollout lines, tolerating a BOM and CRLF");
        Reject(() => UpdateFeed.ParseReleases("not a feed line"), "a malformed feed line is refused, not skipped");
        Reject(() => UpdateFeed.ParseReleases(line("a", "MaterialDockerImage-x.y.z-full.nupkg", 1)), "a package with an unreadable version is refused");
        Reject(() => UpdateFeed.ParseReleases(line("a", "MaterialDockerImage-1.0.0-full.nupkg", 1) + "# 150%"), "a rollout above 100 percent is refused");
        Reject(() => UpdateFeed.ParseReleases(new string('x', UpdateFeed.MaxFeedChars + 1)), "an oversized feed is refused");
        Check(UpdateFeed.ParseReleases("").Count == 0, "an empty feed parses to nothing");

        var newer = UpdateFeed.Decide("0.50.1", "MaterialDockerImage", releases);
        Check(newer.UpdateAvailable && newer.NewestVersion == "0.51.1", "a newer full package is offered and a delta or another app's package is not counted");
        Check(!UpdateFeed.Decide("0.51.1", "MaterialDockerImage", releases).UpdateAvailable && !UpdateFeed.Decide("0.60.0", "MaterialDockerImage", releases).UpdateAvailable, "the same or a newer running version is never downgraded");
        Check(!UpdateFeed.Decide("1.0.0", "Missing", releases).UpdateAvailable && !UpdateFeed.Decide("garbage", "MaterialDockerImage", releases).UpdateAvailable && !UpdateFeed.Decide("1.0.0", "MaterialDockerImage", []).UpdateAvailable, "no matching package, a bad running version or an empty feed offers nothing");
        Check(UpdateFeed.CleanRunningVersion("0.51.1+abcdef") == "0.51.1" && UpdateFeed.CleanRunningVersion("0.51.1") == "0.51.1", "the running version drops source metadata");
    }

    // ----------------------------------------------------- info-icon text
    static void Guidance()
    {
        var text = InfoGuidance.ForSavedSetting("Turns something on", "On");
        Check(text.StartsWith("Turns something on. The built-in default is On.") && text.Contains("settings.json") && text.Contains("Reset all settings"), "fallback guidance adds a full stop, the default and how to undo it");
        Check(InfoGuidance.ForSavedSetting("Already ends.", "Off").StartsWith("Already ends. The built-in default is Off."), "fallback guidance does not double a full stop");
    }

    // -------------------------------------------------- menu shortcut audit
    static readonly string[] LocalKeys = ["Del", "Enter", "F2", "Ctrl+A", "Ctrl+C", "Alt+Up", "Alt+Down"];

    static string Canonical(string gesture) => string.Join("+", gesture.Split('+').Select(p => p.Trim()).Select(p => p switch { "Control" => "Ctrl", "Delete" => "Del", _ => p }).OrderBy(p => p switch { "Ctrl" => 0, "Alt" => 1, "Shift" => 2, _ => 3 }));

    static string? FirstCall(string source, int open, int argument)
    {
        // Returns the text of the given top-level argument of a call whose '(' is at index open - 1.
        var depth = 1; var index = 0; var inString = false; var start = open;
        for (var i = open; i < source.Length && depth > 0; i++)
        {
            var c = source[i];
            if (inString) { if (c == '\\') i++; else if (c == '"') inString = false; continue; }
            if (c == '"') { inString = true; continue; }
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') { depth--; if (depth == 0 && index == argument) return source[start..i].Trim(); }
            else if (c == ',' && depth == 1) { if (index == argument) return source[start..i].Trim(); index++; start = i + 1; }
        }
        return null;
    }

    /// <summary>Violations: hand-typed gestures outside the local-key allowlist, and commands that do not exist.</summary>
    static List<string> Audit(IEnumerable<(string File, string Text)> sources)
    {
        var problems = new List<string>();
        var commandGestures = new HashSet<string>(StringComparer.Ordinal);
        var commandIds = new HashSet<string>(StringComparer.Ordinal);
        var gesturePattern = new Regex(@"Id\s*=\s*""(?<id>[^""]+)""[^;\n]*?Gesture\s*=\s*new\s+KeyGesture\(\s*Key\.(?<key>\w+)(?:\s*,\s*(?<mods>[\w.| ]+?))?\s*\)", RegexOptions.CultureInvariant);
        var idPattern = new Regex(@"new\s+AppCommand\s*\{\s*Id\s*=\s*""(?<id>[^""]+)""", RegexOptions.CultureInvariant);
        foreach (var (_, text) in sources)
        {
            foreach (Match m in idPattern.Matches(text)) commandIds.Add(m.Groups["id"].Value);
            foreach (Match m in gesturePattern.Matches(text))
            {
                var mods = m.Groups["mods"].Success ? string.Join("+", m.Groups["mods"].Value.Split('|').Select(x => x.Trim().Replace("ModifierKeys.", ""))) + "+" : "";
                commandGestures.Add(Canonical(mods + m.Groups["key"].Value));
            }
        }
        foreach (var local in LocalKeys)
            if (commandGestures.Contains(Canonical(local))) problems.Add("local key " + local + " is also a command gesture");
        foreach (var (file, text) in sources)
        {
            foreach (var call in new[] { (Name: "Ui.MenuItem(", Argument: 3), (Name: "RowAction(", Argument: 3) })
            {
                var from = 0;
                while ((from = text.IndexOf(call.Name, from, StringComparison.Ordinal)) >= 0)
                {
                    var open = from + call.Name.Length;
                    from = open;
                    // Skip declarations: "public static MenuItem MenuItem(string header..." never matches Ui.MenuItem( but RowAction declares parameters.
                    var arg = FirstCall(text, open, call.Argument);
                    if (arg == null || !arg.StartsWith('"')) continue;
                    var literal = arg.Trim('"');
                    if (literal.Length == 0) continue;
                    if (!LocalKeys.Contains(literal, StringComparer.Ordinal)) problems.Add(file + ": hand-typed shortcut \"" + literal + "\" is not a declared local key; use Ui.CommandMenuItem with the command id");
                    else if (commandGestures.Contains(Canonical(literal))) problems.Add(file + ": hand-typed shortcut \"" + literal + "\" duplicates a command gesture");
                }
            }
            var idFrom = 0;
            const string commandCall = "Ui.CommandMenuItem(";
            while ((idFrom = text.IndexOf(commandCall, idFrom, StringComparison.Ordinal)) >= 0)
            {
                var open = idFrom + commandCall.Length; idFrom = open;
                var id = FirstCall(text, open, 2);
                if (id == null || !id.StartsWith('"')) { problems.Add(file + ": CommandMenuItem needs a literal command id"); continue; }
                if (!commandIds.Contains(id.Trim('"'))) problems.Add(file + ": CommandMenuItem names unregistered command " + id);
            }
        }
        return problems;
    }

    static void Shortcuts()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "src", "MaterialDockerImage"))) root = root.Parent;
        var app = Path.Combine(root!.FullName, "src", "MaterialDockerImage");
        var sources = Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .Select(p => (File: Path.GetRelativePath(app, p), Text: File.ReadAllText(p))).ToList();
        var real = Audit(sources);
        foreach (var problem in real) Console.WriteLine("SHORTCUT " + problem);
        Check(real.Count == 0, "no menu hand-types a shortcut that differs from its command");
        Check(sources.Any(s => s.Text.Contains("Ui.CommandMenuItem(\"Close tab\"")) && sources.Any(s => s.Text.Contains("\"appearance.element\"")), "the guard sees the converted menus and the appearance command");

        var registry = ("Reg.cs", "c.Register(new AppCommand { Id = \"tab.close\", Title = \"Close tab\", Gesture = new KeyGesture(Key.W, ModifierKeys.Control), Run = X });\n" +
                                   "c.Register(new AppCommand { Id = \"palette\", Title = \"P\", Icon = I, Gesture = new KeyGesture(Key.F, ModifierKeys.Control | ModifierKeys.Shift), Run = Y });");
        Check(Audit([registry, ("Menu.cs", "x.Add(Ui.CommandMenuItem(\"Close tab\", Icons.Close, \"tab.close\", () => Z()));\nx.Add(Ui.MenuItem(\"Delete\", Icons.Delete, () => Z(), \"Del\", true));\nt.RowAction(\"Copy\", I, n => W(n), \"Ctrl+C\");")]).Count == 0, "the guard accepts command menus and declared local keys");
        Check(Audit([registry, ("Bad.cs", "x.Add(Ui.MenuItem(\"Close tab\", Icons.Close, () => Z(), \"Ctrl+W\"));")]).Any(p => p.Contains("Ctrl+W")), "the guard catches a hand-typed command shortcut");
        Check(Audit([registry, ("Bad.cs", "x.Add(Ui.MenuItem(\"Open palette\", Icons.Search, () => Z(),\n    \"Ctrl+Shift+F\"));")]).Any(p => p.Contains("Ctrl+Shift+F")), "the guard catches a hand-typed shortcut that spans lines");
        Check(Audit([registry, ("Bad.cs", "t.RowAction(\"Close\", I, n => W(n), \"Ctrl+W\");")]).Count == 1, "the guard catches a hand-typed row-action shortcut");
        Check(Audit([registry, ("Bad.cs", "x.Add(Ui.CommandMenuItem(\"Nope\", null, \"no.such\", () => Z()));")]).Any(p => p.Contains("unregistered")), "the guard catches a command id that is not registered");
        Check(Audit([("Reg.cs", "new AppCommand { Id = \"x\", Gesture = new KeyGesture(Key.A, ModifierKeys.Control) }"), ("M.cs", "Ui.MenuItem(\"All\", null, () => Z(), \"Ctrl+A\")")]).Any(p => p.Contains("also a command gesture")), "a local key that becomes a command gesture is reported");
    }

    // ---------------------------------------------------------- Cantonese
    static void Cantonese()
    {
        var prior = MaterialDockerImage.Services.Loc.Language; var school = MaterialDockerImage.Services.Loc.SchoolMode;
        MaterialDockerImage.Services.Loc.SchoolMode = false;
        MaterialDockerImage.Services.Loc.Language = "Cantonese";
        var samples = new[]
        {
            "Export visible rows as HTML", "Copy visible rows as Markdown", "Copied 12 rows as CSV.", "Release history", "Search the changelog", "From date", "To date",
            "Copy as Markdown", "Export Markdown…", "5 of 69 changes shown in 3 versions", "The start date is not a valid date. Use YYYY-MM-DD.", "Check for updates", "Check now",
            "Version 0.52.1 is available.", "Version 0.52.1 is ready. Restart when you are done with your work.", "Restart to update", "This build is up to date.",
            "The update check did not finish: timed out. The current version keeps working.", "Update status",
            "Turns it on. The built-in default is On. A change is saved to settings.json on this computer straight away, and Reset all settings in Data and privacy restores the default.",
            "Newer releases may exist. Check the releases page for anything published after this build.",
        };
        bool Cjk(string t) => t.Any(c => c >= '一' && c <= '鿿');
        var missing = samples.Where(x => { var t = MaterialDockerImage.Services.Loc.T(x); return t == x || !Cjk(t); }).ToList();
        Check(missing.Count == 0, "Cantonese covers the new export, changelog, guidance and update strings" + (missing.Count == 0 ? "" : ": " + string.Join(" | ", missing)));
        var counted = MaterialDockerImage.Services.Loc.T("Copied 12 rows as CSV.");
        Check(counted.Contains("12") && counted.Contains("CSV"), "Cantonese templates keep counts and format names literal");
        MaterialDockerImage.Services.Loc.Language = "English";
        Check(samples.All(x => MaterialDockerImage.Services.Loc.T(x) == x), "English leaves the new strings unchanged");
        MaterialDockerImage.Services.Loc.Language = prior; MaterialDockerImage.Services.Loc.SchoolMode = school;
    }
}
