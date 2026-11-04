using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage.Pages;

/// <summary>Left column: projects reported by the engine plus recently opened files, with search and an open-file action.</summary>
public sealed class ComposeProjectList : Border
{
    private readonly ListBox list = new();
    private readonly SearchFilter search = new("Search projects", 260);
    private readonly ContentControl empty = new();
    private readonly TextBlock status = Ui.Caption("");
    private List<ComposeEntry> entries = new();
    private bool suppress;
    public event Action<ComposeEntry?>? SelectionChanged;
    public event Action<string>? OpenFileRequested;
    public ComposeEntry? Selected => (list.SelectedItem as ListBoxItem)?.Tag as ComposeEntry;
    public IReadOnlyList<ComposeEntry> Entries => entries;

    public ComposeProjectList()
    {
        CornerRadius = new CornerRadius(16);
        Padding = new Thickness(12);
        SetResourceReference(BackgroundProperty, "SurfaceLow");
        search.Width = double.NaN;
        search.Changed += Render;
        System.Windows.Automation.AutomationProperties.SetName(list, Loc.T("Compose projects"));
        list.SelectionChanged += (_, _) => { if (!suppress) SelectionChanged?.Invoke(Selected); };
        list.Background = System.Windows.Media.Brushes.Transparent;
        list.BorderThickness = new Thickness(0);
        list.KeyDown += (_, e) => { if (e.Key == Key.Delete && Selected is { Recent: true, Listed: false } r) { ForgetRecent(r); e.Handled = true; } };
        var open = Ui.IconButton(Icons.FolderOpen, "Open Compose file…", Browse);
        var title = new DockPanel();
        title.Children.Add(open.Dock(Dock.Right));
        title.Children.Add(Ui.Text("Projects", 16, FontWeights.SemiBold));
        openButton = Ui.Button("Open Compose file…", Icons.FolderOpen, ButtonKind.Tonal, Browse);
        openButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        var listHost = new Grid();
        listHost.Children.Add(list);
        listHost.Children.Add(empty);
        Child = Ui.Rows(10, [0, 0, 1, 0, 0], title, search, listHost, status, openButton);
    }

    // Hidden while the empty state already offers the same action, so an empty page shows it once per panel.
    private readonly Button openButton;

    public void Browse()
    {
        var dialog = new OpenFileDialog { Filter = "Compose YAML|compose.yaml;compose.yml;docker-compose.yaml;docker-compose.yml;*.yaml;*.yml|All files|*.*", CheckFileExists = true };
        if (Selected?.LocalFiles.FirstOrDefault() is { } current) dialog.InitialDirectory = Path.GetDirectoryName(current);
        if (dialog.ShowDialog() == true) OpenFileRequested?.Invoke(dialog.FileName);
    }

    private void ForgetRecent(ComposeEntry entry)
    {
        foreach (var f in entry.Files) ComposeRecentFiles.Remove(f);
        entries.Remove(entry);
        Render();
        AppHost.Current.Notify.Info("Removed from recent files. The file itself was not changed.");
    }

    public void SetEntries(List<ComposeEntry> items, string? statusText, string? selectKey)
    {
        entries = items;
        status.Text = statusText ?? "";
        Render(selectKey);
    }

    public void Select(string key) => Render(key);

    private void Render() => Render(Selected?.Key);

    private void Render(string? selectKey)
    {
        suppress = true;
        var previous = Selected?.Key;
        list.Items.Clear();
        ListBoxItem? toSelect = null;
        foreach (var entry in entries.Where(e => search.Matches(e.SearchText)))
        {
            var item = new ListBoxItem { Tag = entry, Content = Row(entry), Padding = new Thickness(10, 8, 10, 8) };
            System.Windows.Automation.AutomationProperties.SetName(item, $"{entry.Name}, {entry.Status}");
            if (entry.Recent && !entry.Listed)
            {
                var menu = new ContextMenu();
                menu.Items.Add(Ui.MenuItem("Remove from recent files", Icons.Close, () => ForgetRecent(entry), "Del"));
                item.ContextMenu = menu;
            }
            list.Items.Add(item);
            if (entry.Key == (selectKey ?? previous)) toSelect = item;
        }
        if (toSelect == null && selectKey == null && previous == null && list.Items.Count > 0) toSelect = (ListBoxItem)list.Items[0];
        if (toSelect != null) list.SelectedItem = toSelect;
        suppress = false;
        empty.Content = entries.Count == 0
            ? Ui.EmptyState(Icons.Stacks, "No Compose projects", "The engine reports no Compose projects and no file was opened yet.", Ui.Button("Open Compose file…", Icons.FolderOpen, ButtonKind.Filled, Browse))
            : list.Items.Count == 0 ? Ui.EmptyState(Icons.FilterList, "No matches", search.Status.Length > 0 ? search.Status : "No projects match the search.") : null;
        empty.Visibility = empty.Content == null ? Visibility.Collapsed : Visibility.Visible;
        openButton.Visibility = entries.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (Selected?.Key != previous) SelectionChanged?.Invoke(Selected);
    }

    private static FrameworkElement Row(ComposeEntry entry)
    {
        var top = new DockPanel();
        top.Children.Add(Ui.Badge(entry.Status, entry.StatusTone).Dock(Dock.Right));
        top.Children.Add(Ui.Text(entry.Name, 14, FontWeights.SemiBold, "OnSurface", false, false));
        var files = entry.Files.Count == 0 ? "No config files reported" : string.Join(", ", entry.Files);
        var path = Ui.Mono(files, 11.5, "OnSurfaceVariant");
        path.ToolTip = files;
        var stack = Ui.VStack(4, top, path);
        if (entry.Files.Count > 0 && !entry.HasLocalFiles) stack.Children.Add(Ui.Caption("Files are not on this computer; actions use the project name."));
        return stack;
    }
}
