using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>The Schedules section: rules that lend a setting for a time window and date range, and the optional local file, https and Home Assistant sources.</summary>
public sealed partial class SettingsPage
{
    private Action? schedulesListener;
    private static readonly string[] DayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
    private static readonly string[] SourceNames = ["Local", "File", "HTTPS", "Home Assistant"];

    private static string SettingLabel(string setting) => setting switch
    {
        ScheduleSettings.Accent => "Accent colour",
        ScheduleSettings.FontSize => "Font size",
        ScheduleSettings.DisplayName => "Display name",
        _ => setting
    };

    private static string SourceLabel(string source) => source switch { ScheduleSources.Https => "HTTPS", ScheduleSources.HomeAssistant => "Home Assistant", _ => source };
    private static string SourceFromLabel(string label) => label switch { "HTTPS" => ScheduleSources.Https, "Home Assistant" => ScheduleSources.HomeAssistant, _ => label };

    private static string DefaultValue(string setting) => setting switch
    {
        ScheduleSettings.Language => "Cantonese",
        ScheduleSettings.Theme => "Light",
        ScheduleSettings.Density => "Compact",
        ScheduleSettings.Accent => "#AA3366",
        ScheduleSettings.FontSize => "16",
        ScheduleSettings.Motion => "Reduced",
        _ => "Night desk"
    };

    private void SchedulesSection()
    {
        var list = new StackPanel();
        var editorHost = new StackPanel { Visibility = Visibility.Collapsed };
        var activeNow = Ui.Caption("");
        var sourceStates = new List<Action>();
        void ShowActive()
        {
            var last = Host.Schedules.Last;
            var parts = ScheduleSettings.All.Where(last.Values.ContainsKey).Select(x => Loc.T(SettingLabel(x)) + " " + (x is ScheduleSettings.Accent or ScheduleSettings.DisplayName or ScheduleSettings.FontSize ? last.Values[x] : Loc.T(last.Values[x]))).ToList();
            activeNow.Text = parts.Count == 0 ? Loc.T("No schedule is applying right now. Your own settings are in effect.") : Loc.T("Applying now: ") + string.Join(" · ", parts) + " " + Loc.T("(your saved choices are unchanged).");
        }

        void Save(string summary)
        {
            S.ScheduleRules = ScheduleEvaluator.Normalize(S.ScheduleRules);
            Commit(summary);
            Host.Schedules.SourcesChanged();
            Render();
            ShowActive();
            foreach (var refresh in sourceStates) refresh();
        }

        void Render()
        {
            list.Children.Clear();
            if (S.ScheduleRules.Count == 0) list.Children.Add(Ui.Caption("No schedules yet. Add a rule to change the language, theme, density, accent colour, font size, motion or display name at set times."));
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

        /// <summary>The control that edits one scheduled value for the rule's setting. get and set read and write the rule's Value or OffValue.</summary>
        FrameworkElement ValueEditor(ScheduleRule rule, Func<string> get, Action<string> set, string name)
        {
            switch (rule.Setting)
            {
                case ScheduleSettings.Accent:
                    {
                        var box = Ui.Input(get(), name, true); box.MinWidth = 120; box.MaxLength = 7;
                        var swatch = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1) };
                        swatch.SetResourceReference(Border.BorderBrushProperty, "Outline");
                        void Paint() { if (ScheduleEvaluator.TryNormalizeValue(ScheduleSettings.Accent, box.Text.Trim(), out var hex)) swatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); else swatch.Background = Brushes.Transparent; }
                        box.TextChanged += (_, _) => { set(box.Text.Trim()); Paint(); };
                        Paint();
                        return Ui.HStack(8, box, swatch, Ui.Caption("#RRGGBB"));
                    }
                case ScheduleSettings.FontSize:
                    {
                        var current = int.TryParse(get(), out var size) ? Math.Clamp(size, ScheduleEvaluator.MinFontSize, ScheduleEvaluator.MaxFontSize) : 16;
                        set(current.ToString());
                        var label = Ui.Text(current + " px", 13, null, "OnSurfaceVariant", false, false);
                        var slider = SettingRows.Slider(ScheduleEvaluator.MinFontSize, ScheduleEvaluator.MaxFontSize, current, v => { set(((int)v).ToString()); label.Text = (int)v + " px"; }, 180);
                        System.Windows.Automation.AutomationProperties.SetName(slider, Loc.T(name));
                        return Ui.HStack(10, slider, label);
                    }
                case ScheduleSettings.DisplayName:
                    {
                        var box = Ui.Input(get(), name); box.MinWidth = 220; box.MaxLength = 80;
                        box.TextChanged += (_, _) => set(box.Text);
                        return box;
                    }
                default:
                    {
                        var items = rule.Setting switch { ScheduleSettings.Language => ScheduleEvaluator.Languages, ScheduleSettings.Theme => ScheduleEvaluator.Themes, ScheduleSettings.Density => ScheduleEvaluator.Densities, _ => ScheduleEvaluator.MotionLevels };
                        var combo = Ui.Combo(items, get());
                        System.Windows.Automation.AutomationProperties.SetName(combo, Loc.T(name));
                        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string v) set(v); };
                        if (combo.SelectedItem is string chosen) set(chosen);
                        return combo;
                    }
            }
        }

        void OpenEditor(ScheduleRule? existing)
        {
            var rule = existing == null
                ? new ScheduleRule { Id = "", Label = "", Setting = ScheduleSettings.Language, Value = "Cantonese", Start = "20:00", End = "07:00" }
                : new ScheduleRule { Id = existing.Id, Label = existing.Label, Enabled = existing.Enabled, Setting = existing.Setting, Value = existing.Value, OffValue = existing.OffValue, Source = existing.Source, EveryDay = existing.EveryDay, Days = existing.Days.ToList(), Start = existing.Start, End = existing.End, FromDate = existing.FromDate, ToDate = existing.ToDate, Priority = existing.Priority };
            editorHost.Children.Clear();
            var label = Ui.Input(rule.Label, "Rule label");
            label.MaxLength = 60; label.MinWidth = 220;
            var valueHost = new ContentControl();
            var offHost = new ContentControl();
            var valueRow = Ui.HStack(10, Ui.Caption("Value"), valueHost);
            var offSwitchHost = new ContentControl();
            var offRow = Ui.HStack(10, offSwitchHost, offHost);
            void RefreshValue()
            {
                var fromSource = rule.Source is ScheduleSources.File or ScheduleSources.Https;
                valueRow.Visibility = fromSource ? Visibility.Collapsed : Visibility.Visible;
                offRow.Visibility = rule.Source == ScheduleSources.HomeAssistant ? Visibility.Visible : Visibility.Collapsed;
                if (!fromSource)
                {
                    if (!ScheduleEvaluator.ValueAllowed(rule.Setting, rule.Value)) rule.Value = DefaultValue(rule.Setting);
                    valueHost.Content = ValueEditor(rule, () => rule.Value, v => rule.Value = v, "Scheduled value");
                }
                offSwitchHost.Content = Ui.Switch(Loc.T("Also apply a value while the entity is off"), rule.OffValue.Length > 0, on =>
                {
                    rule.OffValue = on ? DefaultValue(rule.Setting) : "";
                    offHost.Content = on ? ValueEditor(rule, () => rule.OffValue, v => rule.OffValue = v, "Scheduled value while off") : null;
                });
                if (!ScheduleEvaluator.ValueAllowed(rule.Setting, rule.OffValue)) rule.OffValue = "";
                offHost.Content = rule.OffValue.Length > 0 ? ValueEditor(rule, () => rule.OffValue, v => rule.OffValue = v, "Scheduled value while off") : null;
            }
            var settingCombo = Ui.Combo(ScheduleSettings.All.Select(x => Loc.T(SettingLabel(x))), Loc.T(SettingLabel(rule.Setting)));
            System.Windows.Automation.AutomationProperties.SetName(settingCombo, Loc.T("Setting"));
            settingCombo.SelectionChanged += (_, _) =>
            {
                if (settingCombo.SelectedIndex < 0 || settingCombo.SelectedIndex >= ScheduleSettings.All.Length) return;
                var next = ScheduleSettings.All[settingCombo.SelectedIndex];
                if (next == rule.Setting) return;
                rule.Setting = next; rule.Value = DefaultValue(next); rule.OffValue = "";
                RefreshValue();
            };
            var source = Ui.Segmented(SourceNames, SourceLabel(rule.Source), v => { rule.Source = SourceFromLabel(v); RefreshValue(); }, true);
            var sourceHint = Ui.Caption("Local uses the value below. File and HTTPS read the setting from that source. Home Assistant applies the value while its entity is on.");
            RefreshValue();
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
            var fromDate = Ui.Input(rule.FromDate, "First date", true); fromDate.MinWidth = 120; fromDate.MaxLength = 10;
            var toDate = Ui.Input(rule.ToDate, "Last date", true); toDate.MinWidth = 120; toDate.MaxLength = 10;
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
                rule.FromDate = fromDate.Text.Trim(); rule.ToDate = toDate.Text.Trim();
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
                Ui.HStack(10, Ui.Caption("Setting"), settingCombo),
                Ui.HStack(10, Ui.Caption("Source"), source),
                sourceHint,
                valueRow, offRow,
                every, dayRow,
                Ui.HStack(8, Ui.Caption("From"), startHour, Ui.Text(":", 14, null, "OnSurface", false, false), startMinute, Ui.Caption("until"), endHour, Ui.Text(":", 14, null, "OnSurface", false, false), endMinute),
                Ui.Caption($"{Loc.T("Times use this computer's time zone")}: {zone.DisplayName}. {Loc.T("They follow daylight saving changes. The end time is not included. Equal times mean the whole day. A window that ends earlier than it starts continues past midnight and belongs to the day it starts.")}"),
                Ui.HStack(10, Ui.Caption("First date (optional)"), fromDate, Ui.Caption("Last date (optional)"), toDate),
                Ui.Caption("Dates look like 2026-12-24. Both days are included. Leave both empty for every date. A window that crosses midnight belongs to the date it starts."),
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
        schedulesListener = () => { ShowActive(); foreach (var refresh in sourceStates) refresh(); };
        Host.Schedules.Evaluated += schedulesListener;
        Render(); ShowActive();
        var control = Ui.VStack(10,
            Ui.Caption($"{Loc.T("Times use this computer's time zone")}: {TimeZoneInfo.Local.DisplayName}. {Loc.T("Rules follow daylight saving changes. A later rule wins over an earlier one unless an earlier rule has a higher priority.")}"),
            activeNow, list, editorHost, add);
        rows.Add("schedules", "Scheduled settings", "Change the language, theme, density, accent colour, font size, motion or display name automatically for set times of day, on chosen weekdays and within an optional date range.",
            control, s => s.ScheduleRules.Count,
            "A schedule only lends its value while its window is open. Your own saved settings are never overwritten, and they come back when the window ends. Each setting is decided on its own: the highest priority rule wins, and the later rule wins a tie. Every rule add, change and delete is recorded in local history.",
            true, v => v is int n ? $"{n} rule(s)" : "");

        ScheduleSourceRows(sourceStates, Render, ShowActive);
    }
}
