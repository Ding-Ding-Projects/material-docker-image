using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Building selected targets or a group through the shared <see cref="BuildRunner"/>.</summary>
public sealed partial class BakePage
{
    private void UpdateActions()
    {
        var count = cards.Count(c => c.IsChecked);
        var idle = !BuildRunner.AnyActive;
        buildSelected.IsEnabled = idle && plan != null && count > 0;
        buildGroup.IsEnabled = idle && plan != null && selectedGroup != null;
        printPlan.IsEnabled = idle;
        SetButtonLabel(buildGroup, selectedGroup != null ? Loc.T("Build group") + " " + selectedGroup : Loc.T("Build group"));
        selectionText.Text = plan == null ? "" : $"{count} {Loc.T(count == 1 ? "target selected" : "targets selected")}" + (selectedGroup != null ? $" · {Loc.T("group")} {selectedGroup}" : "");
    }

    /// <summary>Replaces the label text of a button made by <see cref="Ui.Button"/> without rebuilding its icon.</summary>
    private static void SetButtonLabel(Button button, string text)
    {
        if (button.Content is DockPanel { Children.Count: > 0 } panel && panel.Children[^1] is TextBlock label)
        {
            label.Text = text;
            System.Windows.Automation.AutomationProperties.SetName(button, text);
        }
    }

    /// <summary>Splits the overrides box into <c>target.key=value</c> pairs; whitespace separates them and double quotes keep a value together.</summary>
    private List<(string Key, string Value)> OverridePairs()
    {
        var pairs = new List<(string Key, string Value)>();
        var text = overrideBox.Text;
        var token = new System.Text.StringBuilder();
        var quoted = false;
        void Flush()
        {
            if (token.Length == 0) return;
            var item = token.ToString();
            token.Clear();
            var equals = item.IndexOf('=');
            if (equals <= 0) throw new ArgumentException(Loc.T("Overrides:") + " \"" + item + "\" " + Loc.T("is not target.key=value."));
            pairs.Add((item[..equals], item[(equals + 1)..]));
        }
        foreach (var c in text)
        {
            if (c == '"') quoted = !quoted;
            else if (char.IsWhiteSpace(c) && !quoted) Flush();
            else token.Append(c);
        }
        if (quoted) throw new ArgumentException(Loc.T("Overrides:") + " " + Loc.T("a quote is not closed."));
        Flush();
        return pairs;
    }

    private async Task ApplyPathAsync()
    {
        var text = pathBox.Text.Trim().Trim('"');
        if (text.Length == 0) { Host.Notify.Info("Type a bake file or folder path first."); return; }
        string full;
        try { full = Path.GetFullPath(text); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { Host.Notify.Error("That path is not valid."); return; }
        if (Directory.Exists(full)) await OpenFolderAsync(full);
        else if (File.Exists(full))
        {
            bakeFile = full;
            folder = Path.GetDirectoryName(full) ?? "";
            await LoadPlanAsync();
        }
        else Host.Notify.Error(Loc.T("No file or folder exists at") + " " + full);
    }

    private async Task OpenInEditorAsync()
    {
        var path = bakeFile.Length > 0 ? bakeFile
            : folder.Length > 0 ? DefaultFilesIn(folder).OrderBy(name => name.StartsWith("docker-bake", StringComparison.Ordinal) ? 0 : 1).Select(name => Path.Combine(folder, name)).FirstOrDefault()
            : null;
        if (path == null) { Host.Notify.Info("Open a folder or bake file first."); return; }
        var problem = await ExternalEditor.OpenAsync(path, Host.Settings.ExternalEditor);
        if (problem != null) Host.Notify.Error(problem);
    }

    private Task BuildSelectedAsync()
    {
        var names = cards.Where(c => c.IsChecked).Select(c => c.Target.Name).ToList();
        if (names.Count == 0) throw new InvalidOperationException("Select at least one target with its check box.");
        return BakeAsync(names, names);
    }

    private Task BuildGroupAsync()
    {
        if (plan?.Groups.FirstOrDefault(g => g.Name == selectedGroup) is not { } group) throw new InvalidOperationException("Choose a group chip first.");
        return BakeAsync([group.Name], group.Targets);
    }

    /// <param name="operands">Names passed to bake (targets or one group).</param>
    /// <param name="targets">The targets those names resolve to, used for confirmations and the result summary.</param>
    private async Task BakeAsync(IReadOnlyList<string> operands, IReadOnlyList<string> targets)
    {
        if (plan == null) throw new InvalidOperationException("Print the plan first.");
        if (!Directory.Exists(folder)) throw new InvalidOperationException("The bake folder no longer exists. Open it again.");
        var args = new List<string> { "buildx", "bake", "--progress", "plain" };
        args.AddRange(FileArguments());
        if (output == BakeOutput.Load) args.Add("--load");
        if (output == BakeOutput.Push) args.Add("--push");
        lastOverrides = OverridePairs();
        foreach (var (key, value) in lastOverrides)
        {
            if (BakePlan.ValidateSetKey(key) is { } problem) throw new ArgumentException(Loc.T("Overrides:") + " " + problem);
            if (value.Any(char.IsControl)) throw new ArgumentException("Overrides: values cannot contain control characters.");
            args.AddRange(["--set", key + "=" + value]);
        }
        foreach (var name in operands)
        {
            if (!BakePlan.IsName(name)) throw new ArgumentException("Target and group names use letters, digits, '_' and '-'.");
            args.Add(name);
        }
        var resolved = plan.Targets.Where(t => targets.Contains(t.Name)).ToList();
        if (output == BakeOutput.Load)
        {
            var multi = resolved.Where(t => t.Platforms.Count > 1).Select(t => t.Name).ToList();
            if (multi.Count > 0 && !lastOverrides.Any(p => p.Key.EndsWith(".platform", StringComparison.Ordinal)))
                throw new InvalidOperationException(Loc.T("Load locally supports one platform per target, but these targets list several:") + " " + string.Join(", ", multi) + ". Choose Push or As defined, or add a platform override.");
        }
        if (output == BakeOutput.Push)
        {
            var lines = resolved.Select(t => t.Name + ": " + (t.Tags.Count > 0 ? string.Join(", ", t.Tags) : Loc.T("no tags"))).ToList();
            if (!await Host.Overlay.ConfirmAsync("Push these targets to their registries?", string.Join(Environment.NewLine, lines) + Environment.NewLine + Environment.NewLine + Loc.T("Bake pushes with the existing Docker sign-in for each registry. Overrides can change tags."), "Bake and push")) return;
        }
        detail.ShowView(BakeTargetDetail.View.Run);
        timeline.SetResult(null);
        var label = Loc.T("Bake") + " " + string.Join(", ", operands);
        var result = await runner.RunAsync(label, args, Host.Context, folder, string.Join(", ", resolved.SelectMany(t => t.Tags)));
        if (result?.Success == true) timeline.SetResult(ResultCard(resolved));
    }

    private FrameworkElement ResultCard(IReadOnlyList<BakeTarget> targets)
    {
        var rows = new System.Windows.Controls.StackPanel();
        foreach (var target in targets)
            Ui.Add(rows, Ui.Columns(12, [-140, 1], Ui.Mono(target.Name, 13), Ui.Mono(target.Tags.Count > 0 ? string.Join(", ", target.Tags) : Loc.T("no tags in the plan"), 12, "OnSurfaceVariant", true)), 6);
        var destination = output switch { BakeOutput.Load => "Loaded into this engine", BakeOutput.Push => "Pushed to the registries", _ => "Written to the outputs the bake file defines" };
        var actions = new System.Windows.Controls.WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var firstTag = targets.SelectMany(t => t.Tags).FirstOrDefault();
        if (output == BakeOutput.Load && firstTag != null && !lastOverrides.Any(p => p.Key.EndsWith(".tags", StringComparison.Ordinal)))
            actions.Children.Add(Ui.Button("Run " + firstTag, Icons.PlayArrow, ButtonKind.Filled, () => { Host.HandOff["Images.RunImage"] = firstTag; Host.Navigate("Images"); }, false).Margin(0, 0, 8, 8));
        if (timeline.Progress.ResultDigest is { } digest)
            actions.Children.Add(Ui.Button("Copy digest", Icons.ContentCopy, ButtonKind.Outlined, () => { ActionSounds.Copy(digest); Host.Notify.Info("Copied the digest."); }).Margin(0, 0, 8, 8));
        var title = Ui.HStack(10, Ui.Icon(Icons.GppGood, 22, true, "Success"), Ui.Text("Bake result", 16, FontWeights.SemiBold));
        return Ui.Card(Ui.VStack(8, title, Ui.Caption(Loc.T(destination) + " · " + targets.Count + " " + Loc.T(targets.Count == 1 ? "target" : "targets")), rows, actions.Children.Count > 0 ? actions : null), 16, "SurfaceContainer", 16);
    }
}
