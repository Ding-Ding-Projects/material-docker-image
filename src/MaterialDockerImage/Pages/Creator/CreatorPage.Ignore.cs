using System.Text;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage.Pages;

/// <summary>The .dockerignore tab (presets, pattern rows, a live check against a context folder) and the compose.yaml tab.</summary>
public sealed partial class CreatorPage
{
    private readonly ContentControl ignoreRowsHost = new();
    private List<string>? ignoreBound;
    private string ignoreContext = "";
    private readonly TextBlock ignoreContextText = Ui.Mono("", 12, "OnSurfaceVariant", true);
    private readonly StackPanel ignoreSummary = new();
    private readonly ListBox ignoreList = new() { MaxHeight = 320, MinHeight = 60 };
    private readonly SearchFilter ignoreSearch = new("Search checked paths", 240);
    private IReadOnlyList<DockerignoreEntry> ignoreEntries = Array.Empty<DockerignoreEntry>();
    private bool ignoreShowExcludedOnly;

    private TabItem composeTab = null!;
    private readonly CodeEditor composeView = new(true, 160);
    private string composeText = "";

    private FrameworkElement BuildIgnorePanel()
    {
        var presets = new WrapPanel();
        foreach (var name in Dockerignore.Presets.Keys)
        {
            var b = Ui.Button(name, Icons.Add, ButtonKind.Outlined, () => AddIgnorePreset(name), false);
            b.ToolTip = Loc.T("Adds the patterns that are missing:") + " " + string.Join("  ", Dockerignore.Presets[name]);
            b.MinHeight = 32; b.Height = 32; b.Padding = new Thickness(10, 0, 12, 0); b.Margin = new Thickness(0, 0, 6, 6);
            presets.Children.Add(b);
        }
        var choose = Ui.Button("Choose folder…", Icons.FolderOpen, ButtonKind.Text, ChooseIgnoreContext);
        var check = Ui.AsyncButton("Check context", Icons.FactCheck, ButtonKind.Tonal, CheckIgnoreAsync);
        check.ToolTip = Loc.T("Lists which files in the context folder the patterns exclude (first 2000 entries). Nothing is sent to Docker.");
        var save = Ui.AsyncButton("Save to folder", Icons.Save, ButtonKind.Outlined, SaveIgnoreAsync);
        ignoreSearch.Width = double.NaN;
        ignoreSearch.Changed += FillIgnoreList;
        System.Windows.Automation.AutomationProperties.SetName(ignoreList, Loc.T("Context entries"));
        ScrollViewer.SetHorizontalScrollBarVisibility(ignoreList, ScrollBarVisibility.Disabled);
        var excludedOnly = Ui.Switch("Excluded only", false, v => { ignoreShowExcludedOnly = v; FillIgnoreList(); });
        UpdateIgnoreContextText();
        var stack = Ui.VStack(12,
            Ui.Caption("Patterns follow Docker's rules: * stays within one folder, ** spans folders, ? is one character, a leading ! re-includes, and the last matching line wins. Paths are relative to the context root."),
            Ui.Wrap(0, Ui.Overline("Presets"), Ui.InfoButton("Presets", "Adds common patterns for a language. Patterns already listed are skipped.", "Node.js: node_modules, npm-debug.log*, dist, coverage")),
            presets,
            Ui.Field("Patterns", ignoreRowsHost, "One pattern per row; rows starting with # are comments. The order matters for ! exceptions.", "**/*.log\n!keep.log\nnode_modules/\n/build"),
            Ui.Divider(),
            Ui.Overline("Context folder"),
            ignoreContextText,
            Ui.Wrap(8, choose, check, save),
            ignoreSummary,
            Ui.Columns(8, [1, 0], ignoreSearch, excludedOnly),
            ignoreList,
            Ui.Caption("Excluding the Dockerfile or .dockerignore does not stop the build; they are only left out of COPY and ADD."));
        RefreshIgnoreEditor();
        return Ui.Scroll(stack);
    }

    /// <summary>Rebinds the pattern rows when the draft (and so its pattern list) was replaced.</summary>
    private void RefreshIgnoreEditor()
    {
        if (ReferenceEquals(ignoreBound, doc.IgnorePatterns)) return;
        ignoreBound = doc.IgnorePatterns;
        ignoreRowsHost.Content = new ListRows(doc.IgnorePatterns, "Pattern", "Add pattern", Touched);
    }

    private void AddIgnorePreset(string name)
    {
        FlushCommit();
        var added = Dockerignore.Presets[name].Where(p => !doc.IgnorePatterns.Contains(p)).ToList();
        if (added.Count == 0) { Host.Notify.Info(Loc.T("All patterns of this preset are already listed.")); return; }
        doc.IgnorePatterns.AddRange(added);
        ignoreBound = null;
        Changed();
        Host.Notify.Info($"{Loc.T("Added")} {added.Count} {Loc.T(added.Count == 1 ? "pattern" : "patterns")}: {string.Join(", ", added)}");
    }

    private string IgnoreContextFolder()
    {
        if (ignoreContext.Length > 0) return ignoreContext;
        if (savedPath.Length > 0) return Path.GetDirectoryName(savedPath) ?? "";
        return Host.Settings.BuildFolder ?? "";
    }

    private void UpdateIgnoreContextText()
    {
        var folder = IgnoreContextFolder();
        ignoreContextText.Text = folder.Length > 0 ? folder : Loc.T("No folder chosen. Choose the build context folder to check the patterns against real files.");
    }

    private void ChooseIgnoreContext()
    {
        var dialog = new OpenFolderDialog { Title = Loc.T("Choose the build context folder") };
        var current = IgnoreContextFolder();
        if (Directory.Exists(current)) dialog.InitialDirectory = current;
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        ignoreContext = dialog.FolderName;
        UpdateIgnoreContextText();
    }

    private async Task CheckIgnoreAsync()
    {
        FlushCommit();
        var folder = IgnoreContextFolder();
        if (!Directory.Exists(folder)) throw new InvalidOperationException("Choose an existing build context folder first.");
        var patterns = doc.IgnorePatterns.ToList();
        var result = await Task.Run(() => Dockerignore.Evaluate(folder, patterns));
        ignoreEntries = result.Entries;
        ignoreSummary.Children.Clear();
        Border Tile(string label, int value, string brush)
        {
            var tile = Ui.Card(Ui.VStack(2, Ui.Text(value.ToString("N0", System.Globalization.CultureInfo.CurrentCulture), 24, FontWeights.SemiBold, brush), Ui.Caption(label)), 12, "SurfaceContainer", 12);
            tile.MinWidth = 120; tile.Margin = new Thickness(0, 0, 8, 8);
            return tile;
        }
        var tiles = new WrapPanel();
        tiles.Children.Add(Tile(Loc.T("Included entries"), result.Included, "Success"));
        tiles.Children.Add(Tile(Loc.T("Excluded entries"), result.Excluded, "Warning"));
        ignoreSummary.Children.Add(tiles);
        if (result.Truncated) ignoreSummary.Children.Add(Ui.Badge($"Stopped after {Dockerignore.DefaultLimit} entries", Tone.Warning));
        foreach (var problem in result.Problems.Take(5)) ignoreSummary.Children.Add(Ui.Text(problem, 12, null, "Error"));
        FillIgnoreList();
    }

    private void FillIgnoreList()
    {
        ignoreList.Items.Clear();
        foreach (var entry in ignoreEntries)
        {
            if (ignoreShowExcludedOnly && !entry.Excluded) continue;
            if (!ignoreSearch.Matches(entry.Path)) continue;
            var row = Ui.Columns(8, [0, 1, 0],
                Ui.Icon(entry.IsDirectory ? Icons.FolderOpen : Icons.Description, 18, false, entry.Excluded ? "Outline" : "OnSurfaceVariant"),
                Ui.Mono(entry.Path + (entry.IsDirectory ? "/" : ""), 12, entry.Excluded ? "Outline" : "OnSurface"),
                Ui.Badge(entry.Excluded ? "Excluded" : entry.IsDirectory ? "Included" : Loc.T("Included ·") + " " + Ui.FormatBytes(entry.Size), entry.Excluded ? Tone.Warning : Tone.Success, true));
            var item = new ListBoxItem { Content = row };
            System.Windows.Automation.AutomationProperties.SetName(item, $"{entry.Path}: {(entry.Excluded ? Loc.T("excluded") : Loc.T("included"))}");
            ignoreList.Items.Add(item);
        }
        if (ignoreList.Items.Count == 0) ignoreList.Items.Add(new ListBoxItem { Content = Ui.Caption(ignoreEntries.Count == 0 ? "Run Check context to list the files." : "No entries match."), IsEnabled = false });
    }

    private async Task SaveIgnoreAsync()
    {
        FlushCommit();
        var folder = IgnoreContextFolder();
        if (!Directory.Exists(folder)) throw new InvalidOperationException("Choose an existing build context folder first.");
        var path = Path.Combine(folder, ".dockerignore");
        await WriteProjectFilesAsync(new Dictionary<string, string> { [path] = Dockerignore.Render(doc.IgnorePatterns) }, "Saved .dockerignore from the Creator");
    }

    /// <summary>
    /// Writes files after one confirmation that names every file it replaces. The previous content of replaced
    /// files is recorded in local history first, then each file is written atomically.
    /// </summary>
    private async Task<bool> WriteProjectFilesAsync(IReadOnlyDictionary<string, string> files, string summary)
    {
        var existing = files.Keys.Where(File.Exists).ToList();
        if (existing.Count > 0 && !await Host.Overlay.ConfirmAsync(existing.Count == 1 ? "Replace an existing file?" : $"Replace {existing.Count} existing files?",
                string.Join(Environment.NewLine, existing) + Environment.NewLine + Environment.NewLine + Loc.T("The current content is saved in local history first, so you can restore it from Activity."), "Replace", true))
            return false;
        var history = new Dictionary<string, string>();
        foreach (var path in existing)
        {
            var info = new FileInfo(path);
            if (info.Length <= 4 * 1024 * 1024) history["files/creator/previous/" + HistoryStore.SafeName(path) + ".txt"] = File.ReadAllText(path);
        }
        if (history.Count > 0) Host.History.Record("creator", "Before " + summary.ToLowerInvariant(), history);
        var written = new Dictionary<string, string>();
        foreach (var (path, text) in files)
        {
            if (Encoding.UTF8.GetByteCount(text) > DockerfileModel.MaxSourceBytes) throw new InvalidOperationException(Path.GetFileName(path) + " is larger than the 1 MiB limit.");
            var temporary = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.WriteAllText(temporary, text);
            File.Move(temporary, path, true);
            written["files/creator/" + HistoryStore.SafeName(path) + ".txt"] = text;
        }
        Host.History.Record("creator", summary + ": " + string.Join(", ", files.Keys), written);
        Host.Notify.Success(Loc.Voice(summary + ": " + string.Join(", ", files.Keys) + ".", "Saved.", "儲存咗。"));
        return true;
    }

    // ----------------------------------------------------------------- compose.yaml tab

    private TabItem BuildComposeTab()
    {
        var find = new TextFindBar(composeView.Box, "Search compose.yaml");
        System.Windows.Automation.AutomationProperties.SetName(composeView.Box, Loc.T("Generated compose.yaml"));
        var saveAs = Ui.Button("Save as…", Icons.Save, ButtonKind.Outlined, SaveComposeAs);
        var panel = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        panel.Children.Add(Ui.Caption("Generated from the detected project. It is only written when you save it or use Write to folder.").Dock(System.Windows.Controls.Dock.Top));
        find.Margin = new Thickness(0, 8, 0, 8);
        panel.Children.Add(find.Dock(System.Windows.Controls.Dock.Top));
        saveAs.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(saveAs.Dock(System.Windows.Controls.Dock.Bottom).Margin(0, 8, 0, 0));
        panel.Children.Add(composeView);
        composeTab = new TabItem { Header = "compose.yaml", Content = panel, Visibility = Visibility.Collapsed };
        return composeTab;
    }

    private void ShowCompose(string? text)
    {
        composeText = text ?? "";
        composeView.Text = composeText;
        composeTab.Visibility = composeText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (composeText.Length == 0 && previewTabs.SelectedItem == composeTab) previewTabs.SelectedItem = dockerfileTab;
    }

    private void SaveComposeAs()
    {
        if (composeText.Length == 0) return;
        var dialog = new SaveFileDialog { FileName = "compose.yaml", Filter = "Compose file|*.yaml;*.yml|All files|*.*", OverwritePrompt = true, Title = Loc.T("Save compose.yaml as") };
        var folder = IgnoreContextFolder();
        if (Directory.Exists(folder)) dialog.InitialDirectory = folder;
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var path = Path.GetFullPath(dialog.FileName);
        var temporary = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllText(temporary, composeText);
        File.Move(temporary, path, true);
        Host.History.Record("compose", "Saved " + path + " from the Creator", new Dictionary<string, string> { ["files/compose/" + HistoryStore.SafeName(path) + ".txt"] = composeText });
        Host.Notify.Success(Loc.Voice(Loc.T("Compose file saved to") + " " + path + ".", "Saved.", "儲存咗。"));
    }
}
