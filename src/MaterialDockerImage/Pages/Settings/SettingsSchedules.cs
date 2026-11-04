using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage.Pages;

/// <summary>The Schedules section: rules that lend a language or theme for a time window, and the optional local source file.</summary>
public sealed partial class SettingsPage
{
    private Action? schedulesListener;
    private static readonly string[] DayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    private void SchedulesSection()
    {
        var list = new StackPanel();
        var editorHost = new StackPanel { Visibility = Visibility.Collapsed };
        var activeNow = Ui.Caption("");
        void ShowActive()
        {
            var last = Host.Schedules.Last;
            var parts = new List<string>();
            if (last.Language != null) parts.Add(Loc.T("Language") + " " + Loc.T(last.Language));
            if (last.Theme != null) parts.Add(Loc.T("Theme") + " " + Loc.T(last.Theme));
            activeNow.Text = parts.Count == 0 ? Loc.T("No schedule is applying right now. Your own language and theme are in effect.") : Loc.T("Applying now: ") + string.Join(" · ", parts) + " " + Loc.T("(your saved choices are unchanged).");
        }

        void Save(string summary)
        {
            S.ScheduleRules = ScheduleEvaluator.Normalize(S.ScheduleRules);
            Commit(summary);
            Host.Schedules.Evaluate();
            Render();
            ShowActive();
        }

        void Render()
        {
            list.Children.Clear();
            if (S.ScheduleRules.Count == 0) list.Children.Add(Ui.Caption("No schedules yet. Add a rule to switch the language or theme at set times."));
            foreach (var rule in S.ScheduleRules.ToList())
            {
                var problem = ScheduleEvaluator.Validate(rule);
                var enabled = Ui.Switch(Loc.T("Rule enabled") + ": " + (rule.Label.Length > 0 ? rule.Label : rule.Id), rule.Enabled, v => { rule.Enabled = v; Save("Schedule rule " + (v ? "enabled" : "disabled") + ": " + RuleName(rule)); });
                var edit = Ui.IconButton(Icons.Edit, Loc.T("Edit rule") + ": " + RuleName(rule), () => OpenEditor(rule));
                var delete = Ui.IconButton(Icons.Delete, Loc.T("Delete rule") + ": " + RuleName(rule), () => { S.ScheduleRules.Remove(rule); editorHost.Visibility = Visibility.Collapsed; Save("Schedule rule deleted: " + RuleName(rule)); });
                var text = Ui.VStack(2, Ui.Text(RuleName(rule), 14, FontWeights.SemiBold), Ui.Caption(ScheduleEvaluator.Describe(rule) + (rule.Priority != 0 ? $" · {Loc.T("priority")} {rule.Priority}" : "")),
                    problem != null ? Ui.Text(Loc.T(problem), 12, null, "Error") : null);
                var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
                row.Children.Add(Ui.HStack(4, enabled, edit, delete).Dock(Dock.Right));
                row.Children.Add(text);
                var card = Ui.Card(row, 10, "SurfaceContainer", 12);
                card.Margin = new Thickness(0, 0, 0, 6);
                list.Children.Add(card);
            }
        }

        string RuleName(ScheduleRule rule) => rule.Label.Length > 0 ? rule.Label : rule.Id;

        void OpenEditor(ScheduleRule? existing)
        {
            var rule = existing == null
                ? new ScheduleRule { Id = "", Label = "", Setting = "Language", Value = "Cantonese", Start = "20:00", End = "07:00" }
                : new ScheduleRule { Id = existing.Id, Label = existing.Label, Enabled = existing.Enabled, Setting = existing.Setting, Value = existing.Value, Source = existing.Source, EveryDay = existing.EveryDay, Days = existing.Days.ToList(), Start = existing.Start, End = existing.End, Priority = existing.Priority };
            editorHost.Children.Clear();
            var label = Ui.Input(rule.Label, "Rule label");
            label.MaxLength = 60; label.MinWidth = 220;
            var value = Ui.Combo(ScheduleEvaluator.Languages, rule.Value);
            void RefreshValue()
            {
                value.ItemsSource = rule.Setting == "Language" ? ScheduleEvaluator.Languages : ScheduleEvaluator.Themes;
                value.SelectedItem = rule.Value;
            }
            var setting = Ui.Segmented(["Language", "Theme"], rule.Setting, v => { rule.Setting = v; rule.Value = v == "Language" ? "Cantonese" : "Light"; RefreshValue(); });
            var source = Ui.Segmented([ScheduleSources.Local, ScheduleSources.File], rule.Source, v => { rule.Source = v; value.IsEnabled = v == ScheduleSources.Local; });
            value.SelectionChanged += (_, _) => { if (value.SelectedItem is string v) rule.Value = v; };
            RefreshValue(); value.IsEnabled = rule.Source == ScheduleSources.Local;
            var days = DayNames.Select((name, index) => Ui.Chip(name, rule.Days.Contains(index), on => { if (on) { if (!rule.Days.Contains(index)) rule.Days.Add(index); } else rule.Days.Remove(index); })).ToList();
            var dayRow = new WrapPanel();
            foreach (var chip in days) { chip.Margin = new Thickness(0, 0, 6, 6); chip.IsEnabled = !rule.EveryDay; dayRow.Children.Add(chip); }
            var every = Ui.Switch(Loc.T("Every day"), rule.EveryDay, v => { rule.EveryDay = v; foreach (var chip in days) chip.IsEnabled = !v; });
            ScheduleEvaluator.TryParseTime(rule.Start, out var startMinutes); ScheduleEvaluator.TryParseTime(rule.End, out var endMinutes);
            var hours = Enumerable.Range(0, 24).Select(h => h.ToString("00")).ToList();
            var minutes = Enumerable.Range(0, 60).Select(m => m.ToString("00")).ToList();
            var startHour = Ui.Combo(hours, (startMinutes / 60).ToString("00")); var startMinute = Ui.Combo(minutes, (startMinutes % 60).ToString("00"));
            var endHour = Ui.Combo(hours, (endMinutes / 60).ToString("00")); var endMinute = Ui.Combo(minutes, (endMinutes % 60).ToString("00"));
            foreach (var c in new[] { startHour, startMinute, endHour, endMinute }) c.MinWidth = 72;
            System.Windows.Automation.AutomationProperties.SetName(startHour, Loc.T("Start hour")); System.Windows.Automation.AutomationProperties.SetName(startMinute, Loc.T("Start minute"));
            System.Windows.Automation.AutomationProperties.SetName(endHour, Loc.T("End hour")); System.Windows.Automation.AutomationProperties.SetName(endMinute, Loc.T("End minute"));
            System.Windows.Automation.AutomationProperties.SetName(value, Loc.T("Scheduled value"));
            var priorityText = Ui.Text(rule.Priority.ToString("+0;-0;0"), 13, null, "OnSurfaceVariant", false, false);
            var priority = SettingRows.Slider(-5, 5, Math.Clamp(rule.Priority, -5, 5), v => { rule.Priority = (int)v; priorityText.Text = ((int)v).ToString("+0;-0;0"); }, 160);
            System.Windows.Automation.AutomationProperties.SetName(priority, Loc.T("Rule priority"));
            var error = Ui.Text("", 12, null, "Error");
            var zone = TimeZoneInfo.Local;
            var save = Ui.Button("Save rule", Icons.Save, ButtonKind.Filled, () =>
            {
                rule.Label = label.Text.Trim();
                rule.Start = startHour.SelectedItem + ":" + startMinute.SelectedItem;
                rule.End = endHour.SelectedItem + ":" + endMinute.SelectedItem;
                var problem = ScheduleEvaluator.Validate(rule);
                if (problem != null) { error.Text = Loc.T(problem); return; }
                if (existing == null)
                {
                    if (S.ScheduleRules.Count >= ScheduleEvaluator.MaxRules) { error.Text = Loc.T("Up to 50 rules are allowed."); return; }
                    S.ScheduleRules.Add(rule);
                    editorHost.Visibility = Visibility.Collapsed;
                    Save("Schedule rule added: " + ScheduleEvaluator.Describe(rule));
                }
                else
                {
                    var at = S.ScheduleRules.IndexOf(existing);
                    if (at >= 0) S.ScheduleRules[at] = rule;
                    editorHost.Visibility = Visibility.Collapsed;
                    Save("Schedule rule changed: " + ScheduleEvaluator.Describe(rule));
                }
            });
            var cancel = Ui.Button("Cancel", null, ButtonKind.Text, () => editorHost.Visibility = Visibility.Collapsed);
            var body = Ui.VStack(10,
                Ui.Text(existing == null ? "New rule" : "Edit rule", 14, FontWeights.SemiBold),
                Ui.HStack(10, Ui.Caption("Label"), label),
                Ui.HStack(10, Ui.Caption("Setting"), setting),
                Ui.HStack(10, Ui.Caption("Source"), source),
                Ui.HStack(10, Ui.Caption("Value"), value),
                every, dayRow,
                Ui.HStack(8, Ui.Caption("From"), startHour, Ui.Text(":", 14, null, "OnSurface", false, false), startMinute, Ui.Caption("until"), endHour, Ui.Text(":", 14, null, "OnSurface", false, false), endMinute),
                Ui.Caption($"{Loc.T("Times use this computer's time zone")}: {zone.DisplayName}. {Loc.T("They follow daylight saving changes. The end time is not included. Equal times mean the whole day. A window that ends earlier than it starts continues past midnight and belongs to the day it starts.")}"),
                Ui.HStack(10, Ui.Caption("Priority"), priority, priorityText),
                error,
                Ui.HStack(8, save, cancel));
            editorHost.Children.Add(Ui.Card(body, 14, "SurfaceHigh", 12));
            editorHost.Visibility = Visibility.Visible;
            label.Focus();
        }

        var add = Ui.Button("Add rule", Icons.Add, ButtonKind.Tonal, () => OpenEditor(null));
        add.HorizontalAlignment = HorizontalAlignment.Left;
        if (schedulesListener != null) Host.Schedules.Evaluated -= schedulesListener;
        schedulesListener = ShowActive;
        Host.Schedules.Evaluated += schedulesListener;
        Render(); ShowActive();
        var control = Ui.VStack(10,
            Ui.Caption($"{Loc.T("Times use this computer's time zone")}: {TimeZoneInfo.Local.DisplayName}. {Loc.T("Rules follow daylight saving changes. A later rule wins over an earlier one unless an earlier rule has a higher priority.")}"),
            activeNow, list, editorHost, add);
        rows.Add("schedules", "Scheduled language and theme", "Switch the language or the theme automatically for set times of day, every day or on chosen weekdays.",
            control, s => s.ScheduleRules.Count,
            "A schedule only lends its value while its window is open. Your own saved language and theme are never overwritten, and they come back when the window ends. Every rule add, change and delete is recorded in local history.",
            true, v => v is int n ? $"{n} rule(s)" : "");

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
            ShowFile(); Render(); ShowActive();
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
        rows.Add("schedulesource", "Schedule source file", "A local JSON file that supplies the value for rules whose source is File.",
            Ui.VStack(6, Ui.HStack(8, file, browse, clear), fileState), s => s.ScheduleSourceFile,
            "Format: {\"version\": 1, \"language\": \"Cantonese\", \"theme\": \"Light\"}. Both fields are optional, no other fields are accepted, and the file must be under 4 KiB. A bad or missing file pauses the rules that use it and leaves your own settings in effect. Nothing is fetched from a network.", true,
            v => v is string { Length: > 0 } t ? Path.GetFileName(t) : "not set", "{\"version\": 1, \"language\": \"Cantonese\"}");
    }
}
