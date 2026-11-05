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

/// <summary>Backup of one volume into a .tar.gz archive through a short-lived helper container (the Backup tab of the transfer dialog).</summary>
public static class VolumeBackupDialog
{
    private static AppHost Host => AppHost.Current;
    private static readonly string DefaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Docker volume backups");

    internal static TransferPanel BuildPanel(IReadOnlyList<VolumeRow> volumes, string? selected, Func<string?> context, Func<bool> isCurrent)
    {
        var volume = Ui.Combo(volumes.Select(v => v.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase), selected ?? volumes.FirstOrDefault()?.Name);
        var autoName = true;
        var archive = Ui.Input(Path.Combine(DefaultFolder, VolumeArchiveCommands.SuggestedArchiveName(volume.Text)), "Destination archive", true);
        var browse = Ui.IconButton(Icons.FolderOpen, "Choose the destination archive", () =>
        {
            if (PageKit.PickSavePath(Path.GetFileName(archive.Text), "Volume archives|*.tar.gz;*.tgz;*.tar|All files|*.*") is { } p) { autoName = false; archive.Text = p; }
        });
        var image = Ui.Input(VolumeArchiveCommands.DefaultHelperImage, "Helper image", true);
        var info = Ui.Card(Ui.Columns(10, [-18, 1], Ui.Icon(Icons.Info, 18, false, "OnSurfaceVariant"),
            Ui.Text("Runs a short-lived helper container that mounts the volume read-only and writes a tar.gz into the archive's folder. The helper image is pulled once if missing.", 13, null, "OnSurfaceVariant")), 12, "SurfaceContainerHigh", 12);
        var running = new List<string>();
        var stop = false;
        var stopSwitch = Ui.Switch("Pause containers using this volume", false, v => stop = v);
        var stopNote = Ui.Caption("");
        var preview = Ui.Mono("", 12, "OnSurfaceVariant", true);
        string? Problem(out string dir, out string name)
        {
            var path = archive.Text.Trim();
            dir = Path.GetDirectoryName(path) ?? ""; name = Path.GetFileName(path);
            return (volume.Text.Length == 0 ? "Choose a volume." : null) ?? VolumeArchiveCommands.ValidateHostFolder(dir) ?? VolumeArchiveCommands.ValidateArchiveName(name) ?? VolumeArchiveCommands.ValidateImage(image.Text.Trim());
        }
        void Update()
        {
            var problem = Problem(out var dir, out var name);
            preview.Text = problem ?? PageKit.Preview(VolumeArchiveCommands.Backup(volume.Text, dir, name, image.Text.Trim()), context());
        }
        async Task RefreshUsersAsync()
        {
            var name = volume.Text;
            var users = name.Length == 0 ? [] : await VolumeTransferChecks.RunningUsersAsync(name, context());
            if (name != volume.Text) return;
            running = users;
            stopSwitch.IsEnabled = running.Count > 0;
            if (running.Count == 0) { stop = false; stopSwitch.IsChecked = false; }
            stopNote.Text = running.Count == 0 ? Loc.T("No running container uses this volume.") : string.Join(", ", running) + " " + Loc.T("will be stopped, then started again after the copy.");
        }
        volume.SelectionChanged += (_, _) =>
        {
            if (autoName && volume.Text.Length > 0) archive.Text = Path.Combine(Path.GetDirectoryName(archive.Text.Trim()) is { Length: > 0 } d ? d : DefaultFolder, VolumeArchiveCommands.SuggestedArchiveName(volume.Text));
            Update();
            _ = RefreshUsersAsync();
        };
        archive.TextChanged += (_, _) => { if (archive.IsKeyboardFocusWithin) autoName = false; Update(); };
        image.TextChanged += (_, _) => Update();
        stopSwitch.IsEnabled = false;
        stopNote.Text = Loc.T("Checking running containers…");
        _ = RefreshUsersAsync();
        Update();
        var advanced = new Expander
        {
            Header = new TextBlock { Text = Loc.T("Helper image and command") },
            Content = Ui.VStack(10,
                Ui.Field("Helper image", image, "A small image with sh and tar that runs once with --rm and no network. It is pulled if missing.", "busybox:1.36"),
                Ui.Card(Ui.VStack(4, Ui.Overline("Command"), preview), 12, "SurfaceHigh", 12))
        };
        var body = Ui.VStack(14,
            Ui.Field("Volume", volume, "The volume to copy. It is mounted read-only in the helper container."),
            Ui.Field("Destination archive", Ui.Columns(8, [1, 0], archive, browse), "Full path of the .tar.gz, .tgz or .tar to create. The folder is bind-mounted into the helper container at /backup and resolved on the engine host: on Docker Desktop that is this computer.", "C:\\Backups\\docker\\pgdata-20261103.tar.gz"),
            info,
            Ui.VStack(2, stopSwitch, stopNote),
            advanced,
            Ui.Caption("Backups of remote engines are written on the remote machine."));

        return new TransferPanel
        {
            Body = body,
            ActionLabel = "Back up",
            Apply = async status =>
            {
                if (Problem(out var dir, out var name) is { } problem) return problem;
                var vol = volume.Text; var helper = image.Text.Trim();
                if (!isCurrent()) return PageKit.StaleMessage(context());
                if (!await VolumeTransferChecks.ConfirmHostPathsAsync(context(), dir)) return "Canceled: the folder would be resolved on the remote engine host.";
                var (local, _) = await VolumeTransferChecks.EngineLocationAsync(context());
                var target = Path.Combine(dir, name);
                if (local)
                {
                    Directory.CreateDirectory(dir);
                    if (File.Exists(target) && !await Host.Overlay.ConfirmAsync("Replace the existing archive?", $"{target} already exists and is overwritten by the backup.", "Replace", true)) return "Choose another archive name.";
                }
                var args = VolumeArchiveCommands.Backup(vol, Path.GetFullPath(dir), name, helper);
                // The helper reports no percentage, so the bar stays indeterminate and the line shows what has really been written.
                var started = DateTime.UtcNow;
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                void Tick()
                {
                    var written = local && File.Exists(target) ? " · " + Ui.FormatBytes(new FileInfo(target).Length) + " " + Loc.T("written") : "";
                    status(Loc.T("Writing archive") + written + " · " + (int)(DateTime.UtcNow - started).TotalSeconds + " s");
                }
                timer.Tick += (_, _) => { try { Tick(); } catch (IOException) { } };
                Tick(); timer.Start();
                Core.CommandResult? result;
                try { result = await VolumeTransferChecks.WithStoppedAsync(stop ? running : [], context(), () => Host.Execute("Back up volume " + vol, args, boundContext: context())); }
                finally { timer.Stop(); }
                if (result?.Success != true) return PageKit.Failure(result);
                var size = local && File.Exists(target) ? Ui.FormatBytes(new FileInfo(target).Length) : null;
                Host.Notify.Success(size != null ? $"Backup of {vol} written to {target} ({size})." : $"Backup of {vol} written on the engine host at {target}. Its size cannot be read from this computer.");
                if (!Host.IsCapture) Host.History.Record("storage", $"Backed up volume {vol} to {target}" + (size != null ? $" ({size})" : ""));
                return null;
            }
        };
    }
}
