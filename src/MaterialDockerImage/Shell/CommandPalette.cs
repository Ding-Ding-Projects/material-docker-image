using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Shell;

/// <summary>Ctrl+Shift+F palette: every command, destination and setting, with live controls in rows and its own regex search.</summary>
public sealed class CommandPalette
{
    private readonly AppHost host;
    private readonly OverlayHost overlay;
    public CommandPalette(AppHost host, OverlayHost overlay) { this.host = host; this.overlay = overlay; }

    /// <param name="start">When given (from an action file or the command line), the palette's search field is filled in with it and its builder opens. Nothing is searched or replaced.</param>
    public void Open(bool fullWindow, RegexSearchAction? start = null)
    {
        Action? close = null;
        var search = new SearchFilter("Type a command, destination or setting", fullWindow ? 900 : 640);
        search.Height = 56; search.CornerRadius = new CornerRadius(28); search.Padding = new Thickness(20, 0, 10, 0);
        Action? fillAgain = null;
        var results = new StackPanel();
        var category = "All";
        var count = Ui.Caption("");
        count.HorizontalAlignment = HorizontalAlignment.Right;
        count.SetValue(System.Windows.Automation.AutomationProperties.LiveSettingProperty, System.Windows.Automation.AutomationLiveSetting.Polite);
        var chips = new Dictionary<string, System.Windows.Controls.Primitives.ToggleButton>();
        var settingChips = false;
        var chipRow = new WrapPanel();
        foreach (var name in new[] { "All", "Commands", "Destinations", "Settings", "Other" })
        {
            var chosen = name;
            var chip = Ui.Chip(name, name == "All", on =>
            {
                if (settingChips) return;
                if (!on) { settingChips = true; chips[chosen].IsChecked = category == chosen; settingChips = false; return; }
                category = chosen;
                settingChips = true; foreach (var (k, v) in chips) v.IsChecked = k == chosen; settingChips = false;
                fillAgain?.Invoke();
            });
            chip.Margin = new Thickness(0, 0, 8, 0);
            chips[name] = chip;
            chipRow.Children.Add(chip);
        }
        var scroller = Ui.Scroll(results);
        scroller.MaxHeight = fullWindow ? 760 : 460;
        var runnable = new List<(Button Button, AppCommand Command)>();
        var selected = 0;
        void Highlight()
        {
            for (var i = 0; i < runnable.Count; i++)
                runnable[i].Button.SetResourceReference(Control.BackgroundProperty, i == selected ? "SecondaryContainer" : "SurfaceContainer");
            if (selected < runnable.Count) runnable[selected].Button.BringIntoView();
        }
        // A command guarded by a lock (a locked destination, a locked settings row) is listed and labelled as locked; choosing it asks for the unlock and never runs past the lock.
        string? LockOf(AppCommand command) => PaletteLocks.LockIdFor(command.Id) is { } id && host.IsLocked(id) ? id : null;
        void AskUnlock(AppCommand command, string id)
        {
            _ = Shell.LockUi.UnlockThenAsync(id, Loc.T(command.Title), () => { });
            Fill();
        }
        var note = Ui.Caption("");
        note.SetValue(System.Windows.Automation.AutomationProperties.LiveSettingProperty, System.Windows.Automation.AutomationLiveSetting.Polite);
        void Run(AppCommand command)
        {
            if (LockOf(command) is { } lockId) { AskUnlock(command, lockId); return; }
            if (command.CanRun?.Invoke() == false) { host.Notify.Info($"\"{command.Title}\" is not available right now."); return; }
            close!();
            Ui.Guard(command.Run!);
        }
        void Fill()
        {
            results.Children.Clear(); runnable.Clear(); selected = 0;
            var found = host.Commands.All.Where(c => !SchoolHiddenControls.HidesCommand(c.Id, host.Settings.SchoolMode) && search.Matches(LockedResults.SearchText(Loc.T(c.Title) + " " + c.Title + " " + c.Keywords + " " + c.Group, LockOf(c) != null)));
            var shown = LockedResults.Apply(found, c => LockOf(c) != null, host.Settings.HideLockedInSearch, out var hidden);
            note.Text = LockedResults.HiddenStatement(hidden, Loc.T);
            note.Visibility = hidden > 0 ? Visibility.Visible : Visibility.Collapsed;
            static string Bucket(AppCommand c) => c.Group is "Commands" or "Destinations" or "Settings" ? c.Group : "Other";
            var all = shown.ToList();
            chips["Other"].Visibility = all.Any(c => Bucket(c) == "Other") ? Visibility.Visible : Visibility.Collapsed;
            if (category == "Other" && chips["Other"].Visibility != Visibility.Visible) { category = "All"; settingChips = true; foreach (var (k, v) in chips) v.IsChecked = k == "All"; settingChips = false; }
            if (category != "All") all = all.Where(c => Bucket(c) == category).ToList();
            count.Text = all.Count == 1 ? Loc.T("1 result") : Loc.T("{0} results").Replace("{0}", all.Count.ToString());
            var matches = all.OrderBy(c => c.Group switch { "Commands" => 0, "Destinations" => 1, "Settings" => 2, _ => 3 }).ThenBy(c => c.Title).ToList();
            if (matches.Count == 0) { count.Text = Loc.T("No results"); results.Children.Add(Ui.EmptyState(Icons.Search, "No results", search.Status.Length > 0 ? search.Status : "Try fewer words, or turn on regular expression mode with the .* button.")); return; }
            foreach (var group in matches.GroupBy(c => c.Group))
            {
                results.Children.Add(Ui.Overline(group.Key).Margin(12, 12, 0, 4));
                foreach (var command in group)
                {
                    var row = new DockPanel { LastChildFill = true };
                    row.Children.Add(Ui.Icon(command.Icon ?? Icons.Bolt, 20, false, "Primary").Margin(0, 0, 14, 0).Dock(Dock.Left));
                    if (command.GestureText.Length > 0)
                    {
                        var key = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(6, 1, 6, 1), Child = Ui.Text(command.GestureText, 11, null, "OnSurfaceVariant", false, false), VerticalAlignment = VerticalAlignment.Center };
                        key.SetResourceReference(Border.BorderBrushProperty, "OutlineVariant");
                        row.Children.Add(key.Dock(Dock.Right));
                    }
                    if (command.LiveControl != null)
                    {
                        var live = command.LiveControl();
                        live.MinWidth = 260; live.MaxWidth = 340;
                        if (live is CheckBox cb) cb.Content = null;
                        row.Children.Add(live.Dock(Dock.Right));
                    }
                    var lockId = LockOf(command);
                    if (lockId != null)
                    {
                        var chip = Ui.Badge(Loc.T(LockedResults.Marker), Tone.Warning);
                        row.Children.Add(chip.Dock(Dock.Right));
                    }
                    var titleText = Loc.T(command.Title);
                    var sub = SubLine(command, search, titleText);
                    row.Children.Add(Ui.VStack(1, Highlighted(titleText, search, 14, "OnSurface"), Highlighted(sub, search, 12, "OnSurfaceVariant")));
                    if (lockId != null && command.Run == null)
                    {
                        var c2 = command; var id2 = lockId;
                        row.Children.Add(Ui.Button("Unlock…", Icons.LockOpen, ButtonKind.Text, () => AskUnlock(c2, id2)).Dock(Dock.Right));
                    }
                    if (command.Run != null)
                    {
                        var button = new Button { Style = (Style)Application.Current.FindResource("Btn.Base"), Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 0, 12, 0), FontWeight = FontWeights.Normal, MinHeight = 52, Margin = new Thickness(0, 1, 0, 1) };
                        button.SetResourceReference(Control.BackgroundProperty, "SurfaceContainer");
                        button.SetResourceReference(Control.ForegroundProperty, "OnSurface");
                        System.Windows.Automation.AutomationProperties.SetName(button, Loc.T(command.Title) + (lockId != null ? ", " + Loc.T("locked") : "") + (command.GestureText.Length > 0 ? ", shortcut " + command.GestureText : ""));
                        var c = command;
                        button.Click += (_, _) => Run(c);
                        runnable.Add((button, command));
                        results.Children.Add(button);
                    }
                    else
                    {
                        var card = new Border { Child = row, Padding = new Thickness(12, 6, 12, 6), MinHeight = 52, CornerRadius = new CornerRadius(20) };
                        results.Children.Add(card);
                    }
                }
            }
            Highlight();
        }
        fillAgain = Fill;
        search.Changed += Fill;
        search.Box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && runnable.Count > 0) { selected = Math.Min(runnable.Count - 1, selected + 1); Highlight(); e.Handled = true; }
            else if (e.Key == Key.Up && runnable.Count > 0) { selected = Math.Max(0, selected - 1); Highlight(); e.Handled = true; }
            else if (e.Key == Key.Enter && selected < runnable.Count) { Run(runnable[selected].Command); e.Handled = true; }
        };
        var size = Ui.Switch("Full window", fullWindow, v => { host.Settings.PaletteFullWindow = v; host.SaveSettings(null); close!(); Open(v); });
        var footer = Ui.Columns(12, [1, 0], Hints(), size);
        Fill();
        close = overlay.Show(Ui.VStack(12, search, Ui.Columns(12, [1, 0], chipRow, count), note, scroller, Ui.Divider(), footer), fullWindow ? 1000 : 720, null, VerticalAlignment.Top, fullWindow ? .92 : .8);
        search.Box.Focus();
        if (start != null)
        {
            SearchBuilderPopover.ApplyActionToField(search, start);
            search.Dispatcher.BeginInvoke(() => search.OpenBuilder(), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    /// <summary>Second line under a title: the group, plus the matching keyword when the query hit hidden keywords rather than the title.</summary>
    private static string SubLine(AppCommand command, SearchFilter search, string titleText)
    {
        var line = Loc.T(command.Group);
        if (!search.IsActive || Spans(search, titleText).Count > 0) return line;
        foreach (var word in command.Keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (Spans(search, word).Count > 0) return line + " · " + Loc.T("matches") + " “" + word + "”";
        return line;
    }

    /// <summary>Character ranges of the current query inside the text, in plain or regular expression mode. Empty when the pattern is invalid or slow.</summary>
    private static List<(int Start, int Length)> Spans(SearchFilter search, string text)
    {
        var spans = new List<(int, int)>();
        if (!search.IsActive || text.Length == 0) return spans;
        try
        {
            var options = search.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
            var rx = new Regex(search.RegexMode ? search.Query : Regex.Escape(search.Query), options, TimeSpan.FromMilliseconds(50));
            foreach (Match m in rx.Matches(text)) if (m.Length > 0) spans.Add((m.Index, m.Length));
        }
        catch (Exception e) when (e is ArgumentException or RegexMatchTimeoutException) { spans.Clear(); }
        return spans;
    }

    private static TextBlock Highlighted(string text, SearchFilter search, double size, string brush)
    {
        var block = Ui.Text(text, size, null, brush, false, false);
        var spans = Spans(search, text);
        if (spans.Count == 0) return block;
        block.Text = "";
        var at = 0;
        foreach (var (start, length) in spans)
        {
            if (start < at) continue;
            if (start > at) block.Inlines.Add(new System.Windows.Documents.Run(text[at..start]));
            block.Inlines.Add(new System.Windows.Documents.Run(text.Substring(start, length)) { FontWeight = FontWeights.SemiBold, TextDecorations = TextDecorations.Underline });
            at = start + length;
        }
        if (at < text.Length) block.Inlines.Add(new System.Windows.Documents.Run(text[at..]));
        return block;
    }

    private static FrameworkElement Hints()
    {
        FrameworkElement Key(string text)
        {
            var chip = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 6, 0), Child = Ui.Text(text, 12, null, "OnSurfaceVariant", false, false) };
            chip.SetResourceReference(Border.BorderBrushProperty, "OutlineVariant");
            return chip;
        }
        FrameworkElement Group(string[] keys, string label)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0) };
            foreach (var k in keys) row.Children.Add(Key(k));
            row.Children.Add(Ui.Text(label, 12, null, "OnSurfaceVariant", false, false));
            return row;
        }
        var wrap = new WrapPanel();
        wrap.Children.Add(Group(["Enter"], Loc.T("to run")));
        wrap.Children.Add(Group(["Up", "Down"], Loc.T("to move")));
        wrap.Children.Add(Group(["Esc"], Loc.T("to close")));
        wrap.Children.Add(Group(["Alt+R"], Loc.T("regex builder")));
        return wrap;
    }
}
