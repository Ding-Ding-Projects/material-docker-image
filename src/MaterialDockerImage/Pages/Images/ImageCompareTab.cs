using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Compare tab: pick another local image and compare size, layers, platform, configuration, environment variable
/// names, exposed ports and labels side by side. Docker Scout compare output is available when Scout is installed.
/// </summary>
internal sealed class ImageCompareTab : ImageDetailTab
{
    private readonly ScrollViewer view;
    private readonly ContentControl result = new();
    private readonly ContentControl scoutHost = new();
    private readonly SearchablePicker? picker;
    private IReadOnlyList<ImageDiffRow> diff = [];
    private ImageCompareFacts? left, right;
    private bool onlyDifferences = true;
    private readonly SearchFilter search = new("Search differences", 220);
    private readonly StackPanel rows = new();
    private readonly TextBlock rowStatus = Ui.Caption("");
    private readonly ContentControl summaryHost = new();
    private readonly ContentControl sidesHost = new();
    private readonly StackPanel compareBody;

    public ImageCompareTab(ImageDetailContext context) : base(context)
    {
        var others = context.Page.AllImages.Where(i => i.Id != context.Image.Id).Select(i => i.Reference).Distinct(StringComparer.Ordinal).OrderBy(r => r, StringComparer.Ordinal).ToList();
        UIElement chooser;
        if (others.Count == 0)
        {
            chooser = Ui.Card(Ui.Text("There is no other local image to compare with. Pull or build another image first.", 13, null, "OnSurfaceVariant"), 14, "SurfaceContainer", 12);
        }
        else
        {
            picker = new SearchablePicker("Local images", others);
            var compare = Ui.AsyncButton("Compare", Icons.CompareArrows, ButtonKind.Filled, CompareAsync);
            var scout = Ui.AsyncButton("Docker Scout compare", Icons.Security, ButtonKind.Tonal, ScoutCompareAsync);
            chooser = Ui.VStack(8,
                Ui.Field("Compare with", picker, "Another local image. Images with the same ID as this one are left out because they are identical.", "nginx:1.26 compared with nginx:1.27"),
                Ui.Wrap(8, compare, scout, Ui.InfoButton("Docker Scout compare",
                    "Runs docker scout compare --to <other> <this image>, which reports package and vulnerability differences. It needs Docker Scout and can take a minute.",
                    "docker scout compare --to nginx:1.26 nginx:1.27")));
        }
        search.Changed += RenderRows;
        var toggle = Ui.Switch("Only differences", onlyDifferences, on => { onlyDifferences = on; RenderRows(); });
        compareBody = Ui.VStack(10, summaryHost, sidesHost, Ui.Wrap(10, search, toggle, rowStatus), rows);
        view = Ui.Scroll(Ui.VStack(14, chooser, result, scoutHost));
    }

    public override string Title => "Compare";
    public override FrameworkElement View => view;

    private async Task CompareAsync()
    {
        if (picker?.Selected is not { } other) { Host.Notify.Warning("Choose an image to compare with."); return; }
        if (!EnsureCurrent()) return;
        result.Content = Loading("Inspecting " + other + "…");
        var inspect = await Host.Read(["image", "inspect", other], Ctx.Context);
        if (!inspect.Success) { result.Content = Ui.Text("docker image inspect " + other + " failed: " + inspect.StandardError.Trim(), 13, null, "Error", true, false); return; }
        try
        {
            left = ImageCompare.FromInspect(Ctx.RawJson, Ctx.Image.Reference);
            right = ImageCompare.FromInspect(inspect.StandardOutput, other);
        }
        catch (Exception e) when (e is JsonException or FormatException) { result.Content = Ui.Text("The image details could not be parsed: " + e.Message, 13, null, "Error", true, false); return; }
        diff = ImageCompare.Diff(left, right);
        result.Content = Render(left, right);
    }

    private UIElement Render(ImageCompareFacts a, ImageCompareFacts b)
    {
        var changed = diff.Count(d => d.Kind != ImageDiffKind.Same);
        summaryHost.Content = Ui.Badge(changed == 0 ? "No differences in the compared fields" : $"{changed} differences", changed == 0 ? Tone.Success : Tone.Warning, true);
        sidesHost.Content = Ui.Columns(8, [1, 1], Side("This image", a, b), Side("Other image", b, a));
        RenderRows();
        return compareBody;
    }

    /// <summary>A summary card for one side with size, layers and platform; values that differ are marked.</summary>
    private static UIElement Side(string title, ImageCompareFacts self, ImageCompareFacts other)
    {
        UIElement Line(string label, string value, bool differs) => Ui.Columns(6, [-70, 1], Ui.Caption(label),
            Ui.Text(value, 13.5, differs ? FontWeights.SemiBold : FontWeights.Normal, differs ? "Primary" : "OnSurface", true, false));
        var name = Ui.Mono(self.Reference, 12.5, "OnSurface", true);
        name.FontWeight = FontWeights.SemiBold;
        return Ui.Card(Ui.VStack(6,
            Ui.Overline(title), name,
            Line("Size", Ui.FormatBytes(self.Size), self.Size != other.Size),
            Line("Layers", self.Layers.ToString(CultureInfo.InvariantCulture), self.Layers != other.Layers),
            Line("Platform", self.Platform, self.Platform != other.Platform),
            Line("Env names", self.EnvNames.Count.ToString(CultureInfo.InvariantCulture), !self.EnvNames.SequenceEqual(other.EnvNames)),
            Line("Ports", self.Ports.Count == 0 ? "—" : string.Join(", ", self.Ports), !self.Ports.SequenceEqual(other.Ports)),
            Line("Labels", self.Labels.Count.ToString(CultureInfo.InvariantCulture), !self.Labels.OrderBy(x => x.Key).SequenceEqual(other.Labels.OrderBy(x => x.Key)))), 12, "SurfaceContainer", 12);
    }

    private void RenderRows()
    {
        rows.Children.Clear();
        var shown = 0;
        string? section = null;
        foreach (var d in diff)
        {
            if (onlyDifferences && d.Kind == ImageDiffKind.Same) continue;
            if (search.IsActive && !search.Matches(d.Section + " " + d.Name + " " + d.Left + " " + d.Right)) continue;
            if (d.Section != section) { section = d.Section; rows.Children.Add(Ui.Overline(section).Margin(0, shown == 0 ? 0 : 8, 0, 4)); }
            rows.Children.Add(Row(d));
            shown++;
        }
        if (shown == 0) rows.Children.Add(Ui.Caption(search.Status.Length > 0 ? search.Status : onlyDifferences ? "No differences match." : "No rows match."));
        rowStatus.Text = search.Status.Length > 0 ? search.Status : $"{shown} of {diff.Count} rows";
    }

    private static UIElement Row(ImageDiffRow d)
    {
        var (label, tone) = d.Kind switch
        {
            ImageDiffKind.Changed => ("Changed", Tone.Warning),
            ImageDiffKind.OnlyLeft => ("Only in this", Tone.Primary),
            ImageDiffKind.OnlyRight => ("Only in other", Tone.Tertiary),
            _ => ("Same", Tone.Neutral)
        };
        string Show(string v) => d.Section == "Summary" && d.Name == "Size" && long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Ui.FormatBytes(n)
            : d.Section == "Summary" && d.Name == "Created" ? PageKit.FormatTime(v) : v.Length == 0 ? "—" : v;
        var name = Ui.Mono(d.Name, 12, "OnSurface", true);
        var values = d.Section is "Environment names" or "Exposed ports"
            ? (UIElement)Ui.Caption(d.Kind == ImageDiffKind.Same ? "In both images" : d.Kind == ImageDiffKind.OnlyLeft ? "Only in this image" : "Only in the other image")
            : Ui.Columns(6, [1, 0, 1], Ui.Mono(Show(d.Left), 12, "OnSurface", true), Ui.Icon(Icons.ArrowForward, 14, false, "OnSurfaceVariant"), Ui.Mono(Show(d.Right), 12, "OnSurface", true));
        var grid = Ui.Columns(8, [1, 0], Ui.VStack(3, name, values), Ui.Badge(label, tone, true));
        var card = new Border { Child = grid, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 0, 4) };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceContainer");
        return card;
    }

    private async Task ScoutCompareAsync()
    {
        if (picker?.Selected is not { } other) { Host.Notify.Warning("Choose an image to compare with."); return; }
        if (!EnsureCurrent()) return;
        var availability = await ScoutRunner.CheckAsync(Ctx.Context);
        if (!availability.Available) { scoutHost.Content = ScoutSeverityUi.StateCard(ScoutState.PluginMissing, availability.Output, availability.Command, ScoutSeverityUi.InstallGuideButton()); return; }
        var reference = Ctx.Image.Reference;
        var run = await Host.Execute($"Docker Scout compare {reference} to {other}", ["scout", "compare", "--to", other, reference], true, Ctx.Context);
        if (run == null) return;
        var output = ScoutReport.StripAnsi(run.StandardOutput.Trim());
        var errors = ScoutReport.StripAnsi(run.StandardError.Trim());
        if (!run.Success)
        {
            scoutHost.Content = ScoutSeverityUi.StateCard(run.Cancelled ? ScoutState.Cancelled : ScoutStatus.ClassifyFailure(errors + "\n" + output), (errors + "\n" + output).Trim(), $"docker scout compare --to {other} {reference}");
            return;
        }
        var viewer = new SearchableText("Docker Scout compare", PageKit.SafeFileName(reference) + "-compare.txt", "Search compare output") { Height = 360 };
        viewer.Search.Width = 200;
        viewer.SetText(output.Length > 0 ? output : errors);
        scoutHost.Content = Ui.VStack(6, Ui.Overline("Docker Scout compare"), Ui.Mono($"docker scout compare --to {other} {reference}", 12, "OnSurfaceVariant", true),
            run.Truncated ? Ui.Caption("The output exceeded the capture limit and is truncated.") : null, viewer);
    }
}
