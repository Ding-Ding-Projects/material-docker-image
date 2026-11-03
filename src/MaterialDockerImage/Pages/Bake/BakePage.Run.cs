using System.Windows;
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
        selectionText.Text = plan == null ? "" : $"{count} {Loc.T(count == 1 ? "target selected" : "targets selected")}" + (selectedGroup != null ? $" · {Loc.T("group")} {selectedGroup}" : "");
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
        foreach (var (key, value) in overrides.Pairs())
        {
            if (BakePlan.ValidateSetKey(key) is { } problem) throw new ArgumentException("Overrides: " + problem);
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
            if (multi.Count > 0 && !overrides.Pairs().Any(p => p.Key.EndsWith(".platform", StringComparison.Ordinal)))
                throw new InvalidOperationException("Load locally supports one platform per target, but these targets list several: " + string.Join(", ", multi) + ". Choose Push or As defined, or add a platform override.");
        }
        if (output == BakeOutput.Push)
        {
            var lines = resolved.Select(t => t.Name + ": " + (t.Tags.Count > 0 ? string.Join(", ", t.Tags) : Loc.T("no tags"))).ToList();
            if (!await Host.Overlay.ConfirmAsync("Push these targets to their registries?", string.Join(Environment.NewLine, lines) + Environment.NewLine + Environment.NewLine + Loc.T("Bake pushes with the existing Docker sign-in for each registry. Overrides can change tags."), "Bake and push")) return;
        }
        tabs.SelectedItem = runTab;
        timeline.SetResult(null);
        var label = "Bake " + string.Join(", ", operands);
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
        if (output == BakeOutput.Load && firstTag != null && !overrides.Pairs().Any(p => p.Key.EndsWith(".tags", StringComparison.Ordinal)))
            actions.Children.Add(Ui.Button("Run " + firstTag, Icons.PlayArrow, ButtonKind.Filled, () => { Host.HandOff["Images.RunImage"] = firstTag; Host.Navigate("Images"); }, false).Margin(0, 0, 8, 8));
        if (timeline.Progress.ResultDigest is { } digest)
            actions.Children.Add(Ui.Button("Copy digest", Icons.ContentCopy, ButtonKind.Outlined, () => { Clipboard.SetText(digest); Host.Notify.Info("Copied the digest."); }).Margin(0, 0, 8, 8));
        var title = Ui.HStack(10, Ui.Icon(Icons.GppGood, 22, true, "Success"), Ui.Text("Bake result", 16, FontWeights.SemiBold));
        return Ui.Card(Ui.VStack(8, title, Ui.Caption(Loc.T(destination) + " · " + targets.Count + " " + Loc.T(targets.Count == 1 ? "target" : "targets")), rows, actions.Children.Count > 0 ? actions : null), 16, "SurfaceContainer", 16);
    }
}
