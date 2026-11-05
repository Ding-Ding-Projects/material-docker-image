using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Editor for one loaded Compose file. Save always writes the exact loaded path (it is never redirected by a
/// changed path); Save as writes a reviewed destination. Saves are recorded in local history.
/// </summary>
public sealed class ComposeSourceEditor : Grid
{
    private const long MaxBytes = 1024 * 1024;
    private static AppHost Host => AppHost.Current;
    private readonly TextBox editor = Ui.CodeBox("", 200);
    private readonly SearchFilter search = new("Search source", 220);
    private readonly TextBlock fileName = Ui.Text("No file loaded", 15, FontWeights.SemiBold, "OnSurface", false, true);
    private readonly TextBlock pathText = Ui.Mono("", 12, "OnSurfaceVariant");
    private readonly Border stateBadge = Ui.Badge("", Tone.Neutral);
    private readonly TextBlock validation = Ui.Caption("");
    private readonly Button save;
    private readonly Button saveAs;
    private string loadedPath = "";
    private string loadedText = "";
    private DateTime loadedWriteTime;
    private DateTimeOffset? savedAt;
    private bool loading;
    private int lastFind = -1;

    public string LoadedPath => loadedPath;
    public bool IsDirty => loadedPath.Length > 0 && editor.Text != loadedText;
    public event Action? DirtyChanged;
    /// <summary>Called after a successful save with the saved path, so the page can validate and refresh.</summary>
    public Func<string, Task>? Saved { get; set; }

    public ComposeSourceEditor()
    {
        System.Windows.Automation.AutomationProperties.SetName(editor, Loc.T("Compose source"));
        editor.IsEnabled = false;
        // Paths are relative to the loaded Compose file's folder.
        EditorSuggestions.Attach(editor, EditorSuggestionLanguage.Compose, () => loadedPath.Length > 0 ? Path.GetDirectoryName(loadedPath) : null);
        editor.TextChanged += (_, _) => { if (!loading) { UpdateState(); DirtyChanged?.Invoke(); } };
        editor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { _ = Ui.RunGuarded(save!, SaveAsync); e.Handled = true; }
            else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { search.Box.Focus(); search.Box.SelectAll(); e.Handled = true; }
            else if (e.Key == Key.F3) { Find(Keyboard.Modifiers == ModifierKeys.Shift); e.Handled = true; }
        };
        search.Changed += () => lastFind = -1;
        search.Box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Find(Keyboard.Modifiers == ModifierKeys.Shift); e.Handled = true; } };
        save = Ui.AsyncButton("Save", Icons.Save, ButtonKind.Filled, SaveAsync);
        save.ToolTip = Loc.T("Save to the exact loaded path (Ctrl+S)");
        saveAs = Ui.AsyncButton("Save as…", Icons.Download, ButtonKind.Outlined, SaveAsAsync);
        var findNext = Ui.IconButton(Icons.ExpandMore, "Next match (Enter or F3)", () => Find(false));
        var copyFile = Ui.IconButton(Icons.ContentCopy, "Copy file", () => { if (editor.Text.Length > 0) ActionSounds.Copy(editor.Text); });
        var openExternal = Ui.AsyncIconButton(Icons.OpenInNew, "Open in editor", OpenExternalAsync);
        // Header row as in the design: file name, state, then copy and open-in-editor icon buttons.
        var title = new DockPanel { Height = 48, Margin = new Thickness(16, 0, 8, 0) };
        title.Children.Add(openExternal.Dock(Dock.Right));
        title.Children.Add(copyFile.Dock(Dock.Right));
        stateBadge.Margin = new Thickness(8, 0, 6, 0);
        title.Children.Add(stateBadge.Dock(Dock.Right));
        title.Children.Add(Ui.HStack(8, Ui.Icon(Icons.Description, 20, false, "Primary"), fileName));
        fileName.VerticalAlignment = VerticalAlignment.Center;
        var tools = Ui.Wrap(8, search, findNext, save, saveAs);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Children.Add(title);
        Children.Add(Ui.Divider().At(1));
        Children.Add(tools.At(2).Margin(16, 10, 16, 2));
        Children.Add(editor.At(3).Margin(12, 4, 12, 0));
        Children.Add(validation.At(4).Margin(16, 6, 16, 12));
        UpdateState();
    }

    private async Task OpenExternalAsync()
    {
        if (loadedPath.Length == 0) { Host.Notify.Info("No file loaded."); return; }
        var problem = await ExternalEditor.OpenAsync(loadedPath, Host.Settings.ExternalEditor);
        if (problem != null) Host.Notify.Error(problem);
    }

    private void Find(bool backwards) { var message = TextFind.Next(editor, search, ref lastFind, backwards); if (message != null) Host.Notify.Info(message); }

    /// <summary>Loads a file into the editor. Asks before discarding unsaved edits. Returns false when the user kept the draft.</summary>
    public async Task<bool> LoadAsync(string path, bool askBeforeDiscard = true)
    {
        var resolved = Path.GetFullPath(path);
        if (IsDirty && !string.Equals(resolved, loadedPath, StringComparison.OrdinalIgnoreCase) && askBeforeDiscard)
        {
            if (!await Host.Overlay.ConfirmAsync("Discard unsaved Compose edits?", $"{loadedPath} has unsaved changes. Loading {resolved} discards them. The discard is recorded in local history.", "Discard and load", true)) return false;
            if (!Host.IsCapture) Host.History.Record("discard", "Discarded unsaved Compose edits of " + loadedPath, new Dictionary<string, string> { [HistoryKey(loadedPath) + ".discarded"] = editor.Text });
        }
        if (!File.Exists(resolved)) throw new FileNotFoundException("The Compose file does not exist on this computer.", resolved);
        if (new FileInfo(resolved).Length > MaxBytes) throw new ArgumentException("This Compose file is larger than the 1 MiB editor limit.");
        var content = await File.ReadAllTextAsync(resolved);
        loading = true;
        editor.Text = content;
        loading = false;
        loadedText = content;
        loadedPath = resolved;
        loadedWriteTime = File.GetLastWriteTimeUtc(resolved);
        savedAt = null;
        editor.IsEnabled = true;
        validation.Text = "";
        UpdateState();
        DirtyChanged?.Invoke();
        return true;
    }

    public void Clear()
    {
        loading = true; editor.Text = ""; loading = false;
        loadedPath = ""; loadedText = ""; savedAt = null; editor.IsEnabled = false;
        validation.Text = Loc.T("This project has no Compose file on this computer, so there is no source to edit.");
        UpdateState();
        DirtyChanged?.Invoke();
    }

    public void SetValidation(bool? valid, string text)
    {
        validation.Text = text;
        validation.SetResourceReference(TextBlock.ForegroundProperty, valid == false ? "Error" : "OnSurfaceVariant");
        lastValid = valid;
        UpdateState();
    }
    private bool? lastValid;

    private void UpdateState()
    {
        fileName.Text = loadedPath.Length == 0 ? Loc.T("No file loaded") : Path.GetFileName(loadedPath);
        pathText.Text = loadedPath;
        fileName.ToolTip = loadedPath.Length > 0 ? loadedPath : null;
        save.IsEnabled = saveAs.IsEnabled = loadedPath.Length > 0;
        var (text, tone) = loadedPath.Length == 0 ? ("No file", Tone.Neutral)
            : IsDirty ? ("Unsaved changes", Tone.Warning)
            : lastValid == false ? ("Invalid" + (savedAt is { } s1 ? $" · saved {s1.ToLocalTime():HH:mm}" : ""), Tone.Error)
            : lastValid == true ? ("Valid" + (savedAt is { } s2 ? $" · saved {s2.ToLocalTime():HH:mm}" : ""), Tone.Success)
            : (savedAt is { } s3 ? $"Saved {s3.ToLocalTime():HH:mm}" : "Loaded", Tone.Neutral);
        var replacement = Ui.Badge(Loc.T(text), tone);
        var child = replacement.Child; replacement.Child = null; stateBadge.Child = child;
        stateBadge.SetResourceReference(Border.BackgroundProperty, Ui.ToneKeys(tone).Background);
    }

    private static string HistoryKey(string path) => "compose/" + HistoryStore.SafeName(Path.GetFileName(Path.GetDirectoryName(path) ?? "project")) + "/" + HistoryStore.SafeName(Path.GetFileName(path));

    private async Task SaveAsync()
    {
        var path = SourceSafety.RequireLoadedPath(loadedPath, pathText.Text);
        if (!IsDirty) { Host.Notify.Info("No unsaved changes."); return; }
        if (File.Exists(path) && File.GetLastWriteTimeUtc(path) != loadedWriteTime)
        {
            if (!await Host.Overlay.ConfirmAsync("The file changed on disk", $"{path} was modified outside this app after it was loaded. Saving replaces those changes with the editor's text. The current disk version is kept in local history first.", "Overwrite", true)) return;
            if (!Host.IsCapture) Host.History.Record("compose-external", "Disk version before overwrite: " + path, new Dictionary<string, string> { [HistoryKey(path) + ".disk"] = await File.ReadAllTextAsync(path) });
        }
        await WriteAsync(path, Loc.T("Saved Compose file") + " " + path);
    }

    private async Task SaveAsAsync()
    {
        if (loadedPath.Length == 0) throw new InvalidOperationException("Load a Compose file before using Save as.");
        var dialog = new SaveFileDialog { Filter = "Compose YAML|*.yaml;*.yml|All files|*.*", FileName = Path.GetFileName(loadedPath), InitialDirectory = Path.GetDirectoryName(loadedPath), OverwritePrompt = false };
        if (dialog.ShowDialog() != true) return;
        var destination = Path.GetFullPath(dialog.FileName);
        var exists = File.Exists(destination);
        if (!await Host.Overlay.ConfirmAsync("Save Compose source to this destination?", destination + (exists ? "\n\nThis file already exists and will be replaced. The replaced version is kept in local history." : ""), exists ? "Replace" : "Save", exists))
            return;
        if (exists && !Host.IsCapture) Host.History.Record("compose-overwrite", "Version replaced by Save as: " + destination, new Dictionary<string, string> { [HistoryKey(destination) + ".replaced"] = await File.ReadAllTextAsync(destination) });
        await WriteAsync(destination, Loc.T("Saved Compose file as") + " " + destination);
        ComposeRecentFiles.Add(destination);
    }

    private async Task WriteAsync(string path, string summary)
    {
        var text = editor.Text;
        await File.WriteAllTextAsync(path, text, new UTF8Encoding(false));
        loadedPath = path;
        loadedText = text;
        loadedWriteTime = File.GetLastWriteTimeUtc(path);
        savedAt = DateTimeOffset.Now;
        lastValid = null;
        if (!Host.IsCapture) Host.History.Record("compose-save", summary, new Dictionary<string, string> { [HistoryKey(path)] = text });
        Host.Notify.Success(Loc.Voice(summary + ".", "Saved.", "儲存咗。"));
        UpdateState();
        DirtyChanged?.Invoke();
        if (Saved != null) await Saved(path);
    }
}
