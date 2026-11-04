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
        var results = new StackPanel();
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
            var matches = shown.OrderBy(c => c.Group switch { "Commands" => 0, "Destinations" => 1, "Settings" => 2, _ => 3 }).ThenBy(c => c.Title).ToList();
            if (matches.Count == 0) { results.Children.Add(Ui.EmptyState(Icons.Search, "No results", search.Status.Length > 0 ? search.Status : "Try fewer words, or turn on regular expression mode with the .* button.")); return; }
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
                    row.Children.Add(Ui.Text(command.Title, 14, null, "OnSurface", false));
                    if (lockId != null && command.Run == null)
                    {
                        var c2 = command; var id2 = lockId;
                        row.Children.Add(Ui.Button("Unlock…", Icons.LockOpen, ButtonKind.Text, () => AskUnlock(c2, id2)).Dock(Dock.Right));
                    }
                    if (command.Run != null)
                    {
                        var button = new Button { Style = (Style)Application.Current.FindResource("Btn.Base"), Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 0, 12, 0), FontWeight = FontWeights.Normal, MinHeight = 48, Margin = new Thickness(0, 1, 0, 1) };
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
        search.Changed += Fill;
        search.Box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && runnable.Count > 0) { selected = Math.Min(runnable.Count - 1, selected + 1); Highlight(); e.Handled = true; }
            else if (e.Key == Key.Up && runnable.Count > 0) { selected = Math.Max(0, selected - 1); Highlight(); e.Handled = true; }
            else if (e.Key == Key.Enter && selected < runnable.Count) { Run(runnable[selected].Command); e.Handled = true; }
        };
        var size = Ui.Switch("Full window", fullWindow, v => { host.Settings.PaletteFullWindow = v; host.SaveSettings(null); close!(); Open(v); });
        var footer = Ui.Columns(12, [1, 0], Ui.Caption("Enter runs · Up and Down move · Escape closes · Alt+R opens the regex builder"), size);
        Fill();
        close = overlay.Show(Ui.VStack(12, search, note, scroller, Ui.Divider(), footer), fullWindow ? 1000 : 720, null, VerticalAlignment.Top, fullWindow ? .92 : .8);
        search.Box.Focus();
        if (start != null)
        {
            SearchBuilderPopover.ApplyActionToField(search, start);
            search.Dispatcher.BeginInvoke(() => search.OpenBuilder(), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }
}
