using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage.Pages;

/// <summary>Local version history: filterable list of revisions with a diff inspector, restore, labels and export.</summary>
public sealed class ActivityPage : PageBase
{
    public override string Key => "Activity";
    private readonly DataTable<HistoryEntry> table;
    private readonly ActivityInspector inspector;
    private readonly WrapPanel actionChips = new();
    private readonly HashSet<string> activeActions = new(StringComparer.Ordinal);
    private readonly DatePicker from = new() { Width = 150 };
    private readonly DatePicker to = new() { Width = 150 };
    private readonly ContentControl body = new() { Focusable = false };
    private readonly FrameworkElement main;
    private readonly TextBlock subtitle;
    private IReadOnlyList<HistoryEntry> entries = [];
    private bool dirty;

    public ActivityPage()
    {
        // Restores and labels raise History.Changed, which refreshes the list below.
        inspector = new ActivityInspector(() => { });
        table = new DataTable<HistoryEntry>("Activity history", "Search history")
        {
            EmptyTitle = "No history yet",
            EmptyMessage = "Saves, settings changes and operations are recorded here as they happen.",
            NoMatchMessage = "No revisions match the search, action and date filters.",
            Key = e => e.Sha
        };
        table.Column("Time", e => e.WhenText, 150)
            .ChipColumn("Action", e => e.Action, e => ToneFor(e.Action), 110)
            .Column("Summary", e => e.Summary, 380)
            .Column("Files", e => e.PathsText, 220, true)
            .Column("Labels", e => e.LabelsText, 120);
        // Action and date filters have their own controls above the table and are applied before SetItems.
        table.Filter("Labelled only", e => e.Labels.Count > 0);
        table.BulkAction("Export selected…", Icons.FileExport, items => { Export(items, "selected"); return Task.CompletedTask; });
        table.RowAction("Copy summary", Icons.ContentCopy, e => { Clipboard.SetText(e.Summary); return Task.CompletedTask; });
        table.RowAction("Copy revision ID", Icons.Tag, e => { Clipboard.SetText(e.Sha); return Task.CompletedTask; });
        table.FocusedChanged += e => inspector.Show(e);
        foreach (var picker in new[] { from, to })
        {
            picker.SelectedDateChanged += (_, _) => Apply();
            picker.SetResourceReference(Control.BackgroundProperty, "SurfaceLowest");
            picker.SetResourceReference(Control.ForegroundProperty, "OnSurface");
            picker.SetResourceReference(Control.BorderBrushProperty, "OutlineVariant");
            picker.VerticalContentAlignment = VerticalAlignment.Center;
            picker.MinHeight = 36;
        }
        System.Windows.Automation.AutomationProperties.SetName(from, Loc.T("From date"));
        System.Windows.Automation.AutomationProperties.SetName(to, Loc.T("To date"));
        var clearDates = Ui.Button("Any date", Icons.Close, ButtonKind.Text, () => { from.SelectedDate = null; to.SelectedDate = null; });
        var filters = new WrapPanel();
        filters.Children.Add(Ui.HStack(8, Ui.Overline("From"), from, Ui.Overline("To"), to, clearDates).Margin(0, 0, 16, 8));
        filters.Children.Add(actionChips);
        var left = new DockPanel();
        left.Children.Add(filters.Dock(Dock.Top).Margin(0, 0, 0, 8));
        left.Children.Add(table);
        main = Ui.Columns(16, [1.4, 1], Ui.Card(left, 16, "SurfaceLow", 16), inspector.Root);
        var exportVisible = Ui.Button("Export visible…", Icons.FileExport, ButtonKind.Tonal, () => Export(table.Visible, "visible"));
        var openFolder = Ui.Button("Open folder", Icons.FolderOpen, ButtonKind.Text, () => PageParts.OpenFolder(Host.History.Root));
        var header = Ui.Header("Activity", "", out subtitle, openFolder, exportVisible, Ui.AsyncButton("Refresh", Icons.Refresh, ButtonKind.Tonal, RefreshAsync));
        subtitle.Text = "Local version history in " + PageParts.Sanitize(Host.History.Root);
        Layout(header, body);
        body.Content = main;
        Host.History.Changed += () => Dispatcher.BeginInvoke(() => { if (IsVisible) _ = SafeRefresh(); else dirty = true; });
        IsVisibleChanged += (_, _) => { if (IsVisible && dirty) { dirty = false; _ = SafeRefresh(); } };
    }

    public static Tone ToneFor(string action) => action switch
    {
        "settings" => Tone.Primary,
        "restore" => Tone.Tertiary,
        "discard" => Tone.Warning,
        "operation" => Tone.Neutral,
        _ => Tone.Success
    };

    public override async Task RefreshAsync()
    {
        entries = await Task.Run(() => Host.History.List());
        if (Host.History.LastError is { } error)
        {
            body.Content = Ui.EmptyState(Icons.Warning, "Local history is unavailable",
                "The history repository could not be opened, so nothing can be listed, restored or exported. Saving your work still succeeds; it just is not recorded. Reason: " + error,
                Ui.Button("Open folder", Icons.FolderOpen, ButtonKind.Tonal, () => PageParts.OpenFolder(Host.History.Root)),
                Ui.AsyncButton("Try again", Icons.Refresh, ButtonKind.Filled, RefreshAsync));
            return;
        }
        body.Content = main;
        BuildChips();
        Apply();
    }

    private void BuildChips()
    {
        actionChips.Children.Clear();
        var counts = entries.GroupBy(e => e.Action).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
        activeActions.IntersectWith(counts.Select(g => g.Key));
        foreach (var group in counts)
        {
            var action = group.Key;
            var chip = Ui.Chip($"{action} ({group.Count()})", activeActions.Contains(action), on => { if (on) activeActions.Add(action); else activeActions.Remove(action); Apply(); });
            chip.Margin = new Thickness(0, 0, 8, 8);
            actionChips.Children.Add(chip);
        }
    }

    private void Apply()
    {
        var start = from.SelectedDate?.Date;
        var end = to.SelectedDate?.Date.AddDays(1);
        var items = entries.Where(e =>
            (activeActions.Count == 0 || activeActions.Contains(e.Action)) &&
            (start == null || e.When.LocalDateTime >= start) &&
            (end == null || e.When.LocalDateTime < end));
        table.SetItems(items);
        if (table.Focused == null) inspector.Show(null);
    }

    private void Export(IReadOnlyList<HistoryEntry> items, string scope)
    {
        if (items.Count == 0) { Host.Notify.Info($"There are no {scope} revisions to export."); return; }
        var dialog = new SaveFileDialog { FileName = $"history-{DateTime.Now:yyyyMMdd-HHmmss}.zip", Filter = "ZIP archive|*.zip", Title = Loc.T("Export history") };
        if (dialog.ShowDialog() != true) return;
        try
        {
            Host.History.Export(items, dialog.FileName);
            Host.Notify.Success($"Exported {items.Count} {scope} revision(s) to {Path.GetFileName(dialog.FileName)} (index.json plus one patch per revision).");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Host.Notify.Error("The export could not be written: " + e.Message);
        }
    }

    public override IEnumerable<AppCommand> PageCommands() =>
    [
        new AppCommand { Id = "activity.open", Title = "Show local history", Group = "Activity", Icon = Icons.History, Keywords = "history revisions restore undo", Run = () => Host.Navigate(Key) },
        new AppCommand { Id = "activity.folder", Title = "Open history folder", Group = "Activity", Icon = Icons.FolderOpen, Keywords = "history git folder", Run = () => PageParts.OpenFolder(Host.History.Root) },
    ];
}
