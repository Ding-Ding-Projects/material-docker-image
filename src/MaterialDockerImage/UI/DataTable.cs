using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using MaterialDockerImage.Services;
using Microsoft.Win32;

namespace MaterialDockerImage.UI;

/// <summary>One displayed row. Cells hold display text; Tones hold optional chip tones for chip columns.</summary>
public sealed class TableRow<T> : INotifyPropertyChanged
{
    public TableRow(T item, string[] cells, string[] tones) { Item = item; Cells = cells; Tones = tones; }
    public T Item { get; }
    public string[] Cells { get; }
    public string[] Tones { get; }
    public string SearchText => string.Join(" ", Cells);
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class ToneBrushConverter : IValueConverter
{
    public bool Foreground { get; init; }
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var tone = Enum.TryParse<Tone>(value as string, out var t) ? t : Tone.Neutral;
        var keys = Ui.ToneKeys(tone);
        return Application.Current.TryFindResource(Foreground ? keys.Foreground : keys.Background) ?? Brushes.Transparent;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed record TableColumn<T>(string Header, Func<T, string> Text, double Width, bool Mono = false, Func<T, Tone>? Chip = null);

/// <summary>
/// Shared collection surface: search with regex builder, filter chips, multi-select with honest select-all scope,
/// a bulk action bar, target-specific context menus showing working shortcuts, empty states and exports.
/// </summary>
public sealed class DataTable<T> : Grid
{
    private readonly List<TableColumn<T>> columns = new();
    private readonly List<(string Label, Func<T, bool> Predicate, System.Windows.Controls.Primitives.ToggleButton Chip)> filters = new();
    private readonly List<(string Label, string? Icon, Func<IReadOnlyList<T>, Task> Action, bool Danger)> bulkActions = new();
    private readonly List<(string Label, string? Icon, string? Gesture, Func<T, Task> Action, bool Danger)> rowActions = new();
    private readonly ObservableCollection<TableRow<T>> rows = new();
    private List<T> all = new();
    private bool staggered;
    private readonly GridView view = new();
    public ListView List { get; } = new();
    public SearchFilter Search { get; }
    public WrapPanel FilterBar { get; } = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Border bulkBar;
    private readonly TextBlock bulkText = Ui.Text("", 14, FontWeights.SemiBold, "OnPrimaryContainer");
    private readonly WrapPanel bulkButtons = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentControl empty = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock statusText = Ui.Caption("");
    public string EmptyTitle { get; set; } = "Nothing here yet";
    public string EmptyMessage { get; set; } = "Refresh to load real engine records. No sample records are shown.";
    public string NoMatchMessage { get; set; } = "No rows match the current search and filters.";
    public string Name2 { get; }
    public event Action<T?>? FocusedChanged;
    public event Action<T>? Activated;
    public IReadOnlyList<T> Items => all;
    public IReadOnlyList<T> Visible => rows.Select(r => r.Item).ToList();
    public IReadOnlyList<T> Selected => List.SelectedItems.Cast<TableRow<T>>().Select(r => r.Item).ToList();
    public T? Focused => List.SelectedItem is TableRow<T> r ? r.Item : default;
    public Func<T, string>? Key { get; set; }
    private readonly DockPanel toolbar;

    /// <summary>Places a page-level action at the right of the search and filter row, beside Export.</summary>
    public DataTable<T> ToolbarAction(FrameworkElement action)
    {
        action.VerticalAlignment = VerticalAlignment.Center;
        action.Margin = new Thickness(8, 0, 4, 0);
        DockPanel.SetDock(action, System.Windows.Controls.Dock.Right);
        toolbar.Children.Insert(1, action);
        return this;
    }

    public DataTable(string name, string searchPlaceholder = "Search")
    {
        Name2 = name;
        Search = new SearchFilter(searchPlaceholder, 300);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = true };
        var export = Ui.IconButton(Icons.FileExport, "Export visible rows", () => ExportMenu());
        toolbar.Children.Add(export.Dock(System.Windows.Controls.Dock.Right));
        var searchAndFilters = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        Search.Margin = new Thickness(0, 0, 12, 4);
        searchAndFilters.Children.Add(Search);
        FilterBar.Margin = new Thickness(0, 4, 0, 4);
        searchAndFilters.Children.Add(FilterBar);
        toolbar.Children.Add(searchAndFilters);
        Children.Add(toolbar);
        var bulkContent = new DockPanel();
        var check = Ui.Icon(Icons.CheckBox, 20, true, "OnPrimaryContainer").Margin(0, 0, 10, 0);
        bulkContent.Children.Add(check.Dock(System.Windows.Controls.Dock.Left));
        bulkContent.Children.Add(bulkButtons.Dock(System.Windows.Controls.Dock.Right));
        bulkContent.Children.Add(bulkText);
        bulkBar = new Border { Child = bulkContent, CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 6, 6, 6), Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed };
        bulkBar.SetResourceReference(Border.BackgroundProperty, "PrimaryContainer");
        Children.Add(bulkBar.At(1));
        List.View = view;
        List.ItemsSource = rows;
        AutomationProperties.SetName(List, Loc.T(name));
        List.SelectionChanged += (_, _) => { UpdateBulk(); FocusedChanged?.Invoke(Focused); };
        List.MouseDoubleClick += (_, e) => { if (Focused is { } f && e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(List, d) != null) Activated?.Invoke(f); };
        List.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter && Focused is { } f) { Activated?.Invoke(f); e.Handled = true; }
            if (e.Key == System.Windows.Input.Key.Delete && Focused is not null) { var remove = rowActions.FirstOrDefault(a => a.Gesture == "Del"); if (remove.Action != null) _ = Run(() => remove.Action(Focused!)); e.Handled = true; }
        };
        List.ContextMenuOpening += (_, e) => { if (!BuildContextMenu()) e.Handled = true; };
        List.ContextMenu = new ContextMenu();
        Children.Add(List.At(2));
        Children.Add(empty.At(2));
        Children.Add(statusText.At(3).Margin(4, 6, 0, 0));
        Search.Changed += Apply;
        AddCheckColumn();
    }

    private void AddCheckColumn()
    {
        var factory = new FrameworkElementFactory(typeof(CheckBox));
        factory.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding("IsSelected") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListViewItem), 1), Mode = BindingMode.TwoWay });
        factory.SetValue(FocusableProperty, false);
        factory.SetValue(AutomationProperties.NameProperty, "Select row");
        view.Columns.Add(new GridViewColumn { Width = 48, CellTemplate = new DataTemplate { VisualTree = factory }, Header = "" });
    }

    public DataTable<T> Column(string header, Func<T, string> text, double width = 140, bool mono = false)
    {
        var index = columns.Count;
        columns.Add(new(header, text, width, mono));
        var factory = new FrameworkElementFactory(typeof(TextBlock));
        factory.SetBinding(TextBlock.TextProperty, new Binding($"Cells[{index}]"));
        factory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        factory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        factory.SetBinding(ToolTipProperty, new Binding($"Cells[{index}]"));
        if (mono) { factory.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont"); factory.SetValue(TextBlock.FontSizeProperty, 12.5); }
        view.Columns.Add(new GridViewColumn { Header = Loc.T(header), Width = width, CellTemplate = new DataTemplate { VisualTree = factory } });
        return this;
    }

    public DataTable<T> ChipColumn(string header, Func<T, string> text, Func<T, Tone> tone, double width = 120)
    {
        var index = columns.Count;
        columns.Add(new(header, text, width, false, tone));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        border.SetValue(Border.PaddingProperty, new Thickness(10, 3, 10, 3));
        border.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Left);
        border.SetBinding(Border.BackgroundProperty, new Binding($"Tones[{index}]") { Converter = new ToneBrushConverter() });
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new Binding($"Cells[{index}]"));
        label.SetValue(TextBlock.FontSizeProperty, 12.0);
        label.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        label.SetBinding(TextBlock.ForegroundProperty, new Binding($"Tones[{index}]") { Converter = new ToneBrushConverter { Foreground = true } });
        border.AppendChild(label);
        view.Columns.Add(new GridViewColumn { Header = Loc.T(header), Width = width, CellTemplate = new DataTemplate { VisualTree = border } });
        return this;
    }

    public DataTable<T> Filter(string label, Func<T, bool> predicate, bool initiallyOn = false)
    {
        var chip = Ui.Chip(label, initiallyOn, _ => Apply());
        chip.Margin = new Thickness(0, 0, 8, 0);
        filters.Add((label, predicate, chip));
        FilterBar.Children.Add(chip);
        return this;
    }

    public DataTable<T> BulkAction(string label, string? icon, Func<IReadOnlyList<T>, Task> action, bool danger = false)
    {
        bulkActions.Add((label, icon, action, danger));
        var b = Ui.Button(label, icon, ButtonKind.Text);
        b.SetResourceReference(Control.ForegroundProperty, danger ? "Error" : "OnPrimaryContainer");
        b.Click += async (_, _) => await Ui.RunGuarded(b, () => action(Selected));
        bulkButtons.Children.Add(b);
        return this;
    }

    public DataTable<T> RowAction(string label, string? icon, Func<T, Task> action, string? gesture = null, bool danger = false)
    {
        rowActions.Add((label, icon, gesture, action, danger));
        return this;
    }

    private static async Task Run(Func<Task> action) { try { await action(); } catch (Exception e) when (e is not OutOfMemoryException) { AppHost.Current?.Notify.Error(e.Message); } }

    private bool BuildContextMenu()
    {
        if (Focused is not { } item || rowActions.Count == 0) return false;
        var menu = List.ContextMenu!;
        menu.Items.Clear();
        var filter = new SearchFilter("Filter actions", 240) { Margin = new Thickness(8, 0, 8, 6) };
        var host = new MenuItem { Header = filter, StaysOpenOnClick = true, Focusable = false };
        host.Template = new ControlTemplate(typeof(MenuItem)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
        host.Template.VisualTree.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        menu.Items.Add(host);
        var items = new List<(MenuItem Item, string Label)>();
        foreach (var action in rowActions)
        {
            var a = action;
            var m = Ui.MenuItem(a.Label, a.Icon, () => _ = Run(() => a.Action(item)), a.Gesture, a.Danger);
            items.Add((m, Loc.T(a.Label)));
            menu.Items.Add(m);
        }
        var none = new MenuItem { Header = Loc.T("No actions match this filter."), IsEnabled = false, Visibility = Visibility.Collapsed };
        menu.Items.Add(none);
        filter.Changed += () => { var shown = 0; foreach (var (m, label) in items) { var ok = filter.Matches(label); m.Visibility = ok ? Visibility.Visible : Visibility.Collapsed; if (ok) shown++; } none.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed; };
        menu.Opened += Focus;
        void Focus(object? s, RoutedEventArgs e) { filter.Box.Focus(); menu.Opened -= Focus; }
        return true;
    }

    public void SetItems(IEnumerable<T> items)
    {
        var keep = Key == null ? new HashSet<string>() : Selected.Select(Key).ToHashSet();
        all = items.ToList();
        Apply();
        // First real rows enter with a short stagger (skipped when motion is off or reduced).
        if (!staggered && rows.Count > 0) { staggered = true; Motion.StaggerItems(List); }
        if (Key != null && keep.Count > 0) foreach (var row in rows.Where(r => keep.Contains(Key(r.Item)))) List.SelectedItems.Add(row);
        // Documentation captures show the inspector with the first real record instead of an empty selection.
        if (AppHost.Current?.IsCapture == true && List.SelectedIndex < 0 && rows.Count > 0) List.SelectedIndex = rows.Count - 1;
    }

    public void Apply()
    {
        var active = filters.Where(f => f.Chip.IsChecked == true).ToList();
        rows.Clear();
        foreach (var item in all)
        {
            if (active.Any(f => !f.Predicate(item))) continue;
            var row = new TableRow<T>(item, columns.Select(c => c.Text(item) ?? "").ToArray(), columns.Select(c => c.Chip?.Invoke(item).ToString() ?? "").ToArray());
            if (!Search.Matches(row.SearchText)) continue;
            rows.Add(row);
        }
        var filtered = all.Count > 0 && rows.Count == 0;
        empty.Content = all.Count == 0 ? Ui.EmptyState(Icons.Inventory2, EmptyTitle, EmptyMessage) : filtered ? Ui.EmptyState(Icons.FilterList, "No matches", Search.Status.Length > 0 ? Search.Status : NoMatchMessage) : null;
        empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        statusText.Text = all.Count == 0 ? "" : Loc.T($"{rows.Count} of {all.Count} shown") + (active.Count > 0 ? " · " + Loc.T("filters:") + " " + string.Join(", ", active.Select(f => Loc.T(f.Label))) : "") + (Search.IsActive ? " · " + Loc.T("search active") : "");
        UpdateBulk();
    }

    private void UpdateBulk()
    {
        var count = List.SelectedItems.Count;
        bulkBar.Visibility = count > 0 && bulkActions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        bulkText.Text = Loc.T($"{count} selected of {rows.Count} matching");
    }

    public void SelectAllMatching() => List.SelectAll();
    public void InvertSelection()
    {
        var selected = List.SelectedItems.Cast<object>().ToHashSet();
        List.SelectedItems.Clear();
        foreach (var row in rows) if (!selected.Contains(row)) List.SelectedItems.Add(row);
    }

    private void ExportMenu()
    {
        var menu = new ContextMenu();
        foreach (var format in new[] { "CSV", "TSV", "JSON", "Markdown" }) menu.Items.Add(Ui.MenuItem("Export visible rows as " + format, Icons.FileExport, () => Export(format)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Ui.MenuItem("Select all matching", Icons.SelectAll, SelectAllMatching, "Ctrl+A"));
        menu.Items.Add(Ui.MenuItem("Invert selection", Icons.Deselect, InvertSelection));
        menu.Items.Add(Ui.MenuItem("Clear selection", Icons.Close, () => List.SelectedItems.Clear()));
        menu.IsOpen = true;
    }

    public string Render(string format)
    {
        var visible = rows.ToList();
        var headers = columns.Select(c => c.Header).ToArray();
        string Escape(string v, char sep) => v.IndexOfAny([sep, '"', '\n', '\r']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        var sb = new StringBuilder();
        switch (format)
        {
            case "CSV": case "TSV":
                var sep = format == "CSV" ? ',' : '\t';
                sb.AppendLine(string.Join(sep, headers.Select(h => Escape(h, sep))));
                foreach (var r in visible) sb.AppendLine(string.Join(sep, r.Cells.Select(c => Escape(c, sep))));
                break;
            case "JSON":
                var list = visible.Select(r => headers.Zip(r.Cells).ToDictionary(p => p.First, p => p.Second)).ToList();
                sb.Append(JsonSerializer.Serialize(new { schema = "material-docker-image/table-export", version = 1, table = Name2, exported = DateTimeOffset.Now, rows = list }, new JsonSerializerOptions { WriteIndented = true }));
                break;
            default:
                sb.AppendLine("| " + string.Join(" | ", headers) + " |");
                sb.AppendLine("|" + string.Concat(headers.Select(_ => " --- |")));
                foreach (var r in visible) sb.AppendLine("| " + string.Join(" | ", r.Cells.Select(c => c.Replace("|", "\\|"))) + " |");
                break;
        }
        return sb.ToString();
    }

    private void Export(string format)
    {
        var extension = format switch { "CSV" => "csv", "TSV" => "tsv", "JSON" => "json", _ => "md" };
        var dialog = new SaveFileDialog { FileName = HistoryStore.SafeName(Name2) + "." + extension, Filter = $"{format}|*.{extension}" };
        if (dialog.ShowDialog() != true) return;
        File.WriteAllText(dialog.FileName, Render(format), new UTF8Encoding(false));
        AppHost.Current?.Notify.Success($"Exported {rows.Count} rows as {format} (UTF-8, {(format == "JSON" ? "schema version 1" : "header row included")}).");
    }
}
