using System.Text;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage.Pages;

/// <summary>Inspector for one history revision: details, searchable diff, restore, save-as and label.</summary>
internal sealed class ActivityInspector
{
    private static AppHost Host => AppHost.Current;
    private readonly StackPanel details = new();
    private readonly StackPanel actions = new();
    private readonly ReadOnlySearchText diff = new("Search this diff", 220);
    private readonly Action changed;
    public FrameworkElement Root { get; }

    public ActivityInspector(Action changed)
    {
        this.changed = changed;
        var stack = new DockPanel();
        stack.Children.Add(Ui.VStack(12, details, actions).Margin(0, 0, 0, 12).Dock(Dock.Top));
        stack.Children.Add(diff);
        Root = Ui.Card(stack, 16, "SurfaceLow", 16);
        Show(null);
    }

    /// <summary>Restorable files are user content snapshots: settings, saved files and drafts.</summary>
    public static bool Restorable(string path) => path.StartsWith("settings/", StringComparison.Ordinal) || path.StartsWith("files/", StringComparison.Ordinal) || path.StartsWith("drafts/", StringComparison.Ordinal);

    private static bool IsProjectFile(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        return name.Contains("dockerfile") || name.Contains("compose") || name.EndsWith(".yml") || name.EndsWith(".yaml") || path.StartsWith("drafts/", StringComparison.Ordinal);
    }

    public void Show(HistoryEntry? value)
    {
        details.Children.Clear(); actions.Children.Clear();
        if (value == null)
        {
            details.Children.Add(Ui.EmptyState(Icons.History, "Select a revision", "Choose a row to see what changed, restore a file or add a label."));
            diff.Visibility = Visibility.Collapsed;
            return;
        }
        diff.Visibility = Visibility.Visible;
        details.Children.Add(Ui.HStack(8, Ui.Badge(value.Action, ActivityPage.ToneFor(value.Action)), Ui.Mono(value.ShortSha, 12, "OnSurfaceVariant")));
        details.Children.Add(Ui.Text(value.Summary, 15, FontWeights.SemiBold, "OnSurface", true, false));
        details.Children.Add(Ui.Caption(value.WhenText + (value.Labels.Count > 0 ? " · labels: " + value.LabelsText : "")));
        details.Children.Add(Ui.Caption(value.Paths.Count == 0 ? "No files changed besides the activity log." : "Files: " + value.PathsText));
        diff.Text = Host.History.Diff(value.Sha);

        var bar = new WrapPanel();
        foreach (var path in value.Paths.Where(Restorable))
        {
            var p = path;
            bar.Children.Add(Ui.AsyncButton("Restore " + Path.GetFileName(p), Icons.Undo, ButtonKind.Tonal, () => RestoreAsync(value, p), false).Margin(0, 0, 8, 8));
            if (IsProjectFile(p)) bar.Children.Add(Ui.Button("Save restored content as…", Icons.Save, ButtonKind.Text, () => SaveAs(value, p)).Margin(0, 0, 8, 8));
        }
        bar.Children.Add(Ui.AsyncButton("Label…", Icons.Label, ButtonKind.Text, () => LabelAsync(value)).Margin(0, 0, 8, 8));
        bar.Children.Add(Ui.Button("Copy revision ID", Icons.ContentCopy, ButtonKind.Text, () => Clipboard.SetText(value.Sha)).Margin(0, 0, 8, 8));
        actions.Children.Add(bar);
    }

    private async Task RestoreAsync(HistoryEntry value, string path)
    {
        var settings = path == "settings/settings.json";
        var message = settings
            ? $"Restores the settings saved in revision {value.ShortSha} ({value.WhenText}). The restore is recorded as a new revision, and nothing is deleted from history.\n\nRestored settings are saved to settings.json right away. Theme and appearance update immediately; labels on open pages, the engine context and tabs take full effect after you restart the app."
            : $"Writes the content of {path} from revision {value.ShortSha} into local history as a new revision. Your project files on disk are not changed; use Save restored content as… to write a copy where you want it.";
        if (!await Host.Overlay.ConfirmAsync("Restore " + Path.GetFileName(path) + "?", message, "Restore")) return;
        var content = Host.History.Restore(value.Sha, path);
        if (content == null) { Host.Notify.Error("The file could not be read from that revision" + (Host.History.LastError != null ? ": " + Host.History.LastError : ".")); return; }
        if (settings)
        {
            try
            {
                // Validate first so a damaged revision never replaces working settings. The values are copied into the
                // live settings object (the app saves that object on exit, so writing the file alone would be lost).
                var parsed = System.Text.Json.JsonSerializer.Deserialize<UserSettings>(content) ?? throw new System.Text.Json.JsonException("Empty settings.");
                parsed.Normalize();
                PageParts.CopySettings(parsed, Host.Settings);
                SettingRows.SyncRuntime();
                Host.SaveSettings(null);
                Host.Notify.Success("Settings restored and saved. Theme and appearance update now; labels on open pages, the engine context and tabs take full effect after you restart the app.");
            }
            catch (Exception e) when (e is System.Text.Json.JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                Host.Notify.Error("The revision's settings were recorded in history but not applied: " + e.Message);
            }
        }
        else Host.Notify.Success($"Restored {path} as a new history revision.");
        changed();
    }

    private void SaveAs(HistoryEntry value, string path)
    {
        var content = Host.History.FileAt(value.Sha, path);
        if (content == null) { Host.Notify.Error("The file could not be read from that revision."); return; }
        var name = Path.GetFileName(path);
        var dialog = new SaveFileDialog { FileName = name, Title = Loc.T("Save restored content as"), Filter = "All files|*.*", OverwritePrompt = true };
        if (dialog.ShowDialog() != true) return;
        File.WriteAllText(dialog.FileName, content, new UTF8Encoding(false));
        Host.History.Record("restore", $"Saved {path} from {value.ShortSha} as {Path.GetFileName(dialog.FileName)}", new Dictionary<string, string> { ["files/" + HistoryStore.SafeName(Path.GetFileName(dialog.FileName))] = content });
        Host.Notify.Success("Saved " + Path.GetFileName(dialog.FileName) + ".");
        changed();
    }

    private async Task LabelAsync(HistoryEntry value)
    {
        var name = await Host.Overlay.PromptAsync("Label this revision", "Label", "", "Labels make a revision easy to find later. Letters, digits, dots, dashes and underscores are kept; other characters become underscores.",
            v => v.Length > 60 ? "Use at most 60 characters." : null);
        if (name == null) return;
        if (Host.History.Label(value.Sha, name)) { Host.Notify.Success($"Labelled {value.ShortSha} as {HistoryStore.SafeName(name)}."); changed(); }
        else Host.Notify.Error("The label could not be added" + (Host.History.LastError != null ? ": " + Host.History.LastError : ". A label with that name may already exist."));
    }
}
