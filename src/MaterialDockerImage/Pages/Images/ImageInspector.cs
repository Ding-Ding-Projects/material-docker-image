using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Right-hand details card for the focused image: inspect facts, layer history with its own search, and actions.</summary>
internal sealed class ImageInspector : Border
{
    private static AppHost Host => AppHost.Current;
    private readonly ImagesPage page;
    private readonly ContentControl content = new();
    private ImageRow? current;
    private string rawJson = "";
    private int loadVersion;

    public ImageInspector(ImagesPage page)
    {
        this.page = page;
        Width = 400;
        Margin = new Thickness(16, 0, 0, 0);
        CornerRadius = new CornerRadius(16);
        Padding = new Thickness(16);
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
            content.Content = Ui.EmptyState(Icons.Layers, "No image selected", "Select an image to see its platform, layers, labels, environment names and actions.");
            return;
        }
        content.Content = Ui.VStack(8, Header(image), Ui.Caption("Reading image details…"));
        _ = LoadAsync(image, version);
    }

    private UIElement Header(ImageRow image) => Ui.VStack(2,
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
        IReadOnlyList<JsonElement> layers;
        try { layers = history.Success ? DockerJson.ParseRows(history.StandardOutput) : []; }
        catch (JsonException) { layers = []; }

        var size = double.TryParse(PageKit.Field(info, "Size"), NumberStyles.Float, CultureInfo.InvariantCulture, out var bytes) ? Ui.FormatBytes(bytes) : image.Size;
        var platform = PageKit.Field(info, "Os") + "/" + PageKit.Field(info, "Architecture") + (PageKit.Field(info, "Variant") is { Length: > 0 } v ? "/" + v : "");
        var tags = PageKit.Strings(PageKit.Child(info, "RepoTags")).ToList();
        var digests = PageKit.Strings(PageKit.Child(info, "RepoDigests")).ToList();
        var env = PageKit.Strings(PageKit.Child(info, "Config", "Env")).Select(e => e.Split('=')[0]).ToList();
        var ports = PageKit.Child(info, "Config", "ExposedPorts") is { ValueKind: JsonValueKind.Object } p ? p.EnumerateObject().Select(x => x.Name).ToList() : [];
        var entry = PageKit.Strings(PageKit.Child(info, "Config", "Entrypoint")).ToList();
        var cmd = PageKit.Strings(PageKit.Child(info, "Config", "Cmd")).ToList();
        var labels = PageKit.Child(info, "Config", "Labels") is { ValueKind: JsonValueKind.Object } l ? l.EnumerateObject().Select(x => x.Name + "=" + (x.Value.ValueKind == JsonValueKind.String ? x.Value.GetString() : x.Value.ToString())).ToList() : [];

        var facts = Ui.VStack(6,
            PageKit.Fact("Size", size),
            PageKit.Fact("Created", PageKit.FormatTime(PageKit.Field(info, "Created"))),
            PageKit.Fact("Platform", platform, true),
            PageKit.Fact("Layers", PageKit.Child(info, "RootFS", "Layers") is { ValueKind: JsonValueKind.Array } rl ? rl.GetArrayLength().ToString(CultureInfo.InvariantCulture) : "—"),
            PageKit.Fact("User", PageKit.Field(info, "Config", "User") is { Length: > 0 } u ? u : "root (default)", true),
            PageKit.Fact("Working directory", PageKit.Field(info, "Config", "WorkingDir"), true),
            PageKit.Fact("Entrypoint", entry.Count > 0 ? JsonSerializer.Serialize(entry) : "", true),
            PageKit.Fact("Command", cmd.Count > 0 ? JsonSerializer.Serialize(cmd) : "", true),
            PageKit.Fact("Exposed ports", string.Join(", ", ports), true),
            PageKit.Fact("Containers", image.Usage));

        var historyView = new SearchableText("Layer history", PageKit.SafeFileName(image.Reference) + "-history.txt", "Search layers") { Height = 220, KeepFirstLine = true };
        historyView.Search.Width = 200;
        historyView.SetText(FormatHistory(layers));

        var stack = Ui.VStack(14,
            Header(image),
            Actions(image),
            facts,
            Section("Tags", tags.Count == 0 ? Ui.Caption("No tags (dangling or referenced by ID only).") : List(tags)),
            digests.Count > 0 ? Section("Repository digests", List(digests)) : null,
            Section("Layer history", layers.Count == 0 ? Ui.Caption(history.Success ? "No history entries." : "History could not be read: " + history.StandardError.Trim()) : historyView),
            Section("Labels", labels.Count == 0 ? Ui.Caption("No labels.") : List(labels)),
            Section("Environment names", env.Count == 0 ? Ui.Caption("No environment variables.") : Ui.VStack(4, Ui.Caption("Names only. Values are visible in Inspect raw JSON after you choose to show them."), List(env))));
        content.Content = Ui.Scroll(stack);
    }

    private static string FormatHistory(IReadOnlyList<JsonElement> layers)
    {
        var lines = new List<string> { "SIZE       CREATED              INSTRUCTION" };
        foreach (var layer in layers)
        {
            var by = DockerJson.Text(layer, "CreatedBy").Replace("\n", " ").Replace("\t", " ");
            by = by.Replace("/bin/sh -c #(nop) ", "", StringComparison.Ordinal).Trim();
            lines.Add($"{DockerJson.Text(layer, "Size"),-10} {DockerJson.Text(layer, "CreatedSince"),-20} {by}");
        }
        return string.Join("\n", lines);
    }

    private static UIElement Section(string title, UIElement body) => Ui.VStack(6, Ui.Overline(title), body);
    private static UIElement List(IEnumerable<string> items) => Ui.VStack(2, items.Select(i => (UIElement)Ui.Mono(i, 12, "OnSurface", true)).ToArray());

    private UIElement Actions(ImageRow image)
    {
        var run = Ui.Button("Run", Icons.PlayArrow, ButtonKind.Filled, () => page.OpenRun(image));
        var raw = Ui.Button("Inspect raw JSON", Icons.DataObject, ButtonKind.Tonal, () => ShowRaw(image));
        var more = Ui.Wrap(8,
            Ui.AsyncButton("Tag…", Icons.Sell, ButtonKind.Outlined, () => page.TagAsync(image)),
            Ui.AsyncButton("Push…", Icons.CloudUpload, ButtonKind.Outlined, () => page.PushAsync([image])),
            Ui.AsyncButton("Save archive…", Icons.Download, ButtonKind.Outlined, () => page.SaveAsync([image])),
            Ui.Button("Copy ID", Icons.ContentCopy, ButtonKind.Text, () => PageKit.Copy(image.Id, "the image ID")));
        return Ui.VStack(8, Ui.Wrap(8, run, raw), more);
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
