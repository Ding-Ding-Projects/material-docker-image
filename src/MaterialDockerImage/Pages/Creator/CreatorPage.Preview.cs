using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Right column: the editable Dockerfile source (two-way synchronised with the cards), an optional coloured
/// read-only view, the .dockerignore and compose.yaml tabs, and the Checks panel with fix buttons.
/// </summary>
/// <remarks>
/// Text to model: every edit restarts a 300 ms timer; when it fires (or the editor loses focus) the text is parsed
/// and replaces the model, the stage list and cards are rebuilt, and the text itself is left exactly as typed.
/// Model to text: card, stage, starter and fix changes re-render the text, keeping the caret on the edited
/// instruction. <see cref="syncingPreview"/> stops the two directions from triggering each other.
/// </remarks>
public sealed partial class CreatorPage
{
    private readonly CodeEditor preview = new(false, 160);
    private TextFindBar previewFind = null!;
    private readonly CreatorSyntaxMirror mirror = new();
    private readonly StackPanel checks = new();
    private readonly TextBlock checksCount = Ui.Caption("");
    private readonly DispatcherTimer parseTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer caretTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private CheckBox? syntaxSwitch, colourSwitch;
    private TabControl previewTabs = null!;
    private TabItem dockerfileTab = null!;
    private bool syncingSyntax, syncingPreview, textDirty;
    private IReadOnlyList<DockerfileLineSpan> previewMap = Array.Empty<DockerfileLineSpan>();
    private IReadOnlyList<string> parseWarnings = Array.Empty<string>();
    /// <summary>The preview text with its line map and per-element rendering, used to patch only changed lines.</summary>
    private DockerfileSourceState? sourceState;
    /// <summary>The card matching the preview caret (outlined, not expanded).</summary>
    private DockerInstruction? highlighted;

    private UIElement BuildPreviewColumn()
    {
        previewFind = new TextFindBar(preview.Box, "Search Dockerfile");
        System.Windows.Automation.AutomationProperties.SetName(preview.Box, Loc.T("Dockerfile source (editable)"));
        preview.Box.TextChanged += (_, _) =>
        {
            if (syncingPreview) return;
            textDirty = true;
            parseTimer.Stop(); parseTimer.Start();
        };
        preview.Box.LostKeyboardFocus += (_, _) => { if (textDirty) { parseTimer.Stop(); ApplyPreviewText(); } };
        preview.Box.SelectionChanged += (_, _) =>
        {
            if (syncingPreview || !preview.Box.IsKeyboardFocused) return;
            caretTimer.Stop(); caretTimer.Start();
        };
        parseTimer.Tick += (_, _) => { parseTimer.Stop(); ApplyPreviewText(); };
        caretTimer.Tick += (_, _) => { caretTimer.Stop(); SelectCardAtCaret(); };

        var copy = Ui.IconButton(Icons.ContentCopy, "Copy Dockerfile", () =>
        {
            try { Clipboard.SetText(preview.Text); Host.Notify.Info("Dockerfile copied to the clipboard."); }
            catch (System.Runtime.InteropServices.ExternalException) { Host.Notify.Warning("The clipboard is busy. Try again."); }
        });
        var buildCommand = Ui.IconButton(Icons.Terminal, "Copy as build command", CopyBuildCommand);
        // Compact switches (explanations in tooltips) so the editable source keeps most of the column height.
        syntaxSwitch = Ui.Switch("Syntax header", true, v => { if (syncingSyntax) return; FlushCommit(); doc.SyntaxHeader = v; Changed(); });
        syntaxSwitch.ToolTip = Loc.T("Adds # syntax=docker/dockerfile:1 so BuildKit uses the current Dockerfile frontend.");
        colourSwitch = Ui.Switch("Coloured view", false, on => ShowMirror(on));
        colourSwitch.ToolTip = Loc.T("Read-only view with syntax colouring. Switch off to edit the text.");
        previewFind.Search.Changed += () => { if (colourSwitch.IsChecked == true && previewFind.Search.Query.Length > 0) colourSwitch.IsChecked = false; };

        var editorHost = new Grid();
        editorHost.Children.Add(preview);
        editorHost.Children.Add(mirror);
        mirror.Visibility = Visibility.Collapsed;
        var dockerfilePanel = new DockPanel();
        var hint = Ui.Caption("Type here or edit the cards: both stay in sync.");
        hint.ToolTip = Loc.T("Lines the Creator cannot model become Raw cards with Edit as text.");
        dockerfilePanel.Children.Add(hint.Dock(System.Windows.Controls.Dock.Top));
        previewFind.Margin = new Thickness(0, 8, 0, 8);
        dockerfilePanel.Children.Add(previewFind.Dock(System.Windows.Controls.Dock.Top));
        dockerfilePanel.Children.Add(Ui.Wrap(16, syntaxSwitch, colourSwitch).Dock(System.Windows.Controls.Dock.Bottom).Margin(0, 8, 0, 0));
        dockerfilePanel.Children.Add(editorHost);

        previewTabs = new TabControl();
        dockerfilePanel.Margin = new Thickness(0, 10, 0, 0);
        dockerfileTab = new TabItem { Header = "Dockerfile", Content = dockerfilePanel };
        previewTabs.Items.Add(dockerfileTab);
        previewTabs.Items.Add(new TabItem { Header = ".dockerignore", Content = BuildIgnorePanel().Margin(0, 10, 0, 0) });
        previewTabs.Items.Add(BuildComposeTab());
        var previewTop = Ui.Columns(4, [1, 0, 0], Ui.Text("Source", 16, FontWeights.SemiBold), buildCommand, copy);
        var previewPanel = new DockPanel();
        previewPanel.Children.Add(previewTop.Dock(System.Windows.Controls.Dock.Top));
        previewPanel.Children.Add(previewTabs);
        var previewCard = Ui.Card(previewPanel, 16, "SurfaceLow", 16);

        var checksHeader = Ui.Columns(8, [1, 0], Ui.Text("Checks", 16, FontWeights.SemiBold), checksCount);
        checksHeader.Margin = new Thickness(0, 0, 0, 10);
        var checksPanel = new DockPanel();
        checksPanel.Children.Add(checksHeader.Dock(System.Windows.Controls.Dock.Top));
        checksPanel.Children.Add(Ui.Scroll(checks));
        var checksCard = Ui.Card(checksPanel, 16, "SurfaceLow", 16);

        return Ui.Rows(16, [5, 2], previewCard, checksCard);
    }

    private void ShowMirror(bool on)
    {
        if (on) { if (textDirty) { parseTimer.Stop(); ApplyPreviewText(); } mirror.Show(preview.Text); }
        mirror.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        preview.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
    }

    // ----------------------------------------------------------------- model to text

    private void RefreshPreview()
    {
        if (TryPatchPreview()) { SyncSyntaxSwitch(); return; }
        var rendered = DockerfileModel.RenderWithMap(doc);
        previewMap = rendered.Lines;
        sourceState = DockerfilePatch.FromRender(doc);
        if (preview.Text != rendered.Text)
        {
            parseTimer.Stop();
            textDirty = false;
            parseWarnings = Array.Empty<string>();
            var anchor = AnchorLine();
            var offset = preview.Box.VerticalOffset;
            syncingPreview = true;
            try
            {
                preview.Text = rendered.Text;
                preview.Box.ScrollToVerticalOffset(offset);
                if (anchor >= 0)
                {
                    preview.Box.Select(CharacterIndexOfLine(rendered.Text, anchor), 0);
                    ScrollLineIntoView(anchor);
                }
            }
            finally { syncingPreview = false; }
            HighlightLines(expanded ?? highlighted);
            if (mirror.Visibility == Visibility.Visible) mirror.Show(rendered.Text);
        }
        SyncSyntaxSwitch();
    }

    private void SyncSyntaxSwitch()
    {
        if (syntaxSwitch != null && syntaxSwitch.IsChecked != doc.SyntaxHeader)
        {
            syncingSyntax = true;
            try { syntaxSwitch.IsChecked = doc.SyntaxHeader; } finally { syncingSyntax = false; }
        }
    }

    /// <summary>
    /// In-place GUI edits (a field of one card or stage) replace only that element's lines, so the user's comments,
    /// blank lines and formatting elsewhere survive. Returns false when the structure changed and a full render is needed.
    /// </summary>
    private bool TryPatchPreview()
    {
        if (sourceState == null || textDirty || preview.Text != sourceState.Text) return false;
        var patched = DockerfilePatch.TryPatch(sourceState, doc);
        if (patched == null) return false;
        sourceState = patched;
        previewMap = patched.Lines;
        if (patched.Text != preview.Text)
        {
            var caret = preview.Box.CaretIndex;
            var offset = preview.Box.VerticalOffset;
            syncingPreview = true;
            try
            {
                preview.Text = patched.Text;
                preview.Box.ScrollToVerticalOffset(offset);
                preview.Box.Select(Math.Min(caret, patched.Text.Length), 0);
            }
            finally { syncingPreview = false; }
            HighlightLines(expanded ?? highlighted);
            if (mirror.Visibility == Visibility.Visible) mirror.Show(patched.Text);
        }
        return true;
    }

    /// <summary>The line to keep the caret on after a GUI edit: the open card, the highlighted card, or the selected stage's FROM.</summary>
    private int AnchorLine()
    {
        foreach (var instruction in new[] { expanded, highlighted })
            if (instruction != null && SpanOf(instruction) is { } span) return span.StartLine;
        return previewMap.FirstOrDefault(s => s.StageIndex == selectedStage && s.InstructionIndex < 0)?.StartLine ?? -1;
    }

    private DockerfileLineSpan? SpanOf(DockerInstruction instruction)
    {
        var p = doc.Preamble.IndexOf(instruction);
        if (p >= 0) return previewMap.FirstOrDefault(s => s.StageIndex == -1 && s.InstructionIndex == p);
        for (var s = 0; s < doc.Stages.Count; s++)
        {
            var i = doc.Stages[s].Instructions.IndexOf(instruction);
            if (i >= 0) return previewMap.FirstOrDefault(x => x.StageIndex == s && x.InstructionIndex == i);
        }
        return null;
    }

    private static int CharacterIndexOfLine(string text, int line)
    {
        var index = 0;
        for (var k = 0; k < line; k++)
        {
            var next = text.IndexOf('\n', index);
            if (next < 0) return text.Length;
            index = next + 1;
        }
        return index;
    }

    private void ScrollLineIntoView(int line) => Dispatcher.BeginInvoke(() =>
    {
        var first = preview.Box.GetFirstVisibleLineIndex();
        var last = preview.Box.GetLastVisibleLineIndex();
        if (first < 0 || last < 0 || line < first || line > last) preview.Box.ScrollToLine(Math.Max(0, line));
    }, DispatcherPriority.Loaded);

    /// <summary>Selects the lines of a card in the preview (without taking focus) so the two views point at the same step.</summary>
    private void HighlightLines(DockerInstruction? instruction)
    {
        if (instruction == null || preview.Box.IsKeyboardFocused || textDirty) return;
        if (SpanOf(instruction) is not { } span) return;
        var text = preview.Text;
        var start = CharacterIndexOfLine(text, span.StartLine);
        var end = CharacterIndexOfLine(text, span.EndLine + 1);
        if (end > start && end <= text.Length && text[end - 1] == '\n') end--;
        syncingPreview = true;
        try { preview.Box.Select(start, Math.Max(0, end - start)); }
        finally { syncingPreview = false; }
        ScrollLineIntoView(span.StartLine);
    }

    // ----------------------------------------------------------------- text to model

    /// <summary>Parses the edited text and replaces the model without touching the text, caret or scroll position.</summary>
    private void ApplyPreviewText()
    {
        if (!textDirty) return;
        textDirty = false;
        DockerfileParseResult result;
        try { result = DockerfileModel.Parse(preview.Text); }
        catch (ArgumentException e) { parseWarnings = [e.Message]; RefreshChecks(); return; }
        var next = result.Document;
        next.Origin = doc.Origin;
        next.IgnorePatterns = doc.IgnorePatterns;
        next.Target = next.Stages.Any(s => s.Name.Trim().Length > 0 && string.Equals(s.Name.Trim(), doc.Target.Trim(), StringComparison.OrdinalIgnoreCase)) ? doc.Target : "";
        commitTimer.Stop();
        pendingCommit = false;
        doc = next;
        previewMap = result.LineMap;
        sourceState = DockerfilePatch.FromParse(preview.Text, result);
        parseWarnings = result.Warnings;
        expanded = null; highlighted = null;
        if (history.Commit(doc)) SchedulePersist();
        var span = CaretSpan();
        selectedStage = span is { StageIndex: >= 0 } ? span.StageIndex : doc.Stages.Count == 0 ? 0 : Math.Clamp(selectedStage, 0, doc.Stages.Count - 1);
        highlighted = InstructionAt(span);
        RefreshStageList();
        RebuildStageEditor();
        RefreshChecks();
        UpdateSubtitle();
        UpdateUndoButtons();
        RefreshIgnoreEditor();
        SyncSyntaxSwitch();
    }

    private DockerfileLineSpan? CaretSpan()
    {
        var line = preview.Box.GetLineIndexFromCharacterIndex(preview.Box.CaretIndex);
        if (line < 0) line = preview.Text[..Math.Min(preview.Box.CaretIndex, preview.Text.Length)].Count(c => c == '\n');
        return DockerfileModel.SpanAt(previewMap, line);
    }

    private DockerInstruction? InstructionAt(DockerfileLineSpan? span)
    {
        if (span == null || span.InstructionIndex < 0) return null;
        var list = span.StageIndex < 0 ? doc.Preamble : span.StageIndex < doc.Stages.Count ? doc.Stages[span.StageIndex].Instructions : null;
        return list != null && span.InstructionIndex < list.Count ? list[span.InstructionIndex] : null;
    }

    /// <summary>Caret moved in the preview: select the stage and outline the card for that line.</summary>
    private void SelectCardAtCaret()
    {
        if (textDirty) return;
        var span = CaretSpan();
        if (span == null) return;
        var instruction = InstructionAt(span);
        if (span.StageIndex >= 0 && span.StageIndex != selectedStage)
        {
            FlushCommit();
            selectedStage = span.StageIndex;
            expanded = null; highlighted = instruction;
            RefreshStageList();
            RebuildStageEditor();
        }
        else
        {
            highlighted = instruction;
            UpdateCardSummaries();
        }
        if (instruction != null)
        {
            var view = cardViews.FirstOrDefault(c => c.Instruction == instruction);
            view.Card?.BringIntoView();
        }
    }

    // ----------------------------------------------------------------- commands

    private void CopyBuildCommand()
    {
        FlushCommit();
        var dockerfile = savedPath.Length > 0 ? savedPath : "Dockerfile";
        var context = savedPath.Length > 0 ? Path.GetDirectoryName(savedPath) ?? "." : ".";
        var command = DockerfilePlatform.BuildCommand(doc, dockerfile, context);
        try
        {
            Clipboard.SetText(command.Command);
            Host.Notify.Info(Loc.T("Build command copied:") + " " + command.Command + (command.Notes.Count > 0 ? " " + string.Join(" ", command.Notes) : ""));
        }
        catch (System.Runtime.InteropServices.ExternalException) { Host.Notify.Warning("The clipboard is busy. Try again."); }
    }

    // ----------------------------------------------------------------- checks

    private void RefreshChecks()
    {
        checks.Children.Clear();
        var results = DockerfileLint.Check(doc);
        var problems = DockerfileLint.CountProblems(results);
        var errors = results.Count(r => r.Severity == LintSeverity.Error);
        checksCount.Text = problems == 0 && parseWarnings.Count == 0 ? Loc.T("All clear") : errors > 0 ? $"{errors} {Loc.T(errors == 1 ? "error" : "errors")} · {problems} {Loc.T("to review")}" : $"{problems + parseWarnings.Count} {Loc.T("to review")}";
        foreach (var warning in parseWarnings) checks.Children.Add(ParseNoteRow(warning));
        foreach (var result in results) checks.Children.Add(CheckRow(result));
        if (results.Count == 0 && parseWarnings.Count == 0) checks.Children.Add(Ui.Caption("No checks apply yet."));
    }

    private UIElement ParseNoteRow(string warning)
    {
        var line = DockerfileParseResult.WarningLine(warning);
        var text = Ui.VStack(2, Ui.Text("Source note", 14, FontWeights.SemiBold), Ui.Text(warning, 13, null, "OnSurfaceVariant", true, false),
            Ui.Text("PARSE" + (line > 0 ? $" · {Loc.T("line")} {line}" : ""), 12, null, "Outline", false, false));
        var actions = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        if (line > 0)
        {
            var go = Ui.Button($"{Loc.T("Line")} {line}", null, ButtonKind.Tonal, () => GoToSourceLine(line - 1), false);
            go.MinHeight = 32; go.Height = 32; go.Padding = new Thickness(12, 0, 12, 0);
            go.ToolTip = Loc.T("Move the caret to this line in the Dockerfile source.");
            actions.Children.Add(go);
        }
        var row = Ui.Columns(10, [0, 1, 0], Ui.Icon(Icons.Code, 20, true, "Tertiary"), text, actions);
        ((FrameworkElement)row.Children[0]).VerticalAlignment = VerticalAlignment.Top;
        row.Margin = new Thickness(0, 0, 0, 12);
        System.Windows.Automation.AutomationProperties.SetName(row, Loc.T("Source note") + ": " + warning);
        return row;
    }

    /// <summary>Focuses the source editor with an instruction's lines selected ("Edit as text" on raw cards).</summary>
    private void EditAsText(DockerInstruction instruction)
    {
        FlushCommit();
        if (SpanOf(instruction) is not { } span) return;
        previewTabs.SelectedItem = dockerfileTab;
        if (colourSwitch?.IsChecked == true) colourSwitch.IsChecked = false;
        var text = preview.Text;
        var start = CharacterIndexOfLine(text, span.StartLine);
        var end = Math.Min(text.Length, CharacterIndexOfLine(text, span.EndLine + 1));
        if (end > start && text[end - 1] == '\n') end--;
        if (end > start && text[end - 1] == '\r') end--;
        preview.Box.Focus();
        syncingPreview = true;
        try { preview.Box.Select(start, Math.Max(0, end - start)); }
        finally { syncingPreview = false; }
        ScrollLineIntoView(span.StartLine);
    }

    private void GoToSourceLine(int line)
    {
        previewTabs.SelectedItem = dockerfileTab;
        if (colourSwitch?.IsChecked == true) colourSwitch.IsChecked = false;
        preview.GoToLine(line);
    }

    private UIElement CheckRow(LintResult result)
    {
        var (icon, brush) = result.Severity switch
        {
            LintSeverity.Error => (Icons.Error, "Error"),
            LintSeverity.Warning => (Icons.Warning, "Warning"),
            LintSeverity.Info => (Icons.Info, "Primary"),
            _ => (Icons.CheckCircle, "Success")
        };
        var title = Ui.Text(result.Title, 14, FontWeights.SemiBold);
        var message = Ui.Text(result.Message, 13, null, "OnSurfaceVariant");
        var id = result.Id + (DockerfileLint.BuildKitName(result.Id) is { } check ? " (" + check + ")" : "");
        var where = result.StageIndex >= 0 && result.StageIndex < doc.Stages.Count
            ? Ui.Text($"{id} · {(doc.Stages[result.StageIndex].Name.Trim().Length > 0 ? doc.Stages[result.StageIndex].Name.Trim() : "stage " + result.StageIndex)}" + (result.InstructionIndex >= 0 ? $" · {Loc.T("step")} {result.InstructionIndex + 1}" : ""), 12, null, "Outline", false, false)
            : Ui.Text(id + (result.InstructionIndex >= 0 ? $" · {Loc.T("global")} {result.InstructionIndex + 1}" : ""), 12, null, "Outline", false, false);
        var text = Ui.VStack(2, title, message, where);
        var actions = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        if (result.Fix != null)
        {
            var fix = Ui.Button(result.Fix.Label, null, ButtonKind.Tonal, () => PageTasks.Fire(() => ApplyFixAsync(result)), false);
            fix.MinHeight = 32; fix.Height = 32; fix.Padding = new Thickness(12, 0, 12, 0);
            fix.ToolTip = Loc.T("Apply this fix to the draft. Undo reverts it.");
            actions.Children.Add(fix);
        }
        if (result.StageIndex >= 0)
        {
            var show = Ui.IconButton(Icons.ArrowForward, "Show in editor", () => Reveal(result));
            show.Width = show.Height = show.MinHeight = 32; show.FontSize = 18; show.HorizontalAlignment = HorizontalAlignment.Right;
            actions.Children.Add(show);
        }
        var row = Ui.Columns(10, [0, 1, 0], Ui.Icon(icon, 20, true, brush), text, actions);
        ((FrameworkElement)row.Children[0]).VerticalAlignment = VerticalAlignment.Top;
        row.Margin = new Thickness(0, 0, 0, 12);
        System.Windows.Automation.AutomationProperties.SetName(row, $"{result.Severity}: {result.Title}. {result.Message}");
        return row;
    }

    private void Reveal(LintResult result)
    {
        if (result.StageIndex < 0 || result.StageIndex >= doc.Stages.Count) return;
        FlushCommit();
        selectedStage = result.StageIndex;
        var list = doc.Stages[selectedStage].Instructions;
        expanded = result.InstructionIndex >= 0 && result.InstructionIndex < list.Count ? list[result.InstructionIndex] : null;
        RefreshStageList();
        RebuildStageEditor();
        if (expanded != null) { FocusCard(expanded); HighlightLines(expanded); }
        else Dispatcher.BeginInvoke(() => baseImage.Focus(), DispatcherPriority.Loaded);
    }

    private async Task ApplyFixAsync(LintResult result)
    {
        var fix = result.Fix!;
        FlushCommit();
        if (fix.NeedsValue)
        {
            var value = await Host.Overlay.PromptAsync(fix.PromptTitle!, fix.PromptLabel ?? "Value", fix.PromptInitial ?? "", fix.PromptHelp, fix.ValidateValue);
            if (value == null) return;
            fix.ApplyValue!(doc, value);
        }
        else fix.Apply!(doc);
        if (result.StageIndex >= 0 && result.StageIndex < doc.Stages.Count) selectedStage = result.StageIndex;
        Changed();
        Host.Notify.Info(Loc.T("Fix applied:") + " " + result.Title + ". " + Loc.T("Undo reverts it."));
    }
}

/// <summary>Read-only Dockerfile view with syntax colouring from <see cref="DockerfileHighlighter"/>, using theme role brushes.</summary>
public sealed class CreatorSyntaxMirror : Border
{
    private readonly TextBlock text = new() { FontSize = 13, Padding = new Thickness(14, 12, 14, 12) };
    private const int MaxColouredLength = 200_000;

    public CreatorSyntaxMirror()
    {
        CornerRadius = new CornerRadius(12);
        BorderThickness = new Thickness(1);
        SetResourceReference(BackgroundProperty, "SurfaceContainer");
        SetResourceReference(BorderBrushProperty, "OutlineVariant");
        text.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        text.SetResourceReference(TextBlock.ForegroundProperty, "OnSurface");
        System.Windows.Automation.AutomationProperties.SetName(text, Loc.T("Coloured Dockerfile view"));
        Child = new ScrollViewer { Content = text, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = true };
    }

    public void Show(string source)
    {
        text.Inlines.Clear();
        if (source.Length > MaxColouredLength) { text.Text = source; return; }
        foreach (var token in DockerfileHighlighter.Tokenize(source))
        {
            var run = new System.Windows.Documents.Run(source.Substring(token.Start, token.Length));
            var (brush, bold, italic) = token.Kind switch
            {
                DockerfileTokenKind.Keyword => ("Primary", true, false),
                DockerfileTokenKind.Flag => ("Tertiary", false, false),
                DockerfileTokenKind.String => ("Success", false, false),
                DockerfileTokenKind.Variable => ("Warning", false, false),
                DockerfileTokenKind.Comment => ("Outline", false, true),
                DockerfileTokenKind.Directive => ("Outline", true, false),
                DockerfileTokenKind.HeredocBody => ("OnSurfaceVariant", false, false),
                _ => ("OnSurface", false, false)
            };
            run.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, brush);
            if (bold) run.FontWeight = FontWeights.SemiBold;
            if (italic) run.FontStyle = FontStyles.Italic;
            text.Inlines.Add(run);
        }
    }
}
