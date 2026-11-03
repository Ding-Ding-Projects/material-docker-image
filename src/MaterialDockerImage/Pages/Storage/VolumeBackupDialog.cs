using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage.Pages;

/// <summary>Shared checks for volume archive operations: where the engine runs, and which running containers use a volume.</summary>
public static class VolumeTransferChecks
{
    private static AppHost Host => AppHost.Current;

    /// <summary>Reads the docker endpoint of the context and reports whether it is on this computer.</summary>
    public static async Task<(bool Local, string Endpoint)> EngineLocationAsync(string? context)
    {
        var args = new List<string> { "context", "inspect" };
        if (!string.IsNullOrWhiteSpace(context)) args.Add(context);
        var r = await ContextActions.ReadLocal(args);
        if (!r.Success) return (true, "");
        try
        {
            using var doc = JsonDocument.Parse(r.StandardOutput);
            var first = doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0 ? doc.RootElement[0] : doc.RootElement;
            var endpoint = CliValues.Str(first, "Endpoints", "docker", "Host");
            return (ContextEndpoint.IsLocal(endpoint), endpoint);
        }
        catch (JsonException) { return (true, ""); }
    }

    /// <summary>Bind paths resolve on the engine host. On a remote engine the user must confirm before continuing.</summary>
    public static async Task<bool> ConfirmHostPathsAsync(string? context, string folder)
    {
        var (local, endpoint) = await EngineLocationAsync(context);
        if (local) return true;
        return await Host.Overlay.ConfirmAsync("The engine is not on this computer",
            $"Context {context ?? "current"} points at {endpoint}. The folder {folder} is resolved on that engine host, not on this computer, so the archive is read from or written to the remote machine. Continue only if that path exists there.",
            "Continue on the engine host", true);
    }

    public static async Task<List<string>> RunningUsersAsync(string volume, string? context)
    {
        try
        {
            var rows = await Host.ReadRows(["container", "ls", "--filter", "volume=" + volume, "--filter", "status=running", "--format", "{{json .}}"], context);
            return rows.Select(r => CliValues.Str(r, "Names")).Where(n => n.Length > 0).ToList();
        }
        catch (InvalidOperationException) { return []; }
    }

    public static Grid FolderPicker(TextBox box, string title)
    {
        var browse = Ui.IconButton(Icons.FolderOpen, title, () =>
        {
            var dialog = new OpenFolderDialog { Title = Loc.T(title) };
            if (Directory.Exists(box.Text)) dialog.InitialDirectory = box.Text;
            if (dialog.ShowDialog() == true) box.Text = dialog.FolderName;
        });
        return Ui.Columns(8, [1, 0], box, browse);
    }

    /// <summary>Stops containers, runs the work, and always starts the stopped containers again.</summary>
    public static async Task<Core.CommandResult?> WithStoppedAsync(IReadOnlyList<string> containers, string? context, Func<Task<Core.CommandResult?>> work)
    {
        if (containers.Count == 0) return await work();
        var stop = await Host.Execute($"Stop {containers.Count} container(s) for the volume operation", ["container", "stop", .. containers], boundContext: context);
        if (stop?.Success != true) { Host.Notify.Error("The containers did not stop, so the volume operation was not started."); return stop; }
        try { return await work(); }
        finally
        {
            var start = await Host.Execute($"Start {containers.Count} container(s) again", ["container", "start", .. containers], boundContext: context);
            if (start?.Success != true) Host.Notify.Warning("These containers were stopped for the volume operation and did not start again: " + string.Join(", ", containers) + ". Start them from Containers.");
        }
    }
}

/// <summary>Backup of one volume into a .tar.gz on a folder through a short-lived helper container.</summary>
public static class VolumeBackupDialog
{
    private static AppHost Host => AppHost.Current;

    public static async Task ShowAsync(VolumeRow volume, Func<string?> context, Func<bool> isCurrent)
    {
        var running = await VolumeTransferChecks.RunningUsersAsync(volume.Name, context());
        var folder = Ui.Input(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Docker volume backups"), "Destination folder", true);
        var archive = Ui.Input(VolumeArchiveCommands.SuggestedArchiveName(volume.Name), "Archive name", true);
        var image = Ui.Input(VolumeArchiveCommands.DefaultHelperImage, "Helper image", true);
        var stop = false;
        var stopSwitch = Ui.Switch("Stop containers using this volume during backup", false, v => stop = v,
            running.Count == 0 ? "No running container uses this volume." : "Stops and afterwards starts: " + string.Join(", ", running) + ". Gives a consistent copy of databases and other files that change while running.");
        stopSwitch.IsEnabled = running.Count > 0;
        var preview = Ui.Mono("", 12, "OnSurfaceVariant", true);
        void Update()
        {
            var problem = VolumeArchiveCommands.ValidateHostFolder(folder.Text.Trim()) ?? VolumeArchiveCommands.ValidateArchiveName(archive.Text.Trim()) ?? VolumeArchiveCommands.ValidateImage(image.Text.Trim());
            preview.Text = problem ?? PageKit.Preview(VolumeArchiveCommands.Backup(volume.Name, folder.Text.Trim(), archive.Text.Trim(), image.Text.Trim()), context());
        }
        folder.TextChanged += (_, _) => Update(); archive.TextChanged += (_, _) => Update(); image.TextChanged += (_, _) => Update();
        var body = Ui.VStack(14,
            Ui.Card(Ui.HStack(10, Ui.Icon(Icons.Backup, 22, true, "Primary"), Ui.VStack(2, Ui.Mono(volume.Name, 14, "OnSurface", true), Ui.Caption($"{volume.Driver} · {volume.Size} · used by {volume.UsedByText}"))), 12, "SurfaceContainer", 12),
            Ui.Field("Destination folder", VolumeTransferChecks.FolderPicker(folder, "Choose the destination folder"), "Bind-mounted into the helper container at /backup. The path is resolved on the engine host: on Docker Desktop that is this computer.", "C:\\Backups\\docker"),
            Ui.Field("Archive name", archive, "Created in the destination folder. .tar.gz and .tgz are compressed; .tar is not.", "pgdata-20261103.tar.gz"),
            Ui.Field("Helper image", image, "A small image with sh and tar that runs once with --rm and no network. It is pulled if missing.", "busybox:1.36"),
            stopSwitch,
            Ui.Card(Ui.VStack(4, Ui.Overline("Command"), preview), 12, "SurfaceHigh", 12),
            Ui.Caption("Bind paths are on the engine host. Backups of remote engines are written on the remote machine."));
        Update();
        PageKit.Dialog("Back up volume", "Creates a compressed archive of every file in the volume.", body, "Back up", async () =>
        {
            var dir = folder.Text.Trim(); var name = archive.Text.Trim(); var helper = image.Text.Trim();
            if ((VolumeArchiveCommands.ValidateHostFolder(dir) ?? VolumeArchiveCommands.ValidateArchiveName(name) ?? VolumeArchiveCommands.ValidateImage(helper)) is { } problem) return problem;
            if (!isCurrent()) return PageKit.StaleMessage(context());
            if (!await VolumeTransferChecks.ConfirmHostPathsAsync(context(), dir)) return "Canceled: the folder would be resolved on the remote engine host.";
            var (local, _) = await VolumeTransferChecks.EngineLocationAsync(context());
            var target = Path.Combine(dir, name);
            if (local)
            {
                Directory.CreateDirectory(dir);
                if (File.Exists(target) && !await Host.Overlay.ConfirmAsync("Replace the existing archive?", $"{target} already exists and is overwritten by the backup.", "Replace", true)) return "Choose another archive name.";
            }
            var args = VolumeArchiveCommands.Backup(volume.Name, Path.GetFullPath(dir), name, helper);
            var result = await VolumeTransferChecks.WithStoppedAsync(stop ? running : [], context(), () => Host.Execute("Back up volume " + volume.Name, args, boundContext: context()));
            if (result?.Success != true) return PageKit.Failure(result);
            var size = local && File.Exists(target) ? Ui.FormatBytes(new FileInfo(target).Length) : null;
            Host.Notify.Success(size != null ? $"Backup of {volume.Name} written to {target} ({size})." : $"Backup of {volume.Name} written on the engine host at {target}. Its size cannot be read from this computer.");
            if (!Host.IsCapture) Host.History.Record("storage", $"Backed up volume {volume.Name} to {target}" + (size != null ? $" ({size})" : ""));
            return null;
        }, width: 640);
    }
}
