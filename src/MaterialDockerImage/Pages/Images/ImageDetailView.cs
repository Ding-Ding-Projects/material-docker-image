using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Full-page detail of one image (design/workspace-redesign-2026-11/project/ImagesV2.dc.html): a header with the
/// breadcrumb back to the list, the reference, digest, size, layer count and age, and the image actions; then tabs for
/// Layers (with the Platforms and Scout summary column beside it on wide windows), Platforms, Vulnerabilities, SBOM,
/// Compare and Overview. Every value comes from docker image inspect, docker image history, the registry manifest or a
/// Docker Scout analysis from this session.
/// </summary>
internal sealed class ImageDetailView : DockPanel
{
    private static AppHost Host => AppHost.Current;
    private const double SideWidth = 380;
    private const double SideBreakpoint = 1100;
    private readonly ImagesPage page;
    private readonly ImageRow image;
    private readonly Action back;
    private readonly TextBlock meta;
    private readonly ContentControl body = new();
    private readonly Dictionary<ImageDetailTab, TextBlock> counts = [];
    private string rawJson = "";
    private TabControl? tabs;

    public ImageDetailView(ImagesPage page, ImageRow image, Action back)
    {
        this.page = page;
        this.image = image;
        this.back = back;
        LastChildFill = true;
        System.Windows.Automation.AutomationProperties.SetName(this, Loc.T("Image detail"));

        meta = Ui.Mono(MetaLine(null), 12, "OnSurfaceVariant");
        meta.TextTrimming = TextTrimming.CharacterEllipsis;
        meta.ToolTip = image.Id;
        Children.Add(Header().Dock(System.Windows.Controls.Dock.Top));
        body.Content = Ui.HStack(10, new ProgressBar { IsIndeterminate = true, Height = 4, Width = 160, VerticalAlignment = VerticalAlignment.Center }, Ui.Caption("Reading image details…")).Margin(4, 16, 0, 0);
        Children.Add(body);

        KeyDown += (_, e) =>
        {
            if (e.Handled) return;
            if (e.Key == Key.Escape || e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt || e.SystemKey == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt) { back(); e.Handled = true; }
        };
        Loaded += (_, _) => _ = LoadAsync();
    }

    private FrameworkElement Header()
    {
        var tile = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(14), VerticalAlignment = VerticalAlignment.Center };
        tile.SetResourceReference(Border.BackgroundProperty, "PrimaryContainer");
        var glyph = Ui.Icon(Icons.Layers, 24, true, "OnPrimaryContainer");
        glyph.HorizontalAlignment = HorizontalAlignment.Center; glyph.VerticalAlignment = VerticalAlignment.Center;
        tile.Child = glyph;

        var link = new Hyperlink(new Run(Loc.T("Images"))) { Focusable = true };
        link.Click += (_, _) => back();
        System.Windows.Automation.AutomationProperties.SetName(link, Loc.T("Back to Images"));
        var crumb = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
        crumb.SetResourceReference(TextBlock.ForegroundProperty, "OnSurfaceVariant");
        crumb.Inlines.Add(link);
        crumb.Inlines.Add(new Run(" / " + (image.Repository != "<none>" ? image.Repository : image.ShortId)));
        System.Windows.Automation.AutomationProperties.SetName(crumb, Loc.T("Breadcrumb"));

        var title = Ui.Text(image.Tagged ? image.Name : image.Repository != "<none>" ? image.Name : image.ShortId, 22, FontWeights.SemiBold, "OnSurface", false, false);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.ToolTip = image.Name;
        var text = Ui.VStack(2, crumb, title, meta);
        text.VerticalAlignment = VerticalAlignment.Center;

        var copy = Ui.IconButton(Icons.ContentCopy, "Copy image digest", () => PageKit.Copy(image.Id, "the image digest"), "OnSurface");
        var copyHost = new Border { CornerRadius = new CornerRadius(20), Child = copy, VerticalAlignment = VerticalAlignment.Center };
        copyHost.SetResourceReference(Border.BackgroundProperty, "SurfaceHigh");
        var export = Ui.AsyncButton("Export", Icons.Download, ButtonKind.Outlined, () => page.SaveAsync([image]));
        var push = Ui.AsyncButton("Push", Icons.CloudUpload, ButtonKind.Tonal, () => page.PushAsync([image]));
        var run = Ui.Button("Run", Icons.PlayArrow, ButtonKind.Filled, () => page.OpenRun(image));
        var more = Ui.IconButton(Icons.MoreVert, "More image actions");
        more.Click += (_, _) => MoreMenu(more).IsOpen = true;
        var actions = Ui.HStack(8, copyHost, export, push, run, more);
        actions.VerticalAlignment = VerticalAlignment.Center;

        var grid = Ui.Columns(14, [0, 1, 0], tile, text, actions);
        grid.Margin = new Thickness(0, 0, 0, 8);
        // Narrow windows keep the reference readable: the secondary actions move into More below 820 px.
        grid.SizeChanged += (_, e) =>
        {
            var narrow = e.NewSize.Width < 820;
            export.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
            push.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
            copyHost.Visibility = e.NewSize.Width < 620 ? Visibility.Collapsed : Visibility.Visible;
        };
        return grid;
    }

    private ContextMenu MoreMenu(FrameworkElement anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        menu.Items.Add(Ui.MenuItem("Export", Icons.Download, () => _ = Guard(() => page.SaveAsync([image]))));
        menu.Items.Add(Ui.MenuItem("Push…", Icons.CloudUpload, () => _ = Guard(() => page.PushAsync([image]))));
        menu.Items.Add(Ui.MenuItem("Tag…", Icons.Sell, () => _ = Guard(() => page.TagAsync(image))));
        menu.Items.Add(Ui.MenuItem("Retag to multiple…", Icons.Tag, () => _ = Guard(() => page.RetagManyAsync(image))));
        menu.Items.Add(Ui.MenuItem("Push all tags of repository…", Icons.CloudSync, () => _ = Guard(() => page.PushRepositoryAsync(image))));
        menu.Items.Add(new Separator());
        menu.Items.Add(Ui.MenuItem("Inspect raw JSON", Icons.DataObject, () => ImageInspector.ShowRaw(image, rawJson)));
        menu.Items.Add(Ui.MenuItem("Copy pull command", Icons.ContentCopy, () => page.CopyPullCommand(image)));
        menu.Items.Add(Ui.MenuItem("Copy image ID", Icons.ContentCopy, () => PageKit.Copy(image.Id, "the image ID")));
        menu.Items.Add(new Separator());
        menu.Items.Add(Ui.MenuItem("Remove…", Icons.Delete, () => _ = Guard(RemoveAsync), null, true));
        return menu;
    }

    private async Task RemoveAsync()
    {
        await page.RemoveOneAsync(image);
        if (!page.AllImages.Any(i => i.Key == image.Key)) back();
    }

    private static async Task Guard(Func<Task> action)
    {
        try { await action(); }
        catch (Exception e) when (e is not OutOfMemoryException) { Host.Notify.Error(e.Message); }
    }

    /// <summary>sha256:7f3c91a0e2b4c6d8…8a4fd81e · 182MB · 9 layers · created 4 minutes ago</summary>
    private string MetaLine(int? layerCount)
    {
        var id = image.Id;
        var colon = id.IndexOf(':');
        var hex = colon > 0 ? id[(colon + 1)..] : id;
        var digest = hex.Length > 28 ? (colon > 0 ? id[..(colon + 1)] : "") + hex[..16] + "…" + hex[^8..] : id;
        var parts = new List<string> { digest };
        if (image.Size.Length > 0) parts.Add(image.Size);
        if (layerCount is { } n) parts.Add(n.ToString(CultureInfo.CurrentCulture) + " " + Loc.T(n == 1 ? "layer" : "layers"));
        if (image.CreatedSince.Length > 0) parts.Add(Loc.T("created") + " " + image.CreatedSince);
        return string.Join(" · ", parts);
    }

    private async Task LoadAsync()
    {
        if (tabs != null) return;
        var context = page.Context;
        CommandResult inspect, history;
        try
        {
            // The row's ID can be gone when the tag was rebuilt since the list loaded; the tag then names the new image.
            var target = image.Id;
            inspect = await Host.Read(["image", "inspect", target], context);
            if (!inspect.Success && image.Tagged)
            {
                var byTag = await Host.Read(["image", "inspect", image.Reference], context);
                if (byTag.Success) { inspect = byTag; target = image.Reference; _ = page.SafeRefresh(); }
            }
            history = await Host.Read(["image", "history", "--no-trunc", "--format", "{{json .}}", target], context);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException)
        {
            body.Content = Problem(Loc.T("Details could not be read:") + " " + e.Message);
            return;
        }
        if (!inspect.Success || PageKit.FirstInspect(inspect.StandardOutput) is not { } info)
        {
            body.Content = Problem(Loc.T("docker image inspect failed:") + " " + inspect.StandardError.Trim());
            return;
        }
        rawJson = inspect.StandardOutput;
        meta.Text = MetaLine(PageKit.Strings(PageKit.Child(info, "RootFS", "Layers")).Count());
        var ctx = new ImageDetailContext(page, image, context, info, inspect.StandardOutput, history);

        var layers = new ImageLayersTab(ctx);
        var platforms = new ImagePlatformsTab(ctx);
        var scout = new ImageScoutTab(ctx);
        var sbom = new ImageSbomTab(ctx);
        var compare = new ImageCompareTab(ctx);
        var overview = new ImageOverviewTab(ctx);
        var control = new TabControl();
        System.Windows.Automation.AutomationProperties.SetName(control, Loc.T("Image detail sections"));
        var side = new ImageSummaryColumn(ctx, () => control.SelectedIndex = 2);
        side.PlatformsCounted += n => SetCount(platforms, n);

        AddTab(control, layers, Icons.Layers, LayersBody(layers, side));
        AddTab(control, platforms, Icons.DeveloperBoard, Panel(platforms));
        AddTab(control, scout, Icons.Security, Panel(scout));
        AddTab(control, sbom, Icons.ListAlt, Panel(sbom));
        AddTab(control, compare, Icons.CompareArrows, Panel(compare));
        AddTab(control, overview, Icons.Info, Panel(overview));
        SetCount(layers, layers.Count);
        SetScoutCount(scout, ScoutSessionCache.Get(context, image.Id));
        void OnScout(ScoutScanResult r) { if (r.ImageId == image.Id && r.Context == context) SetScoutCount(scout, r); }
        ScoutSessionCache.Updated += OnScout;
        Unloaded += (_, _) => ScoutSessionCache.Updated -= OnScout;
        Loaded += (_, _) => { ScoutSessionCache.Updated -= OnScout; ScoutSessionCache.Updated += OnScout; SetScoutCount(scout, ScoutSessionCache.Get(context, image.Id)); };

        control.SelectionChanged += (_, e) =>
        {
            if (e.OriginalSource != control) return;
            if (control.SelectedItem is TabItem { Tag: ImageDetailTab t }) t.Activate();
        };
        control.SelectedIndex = 0;
        layers.Activate();
        tabs = control;
        body.Content = control;
        _ = GuardLoad(side.LoadAsync);
    }

    private static async Task GuardLoad(Func<Task> load)
    {
        try { await load(); }
        catch (Exception e) when (e is not OutOfMemoryException) { Host.Notify.Error(e.Message); }
    }

    private void SetScoutCount(ImageDetailTab tab, ScoutScanResult? result) =>
        SetCount(tab, result is { Succeeded: true } r ? r.Counts.Total : null);

    private void SetCount(ImageDetailTab tab, int? count)
    {
        if (!counts.TryGetValue(tab, out var text)) return;
        text.Text = count is { } n ? n.ToString("N0", CultureInfo.CurrentCulture) : "";
        text.Visibility = count == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void AddTab(TabControl control, ImageDetailTab tab, string icon, UIElement content)
    {
        var glyph = Ui.Icon(icon, 18);
        glyph.ClearValue(TextBlock.ForegroundProperty);
        glyph.VerticalAlignment = VerticalAlignment.Center;
        var label = new TextBlock { Text = Loc.T(tab.Title), VerticalAlignment = VerticalAlignment.Center };
        var count = new TextBlock { FontSize = 12, FontWeight = FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        count.SetResourceReference(TextBlock.ForegroundProperty, "OnSurfaceVariant");
        counts[tab] = count;
        var header = Ui.HStack(8, glyph, label, count);
        var item = new TabItem { Header = header, Content = content, Tag = tab };
        System.Windows.Automation.AutomationProperties.SetName(item, Loc.T(tab.Title));
        control.Items.Add(item);
    }

    private static Border Panel(ImageDetailTab tab) => Ui.Card(tab.View, 16, "SurfaceLow", 16);

    /// <summary>The layers card with the summary column beside it from 1100 px; narrower windows give the table the full width.</summary>
    private static Grid LayersBody(ImageLayersTab layers, ImageSummaryColumn side)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SideWidth + 12) });
        var card = Ui.Card(layers.View, 12, "SurfaceLow", 16);
        System.Windows.Automation.AutomationProperties.SetName(card, Loc.T("Layers"));
        grid.Children.Add(card.At(0, 0));
        var scroll = Ui.Scroll(side);
        scroll.Margin = new Thickness(12, 0, 0, 0);
        grid.Children.Add(scroll.At(0, 1));
        grid.SizeChanged += (_, e) =>
        {
            var wide = e.NewSize.Width >= SideBreakpoint;
            scroll.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
            grid.ColumnDefinitions[1].Width = new GridLength(wide ? SideWidth + 12 : 0);
        };
        return grid;
    }

    private UIElement Problem(string message) => Ui.Card(Ui.VStack(10,
        Ui.HStack(8, Ui.Icon(Icons.Error, 20, true, "OnErrorContainer"), Ui.Text(message, 13, null, "OnErrorContainer", true, false)),
        Ui.Button("Back to Images", Icons.ArrowBack, ButtonKind.Text, back)), 16, "ErrorContainer", 16).Margin(0, 8, 0, 0);
}
