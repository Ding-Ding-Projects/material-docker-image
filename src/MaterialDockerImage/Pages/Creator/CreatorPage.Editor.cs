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
        var quick = Ui.Wrap(6,
            QuickChip("RUN with cache mount", () => new DockerInstruction { Kind = InstructionKind.Run, Command = "echo build", Mounts = { new RunMount { Type = RunMounts.Cache, Target = "/root/.cache" } } }, false),
            QuickChip("RUN heredoc script", () => new DockerInstruction { Kind = InstructionKind.Run, Heredoc = true, HeredocDelimiter = "EOF", HeredocBody = "set -e\necho build" }, false),
            QuickChip("HEALTHCHECK", () => DockerfileModel.Create(InstructionKind.Healthcheck), true),
            QuickChip("Non-root USER", () => new DockerInstruction { Kind = InstructionKind.User, Value = "10001" }, true),
            QuickChip("EXPOSE", () => DockerfileModel.Create(InstructionKind.Expose), true));
        var addRow = Ui.Wrap(8, addInstruction, quick);
        footer = new Border { Child = addRow, Padding = new Thickness(0, 10, 0, 0), BorderThickness = new Thickness(0, 1, 0, 0) };
        footer.SetResourceReference(Border.BorderBrushProperty, "SurfaceHigh");

        noStage = Ui.EmptyState(Icons.Layers, "No stages yet", "Pick a starter on the left, open an existing Dockerfile, or add a stage.",
            Ui.Button("Add stage", Icons.Add, ButtonKind.Filled, AddStage));
        noStage.Visibility = Visibility.Collapsed;

        var stack = Ui.VStack(14, preambleSection, stageFields, Ui.Divider(), cards);
        var grid = new Grid();
        editorScroll = Ui.Scroll(stack);
        var host = new DockPanel();
        host.Children.Add(footer.Dock(System.Windows.Controls.Dock.Bottom));
        host.Children.Add(editorScroll);
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
        if (!has) return;
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
    }

    private void UpdateCardSummaries()
    {
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
        var isOpen = expanded == instruction;
        var keyword = DockerfileModel.Keyword(instruction.Kind);
        var chip = KeywordPill(keyword, instruction.Kind is InstructionKind.Comment or InstructionKind.Raw);
        chip.VerticalAlignment = VerticalAlignment.Top;
        var summary = Ui.Mono(DockerfileModel.Summary(instruction), 13, "OnSurface", false);
        summary.ToolTip = summary.Text;
        var hint = Ui.Caption(DockerfileModel.Hint(instruction));
        var text = Ui.VStack(2, summary, hint);

        void Move(int delta)
        {
            var to = index + delta;
            if (to < 0 || to >= list.Count) return;
            FlushCommit();
            (list[index], list[to]) = (list[to], list[index]);
            Changed();
            FocusCard(instruction);
        }
        var up = Ui.IconButton(Icons.ExpandLess, "Move up (Alt+Up)", () => Move(-1));
        var down = Ui.IconButton(Icons.ExpandMore, "Move down (Alt+Down)", () => Move(1));
        up.IsEnabled = index > 0; down.IsEnabled = index < list.Count - 1;
        var edit = Ui.IconButton(isOpen ? Icons.Check : Icons.Edit, isOpen ? "Done editing" : "Edit", () => { FlushCommit(); expanded = isOpen ? null : instruction; RebuildStageEditor(); FocusCard(instruction); });
        var duplicate = Ui.IconButton(Icons.FilterNone, "Duplicate", () => { FlushCommit(); var copy = instruction.Clone(); list.Insert(index + 1, copy); expanded = copy; Changed(); });
        var delete = Ui.IconButton(Icons.Delete, "Delete (Del)", () => { FlushCommit(); list.RemoveAt(index); if (expanded == instruction) expanded = null; Changed(); Host.Notify.Info(Loc.T("Instruction deleted. Undo restores it.")); });
        var asText = Ui.IconButton(Icons.Code, "Edit as text", () => EditAsText(instruction));
        asText.Visibility = instruction.Kind == InstructionKind.Raw ? Visibility.Visible : Visibility.Collapsed;
        // One overflow button replaces the row of icon buttons; every action stays in the menu below (and on the keyboard).
        var more = Ui.IconButton(Icons.MoreVert, "More step actions");
        more.Width = more.Height = more.MinHeight = 32; more.FontSize = 18;
        more.VerticalAlignment = VerticalAlignment.Top;
        var actions = more;

        var top = Ui.Columns(12, [0, 1, 0], chip, text, actions);
        var body = new StackPanel();
        body.Children.Add(top);
        if (isOpen)
        {
            var form = new InstructionForm(instruction, EarlierStageNames, Touched) { Margin = new Thickness(0, 10, 0, 0) };
            var done = Ui.Button("Done", null, ButtonKind.Filled, () => { FlushCommit(); expanded = null; RebuildStageEditor(); FocusCard(instruction); });
            done.Height = 32; done.MinHeight = 32; done.Padding = new Thickness(12, 0, 12, 0); done.FontSize = 13;
            var editHeader = Ui.Columns(8, [0, 1, 0], KeywordPill(keyword, false), Ui.Text($"{Loc.T("Editing step")} {index + 1}", 14, FontWeights.SemiBold), done);
            body.Children.Add(Ui.Divider().Margin(0, 12, 0, 0));
            body.Children.Add(editHeader.Margin(0, 10, 0, 0));
            body.Children.Add(form);
        }
        var card = new Border { Child = body, CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10, 8, 10), BorderThickness = new Thickness(isOpen ? 2 : 0), Margin = new Thickness(0, 0, 0, 8), Focusable = true, Tag = instruction };
        card.SetResourceReference(Border.BackgroundProperty, isOpen ? "SurfaceHigh" : "SurfaceContainer");
        var marked = isOpen || highlighted == instruction;
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
        System.Windows.Automation.AutomationProperties.SetName(card, $"{Loc.T("Step")} {index + 1}: {keyword} {summary.Text}");
        card.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            if (Keyboard.Modifiers == ModifierKeys.Alt && key == System.Windows.Input.Key.Up) { Move(-1); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Alt && key == System.Windows.Input.Key.Down) { Move(1); e.Handled = true; }
            else if (e.OriginalSource == card && key == System.Windows.Input.Key.Enter) { edit.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); e.Handled = true; }
            else if (e.OriginalSource == card && key == System.Windows.Input.Key.Delete) { delete.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); e.Handled = true; }
        };
        card.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2 && !isOpen) { expanded = instruction; RebuildStageEditor(); e.Handled = true; } };
        card.ContextMenu = new ContextMenu();
        card.ContextMenu.Items.Add(Ui.MenuItem(isOpen ? "Done editing" : "Edit", Icons.Edit, () => { FlushCommit(); expanded = isOpen ? null : instruction; RebuildStageEditor(); }, "Enter"));
        card.ContextMenu.Items.Add(Ui.MenuItem("Edit as text", Icons.Code, () => EditAsText(instruction)));
        card.ContextMenu.Items.Add(Ui.MenuItem("Move up", Icons.ExpandLess, () => Move(-1), "Alt+Up"));
        card.ContextMenu.Items.Add(Ui.MenuItem("Move down", Icons.ExpandMore, () => Move(1), "Alt+Down"));
        card.ContextMenu.Items.Add(Ui.MenuItem("Duplicate", Icons.FilterNone, () => duplicate.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent))));
        card.ContextMenu.Items.Add(Ui.MenuItem("Delete", Icons.Delete, () => delete.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)), "Del", true));
        more.Click += (_, _) => { card.ContextMenu.PlacementTarget = more; card.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; card.ContextMenu.IsOpen = true; };
        cardViews.Add((instruction, summary, hint, card));
        return card;
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
            var view = cardViews.FirstOrDefault(c => c.Instruction == instruction);
            view.Card?.Focus();
            view.Card?.BringIntoView();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
}
