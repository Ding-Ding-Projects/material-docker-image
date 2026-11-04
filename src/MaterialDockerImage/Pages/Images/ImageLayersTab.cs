using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Layers tab: docker image history as step rows with the cleaned instruction in mono, size, age and a size bar
/// relative to the largest layer. Has its own search and an option to hide metadata-only (empty) layers.
/// </summary>
internal sealed class ImageLayersTab : ImageDetailTab
{
    private readonly DockPanel view = new() { LastChildFill = true };
    private readonly StackPanel rows = new();
    private readonly TextBlock status = Ui.Caption("");
    private readonly SearchFilter search = new("Search layers", 220);
    private readonly IReadOnlyList<ImageLayerEntry> layers;
    private readonly string problem = "";
    private bool hideEmpty;

    public ImageLayersTab(ImageDetailContext context) : base(context)
    {
        var history = context.History;
        if (!history.Success) { layers = []; problem = Loc.T("History could not be read:") + " " + (history.StandardError.Trim() is { Length: > 0 } e ? e : $"docker exited with code {history.ExitCode}."); }
        else
        {
            try { layers = ImageLayerHistory.Parse(history.StandardOutput); }
            catch (JsonException e) { layers = []; problem = Loc.T("History output could not be parsed:") + " " + e.Message; }
        }

        var total = layers.Sum(l => l.Bytes);
        var nonEmpty = layers.Count(l => !l.Empty);
        var largest = layers.Count == 0 ? 0 : layers.Max(l => l.Bytes);
        var totals = Ui.Wrap(8,
            Ui.Badge($"{layers.Count} {(layers.Count == 1 ? "step" : "steps")}", Tone.Primary),
            Ui.Badge($"{nonEmpty} with content", Tone.Neutral),
            Ui.Badge(Loc.T("Total") + " " + Ui.FormatBytes(total), Tone.Neutral),
            largest > 0 ? Ui.Badge(Loc.T("Largest") + " " + Ui.FormatBytes(largest), Tone.Tertiary) : null);
        totals.Margin = new Thickness(0, 0, 0, 8);

        var hide = Ui.Switch("Hide empty layers", false, on => { hideEmpty = on; Render(); });
        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var e in new FrameworkElement[] { search, hide, Ui.InfoButton("Hide empty layers", "Steps such as CMD, ENV, LABEL and EXPOSE only change image metadata and add 0 B. Hiding them leaves the steps that add files.", "CMD [\"node\", \"server.js\"]  0 B"), status })
        {
            e.Margin = new Thickness(0, 0, 10, 4);
            e.VerticalAlignment = VerticalAlignment.Center;
            toolbar.Children.Add(e);
        }
        search.Changed += Render;

        view.Children.Add(totals.Dock(System.Windows.Controls.Dock.Top));
        view.Children.Add(toolbar.Dock(System.Windows.Controls.Dock.Top));
        view.Children.Add(Ui.Scroll(rows));
        Render();
    }

    public override string Title => "Layers";
    public override FrameworkElement View => view;

    private void Render()
    {
        rows.Children.Clear();
        if (problem.Length > 0) { rows.Children.Add(Ui.Text(problem, 13, null, "Error", true, false)); status.Text = ""; return; }
        if (layers.Count == 0) { rows.Children.Add(Ui.EmptyState(Icons.Layers, "No history entries", "docker image history returned no steps for this image.")); status.Text = ""; return; }
        var largest = Math.Max(1, layers.Max(l => l.Bytes));
        var shown = 0;
        foreach (var layer in layers)
        {
            if (hideEmpty && layer.Empty) continue;
            if (search.IsActive && !search.Matches(layer.Keyword + " " + layer.Instruction + " " + layer.SizeText + " " + layer.CreatedSince + " " + layer.Comment)) continue;
            rows.Children.Add(Row(layer, layers.Count - layer.Index, largest));
            shown++;
        }
        if (shown == 0) rows.Children.Add(Ui.EmptyState(Icons.FilterList, "No matches", search.Status.Length > 0 ? search.Status : "No layers match the search and filter."));
        status.Text = search.Status.Length > 0 ? search.Status : $"{shown} of {layers.Count} shown";
    }

    private static UIElement Row(ImageLayerEntry layer, int step, long largest)
    {
        var number = Ui.Mono(step.ToString(CultureInfo.InvariantCulture), 12, "OnSurfaceVariant");
        number.MinWidth = 26;
        var instruction = Ui.Mono(layer.Instruction.Length > 0 ? layer.Instruction : "(no instruction recorded)", 12, "OnSurface", true);
        instruction.MaxHeight = 52;
        instruction.TextTrimming = TextTrimming.CharacterEllipsis;
        instruction.ToolTip = layer.CreatedBy;
        var meta = Ui.Wrap(6,
            layer.Keyword.Length > 0 ? Ui.Badge(layer.Keyword, layer.Empty ? Tone.Neutral : Tone.Primary) : null,
            Ui.Caption(layer.CreatedSince),
            layer.Comment.Length > 0 ? Ui.Caption("· " + layer.Comment) : null);
        var size = Ui.Text(layer.SizeText.Length > 0 ? layer.SizeText : "—", 13, FontWeights.SemiBold, layer.Empty ? "OnSurfaceVariant" : "OnSurface", false, false);
        size.HorizontalAlignment = HorizontalAlignment.Right;
        var middle = Ui.VStack(4, meta, instruction, SizeBar(layer.Bytes, largest));
        var grid = Ui.Columns(10, [0, 1, 0], number, middle, size);
        var card = new Border { Child = grid, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 8, 12, 8), Margin = new Thickness(0, 0, 0, 6) };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceContainer");
        System.Windows.Automation.AutomationProperties.SetName(card, $"{Loc.T("Step")} {step}: {layer.Keyword} {layer.Instruction}, {layer.SizeText}");
        card.ContextMenu = new ContextMenu();
        card.ContextMenu.Items.Add(Ui.MenuItem("Copy instruction", Icons.ContentCopy, () => PageKit.Copy((layer.Keyword + " " + layer.Instruction).Trim(), "the instruction")));
        card.ContextMenu.Items.Add(Ui.MenuItem("Copy recorded command", Icons.Terminal, () => PageKit.Copy(layer.CreatedBy, "the recorded command")));
        return card;
    }

    /// <summary>A thin bar whose filled part is the layer size relative to the largest layer.</summary>
    private static FrameworkElement SizeBar(long bytes, long largest)
    {
        var ratio = Math.Clamp(bytes / (double)largest, 0, 1);
        var grid = new Grid { Height = 6, Margin = new Thickness(0, 2, 0, 0) };
        var track = new Border { CornerRadius = new CornerRadius(3) };
        track.SetResourceReference(Border.BackgroundProperty, "SurfaceHighest");
        grid.Children.Add(track.At(0, 0, 1, 2));
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ratio, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - ratio, GridUnitType.Star) });
        if (bytes > 0)
        {
            var fill = new Border { CornerRadius = new CornerRadius(3), MinWidth = 3 };
            fill.SetResourceReference(Border.BackgroundProperty, "Primary");
            grid.Children.Add(fill.At(0, 0));
        }
        return grid;
    }
}
