using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Overview tab: inspect facts, tags, digests, labels and environment variable names (never values).</summary>
internal sealed class ImageOverviewTab : ImageDetailTab
{
    private readonly ScrollViewer view;

    public ImageOverviewTab(ImageDetailContext context) : base(context)
    {
        var info = context.Info;
        var image = context.Image;
        var size = double.TryParse(PageKit.Field(info, "Size"), NumberStyles.Float, CultureInfo.InvariantCulture, out var bytes) ? Ui.FormatBytes(bytes) : image.Size;
        var tags = PageKit.Strings(PageKit.Child(info, "RepoTags")).ToList();
        var digests = context.RepoDigests;
        var env = PageKit.Strings(PageKit.Child(info, "Config", "Env")).Select(e => e.Split('=')[0]).ToList();
        var ports = PageKit.Child(info, "Config", "ExposedPorts") is { ValueKind: JsonValueKind.Object } p ? p.EnumerateObject().Select(x => x.Name).ToList() : [];
        var entry = PageKit.Strings(PageKit.Child(info, "Config", "Entrypoint")).ToList();
        var cmd = PageKit.Strings(PageKit.Child(info, "Config", "Cmd")).ToList();
        var labels = PageKit.Child(info, "Config", "Labels") is { ValueKind: JsonValueKind.Object } l ? l.EnumerateObject().Select(x => x.Name + "=" + (x.Value.ValueKind == JsonValueKind.String ? x.Value.GetString() : x.Value.ToString())).ToList() : [];

        var facts = Ui.Card(Ui.VStack(6,
            PageKit.Fact("Size", size),
            PageKit.Fact("Created", PageKit.FormatTime(PageKit.Field(info, "Created"))),
            PageKit.Fact("Platform", context.Platform, true),
            PageKit.Fact("Layers", PageKit.Child(info, "RootFS", "Layers") is { ValueKind: JsonValueKind.Array } rl ? rl.GetArrayLength().ToString(CultureInfo.InvariantCulture) : "—"),
            PageKit.Fact("User", PageKit.Field(info, "Config", "User") is { Length: > 0 } u ? u : "root (default)", true),
            PageKit.Fact("Working directory", PageKit.Field(info, "Config", "WorkingDir"), true),
            PageKit.Fact("Entrypoint", entry.Count > 0 ? JsonSerializer.Serialize(entry) : "", true),
            PageKit.Fact("Command", cmd.Count > 0 ? JsonSerializer.Serialize(cmd) : "", true),
            PageKit.Fact("Exposed ports", string.Join(", ", ports), true),
            PageKit.Fact("Containers", image.Usage)), 14, "SurfaceContainer", 12);

        var stack = Ui.VStack(14,
            facts,
            Section("Tags", tags.Count == 0 ? Ui.Caption("No tags (dangling or referenced by ID only).") : Chips(tags)),
            digests.Count > 0 ? Section("Repository digests", List(digests)) : null,
            Section("Labels", labels.Count == 0 ? Ui.Caption("No labels.") : List(labels)),
            Section("Environment names", env.Count == 0 ? Ui.Caption("No environment variables.") : Ui.VStack(4, Ui.Caption("Names only. Values are visible in Inspect raw JSON after you choose to show them."), Chips(env))));
        view = Ui.Scroll(stack);
    }

    public override string Title => "Overview";
    public override FrameworkElement View => view;

    private static UIElement Section(string title, UIElement body) => Ui.VStack(6, Ui.Overline(title), body);
    private static UIElement List(IEnumerable<string> items) => Ui.VStack(2, items.Select(i => (UIElement)Ui.Mono(i, 12, "OnSurface", true)).ToArray());

    private static UIElement Chips(IEnumerable<string> items)
    {
        var wrap = new WrapPanel();
        foreach (var item in items)
        {
            var badge = Ui.Badge(item, Tone.Neutral);
            badge.Margin = new Thickness(0, 0, 6, 6);
            if (badge.Child is TextBlock t) t.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            wrap.Children.Add(badge);
        }
        return wrap;
    }
}
