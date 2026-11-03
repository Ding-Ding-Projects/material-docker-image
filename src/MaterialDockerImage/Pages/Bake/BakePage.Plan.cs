using System.Windows;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage.Pages;

/// <summary>Choosing the bake source and loading its plan (bake --list and bake --print).</summary>
public sealed partial class BakePage
{
    private string folder = "";
    /// <summary>The explicitly opened bake file, or empty when bake reads the default files of <see cref="folder"/>.</summary>
    private string bakeFile = "";
    private BakePlan? plan;
    private readonly List<BakeTargetCard> cards = new();
    private string? focusedTarget, selectedGroup;
    private bool triedResume;

    public override async Task RefreshAsync()
    {
        if (plan != null) { await LoadPlanAsync(); return; }
        if (triedResume || folder.Length > 0) return;
        triedResume = true;
        // Resume from the Build context folder when it holds a bake or Compose file.
        var candidate = Host.Settings.BuildFolder;
        if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate) && DefaultFilesIn(candidate).Count > 0)
            await OpenFolderAsync(Path.GetFullPath(candidate));
    }

    private static List<string> DefaultFilesIn(string directory) => BakePlan.DefaultFiles.Where(name => File.Exists(Path.Combine(directory, name))).ToList();

    private async Task ChooseFolderAsync()
    {
        var dialog = new OpenFolderDialog { Title = Loc.T("Choose a folder with a bake or Compose file") };
        if (Directory.Exists(folder)) dialog.InitialDirectory = folder;
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        await OpenFolderAsync(dialog.FolderName);
    }

    private async Task ChooseFileAsync()
    {
        var dialog = new OpenFileDialog { Title = Loc.T("Open a bake file"), Filter = "Bake and Compose files|*.hcl;*.json;compose.yaml;compose.yml;docker-compose.yml;docker-compose.yaml;*.yaml;*.yml|All files|*.*" };
        if (Directory.Exists(folder)) dialog.InitialDirectory = folder;
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var full = Path.GetFullPath(dialog.FileName);
        bakeFile = full;
        folder = Path.GetDirectoryName(full) ?? "";
        await LoadPlanAsync();
    }

    private async Task OpenFolderAsync(string path)
    {
        folder = path; bakeFile = "";
        if (DefaultFilesIn(folder).Count == 0)
        {
            plan = null;
            subtitle.Text = folder;
            ShowEmpty("No bake file found", "This folder has none of " + string.Join(", ", BakePlan.DefaultFiles) + ". Open another folder, or open a bake file with a different name.");
            return;
        }
        await LoadPlanAsync();
    }

    /// <summary>The --file arguments: the opened file, or nothing so bake reads the folder's default files itself.</summary>
    private List<string> FileArguments()
    {
        if (bakeFile.Length == 0) return [];
        DockerServiceGuard(bakeFile);
        return ["--file", bakeFile];
    }

    private static void DockerServiceGuard(string path)
    {
        if (path.StartsWith('-') || path.Any(char.IsControl)) throw new ArgumentException("The bake file path is not usable.");
    }

    private async Task LoadPlanAsync()
    {
        if (folder.Length == 0) { Host.Notify.Info("Open a folder or bake file first."); return; }
        if (!Directory.Exists(folder)) { ShowEmpty("Folder unavailable", "The folder " + folder + " no longer exists. Open another folder."); return; }
        if (bakeFile.Length > 0 && !File.Exists(bakeFile)) { ShowEmpty("Bake file unavailable", "The file " + bakeFile + " no longer exists. Open another file."); return; }
        var files = FileArguments();
        var context = Host.Context;
        subtitle.Text = (bakeFile.Length > 0 ? bakeFile : folder) + " · " + Loc.T("resolving plan…");

        // Ask bake for every target and group name first; older Buildx releases cannot, and then only the default group is printed.
        var list = await BuildRunner.ReadInFolderAsync(["buildx", "bake", .. files, "--list=targets"], context, folder);
        var names = list.Success ? BakePlan.ParseNames(list.StandardOutput) : [];
        var print = await BuildRunner.ReadInFolderAsync(["buildx", "bake", .. files, "--print", .. names], context, folder);
        if (!print.Success)
        {
            plan = null;
            var error = print.StandardError.Trim();
            subtitle.Text = bakeFile.Length > 0 ? bakeFile : folder;
            ShowEmpty("Bake could not resolve this file", (error.Length > 0 ? error : "docker buildx bake --print exited with code " + print.ExitCode) + Environment.NewLine + Environment.NewLine + Loc.T("Fix the file and choose Print plan, or open another folder."));
            return;
        }
        BakePlan parsed;
        try { parsed = BakePlan.Parse(print.StandardOutput); }
        catch (Exception e) when (e is FormatException or System.Text.Json.JsonException)
        {
            plan = null;
            ShowEmpty("Bake printed a plan this application cannot read", e.Message);
            return;
        }
        plan = parsed;
        listNote.Text = list.Success ? "" : Loc.T("This Buildx version cannot list every target (bake --list), so only the default group's targets are shown.");
        listNote.Visibility = list.Success ? Visibility.Collapsed : Visibility.Visible;
        if (selectedGroup != null && plan.Groups.All(g => g.Name != selectedGroup)) selectedGroup = null;
        if (focusedTarget != null && plan.Targets.All(t => t.Name != focusedTarget)) focusedTarget = null;
        RenderSource();
        RenderGroups();
        RebuildCards();
        detail.Show(plan.Targets.FirstOrDefault(t => t.Name == focusedTarget));
        subtitle.Text = $"{(bakeFile.Length > 0 ? bakeFile : folder)} · {plan.Groups.Count} {Loc.T(plan.Groups.Count == 1 ? "group" : "groups")} · {plan.Targets.Count} {Loc.T(plan.Targets.Count == 1 ? "target" : "targets")}";
        ShowWorkspace();
        UpdateActions();
    }

    private void RenderSource()
    {
        fileChips.Children.Clear();
        List<string> shown = bakeFile.Length > 0 ? [Path.GetFileName(bakeFile)] : DefaultFilesIn(folder);
        foreach (var name in shown) fileChips.Children.Add(Ui.Badge(name, Tone.Primary).Margin(0, 0, 6, 6));
        sourceCaption.Text = bakeFile.Length > 0
            ? Loc.T("Opened file. Relative contexts resolve from") + " " + folder
            : Loc.T("Bake reads these default files together, in this order, from") + " " + folder;
    }

    private void RenderGroups()
    {
        groupChips.Children.Clear();
        if (plan == null || plan.Groups.Count == 0) { groupChips.Children.Add(Ui.Caption("No groups in this plan.")); return; }
        foreach (var group in plan.Groups)
        {
            var chip = Ui.Chip($"{group.Name} · {group.Targets.Count}", group.Name == selectedGroup, on =>
            {
                selectedGroup = on ? group.Name : selectedGroup == group.Name ? null : selectedGroup;
                RenderGroups();
                UpdateCardStates();
                UpdateActions();
            });
            chip.ToolTip = string.Join(", ", group.Targets);
            chip.Margin = new Thickness(0, 0, 6, 6);
            groupChips.Children.Add(chip);
        }
    }

    private void RebuildCards()
    {
        var checkedNames = cards.Where(c => c.IsChecked).Select(c => c.Target.Name).ToHashSet(StringComparer.Ordinal);
        cards.Clear();
        foreach (var target in plan?.Targets ?? [])
        {
            var card = new BakeTargetCard(target, () => { focusedTarget = target.Name; detail.Show(target); tabs.SelectedItem = definitionTab; UpdateCardStates(); });
            card.IsChecked = checkedNames.Contains(target.Name);
            card.SelectionChanged += UpdateActions;
            cards.Add(card);
        }
        RenderTargets();
    }

    private void RenderTargets()
    {
        targetList.Children.Clear();
        foreach (var card in cards.Where(c => targetSearch.Matches(c.Target.Name + " " + string.Join(" ", c.Target.Tags) + " " + c.Target.Context + " " + c.Target.Dockerfile + " " + string.Join(" ", c.Target.Platforms))))
            targetList.Children.Add(card);
        if (targetList.Children.Count == 0)
            targetList.Children.Add(Ui.Caption(cards.Count == 0 ? "This plan has no targets." : "No targets match the search."));
        UpdateCardStates();
    }

    private void UpdateCardStates()
    {
        var members = plan?.Groups.FirstOrDefault(g => g.Name == selectedGroup)?.Targets ?? [];
        foreach (var card in cards) card.SetState(card.Target.Name == focusedTarget, members.Contains(card.Target.Name));
    }

    private void SetShownChecked(bool value)
    {
        foreach (var card in targetList.Children.OfType<BakeTargetCard>()) card.IsChecked = value;
    }
}
