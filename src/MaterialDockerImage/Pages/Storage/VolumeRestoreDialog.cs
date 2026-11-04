using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Restore an archive into an existing or new volume, and clone a volume into a new one.</summary>
public static class VolumeRestoreDialog
{
    private static AppHost Host => AppHost.Current;

    public static void Show(string? targetVolume, IReadOnlyList<string> existing, Func<string?> context, Func<bool> isCurrent, Func<Task> reload)
    {
        var archive = Ui.Input("", "Archive", true);
        var browse = Ui.IconButton(Icons.FolderOpen, "Choose the archive", () => { if (PageKit.PickOpenPath("Choose a volume archive", "Volume archives|*.tar.gz;*.tgz;*.tar|All files|*.*") is { } p) archive.Text = p; });
        var target = Ui.Combo(existing.OrderBy(n => n, StringComparer.OrdinalIgnoreCase), targetVolume, editable: true);
        if (targetVolume == null) target.Text = "";
        var image = Ui.Input(VolumeArchiveCommands.DefaultHelperImage, "Helper image", true);
        var replace = false;
        var replaceSwitch = Ui.Switch("Replace current contents", false, v => replace = v, "Deletes every file in the target volume before extracting. Without it, archive files are added and same-named files overwritten.");
        replaceSwitch.SetResourceReference(Control.ForegroundProperty, "Error");
        var state = Ui.Caption("");
        var preview = Ui.Mono("", 12, "OnSurfaceVariant", true);
        bool IsNew() => !existing.Contains(target.Text.Trim(), StringComparer.Ordinal);
        string? Problem(out string dir, out string file)
        {
            var path = archive.Text.Trim();
            dir = Path.GetDirectoryName(path) ?? ""; file = Path.GetFileName(path);
            return (path.Length == 0 ? "Choose an archive." : null)
                ?? VolumeArchiveCommands.ValidateHostFolder(dir) ?? VolumeArchiveCommands.ValidateArchiveName(file)
                ?? VolumeArchiveCommands.ValidateVolumeName(target.Text.Trim()) ?? VolumeArchiveCommands.ValidateImage(image.Text.Trim());
        }
        void Update()
        {
            var isNew = IsNew();
            replaceSwitch.IsEnabled = !isNew;
            state.Text = target.Text.Trim().Length == 0 ? "" : isNew ? Loc.T("A new volume with this name is created first.") : Loc.T("Restores into the existing volume.");
            var problem = Problem(out var dir, out var file);
            preview.Text = problem ?? PageKit.Preview(VolumeArchiveCommands.Restore(target.Text.Trim(), dir, file, image.Text.Trim(), replace && !isNew), context());
        }
        archive.TextChanged += (_, _) => Update(); image.TextChanged += (_, _) => Update();
        target.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Update()));
        target.SelectionChanged += (_, _) => Dispatcher(Update);
        replaceSwitch.Checked += (_, _) => Update(); replaceSwitch.Unchecked += (_, _) => Update();
        var body = Ui.VStack(14,
            Ui.Field("Archive", Ui.Columns(8, [1, 0], archive, browse), "A .tar.gz, .tgz or .tar made by Back up or by tar. Its folder is bind-mounted read-only at /backup; the path is resolved on the engine host.", "C:\\Backups\\docker\\pgdata-20261103.tar.gz"),
            Ui.Field("Target volume", target, "Choose an existing volume or type a new name.", "pgdata-restored"), state,
            replaceSwitch,
            Ui.Field("Helper image", image, "A small image with sh, tar and find that runs once with --rm and no network.", "busybox:1.36"),
            Ui.Card(Ui.VStack(4, Ui.Overline("Command"), preview), 12, "SurfaceHigh", 12),
            Ui.Caption("Stop containers that use the target volume before restoring, so they do not read half-written files."));
        PageKit.Dialog("Restore volume", "Extracts an archive into a volume through a helper container.", body, "Restore", async () =>
        {
            if (Problem(out var dir, out var file) is { } problem) return problem;
            if (!isCurrent()) return PageKit.StaleMessage(context());
            var name = target.Text.Trim(); var helper = image.Text.Trim();
            var isNew = IsNew();
            var wipe = replace && !isNew;
            if (!await VolumeTransferChecks.ConfirmHostPathsAsync(context(), dir)) return "Canceled: the archive path would be resolved on the remote engine host.";
            var (local, _) = await VolumeTransferChecks.EngineLocationAsync(context());
            if (local && !File.Exists(archive.Text.Trim())) return "The archive does not exist on this computer.";
            List<string> users = isNew ? new() : await VolumeTransferChecks.RunningUsersAsync(name, context());
            var usersNote = users.Count > 0 ? $" Running containers use this volume: {string.Join(", ", users)}." : "";
            if (wipe)
            {
                if (!await Host.Overlay.SuperConfirmAsync(Loc.T("Replace the contents of") + " " + name, "Every file currently in the volume is deleted, then the archive is extracted. This cannot be undone." + usersNote, [$"volume {name}: all current files", $"archive {file}"])) return "Restore canceled.";
            }
            else if (!isNew && !await Host.Overlay.ConfirmAsync(Loc.T("Restore into") + " " + name, "Files from the archive are added to the volume and overwrite files with the same path." + usersNote, "Restore", true)) return "Restore canceled.";
            if (!isCurrent()) return PageKit.StaleMessage(context());
            if (isNew)
            {
                var created = await Host.Execute("Create volume " + name, ["volume", "create", name], boundContext: context());
                if (created?.Success != true) return PageKit.Failure(created);
            }
            var result = await Host.Execute("Restore volume " + name, VolumeArchiveCommands.Restore(name, Path.GetFullPath(dir), file, helper, wipe), boundContext: context());
            await reload();
            if (result?.Success != true) return PageKit.Failure(result);
            if (!Host.IsCapture) Host.History.Record("storage", $"Restored {file} into volume {name}" + (wipe ? " (replaced contents)" : isNew ? " (new volume)" : " (merged)"));
            return null;
        }, width: 640);
        Update();
    }

    private static void Dispatcher(Action action) => Application.Current.Dispatcher.BeginInvoke(action);

    /// <summary>Copies a volume into a new volume (create, then a tar stream between two mounts in one helper container).</summary>
    public static async Task CloneAsync(VolumeRow source, IReadOnlyList<string> existing, Func<string?> context, Func<bool> isCurrent, Func<Task> reload)
    {
        if (!isCurrent()) { Host.Notify.Error(PageKit.StaleMessage(context())); return; }
        var name = await Host.Overlay.PromptAsync(Loc.T("Clone volume") + " " + source.Name, "New volume name", source.Name + "-copy",
            "A new volume is created and every file is copied into it with a helper container (" + VolumeArchiveCommands.DefaultHelperImage + "). The source is mounted read-only. No host folder is used, so this also works on remote engines.",
            v => VolumeArchiveCommands.ValidateVolumeName(v) ?? (existing.Contains(v, StringComparer.Ordinal) ? "A volume with this name already exists. Clone only creates new volumes." : null));
        if (name == null) return;
        var users = await VolumeTransferChecks.RunningUsersAsync(source.Name, context());
        if (users.Count > 0 && !await Host.Overlay.ConfirmAsync("Clone a volume in use", $"Running containers use {source.Name}: {string.Join(", ", users)}. Files that change during the copy may be inconsistent in the clone. Stop them first for a consistent copy.", "Clone anyway")) return;
        if (!isCurrent()) { Host.Notify.Error(PageKit.StaleMessage(context())); return; }
        var created = await Host.Execute("Create volume " + name, ["volume", "create", name], boundContext: context());
        if (created?.Success != true) { await reload(); return; }
        var result = await Host.Execute($"Clone volume {source.Name} to {name}", VolumeArchiveCommands.Clone(source.Name, name, VolumeArchiveCommands.DefaultHelperImage), boundContext: context());
        if (result?.Success == true && !Host.IsCapture) Host.History.Record("storage", $"Cloned volume {source.Name} to {name}");
        else if (result?.Success != true) Host.Notify.Warning($"The new volume {name} exists but the copy did not complete. Inspect or remove it before retrying.");
        await reload();
    }
}
