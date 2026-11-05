using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Everything a detail tab needs about the focused image, read once per selection.</summary>
internal sealed record ImageDetailContext(ImagesPage Page, ImageRow Image, string? Context, JsonElement Info, string RawJson, CommandResult History)
{
    public IReadOnlyList<string> RepoDigests => PageKit.Strings(PageKit.Child(Info, "RepoDigests")).ToList();
    public string Platform => PageKit.Field(Info, "Os") + "/" + PageKit.Field(Info, "Architecture") + (PageKit.Field(Info, "Variant") is { Length: > 0 } v ? "/" + v : "");
}

/// <summary>A lazily loaded tab of the image detail. Loading starts the first time the tab is shown.</summary>
internal abstract class ImageDetailTab
{
    protected static AppHost Host => AppHost.Current;
    private bool started;
    protected ImageDetailTab(ImageDetailContext context) { Ctx = context; }
    protected ImageDetailContext Ctx { get; }
    public abstract string Title { get; }
    public abstract FrameworkElement View { get; }

    public void Activate()
    {
        if (started) return;
        started = true;
        _ = Guarded();
    }

    private async Task Guarded()
    {
        try { await OnActivateAsync(); }
        catch (Exception e) when (e is not OutOfMemoryException) { Host.Notify.Error(Title + ": " + e.Message); }
    }

    protected virtual Task OnActivateAsync() => Task.CompletedTask;

    protected bool EnsureCurrent() => Ctx.Page.EnsureCurrent();

    /// <summary>A small indeterminate progress line for background reads.</summary>
    protected static UIElement Loading(string text)
    {
        var bar = new ProgressBar { IsIndeterminate = true, Height = 4, Width = 160, VerticalAlignment = VerticalAlignment.Center };
        return Ui.HStack(10, bar, Ui.Caption(text)).Margin(0, 8, 0, 8);
    }
}

/// <summary>
/// Right-hand detail for the focused image: header and actions, then tabs for Overview, Layers, Platforms,
/// Vulnerabilities, SBOM and Compare. Tab content lives in separate files.
/// </summary>
internal sealed class ImageInspector : Border
{
    private static AppHost Host => AppHost.Current;
    private readonly ImagesPage page;
    private readonly ContentControl content = new();
    private ImageRow? current;
    private string rawJson = "";
    private int loadVersion;
    private int selectedTab;

    public ImageInspector(ImagesPage page)
    {
        this.page = page;
        Width = 520;
        Margin = new Thickness(16, 0, 0, 0);
        CornerRadius = new CornerRadius(16);
        Padding = new Thickness(20);
        SetResourceReference(BackgroundProperty, "SurfaceLow");
        Child = content;
        System.Windows.Automation.AutomationProperties.SetName(this, Loc.T("Selected image"));
        Show(null);
    }

    public void Show(ImageRow? image)
    {
        if (image != null && image == current) return;
        current = image;
        var version = ++loadVersion;
        if (image == null)
        {
            rawJson = "";
            content.Content = Ui.EmptyState(Icons.Layers, "No image selected", "Select an image to see its overview, layers, platforms, vulnerabilities, SBOM and comparisons.");
            return;
        }
        content.Content = Ui.VStack(8, Header(image), Ui.Caption("Reading image details…"));
        _ = LoadAsync(image, version);
    }

    private static UIElement Header(ImageRow image) => Ui.VStack(2,
        Ui.Overline("Image"),
        Ui.Text(image.Name, 18, FontWeights.SemiBold, "OnSurface", true, false),
        Ui.Mono(image.Id, 11.5, "OnSurfaceVariant", true));

    private async Task LoadAsync(ImageRow image, int version)
    {
        var context = page.Context;
        CommandResult inspect, history;
        try
        {
            inspect = await Host.Read(["image", "inspect", image.Id], context);
            history = await Host.Read(["image", "history", "--no-trunc", "--format", "{{json .}}", image.Id], context);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException)
        {
            if (version == loadVersion) content.Content = Ui.VStack(8, Header(image), Ui.Text("Details could not be read: " + e.Message, 13, null, "Error", true, false));
            return;
        }
        if (version != loadVersion) return;
        if (!inspect.Success || PageKit.FirstInspect(inspect.StandardOutput) is not { } info)
        {
            content.Content = Ui.VStack(8, Header(image), Ui.Text("docker image inspect failed: " + inspect.StandardError.Trim(), 13, null, "Error", true, false), Actions(image));
            return;
        }
        rawJson = inspect.StandardOutput;
        var ctx = new ImageDetailContext(page, image, context, info, inspect.StandardOutput, history);
        ImageDetailTab[] tabs =
        [
            new ImageOverviewTab(ctx),
            new ImageLayersTab(ctx),
            new ImagePlatformsTab(ctx),
            new ImageScoutTab(ctx),
            new ImageSbomTab(ctx),
            new ImageCompareTab(ctx),
        ];
        var control = new TabControl();
        System.Windows.Automation.AutomationProperties.SetName(control, Loc.T("Image detail sections"));
        foreach (var tab in tabs) control.Items.Add(new TabItem { Header = Loc.T(tab.Title), Content = tab.View, Tag = tab });
        control.SelectionChanged += (_, e) =>
        {
            if (e.OriginalSource != control) return;
            if (control.SelectedItem is TabItem { Tag: ImageDetailTab t }) { selectedTab = control.SelectedIndex; t.Activate(); }
        };
        control.SelectedIndex = Math.Clamp(selectedTab, 0, tabs.Length - 1);
        tabs[control.SelectedIndex].Activate();

        var layout = new DockPanel { LastChildFill = true };
        layout.Children.Add(Header(image).Dock(System.Windows.Controls.Dock.Top));
        // Actions sit at the bottom of the card, as in the design; the tabs take the space between.
        layout.Children.Add(((FrameworkElement)Actions(image)).Margin(0, 10, 0, 0).Dock(System.Windows.Controls.Dock.Bottom));
        control.Margin = new Thickness(0, 12, 0, 0);
        layout.Children.Add(control);
        content.Content = layout;
    }

    private UIElement Actions(ImageRow image)
    {
        var run = Ui.Button("Run", Icons.PlayArrow, ButtonKind.Filled, () => page.OpenRun(image));
        var raw = Ui.Button("Inspect raw JSON", Icons.DataObject, ButtonKind.Tonal, () => ShowRaw(image));
        var more = Ui.Button("More", Icons.MoreHoriz, ButtonKind.Text);
        more.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = more, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            menu.Items.Add(Ui.MenuItem("Tag…", Icons.Sell, () => _ = Guard(() => page.TagAsync(image))));
            menu.Items.Add(Ui.MenuItem("Retag to multiple…", Icons.Tag, () => _ = Guard(() => page.RetagManyAsync(image))));
            menu.Items.Add(Ui.MenuItem("Push…", Icons.CloudUpload, () => _ = Guard(() => page.PushAsync([image]))));
            menu.Items.Add(Ui.MenuItem("Push all tags of repository…", Icons.CloudSync, () => _ = Guard(() => page.PushRepositoryAsync(image))));
            menu.Items.Add(Ui.MenuItem("Save archive…", Icons.Download, () => _ = Guard(() => page.SaveAsync([image]))));
            menu.Items.Add(Ui.MenuItem("Copy pull command", Icons.ContentCopy, () => page.CopyPullCommand(image)));
            menu.Items.Add(Ui.MenuItem("Copy image ID", Icons.ContentCopy, () => PageKit.Copy(image.Id, "the image ID")));
            menu.IsOpen = true;
        };
        return Ui.Wrap(8, run, raw,
            Ui.AsyncButton("Tag…", Icons.Sell, ButtonKind.Outlined, () => page.TagAsync(image)),
            Ui.AsyncButton("Push…", Icons.CloudUpload, ButtonKind.Outlined, () => page.PushAsync([image])),
            more);
    }

    private static async Task Guard(Func<Task> action)
    {
        try { await action(); }
        catch (Exception e) when (e is not OutOfMemoryException) { Host.Notify.Error(e.Message); }
    }

    private void ShowRaw(ImageRow image)
    {
        if (rawJson.Length == 0) { Host.Notify.Warning("The image details have not loaded yet."); return; }
        var masked = PageKit.FormatInspect(rawJson, true) ?? rawJson;
        var full = PageKit.FormatInspect(rawJson, false) ?? rawJson;
        SearchableText? view = null;
        var reveal = Ui.Switch("Show environment values", false, on => view?.SetText(on ? full : masked));
        PageKit.ShowText("Inspect " + image.Name, "docker image inspect output. Environment values are masked until you choose to show them.", masked, PageKit.SafeFileName(image.Reference) + "-inspect.json", reveal, v => view = v);
    }
}
