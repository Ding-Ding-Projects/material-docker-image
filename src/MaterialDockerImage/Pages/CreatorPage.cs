using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Interactive Dockerfile creator. The draft is a <see cref="DockerfileDocument"/>; every change re-renders the
/// preview and re-runs the checks. The draft is saved as JSON in the data directory and recorded in local history
/// (debounced). Layout: stages and starters (left), stage editor with instruction cards (centre), preview and
/// checks (right). See the partial files under Pages/Creator.
/// </summary>
public sealed partial class CreatorPage : PageBase
{
    public override string Key => "Creator";
    private static string DraftPath => Path.Combine(UserSettings.DataDirectory, "creator-draft.json");

    private DockerfileDocument doc = new() { Stages = new() };
    private readonly DockerfileModelHistory history = new(200);
    private int selectedStage;
    private DockerInstruction? expanded;
    private string savedPath = "";
    private string lastRecorded = "";
    private bool pendingCommit, loaded;
    private readonly DispatcherTimer commitTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly DispatcherTimer persistTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private readonly DispatcherTimer recordTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly TextBlock subtitle;
    private readonly Button undoButton, redoButton, checkButton;
    /// <summary>The Dockerfile text last written to <see cref="savedPath"/>, to tell saved from unsaved changes.</summary>
    private string savedText = "";

    public CreatorPage()
    {
        undoButton = Ui.IconButton(Icons.Undo, "Undo (Ctrl+Z)", Undo);
        redoButton = Ui.IconButton(Icons.Redo, "Redo (Ctrl+Y)", Redo);
        var more = Ui.IconButton(Icons.MoreVert, "More Creator actions");
        var moreMenu = new ContextMenu();
        var import = Ui.MenuItem("Open existing Dockerfile", Icons.FolderOpen, () => PageTasks.Fire(ImportAsync));
        import.ToolTip = Loc.T("Import a Dockerfile into the Creator. Lines the Creator cannot model are kept exactly as written.");
        moreMenu.Items.Add(import);
        moreMenu.Items.Add(Ui.MenuItem("Save as…", Icons.Save, SaveAs));
        moreMenu.Items.Add(Ui.MenuItem("Copy as build command", Icons.Terminal, CopyBuildCommand));
        moreMenu.Items.Add(Ui.MenuItem("Choose project folder…", Icons.ManageSearch, () => PageTasks.Fire(ChooseAndDetectAsync)));
        var startFrom = new MenuItem { Header = Loc.T("Start from") };
        foreach (var starter in DockerfileStarters.All)
        {
            var item = Ui.MenuItem(starter.Label, Icons.RocketLaunch, () => PageTasks.Fire(() => ApplyStarterAsync(starter)));
            item.ToolTip = starter.Description;
            startFrom.Items.Add(item);
        }
        moreMenu.Items.Add(new Separator());
        moreMenu.Items.Add(startFrom);
        more.Click += (_, _) => { moreMenu.PlacementTarget = more; moreMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; moreMenu.IsOpen = true; };
        checkButton = Ui.Button("Check", Icons.FactCheck, ButtonKind.Outlined, OpenChecks);
        checkButton.ToolTip = Loc.T("Runs the checks on the draft and lists the results with their fixes.");
        var save = Ui.Button("Save", Icons.Save, ButtonKind.Tonal, Save);
        save.ToolTip = Loc.T("Saves the Dockerfile to the file it was last saved to, or asks where to save it.");
        var openInBuild = Ui.AsyncButton("Build", Icons.Construction, ButtonKind.Filled, OpenInBuildAsync);
        openInBuild.ToolTip = Loc.T("Opens the draft in Build, where you start the build.");
        var header = Ui.Header("Dockerfile Creator", "", out subtitle, undoButton, redoButton, more, checksCount, checkButton, save, openInBuild);

        // Three columns as on the design board: project and outline (300), the open step (rest), preview (420).
        var left = BuildStagesColumn();
        var centre = Ui.Card(BuildEditorColumn(), 16, "SurfaceLow", 16);
        var right = BuildPreviewColumn();

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 340 });
        var rightGap = new ColumnDefinition { Width = new GridLength(12) };
        var rightColumn = new ColumnDefinition { Width = new GridLength(420) };
        body.ColumnDefinitions.Add(rightGap);
        body.ColumnDefinitions.Add(rightColumn);
        body.Children.Add(left);
        body.Children.Add(centre.At(0, 2));
        body.Children.Add(right.At(0, 4));

        // Narrow windows: the preview shares the centre column, switched by a segmented control.
        var showPreview = false;
        var narrow = false;
        Action applyWidth = () => { };
        var narrowSwitch = Ui.Segmented(["Steps", "Preview"], "Steps", option => { showPreview = option != "Steps"; applyWidth(); });
        narrowSwitch.Width = 300; narrowSwitch.HorizontalAlignment = HorizontalAlignment.Left; narrowSwitch.Margin = new Thickness(0, 0, 0, 12);
        var bodyHost = new DockPanel();
        bodyHost.Children.Add(narrowSwitch.Dock(System.Windows.Controls.Dock.Top));
        bodyHost.Children.Add(body);
        applyWidth = () =>
        {
            narrowSwitch.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;
            rightGap.Width = new GridLength(narrow ? 0 : 12);
            rightColumn.Width = new GridLength(narrow ? 0 : 420);
            Grid.SetColumn(right, narrow ? 2 : 4);
            right.Visibility = !narrow || showPreview ? Visibility.Visible : Visibility.Collapsed;
            centre.Visibility = !narrow || !showPreview ? Visibility.Visible : Visibility.Collapsed;
        };
        bodyHost.SizeChanged += (_, e) => { var now = e.NewSize.Width < 1100; if (now != narrow) { narrow = now; applyWidth(); } };
        applyWidth();
        Layout(header, bodyHost);

        commitTimer.Tick += (_, _) => { commitTimer.Stop(); FlushCommit(); };
        persistTimer.Tick += (_, _) => { persistTimer.Stop(); PersistDraft(); };
        recordTimer.Tick += (_, _) => { recordTimer.Stop(); RecordDraft(null); };
        Unloaded += (_, _) => { if (textDirty) { parseTimer.Stop(); ApplyPreviewText(); } FlushCommit(); if (persistTimer.IsEnabled) { persistTimer.Stop(); PersistDraft(); } };
        PreviewKeyDown += OnKeys;
        history.Reset(doc);
        RebuildAll();
    }

    // ----------------------------------------------------------------- lifecycle

    public override Task RefreshAsync()
    {
        if (loaded) return Task.CompletedTask;
        loaded = true;
        try
        {
            if (!File.Exists(DraftPath)) { RebuildAll(); return Task.CompletedTask; }
            if (new FileInfo(DraftPath).Length > 4 * 1024 * 1024) throw new InvalidDataException("The saved draft is larger than 4 MiB.");
            var json = File.ReadAllText(DraftPath);
            try
            {
                doc = DockerfileModel.Deserialize(json);
                lastRecorded = DockerfileModel.Serialize(doc);
            }
            catch (Exception e) when (e is JsonException or NotSupportedException)
            {
                if (!Host.IsCapture) Host.History.Record("creator", "Unreadable Creator draft set aside", new Dictionary<string, string> { ["files/creator/creator-draft-unreadable.json"] = json });
                Host.Notify.Warning("The saved Creator draft could not be read, so a new draft was started. The unreadable file is kept in local history.");
                doc = new DockerfileDocument { Stages = new() };
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Host.Notify.Warning(Loc.T("The Creator draft could not be loaded:") + " " + e.Message);
        }
        selectedStage = Math.Max(0, doc.Stages.Count - 1);
        expanded = null;
        history.Reset(doc);
        RebuildAll();
        return Task.CompletedTask;
    }

    public override IEnumerable<AppCommand> PageCommands()
    {
        foreach (var starter in DockerfileStarters.All)
            yield return new AppCommand { Id = "creator.starter." + starter.Id, Title = "Creator: start from " + starter.Label, Group = "Creator", Icon = Icons.RocketLaunch, Keywords = "dockerfile template preset starter " + starter.Description, Run = () => { Host.Navigate("Creator"); PageTasks.Fire(() => ApplyStarterAsync(starter)); } };
        yield return new AppCommand { Id = "creator.import", Title = "Creator: open existing Dockerfile…", Group = "Creator", Icon = Icons.FolderOpen, Keywords = "import parse", Run = () => { Host.Navigate("Creator"); PageTasks.Fire(ImportAsync); } };
        yield return new AppCommand { Id = "creator.detect", Title = "Creator: detect project in a folder…", Group = "Creator", Icon = Icons.ManageSearch, Keywords = "docker init detect language package.json go.mod csproj", Run = () => { Host.Navigate("Creator"); PageTasks.Fire(ChooseAndDetectAsync); } };
        yield return new AppCommand { Id = "creator.buildcommand", Title = "Creator: copy as build command", Group = "Creator", Icon = Icons.Terminal, Keywords = "buildx command clipboard secret allow", Run = CopyBuildCommand };
        yield return new AppCommand { Id = "creator.tobuild", Title = "Creator: open draft in Build", Group = "Creator", Icon = Icons.Construction, Keywords = "send build", Run = () => PageTasks.Fire(OpenInBuildAsync) };
    }

    private void OnKeys(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        var inText = Keyboard.FocusedElement is TextBox;
        if (ctrl && e.Key == System.Windows.Input.Key.Z && !inText) { Undo(); e.Handled = true; }
        else if (ctrl && e.Key == System.Windows.Input.Key.Y && !inText) { Redo(); e.Handled = true; }
        else if (ctrl && e.Key == System.Windows.Input.Key.F) { previewFind.FocusSearch(); e.Handled = true; }
    }

    // ----------------------------------------------------------------- change pipeline

    /// <summary>
    /// Called after a field edit (typing). Updates preview, checks and card summaries without rebuilding the
    /// editors, and schedules an undo snapshot once typing pauses.
    /// </summary>
    private void Touched()
    {
        pendingCommit = true;
        commitTimer.Stop(); commitTimer.Start();
        RefreshDerived();
        UpdateCardSummaries();
        RefreshStageList();
        UpdateUndoButtons();
    }

    /// <summary>Called after a structural change (add, remove, move, starter, fix). Snapshots and rebuilds.</summary>
    private void Changed()
    {
        commitTimer.Stop();
        pendingCommit = true;
        FlushCommit();
        RebuildAll();
    }

    private void FlushCommit()
    {
        if (!pendingCommit) return;
        pendingCommit = false;
        if (history.Commit(doc)) { SchedulePersist(); UpdateUndoButtons(); }
    }

    private void SchedulePersist()
    {
        persistTimer.Stop(); persistTimer.Start();
        recordTimer.Stop(); recordTimer.Start();
    }

    private void RebuildAll()
    {
        selectedStage = doc.Stages.Count == 0 ? 0 : Math.Clamp(selectedStage, 0, doc.Stages.Count - 1);
        RefreshStageList();
        RebuildStageEditor();
        RefreshDerived();
        UpdateUndoButtons();
        RefreshIgnoreEditor();
    }

    private void RefreshDerived()
    {
        RefreshPreview();
        RefreshChecks();
        UpdateSubtitle();
        RefreshPresetChips();
    }

    private void UpdateUndoButtons()
    {
        undoButton.IsEnabled = history.CanUndo || pendingCommit;
        redoButton.IsEnabled = history.CanRedo && !pendingCommit;
    }

    private void UpdateSubtitle()
    {
        var parts = new List<string>();
        if (doc.Origin.Length > 0) parts.Add(Loc.Lead(doc.Origin));
        parts.Add($"{doc.Stages.Count} {Loc.T(doc.Stages.Count == 1 ? "stage" : "stages")}");
        var count = DockerfileModel.InstructionCount(doc);
        parts.Add($"{count} {Loc.T(count == 1 ? "instruction" : "instructions")}");
        parts.Add(DockerfileOutline.SaveState(savedPath, savedText, DockerfileModel.Render(doc)) switch
        {
            CreatorSaveState.DraftOnly => Loc.T("draft kept automatically"),
            CreatorSaveState.Saved => Loc.T("last saved to") + " " + savedPath,
            _ => Loc.T("unsaved changes") + " · " + savedPath,
        });
        subtitle.Text = string.Join(" · ", parts);
        subtitle.ToolTip = subtitle.Text;
        subtitle.TextTrimming = TextTrimming.CharacterEllipsis;
        subtitle.TextWrapping = TextWrapping.NoWrap;
    }

    private void Undo()
    {
        FlushCommit();
        var previous = history.Undo();
        if (previous == null) return;
        doc = previous; expanded = null; highlighted = null;
        SchedulePersist();
        RebuildAll();
    }

    private void Redo()
    {
        FlushCommit();
        var next = history.Redo();
        if (next == null) return;
        doc = next; expanded = null; highlighted = null;
        SchedulePersist();
        RebuildAll();
    }

    // ----------------------------------------------------------------- persistence

    private void PersistDraft()
    {
        if (Host.IsCapture) return;
        try
        {
            Directory.CreateDirectory(UserSettings.DataDirectory);
            var temporary = DraftPath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.WriteAllText(temporary, DockerfileModel.Serialize(doc));
            File.Move(temporary, DraftPath, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Host.Notify.Error(Loc.T("The Creator draft could not be saved:") + " " + e.Message);
        }
    }

    /// <summary>Records the current draft in local history when it differs from the last recorded draft.</summary>
    private void RecordDraft(string? summary)
    {
        if (Host.IsCapture) return;
        var json = DockerfileModel.Serialize(doc);
        if (summary == null && json == lastRecorded) return;
        Host.History.Record("creator", summary ?? $"Creator draft · {(doc.Origin.Length > 0 ? doc.Origin + " · " : "")}{doc.Stages.Count} stage(s), {DockerfileModel.InstructionCount(doc)} instruction(s)",
            new Dictionary<string, string> { ["files/creator/creator-draft.json"] = json, ["files/creator/Dockerfile.txt"] = DockerfileModel.Render(doc) });
        lastRecorded = json;
    }

    /// <summary>Replaces the whole draft after recording the old one in history.</summary>
    private void ReplaceDraft(DockerfileDocument replacement, string reason)
    {
        FlushCommit();
        var hadContent = doc.Stages.Count > 0 || doc.Preamble.Count > 0;
        if (hadContent) RecordDraft("Creator draft before " + reason);
        doc = replacement;
        selectedStage = Math.Max(0, doc.Stages.Count - 1);
        expanded = null; highlighted = null;
        savedPath = ""; savedText = "";
        Changed();
        recordTimer.Stop();
        RecordDraft(null);
        PersistDraft();
        if (hadContent) Host.Notify.Info(Loc.T("The previous draft was saved in local history. Open Activity to restore it."));
    }

    private async Task ApplyStarterAsync(DockerfileStarter starter)
    {
        var hasContent = doc.Stages.Count > 0 || doc.Preamble.Count > 0;
        // Capture mode never saves the draft or history, so the screenshot run replaces it without a prompt that would stay on screen.
        if (hasContent && !Host.IsCapture && !await Host.Overlay.ConfirmAsync($"Start from {starter.Label}?", starter.Description + Environment.NewLine + Environment.NewLine + Loc.T("This replaces the current draft. The current draft is saved in local history first, so you can restore it from Activity."), "Replace draft"))
            return;
        ReplaceDraft(starter.Create(), "applying the " + starter.Label + " starter");
        Host.Notify.Success(Loc.T("Started from") + " " + starter.Label + ". " + starter.Description);
    }

    private async Task ImportAsync()
    {
        var dialog = new OpenFileDialog { Filter = "Dockerfile|Dockerfile;*.dockerfile;Dockerfile.*;Containerfile|All files|*.*", Title = Loc.T("Open existing Dockerfile") };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var info = new FileInfo(dialog.FileName);
        if (info.Length > DockerfileModel.MaxSourceBytes) throw new InvalidOperationException("The Dockerfile is larger than the 1 MiB import limit.");
        var result = DockerfileModel.Parse(File.ReadAllText(info.FullName));
        var hasContent = doc.Stages.Count > 0 || doc.Preamble.Count > 0;
        if (hasContent && !await Host.Overlay.ConfirmAsync("Replace the draft with this Dockerfile?", info.FullName + Environment.NewLine + Environment.NewLine + Loc.T("The current draft is saved in local history first."), "Import"))
            return;
        result.Document.Origin = "Imported from " + info.Name;
        ReplaceDraft(result.Document, "importing " + info.FullName);
        if (result.Warnings.Count > 0)
            Host.Notify.Warning($"Imported with {result.Warnings.Count} note(s): " + string.Join(" ", result.Warnings.Take(3)) + (result.Warnings.Count > 3 ? " …" : ""));
        else Host.Notify.Success(Loc.T("Imported") + " " + info.FullName + ". The source was parsed, not executed.");
    }

    private void SaveAs()
    {
        FlushCommit();
        var text = DockerfileModel.Render(doc);
        var dialog = new SaveFileDialog { FileName = savedPath.Length > 0 ? Path.GetFileName(savedPath) : "Dockerfile", Filter = "Dockerfile|Dockerfile;*.dockerfile;Dockerfile.*|All files|*.*", OverwritePrompt = true, Title = Loc.T("Save Dockerfile as") };
        var folder = savedPath.Length > 0 ? Path.GetDirectoryName(savedPath) : Host.Settings.BuildFolder;
        if (Directory.Exists(folder)) dialog.InitialDirectory = folder;
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        WriteDockerfile(Path.GetFullPath(dialog.FileName), text);
    }

    /// <summary>Saves to the file the draft was last saved to; without one, asks where (Save as).</summary>
    private void Save()
    {
        FlushCommit();
        if (savedPath.Length == 0) { SaveAs(); return; }
        var text = DockerfileModel.Render(doc);
        // Keep what was on disk in local history first, in case the file changed outside the Creator.
        if (File.Exists(savedPath) && new FileInfo(savedPath).Length <= 4 * 1024 * 1024)
        {
            var previous = File.ReadAllText(savedPath);
            if (previous != text && previous != savedText) Host.History.Record("dockerfile", "Before saving " + savedPath + " from the Creator", new Dictionary<string, string> { ["files/dockerfile/previous/" + HistoryStore.SafeName(savedPath) + ".txt"] = previous });
        }
        WriteDockerfile(savedPath, text);
    }

    private void WriteDockerfile(string path, string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > DockerfileModel.MaxSourceBytes) throw new InvalidOperationException("The Dockerfile is larger than the 1 MiB limit.");
        var temporary = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllText(temporary, text);
        File.Move(temporary, path, true);
        savedPath = path;
        savedText = text;
        Host.History.Record("dockerfile", "Saved " + path + " from the Creator", new Dictionary<string, string> { ["files/dockerfile/" + HistoryStore.SafeName(path) + ".txt"] = text, ["files/creator/creator-draft.json"] = DockerfileModel.Serialize(doc) });
        UpdateSubtitle();
        Host.Notify.Success(Loc.Voice(Loc.T("Dockerfile saved to") + " " + path + ".", "Saved.", "儲存咗。"));
    }

    private async Task OpenInBuildAsync()
    {
        FlushCommit();
        if (doc.Stages.Count == 0) throw new InvalidOperationException("Add a stage or apply a starter before opening the draft in Build.");
        var errors = DockerfileLint.Check(doc).Count(r => r.Severity == LintSeverity.Error);
        if (errors > 0 && !await Host.Overlay.ConfirmAsync("Open a draft with errors?", $"{errors} check(s) report errors that will make the build fail. Open it in Build anyway?", "Open in Build"))
            return;
        Host.HandOff["Build.Source"] = DockerfileModel.Render(doc);
        Host.Navigate("Build");
        if (doc.Target.Trim().Length > 0 && Host.HandOff.TryGetValue("Build.SetTarget", out var setter) && setter is Action<string> setTarget) setTarget(doc.Target.Trim());
    }
}
