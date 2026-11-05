using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Dockerfile view: stages laid out by dependency depth, FROM and COPY --from connectors, and the stage inspector.</summary>
public sealed partial class CanvasPage
{
    private const double StageHeight = 176, ColumnGap = 110, RowGap = 56, GraphMargin = 40;
    private DockerGraph? graph;
    private string graphSource = "";

    private static string? CurrentSource() =>
        Host.HandOff.TryGetValue("Build.CurrentSource", out var value) && value is Func<string> get ? get() : null;

    private void DrawDockerfile()
    {
        var source = CurrentSource();
        if (source == null)
        {
            graph = null; graphSource = "";
            surface.Clear(10, 10);
            SetObjects([], "");
            SetWarning(null);
            subtitle.Text = Loc.T("No Dockerfile open");
            SetEmpty(Ui.EmptyState(Icons.AccountTree, "No Dockerfile open", "Open or write a Dockerfile in Build. Canvas draws the stages from the Build editor.",
                Ui.Button("Open Build", Icons.Construction, ButtonKind.Filled, () => Host.Navigate("Build"))));
            ShowInspectorHint("Open a Dockerfile in Build to inspect its stages.");
            return;
        }
        graphSource = source;
        graph = DockerfileGraph.Parse(source);
        var sourceOnly = graph.Warnings.Any(w => w.StartsWith("Source-only", StringComparison.Ordinal));
        SetWarning(graph.Warnings.Count > 0 ? string.Join(" ", graph.Warnings) : null);
        subtitle.Text = $"{graph.Stages.Count} {Loc.T(graph.Stages.Count == 1 ? "stage" : "stages")} · {graph.Edges.Count} {Loc.T(graph.Edges.Count == 1 ? "link" : "links")} · {Loc.T("parsed from the Build editor, not executed")}";
        if (graph.Stages.Count == 0)
        {
            surface.Clear(10, 10);
            SetObjects([], "");
            SetEmpty(Ui.EmptyState(Icons.AccountTree, sourceOnly ? "Source-only mode" : "No stages found",
                sourceOnly ? "This Dockerfile uses syntax Canvas does not draw. Edit it in Build; nothing was changed." : "Add a FROM line in the Build editor to see stages here.",
                Ui.Button("Open Build", Icons.Construction, ButtonKind.Filled, () => Host.Navigate("Build"))));
            ShowInspectorHint("No stage to inspect.");
            return;
        }
        SetEmpty(null);
        LayoutStages(graph, out var positions, out var width, out var height);
        surface.Clear(width, height);
        var target = BuildTarget();
        var lastIndex = graph.Stages.Count - 1;
        foreach (var stage in graph.Stages)
        {
            var (x, y) = positions[stage.Index];
            var isTarget = target.Length > 0 ? string.Equals(stage.Name, target, StringComparison.OrdinalIgnoreCase) : stage.Index == lastIndex;
            if (!graph.Stages.Any(s => s.Index < stage.Index && string.Equals(s.Name, stage.Base, StringComparison.OrdinalIgnoreCase)))
                surface.AddBasePill(x, y - 36, stage.Base, 8);
            surface.AddNode(x, y, GraphSurface.NodeWidth, StageCard(stage, isTarget), $"Stage {stage.Name}" + (isTarget ? ", build target" : ""), StageKey(stage), () => SelectStage(stage), isTarget ? "SecondaryContainer" : "SurfaceContainer").Height = StageHeight;
        }
        var pairCounts = new Dictionary<(int, int), int>();
        foreach (var edge in graph.Edges)
        {
            var (fx, fy) = positions[edge.From];
            var (tx, ty) = positions[edge.To];
            var n = pairCounts.TryGetValue((edge.From, edge.To), out var c) ? c : 0;
            pairCounts[(edge.From, edge.To)] = n + 1;
            var label = edge.Label == "FROM" ? "FROM " + graph.Stages[edge.From].Name : CopyLabel(graph, edge);
            surface.AddEdge(new Point(fx + GraphSurface.NodeWidth, fy + StageHeight / 2 + n * 18 - 9), new Point(tx - 2, ty + StageHeight / 2 + n * 18 - 9), label, edge.Label == "FROM" ? "Primary" : "Tertiary", edge.Label != "FROM", n * 28);
        }
        var items = new List<GraphObject>();
        foreach (var stage in graph.Stages)
        {
            var s = stage;
            items.Add(new(Icons.Layers, stage.Name, $"{stage.Instructions.Count} {Loc.T(stage.Instructions.Count == 1 ? "step" : "steps")}", 0, StageKey(stage), () => SelectStage(s), () => OpenLine(s.Line)));
            foreach (var line in stage.Instructions.Where(IsNotable))
                items.Add(new(InstructionIcon(line), Shorten(line, 40), "", 1, StageKey(stage), () => SelectStage(s), () => OpenLine(FindLine(s, line))));
        }
        SetObjects(items, Loc.T("Read from the Build editor. Edits here change the editor text; save in Build to write the file."));
        var keep = graph.Stages.FirstOrDefault(s => Equals(StageKey(s), selectedKey)) ?? graph.Stages.FirstOrDefault(s => string.Equals(s.Name, target, StringComparison.OrdinalIgnoreCase)) ?? graph.Stages[^1];
        SelectStage(keep);
        Dispatcher.BeginInvoke(() => { if (surface.ZoomLevel > 0 && width > 0) surface.Fit(); }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static string StageKey(DockerStage stage) => "stage:" + stage.Index;

    /// <summary>The target stage chosen in the Build page; empty when none is set (the final stage is built).</summary>
    private static string BuildTarget() => Host.HandOff.TryGetValue("Build.CurrentTarget", out var value) && value is Func<string> get ? get() : "";

    private static void LayoutStages(DockerGraph graph, out Dictionary<int, (double X, double Y)> positions, out double width, out double height)
    {
        var depth = new int[graph.Stages.Count];
        // Edges always point from an earlier or any resolved stage; iterate to a fixed point (bounded by stage count).
        for (var pass = 0; pass < graph.Stages.Count; pass++)
        {
            var changed = false;
            foreach (var edge in graph.Edges)
                if (edge.From != edge.To && depth[edge.To] < depth[edge.From] + 1 && depth[edge.From] + 1 < graph.Stages.Count) { depth[edge.To] = depth[edge.From] + 1; changed = true; }
            if (!changed) break;
        }
        positions = new();
        var rows = new Dictionary<int, int>();
        foreach (var stage in graph.Stages)
        {
            var d = depth[stage.Index];
            var row = rows.TryGetValue(d, out var r) ? r : 0;
            rows[d] = row + 1;
            positions[stage.Index] = (GraphMargin + d * (GraphSurface.NodeWidth + ColumnGap), GraphMargin + 24 + row * (StageHeight + RowGap));
        }
        var columns = depth.Length == 0 ? 1 : depth.Max() + 1;
        var maxRows = rows.Count == 0 ? 1 : rows.Values.Max();
        width = GraphMargin * 2 + columns * GraphSurface.NodeWidth + (columns - 1) * ColumnGap;
        height = GraphMargin * 2 + 24 + maxRows * StageHeight + (maxRows - 1) * RowGap;
    }

    private static FrameworkElement StageCard(DockerStage stage, bool isTarget)
    {
        var title = Ui.HStack(8, Ui.Icon(Icons.Layers, 20, true, isTarget ? "OnSecondaryContainer" : "Primary"), Ui.Text(stage.Name, 16, FontWeights.SemiBold, isTarget ? "OnSecondaryContainer" : "OnSurface", false, false));
        var badge = isTarget ? Ui.Badge(Loc.T("Target"), Tone.Primary) : Ui.Badge($"{Loc.T("Line")} {stage.Line + 1}");
        var header = Ui.Columns(8, [1, 0], title, badge);
        var lines = stage.Instructions.Take(4).Select(l => Shorten(l, 34)).ToList();
        if (stage.Instructions.Count > 4) lines.Add($"… {stage.Instructions.Count - 4} {Loc.T("more")}");
        var body = Ui.Mono(lines.Count == 0 ? Loc.T("(no instructions)") : string.Join("\n", lines), 12, isTarget ? "OnSecondaryContainer" : "OnSurfaceVariant", false);
        body.TextTrimming = TextTrimming.CharacterEllipsis;
        var footer = Ui.Text($"{Loc.T("Line")} {stage.Line + 1} · {stage.Instructions.Count} {Loc.T(stage.Instructions.Count == 1 ? "step" : "steps")}" + (stage.Platform.Length > 0 ? " · " + stage.Platform.Replace("--platform=", "") : ""), 12, null, isTarget ? "OnSecondaryContainer" : "Outline", false, false);
        return Ui.VStack(8, header, body, footer);
    }

    private static string CopyLabel(DockerGraph graph, GraphEdge edge)
    {
        var from = graph.Stages[edge.From];
        var copy = graph.Stages[edge.To].Instructions.FirstOrDefault(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"--from=(" + System.Text.RegularExpressions.Regex.Escape(from.Name) + "|" + from.Index + @")(\s|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)));
        var path = copy?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).FirstOrDefault(p => !p.StartsWith("--", StringComparison.Ordinal));
        return "COPY --from=" + from.Name + (path != null ? "\n" + Shorten(path, 28) : "");
    }

    private static bool IsNotable(string line) => System.Text.RegularExpressions.Regex.IsMatch(line, @"^(COPY\s+.*--from=|EXPOSE|USER|CMD|ENTRYPOINT|HEALTHCHECK|VOLUME|WORKDIR)", System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    private static string InstructionIcon(string line) => line.Split(' ')[0].ToUpperInvariant() switch
    {
        "COPY" => Icons.ContentCopy, "EXPOSE" => Icons.Lan, "USER" => Icons.Person, "CMD" or "ENTRYPOINT" => Icons.Terminal,
        "HEALTHCHECK" => Icons.Stethoscope, "VOLUME" => Icons.Database, "WORKDIR" => Icons.FolderOpen, _ => Icons.Code
    };
    private static string Shorten(string text, int max) => text.Length > max ? text[..(max - 1)] + "…" : text;

    private int FindLine(DockerStage stage, string instruction)
    {
        var lines = graphSource.Replace("\r\n", "\n").Split('\n');
        for (var i = stage.Line + 1; i < lines.Length; i++) if (lines[i].Trim() == instruction) return i;
        return stage.Line;
    }

    private static void OpenLine(int line)
    {
        if (Host.HandOff.TryGetValue("Build.GoToLine", out var value) && value is Action<int> go) go(line);
        else { Host.Navigate("Build"); }
    }

    private void SelectStage(DockerStage stage)
    {
        if (graph == null) return;
        SyncSelection(StageKey(stage));
        inspector.Children.Clear();
        inspectorActions.Content = null;
        var numbered = int.TryParse(stage.Name, out _);
        var alias = Ui.Input(stage.Name, "Alias", true);
        var baseImage = Ui.Input(stage.Base, "Base image", true);
        var pinned = stage.Base.Contains('@');
        var localBase = graph.Stages.Any(s => s.Index < stage.Index && string.Equals(s.Name, stage.Base, StringComparison.OrdinalIgnoreCase));
        var pinNote = localBase ? Ui.Caption(Loc.T("Builds on the earlier stage") + " " + stage.Base)
            : Ui.Text(pinned ? Loc.T("Pinned to a digest") : Loc.T("Not pinned to a digest"), 12, null, pinned ? "Success" : "Warning");
        var apply = Ui.Button("Apply stage changes", Icons.Check, ButtonKind.Tonal, () => ApplyStageEdit(stage, baseImage.Text.Trim(), alias.Text.Trim()));
        apply.HorizontalAlignment = HorizontalAlignment.Left;

        var incoming = graph.Edges.Where(e => e.To == stage.Index).ToList();
        var outgoing = graph.Edges.Where(e => e.From == stage.Index).ToList();
        UIElement Relation(GraphEdge e, bool isIncoming)
        {
            var other = graph.Stages[isIncoming ? e.From : e.To];
            var kind = e.Label == "FROM" ? (isIncoming ? "FROM" : "base of") : "COPY --from";
            // Show the copied path as the design does; the link kind stays in the tooltip.
            var detail = kind;
            if (e.Label != "FROM" && isIncoming && CopyLabel(graph, e).Split('\n') is { Length: > 1 } parts) detail = parts[1];
            var name = Ui.Mono(other.Name, 12, "OnSurface");
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            var meta = Ui.Mono(detail, 12, "OnSurfaceVariant");
            meta.TextTrimming = TextTrimming.CharacterEllipsis; meta.MaxWidth = 150;
            var row = Ui.Card(Ui.Columns(8, [1, 0], name, meta), 0, "SurfaceContainer", 8);
            row.Height = 36; row.Padding = new Thickness(12, 0, 12, 0); row.Cursor = System.Windows.Input.Cursors.Hand;
            row.ToolTip = kind + " · " + other.Name;
            System.Windows.Automation.AutomationProperties.SetName(row, other.Name + ", " + kind);
            row.Focusable = true;
            row.MouseLeftButtonUp += (_, _) => SelectStage(other);
            row.KeyDown += (_, k) => { if (k.Key is System.Windows.Input.Key.Enter or System.Windows.Input.Key.Space) { SelectStage(other); k.Handled = true; } };
            return row;
        }

        var target = BuildTarget();
        var isTarget = !numbered && string.Equals(target, stage.Name, StringComparison.OrdinalIgnoreCase);
        var targetSwitch = Ui.Switch("Use as build target", isTarget, on =>
        {
            if (Host.HandOff.TryGetValue("Build.SetTarget", out var value) && value is Action<string> set) set(on ? stage.Name : "");
            DrawDockerfile();
        }, numbered ? "Add an alias to this stage before using it as a target." : "Sets --target in Build.");
        targetSwitch.IsEnabled = !numbered;

        var openSource = Ui.Button("Open source", Icons.Code, ButtonKind.Outlined, () => OpenLine(stage.Line));
        var buildStage = Ui.Button("Build stage", Icons.Construction, ButtonKind.Filled, () =>
        {
            if (Host.HandOff.TryGetValue("Build.SetTarget", out var value) && value is Action<string> set) set(stage.Name);
            Host.Navigate("Build");
        });
        buildStage.IsEnabled = !numbered;
        if (numbered) buildStage.ToolTip = Loc.T("This unnamed stage has no alias. Add one before building it as a target.");

        var sourceLines = Ui.Mono(stage.Instructions.Count == 0 ? Loc.T("(no instructions)") : string.Join("\n", stage.Instructions), 12, "OnSurfaceVariant", true);
        inspector.Children.Add(Ui.VStack(4,
            Ui.Overline($"{Loc.T("Stage")} {stage.Index + 1} {Loc.T("of")} {graph.Stages.Count}"),
            Ui.Title(stage.Name, 22),
            Ui.Caption($"{Loc.T("Line")} {stage.Line + 1}" + (stage.Platform.Length > 0 ? " · " + stage.Platform : ""))));
        inspector.Children.Add(Ui.VStack(12,
            Ui.Field("Alias", alias, "The stage name used by --target, COPY --from and later FROM lines. Renaming is refused while other stages refer to it.", "runtime"),
            Ui.VStack(4, Ui.Field("Base image", baseImage, "The image or earlier stage this stage starts from. Only the FROM line changes.", "node:22-alpine"), pinNote),
            apply).Margin(0, 16, 0, 0));
        if (incoming.Count > 0) inspector.Children.Add(Section("Receives from", incoming.Select(e => Relation(e, true)).ToArray()));
        if (outgoing.Count > 0) inspector.Children.Add(Section("Feeds", outgoing.Select(e => Relation(e, false)).ToArray()));
        inspector.Children.Add(targetSwitch.Margin(0, 16, 0, 0));
        var actionRow = Ui.Columns(8, [1, 1], openSource, buildStage);
        inspectorActions.Content = actionRow;
        inspector.Children.Add(Section("Source lines", Ui.Card(sourceLines, 12, "SurfaceContainer", 12)));
    }

    private void ApplyStageEdit(DockerStage stage, string newBase, string newName)
    {
        var current = CurrentSource();
        if (current == null) throw new InvalidOperationException("The Build editor is not available. Open Build and try again.");
        if (current != graphSource) { DrawDockerfile(); throw new InvalidOperationException("The Build editor changed since this graph was drawn. The graph was refreshed; review and apply again."); }
        if (newBase == stage.Base && newName == stage.Name) { Host.Notify.Info("Nothing changed."); return; }
        var updated = DockerfileGraph.EditStage(current, stage, newBase, newName);
        if (!Host.HandOff.TryGetValue("Build.SetSource", out var value) || value is not Action<string> set) throw new InvalidOperationException("The Build editor is not available.");
        set(updated);
        selectedKey = StageKey(stage);
        DrawDockerfile();
        Host.Notify.Success("Stage updated in the Build editor. Save it in Build before building.");
    }
}
