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
    private readonly Button undoButton, redoButton;

    public CreatorPage()
    {
        undoButton = Ui.IconButton(Icons.Undo, "Undo (Ctrl+Z)", Undo);
        redoButton = Ui.IconButton(Icons.Redo, "Redo (Ctrl+Y)", Redo);
        var import = Ui.Button("Open existing", Icons.FolderOpen, ButtonKind.Text, () => PageTasks.Fire(ImportAsync));
        import.ToolTip = Loc.T("Import a Dockerfile into the Creator. Lines the Creator cannot model are kept exactly as written.");
        var saveAs = Ui.Button("Save as…", Icons.Save, ButtonKind.Outlined, SaveAs);
        var openInBuild = Ui.AsyncButton("Open in Build", Icons.Construction, ButtonKind.Filled, OpenInBuildAsync);
        var header = Ui.Header("Dockerfile Creator", "", out subtitle, undoButton, redoButton, import, saveAs, openInBuild);

        var left = Ui.Card(BuildStagesColumn(), 16, "SurfaceLow", 16);
        var centre = Ui.Card(BuildEditorColumn(), 16, "SurfaceLow", 16);
        var right = BuildPreviewColumn();

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 200, MaxWidth = 300 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MinWidth = 340 });
        var rightGap = new ColumnDefinition { Width = new GridLength(16) };
        var rightColumn = new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star), MinWidth = 300 };
        body.ColumnDefinitions.Add(rightGap);
        body.ColumnDefinitions.Add(rightColumn);
        body.Children.Add(left);
        body.Children.Add(centre.At(0, 2));
        body.Children.Add(right.At(0, 4));

        // Narrow windows: the preview and checks share the centre column, switched by a segmented control.
        var showPreview = false;
        var narrow = false;
        Action applyWidth = () => { };
        var narrowSwitch = Ui.Segmented(["Steps", "Preview and checks"], "Steps", option => { showPreview = option != "Steps"; applyWidth(); });
        narrowSwitch.Width = 300; narrowSwitch.HorizontalAlignment = HorizontalAlignment.Left; narrowSwitch.Margin = new Thickness(0, 0, 0, 12);
        var bodyHost = new DockPanel();
        bodyHost.Children.Add(narrowSwitch.Dock(System.Windows.Controls.Dock.Top));
        bodyHost.Children.Add(body);
        applyWidth = () =>
        {
            narrowSwitch.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;
            rightGap.Width = new GridLength(narrow ? 0 : 16);
            rightColumn.Width = narrow ? new GridLength(0) : new GridLength(1.6, GridUnitType.Star);
            rightColumn.MinWidth = narrow ? 0 : 300;
            Grid.SetColumn(right, narrow ? 2 : 4);
            right.Visibility = !narrow || showPreview ? Visibility.Visible : Visibility.Collapsed;
            centre.Visibility = !narrow || !showPreview ? Visibility.Visible : Visibility.Collapsed;
        };
        bodyHost.SizeChanged += (_, e) => { var now = e.NewSize.Width < 1000; if (now != narrow) { narrow = now; applyWidth(); } };
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
            Host.Notify.Warning("The Creator draft could not be loaded: " + e.Message);
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
    }

    private void UpdateUndoButtons()
    {
        undoButton.IsEnabled = history.CanUndo || pendingCommit;
        redoButton.IsEnabled = history.CanRedo && !pendingCommit;
    }

    private void UpdateSubtitle()
    {
        var parts = new List<string>();
        if (doc.Origin.Length > 0) parts.Add(doc.Origin);
        parts.Add($"{doc.Stages.Count} {Loc.T(doc.Stages.Count == 1 ? "stage" : "stages")}");
        var count = DockerfileModel.InstructionCount(doc);
        parts.Add($"{count} {Loc.T(count == 1 ? "instruction" : "instructions")}");
        parts.Add(savedPath.Length > 0 ? Loc.T("last saved to") + " " + savedPath : Loc.T("draft kept automatically"));
        subtitle.Text = string.Join(" · ", parts);
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
            Host.Notify.Error("The Creator draft could not be saved: " + e.Message);
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
        savedPath = "";
        Changed();
        recordTimer.Stop();
        RecordDraft(null);
        PersistDraft();
        if (hadContent) Host.Notify.Info(Loc.T("The previous draft was saved in local history. Open Activity to restore it."));
    }

    private async Task ApplyStarterAsync(DockerfileStarter starter)
    {
        var hasContent = doc.Stages.Count > 0 || doc.Preamble.Count > 0;
        if (hasContent && !await Host.Overlay.ConfirmAsync($"Start from {starter.Label}?", starter.Description + Environment.NewLine + Environment.NewLine + Loc.T("This replaces the current draft. The current draft is saved in local history first, so you can restore it from Activity."), "Replace draft"))
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
        else Host.Notify.Success("Imported " + info.FullName + ". The source was parsed, not executed.");
    }

    private void SaveAs()
    {
        FlushCommit();
        var text = DockerfileModel.Render(doc);
        var dialog = new SaveFileDialog { FileName = savedPath.Length > 0 ? Path.GetFileName(savedPath) : "Dockerfile", Filter = "Dockerfile|Dockerfile;*.dockerfile;Dockerfile.*|All files|*.*", OverwritePrompt = true, Title = Loc.T("Save Dockerfile as") };
        var folder = savedPath.Length > 0 ? Path.GetDirectoryName(savedPath) : Host.Settings.BuildFolder;
        if (Directory.Exists(folder)) dialog.InitialDirectory = folder;
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var path = Path.GetFullPath(dialog.FileName);
        if (Encoding.UTF8.GetByteCount(text) > DockerfileModel.MaxSourceBytes) throw new InvalidOperationException("The Dockerfile is larger than the 1 MiB limit.");
        var temporary = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllText(temporary, text);
        File.Move(temporary, path, true);
        savedPath = path;
        Host.History.Record("dockerfile", "Saved " + path + " from the Creator", new Dictionary<string, string> { ["files/dockerfile/" + HistoryStore.SafeName(path) + ".txt"] = text, ["files/creator/creator-draft.json"] = DockerfileModel.Serialize(doc) });
        UpdateSubtitle();
        Host.Notify.Success(Loc.Voice("Dockerfile saved to " + path + ".", "Saved.", "儲存咗。"));
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
