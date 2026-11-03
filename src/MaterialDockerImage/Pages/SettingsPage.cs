using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Settings with a searchable section list. Every row explains what it does and where its value comes from,
/// and changes apply immediately through <see cref="AppHost.SaveSettings"/>.
/// </summary>
public sealed partial class SettingsPage : PageBase
{
    public override string Key => "Settings";
    private static readonly (string Name, string Icon)[] Sections =
    [
        ("Appearance", Icons.Palette), ("Language", Icons.Translate), ("Accessibility", Icons.AccessibilityNew), ("Focus modes", Icons.CenterFocusStrong),
        ("Sounds and narration", Icons.VolumeUp), ("Engine", Icons.Dns), ("Data and privacy", Icons.Shield), ("About", Icons.Info)
    ];
    private readonly SearchFilter search = new("Search settings", 240);
    private readonly ListBox nav = new();
    private readonly StackPanel content = new();
    private readonly ScrollViewer scroller;
    private readonly TextBlock searchState = Ui.Caption("");
    private readonly Dictionary<string, Border> sectionCards = new();
    private SettingRows rows = new();
    private bool stale;
    private bool committing;
    private bool fillingNav;

    public SettingsPage()
    {
        System.Windows.Automation.AutomationProperties.SetName(nav, Loc.T("Settings sections"));
        nav.SelectionChanged += (_, _) => { if (!fillingNav && nav.SelectedItem is ListBoxItem { Tag: string name } && sectionCards.TryGetValue(name, out var card)) card.BringIntoView(new Rect(0, 0, 1, 1)); };
        search.Changed += ApplySearch;
        scroller = Ui.Scroll(content.Margin(0, 0, 12, 24));
        var left = new DockPanel();
        left.Children.Add(Ui.VStack(8, search, searchState).Margin(0, 0, 0, 8).Dock(Dock.Top));
        left.Children.Add(nav);
        var body = Ui.Columns(16, [-272, 1], Ui.Card(left, 12, "SurfaceLow", 16), scroller);
        Layout(Ui.Header("Settings", "Changes apply immediately and are saved to settings.json with a local history revision", out _), body);
        Host.SettingsChanged += () => { if (!committing) { if (IsVisible) Dispatcher.BeginInvoke(Rebuild); else stale = true; } };
        Host.ContextChanged += () => { if (IsVisible) Rebuild(); else stale = true; };
        IsVisibleChanged += (_, _) => { if (IsVisible && stale) Rebuild(); };
        Build();
    }

    private void Commit(string what, bool coalesce = false) => rows.Commit(what, coalesce);

    private void Rebuild()
    {
        stale = false;
        var offset = scroller.VerticalOffset;
        Build();
        scroller.ScrollToVerticalOffset(offset);
    }

    private void Build()
    {
        // Saves made from this page do not rebuild it (that would reset the control being used).
        rows = new SettingRows { Guard = save => { committing = true; try { save(); } finally { committing = false; } } };
        content.Children.Clear(); sectionCards.Clear();
        var builders = new Dictionary<string, Action>
        {
            ["Appearance"] = AppearanceSection, ["Language"] = LanguageSection, ["Accessibility"] = AccessibilitySection,
            ["Focus modes"] = FocusSection, ["Sounds and narration"] = SoundsSection, ["Engine"] = EngineSection,
            ["Data and privacy"] = DataSection, ["About"] = AboutSection
        };
        foreach (var (name, icon) in Sections)
        {
            rows.Section = name;
            var before = rows.All.Count;
            var extras = new List<UIElement>();
            currentExtras = extras;
            builders[name]();
            var stack = new StackPanel();
            stack.Children.Add(Ui.HStack(10, Ui.Icon(icon, 22, true, "Primary"), Ui.Text(name, 18, FontWeights.SemiBold)).Margin(0, 0, 0, 4));
            foreach (var row in rows.All.Skip(before)) stack.Children.Add(row.Element);
            foreach (var extra in extras) stack.Children.Add(extra);
            var card = Ui.Card(stack, 20, "SurfaceLow", 16);
            card.Margin = new Thickness(0, 0, 0, 16);
            card.Tag = name;
            sectionCards[name] = card;
            content.Children.Add(card);
        }
        ApplySearch();
    }

    private List<UIElement> currentExtras = new();
    /// <summary>Adds a non-setting element (for example a note or a list) to the section being built.</summary>
    private void Extra(FrameworkElement element) { element.Tag = "extra"; currentExtras.Add(element); }

    private void ApplySearch()
    {
        var selected = (nav.SelectedItem as ListBoxItem)?.Tag as string;
        fillingNav = true;
        nav.Items.Clear();
        var total = 0;
        foreach (var (name, icon) in Sections)
        {
            var sectionHit = search.Matches(name + " " + Loc.T(name));
            var sectionRows = rows.All.Where(r => r.Section == name).ToList();
            var hits = 0;
            foreach (var row in sectionRows)
            {
                var visible = !search.IsActive || sectionHit || search.Matches(row.SearchText);
                row.Element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                if (visible) hits++;
            }
            var show = !search.IsActive || sectionHit || hits > 0;
            sectionCards[name].Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (sectionCards[name].Child is StackPanel panel)
                foreach (var extra in panel.Children.OfType<FrameworkElement>().Where(e => e.Tag as string == "extra"))
                    extra.Visibility = !search.IsActive || sectionHit ? Visibility.Visible : Visibility.Collapsed;
            if (!show) continue;
            total += search.IsActive ? hits : sectionRows.Count;
            var item = new ListBoxItem { Tag = name, Padding = new Thickness(12, 10, 12, 10), Content = Ui.HStack(12, Ui.Icon(icon, 20), Ui.Text(name, 14, null, "OnSurface", false)) };
            System.Windows.Automation.AutomationProperties.SetName(item, Loc.T(name));
            nav.Items.Add(item);
            if (name == selected) nav.SelectedItem = item;
        }
        fillingNav = false;
        searchState.Text = !search.IsActive ? "" : search.Status.Length > 0 ? search.Status : total == 0 ? Loc.T("No settings match.") : $"{total} matching setting(s)";
    }

    public override FrameworkElement? FindTarget(string id)
    {
        var key = id.StartsWith("set.", StringComparison.Ordinal) ? id[4..] : id;
        var row = rows.All.FirstOrDefault(r => r.Id.Equals(key, StringComparison.OrdinalIgnoreCase))
                  ?? rows.All.FirstOrDefault(r => r.Label.Equals(key, StringComparison.OrdinalIgnoreCase))
                  ?? rows.All.FirstOrDefault(r => r.SearchText.Contains(key, StringComparison.OrdinalIgnoreCase));
        if (row == null) return null;
        if (row.Element.Visibility != Visibility.Visible) search.Query = "";
        Dispatcher.BeginInvoke(() =>
        {
            row.Element.BringIntoView();
            FirstFocusable(row.Element)?.Focus();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
        return row.Element;
    }

    private static Control? FirstFocusable(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is Control { Focusable: true, IsTabStop: true, IsEnabled: true, IsVisible: true } control) return control;
            if (FirstFocusable(child) is { } found) return found;
        }
        return null;
    }

    public override IEnumerable<AppCommand> PageCommands() =>
    [
        new AppCommand { Id = "settings.export", Title = "Export settings…", Group = "Settings", Icon = Icons.FileExport, Keywords = "backup settings json", Run = () => { Host.Navigate(Key); ExportSettings(); } },
        new AppCommand { Id = "settings.import", Title = "Import settings…", Group = "Settings", Icon = Icons.Upload, Keywords = "restore settings json", Run = () => { Host.Navigate(Key); _ = ImportSettingsAsync(); } },
        new AppCommand { Id = "settings.school", Title = "School mode", Group = "Settings", Icon = Icons.School, Keywords = "english only plain", LiveControl = () => Ui.Switch("School mode", Host.Settings.SchoolMode, v => { Host.Settings.SchoolMode = v; SettingRows.SyncRuntime(); Host.SaveSettings("School mode " + (v ? "on" : "off")); }) },
        new AppCommand { Id = "settings.open", Title = "Open settings", Group = "Settings", Icon = Icons.Settings, Keywords = "preferences options", Run = () => Host.Navigate(Key) },
    ];
}
