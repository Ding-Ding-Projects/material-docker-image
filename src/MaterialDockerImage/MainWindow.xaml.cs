using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using MaterialDockerImage.Pages;
using MaterialDockerImage.Services;
using MaterialDockerImage.Shell;
using MaterialDockerImage.UI;

namespace MaterialDockerImage;

/// <summary>Pages that hold unsaved drafts implement this so closing and context changes can protect them.</summary>
public interface IDirtyAware { bool IsDirty { get; } string DirtyLabel { get; } }

public sealed record TabState(string Destination, bool Pinned, bool Workspace);

public partial class MainWindow : Window
{
    private readonly UserSettings settings = UserSettings.Load();
    private readonly AppHost host;
    private readonly OverlayHost overlay = new();
    private readonly OperationsDock dock = new();
    private readonly Dictionary<string, PageBase> pages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RadioButton> railItems = new(StringComparer.Ordinal);
    private readonly List<TabState> tabs = new();
    private int currentTab;
    private readonly ComboBox contexts = new() { MinWidth = 170, MinHeight = 32, Height = 32 };
    private readonly TextBlock contextDot = new() { Width = 8, Height = 8 };
    private readonly Border contextState = new() { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock badge = Ui.Text("", 9, FontWeights.SemiBold, "OnError", false, false);
    private readonly TextBlock clockText = Ui.Caption("");
    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool suppressContext;
    private CommandPalette? palette;
    public bool CaptureMode { get; }

    public MainWindow(bool capture = false)
    {
        CaptureMode = capture;
        InitializeComponent();
        if (capture) SourceInitialized += (_, _) => System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle)?.AddHook(AllowLargerThanScreen);
        Loc.Language = settings.Language; Loc.FunnyEnglish = settings.FunnyEnglish; Loc.FunnyCantonese = settings.FunnyCantonese; Loc.DialogEmoji = settings.DialogEmoji; Loc.SchoolMode = settings.SchoolMode;
        if (!string.IsNullOrWhiteSpace(settings.PersonalVocabularyPath) && File.Exists(settings.PersonalVocabularyPath)) Loc.LoadVocabulary(settings.PersonalVocabularyPath);
        ApplyAppearance();
        OverlaySlot.Content = overlay;
        DockHost.Content = dock;
        var notifier = new Notifier(Toasts) { SoundsEnabled = () => settings.Sounds && !settings.LowStimulation && !CaptureMode };
        host = new AppHost(settings, notifier, overlay, dock, capture);
        notifier.Narrate = Narrator.Speak;
        Narrator.Enabled = () => settings.Narration && !CaptureMode;
        host.Navigate = Navigate;
        host.NavigateWith = (destination, _) => Navigate(destination);
        host.SettingsChanged += ApplyAppearance;
        notifier.UnreadChanged += () => { badge.Text = notifier.Unread > 99 ? "99+" : notifier.Unread.ToString(); ((FrameworkElement)badge.Parent).Visibility = notifier.Unread > 0 ? Visibility.Visible : Visibility.Collapsed; };
        dock.SetExpanded(settings.OutputDockOpen);
        BuildTitleBar();
        BuildRail();
        RegisterCommands();
        host.Commands.Bind(this);
        RestoreTabs();
        MinimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        MaximizeButton.Click += (_, _) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        CloseButton.Click += (_, _) => Close();
        StateChanged += (_, _) => { Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0); MaximizeButton.Content = WindowState == WindowState.Maximized ? Icons.FilterNone : Icons.CropSquare; AutomationProperties.SetName(MaximizeButton, WindowState == WindowState.Maximized ? "Restore" : "Maximize"); };
        SizeChanged += (_, _) => Adapt();
        Closing += OnClosing;
        Loaded += async (_, _) =>
        {
            Adapt();
            await RefreshContexts();
            if (!CaptureMode) MaybeDimSum();
        };
        clock.Tick += (_, _) => clockText.Text = DateTime.Now.ToString("HH:mm:ss");
        clock.Start();
    }

    private void Adapt()
    {
        var narrow = ActualWidth > 0 && ActualWidth < 1180;
        PaletteSlot.Width = narrow ? 220 : 460;
        Provenance.Visibility = ActualWidth > 0 && ActualWidth < 1040 ? Visibility.Collapsed : Visibility.Visible;
        var shortWindow = ActualHeight > 0 && ActualHeight < 900;
        foreach (var item in railItems.Values) if (item.Content is TextBlock label) label.Visibility = settings.FocusMode || shortWindow ? Visibility.Collapsed : Visibility.Visible;
        dock.CompactHeight = ActualHeight > 0 && ActualHeight < 860;
    }

    private void ApplyAppearance()
    {
        Services.Theme.Apply(Application.Current.Resources, settings.Theme, settings.SeedColor, settings.HighContrast);
        FontSize = settings.FontSize;
        Application.Current.Resources["TargetHeight"] = settings.LargerTargets ? 48.0 : 40.0;
        Title = settings.DisplayName;
        DisplayNameText.Text = settings.DisplayName;
        AppIcon.Text = Icons.DeployedCode;
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version?.ToString(3) ?? "unavailable";
        var stamp = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(x => x.Key == "BuildTimestamp")?.Value;
        Provenance.Text = $"{Loc.T("Version")} {version} · {Loc.T("Updated")} " + (DateTimeOffset.TryParse(stamp, out var t) ? t.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + " UTC" + t.ToLocalTime().ToString("zzz") : "provenance unavailable");
        RailColumn.Width = new GridLength(settings.FocusMode ? 72 : 96);
        clockText.Visibility = settings.TimeAwareness ? Visibility.Visible : Visibility.Collapsed;
        Adapt();
    }

    // ----- Title bar -----
    private void BuildTitleBar()
    {
        var paletteButton = new Button { Style = (Style)FindResource("Btn.Base"), Height = 32, MinHeight = 32, Padding = new Thickness(14, 0, 8, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch, FontWeight = FontWeights.Normal };
        paletteButton.SetResourceReference(BackgroundProperty, "SurfaceContainer");
        paletteButton.SetResourceReference(ForegroundProperty, "OnSurfaceVariant");
        var hint = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(6, 1, 6, 1), Child = Ui.Text("Ctrl+Shift+F", 11, null, "Outline", false, false) };
        hint.SetResourceReference(Border.BorderBrushProperty, "OutlineVariant");
        var content = new DockPanel();
        content.Children.Add(Ui.Icon(Icons.Search, 18).Margin(0, 0, 10, 0).Dock(Dock.Left));
        content.Children.Add(hint.Dock(Dock.Right));
        content.Children.Add(Ui.Text("Search commands, resources and settings", 13, null, "OnSurfaceVariant", false));
        paletteButton.Content = content;
        AutomationProperties.SetName(paletteButton, Loc.T("Command palette") + " (Ctrl+Shift+F)");
        paletteButton.Click += (_, _) => OpenPalette();
        WindowChrome.SetIsHitTestVisibleInChrome(paletteButton, true);
        PaletteSlot.Child = paletteButton;

        contexts.ToolTip = Loc.T("Engine context for this window. The global Docker context is never changed.");
        AutomationProperties.SetName(contexts, Loc.T("Engine context"));
        contexts.SelectionChanged += async (_, _) =>
        {
            if (suppressContext) return;
            var selected = contexts.SelectedItem as string;
            if (selected == host.Context) return;
            var dirty = pages.Values.OfType<IDirtyAware>().Where(p => p.IsDirty).ToList();
            host.SetContext(selected);
            host.Notify.Info($"Engine context is now {selected}. Engine views refreshed; open drafts were kept.");
            await CheckEngine();
        };
        var contextChip = Ui.HStack(0, contextState, contexts);
        contextChip.VerticalAlignment = VerticalAlignment.Center;
        WindowChrome.SetIsHitTestVisibleInChrome(contexts, true);
        var bell = Ui.IconButton(Icons.Notifications, "Notifications", ShowNotifications);
        var badgeBorder = new Border { Child = badge, MinWidth = 14, Height = 14, CornerRadius = new CornerRadius(7), Padding = new Thickness(3, 0, 3, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 6, 0), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        badgeBorder.SetResourceReference(Border.BackgroundProperty, "Error");
        badge.HorizontalAlignment = HorizontalAlignment.Center;
        var bellGrid = new Grid(); bellGrid.Children.Add(bell); bellGrid.Children.Add(badgeBorder);
        var access = Ui.IconButton(Icons.AccessibilityNew, "Accessibility tools", ShowAccessibility);
        access.Visibility = settings.AccessibilityMenuVisible ? Visibility.Visible : Visibility.Collapsed;
        host.SettingsChanged += () => access.Visibility = settings.AccessibilityMenuVisible ? Visibility.Visible : Visibility.Collapsed;
        foreach (var e in new FrameworkElement[] { bell, access }) WindowChrome.SetIsHitTestVisibleInChrome(e, true);
        clockText.Margin = new Thickness(0, 0, 12, 0);
        TitleActions.Children.Add(clockText);
        TitleActions.Children.Add(contextChip);
        TitleActions.Children.Add(bellGrid.Margin(8, 0, 0, 0));
        TitleActions.Children.Add(access);
    }

    private async Task RefreshContexts()
    {
        try
        {
            var values = await host.Docker.ListContextsAsync();
            suppressContext = true;
            contexts.ItemsSource = values.Select(x => x.Name).ToArray();
            var saved = host.Context;
            contexts.SelectedItem = values.Any(x => x.Name == saved) ? saved : values.FirstOrDefault(x => x.Current)?.Name ?? values.FirstOrDefault()?.Name;
            suppressContext = false;
            if (contexts.SelectedItem as string != host.Context) host.SetContext(contexts.SelectedItem as string);
            await CheckEngine();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or Core.DockerCommandException or InvalidOperationException or System.Text.Json.JsonException)
        {
            suppressContext = false;
            contexts.ItemsSource = new[] { Loc.T("Docker unavailable") };
            contexts.SelectedIndex = 0;
            contextState.SetResourceReference(Border.BackgroundProperty, "Error");
            contextState.ToolTip = e is System.ComponentModel.Win32Exception ? "The docker command was not found." : e.Message;
        }
    }

    private async Task CheckEngine()
    {
        contextState.SetResourceReference(Border.BackgroundProperty, "Outline");
        var result = await host.Read(["version", "--format", "{{.Server.Version}}"]);
        contextState.SetResourceReference(Border.BackgroundProperty, result.Success ? "Success" : "Error");
        contextState.ToolTip = result.Success ? "Engine " + result.StandardOutput.Trim() + " reachable" : "Engine unreachable: " + result.StandardError.Trim();
        AutomationProperties.SetHelpText(contexts, contextState.ToolTip?.ToString() ?? "");
    }

    // ----- Rail and navigation -----
    private void BuildRail()
    {
        foreach (var destination in Destinations.All.Where(d => d.OnRail))
        {
            var item = new RadioButton { Style = (Style)FindResource("Rail.Item"), Tag = destination.Icon, Content = new TextBlock { Text = Loc.T(destination.Key), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 76, TextAlignment = TextAlignment.Center }, GroupName = "rail", ToolTip = Loc.T(destination.Description) };
            AutomationProperties.SetName(item, Loc.T(destination.Key));
            item.Checked += (_, _) => { if (!navigating) Navigate(destination.Key); };
            item.ContextMenu = new ContextMenu();
            item.ContextMenu.Items.Add(Ui.MenuItem("Open in new tab", Icons.Tab, () => OpenTab(destination.Key, false)));
            railItems[destination.Key] = item;
            (destination.Key == "Settings" ? RailBottom : Rail).Children.Add(item);
        }
    }

    private bool navigating;
    public void Navigate(string key)
    {
        if (Destinations.Find(key) is null) return;
        if (tabs.Count == 0) { tabs.Add(new(key, false, true)); currentTab = 0; }
        tabs[currentTab] = tabs[currentTab] with { Destination = key };
        ShowCurrent();
    }

    private PageBase Page(string key)
    {
        if (!pages.TryGetValue(key, out var page))
        {
            page = Destinations.Find(key)!.Create();
            pages[key] = page;
            foreach (var command in page.PageCommands()) host.Commands.Register(command);
        }
        return page;
    }

    private void ShowCurrent()
    {
        if (tabs.Count == 0) return;
        currentTab = Math.Clamp(currentTab, 0, tabs.Count - 1);
        var key = tabs[currentTab].Destination;
        var page = Page(key);
        if (page.Parent is ContentControl old && old != PageHost) old.Content = null;
        PageHost.Content = page;
        navigating = true;
        foreach (var pair in railItems) pair.Value.IsChecked = pair.Key == key;
        navigating = false;
        settings.LastDestination = key;
        PersistTabs();
        RenderTabs();
        _ = page.ShowAsync();
    }

    // ----- Tabs -----
    private void RestoreTabs()
    {
        var first = settings.SetupComplete ? (Destinations.Find(settings.LastDestination) != null ? settings.LastDestination : "Build") : "Setup";
        tabs.Add(new(first, true, true));
        foreach (var key in settings.OpenTabs.Where(k => Destinations.Find(k) != null).Take(12)) tabs.Add(new(key, settings.PinnedTabs.Contains(key), false));
        currentTab = 0;
        var newTab = Ui.IconButton(Icons.Add, "New tab", ShowCatalog);
        newTab.ToolTip = Loc.T("New tab") + " (Ctrl+T)";
        TabTools.Children.Add(Ui.IconButton(Icons.Search, "Search open tabs", SearchTabs));
        TabTools.Children.Add(newTab);
        ShowCurrent();
    }

    private void PersistTabs()
    {
        settings.OpenTabs = tabs.Where(t => !t.Workspace).Select(t => t.Destination).ToList();
        settings.PinnedTabs = tabs.Where(t => t.Pinned && !t.Workspace).Select(t => t.Destination).ToList();
        try { settings.Save(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private string TabTitle(TabState tab) => tab.Workspace
        ? (string.IsNullOrWhiteSpace(settings.BuildFolder) ? Loc.T("Workspace") : Path.GetFileName(Path.TrimEndingDirectorySeparator(settings.BuildFolder))) + " · " + Loc.T(tab.Destination)
        : Loc.T(tab.Destination);

    private void RenderTabs()
    {
        Tabs.Children.Clear();
        for (var i = 0; i < tabs.Count; i++)
        {
            var index = i; var tab = tabs[i];
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            if (tab.Pinned || tab.Workspace) content.Children.Add(Ui.Icon(tab.Workspace ? Icons.FolderOpen : Icons.Keep, 16, true, "Primary").Margin(0, 0, 8, 0));
            content.Children.Add(new TextBlock { Text = TabTitle(tab), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis });
            if (!tab.Workspace)
            {
                var close = Ui.IconButton(Icons.Close, "Close tab " + TabTitle(tab), () => CloseTab(index));
                close.Width = close.Height = close.MinHeight = 28; close.FontSize = 15; close.Margin = new Thickness(6, 0, 0, 0);
                content.Children.Add(close);
            }
            else content.Children.Add(new Border { Width = 8 });
            var radio = new RadioButton { Style = (Style)FindResource("Tab.Item"), Content = content, GroupName = "tabs", IsChecked = i == currentTab, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 4, 0) };
            AutomationProperties.SetName(radio, TabTitle(tab));
            radio.Checked += (_, _) => { if (currentTab != index) { currentTab = index; ShowCurrent(); } };
            radio.MouseUp += (_, e) => { if (e.ChangedButton == MouseButton.Middle && !tab.Workspace) CloseTab(index); };
            var menu = new ContextMenu();
            if (!tab.Workspace) menu.Items.Add(Ui.MenuItem(tab.Pinned ? "Unpin tab" : "Pin tab", Icons.Keep, () => { tabs[index] = tab with { Pinned = !tab.Pinned }; Reorder(); }));
            if (!tab.Workspace) menu.Items.Add(Ui.MenuItem("Close tab", Icons.Close, () => CloseTab(index), "Ctrl+W"));
            menu.Items.Add(Ui.MenuItem("Close other tabs", Icons.Close, () => CloseWhere(t => t != tab)));
            menu.Items.Add(Ui.MenuItem("Close tabs containing text…", Icons.FilterList, () => _ = CloseByText(true)));
            menu.Items.Add(Ui.MenuItem("Close tabs not containing text…", Icons.FilterList, () => _ = CloseByText(false)));
            menu.Items.Add(new Separator());
            if (i > 1) menu.Items.Add(Ui.MenuItem("Move left", Icons.ChevronLeft, () => Move(index, -1)));
            if (i > 0 && i < tabs.Count - 1) menu.Items.Add(Ui.MenuItem("Move right", Icons.ChevronRight, () => Move(index, 1)));
            radio.ContextMenu = menu;
            Tabs.Children.Add(radio);
        }
    }

    private void Reorder()
    {
        var current = tabs[currentTab];
        var ordered = tabs.Where(t => t.Workspace).Concat(tabs.Where(t => !t.Workspace && t.Pinned)).Concat(tabs.Where(t => !t.Workspace && !t.Pinned)).ToList();
        tabs.Clear(); tabs.AddRange(ordered); currentTab = tabs.IndexOf(current);
        PersistTabs(); RenderTabs();
    }
    private void Move(int index, int delta)
    {
        var target = index + delta;
        if (target < 1 || target >= tabs.Count) return;
        (tabs[index], tabs[target]) = (tabs[target], tabs[index]);
        if (currentTab == index) currentTab = target; else if (currentTab == target) currentTab = index;
        PersistTabs(); RenderTabs();
    }
    public void OpenTab(string key, bool pinned)
    {
        if (settings.OneThingAtATime) { host.Notify.Info("One thing at a time is on, so the current tab shows " + Loc.T(key) + " instead of opening another tab."); Navigate(key); return; }
        var existing = tabs.FindIndex(t => !t.Workspace && t.Destination == key);
        if (existing >= 0) { currentTab = existing; ShowCurrent(); return; }
        tabs.Add(new(key, pinned, false));
        currentTab = tabs.Count - 1;
        ShowCurrent();
    }
    private void CloseTab(int index)
    {
        if (index <= 0 || index >= tabs.Count || tabs[index].Workspace) return;
        tabs.RemoveAt(index);
        if (currentTab >= index) currentTab = Math.Max(0, currentTab - 1);
        ShowCurrent();
    }
    private void CloseWhere(Func<TabState, bool> predicate)
    {
        var current = tabs[currentTab];
        tabs.RemoveAll(t => !t.Workspace && !t.Pinned && predicate(t));
        currentTab = Math.Max(0, tabs.IndexOf(current));
        ShowCurrent();
    }
    private async Task CloseByText(bool containing)
    {
        var text = await overlay.PromptAsync(containing ? "Close tabs containing text" : "Close tabs not containing text", "Text to match", "", "Pinned tabs and the workspace tab are never closed. A preview is shown before anything closes.");
        if (text == null) return;
        var candidates = tabs.Where(t => !t.Workspace && !t.Pinned && TabTitle(t).Contains(text, StringComparison.OrdinalIgnoreCase) == containing).ToList();
        if (candidates.Count == 0) { host.Notify.Info("No unpinned tabs match. Nothing was closed."); return; }
        if (!await overlay.ConfirmAsync($"Close {candidates.Count} tab(s)?", string.Join(Environment.NewLine, candidates.Select(TabTitle)), "Close tabs")) return;
        CloseWhere(candidates.Contains);
    }

    /// <summary>New tab catalog: every destination with icons, its own plain-text search and adjacent regex builder.</summary>
    private void ShowCatalog()
    {
        Action? close = null;
        var search = new SearchFilter("Search destinations, tools and settings", 480);
        var list = new ListBox { MinHeight = 320, MaxHeight = 460 };
        void Fill()
        {
            list.Items.Clear();
            foreach (var d in Destinations.All.Where(d => search.Matches(d.Key + " " + Loc.T(d.Key) + " " + d.Description)))
            {
                var row = Ui.HStack(14, Ui.Icon(d.Icon, 22, false, "Primary"), Ui.VStack(2, Ui.Text(d.Key, 14, FontWeights.SemiBold), Ui.Caption(d.Description)));
                list.Items.Add(new ListBoxItem { Content = row, Tag = d.Key });
            }
            if (list.Items.Count > 0) list.SelectedIndex = 0;
        }
        void Open() { if (list.SelectedItem is ListBoxItem { Tag: string key }) { close!(); OpenTab(key, false); } }
        search.Changed += Fill;
        search.Box.PreviewKeyDown += (_, e) => { if (e.Key == Key.Down) { list.Focus(); e.Handled = true; } else if (e.Key == Key.Enter) { Open(); e.Handled = true; } };
        list.KeyDown += (_, e) => { if (e.Key == Key.Enter) Open(); };
        list.MouseDoubleClick += (_, _) => Open();
        Fill();
        var actions = Ui.HStack(8, Ui.Button("Cancel", null, ButtonKind.Text, () => close!()), Ui.Button("Open in new tab", Icons.Tab, ButtonKind.Filled, Open));
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        close = overlay.Show(Ui.VStack(14, Ui.Title("New tab"), Ui.Caption("Opening a destination that already has a tab focuses that tab, so drafts are never duplicated."), search, list, actions), 620);
    }

    private void SearchTabs()
    {
        Action? close = null;
        var search = new SearchFilter("Search open tabs", 440);
        var list = new ListBox { MinHeight = 200 };
        void Fill() { list.Items.Clear(); for (var i = 0; i < tabs.Count; i++) if (search.Matches(TabTitle(tabs[i]))) list.Items.Add(new ListBoxItem { Content = TabTitle(tabs[i]), Tag = i }); if (list.Items.Count > 0) list.SelectedIndex = 0; }
        void Go() { if (list.SelectedItem is ListBoxItem { Tag: int i }) { close!(); currentTab = i; ShowCurrent(); } }
        search.Changed += Fill; list.MouseDoubleClick += (_, _) => Go(); list.KeyDown += (_, e) => { if (e.Key == Key.Enter) Go(); };
        search.Box.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { Go(); e.Handled = true; } else if (e.Key == Key.Down) list.Focus(); };
        Fill();
        close = overlay.Show(Ui.VStack(12, Ui.Title("Search open tabs"), search, list), 540);
    }

    // ----- Commands and palette -----
    private void RegisterCommands()
    {
        var c = host.Commands;
        c.Register(new AppCommand { Id = "palette", Title = "Open command palette", Icon = Icons.Search, Gesture = new KeyGesture(Key.F, ModifierKeys.Control | ModifierKeys.Shift), Run = OpenPalette });
        c.Register(new AppCommand { Id = "tab.new", Title = "New tab", Icon = Icons.Tab, Gesture = new KeyGesture(Key.T, ModifierKeys.Control), Run = ShowCatalog });
        c.Register(new AppCommand { Id = "tab.close", Title = "Close tab", Icon = Icons.Close, Gesture = new KeyGesture(Key.W, ModifierKeys.Control), Run = () => CloseTab(currentTab) });
        c.Register(new AppCommand { Id = "tab.next", Title = "Next tab", Icon = Icons.ChevronRight, Gesture = new KeyGesture(Key.Tab, ModifierKeys.Control), Run = () => { currentTab = (currentTab + 1) % tabs.Count; ShowCurrent(); } });
        c.Register(new AppCommand { Id = "tab.search", Title = "Search open tabs", Icon = Icons.Search, Gesture = new KeyGesture(Key.A, ModifierKeys.Control | ModifierKeys.Shift), Run = SearchTabs });
        c.Register(new AppCommand { Id = "notifications", Title = "Show notification centre", Icon = Icons.Notifications, Run = ShowNotifications });
        c.Register(new AppCommand { Id = "accessibility", Title = "Accessibility tools", Icon = Icons.AccessibilityNew, Gesture = new KeyGesture(Key.F1, ModifierKeys.Alt), Run = ShowAccessibility });
        c.Register(new AppCommand { Id = "output.toggle", Title = "Show or hide the output panel", Icon = Icons.Terminal, Gesture = new KeyGesture(Key.J, ModifierKeys.Control), Run = () => { dock.SetExpanded(!dock.Expanded); settings.OutputDockOpen = dock.Expanded; host.SaveSettings(null); } });
        c.Register(new AppCommand { Id = "contexts.refresh", Title = "Refresh engine contexts", Icon = Icons.Refresh, Gesture = new KeyGesture(Key.F5, ModifierKeys.Shift), Run = () => _ = RefreshContexts() });
        var number = 1;
        foreach (var d in Destinations.All)
        {
            var key = d.Key;
            c.Register(new AppCommand { Id = "go." + key, Title = "Go to " + key, Group = "Destinations", Icon = d.Icon, Keywords = d.Description, Gesture = number <= 9 && d.OnRail ? new KeyGesture(Key.D0 + number, ModifierKeys.Control) : null, Run = () => Navigate(key) });
            if (d.OnRail) number++;
        }
        c.Register(new AppCommand { Id = "set.theme", Title = "Theme", Group = "Settings", Icon = Icons.Palette, Keywords = "dark light appearance", LiveControl = () => Ui.Segmented(["System", "Light", "Dark"], settings.Theme, v => { settings.Theme = v; host.SaveSettings("Theme set to " + v); }) });
        c.Register(new AppCommand { Id = "set.motion", Title = "Reduced motion", Group = "Settings", Icon = Icons.Speed, Keywords = "animation accessibility", LiveControl = () => Ui.Switch("Reduced motion", settings.ReducedMotion, v => { settings.ReducedMotion = v; host.SaveSettings("Reduced motion " + (v ? "on" : "off")); }) });
        c.Register(new AppCommand { Id = "set.contrast", Title = "High contrast", Group = "Settings", Icon = Icons.Contrast, Keywords = "accessibility", LiveControl = () => Ui.Switch("High contrast", settings.HighContrast, v => { settings.HighContrast = v; host.SaveSettings("High contrast " + (v ? "on" : "off")); }) });
        c.Register(new AppCommand { Id = "set.language", Title = "Language", Group = "Settings", Icon = Icons.Translate, Keywords = "cantonese bilingual english", LiveControl = () => Ui.Segmented(["English", "Cantonese", "Bilingual"], settings.Language, v => { settings.Language = v; Loc.Language = v; host.SaveSettings("Language set to " + v); host.Notify.Info("Language saved. Open pages refresh their labels when reopened."); }) });
        c.Register(new AppCommand { Id = "set.sounds", Title = "Action sounds", Group = "Settings", Icon = Icons.VolumeUp, LiveControl = () => Ui.Switch("Action sounds", settings.Sounds, v => { settings.Sounds = v; host.SaveSettings("Action sounds " + (v ? "on" : "off")); }) });
        c.Register(new AppCommand { Id = "set.output", Title = "Output panel open", Group = "Settings", Icon = Icons.Terminal, LiveControl = () => Ui.Switch("Output panel open", dock.Expanded, v => { dock.SetExpanded(v); settings.OutputDockOpen = v; host.SaveSettings(null); }) });
    }

    private void OpenPalette()
    {
        foreach (var d in Destinations.All) Page(d.Key);
        palette ??= new CommandPalette(host, overlay);
        palette.Open(settings.PaletteFullWindow);
    }

    private void ShowNotifications()
    {
        host.Notify.MarkRead();
        Action? close = null;
        var table = new DataTable<Notice>("Notifications", "Search notifications") { EmptyTitle = "No notifications", EmptyMessage = "Messages appear here after they leave the screen, so you can review them later." };
        table.Column("Time", n => n.WhenText, 90).ChipColumn("Kind", n => n.KindText, n => n.Kind switch { NoticeKind.Error => Tone.Error, NoticeKind.Warning => Tone.Warning, NoticeKind.Success => Tone.Success, _ => Tone.Neutral }, 100).Column("Message", n => n.Message, 520);
        table.Filter("Errors and warnings", n => n.Kind is NoticeKind.Error or NoticeKind.Warning);
        table.BulkAction("Dismiss", Icons.Delete, items => { foreach (var n in items) host.Notify.Centre.Remove(n); table.SetItems(host.Notify.Centre); return Task.CompletedTask; });
        table.RowAction("Copy message", Icons.ContentCopy, n => { Clipboard.SetText(n.Message); return Task.CompletedTask; }, "Ctrl+C");
        table.RowAction("Dismiss", Icons.Delete, n => { host.Notify.Centre.Remove(n); table.SetItems(host.Notify.Centre); return Task.CompletedTask; }, "Del");
        table.SetItems(host.Notify.Centre);
        table.Height = 460;
        var actions = Ui.HStack(8, Ui.Button("Clear all", Icons.Delete, ButtonKind.Text, () => { host.Notify.Centre.Clear(); table.SetItems(host.Notify.Centre); }), Ui.Button("Close", null, ButtonKind.Filled, () => close!()));
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        close = overlay.Show(Ui.VStack(12, Ui.Title("Notification centre"), table, actions), 860);
    }

    private void ShowAccessibility()
    {
        Action? close = null;
        var size = new Slider { Minimum = 12, Maximum = 22, Value = settings.FontSize, TickFrequency = 1, IsSnapToTickEnabled = true };
        size.ValueChanged += (_, _) => { settings.FontSize = size.Value; host.SaveSettings(null); };
        var hide = Ui.Button("Hide this menu…", Icons.Visibility, ButtonKind.Text, async () =>
        {
            if (!await overlay.ConfirmAsync("Hide the accessibility menu?", "Your accessibility preferences stay as they are. You can show the menu again from Settings, Accessibility, or from the command palette.", "Hide menu")) return;
            settings.AccessibilityMenuVisible = false; host.SaveSettings("Accessibility menu hidden"); close!();
        });
        var body = Ui.VStack(12,
            Ui.Title("Accessibility tools"),
            Ui.Field("Text size", size),
            Ui.Switch("High contrast", settings.HighContrast, v => { settings.HighContrast = v; host.SaveSettings("High contrast " + (v ? "on" : "off")); }),
            Ui.Switch("Reduced motion", settings.ReducedMotion, v => { settings.ReducedMotion = v; host.SaveSettings("Reduced motion " + (v ? "on" : "off")); }),
            Ui.Switch("Larger targets", settings.LargerTargets, v => { settings.LargerTargets = v; host.SaveSettings("Larger targets " + (v ? "on" : "off")); }),
            Ui.Switch("Read notifications aloud", settings.Narration, v => { settings.Narration = v; host.SaveSettings("Narration " + (v ? "on" : "off")); }),
            Ui.Card(Ui.VStack(4, Ui.Overline("Keyboard"), Ui.Caption("Ctrl+Shift+F command palette · Ctrl+1…9 destinations · Ctrl+T new tab · Ctrl+W close tab · Ctrl+Tab next tab · Ctrl+J output panel · Alt+R regex builder in a search · Escape closes dialogs")), 12, "SurfaceHigh", 12),
            Ui.Columns(8, [1, 0], hide, Ui.Button("Done", null, ButtonKind.Filled, () => close!())));
        close = overlay.Show(body, 480);
    }

    // ----- Startup surprise -----
    private static readonly (string English, string Cantonese)[] DimSum = [("Har gow (shrimp dumplings)", "蝦餃"), ("Siu mai (pork dumplings)", "燒賣"), ("Char siu bao (barbecue pork buns)", "叉燒包"), ("Cheung fun (rice noodle rolls)", "腸粉"), ("Lo mai gai (sticky rice in lotus leaf)", "糯米雞"), ("Egg tarts", "蛋撻"), ("Turnip cake", "蘿蔔糕"), ("Phoenix claws", "鳳爪")];
    private void MaybeDimSum()
    {
        if (!settings.SetupComplete || settings.SchoolMode || host.Busy) return;
        if (Random.Shared.NextDouble() >= 0.10) return;
        var dish = DimSum[Random.Shared.Next(DimSum.Length)];
        host.Notify.Info($"Dim sum surprise: {dish.English} · {dish.Cantonese}");
    }

    // ----- Closing -----
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (CaptureMode) return;
        if (host.Busy) { e.Cancel = true; host.Notify.Warning("Cancel the active operation and wait for its result before closing."); return; }
        var dirty = pages.Values.OfType<IDirtyAware>().Where(p => p.IsDirty).ToList();
        if (dirty.Count == 0 || allowClose) { try { settings.Save(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } return; }
        e.Cancel = true;
        if (await overlay.ConfirmAsync("Discard unsaved drafts?", string.Join(Environment.NewLine, dirty.Select(d => d.DirtyLabel)) + Environment.NewLine + Environment.NewLine + "The discard is recorded in local history.", "Discard and close", true))
        {
            host.History.Record("discard", "Closed with unsaved drafts: " + string.Join(", ", dirty.Select(d => d.DirtyLabel)));
            allowClose = true;
            Close();
        }
    }
    private bool allowClose;

    // ----- Capture mode -----
    /// <summary>Renders every destination (and the palette, notification centre and super confirmation) to PNG files.</summary>
    public async Task<int> CaptureAsync(string directory, string suffix, double width = 1440, double height = 960)
    {
        // Lay the window content out at the exact requested size, independent of the runner's screen size.
        Width = width; Height = height;
        Root.Width = width; Root.Height = height; Root.HorizontalAlignment = HorizontalAlignment.Left; Root.VerticalAlignment = VerticalAlignment.Top;
        Directory.CreateDirectory(directory);
        var written = 0;
        async Task Shot(string name)
        {
            host.Notify.ClearToasts();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(250);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            UpdateLayout();
            var width = (int)Math.Ceiling(Root.ActualWidth); var height = (int)Math.Ceiling(Root.ActualHeight);
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) { dc.DrawRectangle((Brush)FindResource("Surface"), null, new Rect(0, 0, width, height)); dc.DrawRectangle(new VisualBrush(Root) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, width, height) }, null, new Rect(0, 0, width, height)); }
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            await using var file = File.Create(Path.Combine(directory, $"{name}{suffix}.png"));
            encoder.Save(file);
            written++;
        }
        await RefreshContexts();
        // Populate the Creator from a real starter and hand it to Build so Build and Canvas show a real Dockerfile.
        async Task RunCommand(string id) { if (host.Commands.Find(id)?.Run is { } run) { run(); await Task.Delay(1500); } }
        foreach (var d in Destinations.All) Page(d.Key);
        await RunCommand("creator.starter.node");
        await RunCommand("creator.tobuild");
        foreach (var d in Destinations.All)
        {
            Navigate(d.Key);
            var page = Page(d.Key);
            try { await page.SafeRefresh().WaitAsync(TimeSpan.FromSeconds(30)); } catch (TimeoutException) { }
            if (d.Key == "Monitor") await Task.Delay(4000);
            await Shot(d.Key.ToLowerInvariant());
        }
        Navigate("Containers");
        OpenPalette();
        await Shot("command-palette");
        host.Overlay.CloseAll();
        Navigate("System");
        _ = host.Overlay.SuperConfirmAsync("Remove unused data", "Super confirmation preview captured for documentation. Nothing is removed unless both keys are on and the slider reaches the end.", ["Stopped containers", "Dangling images", "Unused networks", "Build cache"]);
        await Shot("super-confirmation");
        host.Overlay.CloseAll();
        ShowNotifications();
        await Shot("notification-centre");
        host.Overlay.CloseAll();
        return written;
    }

    /// <summary>Capture mode only: lets the window be larger than the runner's screen so captures use real layout sizes.</summary>
    private static IntPtr AllowLargerThanScreen(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024;
        if (message != WM_GETMINMAXINFO) return IntPtr.Zero;
        // MINMAXINFO: ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize (each two 32-bit ints).
        System.Runtime.InteropServices.Marshal.WriteInt32(lParam, 8, 4096);
        System.Runtime.InteropServices.Marshal.WriteInt32(lParam, 12, 4096);
        System.Runtime.InteropServices.Marshal.WriteInt32(lParam, 32, 4096);
        System.Runtime.InteropServices.Marshal.WriteInt32(lParam, 36, 4096);
        handled = true;
        return IntPtr.Zero;
    }

    public void OpenDockerfile(string path)
    {
        host.HandOff["Build.OpenDockerfile"] = Path.GetFullPath(path);
        Navigate("Build");
    }
}
