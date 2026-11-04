using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage.Pages;

/// <summary>Compose v2 tools of the project panel: env files, watch, top, images, events, copy and one-shot exec.</summary>
public sealed partial class ComposeProjectPanel
{
    private readonly List<string> envFiles = new();
    private readonly WrapPanel envChips = new();
    private readonly List<Button> fileOnlyTools = new();

    private void BuildTools()
    {
        Button Tool(string label, string icon, Func<Task> run, bool needsFiles, string tip)
        {
            var b = Ui.AsyncButton(label, icon, ButtonKind.Outlined, run);
            b.ToolTip = Loc.T(tip);
            b.Margin = new Thickness(0, 0, 8, 8);
            if (needsFiles) fileOnlyTools.Add(b);
            tools.Children.Add(b);
            return b;
        }
        Tool("Watch", Icons.Visibility, WatchAsync, true, "docker compose watch: rebuild or sync services when files change (needs develop.watch in the file)");
        Tool("Top", Icons.Speed, TopAsync, false, "docker compose top: processes running in each service container");
        Tool("Images", Icons.Layers, ImagesAsync, false, "docker compose images: the image each container uses");
        Tool("Events", Icons.EventNote, EventsAsync, false, "docker compose events --json: live container events of this project");
        Tool("Copy files…", Icons.SwapHoriz, CopyAsync, false, "docker compose cp: copy between a service container and this computer");
        Tool("Exec…", Icons.Terminal, ExecAsync, false, "docker compose exec -T: run one command in a running service container");
    }

    private void RenderEnvFiles()
    {
        envChips.Children.Clear();
        if (target?.DefaultEnvFile is { } detected)
        {
            var chip = Ui.Badge(".env " + Loc.T("detected"), Tone.Success);
            chip.ToolTip = detected + " · " + Loc.T("read by Compose for variable substitution");
            chip.Margin = new Thickness(0, 0, 8, 6);
            envChips.Children.Add(chip);
        }
        foreach (var file in envFiles.ToList())
        {
            var f = file;
            var remove = Ui.IconButton(Icons.Close, Loc.T("Remove env file") + " " + Path.GetFileName(f), () => { envFiles.Remove(f); ApplyEnvFiles(); });
            remove.Width = remove.Height = remove.MinHeight = 24; remove.FontSize = 14;
            var label = Ui.Mono("--env-file " + Path.GetFileName(f), 12, "OnPrimaryContainer");
            var chip = new Border { Child = Ui.HStack(4, label, remove), CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 2, 4, 2), Margin = new Thickness(0, 0, 8, 6), ToolTip = f };
            chip.SetResourceReference(Border.BackgroundProperty, "PrimaryContainer");
            envChips.Children.Add(chip);
        }
        if (target is { HasFiles: true } && target.DefaultEnvFile == null && envFiles.Count == 0)
        {
            var none = Ui.Badge(Loc.T("No .env in the project directory"), Tone.Neutral);
            none.Margin = new Thickness(0, 0, 8, 6);
            envChips.Children.Add(none);
        }
        var add = Ui.Button("Add env file…", Icons.Add, ButtonKind.Text, () =>
        {
            if (target is not { HasFiles: true }) { Host.Notify.Error("Env files apply to projects whose Compose file is on this computer."); return; }
            var dialog = new OpenFileDialog { Title = Loc.T("Choose an env file"), Filter = "Env files|*.env;.env*;*.env.*|All files|*.*", InitialDirectory = target.Directory ?? "" };
            if (dialog.ShowDialog() != true) return;
            if (!envFiles.Contains(dialog.FileName, StringComparer.OrdinalIgnoreCase)) envFiles.Add(dialog.FileName);
            ApplyEnvFiles();
        });
        add.Margin = new Thickness(0, -4, 0, 4);
        envChips.Children.Add(add);
    }

    private void ApplyEnvFiles()
    {
        if (target == null) return;
        target = target with { EnvFiles = envFiles.ToList() };
        RenderEnvFiles();
        _ = SafeLoadDetails();
    }

    private void ShowOutput(string title, CommandResult r, string subtitle)
    {
        var text = r.Success ? r.StandardOutput : (r.StandardError + "\n" + r.StandardOutput).Trim();
        ResourceTextViewer.Show(title, text.Trim().Length == 0 ? Loc.T("The command printed no output.") : text, r.Success ? subtitle : $"{subtitle} · failed with exit code {r.ExitCode}");
    }

    private List<string> ServiceNames() => services.Items.Select(s => s.Service).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();

    private async Task WatchAsync()
    {
        if (Refuse(true)) return;
        var config = await Host.Read(target!.Args(activeProfiles, "config", "--format", "json"), Context());
        if (!config.Success) { ShowOutput("Compose config is invalid", config, "Watch needs a valid Compose file"); return; }
        IReadOnlyList<string> watched;
        try { watched = ComposeWatchSupport.ServicesWithWatch(config.StandardOutput); }
        catch (JsonException e) { Host.Notify.Error(Loc.T("The resolved Compose model could not be read:") + " " + e.Message); return; }
        if (watched.Count == 0)
        {
            Action? close = null;
            var body = Ui.VStack(14,
                Ui.Title("Nothing to watch", 20),
                Ui.Text("No service in this project declares develop.watch rules, so docker compose watch would exit without watching anything. Add a develop section to the services you want to rebuild or sync on change, save, and try again.", 14, null, "OnSurfaceVariant", true, true),
                Ui.Card(Ui.VStack(4, Ui.Overline("Worked example"), Ui.Mono("services:\n  web:\n    build: .\n    develop:\n      watch:\n        - action: sync\n          path: ./src\n          target: /app/src\n        - action: rebuild\n          path: package.json", 12, "OnSurface", true)), 12, "SurfaceHigh", 12),
                Ui.Button("Close", null, ButtonKind.Filled, () => close?.Invoke()).Margin(0, 4, 0, 0));
            close = Host.Overlay.Show(body, 560);
            return;
        }
        if (Refuse(true)) return;
        ResourceTextViewer.ShowStream("Watch · " + entry!.Name, "docker compose watch · watching " + string.Join(", ", watched) + " · Stop ends watching; started services keep running",
            target.Args(activeProfiles, "watch"), Context(), "Stop", "Watching. Builds, syncs and restarts appear here as files change.", "Watch ended.", "Stopped watching. Services started by watch keep running; use Stop or Down to end them.");
    }

    private async Task TopAsync()
    {
        if (target == null || entry == null) { Host.Notify.Info("Select a Compose project first."); return; }
        var r = await Host.Read(target.Args(activeProfiles, "top"), Context());
        ShowOutput(Loc.T("Processes ·") + " " + entry.Name, r, "docker compose top · one table per running container");
    }

    private async Task ImagesAsync()
    {
        if (target == null || entry == null) { Host.Notify.Info("Select a Compose project first."); return; }
        IReadOnlyList<JsonElement> rows;
        try { rows = await Host.ReadRows(target.Args(activeProfiles, "images", "--format", "json"), Context()); }
        catch (Exception e) when (e is InvalidOperationException or JsonException) { Host.Notify.Error("docker compose images failed: " + e.Message); return; }
        var table = new DataTable<JsonElement>("Compose images", "Search images");
        table.EmptyTitle = Loc.T("No images");
        table.EmptyMessage = Loc.T("No container of this project exists yet, so Compose reports no images. Run Up first.");
        string Size(JsonElement x) => CliValues.Get(x, "Size") is { ValueKind: JsonValueKind.Number } ? Ui.FormatBytes(CliValues.Long(x, "Size")) : CliValues.Str(x, "Size");
        string ShortId(JsonElement x) { var id = CliValues.Str(x, "ID").Replace("sha256:", ""); return id.Length > 12 ? id[..12] : id; }
        table.Column("Container", x => CliValues.Str(x, "ContainerName"), 200, true)
            .Column("Repository", x => CliValues.Str(x, "Repository"), 220, true)
            .Column("Tag", x => CliValues.Str(x, "Tag"), 110, true)
            .Column("Image ID", ShortId, 120, true)
            .Column("Platform", x => CliValues.Str(x, "Platform"), 110)
            .Column("Size", Size, 90);
        table.SetItems(rows);
        Action? close = null;
        var body = Ui.Rows(12, [0, 1, 0], Ui.VStack(2, Ui.Title("Images · " + entry.Name, 20), Ui.Caption($"docker compose images · {rows.Count} container{(rows.Count == 1 ? "" : "s")}")), table,
            Ui.Button("Close", null, ButtonKind.Filled, () => close?.Invoke()).Margin(0, 0, 0, 0));
        body.Height = 480;
        close = Host.Overlay.Show(body, 900);
    }

    private Task EventsAsync()
    {
        if (target == null || entry == null) { Host.Notify.Info("Select a Compose project first."); return Task.CompletedTask; }
        if (!IsCurrent()) { Host.Notify.Error("The engine context changed. Refresh Compose first."); return Task.CompletedTask; }
        ComposeEventsViewer.Show(Loc.T("Events ·") + " " + entry.Name, target.Args(activeProfiles, "events", "--json"), Context());
        return Task.CompletedTask;
    }

    private Task CopyAsync()
    {
        if (Refuse(false)) return Task.CompletedTask;
        var names = ServiceNames();
        if (names.Count == 0) { Host.Notify.Info("No services are listed for this project yet."); return Task.CompletedTask; }
        var toLocal = true;
        var service = Ui.Combo(names);
        var containerPath = Ui.Input("", "Container path", true);
        var localPath = Ui.Input("", "Path on this computer", true);
        var pickFile = Ui.IconButton(Icons.Description, "Choose a file", () =>
        {
            if (toLocal) { if (PageKit.PickSavePath(Path.GetFileName(containerPath.Text.TrimEnd('/')) is { Length: > 0 } n ? n : "copy", "All files|*.*") is { } p) localPath.Text = p; }
            else if (PageKit.PickOpenPath("Choose the file to copy", "All files|*.*") is { } p) localPath.Text = p;
        });
        var pickFolder = Ui.IconButton(Icons.FolderOpen, "Choose a folder", () => { var d = new OpenFolderDialog(); if (d.ShowDialog() == true) localPath.Text = d.FolderName; });
        var direction = Ui.Segmented(["Service to this computer", "This computer to service"], "Service to this computer", v => toLocal = v == "Service to this computer");
        var body = Ui.VStack(14,
            Ui.Field("Direction", direction, "Copy a file or folder out of the service to this computer, or from this computer into the service."),
            Ui.Field("Service", service, "The first container of the service is used."),
            Ui.Field("Container path", containerPath, "An absolute path inside the container. A folder is copied with its contents.", "/app/logs"),
            Ui.Field("Path on this computer", Ui.Columns(8, [1, 0, 0], localPath, pickFile, pickFolder), "Where to write (from the service) or what to send (to the service).", "C:\\Temp\\logs"));
        PageKit.Dialog("Copy files", "docker compose cp", body, "Copy", async () =>
        {
            var svc = service.SelectedItem as string ?? "";
            var inside = containerPath.Text.Trim();
            var local = localPath.Text.Trim();
            if (!inside.StartsWith('/') || inside.Any(char.IsControl)) return "Enter an absolute container path starting with /.";
            if (local.Length == 0 || local.StartsWith('-')) return "Choose a path on this computer.";
            if (!toLocal && !File.Exists(local) && !Directory.Exists(local)) return "The file or folder to send does not exist.";
            if (toLocal && (File.Exists(local) || (Directory.Exists(local) && Directory.EnumerateFileSystemEntries(local).Any())) &&
                !await Host.Overlay.ConfirmAsync("Overwrite on this computer?", $"{local} already exists. Files with the same names are overwritten by the copy.", "Overwrite", true)) return "Copy canceled.";
            if (!toLocal && !await Host.Overlay.ConfirmAsync("Copy into the container?", $"Files at {inside} in service {svc} with the same names are overwritten. The copy is lost when the container is recreated unless the path is on a volume.", "Copy", true)) return "Copy canceled.";
            if (Refuse(false)) return "The project can no longer be acted on.";
            DockerService.ValidateOperand(svc, "service");
            var args = toLocal ? target!.Args(activeProfiles, "cp", svc + ":" + inside, Path.GetFullPath(local)) : target!.Args(activeProfiles, "cp", Path.GetFullPath(local), svc + ":" + inside);
            var result = await Host.Execute($"Compose copy · {svc}", args, boundContext: Context());
            return result?.Success == true ? null : PageKit.Failure(result);
        }, width: 620);
        return Task.CompletedTask;
    }

    private Task ExecAsync()
    {
        if (Refuse(false)) return Task.CompletedTask;
        var names = services.Items.Where(s => s.State == "running").Select(s => s.Service).Distinct(StringComparer.Ordinal).ToList();
        if (names.Count == 0) { Host.Notify.Info("No service of this project is running. Exec needs a running container."); return Task.CompletedTask; }
        var service = Ui.Combo(names);
        var command = Ui.Input("", "Command", true);
        var user = Ui.Input("", "User", true);
        var workdir = Ui.Input("", "Working directory", true);
        var body = Ui.VStack(14,
            Ui.Field("Service", service, "A running service. The first container of the service is used."),
            Ui.Field("Command", command, "The program and its arguments. Quote arguments that contain spaces. Runs without a terminal (-T); interactive programs that need input will not work here.", "ls -la /app"),
            Ui.Columns(12, [1, 1],
                Ui.Field("User", user, "Optional user name or UID to run as.", "root"),
                Ui.Field("Working directory", workdir, "Optional absolute path inside the container.", "/app")));
        PageKit.Dialog("Run a command", "docker compose exec -T · one-shot", body, "Run", async () =>
        {
            var svc = service.SelectedItem as string ?? "";
            string[] argv;
            try { argv = PageKit.SplitCommand(command.Text.Trim()); } catch (ArgumentException e) { return e.Message; }
            if (argv.Length == 0) return "Enter a command.";
            if (Refuse(false)) return "The project can no longer be acted on.";
            DockerService.ValidateOperand(svc, "service");
            var tail = new List<string> { "exec", "-T" };
            if (user.Text.Trim().Length > 0) { DockerService.ValidateOperand(user.Text.Trim(), "user"); tail.AddRange(["--user", user.Text.Trim()]); }
            if (workdir.Text.Trim().Length > 0) { if (!workdir.Text.Trim().StartsWith('/')) return "The working directory must be an absolute path."; tail.AddRange(["--workdir", workdir.Text.Trim()]); }
            tail.Add(svc);
            tail.AddRange(argv);
            // The label names the service only, so the command line is not written to local history.
            var result = await Host.Execute($"Compose exec · {svc}", target!.Args(activeProfiles, [.. tail]), boundContext: Context());
            if (result == null) return PageKit.Failure(result);
            ShowOutput($"exec · {svc}", result, $"{argv[0]} · exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}");
            return null;
        }, width: 600);
        return Task.CompletedTask;
    }
}
