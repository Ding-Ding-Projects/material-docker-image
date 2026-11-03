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
    private readonly WrapPanel groupChips = new();
    private readonly SearchFilter targetSearch = new("Search targets", 260);
    private readonly StackPanel targetList = new();
    private readonly TextBlock listNote = Ui.Caption("");
    private readonly TextBlock selectionText = Ui.Caption("");
    private readonly BakeTargetDetail detail = new();
    private readonly BuildKeyValueRows overrides;
    private readonly BuildTimelineView timeline;
    private readonly BuildRunner runner;
    private readonly TabControl tabs = new();
    private readonly TabItem definitionTab, runTab;
    private readonly Button buildSelected, buildGroup, printPlan;
    private BakeOutput output = BakeOutput.Load;

    private enum BakeOutput { Load, Push, AsDefined }

    public BakePage()
    {
        var openFolder = Ui.Button("Open folder", Icons.FolderOpen, ButtonKind.Outlined, () => PageTasks.Fire(ChooseFolderAsync));
        var openFile = Ui.Button("Open bake file", Icons.Description, ButtonKind.Outlined, () => PageTasks.Fire(ChooseFileAsync));
        printPlan = Ui.AsyncButton("Print plan", Icons.Refresh, ButtonKind.Tonal, LoadPlanAsync);
        printPlan.ToolTip = Loc.T("Runs docker buildx bake --print to resolve every group and target again. Nothing is built.");
        var header = Ui.Header("Bake", "Bake files: list, inspect and build targets", out subtitle, openFolder, openFile, printPlan);

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

        // Left: source, groups and targets
        targetSearch.Changed += RenderTargets;
        var selectAll = Ui.Button("Select shown", Icons.SelectAll, ButtonKind.Text, () => SetShownChecked(true));
        var clear = Ui.Button("Clear", Icons.Deselect, ButtonKind.Text, () => SetShownChecked(false));
        var targetsHead = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        targetsHead.Children.Add(Ui.HStack(4, selectAll, clear).Dock(System.Windows.Controls.Dock.Right));
        targetsHead.Children.Add(targetSearch);
        var left = Ui.VStack(14,
            Ui.VStack(6, Ui.Overline("Files bake reads"), fileChips, sourceCaption),
            Ui.VStack(6, Ui.Field("Groups", groupChips, "Groups from the bake file. Choose one to highlight its targets and enable Build group.", "group \"default\" { targets = [\"app\", \"worker\"] }")),
            Ui.VStack(6, Ui.Overline("Targets"), targetsHead, listNote, targetList));
        var leftCard = Ui.Card(Ui.Scroll(left), 16, "SurfaceLow", 16);

        // Right: options, actions, definition and run
        overrides = new BuildKeyValueRows(new List<NameValue>(), "target.key", "Value", "Add override", BakePlan.ValidateSetKey, () => { });
        var outputChoice = Ui.Segmented(OutputNames, OutputNames[0], option => output = (BakeOutput)Array.IndexOf(OutputNames, option));
        buildSelected = Ui.AsyncButton("Build selected", Icons.Construction, ButtonKind.Filled, BuildSelectedAsync);
        buildGroup = Ui.AsyncButton("Build group", Icons.Workspaces, ButtonKind.Tonal, BuildGroupAsync);
        var actions = Ui.Wrap(8, buildSelected, buildGroup);
        var optionsBody = Ui.VStack(12,
            Ui.Field("Destination", outputChoice, "Load locally adds --load so images appear in this engine (one platform per target). Push adds --push and asks first. As defined keeps the outputs written in the bake file.", "--load"),
            new Expander
            {
                Header = Loc.T("Overrides (--set)"),
                Content = Ui.Field("Overrides", overrides, "Overrides a target attribute for this run only (--set target.key=value). Use * for every target. Keys include args.NAME, platform, tags, target, no-cache, cache-from and output.", "app.platform = linux/arm64\n*.args.VERSION = 1.4.0\napp.tags = registry.example.com/team/app:dev")
            },
            Ui.Columns(12, [1, 0], selectionText, actions));
        var optionsCard = Ui.Card(optionsBody, 16, "SurfaceContainer", 16);

        timeline = new BuildTimelineView("Build a target or group to see each BuildKit step with its status, cache use and duration.");
        runner = new BuildRunner(timeline);
        definitionTab = new TabItem { Header = Loc.T("Definition"), Content = detail };
        runTab = new TabItem { Header = Loc.T("Build run"), Content = timeline };
        tabs.Items.Add(definitionTab); tabs.Items.Add(runTab);
        var right = new DockPanel();
        right.Children.Add(optionsCard.Dock(System.Windows.Controls.Dock.Top).Margin(0, 0, 0, 12));
        right.Children.Add(Ui.Card(tabs, 16, "SurfaceLow", 16));

        workspace = Ui.SplitInspector(leftCard, right, 1100, 560, 16);
        root.Children.Add(emptyState);
        root.Children.Add(workspace);
        Layout(header, root);
        BuildRunner.ActiveChanged += _ => UpdateActions();
        ShowEmpty("No bake file chosen", "Choose a folder that contains docker-bake.hcl, docker-bake.json or compose.yaml, or open a bake file directly. Bake resolves every group and target without building anything until you ask.");
        UpdateActions();
    }

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
