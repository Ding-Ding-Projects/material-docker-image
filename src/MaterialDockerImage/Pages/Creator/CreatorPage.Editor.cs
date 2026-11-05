using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Centre column: stage fields, instruction cards with inline forms, the Add instruction menu and quick-add chips.</summary>
public sealed partial class CreatorPage
{
    public static readonly string[] CommonImages =
    [
        "alpine:3.21", "debian:bookworm-slim", "ubuntu:24.04", "node:22-alpine", "node:22-slim", "python:3.13-slim", "python:3.13-alpine",
        "golang:1.24-alpine", "rust:1-slim", "eclipse-temurin:21-jre", "maven:3-eclipse-temurin-21", "mcr.microsoft.com/dotnet/sdk:9.0",
        "mcr.microsoft.com/dotnet/aspnet:9.0", "nginxinc/nginx-unprivileged:1.27-alpine", "nginx:1.27-alpine", "gcr.io/distroless/static-debian12:nonroot",
        "gcr.io/distroless/base-debian12:nonroot", "scratch",
    ];
    private static readonly string[] Platforms = ["", "linux/amd64", "linux/arm64", "linux/arm/v7", "$BUILDPLATFORM", "$TARGETPLATFORM"];

    private static readonly (InstructionKind Kind, string Description)[] Catalog =
    [
        (InstructionKind.Run, "Run a command while building (supports cache and secret mounts)"),
        (InstructionKind.Copy, "Copy files from the context or another stage"),
        (InstructionKind.Workdir, "Set the working directory"),
        (InstructionKind.Env, "Set environment variables"),
        (InstructionKind.Arg, "Declare a build argument"),
        (InstructionKind.Expose, "Document a listening port"),
        (InstructionKind.User, "Switch to a user"),
        (InstructionKind.Cmd, "Default command for the container"),
        (InstructionKind.Entrypoint, "Program the container always runs"),
        (InstructionKind.Healthcheck, "Tell the engine how to check readiness"),
        (InstructionKind.Label, "Add image metadata"),
        (InstructionKind.Volume, "Declare data mount points"),
        (InstructionKind.Add, "Add files, URLs or archives"),
        (InstructionKind.StopSignal, "Signal used to stop the container"),
        (InstructionKind.Shell, "Change the shell for shell-form instructions"),
        (InstructionKind.OnBuild, "Instruction for images built FROM this one"),
        (InstructionKind.Comment, "A comment line"),
        (InstructionKind.Raw, "Any instruction written by hand"),
    ];

    private readonly ComboBox baseImage = Ui.Combo(CommonImages, null, true);
    private readonly TextBox stageName = Ui.Input("", "Stage name", true);
    private readonly ComboBox stagePlatform = Ui.Combo(Platforms, "", true);
    private readonly TextBlock stageProblems = Ui.Text("", 13, null, "Error");
    private readonly StackPanel cards = new();
    private readonly StackPanel preambleCards = new();
    private FrameworkElement preambleSection = null!;
    private FrameworkElement stageFields = null!;
    private FrameworkElement noStage = null!;
    private ScrollViewer editorScroll = null!;
    private readonly List<(DockerInstruction Instruction, TextBlock Summary, TextBlock Hint, Border Card)> cardViews = new();
    private bool syncingStage;
    private Button? addInstruction;
    private Border footer = null!;
    private readonly TextBox checkDirective = Ui.Input("", "Check directive", true);
    private readonly TextBox escapeDirective = Ui.Input("", "Escape directive", true);
    private FrameworkElement escapeField = null!;

    private UIElement BuildEditorColumn()
    {
        foreach (var c in new[] { baseImage, stagePlatform }) c.SetResourceReference(Control.FontFamilyProperty, "MonoFont");
        baseImage.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => StageFieldChanged()));
        baseImage.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(StageFieldChanged);
        stagePlatform.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => StageFieldChanged()));
        stagePlatform.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(StageFieldChanged);
        stagePlatform.ToolTip = Loc.T("Empty means any: the build platform decides.");
        stageName.TextChanged += (_, _) => StageFieldChanged();

        var crossCompile = Ui.Button("Build on BUILDPLATFORM", Icons.AltRoute, ButtonKind.Text, UseBuildPlatform);
        crossCompile.ToolTip = Loc.T("Cross-compilation helper: sets FROM --platform=$BUILDPLATFORM so the compiler runs natively, and declares ARG TARGETOS and ARG TARGETARCH in this stage for commands such as GOOS=$TARGETOS GOARCH=$TARGETARCH.");
        crossCompile.HorizontalAlignment = HorizontalAlignment.Left;
        stageFields = Ui.VStack(12,
            Ui.Field("Base image", baseImage, "The image this stage starts from, or the name of an earlier stage. Pin a version tag or digest for repeatable builds.", "node:22-alpine\nnode:22-alpine@sha256:…\ndeps"),
            Ui.Columns(12, [1, 1],
                Ui.Field("Stage name", stageName, "Name used by COPY --from, later FROM lines and the build target. Letters, digits, '.', '_' and '-'.", "runtime"),
                Ui.Field("Platform", stagePlatform, "Optional FROM --platform. Leave empty for any; $BUILDPLATFORM runs build tools natively during cross-builds.", "$BUILDPLATFORM")),
            crossCompile,
            stageProblems);
        stageProblems.Visibility = Visibility.Collapsed;

        preambleSection = BuildGlobalCard();

        addInstruction = Ui.Button("Add instruction", Icons.Add, ButtonKind.Tonal, () => OpenAddMenu(addInstruction!));
        addInstruction.Height = 36; addInstruction.MinHeight = 36; addInstruction.Padding = new Thickness(14, 0, 14, 0);
        addInstruction.ToolTip = Loc.T("Choose an instruction to add after the open card, or at the end.");
        quickChips = Ui.Wrap(6,
            QuickChip("RUN with cache mount", () => new DockerInstruction { Kind = InstructionKind.Run, Command = "echo build", Mounts = { new RunMount { Type = RunMounts.Cache, Target = "/root/.cache" } } }, false),
            QuickChip("RUN heredoc script", () => new DockerInstruction { Kind = InstructionKind.Run, Heredoc = true, HeredocDelimiter = "EOF", HeredocBody = "set -e\necho build" }, false),
            QuickChip("HEALTHCHECK", () => DockerfileModel.Create(InstructionKind.Healthcheck), true),
            QuickChip("Non-root USER", () => new DockerInstruction { Kind = InstructionKind.User, Value = "10001" }, true),
            QuickChip("EXPOSE", () => DockerfileModel.Create(InstructionKind.Expose), true));
        var addRow = Ui.Wrap(8, addInstruction, quickChips);
        footer = new Border { Child = addRow, Padding = new Thickness(0, 10, 0, 0), BorderThickness = new Thickness(0, 1, 0, 0) };
        footer.SetResourceReference(Border.BorderBrushProperty, "SurfaceHigh");

        noStage = Ui.EmptyState(Icons.Layers, "No stages yet", "Pick a starter on the left, open an existing Dockerfile, or add a stage.",
            Ui.Button("Add stage", Icons.Add, ButtonKind.Filled, AddStage));
        noStage.Visibility = Visibility.Collapsed;

        var stack = Ui.VStack(14, preambleSection, stageFields, Ui.Divider(), cards);
        var grid = new Grid();
        editorScroll = Ui.Scroll(stack);
        focusHost.Visibility = Visibility.Collapsed;
        var body = new Grid();
        body.Children.Add(editorScroll);
        body.Children.Add(focusHost);
        var host = new DockPanel();
        host.Children.Add(footer.Dock(System.Windows.Controls.Dock.Bottom));
        host.Children.Add(body);
        grid.Children.Add(host);
        grid.Children.Add(noStage);
        return grid;
    }

    private System.Windows.Controls.Primitives.ToggleButton QuickChip(string label, Func<DockerInstruction> create, bool beforeStart) =>
        CreatorChips.Action(label, () =>
        {
            if (doc.Stages.Count == 0) { Host.Notify.Warning("Add a stage first."); return; }
            FlushCommit();
            var instruction = create();
            var stage = doc.Stages[selectedStage];
            if (beforeStart) DockerfileLint.InsertBeforeStart(stage, instruction); else InsertAfterExpanded(stage, instruction);
            expanded = instruction;
            Changed();
        }, 36, false);

    private void InsertAfterExpanded(DockerfileStage stage, DockerInstruction instruction)
    {
        var at = expanded == null ? -1 : stage.Instructions.IndexOf(expanded);
        if (at < 0)
        {
            // Default position: before the start instructions so new build steps do not land after CMD.
            var start = stage.Instructions.FindIndex(x => x.Kind is InstructionKind.Cmd or InstructionKind.Entrypoint or InstructionKind.Healthcheck);
            if (start < 0 || instruction.Kind is InstructionKind.Cmd or InstructionKind.Entrypoint) stage.Instructions.Add(instruction); else stage.Instructions.Insert(start, instruction);
        }
        else stage.Instructions.Insert(at + 1, instruction);
    }

    private void OpenAddMenu(FrameworkElement anchor)
    {
        if (doc.Stages.Count == 0) { Host.Notify.Warning("Add a stage first."); return; }
        var search = new SearchFilter("Search instructions", 300);
        var list = new ListBox { MaxHeight = 360 };
        System.Windows.Automation.AutomationProperties.SetName(list, Loc.T("Instructions"));
        System.Windows.Controls.Primitives.Popup? popup = null;
        void Fill()
        {
            list.Items.Clear();
            foreach (var (kind, description) in Catalog)
            {
                var keyword = DockerfileModel.Keyword(kind);
                if (!search.Matches(keyword + " " + description)) continue;
                var item = new ListBoxItem { Tag = kind, Content = Ui.VStack(0, Ui.Mono(keyword == "#" ? "# comment" : keyword == "RAW" ? "Raw instruction" : keyword, 13, "Primary"), Ui.Text(description, 12, null, "OnSurfaceVariant", true)) };
                System.Windows.Automation.AutomationProperties.SetName(item, keyword + ". " + description);
                list.Items.Add(item);
            }
            if (list.Items.Count > 0) list.SelectedIndex = 0;
        }
        void Pick()
        {
            if (list.SelectedItem is not ListBoxItem { Tag: InstructionKind kind }) return;
            popup!.IsOpen = false;
            FlushCommit();
            var instruction = DockerfileModel.Create(kind);
            InsertAfterExpanded(doc.Stages[selectedStage], instruction);
            expanded = instruction;
            Changed();
        }
        search.Changed += Fill;
        search.Box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Down && list.Items.Count > 0) { list.SelectedIndex = Math.Min(list.Items.Count - 1, list.SelectedIndex + 1); e.Handled = true; }
            else if (e.Key == System.Windows.Input.Key.Up && list.Items.Count > 0) { list.SelectedIndex = Math.Max(0, list.SelectedIndex - 1); e.Handled = true; }
            else if (e.Key == System.Windows.Input.Key.Enter) { Pick(); e.Handled = true; }
        };
        list.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { Pick(); e.Handled = true; } };
        list.PreviewMouseLeftButtonUp += (_, e) => { if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(list, d) is ListBoxItem) Pick(); };
        Fill();
        popup = Ui.ShowPopup(anchor, Ui.VStack(10, Ui.Text("Add instruction", 16, FontWeights.SemiBold), search, list), 360);
        search.Box.Dispatcher.BeginInvoke(() => search.Box.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    // ----------------------------------------------------------------- global arguments and directives

    /// <summary>The "Build arguments and platform" card: global ARGs before the first FROM and the parser directives.</summary>
    private FrameworkElement BuildGlobalCard()
    {
        var addArg = Ui.Button("Add ARG", Icons.Add, ButtonKind.Tonal, () =>
        {
            FlushCommit();
            var arg = DockerfileModel.Create(InstructionKind.Arg);
            arg.Name = UniqueArgName("VERSION");
            doc.Preamble.Add(arg);
            expanded = arg;
            Changed();
        });
        addArg.ToolTip = Loc.T("Global build argument. It can be used in FROM lines; declare it again (ARG NAME) inside a stage to use it in that stage's commands.");
        var platformArgs = Ui.Button("Add TARGETPLATFORM and BUILDPLATFORM", Icons.DeviceHub, ButtonKind.Outlined, () => AddPlatformArgs("TARGETPLATFORM", "BUILDPLATFORM"));
        platformArgs.ToolTip = Loc.T("BuildKit sets these automatically; declaring them documents that the Dockerfile is multi-platform aware.");
        var more = new WrapPanel();
        foreach (var (name, meaning) in DockerfilePlatform.AutomaticArgs.Where(a => a.Name is "TARGETOS" or "TARGETARCH" or "TARGETVARIANT" or "BUILDARCH"))
        {
            var b = Ui.Button(name, Icons.Add, ButtonKind.Text, () => AddPlatformArgs(name), false);
            b.ToolTip = meaning; b.MinHeight = 32; b.Height = 32; b.Padding = new Thickness(8, 0, 10, 0); b.Margin = new Thickness(0, 0, 4, 4);
            more.Children.Add(b);
        }
        checkDirective.TextChanged += (_, _) => { if (syncingStage) return; if (doc.CheckDirective == checkDirective.Text) return; doc.CheckDirective = checkDirective.Text; Touched(); };
        escapeDirective.TextChanged += (_, _) => { if (syncingStage) return; if (doc.Escape == escapeDirective.Text) return; doc.Escape = escapeDirective.Text; Touched(); };
        escapeField = Ui.Field("Escape character", escapeDirective, "# escape= changes the escape and line continuation character. The Creator writes backslash continuations, so clear this unless the file targets Windows paths.", "`");
        var stack = Ui.VStack(10,
            Ui.Columns(10, [0, 1], Ui.Icon(Icons.Tune, 20, true, "Primary"), Ui.Text("Build arguments and platform", 15, FontWeights.SemiBold)),
            Ui.Caption("Global ARGs come before the first FROM. Use them in FROM lines, for example FROM node:${NODE_VERSION}-alpine, and pass values with --build-arg."),
            preambleCards,
            Ui.Wrap(8, addArg, platformArgs),
            more,
            Ui.Field("Check directive", checkDirective, "# check= controls the build checks BuildKit runs: skip=<rules> to skip some, error=true to fail the build on warnings.", "skip=JSONArgsRecommended;error=true"),
            escapeField);
        return Ui.Card(stack, 14, "SurfaceContainer", 14);
    }

    private string UniqueArgName(string baseName)
    {
        var name = baseName; var n = 2;
        while (doc.Preamble.Any(i => i.Kind == InstructionKind.Arg && i.Name == name)) name = baseName + "_" + n++;
        return name;
    }

    private void AddPlatformArgs(params string[] names)
    {
        FlushCommit();
        var added = DockerfilePlatform.AddGlobalArgs(doc, names);
        if (added.Count == 0) { Host.Notify.Info(Loc.T("Already declared:") + " " + string.Join(", ", names)); return; }
        Changed();
        Host.Notify.Info(Loc.T("Added global") + " " + string.Join(", ", added.Select(a => "ARG " + a)) + ". " + Loc.T("Declare them again inside a stage to use them in RUN."));
    }

    private void UseBuildPlatform()
    {
        if (doc.Stages.Count == 0) return;
        FlushCommit();
        var message = DockerfilePlatform.UseBuildPlatform(doc, selectedStage);
        Changed();
        Host.Notify.Info(message + " " + Loc.T("Use $TARGETOS and $TARGETARCH in the build command of this stage."));
    }

    // ----------------------------------------------------------------- stage fields

    private void StageFieldChanged()
    {
        if (syncingStage || doc.Stages.Count == 0) return;
        var stage = doc.Stages[selectedStage];
        if (stage.BaseImage == (baseImage.Text ?? "") && stage.Platform == (stagePlatform.Text ?? "") && stage.Name == stageName.Text) return;
        var oldName = stage.Name.Trim();
        stage.BaseImage = baseImage.Text ?? "";
        stage.Platform = stagePlatform.Text ?? "";
        stage.Name = stageName.Text;
        if (oldName.Length > 0 && string.Equals(doc.Target.Trim(), oldName, StringComparison.OrdinalIgnoreCase)) doc.Target = stage.Name.Trim();
        ShowStageProblems();
        Touched();
    }

    private void ShowStageProblems()
    {
        var problems = doc.Stages.Count == 0 ? [] : DockerfileModel.ValidateStage(doc.Stages[selectedStage]);
        stageProblems.Text = string.Join(Environment.NewLine, problems);
        stageProblems.Visibility = problems.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RebuildStageEditor()
    {
        var has = doc.Stages.Count > 0;
        noStage.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        editorScroll.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        footer.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        cards.Children.Clear(); preambleCards.Children.Clear(); cardViews.Clear();
        for (var i = 0; i < doc.Preamble.Count; i++) preambleCards.Children.Add(Card(doc.Preamble, i, -1));
        syncingStage = true;
        try
        {
            checkDirective.Text = doc.CheckDirective;
            escapeDirective.Text = doc.Escape;
            escapeField.Visibility = doc.Escape.Trim().Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { syncingStage = false; }
        if (!has) { expanded = null; focusHost.Children.Clear(); focusHost.Visibility = Visibility.Collapsed; RefreshOutline(); return; }
        var stage = doc.Stages[selectedStage];
        syncingStage = true;
        try
        {
            baseImage.Text = stage.BaseImage;
            stagePlatform.Text = stage.Platform;
            stageName.Text = stage.Name;
        }
        finally { syncingStage = false; }
        ShowStageProblems();
        if (stage.Instructions.Count == 0) cards.Children.Add(Ui.Caption("This stage has no instructions yet. Use Add instruction or a quick-add chip below."));
        for (var i = 0; i < stage.Instructions.Count; i++) cards.Children.Add(Card(stage.Instructions, i, selectedStage));
        ShowFocusedStep();
    }

    /// <summary>
    /// When a step is open, the centre shows only that step's editor (as on the design board); otherwise the stage
    /// overview with the build arguments, stage fields and step cards.
    /// </summary>
    private void ShowFocusedStep()
    {
        focusHint = null;
        focusHost.Children.Clear();
        List<DockerInstruction>? list = null;
        if (expanded != null)
        {
            if (doc.Preamble.Contains(expanded)) list = doc.Preamble;
            else if (doc.Stages.Count > 0 && doc.Stages[selectedStage].Instructions.Contains(expanded)) list = doc.Stages[selectedStage].Instructions;
            else expanded = null;
        }
        var focused = list != null && expanded != null && doc.Stages.Count > 0;
        focusHost.Visibility = focused ? Visibility.Visible : Visibility.Collapsed;
        editorScroll.Visibility = doc.Stages.Count > 0 && !focused ? Visibility.Visible : Visibility.Collapsed;
        quickChips.Visibility = focused ? Visibility.Collapsed : Visibility.Visible;
        if (focused) focusHost.Children.Add(BuildFocused(list!, expanded!));
        RefreshOutline();
    }

    private void UpdateCardSummaries()
    {
        if (focusHint is { } hintView && expanded != null)
        {
            var list = doc.Preamble.Contains(expanded) ? doc.Preamble : doc.Stages.Count > 0 ? doc.Stages[selectedStage].Instructions : null;
            if (list != null) hintView.Text = StepLine(list, expanded);
        }
        RefreshOutline();
        foreach (var (instruction, summary, hint, card) in cardViews)
        {
            summary.Text = DockerfileModel.Summary(instruction);
            hint.Text = Loc.T(DockerfileModel.Hint(instruction));
            var invalid = DockerfileModel.Validate(instruction).Count > 0;
            var marked = expanded == instruction || highlighted == instruction;
            card.SetResourceReference(Border.BorderBrushProperty, invalid ? "Error" : marked ? "Primary" : "OutlineVariant");
            card.BorderThickness = new Thickness(marked ? 2 : invalid ? 1 : 0);
            summary.ToolTip = summary.Text;
        }
    }

    /// <summary>Earlier stage names a COPY in the selected stage may reference.</summary>
    private IReadOnlyList<string> EarlierStageNames() => doc.Stages.Take(selectedStage).Select(s => s.Name.Trim()).Where(n => n.Length > 0).ToList();

    private Border Card(List<DockerInstruction> list, int index, int stageIndex)
    {
        var instruction = list[index];
        var keyword = DockerfileModel.Keyword(instruction.Kind);
        var chip = KeywordPill(keyword, instruction.Kind is InstructionKind.Comment or InstructionKind.Raw);
        chip.VerticalAlignment = VerticalAlignment.Top;
        var summary = Ui.Mono(DockerfileModel.Summary(instruction), 13, "OnSurface", false);
        summary.ToolTip = summary.Text;
        var hint = Ui.Caption(DockerfileModel.Hint(instruction));
        StackPanel text;
        if (DockerfileStepNames.HasName(instruction))
        {
            var name = Ui.Text(DockerfileStepNames.Clean(instruction.StepName), 14, FontWeights.SemiBold, "OnSurface", false, false);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.ToolTip = name.Text;
            text = Ui.VStack(2, name, summary, hint);
        }
        else text = Ui.VStack(2, summary, hint);

        // One overflow button holds every step action (also on the keyboard and the context menu).
        var more = Ui.IconButton(Icons.MoreVert, "More step actions");
        more.Width = more.Height = more.MinHeight = 32; more.FontSize = 18;
        more.VerticalAlignment = VerticalAlignment.Top;

        var top = Ui.Columns(12, [0, 1, 0], chip, text, more);
        var card = new Border { Child = top, CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10, 8, 10), Margin = new Thickness(0, 0, 0, 8), Focusable = true, Tag = instruction, Cursor = System.Windows.Input.Cursors.Hand };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceContainer");
        var marked = highlighted == instruction;
        var invalidNow = DockerfileModel.Validate(instruction).Count > 0;
        card.BorderThickness = new Thickness(marked ? 2 : invalidNow ? 1 : 0);
        card.SetResourceReference(Border.BorderBrushProperty, invalidNow ? "Error" : marked ? "Primary" : "OutlineVariant");
        card.GotKeyboardFocus += (_, _) =>
        {
            if (highlighted == instruction) return;
            highlighted = instruction;
            UpdateCardSummaries();
            HighlightLines(instruction);
        };
        card.FocusVisualStyle = (Style)Application.Current.FindResource("FocusRing");
        System.Windows.Automation.AutomationProperties.SetName(card, $"{Loc.T("Step")} {index + 1}: " + (DockerfileStepNames.HasName(instruction) ? DockerfileStepNames.Clean(instruction.StepName) + ", " : "") + $"{keyword} {summary.Text}");
        card.ToolTip = Loc.T("Click or press Enter to edit this step.");
        card.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            if (Keyboard.Modifiers == ModifierKeys.Alt && key == System.Windows.Input.Key.Up) { MoveStep(list, instruction, -1); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Alt && key == System.Windows.Input.Key.Down) { MoveStep(list, instruction, 1); e.Handled = true; }
            else if (e.OriginalSource == card && key == System.Windows.Input.Key.Enter) { OpenStep(instruction, true); e.Handled = true; }
            else if (e.OriginalSource == card && key == System.Windows.Input.Key.Delete) { DeleteStep(list, instruction); e.Handled = true; }
            else if (e.OriginalSource == card && key == System.Windows.Input.Key.F2 && Keyboard.Modifiers == ModifierKeys.None) { RenameStep(instruction); e.Handled = true; }
        };
        card.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is DependencyObject d && IsInsideButton(d, card)) return; OpenStep(instruction, true); e.Handled = true; };
        card.ContextMenu = StepMenu(list, instruction);
        more.Click += (_, _) => { card.ContextMenu.PlacementTarget = more; card.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; card.ContextMenu.IsOpen = true; };
        cardViews.Add((instruction, summary, hint, card));
        return card;
    }

    private static bool IsInsideButton(DependencyObject source, DependencyObject stop)
    {
        for (var d = source; d != null && d != stop; d = System.Windows.Media.VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d))
            if (d is System.Windows.Controls.Primitives.ButtonBase) return true;
        return false;
    }

    // ----------------------------------------------------------------- step actions (cards, the open step and the outline)

    private ContextMenu StepMenu(List<DockerInstruction> list, DockerInstruction instruction)
    {
        var menu = new ContextMenu();
        var open = expanded == instruction;
        menu.Items.Add(Ui.MenuItem(open ? "Done editing" : "Edit", Icons.Edit, () => { if (open) CloseStep(); else OpenStep(instruction, true); }, "Enter"));
        menu.Items.Add(Ui.MenuItem("Edit as text", Icons.Code, () => EditAsText(instruction)));
        var rename = Ui.MenuItem("Rename step", Icons.Edit, () => RenameStep(instruction), "F2");
        rename.IsEnabled = DockerfileStepNames.CanBeNamed(instruction);
        menu.Items.Add(rename);
        menu.Items.Add(Ui.MenuItem("Move up", Icons.ExpandLess, () => MoveStep(list, instruction, -1), "Alt+Up"));
        menu.Items.Add(Ui.MenuItem("Move down", Icons.ExpandMore, () => MoveStep(list, instruction, 1), "Alt+Down"));
        menu.Items.Add(Ui.MenuItem("Duplicate", Icons.FilterNone, () => DuplicateStep(list, instruction)));
        menu.Items.Add(Ui.MenuItem("Delete", Icons.Delete, () => DeleteStep(list, instruction), "Del", true));
        menu.Opened += (_, _) =>
        {
            var index = list.IndexOf(instruction);
            ((MenuItem)menu.Items[3]).IsEnabled = index > 0;
            ((MenuItem)menu.Items[4]).IsEnabled = index >= 0 && index < list.Count - 1;
        };
        return menu;
    }

    /// <summary>Opens the step (when it is not open yet) and shows its name field in the header.</summary>
    private void RenameStep(DockerInstruction instruction)
    {
        if (!DockerfileStepNames.CanBeNamed(instruction)) return;
        if (expanded != instruction) OpenStep(instruction, false);
        Dispatcher.BeginInvoke(() => beginRename?.Invoke(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Stores the new name (empty removes it) as one undo step.</summary>
    private void ApplyStepName(DockerInstruction instruction, string name)
    {
        FlushCommit();
        if (!DockerfileStepNames.Rename(instruction, name)) return;
        Changed();
        Host.Notify.Info(DockerfileStepNames.HasName(instruction)
            ? Loc.T("Step renamed. The name is saved as a # step: comment above the instruction.")
            : Loc.T("Step name removed."));
        FocusCard(instruction);
    }

    private void OpenStep(DockerInstruction instruction, bool focus)
    {
        FlushCommit();
        expanded = instruction;
        highlighted = instruction;
        RebuildStageEditor();
        HighlightLines(instruction);
        if (focus) FocusCard(instruction);
    }

    private void CloseStep()
    {
        var was = expanded;
        FlushCommit();
        expanded = null;
        RebuildStageEditor();
        if (was != null) FocusCard(was);
    }

    private void MoveStep(List<DockerInstruction> list, DockerInstruction instruction, int delta)
    {
        var index = list.IndexOf(instruction);
        var to = index + delta;
        if (index < 0 || to < 0 || to >= list.Count) return;
        FlushCommit();
        (list[index], list[to]) = (list[to], list[index]);
        Changed();
        FocusCard(instruction);
    }

    private void DuplicateStep(List<DockerInstruction> list, DockerInstruction instruction)
    {
        var index = list.IndexOf(instruction);
        if (index < 0) return;
        FlushCommit();
        var copy = instruction.Clone();
        list.Insert(index + 1, copy);
        expanded = copy;
        Changed();
    }

    private void DeleteStep(List<DockerInstruction> list, DockerInstruction instruction)
    {
        var index = list.IndexOf(instruction);
        if (index < 0) return;
        FlushCommit();
        list.RemoveAt(index);
        if (expanded == instruction) expanded = null;
        if (highlighted == instruction) highlighted = null;
        Changed();
        Host.Notify.Info(Loc.T("Instruction deleted. Undo restores it."));
    }

    // ----------------------------------------------------------------- the open step

    private readonly Grid focusHost = new();
    /// <summary>Quick-add chips; hidden while a step is open so its form keeps the height.</summary>
    private WrapPanel quickChips = null!;
    private TextBlock? focusHint;
    private Border? focusHead;
    /// <summary>Shows the name field of the open step's header.</summary>
    private Action? beginRename;

    private string StepLine(List<DockerInstruction> list, DockerInstruction instruction) =>
        $"{Loc.T("Step")} {list.IndexOf(instruction) + 1} / {list.Count}" + (ReferenceEquals(list, doc.Preamble) ? " · " + Loc.T("global") : "") + " · " + Loc.T(DockerfileModel.Hint(instruction));

    /// <summary>The open step: keyword, title, step position, previous/next, collapse and the step menu, then its form.</summary>
    private FrameworkElement BuildFocused(List<DockerInstruction> list, DockerInstruction instruction)
    {
        var keyword = DockerfileModel.Keyword(instruction.Kind);
        var neutral = instruction.Kind is InstructionKind.Comment or InstructionKind.Raw;
        var pillText = Ui.Mono(keyword, 13, neutral ? "OnSurfaceVariant" : "OnTertiaryContainer");
        pillText.FontWeight = FontWeights.Bold;
        var pill = new Border { Child = pillText, Height = 28, CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        pill.SetResourceReference(Border.BackgroundProperty, neutral ? "SurfaceHighest" : "TertiaryContainer");
        var named = DockerfileStepNames.HasName(instruction);
        var title = Ui.Text(DockerfileStepNames.Title(instruction), 16, FontWeights.SemiBold, "OnSurface", false, !named);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.ToolTip = named ? Loc.T(DockerfileOutline.Title(instruction.Kind)) + " · " + Loc.T("Double-click or press F2 to rename") : Loc.T("Double-click or press F2 to name this step");
        var nameBox = Ui.Input(DockerfileStepNames.Clean(instruction.StepName), "Step name");
        nameBox.MaxLength = DockerfileStepNames.MaxLength;
        nameBox.Visibility = Visibility.Collapsed;
        nameBox.FontSize = 15;
        nameBox.ToolTip = Loc.T("Enter saves, Escape cancels. Leave it empty to remove the name.");
        var renaming = false;
        void EndRename(bool save)
        {
            if (!renaming) return;
            renaming = false;
            var text = nameBox.Text;
            nameBox.Visibility = Visibility.Collapsed;
            title.Visibility = Visibility.Visible;
            if (save) ApplyStepName(instruction, text);
            else focusHead?.Focus();
        }
        void BeginRename()
        {
            if (!DockerfileStepNames.CanBeNamed(instruction)) return;
            renaming = true;
            nameBox.Text = DockerfileStepNames.Clean(instruction.StepName);
            title.Visibility = Visibility.Collapsed;
            nameBox.Visibility = Visibility.Visible;
            nameBox.Focus();
            nameBox.SelectAll();
        }
        beginRename = BeginRename;
        nameBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) { EndRename(true); e.Handled = true; }
            else if (e.Key == System.Windows.Input.Key.Escape) { EndRename(false); e.Handled = true; }
        };
        nameBox.LostKeyboardFocus += (_, _) => EndRename(true);
        title.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) { BeginRename(); e.Handled = true; } };
        var titleHost = new Grid();
        titleHost.Children.Add(title);
        titleHost.Children.Add(nameBox);
        focusHint = Ui.Text(StepLine(list, instruction), 12, null, "OnSurfaceVariant", false, false);
        focusHint.ToolTip = focusHint.Text;
        var index = list.IndexOf(instruction);
        var previous = Ui.IconButton(Icons.ExpandLess, "Previous step", () => { if (index > 0) OpenStep(list[index - 1], true); });
        var next = Ui.IconButton(Icons.ExpandMore, "Next step", () => { if (index < list.Count - 1) OpenStep(list[index + 1], true); });
        previous.IsEnabled = index > 0; next.IsEnabled = index < list.Count - 1;
        var collapse = Ui.IconButton(Icons.CloseFullscreen, "Collapse instruction (Esc)", CloseStep);
        var more = Ui.IconButton(Icons.MoreVert, "More instruction actions");
        var menu = StepMenu(list, instruction);
        more.Click += (_, _) => { menu.PlacementTarget = more; menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; menu.IsOpen = true; };
        foreach (var b in new[] { previous, next, collapse, more }) { b.Width = b.Height = b.MinHeight = 36; b.FontSize = 20; }
        var head = Ui.Columns(10, [0, 1, 0, 0, 0, 0], pill, Ui.VStack(2, titleHost, focusHint), previous, next, collapse, more);
        focusHead = new Border { Child = head, Focusable = true, Padding = new Thickness(0, 0, 0, 14), Background = System.Windows.Media.Brushes.Transparent };
        focusHead.FocusVisualStyle = (Style)Application.Current.FindResource("FocusRing");
        focusHead.ContextMenu = menu;
        System.Windows.Automation.AutomationProperties.SetName(focusHead, $"{keyword} · {(named ? DockerfileStepNames.Clean(instruction.StepName) : Loc.T(DockerfileOutline.Title(instruction.Kind)))} · {StepLine(list, instruction)}");

        var form = new InstructionForm(instruction, ReferenceEquals(list, doc.Preamble) ? () => Array.Empty<string>() : EarlierStageNames, Touched);
        var panel = new DockPanel();
        panel.Children.Add(focusHead.Dock(System.Windows.Controls.Dock.Top));
        panel.Children.Add(Ui.Scroll(form));
        panel.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            if (Keyboard.Modifiers == ModifierKeys.Alt && key == System.Windows.Input.Key.Up) { MoveStep(list, instruction, -1); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Alt && key == System.Windows.Input.Key.Down) { MoveStep(list, instruction, 1); e.Handled = true; }
            else if (key == System.Windows.Input.Key.Escape && Keyboard.Modifiers == ModifierKeys.None && !InOpenDropDown() && !renaming) { CloseStep(); e.Handled = true; }
            else if (key == System.Windows.Input.Key.F2 && Keyboard.Modifiers == ModifierKeys.None && !renaming) { BeginRename(); e.Handled = true; }
        };
        return panel;
    }

    /// <summary>True while the keyboard focus is inside a combo box whose list is open (Escape then closes the list, not the step).</summary>
    private static bool InOpenDropDown()
    {
        for (var d = Keyboard.FocusedElement as DependencyObject; d != null; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
            if (d is ComboBox { IsDropDownOpen: true }) return true;
        return false;
    }

    /// <summary>The instruction keyword in a 26 px pill (comments and raw lines use the neutral colour).</summary>
    private static Border KeywordPill(string keyword, bool neutral)
    {
        var label = Ui.Mono(keyword, 12, neutral ? "OnSurfaceVariant" : "OnTertiaryContainer");
        label.FontWeight = FontWeights.SemiBold;
        var pill = new Border { Child = label, Height = 26, MinWidth = 64, CornerRadius = new CornerRadius(13), Padding = new Thickness(10, 0, 10, 0), HorizontalAlignment = HorizontalAlignment.Left };
        pill.SetResourceReference(Border.BackgroundProperty, neutral ? "SurfaceHighest" : "TertiaryContainer");
        label.TextAlignment = TextAlignment.Center;
        return pill;
    }

    private void FocusCard(DockerInstruction instruction) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (expanded == instruction && focusHost.Visibility == Visibility.Visible && focusHead != null) { focusHead.Focus(); return; }
            var view = cardViews.FirstOrDefault(c => c.Instruction == instruction);
            view.Card?.Focus();
            view.Card?.BringIntoView();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
}
