using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Bake destination: choose a folder or bake file (HCL, JSON or Compose), list its groups and targets from
/// <c>docker buildx bake --print</c>, inspect each resolved target and build selected targets or a group with
/// <c>--set</c> overrides and a load/push choice. Runs share the Build page's runner and timeline view.
/// Plan loading is in Pages/Bake/BakePage.Plan.cs and building in Pages/Bake/BakePage.Run.cs.
/// </summary>
public sealed partial class BakePage : PageBase
{
    public override string Key => "Bake";
    private static readonly string[] OutputNames = ["Load locally", "Push", "As defined"];

    private readonly TextBlock subtitle;
    private readonly Grid root = new();
    private readonly FrameworkElement emptyState, workspace;
    private readonly TextBlock emptyTitle = Ui.Text("", 18, FontWeights.SemiBold);
    private readonly TextBlock emptyMessage = Ui.Text("", 14, null, "OnSurfaceVariant");
    private readonly WrapPanel fileChips = new();
    private readonly TextBlock sourceCaption = Ui.Caption("");
    private readonly FrameworkElement sourceRow;
    private readonly TextBox pathBox, overrideBox;
    private readonly StackPanel groupList = new();
    private readonly SearchFilter targetSearch = new("Search targets", 260);
    private readonly StackPanel targetList = new();
    private readonly TextBlock listNote = Ui.Caption("");
    private readonly TextBlock selectionText = Ui.Caption("");
    private readonly BakeTargetDetail detail;
    private readonly Border outputChoice;
    private readonly Grid split = new();
    private readonly Border leftCard, rightCard;
    private readonly BuildTimelineView timeline;
    private readonly BuildRunner runner;
    private readonly Button buildSelected, buildGroup, printPlan;
    private BakeOutput output = BakeOutput.Load;
    private string? splitMode;
    private List<(string Key, string Value)> lastOverrides = [];

    private enum BakeOutput { Load, Push, AsDefined }

    public BakePage()
    {
        printPlan = Ui.AsyncButton("Print plan", Icons.Description, ButtonKind.Outlined, LoadPlanAsync);
        printPlan.ToolTip = Loc.T("Runs docker buildx bake --print to resolve every group and target again. Nothing is built.");
        buildGroup = Ui.AsyncButton("Build group", Icons.DoneAll, ButtonKind.Tonal, BuildGroupAsync);
        buildSelected = Ui.AsyncButton("Build selected", Icons.Construction, ButtonKind.Filled, BuildSelectedAsync);
        var header = Ui.Header("Bake", "Bake files: list, inspect and build targets", out subtitle, printPlan, buildGroup, buildSelected);

        // Empty state
        var emptyIcon = Ui.Icon(Icons.Workspaces, 40, false, "Outline");
        emptyTitle.TextAlignment = emptyMessage.TextAlignment = TextAlignment.Center;
        var emptyActions = Ui.HStack(8,
            Ui.Button("Open folder", Icons.FolderOpen, ButtonKind.Filled, () => PageTasks.Fire(ChooseFolderAsync)),
            Ui.Button("Open bake file", Icons.Description, ButtonKind.Outlined, () => PageTasks.Fire(ChooseFileAsync)));
        emptyActions.HorizontalAlignment = HorizontalAlignment.Center;
        var emptyStack = Ui.VStack(8, emptyIcon, emptyTitle, emptyMessage, emptyActions.Margin(0, 12, 0, 0));
        emptyStack.MaxWidth = 480; emptyStack.HorizontalAlignment = HorizontalAlignment.Center; emptyStack.VerticalAlignment = VerticalAlignment.Center;
        emptyState = emptyStack;

        // Bake file bar: path, --set overrides and the browse, reload and open-in-editor icon buttons.
        pathBox = Ui.Input("", "Bake file", true);
        pathBox.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; PageTasks.Fire(ApplyPathAsync); } };
        overrideBox = Ui.Input("", "Overrides (--set)", true);
        overrideBox.ToolTip = Loc.T("target.key=value, separated by spaces. Quote a value that contains spaces.");
        var pathField = Ui.Field("Bake file", pathBox, "A bake file (HCL, JSON or Compose) or a folder that holds docker-bake.hcl, docker-bake.json or compose.yaml. Press Enter to load it.", "C:\\Projects\\app\\docker-bake.hcl");
        var overrideField = Ui.Field("Overrides (--set)", overrideBox, "Overrides a target attribute for this run only (--set target.key=value), separated by spaces. Use * for every target. Keys include args.NAME, platform, tags, target, no-cache, cache-from and output. Quote a value that contains spaces.", "*.args.VERSION=1.4.0 app.platform=linux/arm64");
        var browse = Ui.IconButton(Icons.FolderOpen, "Browse for bake file");
        var browseMenu = new ContextMenu();
        browseMenu.Items.Add(Ui.MenuItem("Open bake file", Icons.Description, () => PageTasks.Fire(ChooseFileAsync)));
        browseMenu.Items.Add(Ui.MenuItem("Open folder", Icons.FolderOpen, () => PageTasks.Fire(ChooseFolderAsync)));
        browse.Click += (_, _) => { browseMenu.PlacementTarget = browse; browseMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; browseMenu.IsOpen = true; };
        var reload = Ui.AsyncIconButton(Icons.Refresh, "Reload bake file", LoadPlanAsync);
        var openEditor = Ui.AsyncIconButton(Icons.OpenInNew, "Open bake file in editor", OpenInEditorAsync);
        var barButtons = Ui.HStack(8, browse, reload, openEditor);
        foreach (var b in new[] { browse, reload, openEditor }) b.SetResourceReference(Control.BackgroundProperty, "SurfaceContainer");
        barButtons.VerticalAlignment = VerticalAlignment.Bottom;
        var bar = Ui.Columns(8, [3, 1, 0], pathField, overrideField, barButtons);
        sourceRow = Ui.Wrap(8, Ui.Overline("Files bake reads"), fileChips, sourceCaption);
        sourceRow.Visibility = Visibility.Collapsed;
        var barCard = Ui.Card(Ui.VStack(8, bar, sourceRow), 16, "SurfaceLow", 16);
        barCard.Padding = new Thickness(16, 12, 16, 12);

        // Left: groups, targets and the destination
        targetSearch.Width = double.NaN;
        targetSearch.Changed += RenderTargets;
        var selectAll = Ui.IconButton(Icons.SelectAll, "Select shown targets", () => SetShownChecked(true));
        var clear = Ui.IconButton(Icons.Deselect, "Clear shown targets", () => SetShownChecked(false));
        var targetsHead = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        targetsHead.Children.Add(Ui.HStack(4, selectAll, clear).Dock(System.Windows.Controls.Dock.Right));
        targetsHead.Children.Add(targetSearch);
        var left = Ui.VStack(14,
            Ui.Field("Groups", groupList, "Groups from the bake file. Click a group to highlight its targets and enable Build group; its own Build button builds just that group.", "group \"default\" { targets = [\"app\", \"worker\"] }"),
            Ui.VStack(8, Ui.Overline("Targets"), targetsHead, listNote, targetList));
        outputChoice = Ui.Segmented(OutputNames, OutputNames[0], option => { output = (BakeOutput)Array.IndexOf(OutputNames, option); UpdateNotice(); });
        var destination = Ui.VStack(6, Ui.Field("Destination", outputChoice, "Load locally adds --load so images appear in this engine (one platform per target). Push adds --push and asks first. As defined keeps the outputs written in the bake file.", "--load"), selectionText);
        leftCard = Ui.Card(Ui.Rows(12, [1, 0], Ui.Scroll(left), destination), 16, "SurfaceLow", 16);

        // Right: the inspector with its Resolved, HCL source and Build run views
        timeline = new BuildTimelineView("Build a target or group to see each BuildKit step with its status, cache use and duration.");
        runner = new BuildRunner(timeline);
        detail = new BakeTargetDetail(timeline) { HclSource = target => BakeHclSource.Read(BakeFilePaths(), target.Name) };
        detail.LoadLocallyRequested += () => SelectOutput(BakeOutput.Load);
        rightCard = Ui.Card(detail, 0, "SurfaceLow", 16);

        split.Children.Add(leftCard);
        split.Children.Add(rightCard);
        split.SizeChanged += (_, _) => ArrangeSplit();
        ArrangeSplit();
        workspace = Ui.Rows(12, [0, 1], barCard, split);
        root.Children.Add(emptyState);
        root.Children.Add(workspace);
        Layout(header, root);
        BuildRunner.ActiveChanged += _ => UpdateActions();
        ShowEmpty("No bake file chosen", "Choose a folder that contains docker-bake.hcl, docker-bake.json or compose.yaml, or open a bake file directly. Bake resolves every group and target without building anything until you ask.");
        UpdateActions();
    }

    /// <summary>Groups and targets sit in a 460 px column beside the inspector; narrow pages stack them.</summary>
    private void ArrangeSplit()
    {
        var wide = split.ActualWidth <= 0 || split.ActualWidth >= 1100;
        var mode = wide ? "wide" : "stacked";
        if (mode == splitMode) return;
        splitMode = mode;
        split.ColumnDefinitions.Clear();
        split.RowDefinitions.Clear();
        if (wide)
        {
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(460) });
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            leftCard.At(0, 0); rightCard.At(0, 2);
        }
        else
        {
            split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 280 });
            split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
            split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 260 });
            leftCard.At(0, 0); rightCard.At(2, 0);
        }
    }

    private void SelectOutput(BakeOutput value)
    {
        if (outputChoice.Child is Panel panel && panel.Children.OfType<RadioButton>().ElementAtOrDefault((int)value) is { } radio) radio.IsChecked = true;
    }

    /// <summary>Warns when the chosen destination or the shown target's own output pushes to a registry.</summary>
    private void UpdateNotice()
    {
        string? text = null;
        var focused = plan?.Targets.FirstOrDefault(t => t.Name == focusedTarget);
        if (output == BakeOutput.Push)
            text = Loc.T("Destination Push sends the built targets to their registries. Sign in to each registry first, or switch to load locally.");
        else if (output == BakeOutput.AsDefined && focused != null && focused.Outputs.Any(o => o.Contains("type=registry", StringComparison.OrdinalIgnoreCase)))
        {
            var host = RegistryHost(focused);
            text = Loc.T("Output type=registry pushes on build.") + " " + (host != null ? Loc.T("Sign in to") + " " + host + " " + Loc.T("first, or switch output to load locally.") : Loc.T("Sign in to the registry first, or switch output to load locally."));
        }
        detail.SetNotice(text);
    }

    private static string? RegistryHost(BakeTarget target)
    {
        foreach (var tag in target.Tags)
        {
            var slash = tag.IndexOf('/');
            if (slash > 0 && (tag[..slash].Contains('.') || tag[..slash].Contains(':'))) return tag[..slash];
        }
        return null;
    }

    private IEnumerable<string> BakeFilePaths() => bakeFile.Length > 0 ? [bakeFile] : DefaultFilesIn(folder).Select(name => Path.Combine(folder, name));

    public override IEnumerable<AppCommand> PageCommands() =>
    [
        new() { Id = "bake.folder", Title = "Open bake folder…", Group = "Bake", Icon = Icons.FolderOpen, Keywords = "docker-bake hcl compose folder", Run = () => { Host.Navigate("Bake"); PageTasks.Fire(ChooseFolderAsync); } },
        new() { Id = "bake.file", Title = "Open bake file…", Group = "Bake", Icon = Icons.Description, Keywords = "docker-bake.hcl json compose.yaml", Run = () => { Host.Navigate("Bake"); PageTasks.Fire(ChooseFileAsync); } },
        new() { Id = "bake.print", Title = "Print bake plan", Group = "Bake", Icon = Icons.Refresh, Keywords = "bake --print resolve targets groups", Run = () => { Host.Navigate("Bake"); PageTasks.Fire(LoadPlanAsync); } },
        new() { Id = "bake.build", Title = "Bake selected targets", Group = "Bake", Icon = Icons.Construction, Keywords = "bake build targets", Run = () => { Host.Navigate("Bake"); PageTasks.Fire(BuildSelectedAsync); } },
        new() { Id = "bake.cancel", Title = "Cancel running bake", Group = "Bake", Icon = Icons.Stop, Keywords = "stop abort bake", Run = () => { if (runner.Running) timeline.Cancel(); else Host.Notify.Info("No bake is running."); } },
    ];

    private void ShowEmpty(string title, string message)
    {
        emptyTitle.Text = Loc.T(title);
        emptyMessage.Text = Loc.T(message);
        emptyState.Visibility = Visibility.Visible;
        workspace.Visibility = Visibility.Collapsed;
    }

    private void ShowWorkspace()
    {
        emptyState.Visibility = Visibility.Collapsed;
        workspace.Visibility = Visibility.Visible;
    }
}
