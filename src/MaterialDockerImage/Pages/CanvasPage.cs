using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Canvas destination: a native graph of the Build editor's Dockerfile (stages and their FROM / COPY --from links) or
/// of the engine's runtime topology (containers, networks, volumes). Left: searchable object list. Right: inspector.
/// The Dockerfile view reads and edits the Build editor through the Build.* hand-offs; it never writes files itself.
/// </summary>
public sealed partial class CanvasPage : PageBase
{
    public override string Key => "Canvas";
    private enum View { Dockerfile, Runtime }

    private readonly GraphSurface surface = new();
    private readonly ListBox objects = new();
    private readonly SearchFilter objectSearch = new("Search objects", 200);
    private readonly StackPanel inspector = new();
    private readonly ContentControl inspectorActions = new();
    private readonly TextBlock objectsNote = Ui.Caption("");
    private readonly TextBlock zoomText = Ui.Text("100%", 13, null, "OnSurfaceVariant", false, false);
    private readonly TextBlock subtitle;
    private readonly Border warning;
    private readonly TextBlock warningText = Ui.Text("", 13, null, "OnWarningContainer");
    private readonly Grid graphHost = new();
    private readonly GraphMinimap minimap;
    private readonly Button minimapToggle;
    private bool minimapWanted = true;
    private FrameworkElement? emptyState;
    private View view = View.Dockerfile;
    private readonly List<GraphObject> objectItems = new();
    private object? selectedKey;

    /// <summary>One row of the object list. Indent 1 marks a child of the previous stage or resource.</summary>
    private sealed record GraphObject(string Icon, string Label, string Meta, int Indent, object Key, Action Select, Action? Activate = null);

    public CanvasPage()
    {
        var refresh = Ui.AsyncButton("Refresh", Icons.Refresh, ButtonKind.Outlined, RefreshAsync);
        refresh.ToolTip = Loc.T("Parse the Build editor again, or reload the runtime topology.");
        var openBuild = Ui.Button("Open in Build", Icons.Construction, ButtonKind.Tonal, () => Host.Navigate("Build"));
        var header = Ui.Header("Canvas", "", out subtitle, refresh, openBuild);

        var segmented = Ui.Segmented(["Dockerfile", "Runtime topology"], "Dockerfile", option =>
        {
            view = option == "Dockerfile" ? View.Dockerfile : View.Runtime;
            PageTasks.Fire(RefreshAsync);
        });
        segmented.Width = 320; segmented.HorizontalAlignment = HorizontalAlignment.Left;
        var zoomOut = Ui.IconButton(Icons.ZoomOut, "Zoom out (minus)", () => surface.Zoom(1 / 1.2));
        var zoomIn = Ui.IconButton(Icons.ZoomIn, "Zoom in (plus)", () => surface.Zoom(1.2));
        var fit = Ui.IconButton(Icons.FitScreen, "Fit to screen (0)", surface.Fit);
        var reset = Ui.IconButton(Icons.CenterFocusStrong, "Actual size", () => surface.SetZoom(1));
        zoomText.MinWidth = 44; zoomText.TextAlignment = TextAlignment.Center;
        surface.ZoomChanged += z => zoomText.Text = $"{z:P0}";
        minimap = new GraphMinimap(surface);
        minimapToggle = Ui.IconButton(Icons.VisibilityOff, "Hide minimap (M)", ToggleMinimap);
        var zoomBar = Ui.HStack(0, zoomOut, zoomText, zoomIn, fit, reset, minimapToggle);
        zoomBar.HorizontalAlignment = HorizontalAlignment.Right;
        // Design: the toolbar is a 56 px strip with a 1 px divider inside the centre card. When the centre card is too
        // narrow for the view switch and the zoom controls side by side, the zoom controls move to a second row.
        var toolbarRow = new Grid();
        toolbarRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbarRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbarRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        toolbarRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        toolbarRow.Children.Add(segmented);
        toolbarRow.Children.Add(zoomBar);
        var toolbar = new Border { Child = toolbarRow, MinHeight = 56, Padding = new Thickness(16, 6, 10, 6), BorderThickness = new Thickness(0, 0, 0, 1) };
        toolbar.SetResourceReference(Border.BorderBrushProperty, "SurfaceHighest");
        toolbarRow.VerticalAlignment = VerticalAlignment.Center;
        void PlaceZoomBar(double available)
        {
            zoomBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var stacked = available > 0 && available < segmented.Width + 8 + zoomBar.DesiredSize.Width;
            Grid.SetRow(zoomBar, stacked ? 1 : 0);
            Grid.SetColumn(zoomBar, stacked ? 0 : 1);
            zoomBar.HorizontalAlignment = stacked ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            zoomBar.Margin = new Thickness(stacked ? 0 : 8, stacked ? 8 : 0, 0, 0);
        }
        toolbar.SizeChanged += (_, e) => PlaceZoomBar(e.NewSize.Width - 26);
        PlaceZoomBar(0);

        warning = Ui.Card(Ui.Columns(10, [0, 1], Ui.Icon(Icons.Warning, 20, true, "OnWarningContainer"), warningText), 12, "WarningContainer", 12);
        warning.Visibility = Visibility.Collapsed;
        graphHost.Children.Add(surface);
        graphHost.Children.Add(minimap);
        // The minimap would cover too much of a small graph area (narrow windows), so it steps aside there.
        graphHost.SizeChanged += (_, _) => UpdateMinimap();
        // M works wherever focus is in the graph area, the minimap included; a hidden minimap hands focus back to the graph.
        graphHost.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.M || e.OriginalSource is TextBox || System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.None) return;
            var hadFocus = minimap.IsKeyboardFocusWithin;
            ToggleMinimap();
            if (hadFocus && minimap.Visibility != Visibility.Visible) surface.Focus();
            e.Handled = true;
        };
        var centreInner = new DockPanel();
        centreInner.Children.Add(toolbar.Dock(System.Windows.Controls.Dock.Top));
        warning.Margin = new Thickness(8, 8, 8, 0);
        centreInner.Children.Add(warning.Dock(System.Windows.Controls.Dock.Top));
        graphHost.Margin = new Thickness(8);
        centreInner.Children.Add(graphHost);
        var centre = Ui.Card(centreInner, 0, "SurfaceLow", 16);
        centre.ClipToBounds = true;

        objectSearch.Width = double.NaN;
        objectSearch.Changed += FillObjects;
        System.Windows.Automation.AutomationProperties.SetName(objects, Loc.T("Objects"));
        ScrollViewer.SetHorizontalScrollBarVisibility(objects, ScrollBarVisibility.Disabled);
        objects.SelectionChanged += (_, _) => { if (objects.SelectedItem is ListBoxItem { Tag: GraphObject o } && !Equals(o.Key, selectedKey)) o.Select(); };
        objects.MouseDoubleClick += (_, _) => { if (objects.SelectedItem is ListBoxItem { Tag: GraphObject o }) o.Activate?.Invoke(); };
        objects.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter && objects.SelectedItem is ListBoxItem { Tag: GraphObject o }) { (o.Activate ?? o.Select)(); e.Handled = true; } };
        var left = new DockPanel();
        left.Children.Add(Ui.Overline("Objects").Margin(8, 0, 8, 0).Dock(System.Windows.Controls.Dock.Top));
        objectSearch.Margin = new Thickness(0, 10, 0, 10);
        left.Children.Add(objectSearch.Dock(System.Windows.Controls.Dock.Top));
        objectsNote.Margin = new Thickness(0, 10, 0, 0);
        left.Children.Add(objectsNote.Dock(System.Windows.Controls.Dock.Bottom));
        left.Children.Add(objects);

        var body = new Grid();
        var leftColumn = new ColumnDefinition { Width = new GridLength(220) };
        var rightColumn = new ColumnDefinition { Width = new GridLength(300) };
        body.ColumnDefinitions.Add(leftColumn);
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 240 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        body.ColumnDefinitions.Add(rightColumn);
        body.SizeChanged += (_, e) =>
        {
            var narrow = e.NewSize.Width < 1040;
            leftColumn.Width = new GridLength(narrow ? 180 : 220);
            rightColumn.Width = new GridLength(narrow ? 260 : 300);
        };
        body.Children.Add(Ui.Card(left, 16, "SurfaceLow", 16));
        body.Children.Add(centre.At(0, 2));
        var inspectorBody = new DockPanel();
        inspectorActions.Margin = new Thickness(0, 12, 0, 0);
        inspectorBody.Children.Add(inspectorActions.Dock(System.Windows.Controls.Dock.Bottom));
        inspectorBody.Children.Add(Ui.Scroll(inspector));
        body.Children.Add(Ui.Card(inspectorBody, 20, "SurfaceLow", 16).At(0, 4));
        Layout(header, body);
        ShowInspectorHint("Select a stage or resource to inspect it.");
        // A finished build (or the image size read after it) redraws the stage footers.
        CanvasBuildFooters.Changed += () => Dispatcher.BeginInvoke(() => { if (view == View.Dockerfile && graph != null) DrawDockerfile(); });
    }

    public override async Task RefreshAsync()
    {
        if (view == View.Dockerfile) DrawDockerfile();
        else await DrawRuntimeAsync();
    }

    public override void OnShown()
    {
        // The Build editor may have changed while Canvas was hidden; re-parse when the text differs.
        if (view == View.Dockerfile && CurrentSource() is { } source && (graph == null || source != graphSource)) DrawDockerfile();
    }

    public override IEnumerable<AppCommand> PageCommands() =>
    [
        new() { Id = "canvas.fit", Title = "Canvas: fit graph", Group = "Canvas", Icon = Icons.FitScreen, Keywords = "zoom fit", Run = () => { Host.Navigate("Canvas"); surface.Fit(); } },
        new() { Id = "canvas.parse", Title = "Canvas: parse the Build Dockerfile", Group = "Canvas", Icon = Icons.AccountTree, Keywords = "stage graph", Run = () => { Host.Navigate("Canvas"); DrawDockerfile(); } },
    ];

    // ----------------------------------------------------------------- shared UI pieces

    private void SetEmpty(FrameworkElement? element)
    {
        if (emptyState != null) graphHost.Children.Remove(emptyState);
        emptyState = element == null ? null : Ui.Card(element, 24, "SurfaceLowest", 16);
        surface.Visibility = element == null ? Visibility.Visible : Visibility.Hidden;
        if (emptyState != null) graphHost.Children.Add(emptyState);
        UpdateMinimap();
    }

    private void ToggleMinimap()
    {
        minimapWanted = !minimapWanted;
        var name = Loc.T(minimapWanted ? "Hide minimap (M)" : "Show minimap (M)");
        minimapToggle.Content = minimapWanted ? Icons.VisibilityOff : Icons.Visibility;
        minimapToggle.ToolTip = name;
        System.Windows.Automation.AutomationProperties.SetName(minimapToggle, name);
        UpdateMinimap();
    }

    private void UpdateMinimap()
    {
        var roomy = graphHost.ActualWidth <= 0 || (graphHost.ActualWidth >= 420 && graphHost.ActualHeight >= 280);
        minimap.Visibility = minimapWanted && roomy && emptyState == null ? Visibility.Visible : Visibility.Collapsed;
        // The toggle only acts when there is room for the minimap and a graph to show.
        minimapToggle.IsEnabled = roomy && emptyState == null;
    }

    private void SetWarning(string? text)
    {
        warningText.Text = text ?? "";
        warning.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetObjects(IEnumerable<GraphObject> items, string note)
    {
        objectItems.Clear();
        objectItems.AddRange(items);
        objectsNote.Text = note;
        FillObjects();
    }

    private void FillObjects()
    {
        objects.Items.Clear();
        foreach (var o in objectItems)
        {
            if (!objectSearch.Matches(o.Label + " " + o.Meta)) continue;
            // Design: 44 px rows, icon, label (ellipsis), meta right-aligned in 12 px.
            var label = Ui.Text(o.Label, 14, o.Indent == 0 ? FontWeights.SemiBold : FontWeights.Normal, "OnSurface", false, false);
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            var content = Ui.Columns(10, [0, 1, 0], Ui.Icon(o.Icon, 18), label,
                o.Meta.Length == 0 ? new Border() : Ui.Text(o.Meta, 12, null, "OnSurfaceVariant", false, false));
            var item = new ListBoxItem { Content = content, Tag = o, Padding = new Thickness(10 + o.Indent * 18, 0, 10, 0), MinHeight = 44 };
            System.Windows.Automation.AutomationProperties.SetName(item, o.Label + (o.Meta.Length > 0 ? ", " + o.Meta : ""));
            objects.Items.Add(item);
            if (Equals(o.Key, selectedKey)) objects.SelectedItem = item;
        }
    }

    private void SyncSelection(object key)
    {
        selectedKey = key;
        surface.Highlight(key);
        foreach (ListBoxItem item in objects.Items)
            if (item.Tag is GraphObject o && Equals(o.Key, key) && o.Indent == 0) { objects.SelectedItem = item; objects.ScrollIntoView(item); break; }
    }

    private void ShowInspectorHint(string text)
    {
        inspector.Children.Clear();
        inspectorActions.Content = null;
        inspector.Children.Add(Ui.Text("Inspector", 16, FontWeights.SemiBold));
        inspector.Children.Add(Ui.Caption(text).Margin(0, 8, 0, 0));
    }

    private static FrameworkElement Section(string title, params UIElement?[] children)
    {
        var stack = Ui.VStack(6, children);
        return Ui.VStack(6, Ui.Overline(title), stack).Margin(0, 16, 0, 0);
    }

    /// <summary>Shows read-only JSON (inspect output) in an overlay with search and copy.</summary>
    private void ShowJson(string title, string json)
    {
        var view = new CodeEditor(true, 360);
        view.Text = json;
        var find = new TextFindBar(view.Box, "Search");
        Action? close = null;
        var copy = Ui.Button("Copy", Icons.ContentCopy, ButtonKind.Text, () => { try { ActionSounds.Copy(json); Host.Notify.Info("Copied."); } catch (System.Runtime.InteropServices.ExternalException) { Host.Notify.Warning("The clipboard is busy. Try again."); } });
        var done = Ui.Button("Close", null, ButtonKind.Filled, () => close!());
        var actions = Ui.HStack(8, copy, done); actions.HorizontalAlignment = HorizontalAlignment.Right;
        view.Height = 420;
        close = Host.Overlay.Show(Ui.VStack(12, Ui.Title(title), find, view, actions), 820);
    }
}
