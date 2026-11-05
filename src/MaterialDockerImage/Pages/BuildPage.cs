using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Build destination: configuration card on the left; on the right, tabs for the Dockerfile editor (with find, line
/// numbers and an optional stage outline), the live BuildKit run timeline and recent builds. Engine actions are in
/// Pages/Build/BuildPage.Run.cs. Hand-offs: Build.OpenDockerfile, Build.Source (in); Build.CurrentSource,
/// Build.SetSource, Build.SetTarget, Build.GoToLine (published for Canvas).
/// </summary>
public sealed partial class BuildPage : PageBase, IDirtyAware
{
    public override string Key => "Build";
    private const int MaxBytes = 1024 * 1024;

    private readonly BuildOptionsPanel options = new();
    private readonly TabControl tabs = new();
    private readonly TabItem runTab, historyTab;
    private readonly BuildTimelineView timeline;
    private readonly BuildRunner runner;
    private readonly BuildHistoryCard history = new();
    private readonly Button check, buildRun, build;
    private bool historyLoaded;
    private readonly CodeEditor editor = new(false, 280);
    private readonly TextFindBar find;
    private readonly StageMap stageMap = new();
    private readonly TextBlock subtitle;
    private readonly TextBlock editorState = Ui.Caption("");
    private readonly ColumnDefinition mapColumn, mapGap;
    private readonly DispatcherTimer parseTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private string dockerfilePath = "";
    private bool dirty, loading, resumed, mapVisible;
    private DateTime? savedAt;
    private string draftOrigin = "";
    private int caretLine = 1, caretColumn = 1, stageCount;

    public bool IsDirty => dirty;
    public string DirtyLabel => Loc.T("Build:") + " " + (dockerfilePath.Length > 0 ? dockerfilePath : "unsaved Dockerfile draft");

    public BuildPage()
    {
        find = new TextFindBar(editor.Box, "Find in Dockerfile");
        System.Windows.Automation.AutomationProperties.SetName(editor.Box, Loc.T("Dockerfile source"));
        editor.EnableSuggestions(EditorSuggestionLanguage.Dockerfile, () => options.ContextFolder.Text.Trim());

        check = Ui.AsyncButton("Check Dockerfile", Icons.PlaylistAddCheck, ButtonKind.Outlined, CheckAsync);
        check.ToolTip = Loc.T("Runs docker buildx build --check on the saved file. Nothing is built.");
        buildRun = Ui.AsyncButton("Build and run", Icons.PlayArrow, ButtonKind.Tonal, BuildAndRunAsync);
        buildRun.ToolTip = Loc.T("Builds and loads the image, then opens the run form in Images for the first tag.");
        build = Ui.AsyncButton("Build", Icons.Construction, ButtonKind.Filled, async () => await BuildAsync());
        var header = Ui.Header("Build image", "Choose a context folder and a Dockerfile", out subtitle, check, buildRun, build);
        timeline = new BuildTimelineView("Start a build to see each BuildKit step with its status, cache use and duration. Click a step to filter the log to its lines.",
            Ui.AsyncButton("Build now", Icons.Construction, ButtonKind.Filled, async () => await BuildAsync()));
        runner = new BuildRunner(timeline);
        BuildRunner.ActiveChanged += SetRunning;
        options.CallChanged += UpdateBuildLabel;

        var config = Ui.Card(Ui.Scroll(options), 16, "SurfaceLow", 16);
        options.Tags.SetTags(TagInput.Split(Host.Settings.ImageTag));
        options.ContextFolder.Text = Host.Settings.BuildFolder;
        options.FolderChosen += folder => PageTasks.Fire(() => FolderChosenAsync(folder));
        options.OpenRequested += () => PageTasks.Fire(OpenAsync);

        // Editor section
        var undo = Ui.IconButton(Icons.Undo, "Undo (Ctrl+Z)", () => editor.Box.Undo());
        var redo = Ui.IconButton(Icons.Redo, "Redo (Ctrl+Y)", () => editor.Box.Redo());
        var toolbar = Ui.Wrap(4,
            Ui.IconButton(Icons.FolderOpen, "Open Dockerfile (Ctrl+O)", () => PageTasks.Fire(OpenAsync)),
            Ui.IconButton(Icons.Save, "Save (Ctrl+S)", () => PageTasks.Fire(() => SaveAsync(false))),
            Ui.IconButton(Icons.FileExport, "Save as (Ctrl+Shift+S)", () => PageTasks.Fire(() => SaveAsync(true))),
            undo, redo,
            Ui.AsyncIconButton(Icons.OpenInNew, "Open in Visual Studio Code", OpenInEditorAsync),
            Ui.IconButton(Icons.VerticalSplit, "Show stages beside source", ToggleMap));
        toolbar.HorizontalAlignment = HorizontalAlignment.Right;
        foreach (var icon in toolbar.Children.OfType<Button>()) { icon.Width = icon.Height = icon.MinHeight = 36; icon.Margin = new Thickness(0, 0, 4, 0); }
        // A 48px bar with a hairline under it: title, stage and caret state, then the 36px icon buttons.
        var titleBar = new Border { Height = 48, Padding = new Thickness(16, 0, 8, 0), BorderThickness = new Thickness(0, 0, 0, 1) };
        titleBar.SetResourceReference(Border.BorderBrushProperty, "SurfaceHigh");
        editorState.Margin = new Thickness(0, 0, 8, 0);
        titleBar.Child = Ui.Columns(8, [1, 0, 0], Ui.Text("Dockerfile", 14, FontWeights.SemiBold, "OnSurface", false), editorState, toolbar);

        var editorGrid = new Grid();
        editorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 280 });
        mapGap = new ColumnDefinition { Width = new GridLength(0) };
        mapColumn = new ColumnDefinition { Width = new GridLength(0) };
        editorGrid.ColumnDefinitions.Add(mapGap);
        editorGrid.ColumnDefinitions.Add(mapColumn);
        editorGrid.Children.Add(editor);
        stageMap.Visibility = Visibility.Collapsed;
        editorGrid.Children.Add(stageMap.At(0, 2));
        stageMap.LineRequested += line => editor.GoToLine(line);
        stageMap.OpenCanvasRequested += () => Host.Navigate("Canvas");

        var right = new DockPanel();
        find.Margin = new Thickness(12, 12, 12, 10);
        right.Children.Add(find.Dock(System.Windows.Controls.Dock.Top));
        editorGrid.Margin = new Thickness(12, 0, 12, 12);
        right.Children.Add(editorGrid);
        var editorCard = Ui.Card(Ui.Rows(0, [0, 1], titleBar, right), 0, "SurfaceLow", 16);
        editorCard.ClipToBounds = true;
        editorCard.MinHeight = 200;
        // The build output and recent builds sit in their own card under the editor.
        runTab = new TabItem { Header = Loc.T("Build run"), Content = timeline };
        historyTab = new TabItem { Header = Loc.T("Recent builds"), Content = history };
        tabs.Items.Add(runTab); tabs.Items.Add(historyTab);
        tabs.SelectionChanged += (_, e) =>
        {
            if (e.OriginalSource != tabs || tabs.SelectedItem != historyTab || historyLoaded) return;
            historyLoaded = true;
            PageTasks.Fire(history.RefreshAsync);
        };
        var outputCard = Ui.Card(tabs, 16, "SurfaceLow", 16);
        outputCard.MinHeight = 200;
        var rightColumn = new Grid();
        rightColumn.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star), MinHeight = 200 });
        rightColumn.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
        rightColumn.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star), MinHeight = 200 });
        rightColumn.Children.Add(editorCard);
        rightColumn.Children.Add(outputCard.At(2));

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380), MinWidth = 320 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 360 });
        body.Children.Add(config);
        body.Children.Add(rightColumn.At(0, 2));
        Layout(header, body);

        editor.Box.TextChanged += (_, _) =>
        {
            if (!loading && !dirty) { dirty = true; UpdateSubtitle(); }
            parseTimer.Stop(); parseTimer.Start();
        };
        editor.CaretMoved += (line, column) => { caretLine = line; caretColumn = column; UpdateEditorState(); };
        parseTimer.Tick += (_, _) => { parseTimer.Stop(); Reparse(); };
        PreviewKeyDown += OnKeys;

        Host.HandOff["Build.CurrentSource"] = (Func<string>)(() => editor.Text);
        Host.HandOff["Build.SetSource"] = (Action<string>)SetSourceFromCanvas;
        Host.HandOff["Build.SetTarget"] = (Action<string>)(name =>
        {
            options.Target.Text = name;
            Host.Notify.Info(name.Length == 0 ? Loc.T("Build target cleared; the final stage is built.") : Loc.T("Build target set to") + " " + name);
        });
        Host.HandOff["Build.CurrentTarget"] = (Func<string>)(() => (options.Target.Text ?? "").Trim());
        Host.HandOff["Build.GoToLine"] = (Action<int>)(line => { Host.Navigate("Build"); editor.GoToLine(line); });
        UpdateSubtitle();
        Reparse();
    }

    // ----------------------------------------------------------------- lifecycle and hand-offs

    public override Task RefreshAsync()
    {
        if (historyLoaded) PageTasks.Fire(history.RefreshAsync);
        if (resumed) return Task.CompletedTask;
        resumed = true;
        // Resume the saved build folder once, unless a hand-off already put a file or draft in the editor.
        if (dockerfilePath.Length > 0 || editor.Text.Length > 0 || string.IsNullOrWhiteSpace(Host.Settings.BuildFolder)) return Task.CompletedTask;
        try
        {
            var folder = Path.GetFullPath(Host.Settings.BuildFolder);
            options.ContextFolder.Text = folder;
            if (!Directory.Exists(folder)) { Host.Notify.Warning("The saved build folder is unavailable. Browse to an existing folder to resume."); return Task.CompletedTask; }
            var path = Path.Combine(folder, "Dockerfile");
            if (File.Exists(path)) LoadFile(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException or System.Security.SecurityException)
        {
            Host.Notify.Warning(Loc.T("The saved Dockerfile could not be resumed:") + " " + e.Message + " Choose another file to continue.");
        }
        return Task.CompletedTask;
    }

    public override void OnShown()
    {
        if (Host.HandOff.Remove("Build.OpenDockerfile", out var pathValue) && pathValue is string path)
            PageTasks.Fire(async () =>
            {
                resumed = true;
                if (!await ConfirmDiscardAsync()) return;
                var full = Path.GetFullPath(path);
                LoadFile(full);
                SetFolder(Path.GetDirectoryName(full) ?? "");
            });
        if (Host.HandOff.Remove("Build.Source", out var sourceValue) && sourceValue is string source)
            PageTasks.Fire(async () =>
            {
                resumed = true;
                if (!await ConfirmDiscardAsync()) return;
                if (Encoding.UTF8.GetByteCount(source) > MaxBytes) throw new InvalidOperationException("The draft is larger than the 1 MiB editor limit.");
                SetText(source);
                dockerfilePath = ""; options.DockerfilePath.Text = ""; savedAt = null;
                dirty = true; draftOrigin = "Creator";
                UpdateSubtitle();
                Host.Notify.Info("The Creator draft is in the editor as an unsaved Dockerfile. Save it into the build context before building.");
            });
    }

    public override IEnumerable<AppCommand> PageCommands() =>
    [
        new() { Id = "build.open", Title = "Open Dockerfile…", Group = "Build", Icon = Icons.FolderOpen, Keywords = "load dockerfile file", Run = () => { Host.Navigate("Build"); PageTasks.Fire(OpenAsync); } },
        new() { Id = "build.save", Title = "Save Dockerfile", Group = "Build", Icon = Icons.Save, Keywords = "write dockerfile", Run = () => { Host.Navigate("Build"); PageTasks.Fire(() => SaveAsync(false)); } },
        new() { Id = "build.check", Title = "Check Dockerfile", Group = "Build", Icon = Icons.PlaylistAddCheck, Keywords = "lint buildx check", Run = () => { Host.Navigate("Build"); PageTasks.Fire(CheckAsync); } },
        new() { Id = "build.build", Title = "Build image", Group = "Build", Icon = Icons.Construction, Keywords = "buildx build docker image", Run = () => { Host.Navigate("Build"); PageTasks.Fire(async () => await BuildAsync()); } },
        new() { Id = "build.vscode", Title = "Open Dockerfile in VS Code", Group = "Build", Icon = Icons.OpenInNew, Keywords = "editor code external", Run = () => PageTasks.Fire(OpenInEditorAsync) },
        new() { Id = "build.timeline", Title = "Show build timeline", Group = "Build", Icon = Icons.ViewTimeline, Keywords = "buildkit steps progress run log", Run = () => { Host.Navigate("Build"); tabs.SelectedItem = runTab; } },
        new() { Id = "build.history", Title = "Recent builds", Group = "Build", Icon = Icons.History, Keywords = "buildx history ls previous builds", Run = () => { Host.Navigate("Build"); tabs.SelectedItem = historyTab; } },
        new() { Id = "build.cancel", Title = "Cancel running build", Group = "Build", Icon = Icons.Stop, Keywords = "stop abort buildkit", Run = () => { if (runner.Running) timeline.Cancel(); else Host.Notify.Info("No build from the Build page is running."); } },
    ];

    private void OnKeys(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var ctrlOnly = Keyboard.Modifiers == ModifierKeys.Control;
        if (ctrl && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && e.Key == System.Windows.Input.Key.S) { PageTasks.Fire(() => SaveAsync(shift)); e.Handled = true; }
        else if (ctrlOnly && e.Key == System.Windows.Input.Key.O) { PageTasks.Fire(OpenAsync); e.Handled = true; }
        else if (ctrlOnly && e.Key == System.Windows.Input.Key.F) { find.FocusSearch(); e.Handled = true; }
        else if (e.Key == System.Windows.Input.Key.F3) { if (shift) find.FindPrevious(); else find.FindNext(); e.Handled = true; }
    }

    // ----------------------------------------------------------------- source state

    private void SetText(string text)
    {
        loading = true;
        try { editor.Text = text; }
        finally { loading = false; }
        Reparse();
    }

    /// <summary>Replaces the editor text as one undoable edit (used by Canvas stage edits).</summary>
    private void SetSourceFromCanvas(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaxBytes) throw new InvalidOperationException("The source would exceed the 1 MiB editor limit.");
        editor.Box.BeginChange();
        try { editor.Box.SelectAll(); editor.Box.SelectedText = text; }
        finally { editor.Box.EndChange(); }
        editor.Box.Select(0, 0);
        dirty = true;
        UpdateSubtitle();
        Reparse();
    }

    private void Reparse()
    {
        var graph = DockerfileGraph.Parse(editor.Text);
        stageCount = graph.Stages.Count;
        options.SetStages(graph.Stages.Select(s => s.Name));
        if (mapVisible) stageMap.Update(graph);
        UpdateEditorState();
    }

    private void UpdateEditorState() => editorState.Text = $"{stageCount} {Loc.T(stageCount == 1 ? "stage" : "stages")} · {Loc.T("Ln")} {caretLine}, {Loc.T("Col")} {caretColumn}";

    private void UpdateSubtitle()
    {
        var folder = options.ContextFolder.Text.Trim();
        var parts = new List<string>();
        parts.Add(folder.Length > 0 ? folder : Loc.T("No context folder"));
        if (dockerfilePath.Length == 0) parts.Add(Loc.T(draftOrigin == "Creator" ? "Unsaved draft from Creator" : editor.Text.Length > 0 ? "Unsaved draft" : "No Dockerfile loaded"));
        else if (dirty) parts.Add(Path.GetFileName(dockerfilePath) + " · " + Loc.T("unsaved changes"));
        else parts.Add(Path.GetFileName(dockerfilePath) + (savedAt is { } at ? " " + Loc.T("saved") + " " + at.ToString("HH:mm") : " " + Loc.T("loaded")));
        subtitle.Text = string.Join(" · ", parts);
    }

    private void ToggleMap()
    {
        mapVisible = !mapVisible;
        mapGap.Width = new GridLength(mapVisible ? 12 : 0);
        mapColumn.Width = mapVisible ? new GridLength(240) : new GridLength(0);
        stageMap.Visibility = mapVisible ? Visibility.Visible : Visibility.Collapsed;
        if (mapVisible) stageMap.Update(DockerfileGraph.Parse(editor.Text));
    }

    private void SetFolder(string folder)
    {
        options.ContextFolder.Text = folder;
        Host.Settings.BuildFolder = folder;
        Host.SaveSettings(null);
        UpdateSubtitle();
    }

    // ----------------------------------------------------------------- files

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!dirty) return true;
        var label = dockerfilePath.Length > 0 ? dockerfilePath : Loc.T("the unsaved draft");
        if (!await Host.Overlay.ConfirmAsync("Discard unsaved Dockerfile edits?", label + Environment.NewLine + Environment.NewLine + Loc.T("The discarded text is kept in local history."), "Discard", true)) return false;
        Host.History.Record("dockerfile", "Discarded unsaved edits to " + (dockerfilePath.Length > 0 ? dockerfilePath : "a Dockerfile draft"), new Dictionary<string, string> { ["files/dockerfile/discarded-" + HistoryStore.SafeName(Path.GetFileName(dockerfilePath.Length > 0 ? dockerfilePath : "draft")) + ".txt"] = editor.Text });
        dirty = false;
        return true;
    }

    /// <summary>Loads a file into the editor without confirmation (callers confirm first).</summary>
    private void LoadFile(string path)
    {
        var full = Path.GetFullPath(path);
        var info = new FileInfo(full);
        if (!info.Exists) throw new FileNotFoundException("The Dockerfile does not exist.", full);
        if (info.Length > MaxBytes) throw new InvalidOperationException("Dockerfile is larger than the 1 MiB editor limit.");
        var source = File.ReadAllText(full);
        SetText(source);
        dirty = false; dockerfilePath = full; savedAt = null; draftOrigin = "";
        options.DockerfilePath.Text = full;
        editor.Box.Select(0, 0);
        UpdateSubtitle();
        Host.Notify.Info("Loaded Dockerfile. Source has not been executed.");
    }

    private async Task OpenAsync()
    {
        var dialog = new OpenFileDialog { Filter = "Dockerfile|Dockerfile;*.dockerfile;Dockerfile.*;Containerfile|All files|*.*", Title = Loc.T("Open Dockerfile") };
        if (Directory.Exists(options.ContextFolder.Text)) dialog.InitialDirectory = options.ContextFolder.Text;
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        if (!await ConfirmDiscardAsync()) return;
        LoadFile(dialog.FileName);
        if (!Directory.Exists(options.ContextFolder.Text)) SetFolder(Path.GetDirectoryName(dialog.FileName) ?? "");
    }

    private async Task FolderChosenAsync(string folder)
    {
        SetFolder(Path.GetFullPath(folder));
        var candidate = Path.Combine(folder, "Dockerfile");
        if (!File.Exists(candidate) || string.Equals(Path.GetFullPath(candidate), dockerfilePath, StringComparison.OrdinalIgnoreCase)) return;
        if (!await ConfirmDiscardAsync()) return;
        LoadFile(candidate);
    }

    /// <summary>Saves to the loaded file, or asks for a destination (Save as). Returns true when the file on disk matches the editor.</summary>
    private Task<bool> SaveAsync(bool saveAs)
    {
        var path = dockerfilePath;
        if (saveAs || path.Length == 0)
        {
            var dialog = new SaveFileDialog { FileName = path.Length > 0 ? Path.GetFileName(path) : "Dockerfile", Filter = "Dockerfile|Dockerfile;*.dockerfile;Dockerfile.*|All files|*.*", OverwritePrompt = true, Title = Loc.T("Save Dockerfile") };
            var folder = path.Length > 0 ? Path.GetDirectoryName(path) : options.ContextFolder.Text;
            if (Directory.Exists(folder)) dialog.InitialDirectory = folder;
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return Task.FromResult(false);
            path = Path.GetFullPath(dialog.FileName);
        }
        var text = editor.Text;
        if (Encoding.UTF8.GetByteCount(text) > MaxBytes) throw new InvalidOperationException("The Dockerfile is larger than the 1 MiB editor limit.");
        var temporary = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllText(temporary, text);
        File.Move(temporary, path, true);
        dockerfilePath = path; options.DockerfilePath.Text = path;
        dirty = false; savedAt = DateTime.Now; draftOrigin = "";
        UpdateSubtitle();
        Host.History.Record("dockerfile", "Saved " + path, new Dictionary<string, string> { ["files/dockerfile/" + HistoryStore.SafeName(path) + ".txt"] = text });
        Host.Notify.Success(Loc.Voice(Loc.T("Dockerfile saved to") + " " + path + ".", "Saved.", "儲存咗。"));
        return Task.FromResult(true);
    }

    private async Task<bool> EnsureSavedAsync(string purpose)
    {
        if (!dirty && File.Exists(dockerfilePath)) return true;
        if (editor.Text.Trim().Length == 0) throw new InvalidOperationException("Open or write a Dockerfile first.");
        if (dockerfilePath.Length > 0 && !File.Exists(dockerfilePath)) throw new FileNotFoundException("The loaded Dockerfile no longer exists. Use Save as to write it again.", dockerfilePath);
        if (!await Host.Overlay.ConfirmAsync("Save the Dockerfile first?", Loc.T(purpose) + " " + Loc.T("uses the file on disk, so the editor text must be saved first.") + (dockerfilePath.Length > 0 ? Environment.NewLine + dockerfilePath : ""), "Save")) return false;
        return await SaveAsync(false);
    }

    private async Task OpenInEditorAsync()
    {
        if (!await EnsureSavedAsync("Opening in an external editor")) return;
        var problem = await ExternalEditor.OpenAsync(dockerfilePath, Host.Settings.ExternalEditor);
        if (problem != null) Host.Notify.Warning(problem);
        else Host.Notify.Info("Opened in the external editor. Reload the file here after saving changes there.");
    }
}
