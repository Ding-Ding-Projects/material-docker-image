using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// SBOM tab: docker scout sbom --format json, shown as a searchable package table with counts by package type and an
/// export of the exact JSON Scout produced. Generated SBOMs are kept in memory for this session.
/// </summary>
internal sealed class ImageSbomTab : ImageDetailTab
{
    private static readonly Dictionary<string, (string Json, DateTime Generated)> Generated = new(StringComparer.Ordinal);

    private readonly StackPanel layout = new();
    private readonly ScrollViewer view;
    private readonly ContentControl status = new();
    private readonly WrapPanel types = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly DataTable<ScoutSbomPackage> table = new("SBOM packages", "Search packages, versions or licenses");
    private readonly Button export;
    private string json = "";

    public ImageSbomTab(ImageDetailContext context) : base(context)
    {
        var generate = Ui.AsyncButton("Generate SBOM", Icons.ReceiptLong, ButtonKind.Filled, GenerateAsync);
        export = Ui.Button("Export SBOM JSON…", Icons.FileExport, ButtonKind.Outlined, Export);
        export.IsEnabled = false;
        var actions = Ui.Wrap(8, generate, export, Ui.InfoButton("SBOM",
            "A software bill of materials lists the packages Docker Scout detects in the image: operating system packages and language dependencies with versions and licenses. Export saves the JSON exactly as docker scout sbom wrote it.",
            "docker scout sbom --format json --output sbom.json nginx:1.27"));
        actions.Margin = new Thickness(0, 0, 0, 8);

        table.Key = p => p.Type + "|" + p.Name + "|" + p.Version + "|" + p.Purl;
        table.EmptyTitle = "No SBOM yet";
        table.EmptyMessage = "Generate an SBOM with Docker Scout to list the packages in this image.";
        table.ChipColumn("Type", p => p.Type.Length > 0 ? p.Type : "other", _ => Tone.Primary, 90)
             .Column("Name", p => p.Name, 170)
             .Column("Version", p => p.Version, 130, true)
             .Column("Licenses", p => p.Licenses, 140)
             .Column("Package URL", p => p.Purl, 280, true)
             .RowAction("Copy package URL", Icons.Link, p => { PageKit.Copy(p.Purl.Length > 0 ? p.Purl : p.Name, "the package URL"); return Task.CompletedTask; })
             .RowAction("Copy name and version", Icons.ContentCopy, p => { PageKit.Copy(p.Name + " " + p.Version, "the package name and version"); return Task.CompletedTask; });

        table.Height = 560;
        foreach (var child in new UIElement[] { actions, status, types, table }) layout.Children.Add(child);
        view = Ui.Scroll(layout);
    }

    public override string Title => "SBOM";
    public override FrameworkElement View => view;

    private string CacheKey => (Ctx.Context ?? "") + "\n" + Ctx.Image.Id;

    protected override async Task OnActivateAsync()
    {
        if (Generated.TryGetValue(CacheKey, out var cached)) { Show(cached.Json, cached.Generated); return; }
        var availability = await ScoutRunner.CheckAsync(Ctx.Context);
        if (!availability.Available)
        {
            status.Content = ScoutSeverityUi.StateCard(ScoutState.PluginMissing, availability.Output, availability.Command, ScoutSeverityUi.InstallGuideButton());
            return;
        }
        status.Content = Ui.Caption("Not generated in this session yet. Generation runs docker scout sbom and can take a minute for large images.").Margin(0, 0, 0, 8);
    }

    private async Task GenerateAsync()
    {
        if (!EnsureCurrent()) return;
        var availability = await ScoutRunner.CheckAsync(Ctx.Context);
        if (!availability.Available) { status.Content = ScoutSeverityUi.StateCard(ScoutState.PluginMissing, availability.Output, availability.Command, ScoutSeverityUi.InstallGuideButton()); return; }
        var (result, text) = await ScoutRunner.SbomAsync(Ctx.Image.Reference, Ctx.Context);
        if (result == null) return;
        if (!result.Success || text.Length == 0)
        {
            var output = ScoutReport.StripAnsi((result.StandardError + "\n" + result.StandardOutput).Trim());
            var state = result.Cancelled ? ScoutState.Cancelled : result.Success ? ScoutState.Failed : ScoutStatus.ClassifyFailure(output);
            status.Content = ScoutSeverityUi.StateCard(state, result.Success ? "docker scout sbom reported success but wrote no report.\n" + output : output, "docker scout sbom --format json --output <temporary file> " + Ctx.Image.Reference);
            return;
        }
        Generated[CacheKey] = (text, DateTime.Now);
        Show(text, DateTime.Now);
    }

    private void Show(string text, DateTime generated)
    {
        IReadOnlyList<ScoutSbomPackage> packages;
        try { packages = ScoutSbom.Parse(text); }
        catch (FormatException e)
        {
            status.Content = ScoutSeverityUi.StateCard(ScoutState.Failed, e.Message);
            json = text;
            export.IsEnabled = true;
            return;
        }
        json = text;
        export.IsEnabled = true;
        status.Content = Ui.Caption($"{packages.Count} packages · generated {generated:yyyy-MM-dd HH:mm:ss} with docker scout sbom").Margin(0, 0, 0, 8);
        types.Children.Clear();
        foreach (var (type, count) in ScoutSbom.CountByType(packages))
        {
            var badge = Ui.Badge($"{type} · {count}", Tone.Neutral);
            badge.Margin = new Thickness(0, 0, 6, 6);
            types.Children.Add(badge);
        }
        table.EmptyTitle = "No packages";
        table.EmptyMessage = "Docker Scout detected no packages in this image.";
        table.SetItems(packages);
    }

    private void Export()
    {
        if (json.Length == 0) return;
        try { PageKit.SaveText(PageKit.SafeFileName(Ctx.Image.Reference) + "-sbom.json", "JSON|*.json|All files|*.*", json, "the SBOM"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Host.Notify.Error("The SBOM could not be saved: " + e.Message); }
    }
}
