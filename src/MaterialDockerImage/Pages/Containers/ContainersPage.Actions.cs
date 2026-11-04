using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Container operations. Each checks the snapshot, validates operands and reports the real process result.</summary>
public sealed partial class ContainersPage
{
    private static readonly Regex ContainerName = new(@"^[a-zA-Z0-9][a-zA-Z0-9_.-]*$", RegexOptions.None, TimeSpan.FromMilliseconds(100));

    private static string Verb(string action) => action switch
    {
        "start" => "Start", "stop" => "Stop", "restart" => "Restart", "pause" => "Pause", "unpause" => "Unpause", _ => action
    };

    internal async Task LifecycleAsync(string action, ContainerRow row)
    {
        if (!EnsureCurrent()) return;
        var problem = action switch
        {
            "start" when row.IsRunning => "is already running",
            "start" when row.IsPaused => "is paused; use Unpause",
            "stop" when row.IsStopped => "is not running",
            "pause" when !row.IsRunning => "is not running",
            "unpause" when !row.IsPaused => "is not paused",
            _ => null
        };
        if (problem != null) { Host.Notify.Info($"{row.Name} {problem} (as of the last refresh)."); return; }
        var result = await Host.Execute($"{Verb(action)} {row.Name}", ["container", action, row.Id], boundContext: SnapshotContext);
        if (result != null) await SafeRefresh();
    }

    private async Task BulkAsync(string action, IReadOnlyList<ContainerRow> rows)
    {
        if (!EnsureCurrent() || rows.Count == 0) return;
        var targets = action switch
        {
            "start" => rows.Where(r => r.IsStopped).ToList(),
            "stop" => rows.Where(r => r.IsRunning || r.IsRestarting).ToList(),
            _ => rows.Where(r => !r.IsPaused).ToList()
        };
        var skipped = rows.Count - targets.Count;
        if (targets.Count == 0) { Host.Notify.Info($"None of the selected containers can be {(action == "start" ? "started" : action == "stop" ? "stopped" : "restarted")} in their current state."); return; }
        if (action != "start" && !await Host.Overlay.ConfirmAsync($"{Verb(action)} {targets.Count} container(s)?", string.Join("\n", targets.Select(t => "• " + t.Name))
            + (skipped > 0 ? $"\n\n{skipped} selected container(s) are skipped because of their state." : ""), Verb(action))) return;
        if (!EnsureCurrent()) return;
        var failures = new List<string>();
        foreach (var t in targets)
        {
            var result = await Host.Execute($"{Verb(action)} {t.Name}", ["container", action, t.Id], boundContext: SnapshotContext);
            if (result == null) { failures.Add(t.Name + ": not started"); break; }
            if (!result.Success) failures.Add($"{t.Name}: {PageKit.Failure(result)}");
        }
        Report(Verb(action), targets.Count, failures);
        await SafeRefresh();
    }

    internal async Task KillAsync(ContainerRow row)
    {
        if (!EnsureCurrent()) return;
        if (!(row.IsRunning || row.IsPaused || row.IsRestarting)) { Host.Notify.Info($"{row.Name} is not running."); return; }
        var signal = Ui.Combo(["SIGKILL", "SIGTERM", "SIGINT", "SIGHUP", "SIGQUIT", "SIGUSR1", "SIGUSR2"], "SIGKILL", true);
        var form = Ui.VStack(12,
            Ui.Text($"Sends a signal to the main process of {row.Name}. SIGKILL ends it immediately without cleanup and can lose unsaved data; Stop is the graceful option.", 14, null, "OnSurfaceVariant", true, true),
            Ui.Field("Signal", signal, "The signal docker container kill sends. Names (SIGTERM) or numbers (15) are accepted.", "SIGTERM"));
        PageKit.Dialog(Loc.T("Kill") + " " + row.Name, null, form, "Kill", async () =>
        {
            var s = signal.Text.Trim().ToUpperInvariant();
            if (!Regex.IsMatch(s, @"^(SIG[A-Z0-9+-]+|[0-9]{1,2})$", RegexOptions.None, TimeSpan.FromMilliseconds(100))) return "Enter a signal name such as SIGTERM or a number such as 15.";
            if (!EnsureCurrent()) return PageKit.StaleMessage(SnapshotContext);
            var result = await Host.Execute($"Kill {row.Name} ({s})", ["container", "kill", "--signal", s, row.Id], boundContext: SnapshotContext);
            await SafeRefresh();
            return result?.Success == true ? null : "Kill did not complete. " + PageKit.Failure(result);
        }, danger: true);
    }

    private async Task RenameAsync(ContainerRow row)
    {
        if (!EnsureCurrent()) return;
        var name = await Host.Overlay.PromptAsync(Loc.T("Rename") + " " + row.Name, "New name", row.Name, "Container names must be unique on this engine and start with a letter or digit.",
            v => ContainerName.IsMatch(v) ? v == row.Name ? "Enter a different name." : null : "Use letters, digits, underscores, periods and hyphens, starting with a letter or digit.");
        if (name == null || !EnsureCurrent()) return;
        var result = await Host.Execute($"Rename {row.Name} to {name}", ["container", "rename", row.Id, name], boundContext: SnapshotContext);
        if (result != null) await SafeRefresh();
    }

    private async Task CommitAsync(ContainerRow row)
    {
        if (!EnsureCurrent()) return;
        var reference = Ui.Input("", "Image reference", true);
        var message = Ui.Input("", "Commit message");
        var author = Ui.Input("", "Author");
        var pause = Ui.Switch("Pause during commit", true, _ => { }, "Pauses the container while its filesystem is captured so the snapshot is consistent.");
        var form = Ui.VStack(12,
            Ui.Caption("docker container commit creates a new image from the container's current filesystem changes. Environment values and other configuration become part of the image; anyone who can pull it can read them. Volume contents are not included."),
            Ui.Field("Image reference", reference, "repository:tag for the new image.", "hello-api:debug-snapshot"),
            Ui.Field("Commit message", message, "Optional --message stored in the image history.", "State after reproducing issue 412"),
            Ui.Field("Author", author, "Optional --author.", "Jane Doe <jane@example.com>"),
            pause);
        PageKit.Dialog(Loc.T("Commit") + " " + row.Name + " to image", null, form, "Commit", async () =>
        {
            var r = reference.Text.Trim();
            if (PageKit.ValidateReference(r, false) is { } problem) return problem;
            var args = new List<string> { "container", "commit" };
            try
            {
                if (message.Text.Trim() is { Length: > 0 } m) { StructuredInputs.Token(m); args.AddRange(["--message", m]); }
                if (author.Text.Trim() is { Length: > 0 } a) { StructuredInputs.Token(a); DockerService.ValidateOperand(a, "author"); args.AddRange(["--author", a]); }
            }
            catch (ArgumentException e) { return e.Message; }
            if (pause.IsChecked != true) args.Add("--pause=false");
            args.Add(row.Id); args.Add(r);
            if (!EnsureCurrent()) return PageKit.StaleMessage(SnapshotContext);
            var result = await Host.Execute($"Commit {row.Name} as {r}", args, boundContext: SnapshotContext);
            return result?.Success == true ? null : "Commit did not complete. " + PageKit.Failure(result);
        });
        await Task.CompletedTask;
    }

    private async Task ExportAsync(ContainerRow row)
    {
        if (!EnsureCurrent()) return;
        var path = PageKit.PickSavePath(PageKit.SafeFileName(row.Name) + "-filesystem.tar", "Tar archive|*.tar|All files|*.*");
        if (path == null || !EnsureCurrent()) return;
        var result = await Host.Execute($"Export filesystem of {row.Name}", ["container", "export", "--output", Path.GetFullPath(path), row.Id], boundContext: SnapshotContext);
        if (result?.Success == true) Host.Notify.Info($"Filesystem written to {path}. It excludes volumes. Use Images → Import tarball to turn it into an image.");
    }

    internal async Task RemoveOneAsync(ContainerRow row)
    {
        if (!EnsureCurrent()) return;
        if (row.IsRunning || row.IsPaused || row.IsRestarting) { Host.Notify.Warning($"{row.Name} is {row.StateLabel}. Stop it before removing; this app does not force-remove running containers."); return; }
        var volumes = Ui.Switch("Also remove anonymous volumes", false, _ => { }, "docker container rm --volumes deletes unnamed volumes created for this container. Their data cannot be recovered. Named volumes are kept.");
        var form = Ui.VStack(12, Ui.Text($"Removes container {row.Name} ({row.ShortId}). Its writable layer is deleted; the image is kept.", 14, null, "OnSurfaceVariant", true, true), volumes);
        PageKit.Dialog(Loc.T("Remove") + " " + row.Name, null, form, "Remove", async () =>
        {
            var args = new List<string> { "container", "rm" };
            if (volumes.IsChecked == true)
            {
                if (!await Host.Overlay.SuperConfirmAsync("Remove container and its anonymous volumes",
                    "The container and every anonymous volume attached to it are deleted. Data in those volumes cannot be recovered.", [row.Name + "  (" + row.ShortId + ")", "anonymous volumes of " + row.Name])) return "Removal canceled.";
                args.Add("--volumes");
            }
            args.Add(row.Id);
            if (!EnsureCurrent()) return PageKit.StaleMessage(SnapshotContext);
            var result = await Host.Execute("Remove container " + row.Name, args, boundContext: SnapshotContext);
            await SafeRefresh();
            return result?.Success == true ? null : "Removal did not complete. " + PageKit.Failure(result);
        }, danger: true);
        await Task.CompletedTask;
    }

    private async Task RemoveManyAsync(IReadOnlyList<ContainerRow> rows)
    {
        if (!EnsureCurrent() || rows.Count == 0) return;
        var active = rows.Where(r => !r.IsStopped).ToList();
        var targets = rows.Where(r => r.IsStopped).ToList();
        if (targets.Count == 0) { Host.Notify.Warning("All selected containers are running, paused or restarting. Stop them first."); return; }
        var action = "Each container is removed with docker container rm (no force, volumes kept). Writable layers are deleted." +
            (active.Count > 0 ? $" {active.Count} active container(s) are skipped: " + string.Join(", ", active.Select(a => a.Name)) + "." : "");
        if (!await Host.Overlay.SuperConfirmAsync($"Remove {targets.Count} container(s)", action, targets.Select(t => $"{t.Name}  ({t.ShortId}, {t.Image})").ToList())) return;
        if (!EnsureCurrent()) return;
        var failures = new List<string>();
        foreach (var t in targets)
        {
            var result = await Host.Execute("Remove container " + t.Name, ["container", "rm", t.Id], boundContext: SnapshotContext);
            if (result == null) { failures.Add(t.Name + ": not started"); break; }
            if (!result.Success) failures.Add($"{t.Name}: {PageKit.Failure(result)}");
        }
        Report("Remove", targets.Count, failures);
        await SafeRefresh();
    }

    private async Task PruneAsync()
    {
        if (!EnsureCurrent()) return;
        var stopped = table.Items.Where(c => c.IsStopped).Select(c => $"{c.Name}  ({c.ShortId}, {c.Status})").ToList();
        if (stopped.Count == 0) { Host.Notify.Info("There are no stopped containers in the current list."); return; }
        if (!await Host.Overlay.SuperConfirmAsync("Remove all stopped containers",
            Loc.T("Runs docker container prune --force on context") + " " + (SnapshotContext ?? "default") + ". The engine decides the final set, which may differ if containers changed since the last refresh.", stopped)) return;
        if (!EnsureCurrent()) return;
        await Host.Execute("Prune stopped containers", ["container", "prune", "--force"], boundContext: SnapshotContext);
        await SafeRefresh();
    }

    private async Task RunContainerAsync()
    {
        if (!EnsureCurrent()) return;
        IReadOnlyList<System.Text.Json.JsonElement> images;
        try { images = await Host.ReadRows(["image", "ls", "--format", "{{json .}}"], SnapshotContext); }
        catch (Exception e) when (e is InvalidOperationException or System.Text.Json.JsonException) { Host.Notify.Error(Loc.T("Local images could not be listed:") + " " + e.Message); return; }
        var refs = images.Select(ImageRow.From).Where(i => i.Tagged).Select(i => i.Reference).Distinct().OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList();
        if (refs.Count == 0)
        {
            Host.Notify.Info("There are no tagged local images. Pull or build one first.");
            Host.Navigate("Images");
            return;
        }
        var filter = new SearchFilter("Search local images", 300);
        var list = new ListBox { Height = 280 };
        System.Windows.Automation.AutomationProperties.SetName(list, Loc.T("Local images"));
        void Render()
        {
            var keep = list.SelectedItem as string;
            list.ItemsSource = refs.Where(filter.Matches).ToList();
            list.SelectedItem = keep != null && filter.Matches(keep) ? keep : null;
            if (list.SelectedItem == null && list.Items.Count > 0) list.SelectedIndex = 0;
        }
        filter.Changed += Render;
        Render();
        Action? close = null;
        void Choose()
        {
            if (list.SelectedItem is not string image) return;
            close!();
            Ui.Guard(() => RunEditor.Show(image, SnapshotContext, SnapshotGeneration, SafeRefresh));
        }
        list.MouseDoubleClick += (_, _) => Choose();
        list.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { Choose(); e.Handled = true; } };
        var actions = Ui.HStack(8, Ui.Button("Cancel", null, ButtonKind.Text, () => close!()), Ui.Button("Continue", Icons.ArrowForward, ButtonKind.Filled, Choose));
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        var body = Ui.VStack(12, Ui.Title("Run container", 20), Ui.Caption($"Choose a local image on context {SnapshotContext ?? "default"}. The run editor opens next."), filter, list, actions);
        close = Host.Overlay.Show(body, 560);
    }

    /// <summary>Opens a menu with the actions not shown as header buttons.</summary>
    internal void ShowMoreMenu(FrameworkElement anchor, ContainerRow row)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        menu.Items.Add(Ui.MenuItem("Rename…", Icons.DriveFileRenameOutline, () => _ = Guarded(() => RenameAsync(row))));
        menu.Items.Add(Ui.MenuItem("Commit to image…", Icons.Commit, () => _ = Guarded(() => CommitAsync(row))));
        menu.Items.Add(Ui.MenuItem("Export filesystem…", Icons.FileExport, () => _ = Guarded(() => ExportAsync(row))));
        menu.Items.Add(Ui.MenuItem("Copy ID", Icons.ContentCopy, () => PageKit.Copy(row.Id, "the container ID")));
        menu.IsOpen = true;
    }

    private static void Report(string verb, int count, IReadOnlyList<string> failures)
    {
        if (failures.Count == 0) { if (count > 1) Host.Notify.Success($"{verb}: all {count} completed."); return; }
        Host.Notify.Error($"{verb}: {count - failures.Count} of {count} completed. Failed:\n" + string.Join("\n", failures.Take(8)) + (failures.Count > 8 ? $"\n…and {failures.Count - 8} more (see the output panel)." : ""));
    }
}
