using System.Text.RegularExpressions;
using MaterialDockerImage.UI;

/// <summary>Checks for command palette descriptions and DataTable two-line cells.</summary>
public static class PaletteTableChecks
{
    static int count;

    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        count++;
        Console.WriteLine("PASS " + name);
    }

    public static int Run()
    {
        count = 0;
        Descriptions();
        TwoLineCells();
        TabContentAutomation();
        return count;
    }

    /// <summary>WPF exposes a tab page to screen readers only through a content presenter named PART_SelectedContentHost;
    /// without the name every tab page's content is missing from the automation tree.</summary>
    static void TabContentAutomation()
    {
        var theme = Path.Combine(SourceRoot() ?? "", "Themes", "Material.xaml");
        var text = File.Exists(theme) ? File.ReadAllText(theme) : "";
        var template = Regex.Match(text, @"<ControlTemplate TargetType=""TabControl"">.*?</ControlTemplate>", RegexOptions.Singleline).Value;
        Check(template.Contains(@"x:Name=""PART_SelectedContentHost""") && template.Contains(@"ContentSource=""SelectedContent"""), "the tab control template exposes tab page content to UI Automation");
    }

    static string? SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "MaterialDockerImage"))) dir = dir.Parent;
        return dir == null ? null : Path.Combine(dir.FullName, "src", "MaterialDockerImage");
    }

    static void Descriptions()
    {
        var root = SourceRoot();
        Check(root != null, "the application source is found for the palette description scan");
        var files = Directory.EnumerateFiles(root!, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)).ToList();
        // Every command registered with a literal id ("Id = \"x\", Title = ...") has a description; ids built with + are checked below.
        var literal = new Regex(@"Id = ""([^""]+)"", Title =");
        var ids = files.SelectMany(f => literal.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value)).Distinct().ToList();
        Check(ids.Count >= 90, "the palette description scan finds the registered commands (" + ids.Count + ")");
        var missing = ids.Where(id => CommandDescriptions.For(id) == null).ToList();
        Check(missing.Count == 0, "every literal palette command has a description" + (missing.Count > 0 ? ": " + string.Join(", ", missing) : ""));
        // Destinations are registered as "go." + key from Pages/Destinations.cs.
        var destinations = new Regex(@"new\(""(\w+)"", Icons\.").Matches(File.ReadAllText(Path.Combine(root!, "Pages", "Destinations.cs"))).Select(m => m.Groups[1].Value).ToList();
        Check(destinations.Count >= 15 && destinations.All(k => CommandDescriptions.For("go." + k) != null), "every destination has a palette description");
        Check(DockerfileStarters.All.All(s => CommandDescriptions.For(CommandDescriptions.StarterPrefix + s.Id) != null), "every Creator starter has a palette description");
        Check(AppearancePresets.BuiltIn.All(p => CommandDescriptions.For(CommandDescriptions.PresetPrefix + p.Id) != null), "every built-in appearance preset has a palette description");
        Check(CommandDescriptions.For("no.such.command") == null && CommandDescriptions.For(CommandDescriptions.StarterPrefix + "nope") == null, "an unknown command has no invented description");
        Check(CommandDescriptions.AllTexts.All(t => t.Length is > 10 and <= 90 && !t.Contains('\n')), "palette descriptions are single lines of reasonable length");

        var prior = MaterialDockerImage.Services.Loc.Language; var school = MaterialDockerImage.Services.Loc.SchoolMode;
        MaterialDockerImage.Services.Loc.SchoolMode = false;
        MaterialDockerImage.Services.Loc.Language = "Cantonese";
        bool Cjk(string t) => t.Any(c => c >= '一' && c <= '鿿');
        var untranslated = CommandDescriptions.AllTexts.Where(t => { var c = MaterialDockerImage.Services.Loc.T(t); return c == t || !Cjk(c); }).ToList();
        Check(untranslated.Count == 0, "every palette description has Cantonese" + (untranslated.Count > 0 ? ": " + string.Join(" | ", untranslated.Take(8)) : ""));
        MaterialDockerImage.Services.Loc.Language = "English";
        Check(CommandDescriptions.AllTexts.All(t => MaterialDockerImage.Services.Loc.T(t) == t), "English leaves palette descriptions unchanged");
        MaterialDockerImage.Services.Loc.Language = prior; MaterialDockerImage.Services.Loc.SchoolMode = school;

        Check(CommandDescriptions.SubLine("Reload the container list", "Containers") == "Reload the container list" && CommandDescriptions.SubLine(null, "Containers") == "Containers" && CommandDescriptions.SubLine(" ", "Settings") == "Settings",
            "the palette second line is the description, or the group when there is none");
        var searched = CommandDescriptions.SearchText("重新整理容器", "Refresh containers", "由引擎重新載入容器清單", "Reload the container list from the engine", "", "Containers");
        Check(searched.Contains("from the engine") && searched.Contains("容器清單") && searched.Contains("Refresh containers") && !searched.Contains("  "), "palette search covers both languages of the title and description");
    }

    static void TwoLineCells()
    {
        Check(TableCells.Label("web-1", "nginx:1.27") == "web-1, nginx:1.27" && TableCells.Label("web-1", "") == "web-1" && TableCells.Label("", "nginx") == "nginx" && TableCells.Label(null, null) == "",
            "a two-line cell's accessible name carries both lines and skips an empty one");
        var fields = TableCells.SearchFields(new[] { "Running", "web-1", "80/tcp" }, new[] { "", "nginx:1.27", "" });
        Check(fields.SequenceEqual(new[] { "Running", "web-1", "nginx:1.27", "80/tcp" }), "search fields put each secondary line right after its primary");
        var anchored = new RegexSearchQuery("^nginx:", true, new RegexBuilderFlags());
        Check(anchored.MatchesFields(fields) && !anchored.MatchesFields(new[] { "Running", "web-1", "80/tcp" }), "an anchored search matches the secondary line of a two-line cell");
        var headers = TableCells.ExportHeaders(new List<(string, string?)> { ("State", null), ("Name", "Image"), ("Ports", null) });
        Check(headers.SequenceEqual(new[] { "State", "Name", "Image", "Ports" }), "a two-line column exports as two columns");
        var row = TableCells.ExportRow(new[] { "Running", "web-1", "80/tcp" }, new[] { "", "nginx:1.27", "" }, new[] { false, true, false });
        Check(row.SequenceEqual(new[] { "Running", "web-1", "nginx:1.27", "80/tcp" }), "an export row keeps the primary and secondary lines in their own cells");
        var csv = TableExport.Render("CSV", "Containers", headers, new List<string[]> { row }, DateTimeOffset.UnixEpoch);
        Check(csv == "State,Name,Image,Ports\r\nRunning,web-1,nginx:1.27,80/tcp\r\n", "a two-line column renders to CSV with both lines");
        Check(TableCells.DefaultSecondaryHeader("Repository") == "Repository detail", "an unnamed secondary line exports under a derived header");
    }
}
