using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Left column: stage list with its actions, and the starters.</summary>
public sealed partial class CreatorPage
{
    private readonly ListBox stageList = new() { MinHeight = 80, MaxHeight = 360 };
    private readonly SearchFilter stageSearch = new("Search stages", 200);
    private Button? stageUp, stageDown, stageDuplicate, stageDelete, stageTarget;
    private bool syncingStageList;

    private UIElement BuildStagesColumn()
    {
        stageSearch.Width = double.NaN;
        stageSearch.Changed += RefreshStageList;
        System.Windows.Automation.AutomationProperties.SetName(stageList, Loc.T("Stages"));
        ScrollViewer.SetHorizontalScrollBarVisibility(stageList, ScrollBarVisibility.Disabled);
        stageList.SelectionChanged += (_, _) =>
        {
            if (syncingStageList || stageList.SelectedItem is not ListBoxItem { Tag: int index }) return;
            if (index == selectedStage) return;
            FlushCommit();
            selectedStage = index; expanded = null;
            RebuildStageEditor();
            UpdateStageButtons();
        };

        var add = Ui.Button("Add stage", Icons.Add, ButtonKind.Text, AddStage);
        add.Height = 36; add.MinHeight = 36; add.Padding = new Thickness(6, 0, 12, 0);
        stageUp = Ui.IconButton(Icons.ExpandLess, "Move stage up", () => MoveStage(-1));
        stageDown = Ui.IconButton(Icons.ExpandMore, "Move stage down", () => MoveStage(1));
        stageDuplicate = Ui.IconButton(Icons.FilterNone, "Duplicate stage", DuplicateStage);
        stageTarget = Ui.IconButton(Icons.Keep, "Set as build target", ToggleTarget);
        stageDelete = Ui.IconButton(Icons.Delete, "Delete stage", () => PageTasks.Fire(DeleteStageAsync));
        var actions = Ui.Wrap(2, stageUp, stageDown, stageDuplicate, stageTarget, stageDelete);

        var starters = new WrapPanel();
        foreach (var starter in DockerfileStarters.All)
        {
            var b = CreatorChips.Action(starter.Label, () => PageTasks.Fire(() => ApplyStarterAsync(starter)), 32, false);
            b.ToolTip = starter.Description;
            b.Margin = new Thickness(0, 0, 6, 6);
            starters.Children.Add(b);
        }

        // The outline of the selected stage: one row per line, click to open that step in the centre.
        System.Windows.Automation.AutomationProperties.SetName(outlineList, Loc.T("Instructions"));
        ScrollViewer.SetHorizontalScrollBarVisibility(outlineList, ScrollBarVisibility.Disabled);
        outlineList.SelectionChanged += (_, _) =>
        {
            if (syncingOutline || outlineList.SelectedItem is not ListBoxItem { Tag: int index }) return;
            if (doc.Stages.Count == 0) return;
            var stage = doc.Stages[selectedStage];
            if (index < 0)
            {
                if (expanded == null) return;
                CloseStep();
                Dispatcher.BeginInvoke(() => baseImage.Focus(), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else if (index < stage.Instructions.Count && expanded != stage.Instructions[index]) OpenStep(stage.Instructions[index], false);
        };
        outlineHeading.Margin = new Thickness(4, 4, 4, 6);

        add.HorizontalAlignment = HorizontalAlignment.Left;
        var stagesCard = Ui.Card(Ui.VStack(8,
            Ui.Overline("Stages"),
            stageSearch, stageList, Ui.Columns(8, [1, 0], actions, add),
            Ui.Divider().Margin(0, 4, 0, 4),
            outlineHeading,
            outlineList), 12, "SurfaceLow", 16);
        var startersCard = Ui.Card(Ui.VStack(10,
            Ui.Wrap(0, Ui.Overline("Start from"), Ui.InfoButton("Starters", "A starter builds a complete draft for a common stack. Hover a starter to see exactly what it creates.", "Node.js: deps, build and runtime stages with npm cache mounts, USER node and EXPOSE 8080.")),
            starters,
            Ui.Caption("A starter replaces the draft. The previous draft stays in local history (Activity) and can be restored.")), 16, "SurfaceLow", 16);
        return Ui.Scroll(Ui.VStack(12, BuildDetectSection(), stagesCard, startersCard));
    }

    // ----------------------------------------------------------------- outline of the selected stage

    private readonly ListBox outlineList = new();
    private readonly TextBlock outlineHeading = Ui.Text("", 12, FontWeights.SemiBold, "OnSurfaceVariant", false, false);
    private bool syncingOutline;

    private void RefreshOutline()
    {
        syncingOutline = true;
        try
        {
            outlineList.Items.Clear();
            if (doc.Stages.Count == 0) { outlineHeading.Text = Loc.T("No stages yet"); return; }
            var stage = doc.Stages[selectedStage];
            outlineHeading.Text = Loc.T("Stage") + " " + (stage.Name.Trim().Length > 0 ? stage.Name.Trim() : selectedStage.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var row in DockerfileOutline.Rows(stage))
            {
                var op = Ui.Mono(row.Keyword, 12, "Tertiary");
                op.FontWeight = FontWeights.SemiBold;
                var arg = Ui.Mono(row.Argument, 12, "OnSurface");
                arg.ToolTip = row.Argument;
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.Children.Add(op);
                grid.Children.Add(arg.At(0, 1));
                var item = new ListBoxItem { Content = grid, Tag = row.InstructionIndex, MinHeight = 36, Padding = new Thickness(10, 0, 10, 0) };
                System.Windows.Automation.AutomationProperties.SetName(item, row.Keyword + " " + row.Argument);
                outlineList.Items.Add(item);
                var on = row.InstructionIndex >= 0 && row.InstructionIndex < stage.Instructions.Count && stage.Instructions[row.InstructionIndex] == expanded;
                if (on) outlineList.SelectedItem = item;
            }
        }
        finally { syncingOutline = false; }
    }

    private void RefreshStageList()
    {
        syncingStageList = true;
        try
        {
            stageList.Items.Clear();
            var target = DockerfileModel.TargetStageIndex(doc);
            for (var i = 0; i < doc.Stages.Count; i++)
            {
                var stage = doc.Stages[i];
                var name = stage.Name.Trim().Length > 0 ? stage.Name.Trim() : $"stage {i}";
                var baseText = DockerfileLintHelpers.IsStageReference(doc, i) ? Loc.T("from") + " " + stage.BaseImage.Trim() : stage.BaseImage.Trim();
                var meta = $"{baseText} · {stage.Instructions.Count} {Loc.T(stage.Instructions.Count == 1 ? "step" : "steps")}" + (i == target ? " · " + Loc.T("target") : "");
                if (!stageSearch.Matches(name + " " + meta)) continue;
                var content = Ui.Columns(10, [0, 1], Ui.Icon(Icons.DragIndicator, 18, false, i == target ? "Primary" : "OnSurfaceVariant"),
                    Ui.VStack(0, Ui.Text(name, 14, FontWeights.SemiBold, "OnSurface", false, false), Ui.Text(meta, 12, null, "OnSurfaceVariant", false, false)));
                var item = new ListBoxItem { Content = content, Tag = i, MinHeight = 52, Padding = new Thickness(10, 0, 10, 0) };
                System.Windows.Automation.AutomationProperties.SetName(item, $"{name}, {meta}");
                stageList.Items.Add(item);
                if (i == selectedStage) stageList.SelectedItem = item;
            }
        }
        finally { syncingStageList = false; }
        UpdateStageButtons();
    }

    private void UpdateStageButtons()
    {
        var has = doc.Stages.Count > 0;
        stageUp!.IsEnabled = has && selectedStage > 0;
        stageDown!.IsEnabled = has && selectedStage < doc.Stages.Count - 1;
        stageDuplicate!.IsEnabled = stageDelete!.IsEnabled = stageTarget!.IsEnabled = has;
        if (has)
        {
            var isTarget = doc.Target.Trim().Length > 0 && string.Equals(doc.Stages[selectedStage].Name.Trim(), doc.Target.Trim(), StringComparison.OrdinalIgnoreCase);
            stageTarget.Content = isTarget ? Icons.PushPin : Icons.Keep;
            stageTarget.ToolTip = Loc.T(isTarget ? "Clear build target (build the final stage)" : "Set as build target");
        }
    }

    private void AddStage()
    {
        FlushCommit();
        var previous = doc.Stages.Count > 0 ? doc.Stages[selectedStage] : null;
        var stage = new DockerfileStage { Name = DockerfileModel.UniqueStageName(doc, doc.Stages.Count == 0 ? "app" : "stage"), BaseImage = previous?.BaseImage ?? "alpine:3.21" };
        var at = doc.Stages.Count == 0 ? 0 : selectedStage + 1;
        doc.Stages.Insert(at, stage);
        selectedStage = at; expanded = null;
        Changed();
        Dispatcher.BeginInvoke(() => { stageName.Focus(); stageName.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void DuplicateStage()
    {
        if (doc.Stages.Count == 0) return;
        FlushCommit();
        var copy = doc.Stages[selectedStage].Clone();
        copy.Name = DockerfileModel.UniqueStageName(doc, copy.Name.Trim().Length > 0 ? copy.Name.Trim() : "stage");
        doc.Stages.Insert(selectedStage + 1, copy);
        selectedStage++; expanded = null;
        Changed();
    }

    private async Task DeleteStageAsync()
    {
        if (doc.Stages.Count == 0) return;
        var stage = doc.Stages[selectedStage];
        var name = stage.Name.Trim().Length > 0 ? stage.Name.Trim() : $"stage {selectedStage}";
        var references = doc.Stages.SelectMany(s => s.Instructions).Count(i => i.Kind == InstructionKind.Copy && string.Equals(i.From.Trim(), stage.Name.Trim(), StringComparison.OrdinalIgnoreCase) && stage.Name.Trim().Length > 0)
            + doc.Stages.Count(s => stage.Name.Trim().Length > 0 && string.Equals(s.BaseImage.Trim(), stage.Name.Trim(), StringComparison.OrdinalIgnoreCase));
        var message = $"{name} ({stage.Instructions.Count} instruction(s)) will be removed from the draft." + (references > 0 ? $" {references} reference(s) to it in other stages will then fail the checks." : "") + " Undo restores it.";
        if (!await Host.Overlay.ConfirmAsync($"Delete stage {name}?", message, "Delete", true)) return;
        FlushCommit();
        doc.Stages.RemoveAt(selectedStage);
        if (string.Equals(doc.Target.Trim(), stage.Name.Trim(), StringComparison.OrdinalIgnoreCase)) doc.Target = "";
        selectedStage = Math.Max(0, selectedStage - 1); expanded = null;
        Changed();
    }

    private void MoveStage(int delta)
    {
        var to = selectedStage + delta;
        if (to < 0 || to >= doc.Stages.Count) return;
        FlushCommit();
        (doc.Stages[selectedStage], doc.Stages[to]) = (doc.Stages[to], doc.Stages[selectedStage]);
        selectedStage = to;
        Changed();
        stageList.Focus();
    }

    private void ToggleTarget()
    {
        if (doc.Stages.Count == 0) return;
        var stage = doc.Stages[selectedStage];
        if (stage.Name.Trim().Length == 0) { Host.Notify.Warning("Name this stage before using it as the build target."); stageName.Focus(); return; }
        FlushCommit();
        doc.Target = string.Equals(doc.Target.Trim(), stage.Name.Trim(), StringComparison.OrdinalIgnoreCase) ? "" : stage.Name.Trim();
        Changed();
        Host.Notify.Info(doc.Target.Length == 0 ? Loc.T("Build target cleared; the final stage is built.") : Loc.T("Build target set to") + " " + doc.Target + ". " + Loc.T("Open in Build passes it on."));
    }
}

/// <summary>Small model queries used by the Creator UI.</summary>
internal static class DockerfileLintHelpers
{
    public static bool IsStageReference(DockerfileDocument doc, int index) =>
        doc.Stages.Take(index).Any(s => s.Name.Trim().Length > 0 && string.Equals(s.Name.Trim(), doc.Stages[index].BaseImage.Trim(), StringComparison.OrdinalIgnoreCase));
}
