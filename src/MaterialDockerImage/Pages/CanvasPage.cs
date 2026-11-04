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
    private readonly TextBlock objectsNote = Ui.Caption("");
    private readonly TextBlock zoomText = Ui.Text("100%", 13, null, "OnSurfaceVariant", false, false);
    private readonly TextBlock subtitle;
    private readonly Border warning;
    private readonly TextBlock warningText = Ui.Text("", 13, null, "OnWarningContainer");
    private readonly Grid graphHost = new();
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
        var zoomBar = Ui.HStack(0, zoomOut, zoomText, zoomIn, fit, reset);
        zoomBar.HorizontalAlignment = HorizontalAlignment.Right;
        var toolbar = Ui.Columns(8, [1, 0], segmented, zoomBar);

        warning = Ui.Card(Ui.Columns(10, [0, 1], Ui.Icon(Icons.Warning, 20, true, "OnWarningContainer"), warningText), 12, "WarningContainer", 12);
        warning.Visibility = Visibility.Collapsed;
        graphHost.Children.Add(surface);
        var centre = new DockPanel();
        centre.Children.Add(toolbar.Dock(System.Windows.Controls.Dock.Top));
        warning.Margin = new Thickness(0, 10, 0, 0);
        centre.Children.Add(warning.Dock(System.Windows.Controls.Dock.Top));
        graphHost.Margin = new Thickness(0, 12, 0, 0);
        centre.Children.Add(graphHost);

        objectSearch.Width = double.NaN;
        objectSearch.Changed += FillObjects;
        System.Windows.Automation.AutomationProperties.SetName(objects, Loc.T("Objects"));
        ScrollViewer.SetHorizontalScrollBarVisibility(objects, ScrollBarVisibility.Disabled);
        objects.SelectionChanged += (_, _) => { if (objects.SelectedItem is ListBoxItem { Tag: GraphObject o } && !Equals(o.Key, selectedKey)) o.Select(); };
        objects.MouseDoubleClick += (_, _) => { if (objects.SelectedItem is ListBoxItem { Tag: GraphObject o }) o.Activate?.Invoke(); };
        objects.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter && objects.SelectedItem is ListBoxItem { Tag: GraphObject o }) { (o.Activate ?? o.Select)(); e.Handled = true; } };
        var left = new DockPanel();
        left.Children.Add(Ui.Text("Objects", 16, FontWeights.SemiBold).Dock(System.Windows.Controls.Dock.Top));
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
        body.Children.Add(Ui.Card(Ui.Scroll(inspector), 20, "SurfaceLow", 16).At(0, 4));
        Layout(header, body);
        ShowInspectorHint("Select a stage or resource to inspect it.");
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
            var content = Ui.Columns(8, [0, 1], Ui.Icon(o.Icon, 18),
                Ui.VStack(0, Ui.Text(o.Label, 13, o.Indent == 0 ? FontWeights.SemiBold : FontWeights.Normal, "OnSurface", false, false),
                    o.Meta.Length == 0 ? null : Ui.Text(o.Meta, 12, null, "OnSurfaceVariant", false, false)));
            var item = new ListBoxItem { Content = content, Tag = o, Padding = new Thickness(10 + o.Indent * 18, 6, 10, 6), MinHeight = 32 };
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
