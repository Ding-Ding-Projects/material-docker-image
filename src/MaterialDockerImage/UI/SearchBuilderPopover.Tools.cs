using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MaterialDockerImage.Services;

namespace MaterialDockerImage.UI;

/// <summary>
/// The second wave of builder features, as view code only: match navigation, explanation precision and export, replacement explanation,
/// parameters, remembered experiments, flavor comparison and the folder search. The logic lives in the RegexBuilder*.cs files.
/// </summary>
internal sealed partial class SearchBuilderPopover
{
    // ----- state -----
    private readonly Dictionary<RegexExplainNode, TreeViewItem> treeItems = new();
    private IReadOnlyList<RegexExplainNode> explainNodes = [];
    private bool treeSyncing;
    private RegexExplainPrecision explainPrecision = RegexExplainPrecision.Full;
    private ComboBox? precisionCombo, exportCombo;

    private RegexEvaluation? lastResult;
    private int currentMatch = -1;
    private readonly TextBlock navStatus = Ui.Caption("");

    private readonly TextBlock replaceExplain = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox replaceChars = Ui.Input("", "Characters to insert", true);
    private readonly TextBlock replaceCharsError = Ui.Caption("");

    private readonly StackPanel parameterPanel = new();
    private readonly Dictionary<string, string> parameterValues = new(StringComparer.Ordinal);
    private readonly TextBlock parameterStatus = Ui.Caption("");
    private readonly TextBox parameterName = Ui.Input("", "Parameter name", true);
    private readonly TextBox parameterDefault = Ui.Input("", "Default (optional)", true);
    private CheckBox? parameterRaw;
    private string parameterSignature = "";

    private readonly TextBox experimentName = Ui.Input("", "Experiment name (optional)");
    private readonly StackPanel experimentList = new();
    private readonly TextBlock experimentStatus = Ui.Caption("");
    private List<RegexExperiment>? experiments;
    private CheckBox? includeExtras;

    private readonly StackPanel flavorPanel = new();
    private readonly HashSet<string> chosenFlavors = new(RegexFlavors.Flavors.Select(f => f.Id));

    private readonly TextBox grepFolder = Ui.Input("", "Folder to search", true);
    private readonly TextBox grepGlobs = Ui.Input("*", "File patterns", true);
    private readonly TextBox grepMaxKib = Ui.Input("2048", "Largest file (KiB)", true);
    private CheckBox? grepRecursive;
    private readonly TextBlock grepStatus = Ui.Caption("");
    private readonly TextBlock grepSkipped = new() { TextWrapping = TextWrapping.Wrap };
    private readonly DataGrid grepGrid = new() { IsReadOnly = true, AutoGenerateColumns = false, HeadersVisibility = DataGridHeadersVisibility.Column, CanUserAddRows = false, MaxHeight = 240, MinHeight = 0 };
    private readonly TextBox grepPlanOutput = Ui.CodeBox("", 60);
    private readonly Border grepConfirm = new() { Visibility = Visibility.Collapsed };
    private readonly TextBlock grepConfirmText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel grepSnapshots = new();
    private Button? grepApplyButton;
    private CancellationTokenSource? grepRun;
    private RegexGrepPlan? currentPlan;

    private static string UndoRoot => Path.Combine(UserSettings.DataDirectory, "regex-undo");

    private void InitTools()
    {
        sample.IsInactiveSelectionHighlightEnabled = true;
        replacement.TextChanged += (_, _) => RenderReplacementExplain();
        pattern.SelectionChanged += (_, _) => SyncTreeToCaret();
        card.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.F3) return;
            GoToMatch((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? RegexMatchNavigator.Previous(lastResult?.Matches.Count ?? 0, currentMatch) : RegexMatchNavigator.Next(lastResult?.Matches.Count ?? 0, currentMatch));
            e.Handled = true;
        };
        AutomationProperties.SetName(grepGrid, Loc.T("Folder search results"));
        foreach (var (header, path, star) in new[] { ("File", "File", false), ("Line", "Line", false), ("Column", "Column", false), ("Text", "LineText", true) })
            grepGrid.Columns.Add(new DataGridTextColumn { Header = Loc.T(header), Binding = new System.Windows.Data.Binding(path), Width = star ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto });
        AutomationProperties.SetName(grepPlanOutput, Loc.T("Replacement plan"));
        grepPlanOutput.IsReadOnly = true;
        foreach (var live in new[] { navStatus, parameterStatus, experimentStatus, replaceCharsError, grepStatus })
            live.SetValue(AutomationProperties.LiveSettingProperty, AutomationLiveSetting.Polite);
        grepFolder.Text = field.State.GrepFolder;
        grepGlobs.Text = field.State.GrepGlobs;
        grepFolder.TextChanged += (_, _) => { field.State.GrepFolder = grepFolder.Text; InvalidatePlan(); RenderConversion(); };
        grepGlobs.TextChanged += (_, _) => { field.State.GrepGlobs = grepGlobs.Text; InvalidatePlan(); RenderConversion(); };
        RenderReplacementExplain();
        RenderSnapshots();
        InitSubject();
    }

    // ----- explanation: precision, export, caret to tree -----

    private FrameworkElement ExplanationControls()
    {
        explainPrecision = prefs.Precision;
        precisionCombo = Ui.Combo(new[] { "Overview", "Standard", "Full" }, prefs.ExplanationDetail);
        exportCombo = Ui.Combo(RegexTreeExport.Formats, "Markdown");
        AutomationProperties.SetName(precisionCombo, Loc.T("Detail"));
        AutomationProperties.SetName(exportCombo, Loc.T("Export format"));
        precisionCombo.SelectionChanged += (_, _) =>
        {
            explainPrecision = (precisionCombo.SelectedItem as string) switch { "Overview" => RegexExplainPrecision.Overview, "Standard" => RegexExplainPrecision.Standard, _ => RegexExplainPrecision.Full };
            RenderExplanation(field.Query, field.Flags);
        };
        return Ui.VStack(6,
            Ui.Wrap(8,
                Ui.Field("Detail", precisionCombo, "Overview shows the top-level tokens only, Standard three levels, Full every level. The export uses the same level.", "Standard"),
                Ui.Field("Export format", exportCombo, "Text, Markdown or JSON. The export holds the pattern, the options and the tokens at the chosen detail.", "Markdown")),
            Ui.Wrap(8,
                Small("Copy explanation", "Copies the explanation in the chosen format.", () => Copy(ExplanationExport(), "Explanation copied.")),
                Small("Save explanation…", "Saves the explanation in the chosen format to a file you choose.", SaveExplanation)));
    }

    private string ExplanationExport()
    {
        var (text, flags) = Effective();
        return text.Length == 0 ? "" : RegexTreeExport.Export(exportCombo?.SelectedItem as string ?? "Markdown", text, flags, explainPrecision);
    }

    private void SaveExplanation()
    {
        var format = exportCombo?.SelectedItem as string ?? "Markdown";
        var content = ExplanationExport();
        if (content.Length == 0) { testStatus.Text = Loc.T("There is no pattern to explain."); return; }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = format switch { "JSON" => "JSON|*.json", "Text" => "Text|*.txt", _ => "Markdown|*.md" },
            FileName = "pattern-explanation" + format switch { "JSON" => ".json", "Text" => ".txt", _ => ".md" },
            Title = Loc.T("Save explanation"),
        };
        Close();
        if (dialog.ShowDialog() != true) return;
        try { File.WriteAllText(dialog.FileName, content); AppHost.Current.Notify.Success("Explanation saved to " + dialog.FileName + "."); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { AppHost.Current.Notify.Error("The explanation could not be saved: " + e.Message); }
    }

    /// <summary>Moving the caret in the pattern selects the deepest displayed token at that position.</summary>
    private void SyncTreeToCaret()
    {
        if (treeSyncing || syncing || explainNodes.Count == 0) return;
        var node = RegexTreeExport.NodeAt(explainNodes, pattern.SelectionLength > 0 ? pattern.SelectionStart : pattern.CaretIndex);
        if (node == null) return;
        if (!treeItems.ContainsKey(node))
            node = treeItems.Keys.Where(k => k.Start <= node.Start && k.Start + k.Length >= node.Start + node.Length).OrderBy(k => k.Length).FirstOrDefault();
        if (node == null || !treeItems.TryGetValue(node, out var item)) return;
        treeSyncing = true;
        try { item.IsSelected = true; item.BringIntoView(); }
        finally { treeSyncing = false; }
    }

    // ----- match navigation -----

    private FrameworkElement NavigationRow() => Ui.VStack(4,
        Ui.Wrap(8,
            Small("Previous match", "Go to the previous match and wrap at the start. Shift+F3.", () => GoToMatch(RegexMatchNavigator.Previous(lastResult?.Matches.Count ?? 0, currentMatch))),
            Small("Next match", "Go to the next match and wrap at the end. F3.", () => GoToMatch(RegexMatchNavigator.Next(lastResult?.Matches.Count ?? 0, currentMatch)))),
        navStatus);

    private void GoToMatch(int index)
    {
        if (lastResult == null || index < 0 || index >= lastResult.Matches.Count) { navStatus.Text = Loc.T("No match to go to."); return; }
        currentMatch = index;
        var m = lastResult.Matches[index];
        if (m.Index + m.Length <= sample.Text.Length)
        {
            sample.Select(m.Index, m.Length);
            try { sample.ScrollToLine(sample.GetLineIndexFromCharacterIndex(m.Index)); } catch (ArgumentOutOfRangeException) { }
        }
        RenderTest(lastResult);
    }

    private void RenderNavigationStatus() =>
        navStatus.Text = lastResult == null || lastResult.Matches.Count == 0 ? "" : currentMatch < 0 ? Loc.T("Use Next match or F3 to step through the matches.") : RegexMatchNavigator.Describe(lastResult.Matches, currentMatch, lastResult.MatchesTruncated);

    // ----- replacement: explanation and insertion -----

    private FrameworkElement ReplacementExtras()
    {
        var palette = new WrapPanel();
        foreach (var p in RegexReplacementParser.Palette) palette.Children.Add(Small(p.Label, p.Description, () => InsertReplacement(p.Text), true));
        return Ui.VStack(6,
            Ui.Caption("Insert a reference at the caret:"), palette,
            Ui.Field("Characters to insert", replaceChars, "Type escapes such as \\n \\t \\\\ \\xHH \\uHHHH \\u{1F600}. .NET replacement text has no escapes of its own, so they are turned into the characters themselves. The second button encodes ordinary text so a $ stays a dollar sign.", "\\n\\t"),
            replaceCharsError,
            Ui.Wrap(8,
                Small("Insert characters", null, () =>
                {
                    try { InsertReplacement(RegexReplacementParser.DecodeEscapes(replaceChars.Text)); replaceCharsError.Text = ""; }
                    catch (ArgumentException e) { replaceCharsError.Text = Loc.T(e.Message); }
                }),
                Small("Insert as literal text", "Inserts the text with every $ doubled, so it is replaced as typed.", () => { InsertReplacement(RegexReplacementParser.EncodeLiteral(replaceChars.Text)); replaceCharsError.Text = ""; })),
            Ui.Overline("Replacement explained"), replaceExplain);
    }

    private void InsertReplacement(string text)
    {
        replacement.SelectedText = text;
        replacement.Focus();
        replacement.CaretIndex = Math.Min(replacement.Text.Length, replacement.SelectionStart + replacement.SelectionLength);
        replacement.Select(replacement.CaretIndex, 0);
    }

    private void RenderReplacementExplain()
    {
        var text = field.State.Replacement;
        if (text.Length == 0) { replaceExplain.Text = Loc.T("Type a replacement to see it explained."); replaceExplain.SetResourceReference(TextBlock.ForegroundProperty, "OnSurfaceVariant"); return; }
        var (p, flags) = Effective();
        var groups = p.Length == 0 ? RegexReplacementParser.GroupInfo.Unknown : RegexReplacementParser.GroupsOf(p, flags);
        replaceExplain.Text = string.Join("\n", RegexReplacementParser.Explain(text, groups).Select(l => "• " + l));
        replaceExplain.SetResourceReference(TextBlock.ForegroundProperty, "OnSurface");
    }

    // ----- parameters -----

    private FrameworkElement ParametersTab()
    {
        parameterRaw = Ui.Check("Insert values as pattern fragments (not escaped)", false);
        UpdateParameters();
        return Ui.VStack(8,
            Ui.Caption("A parameter is written {{name}} or {{name:default}} in a pattern. The pattern still works as it stands; filling it replaces each placeholder with the value, escaped as literal text unless you choose otherwise."),
            parameterPanel, parameterRaw, parameterStatus,
            Small("Fill the pattern", "Replaces the placeholders in the pattern with the values.", FillParameters),
            Ui.Field("Make the selection a parameter", Ui.HStack(8, parameterName, parameterDefault), "Replaces the selected part of the pattern with {{name}} or {{name:default}}, so the pattern can be reused with other values. Saved patterns keep their placeholders.", "name: tag, default: latest"),
            Small("Turn selection into parameter", null, () =>
            {
                try
                {
                    var placeholder = RegexParameters.MakeParameter(parameterName.Text.Trim(), parameterDefault.Text.Length == 0 ? null : parameterDefault.Text);
                    if (pattern.SelectionLength == 0) { parameterStatus.Text = Loc.T("Select the part of the pattern to make a parameter first."); return; }
                    Insert(placeholder, "");
                    parameterStatus.Text = "";
                }
                catch (ArgumentException e) { parameterStatus.Text = Loc.T(e.Message); }
            }));
    }

    private void UpdateParameters()
    {
        var found = RegexParameters.Find(pattern.Text);
        var signature = string.Join("|", found.Select(p => p.Name + ":" + p.Default));
        if (signature == parameterSignature) return;
        parameterSignature = signature;
        parameterPanel.Children.Clear();
        if (found.Count == 0) { parameterPanel.Children.Add(Ui.Caption("This pattern has no parameters.")); return; }
        foreach (var p in found)
        {
            var name = p.Name;
            var box = Ui.Input(parameterValues.GetValueOrDefault(name, ""), name, true);
            box.TextChanged += (_, _) => parameterValues[name] = box.Text;
            parameterPanel.Children.Add(Ui.Field(name + (p.Default != null ? " (" + Loc.T("Default:") + " " + p.Default + ")" : ""), box, "The value for {{" + name + "}}. It is inserted as literal text unless pattern fragments are chosen below.", p.Default));
        }
    }

    private void FillParameters()
    {
        var filled = RegexParameters.Fill(pattern.Text, parameterValues, field.Flags, parameterRaw?.IsChecked == true);
        if (!filled.Success) { parameterStatus.Text = string.Join(" ", filled.Problems.Select(Loc.T)); return; }
        Use(filled.Pattern, null);
        parameterStatus.Text = Loc.T("Parameters filled.");
    }

    // ----- experiments (persistent history) -----

    private FrameworkElement HistoryTab()
    {
        RenderExperiments();
        return Ui.VStack(8,
            Ui.Caption("Remembering stores the pattern, flags, sample text, replacement and test cases in regex-experiments.json in the app data folder, at most 50 experiments and 1 MiB. Nothing is stored until you press Remember."),
            Ui.Field("Experiment name", experimentName, "Optional. The pattern is used when empty.", "Image tags"),
            Ui.Wrap(8, Small("Remember this experiment", "Stores the current pattern, flags, sample, replacement and test cases.", RememberExperiment), Small("Forget all", "Deletes every remembered experiment.", ForgetAllExperiments)),
            experimentStatus, new ScrollViewer { Content = experimentList, MaxHeight = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
    }

    private void LoadExperiments()
    {
        if (experiments != null) return;
        try { experiments = RegexExperimentStore.Load(RegexExperimentStore.DefaultPath); }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException) { experiments = new(); experimentStatus.Text = Loc.T("The experiment history could not be read:") + " " + e.Message; }
    }

    private void RenderExperiments()
    {
        LoadExperiments();
        experimentList.Children.Clear();
        if (experiments!.Count == 0) { experimentList.Children.Add(Ui.Caption("No remembered experiments yet.")); return; }
        foreach (var x in experiments)
        {
            var entry = x;
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            row.Children.Add(Ui.IconButton(Icons.Delete, "Forget " + entry.Name, () => PersistExperiments(RegexExperimentStore.Remove(experiments!, entry.Id), "Forgot " + entry.Name + ".")).Dock(Dock.Right));
            row.Children.Add(Small("Load", "Restores the pattern, flags, sample, replacement and test cases.", () => LoadExperiment(entry)).Dock(Dock.Right));
            row.Children.Add(Ui.VStack(0,
                Ui.Text(entry.Name, 13, FontWeights.SemiBold, "OnSurface", true, false),
                Ui.Mono(entry.Pattern.Length > 100 ? entry.Pattern[..100] + "…" : entry.Pattern, 12, "OnSurfaceVariant", true),
                Ui.Caption(entry.Saved.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + " · " + entry.Sample.Length + " " + Loc.T("sample characters"))));
            experimentList.Children.Add(row);
        }
    }

    private void RememberExperiment()
    {
        try
        {
            LoadExperiments();
            var entry = RegexExperimentStore.Create(experimentName.Text, field.Query, field.Flags, field.State.Sample, field.State.Replacement, field.State.TestCases);
            PersistExperiments(RegexExperimentStore.Add(experiments!, entry), "Experiment remembered.");
            experimentName.Clear();
        }
        catch (ArgumentException e) { experimentStatus.Text = Loc.T(e.Message); }
    }

    private void ForgetAllExperiments() => PersistExperiments(new List<RegexExperiment>(), "All remembered experiments were forgotten.");

    private void PersistExperiments(List<RegexExperiment> next, string message)
    {
        try { RegexExperimentStore.Save(RegexExperimentStore.DefaultPath, next); experiments = next; experimentStatus.Text = Loc.T(message); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { experimentStatus.Text = Loc.T("Could not save the experiment history:") + " " + e.Message; }
        RenderExperiments();
    }

    private void LoadExperiment(RegexExperiment x)
    {
        field.Flags = x.Flags with { };
        sample.Text = x.Sample;
        replacement.Text = x.Replacement;
        suiteBox.Text = x.TestCases;
        Use(x.Pattern, null);
        experimentStatus.Text = Loc.T("Experiment loaded:") + " " + x.Name;
    }

    // ----- flavor comparison -----

    private FrameworkElement FlavorSection()
    {
        var picks = new WrapPanel();
        foreach (var f in RegexFlavors.Flavors)
        {
            var id = f.Id;
            var box = Ui.Check(f.Name, true, v => { if (v) chosenFlavors.Add(id); else chosenFlavors.Remove(id); RenderFlavors(); });
            box.Margin = new Thickness(0, 0, 12, 6);
            box.ToolTip = f.Version;
            picks.Children.Add(box);
        }
        RenderFlavors();
        return Section("Flavor comparison", "How the constructs this pattern uses are read by other engines. It comes from a hand-written table, checked against each engine while it was written; the pattern itself is never run in another engine.",
            picks, FlavorVersionPickers(), flavorPanel,
            Small("Copy comparison", "Copies the comparison as text, at the engine versions chosen above.", () => { var (text, flags) = Effective(); Copy(RegexFlavorVersions.ToText(text, flags, chosenFlavors, chosenVersions), "Comparison copied."); }));
    }

    private void RenderFlavors()
    {
        flavorPanel.Children.Clear();
        if (field.Query.Length == 0) { flavorPanel.Children.Add(Ui.Caption("Type a pattern to compare it across engines.")); return; }
        if (!field.RegexMode) { flavorPanel.Children.Add(Ui.Caption("Regular expression mode is off, so the field is plain text and every engine reads it the same way.")); return; }
        var verdicts = RegexFlavorVersions.Compare(field.Query, field.Flags, chosenFlavors, chosenVersions);
        if (verdicts.Count == 0) { flavorPanel.Children.Add(Ui.Caption("Choose at least one engine.")); return; }
        if (verdicts[0].Findings.Count == 0) flavorPanel.Children.Add(Ui.Caption("The pattern uses no construct the table tracks, so every engine reads it the same way."));
        foreach (var v in verdicts)
        {
            var summary = v.Findings.Count == 0 ? "" : v.Compatible ? Loc.T("every tracked construct is supported") : $"{Loc.T("Unsupported:")} {v.Unsupported}, {Loc.T("Different:")} {v.Different}";
            var lines = new List<UIElement> { Ui.Text(v.Flavor.Name, 14, FontWeights.SemiBold), Ui.Caption(v.Flavor.Version + (summary.Length > 0 ? " · " + summary : "")) };
            foreach (var (construct, support, note) in v.Findings.Where(f => f.Support != FlavorSupport.Yes))
            {
                var line = Ui.Caption((support == FlavorSupport.No ? "✗ " : "≈ ") + Loc.T(construct.Label) + (note.Length > 0 ? ": " + Loc.T(note) : ""));
                line.SetResourceReference(TextBlock.ForegroundProperty, support == FlavorSupport.No ? "Error" : "OnSurfaceVariant");
                lines.Add(line);
            }
            flavorPanel.Children.Add(Ui.VStack(0, lines.ToArray()).Margin(0, 0, 0, 8));
        }
    }

    // ----- folder search and replace -----

    private FrameworkElement FilesSection()
    {
        grepRecursive = Ui.Check("Include subfolders", true);
        grepApplyButton = Small("Apply…", "Asks for confirmation, saves a byte-exact undo snapshot and then writes the files. Only available after a preview.", AskApply);
        grepApplyButton.IsEnabled = false;
        var confirm = Small("Replace in files", "Writes the previewed replacements. An undo snapshot is saved first.", ConfirmApply);
        var cancelConfirm = Small("Cancel", null, () => grepConfirm.Visibility = Visibility.Collapsed);
        grepConfirm.Child = Ui.VStack(6, grepConfirmText, Ui.Wrap(8, confirm, cancelConfirm));
        grepConfirm.Padding = new Thickness(10);
        grepConfirm.CornerRadius = new CornerRadius(8);
        grepConfirm.SetResourceReference(Border.BackgroundProperty, "ErrorContainer");
        return Section("Search in files", "Searches the files of a folder with this pattern. Replacing is a dry run first; applying saves an undo snapshot. Limits: 2 MiB per file by default, 5000 files, 1000 results, 15 seconds. Binary files and links are skipped.",
            Ui.Field("Folder to search", grepFolder, "A folder on this computer. A whole drive is refused. .git and node_modules are skipped.", "C:\\Projects\\app"),
            Ui.Field("File patterns", grepGlobs, "Patterns separated by ; or ,. * and ? match within a name; ** crosses folders. A leading ! excludes. A pattern with a slash matches the path from the folder.", "*.log;*.txt;!*.min.js"),
            Ui.Field("Largest file (KiB)", grepMaxKib, "Files larger than this are skipped and listed. The ceiling is 16384 KiB.", "2048"),
            grepRecursive,
            Ui.Wrap(8,
                Small("Search files", "Finds the pattern in the files and lists file, line and column.", RunGrep),
                Small("Preview replacement", "Shows what the replacement would change, file by file. Nothing is written.", RunPlan),
                grepApplyButton,
                Small("Cancel search", null, () => grepRun?.Cancel())),
            grepStatus, grepConfirm, grepSkipped, grepGrid, grepPlanOutput,
            GrepExtras(), ActionFileControls(),
            Ui.Overline("Undo snapshots"), grepSnapshots);
    }

    private RegexGrepOptions GrepOptions()
    {
        long kib = long.TryParse(grepMaxKib.Text, out var k) && k > 0 ? k : 2048;
        var (extra, _) = ExtraOptions();
        field.State.GrepRecursive = grepRecursive?.IsChecked != false;
        field.State.GrepContext = extra?.ContextLines ?? 0;
        field.State.GrepIncludeHidden = extra?.IncludeHidden ?? true;
        field.State.GrepIncludeReadOnly = extra?.IncludeReadOnly ?? true;
        field.State.GrepIncludeSystem = extra?.IncludeSystem ?? true;
        field.State.GrepEncoding = extra?.BomlessEncoding ?? RegexGrepEncoding.Auto;
        return new RegexGrepOptions
        {
            Folder = grepFolder.Text, Globs = grepGlobs.Text, Recursive = grepRecursive?.IsChecked != false, MaxFileBytes = kib * 1024,
            ModifiedAfterUtc = extra?.ModifiedAfterUtc, ModifiedBeforeUtc = extra?.ModifiedBeforeUtc, ContextLines = extra?.ContextLines ?? 0,
            IncludeHidden = extra?.IncludeHidden ?? true, IncludeReadOnly = extra?.IncludeReadOnly ?? true, IncludeSystem = extra?.IncludeSystem ?? true, BomlessEncoding = extra?.BomlessEncoding ?? RegexGrepEncoding.Auto,
        };
    }

    private async void RunGrep()
    {
        grepRun?.Cancel();
        var cts = grepRun = new CancellationTokenSource();
        var (text, flags) = Effective();
        var (_, dateProblem) = ExtraOptions();
        if (dateProblem != null) { grepStatus.Text = dateProblem; return; }
        var options = GrepOptions();
        grepStatus.Text = Loc.T("Searching…");
        grepPlanOutput.Text = ""; grepConfirm.Visibility = Visibility.Collapsed;
        try
        {
            var result = await Task.Run(() => RegexGrep.Search(text, flags, options, cts.Token), cts.Token);
            if (cts.IsCancellationRequested && !result.Cancelled) return;
            if (!popup.IsOpen) return;
            var root = result.Valid ? options.Folder.Trim().Trim('"') : "";
            loadedPattern = null; lastGrepResult = result; lastGrepOptions = options with { Folder = root }; grepMatches = result.Matches; field.State.LoadedResults = null; grepContext.Text = "";
            grepGrid.ItemsSource = result.Matches.Select(m => new RegexGrepRow(SafeRelative(root, m.File), m.Line, m.Column, m.Text, m.LineText)).ToList();
            grepGrid.Visibility = result.Matches.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            grepStatus.Text = result.Valid ? result.Summary() : Loc.T(result.Error ?? "The search could not run.");
            grepSkipped.Text = string.Join("\n", result.Skipped.Take(5).Select(s => "• " + SafeRelative(root, s.File) + ": " + RegexGrep.TranslateSkipReason(s.Reason, Loc.T))) + (result.Skipped.Count > 5 ? "\n• …" : "");
            grepSkipped.SetResourceReference(TextBlock.ForegroundProperty, "OnSurfaceVariant");
        }
        catch (OperationCanceledException) { grepStatus.Text = Loc.T("Search cancelled."); }
    }

    private static string SafeRelative(string root, string file)
    {
        try { return root.Length > 0 ? Path.GetRelativePath(root, file) : file; }
        catch (ArgumentException) { return file; }
    }

    private async void RunPlan()
    {
        grepRun?.Cancel();
        var cts = grepRun = new CancellationTokenSource();
        var (text, flags) = Effective();
        var replacementText = field.State.Replacement;
        var (_, dateProblem) = ExtraOptions();
        if (dateProblem != null) { grepStatus.Text = dateProblem; return; }
        var options = GrepOptions();
        grepStatus.Text = Loc.T("Previewing…");
        grepConfirm.Visibility = Visibility.Collapsed;
        try
        {
            var plan = await Task.Run(() => RegexGrep.PlanReplace(text, flags, replacementText, options, cts.Token), cts.Token);
            if (cts.IsCancellationRequested || !popup.IsOpen) return;
            currentPlan = plan.Valid && plan.Files.Count > 0 ? plan : null;
            grepApplyButton!.IsEnabled = currentPlan != null;
            grepGrid.ItemsSource = null; grepGrid.Visibility = Visibility.Collapsed;
            if (!plan.Valid) { grepStatus.Text = Loc.T(plan.Error ?? "The preview could not run."); grepPlanOutput.Text = ""; return; }
            var root = plan.Options.Folder;
            grepPlanOutput.Text = plan.Files.Count == 0 ? "" : string.Join("\n", plan.Files.Select(f =>
                SafeRelative(root, f.File) + ": " + f.Replacements + " replacement" + (f.Replacements == 1 ? "" : "s") + "\n" + string.Join("\n", f.Changes.Select(c => $"  {c.Line}:{c.Column}  {c.Before} -> {c.After}"))));
            grepStatus.Text = plan.Files.Count == 0
                ? Loc.T("Nothing to replace in the searched files.")
                : $"{plan.TotalReplacements} replacement{(plan.TotalReplacements == 1 ? "" : "s")} in {plan.Files.Count} file{(plan.Files.Count == 1 ? "" : "s")}. " + Loc.T("Nothing has been written. Use Apply to write them.") + (plan.Truncated ? " " + Loc.T("The plan was cut at its limit.") : "");
            grepSkipped.Text = string.Join("\n", plan.Skipped.Take(5).Select(s => "• " + SafeRelative(root, s.File) + ": " + RegexGrep.TranslateSkipReason(s.Reason, Loc.T)));
        }
        catch (OperationCanceledException) { grepStatus.Text = Loc.T("Preview cancelled."); }
    }

    private bool PlanIsCurrent()
    {
        if (currentPlan == null) return false;
        var (text, flags) = Effective();
        var options = GrepOptions();
        string folder;
        try { folder = Path.GetFullPath(options.Folder.Trim().Trim('"')); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
        return currentPlan.Pattern == text && currentPlan.Flags == flags && currentPlan.Replacement == field.State.Replacement
            && string.Equals(currentPlan.Options.Folder, folder, StringComparison.OrdinalIgnoreCase) && currentPlan.Options.Globs == options.Globs && currentPlan.Options.Recursive == options.Recursive;
    }

    private void InvalidatePlan()
    {
        if (currentPlan == null || PlanIsCurrent()) return;
        currentPlan = null;
        if (grepApplyButton != null) grepApplyButton.IsEnabled = false;
        grepConfirm.Visibility = Visibility.Collapsed;
    }

    private void AskApply()
    {
        if (!PlanIsCurrent() || currentPlan == null) { grepStatus.Text = Loc.T("The pattern, replacement or folder changed since the preview. Preview again."); grepApplyButton!.IsEnabled = false; return; }
        grepConfirmText.Text = $"{currentPlan.TotalReplacements} replacement{(currentPlan.TotalReplacements == 1 ? "" : "s")} in {currentPlan.Files.Count} file{(currentPlan.Files.Count == 1 ? "" : "s")} will be written. " + Loc.T("A byte-exact undo snapshot is saved first, and Undo restores the files that have not been edited since.");
        grepConfirm.Visibility = Visibility.Visible;
    }

    private async void ConfirmApply()
    {
        grepConfirm.Visibility = Visibility.Collapsed;
        if (!PlanIsCurrent() || currentPlan == null) { grepStatus.Text = Loc.T("The pattern, replacement or folder changed since the preview. Preview again."); return; }
        var plan = currentPlan;
        grepApplyButton!.IsEnabled = false;
        grepStatus.Text = Loc.T("Writing…");
        var result = await Task.Run(() => RegexGrep.Apply(plan, UndoRoot));
        currentPlan = null;
        if (!result.Success)
        {
            grepStatus.Text = Loc.T(result.Error ?? "The replacement failed.") + (result.RolledBack ? " " + Loc.T("The files already written were restored.") : "");
        }
        else
        {
            grepStatus.Text = $"{result.Replacements} replacement{(result.Replacements == 1 ? "" : "s")} written in {result.FilesChanged} file{(result.FilesChanged == 1 ? "" : "s")}. " + Loc.T("Snapshot:") + " " + result.SnapshotId + (result.Skipped.Count > 0 ? ". " + Loc.T("Skipped:") + " " + result.Skipped.Count : "");
            AppHost.Current.History.Record("regex-replace", $"Replaced text in {result.FilesChanged} file(s), {result.Replacements} replacement(s), undo snapshot {result.SnapshotId}");
        }
        RenderSnapshots();
    }

    private void RenderSnapshots()
    {
        grepSnapshots.Children.Clear();
        List<RegexGrepSnapshotInfo> list;
        try { list = RegexGrep.ListSnapshots(UndoRoot).Take(5).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { list = []; }
        if (list.Count == 0) { grepSnapshots.Children.Add(Ui.Caption("No undo snapshots yet.")); return; }
        foreach (var s in list)
        {
            var snapshot = s;
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            if (snapshot.State == "applied") row.Children.Add(Small("Undo", "Restores the original bytes of every file in this snapshot that has not been edited since.", () => UndoSnapshot(snapshot)).Dock(Dock.Right));
            row.Children.Add(Ui.VStack(0,
                Ui.Text(snapshot.Created.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + " · " + snapshot.State, 13, FontWeights.SemiBold, "OnSurface", true, false),
                Ui.Caption($"{snapshot.Files} file{(snapshot.Files == 1 ? "" : "s")}, {snapshot.Replacements} replacement{(snapshot.Replacements == 1 ? "" : "s")}")));
            grepSnapshots.Children.Add(row);
        }
    }

    private async void UndoSnapshot(RegexGrepSnapshotInfo snapshot)
    {
        grepStatus.Text = Loc.T("Restoring…");
        var result = await Task.Run(() => RegexGrep.Undo(UndoRoot, snapshot.Id));
        grepStatus.Text = result.Success
            ? string.Format(Loc.T(result.FilesRestored == 1 ? "{0} file restored." : "{0} files restored."), result.FilesRestored) + (result.Skipped.Count > 0 ? " " + string.Join(" ", result.Skipped.Take(3).Select(s => Path.GetFileName(s.File) + ": " + RegexGrep.TranslateSkipReason(s.Reason, Loc.T))) : "")
            : Loc.T(result.Error ?? "The snapshot could not be restored.");
        if (result.Success) AppHost.Current.History.Record("regex-undo", $"Restored {result.FilesRestored} file(s) from undo snapshot {snapshot.Id}");
        RenderSnapshots();
    }
}

/// <summary>Row of the folder search results table.</summary>
public sealed record RegexGrepRow(string File, int Line, int Column, string Match, string LineText);
