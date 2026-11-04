using System.IO;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage.Pages;

/// <summary>The three optional value sources for schedule rules: a watched local file, one https address and one Home Assistant entity.</summary>
public sealed partial class SettingsPage
{
    private static readonly string[] PollChoices = ["1", "5", "10", "15", "30", "60", "120", "360", "720", "1440"];

    private void ScheduleSourceRows(List<Action> sourceStates, Action render, Action showActive)
    {
        FileSourceRow(sourceStates, render, showActive);
        HttpsSourceRow(sourceStates, showActive);
        HomeAssistantSourceRow(sourceStates, showActive);
    }

    private static string PollLabel(int minutes) => minutes < 60 ? $"{minutes} min" : minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes} min";

    private ComboBox PollCombo(int current, int minimum, Action<int> changed, string name)
    {
        var choices = PollChoices.Select(int.Parse).Where(m => m >= minimum).ToList();
        var combo = Ui.Combo(choices.Select(PollLabel), PollLabel(choices.Contains(current) ? current : choices.OrderBy(m => Math.Abs(m - current)).First()));
        System.Windows.Automation.AutomationProperties.SetName(combo, Loc.T(name));
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0 && combo.SelectedIndex < choices.Count) changed(choices[combo.SelectedIndex]); };
        return combo;
    }

    private void FileSourceRow(List<Action> sourceStates, Action render, Action showActive)
    {
        var file = Ui.Input(S.ScheduleSourceFile, "Schedule source file", true);
        file.MinWidth = 260;
        var fileState = Ui.Caption("");
        void ShowFile()
        {
            fileState.SetResourceReference(TextBlock.ForegroundProperty, Host.Schedules.FileProblem.Length > 0 ? "Error" : "OnSurfaceVariant");
            fileState.Text = S.ScheduleSourceFile.Length == 0 ? Loc.T("No file chosen. Rules whose source is File stay paused.")
                : Host.Schedules.FileProblem.Length > 0 ? Loc.T("Not used: ") + Loc.T(Host.Schedules.FileProblem)
                : Loc.T("In use. The file is watched, so edits apply within a moment.");
        }
        void SaveFile(string value)
        {
            value = value.Trim().Trim('"');
            if (value == S.ScheduleSourceFile) return;
            S.ScheduleSourceFile = value;
            Commit(value.Length == 0 ? "Schedule source file cleared" : "Schedule source file set to " + Path.GetFileName(value));
            Host.Schedules.ReloadFile();
            ShowFile(); render(); showActive();
        }
        file.LostFocus += (_, _) => SaveFile(file.Text);
        file.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) SaveFile(file.Text); };
        var browse = Ui.Button("Browse…", Icons.FolderOpen, ButtonKind.Text, () =>
        {
            var dialog = new OpenFileDialog { Filter = "JSON|*.json|All files|*.*", Title = Loc.T("Choose a schedule source file") };
            if (dialog.ShowDialog() != true) return;
            file.Text = dialog.FileName; SaveFile(dialog.FileName);
        });
        var clear = Ui.Button("Clear", Icons.Close, ButtonKind.Text, () => { file.Text = ""; SaveFile(""); });
        ShowFile();
        sourceStates.Add(ShowFile);
        rows.Add("schedulesource", "Schedule source file", "A local JSON file that supplies the value for rules whose source is File.",
            Ui.VStack(6, Ui.Wrap(8, file, browse, clear), fileState), s => s.ScheduleSourceFile,
            "Format: {\"version\": 1, \"language\": \"Cantonese\", \"theme\": \"Light\", \"density\": \"Compact\", \"accent\": \"#AA3366\", \"fontSize\": 16, \"motion\": \"Reduced\", \"displayName\": \"Night desk\"}. Every field except version is optional, no other fields are accepted, and the file must be under 4 KiB. A bad or missing file pauses the rules that use it and leaves your own settings in effect. The file is only read from disk.", true,
            v => v is string { Length: > 0 } t ? Path.GetFileName(t) : "not set", "{\"version\": 1, \"language\": \"Cantonese\"}");
    }

    private void HttpsSourceRow(List<Action> sourceStates, Action showActive)
    {
        var url = Ui.Input(S.ScheduleHttpsUrl, "HTTPS address", true);
        url.MinWidth = 300;
        var error = Ui.Text("", 12, null, "Error");
        var state = Ui.Caption("");
        void ShowState()
        {
            var network = Host.Schedules.Network;
            var inUse = ScheduleNetwork.Uses(S.ScheduleRules, ScheduleSources.Https);
            if (S.ScheduleHttpsUrl.Length == 0) state.Text = Loc.T("No address set. Rules whose source is HTTPS stay paused.");
            else if (!inUse) state.Text = Loc.T("The address is saved but no enabled rule uses the HTTPS source, so nothing is requested.");
            else state.Text = ScheduleSourceRules.Describe(network.Https, DateTime.UtcNow, Loc.T);
            state.SetResourceReference(TextBlock.ForegroundProperty, inUse && network.Https.Offline ? "Error" : "OnSurfaceVariant");
        }
        void SaveUrl()
        {
            var text = url.Text.Trim();
            if (text == S.ScheduleHttpsUrl) { error.Text = ""; return; }
            if (text.Length > 0 && !ScheduleSourceRules.TryNormalizeHttpsUrl(text, out var normalized, out var problem)) { error.Text = Loc.T(problem); return; }
            error.Text = "";
            S.ScheduleHttpsUrl = text.Length == 0 ? "" : (ScheduleSourceRules.TryNormalizeHttpsUrl(text, out var good, out _) ? good : "");
            url.Text = S.ScheduleHttpsUrl;
            Commit(S.ScheduleHttpsUrl.Length == 0 ? "HTTPS schedule source cleared" : "HTTPS schedule source address set");
            Host.Schedules.SourcesChanged();
            ShowState(); showActive();
        }
        url.LostFocus += (_, _) => SaveUrl();
        url.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) SaveUrl(); };
        var poll = PollCombo(S.ScheduleHttpsPollMinutes, ScheduleSourceRules.MinHttpsPollMinutes, minutes => { S.ScheduleHttpsPollMinutes = minutes; Commit("HTTPS schedule source checks every " + PollLabel(minutes)); }, "Check every");
        var check = Ui.Button("Check now", Icons.Refresh, ButtonKind.Tonal, () => { Host.Schedules.PollNetwork(true); ShowState(); });
        var clear = Ui.Button("Clear", Icons.Close, ButtonKind.Text, () => { url.Text = ""; SaveUrl(); });
        ShowState();
        sourceStates.Add(ShowState);
        rows.Add("schedulehttps", "HTTPS schedule source", "An https address that returns a small JSON document. Rules whose source is HTTPS read their value from it.",
            Ui.VStack(8, Ui.HStack(8, url, clear), error, Ui.HStack(10, Ui.Caption("Check every"), poll, check), state), s => s.ScheduleHttpsUrl,
            "Same format as the schedule file (up to 64 KiB). The app sends only a GET request, waits 10 seconds at most, never sends cookies or credentials, never follows a redirect to plain http, and checks no more often than every 5 minutes. Addresses with a user name, password or a secret-looking query parameter are refused so nothing secret is saved in plain text. If the server cannot be reached or answers badly, the last good answer stays in use, nothing changes by itself and the state line says so. Nothing is requested unless an enabled rule uses this source.", true,
            v => v is string { Length: > 0 } ? "address set" : "not set", "https://example.com/schedule.json");
    }

    private void HomeAssistantSourceRow(List<Action> sourceStates, Action showActive)
    {
        var baseUrl = Ui.Input(S.ScheduleHaBaseUrl, "Home Assistant address", true); baseUrl.MinWidth = 260;
        var entity = Ui.Input(S.ScheduleHaEntity, "Home Assistant entity", true); entity.MinWidth = 220;
        var token = new PasswordBox { MinWidth = 260, MaxLength = ScheduleSourceRules.MaxTokenLength };
        System.Windows.Automation.AutomationProperties.SetName(token, Loc.T("Home Assistant long-lived access token"));
        var error = Ui.Text("", 12, null, "Error");
        var tokenState = Ui.Caption("");
        var state = Ui.Caption("");
        void ShowToken() => tokenState.Text = HomeAssistantToken.Exists
            ? Loc.T("A token is stored, encrypted for your Windows account. It cannot be shown again; enter a new one to replace it.")
            : Loc.T("No token is stored. Create a long-lived access token in your Home Assistant profile and enter it here.");
        void ShowState()
        {
            var network = Host.Schedules.Network;
            var inUse = ScheduleNetwork.Uses(S.ScheduleRules, ScheduleSources.HomeAssistant);
            if (S.ScheduleHaBaseUrl.Length == 0 || S.ScheduleHaEntity.Length == 0) state.Text = Loc.T("Set the address and the entity. Rules whose source is Home Assistant stay paused.");
            else if (!inUse) state.Text = Loc.T("Saved, but no enabled rule uses Home Assistant, so nothing is requested.");
            else
            {
                var line = ScheduleSourceRules.Describe(network.HomeAssistant, DateTime.UtcNow, Loc.T);
                var entityState = network.HomeAssistant.State switch { HomeAssistantState.On => Loc.T("The entity is on."), HomeAssistantState.Off => Loc.T("The entity is off."), HomeAssistantState.Indeterminate => Loc.T("The entity is neither on nor off, so nothing is applied."), _ => "" };
                state.Text = entityState.Length > 0 ? line + " " + entityState : line;
            }
            state.SetResourceReference(TextBlock.ForegroundProperty, inUse && network.HomeAssistant.Offline ? "Error" : "OnSurfaceVariant");
            ShowToken();
        }
        void SaveAddress()
        {
            var addressText = baseUrl.Text.Trim(); var entityText = entity.Text.Trim();
            string normalized = "";
            if (addressText.Length > 0 && !ScheduleSourceRules.TryNormalizeHomeAssistantUrl(addressText, out normalized, out var problem)) { error.Text = Loc.T(problem); return; }
            if (entityText.Length > 0 && !ScheduleSourceRules.IsValidEntityId(entityText)) { error.Text = Loc.T("The entity looks like input_boolean.night_mode: lower case letters, digits and underscores, a dot between."); return; }
            error.Text = "";
            if (normalized == S.ScheduleHaBaseUrl && entityText == S.ScheduleHaEntity) return;
            S.ScheduleHaBaseUrl = normalized; S.ScheduleHaEntity = entityText;
            baseUrl.Text = normalized;
            Commit("Home Assistant schedule source set");
            Host.Schedules.SourcesChanged();
            ShowState(); showActive();
        }
        baseUrl.LostFocus += (_, _) => SaveAddress(); entity.LostFocus += (_, _) => SaveAddress();
        baseUrl.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) SaveAddress(); };
        entity.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) SaveAddress(); };
        var saveToken = Ui.Button("Store token", Icons.Save, ButtonKind.Tonal, () =>
        {
            if (!HomeAssistantToken.Save(token.Password, out var problem)) { error.Text = Loc.T(problem); return; }
            token.Clear(); error.Text = "";
            Host.History.Record("settings", "Home Assistant token stored");
            Host.Schedules.SourcesChanged();
            ShowState(); showActive();
        });
        var clearToken = Ui.Button("Remove token", Icons.Delete, ButtonKind.Text, () =>
        {
            HomeAssistantToken.Clear(); token.Clear(); error.Text = "";
            Host.History.Record("settings", "Home Assistant token removed");
            Host.Schedules.SourcesChanged();
            ShowState(); showActive();
        });
        var poll = PollCombo(S.ScheduleHaPollMinutes, ScheduleSourceRules.MinHomeAssistantPollMinutes, minutes => { S.ScheduleHaPollMinutes = minutes; Commit("Home Assistant schedule source checks every " + PollLabel(minutes)); }, "Check every");
        var check = Ui.Button("Check now", Icons.Refresh, ButtonKind.Tonal, () => { Host.Schedules.PollNetwork(true); ShowState(); });
        ShowState();
        sourceStates.Add(ShowState);
        rows.Add("schedulehomeassistant", "Home Assistant schedule source", "Rules whose source is Home Assistant apply their value while a chosen entity (for example an input_boolean) is on, and optionally another value while it is off.",
            Ui.VStack(8,
                // Wrapping groups: side by side these fields are wider than a narrow window allows.
                Ui.Wrap(12, Ui.HStack(8, Ui.Caption("Address"), baseUrl), Ui.HStack(8, Ui.Caption("Entity"), entity)),
                Ui.Wrap(8, Ui.HStack(8, Ui.Caption("Token"), token), saveToken, clearToken),
                tokenState, error, Ui.HStack(10, Ui.Caption("Check every"), poll, check), state), s => s.ScheduleHaEntity,
            "The address is the base address of Home Assistant, such as http://homeassistant.local:8123. Plain http is accepted only on your own network; anything else must use https. The token is encrypted with Windows data protection for your account in its own file, is never saved in settings.json, never exported, logged or shown again. The app sends only a GET request for the one entity, waits 10 seconds at most and reads 64 KiB at most. Only the states on and off decide anything. If Home Assistant cannot be reached, the last good state stays in use and nothing changes by itself.", true,
            v => v is string { Length: > 0 } t ? t : "not set", "input_boolean.night_mode");
    }
}
