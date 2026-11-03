using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Right column: live preview with copy and search, and the Checks panel with fix buttons.</summary>
public sealed partial class CreatorPage
{
    private readonly CodeEditor preview = new(true, 160);
    private TextFindBar previewFind = null!;
    private readonly StackPanel checks = new();
    private readonly TextBlock checksCount = Ui.Caption("");
    private CheckBox? syntaxSwitch;
    private bool syncingSyntax;

    private UIElement BuildPreviewColumn()
    {
        previewFind = new TextFindBar(preview.Box, "Search preview");
        System.Windows.Automation.AutomationProperties.SetName(preview.Box, Loc.T("Live Dockerfile preview"));
        var copy = Ui.IconButton(Icons.ContentCopy, "Copy Dockerfile", () =>
        {
            try { Clipboard.SetText(preview.Text); Host.Notify.Info("Dockerfile copied to the clipboard."); }
            catch (System.Runtime.InteropServices.ExternalException) { Host.Notify.Warning("The clipboard is busy. Try again."); }
        });
        syntaxSwitch = Ui.Switch("Syntax header", true, v => { if (syncingSyntax) return; FlushCommit(); doc.SyntaxHeader = v; Changed(); }, "Adds # syntax=docker/dockerfile:1 so BuildKit uses the current Dockerfile frontend.");
        var previewTop = Ui.Columns(8, [1, 0], Ui.Text("Live preview", 16, FontWeights.SemiBold), copy);
        var previewPanel = new DockPanel();
        previewPanel.Children.Add(previewTop.Dock(System.Windows.Controls.Dock.Top));
        previewFind.Margin = new Thickness(0, 8, 0, 8);
        previewPanel.Children.Add(previewFind.Dock(System.Windows.Controls.Dock.Top));
        previewPanel.Children.Add(syntaxSwitch.Dock(System.Windows.Controls.Dock.Bottom).Margin(0, 8, 0, 0));
        previewPanel.Children.Add(preview);
        var previewCard = Ui.Card(previewPanel, 16, "SurfaceLow", 16);

        var checksHeader = Ui.Columns(8, [1, 0], Ui.Text("Checks", 16, FontWeights.SemiBold), checksCount);
        checksHeader.Margin = new Thickness(0, 0, 0, 10);
        var checksPanel = new DockPanel();
        checksPanel.Children.Add(checksHeader.Dock(System.Windows.Controls.Dock.Top));
        checksPanel.Children.Add(Ui.Scroll(checks));
        var checksCard = Ui.Card(checksPanel, 16, "SurfaceLow", 16);

        return Ui.Rows(16, [3, 2], previewCard, checksCard);
    }

    private void RefreshPreview()
    {
        var text = DockerfileModel.Render(doc);
        if (preview.Text != text)
        {
            var offset = preview.Box.VerticalOffset;
            preview.Text = text;
            preview.Box.ScrollToVerticalOffset(offset);
        }
        if (syntaxSwitch != null && syntaxSwitch.IsChecked != doc.SyntaxHeader)
        {
            syncingSyntax = true;
            try { syntaxSwitch.IsChecked = doc.SyntaxHeader; } finally { syncingSyntax = false; }
        }
    }

    private void RefreshChecks()
    {
        checks.Children.Clear();
        var results = DockerfileLint.Check(doc);
        var problems = DockerfileLint.CountProblems(results);
        var errors = results.Count(r => r.Severity == LintSeverity.Error);
        checksCount.Text = problems == 0 ? Loc.T("All clear") : errors > 0 ? $"{errors} {Loc.T(errors == 1 ? "error" : "errors")} · {problems} {Loc.T("to review")}" : $"{problems} {Loc.T("to review")}";
        foreach (var result in results) checks.Children.Add(CheckRow(result));
        if (results.Count == 0) checks.Children.Add(Ui.Caption("No checks apply yet."));
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
        var where = result.StageIndex >= 0 && result.StageIndex < doc.Stages.Count
            ? Ui.Text($"{result.Id} · {(doc.Stages[result.StageIndex].Name.Trim().Length > 0 ? doc.Stages[result.StageIndex].Name.Trim() : "stage " + result.StageIndex)}" + (result.InstructionIndex >= 0 ? $" · {Loc.T("step")} {result.InstructionIndex + 1}" : ""), 12, null, "Outline", false, false)
            : Ui.Text(result.Id, 12, null, "Outline", false, false);
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
        if (expanded != null) FocusCard(expanded);
        else Dispatcher.BeginInvoke(() => baseImage.Focus(), System.Windows.Threading.DispatcherPriority.Loaded);
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
        Changed();
        Host.Notify.Info(Loc.T("Fix applied:") + " " + result.Title + ". " + Loc.T("Undo reverts it."));
    }
}
