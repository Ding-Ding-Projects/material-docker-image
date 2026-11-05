using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>One prune category: label, explanation, the exact command and whether it is selected.</summary>
public sealed class PruneCategory
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string Note { get; init; }
    public bool Selected { get; set; }
    public bool Danger { get; init; }
    public CheckBox? Box { get; set; }
    public TextBlock SizeText { get; } = Ui.Text("", 13, FontWeights.SemiBold, "OnSurfaceVariant", false, false);
}

/// <summary>
/// Cleanup with independent categories. Nothing runs until the super confirmation lists the exact categories and,
/// where cheap to read, the exact candidates. Each selected prune runs separately and reports its own result.
/// </summary>
public sealed class CleanupPanel : Border
{
    private static AppHost Host => AppHost.Current;
    private readonly List<PruneCategory> categories;
    private readonly ComboBox buildAge = Ui.Combo(["Any age", "Older than 24 hours", "Older than 7 days"], "Any age");
    private readonly CheckBox allVolumes;
    private readonly TextBlock summary = Ui.Caption("");
    private readonly TextBlock results = Ui.Mono("", 12, "OnSurface", true);
    private readonly Border resultsCard;
    private readonly Func<string?> boundContext;
    private readonly Func<bool> isCurrent;
    private readonly Func<Task> afterPrune;
    private Dictionary<string, (string Size, string Reclaimable)> usage = new(StringComparer.OrdinalIgnoreCase);

    public CleanupPanel(Func<string?> context, Func<bool> current, Func<Task> refresh)
    {
        boundContext = context; isCurrent = current; afterPrune = refresh;
        CornerRadius = new CornerRadius(16);
        Padding = new Thickness(20);
        SetResourceReference(BackgroundProperty, "SurfaceLow");
        categories =
        [
            new() { Id = "containers", Label = "Stopped containers", Note = "Removes every container that is not running (exited, created or dead). docker container prune -f", Selected = true },
            new() { Id = "dangling", Label = "Dangling images", Note = "Untagged image layers no tag or container refers to. docker image prune -f", Selected = true },
            new() { Id = "images", Label = "All unused images", Note = "Every image not used by any container, including tagged ones you may want later. docker image prune -a -f" },
            new() { Id = "networks", Label = "Unused networks", Note = "User-defined networks with no containers. Built-in bridge, host and none are never removed. docker network prune -f", Selected = true },
            new() { Id = "build", Label = "Build cache", Note = "BuildKit cache records. Later builds may take longer. docker builder prune -f", Selected = true },
            new() { Id = "volumes", Label = "Unused volumes", Note = "Volumes may hold databases and uploads. Removing them deletes that data permanently. docker volume prune -f", Danger = true },
        ];
        allVolumes = Ui.Check("Include all unused named volumes (--all)", false, _ => UpdateSummary());
        allVolumes.Margin = new Thickness(30, 0, 0, 10);
        resultsCard = Ui.Card(Ui.VStack(6, Ui.Overline("Last cleanup result"), results), 12, "SurfaceContainer", 12);
        resultsCard.Visibility = Visibility.Collapsed;

        var list = new StackPanel();
        for (var index = 0; index < categories.Count; index++)
        {
            var category = categories[index];
            var box = Ui.Check(category.Label, category.Selected, v => { category.Selected = v; UpdateSummary(); });
            box.VerticalAlignment = VerticalAlignment.Center;
            category.Box = box;
            var shortNote = category.Note.Contains(" docker ") ? category.Note[..category.Note.LastIndexOf(" docker ", StringComparison.Ordinal)] : category.Note;
            var noteBlock = category.Danger
                ? new DockPanel { Children = { Ui.Icon(Icons.Warning, 14, true, "Warning").Margin(0, 0, 4, 0).Dock(Dock.Left), Ui.Text("Unused volumes may hold databases. Removing them deletes that data.", 12, null, "Warning", true, true) } }
                : (UIElement)Ui.Text(shortNote, 12, null, "OnSurfaceVariant", true, false);
            var text = Ui.VStack(2, Ui.Text(category.Label, 14, null, "OnSurface"), noteBlock);
            box.Content = text;
            System.Windows.Automation.AutomationProperties.SetName(box, Loc.T(category.Label));
            System.Windows.Automation.AutomationProperties.SetHelpText(box, category.Note);
            var row = Ui.Columns(12, [1, 0], box, category.SizeText);
            row.MinHeight = 52;
            category.SizeText.VerticalAlignment = VerticalAlignment.Center;
            var container = Ui.VStack(0, row);
            if (category.Id == "volumes") Ui.Add(container, allVolumes, 0);
            var line = new Border { Child = container, BorderThickness = new Thickness(0, 0, 0, index < categories.Count - 1 ? 1 : 0) };
            line.SetResourceReference(Border.BorderBrushProperty, "SurfaceHigh");
            list.Children.Add(line);
        }
        buildAge.SelectionChanged += (_, _) => UpdateSummary();
        var prune = Ui.AsyncButton("Prune…", Icons.CleaningServices, ButtonKind.Danger, PruneAsync);
        var footer = Ui.Columns(12, [1, 0], summary, prune);
        var ageRow = Ui.HStack(8, Ui.Caption("Build cache older than"), buildAge);
        ageRow.Margin = new Thickness(0, 0, 0, 0);
        buildAge.Height = 32; buildAge.MinWidth = 150;
        System.Windows.Automation.AutomationProperties.SetName(buildAge, Loc.T("Build cache age"));
        Child = Ui.VStack(12,
            Ui.VStack(4, Ui.Text("Clean up", 18, FontWeights.SemiBold), Ui.Caption("Choose exactly what to remove. Nothing is removed until you confirm.")),
            list, ageRow, footer, resultsCard);
        UpdateSummary();
    }

    public void SetUsage(Dictionary<string, (string Size, string Reclaimable)> values)
    {
        usage = values;
        string? Reclaim(string type) => usage.TryGetValue(type, out var u) ? u.Reclaimable : null;
        foreach (var c in categories)
        {
            var text = c.Id switch
            {
                "containers" => Reclaim("Containers"),
                "build" => Reclaim("Build Cache"),
                "volumes" => Reclaim("Local Volumes"),
                "images" => Reclaim("Images"),
                _ => null
            };
            c.SizeText.Text = text == null ? "" : $"up to {text}";
            c.SizeText.ToolTip = text == null ? null : Loc.T("Reclaimable estimate reported by docker system df. The engine reports the actual amount after pruning.");
        }
    }

    private void UpdateSummary()
    {
        var selected = categories.Where(c => c.Selected).ToList();
        summary.Text = selected.Count == 0 ? Loc.T("Select at least one category.") : $"{selected.Count} categor{(selected.Count == 1 ? "y" : "ies")} selected" + (selected.Any(c => c.Danger) ? " · includes volumes" : " · volumes are kept");
    }

    private string? AgeFilter => buildAge.SelectedIndex switch { 1 => "until=24h", 2 => "until=168h", _ => null };

    private List<string> Command(PruneCategory c) => c.Id switch
    {
        "containers" => ["container", "prune", "-f"],
        "dangling" => ["image", "prune", "-f"],
        "images" => ["image", "prune", "-a", "-f"],
        "networks" => ["network", "prune", "-f"],
        "build" => AgeFilter is { } f ? ["builder", "prune", "-f", "--filter", f] : ["builder", "prune", "-f"],
        "volumes" => allVolumes.IsChecked == true ? ["volume", "prune", "-f", "--all"] : ["volume", "prune", "-f"],
        _ => throw new InvalidOperationException("Unknown category.")
    };

    private static readonly Regex AnonymousVolume = new("^[0-9a-f]{64}$");

    /// <summary>Reads the candidates that are cheap to list so the confirmation names them. Unknown sets say so.</summary>
    private async Task<List<string>> CandidatesAsync(PruneCategory c)
    {
        var context = boundContext();
        try
        {
            switch (c.Id)
            {
                case "containers":
                {
                    var rows = await Host.ReadRows(["container", "ls", "--all", "--filter", "status=exited", "--filter", "status=created", "--filter", "status=dead", "--format", "{{json .}}"], context);
                    return rows.Select(r => $"container {CliValues.Str(r, "Names")} ({CliValues.Str(r, "ID")}, {CliValues.Str(r, "Status")})").ToList();
                }
                case "dangling":
                {
                    var rows = await Host.ReadRows(["image", "ls", "--filter", "dangling=true", "--format", "{{json .}}"], context);
                    return rows.Select(r => $"image {CliValues.Str(r, "ID")} ({CliValues.Str(r, "Size")}, created {CliValues.Str(r, "CreatedSince")})").ToList();
                }
                case "volumes":
                {
                    var rows = await Host.ReadRows(["volume", "ls", "--filter", "dangling=true", "--format", "{{json .}}"], context);
                    var names = rows.Select(r => CliValues.Str(r, "Name")).ToList();
                    var chosen = allVolumes.IsChecked == true ? names : names.Where(n => AnonymousVolume.IsMatch(n)).ToList();
                    return chosen.Select(n => "volume " + n + (AnonymousVolume.IsMatch(n) ? " (anonymous)" : " (named)")).ToList();
                }
                default: return [];
            }
        }
        catch (Exception e) when (e is InvalidOperationException or System.Text.Json.JsonException)
        {
            return [$"(could not list candidates: {e.Message})"];
        }
    }

    private async Task PruneAsync()
    {
        if (!isCurrent()) { Host.Notify.Error("The engine context changed since this page loaded. Refresh the page before cleaning up."); return; }
        var selected = categories.Where(c => c.Selected).ToList();
        if (selected.Count == 0) { Host.Notify.Warning("Select at least one category to prune."); return; }
        var affected = new List<string>();
        foreach (var c in selected)
        {
            affected.Add($"{c.Label}: docker {string.Join(' ', Command(c))}");
            var candidates = await CandidatesAsync(c);
            if (c.Id is "containers" or "dangling" or "volumes")
            {
                if (candidates.Count == 0) affected.Add("    nothing to remove right now");
                else affected.AddRange(candidates.Select(x => "    " + x));
            }
            else affected.Add("    the engine selects the exact items when the prune runs");
        }
        if (!selected.Any(c => c.Danger)) affected.Add("Volumes: not included (kept)");
        var title = selected.Any(c => c.Danger) ? "Remove unused data including volumes" : "Remove unused data";
        if (!await Host.Overlay.SuperConfirmAsync(title, Loc.T("This cannot be undone. Each selected category runs as its own prune command against context") + " " + (boundContext() ?? "current") + ".", affected)) return;
        if (!isCurrent()) { Host.Notify.Error("The engine context changed during confirmation. Nothing was removed."); return; }

        var report = new List<string>();
        foreach (var c in selected)
        {
            var result = await Host.Execute("Prune " + c.Label.ToLowerInvariant(), Command(c), boundContext: boundContext());
            if (result == null) { report.Add($"{c.Label}: not run (another operation was active or docker could not start)"); break; }
            var reclaimed = CliValues.ReclaimedSpace(result.StandardOutput);
            report.Add(result.Cancelled ? $"{c.Label}: canceled" : result.Success
                ? $"{c.Label}: completed" + (reclaimed != null ? $", reclaimed {reclaimed}" : ", the engine did not report reclaimed space")
                : $"{c.Label}: failed with exit code {result.ExitCode}" + (result.StandardError.Trim().Length > 0 ? " · " + result.StandardError.Trim().Split('\n')[0] : ""));
            if (result.Cancelled) break;
        }
        results.Text = string.Join(Environment.NewLine, report);
        resultsCard.Visibility = Visibility.Visible;
        if (!Host.IsCapture) Host.History.Record("cleanup", "System cleanup: " + string.Join("; ", report));
        await afterPrune();
    }
}
