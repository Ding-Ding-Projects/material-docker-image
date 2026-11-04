using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage.Pages;

/// <summary>Engine, Data and privacy, and About sections, plus settings export, import and reset.</summary>
public sealed partial class SettingsPage
{
    private void EngineSection()
    {
        var context = Host.Context ?? "the Docker CLI default";
        rows.Add("context", "Selected engine context", $"This window uses {context}. Change it from the context menu in the title bar or in Setup.",
            Ui.Mono(Host.Context ?? "CLI default", 13), s => s.Context, "The global Docker context (docker context use) is never changed; the choice applies to this app only.", false, v => v is string { Length: > 0 } t ? t : "CLI default");
        var intervalText = Ui.Text($"{S.MonitorIntervalSeconds} s", 13, null, "OnSurfaceVariant", false, false);
        var interval = SettingRows.Slider(1, 30, S.MonitorIntervalSeconds, v => { S.MonitorIntervalSeconds = (int)v; intervalText.Text = $"{(int)v} s"; Commit("Monitor refresh interval set to " + (int)v + " s", true); }, 220);
        rows.Add("monitorinterval", "Monitor refresh interval", "How often Monitor samples container statistics, from 1 to 30 seconds.", Ui.HStack(12, interval, intervalText), s => s.MonitorIntervalSeconds,
            "Shorter intervals give smoother charts but run docker stats more often.", false, v => $"{v} s");
        var editor = Ui.Input(S.ExternalEditor, "External editor", true);
        editor.MinWidth = 260;
        var editorError = Ui.Text("", 12, null, "Error");
        void SaveEditor(string value)
        {
            value = value.Trim().Trim('"');
            if (value.Length > 0 && !File.Exists(value)) { editorError.Text = Loc.T("That file does not exist."); return; }
            editorError.Text = "";
            if (value == S.ExternalEditor) return;
            S.ExternalEditor = value;
            Commit(value.Length == 0 ? "External editor reset to the system default" : "External editor set to " + Path.GetFileName(value));
        }
        editor.LostFocus += (_, _) => SaveEditor(editor.Text);
        editor.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) SaveEditor(editor.Text); };
        var browse = Ui.Button("Browse…", Icons.FolderOpen, ButtonKind.Text, () =>
        {
            var dialog = new OpenFileDialog { Filter = "Programs|*.exe;*.cmd;*.bat|All files|*.*", Title = Loc.T("Choose an editor") };
            if (dialog.ShowDialog() != true) return;
            editor.Text = dialog.FileName;
            SaveEditor(dialog.FileName);
        });
        var reset = Ui.Button("Use system default", null, ButtonKind.Text, () => { editor.Text = ""; SaveEditor(""); });
        rows.Add("editor", "External editor", "The program used when you choose Open in editor. Leave empty to use the Windows default for the file type.",
            Ui.VStack(6, Ui.HStack(8, editor, browse, reset), editorError), s => s.ExternalEditor, null, true, v => v is string { Length: > 0 } t ? Path.GetFileName(t) : "system default", @"C:\Program Files\Microsoft VS Code\Code.exe");
        rows.Add("suggestions", "Show suggestions while typing", "Opens Suggestions automatically in the Dockerfile and Compose editors as you type. Ctrl+Space always opens them and Ctrl+Shift+Space opens the value picker at the caret.",
            SettingRows.Toggle(S.SuggestionsWhileTyping, v => { S.SuggestionsWhileTyping = v; Commit("Suggestions while typing " + (v ? "on" : "off")); }), s => s.SuggestionsWhileTyping,
            "Suggestions come from the Dockerfile reference built into the app, the stages and variables in the file, local images on the selected engine context and file names in the build context folder. Secret-looking files are never listed and nothing is sent anywhere.");
        var setup = Ui.Button("Run setup again", Icons.RocketLaunch, ButtonKind.Tonal, () => Host.Navigate("Setup"));
        rows.Add("setup", "Setup wizard", "Detect Docker, choose the engine context and project folder again. Your other settings are kept.", setup, null);
    }

    private void DataSection()
    {
        rows.Add("datafolder", "Data folder", PageParts.Sanitize(UserSettings.DataDirectory),
            Ui.Button("Open folder", Icons.FolderOpen, ButtonKind.Text, () => PageParts.OpenFolder(UserSettings.DataDirectory)), null,
            "settings.json and the history folder live here, in your local application data. Nothing is stored in the installation folder or sent anywhere.");
        rows.Add("historyfolder", "History folder", PageParts.Sanitize(Host.History.Root),
            Ui.Button("Open folder", Icons.FolderOpen, ButtonKind.Text, () => PageParts.OpenFolder(Host.History.Root)), null,
            "A Git repository used only for local history. Activity lists, restores and exports its revisions.");
        rows.Add("school", "School mode", "Uses English only and hides Cantonese wording, funny lead-ins and the dim sum surprise.",
            SettingRows.Toggle(S.SchoolMode, v => { S.SchoolMode = v; Commit("School mode " + (v ? "on" : "off")); }), s => s.SchoolMode,
            "Your language and funny-level choices are kept and come back when School mode is turned off.");
        var buttons = new WrapPanel();
        buttons.Children.Add(Ui.Button("Export settings…", Icons.FileExport, ButtonKind.Tonal, ExportSettings).Margin(0, 0, 8, 8));
        buttons.Children.Add(Ui.AsyncButton("Import settings…", Icons.Upload, ButtonKind.Tonal, ImportSettingsAsync).Margin(0, 0, 8, 8));
        rows.Add("exportimport", "Export or import settings", "Saves all settings to a JSON file, or loads them from one after a preview.", buttons, null,
            "Exported files contain preferences and paths (build folder, editor, personal wording file), never credentials or the personal wording itself. Imports are validated in full before anything changes.", true);
        rows.Add("resetall", "Reset all settings", "Returns every setting to its built-in default. Local history is kept.",
            Ui.AsyncButton("Reset all…", Icons.RestartAlt, ButtonKind.Danger, ResetAllAsync), null);
    }

    private void AboutSection()
    {
        var name = Ui.Input(S.DisplayName, "Display name");
        name.MinWidth = 260;
        var error = Ui.Text("", 12, null, "Error");
        void SaveName(string value)
        {
            var summary = AppIdentity.ApplyDisplayName(S, value, out var problem);
            error.Text = problem.Length == 0 ? "" : Loc.T(problem);
            if (problem.Length > 0) return;
            if (AppIdentity.TryNormalizeName(value, out var shown, out _)) name.Text = shown;
            if (summary != null) Commit(summary);
        }
        var save = Ui.Button("Save", Icons.Save, ButtonKind.Tonal, () => SaveName(name.Text));
        var reset = Ui.Button(Loc.T("Reset to") + " " + UserSettings.ShippedName, null, ButtonKind.Text, () => { name.Text = UserSettings.ShippedName; SaveName(UserSettings.ShippedName); }, false);
        name.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) SaveName(name.Text); };
        rows.Add("displayname", "Display name", "The name shown in the title bar and the window title.", Ui.VStack(6, Ui.HStack(8, name, save, reset), error), s => s.DisplayName, null, true);
        rows.Add("productname", "Product name", UserSettings.ShippedName, new Border(), null,
            "About and the diagnostics report always show the shipped product name, so a renamed display name never hides which product this is. Renaming changes only the title bar and window title; the settings, history and data folders keep their locations.");
        var (version, _, built) = PageParts.AppInfo();
        rows.Add("version", "Version", $"{version} · build {PageParts.BuildText(built)} · .NET {Environment.Version}", new Border(), null);
        var licences = Ui.VStack(6,
            Ui.Text("Third-party components", 14, FontWeights.SemiBold),
            Ui.Caption("Material Symbols font by Google, licensed under the Apache License 2.0."),
            Ui.Caption("LibGit2Sharp, licensed under the MIT License (it bundles libgit2, licensed under GPL v2 with a linking exception)."),
            Ui.Caption("The full licence texts are distributed with the respective projects."));
        var card = Ui.Card(licences, 14, "SurfaceContainer", 12);
        card.Margin = new Thickness(0, 12, 0, 0);
        Extra(card);
    }

    private void ExportSettings()
    {
        var dialog = new SaveFileDialog { FileName = $"material-docker-image-settings-{DateTime.Now:yyyyMMdd}.json", Filter = "JSON|*.json", Title = Loc.T("Export settings") };
        if (dialog.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, S.ToJson(), new UTF8Encoding(false));
            Host.Notify.Success(Loc.T("Settings exported to") + " " + Path.GetFileName(dialog.FileName) + ".");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Host.Notify.Error(Loc.T("The settings could not be exported:") + " " + e.Message); }
    }

    private async Task ImportSettingsAsync()
    {
        var dialog = new OpenFileDialog { Filter = "JSON|*.json", Title = Loc.T("Import settings") };
        if (dialog.ShowDialog() != true) return;
        UserSettings imported;
        try
        {
            var info = new FileInfo(dialog.FileName);
            if (info.Length > 262144) { Host.Notify.Error("The file is larger than 256 KiB, so it is not a settings file."); return; }
            imported = JsonSerializer.Deserialize<UserSettings>(await File.ReadAllTextAsync(dialog.FileName)) ?? throw new JsonException("The file is empty.");
            imported.Normalize();
            // Locked rows keep their current values, so an import cannot change what the person locked.
            ElementLocks.Preserve(S, imported);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Host.Notify.Error(Loc.T("The file is not a valid settings file:") + " " + e.Message);
            return;
        }
        S.ClearScheduledOverride();
        var changes = Differences(S, imported);
        if (changes.Count == 0) { Host.Notify.Info("The file matches your current settings. Nothing changed."); return; }
        var preview = string.Join("\n", changes.Take(30)) + (changes.Count > 30 ? $"\n…and {changes.Count - 30} more" : "");
        if (!await Host.Overlay.ConfirmAsync(Loc.T("Import") + " " + changes.Count + " setting change(s)?", preview + "\n\nThe current settings are kept in local history, so you can restore them from Activity.", "Import")) return;
        RecordCurrent("Settings before import");
        PageParts.CopySettings(imported, S);
        if (S.PersonalVocabularyPath.Length > 0 && File.Exists(S.PersonalVocabularyPath)) Loc.LoadVocabulary(S.PersonalVocabularyPath); else Loc.ClearVocabulary();
        SettingRows.SyncRuntime();
        Host.Schedules.ReloadFile();
        Host.SaveSettings("Imported settings from " + Path.GetFileName(dialog.FileName));
        Rebuild();
        Host.Notify.Success("Settings imported. Some labels update when pages are reopened.");
    }

    /// <summary>Records the current settings so a reset or import can be undone from Activity.</summary>
    private void RecordCurrent(string summary)
    {
        if (!Host.IsCapture) Host.History.Record("settings", summary, new Dictionary<string, string> { ["settings/settings.json"] = S.ToJson() });
    }

    private static List<string> Differences(UserSettings a, UserSettings b)
    {
        var list = new List<string>();
        foreach (var property in typeof(UserSettings).GetProperties())
        {
            if (!property.CanWrite || property.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), true).Length > 0) continue;
            var x = JsonSerializer.Serialize(property.GetValue(a));
            var y = JsonSerializer.Serialize(property.GetValue(b));
            if (x != y) list.Add($"{property.Name}: {PageParts.Shorten(PageParts.Sanitize(x), 60)} → {PageParts.Shorten(PageParts.Sanitize(y), 60)}");
        }
        return list;
    }

    private async Task ResetAllAsync()
    {
        var defaults = new UserSettings();
        // The engine context, open tabs and setup progress are window state, not preferences; keep them.
        defaults.Context = S.Context; defaults.OpenTabs = S.OpenTabs; defaults.PinnedTabs = S.PinnedTabs; defaults.LastDestination = S.LastDestination;
        defaults.SetupComplete = S.SetupComplete; defaults.SetupStep = S.SetupStep; defaults.BuildFolder = S.BuildFolder;
        S.ClearScheduledOverride();
        var lockedKept = ElementLocks.Preserve(S, defaults);
        var changes = Differences(S, defaults);
        if (changes.Count == 0) { Host.Notify.Info(lockedKept.Count > 0 ? "All unlocked settings already have their built-in defaults. Locked settings were left alone." : "All settings already have their built-in defaults."); return; }
        if (!await Host.Overlay.SuperConfirmAsync("Reset all settings", "Every listed setting returns to its built-in default. The current values are recorded in local history first, so they can be restored from Activity. The engine context, tabs, setup progress and build folder are kept.", changes)) return;
        RecordCurrent("Settings before reset");
        PageParts.CopySettings(defaults, S);
        AppIdentity.ClearStoredImages(UserSettings.DataDirectory);
        Loc.ClearVocabulary();
        SettingRows.SyncRuntime();
        Host.Schedules.ReloadFile();
        Host.SaveSettings("Reset all settings to built-in defaults");
        Rebuild();
        Host.Notify.Success("All settings were reset to their built-in defaults.");
    }
}
