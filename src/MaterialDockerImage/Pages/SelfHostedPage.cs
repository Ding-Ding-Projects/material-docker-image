using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Quick setups for self-hosted web apps that run as Compose projects. The deployed view lists only projects whose
/// containers carry the material-docker-image.selfhosted label; the catalogue view offers the apps in
/// UI/SelfHostedCatalogue.cs. The setup wizard is in SelfHosted/SelfHostedWizard.cs, engine actions in SelfHostedOps.
/// </summary>
public sealed class SelfHostedPage : PageBase
{
    public override string Key => "Self-hosted";
    private readonly DataTable<SelfHostedProject> table = new("Self-hosted projects", "Filter projects");
    private readonly SelfHostedCatalogueView catalogue;
    private readonly TextBlock subtitle;
    private readonly ContentControl view = new();
    private readonly ContentControl errorHost = new();
    private readonly FrameworkElement deployedView;
    private readonly ContentControl mode = new() { Width = 260 };
    private string modeValue = "";
    private List<SelfHostedProject> projects = [];
    private bool refreshing, refreshAgain, loaded;
    private SelfHostedWizard? wizard;
    public override bool Busy => refreshing || refreshAgain;
    internal string? Context => SnapshotContext;

    public SelfHostedPage()
    {
        catalogue = new SelfHostedCatalogueView(OpenWizard);
        SetMode("Deployed");
        // The header counts would otherwise still show the state from before a deploy while its result is on screen.
        Host.OperationEnded += () => { if (IsVisible && wizard != null) _ = SafeRefresh(); };
        var header = Ui.Header("Self-hosted", "Quick setups for self-hosted web apps that run in Docker", out subtitle,
            mode,
            Ui.AsyncIconButton(Icons.Refresh, "Refresh", SafeRefresh),
            Ui.Button("Set up an app", Icons.Add, ButtonKind.Filled, () => ShowCatalogue(true)));

        table.Key = p => p.Name;
        table.EmptyTitle = Loc.T("No self-hosted apps yet");
        table.EmptyMessage = Loc.T("Choose an app in the catalogue to deploy it on this engine. Only Compose projects this page created (labelled") + " " + SelfHostedRules.Label + ") " + Loc.T("are listed.");
        table.ChipColumn("State", p => Loc.T(p.State), p => p.StateTone, 130)
            .Column("Project", p => p.Name, 160, true)
            .Column("App", p => p.AppName, 170)
            .Column("Address", p => p.Url, 210, true)
            .Column("Containers", p => p.ContainersText, 100)
            .Column("CPU", p => p.CpuText, 80)
            .Column("Memory", p => p.MemoryText, 100)
            .Filter("Running", p => p.IsRunning)
            .Filter("Stopped", p => !p.IsRunning)
            .BulkAction("Start", Icons.PlayArrow, rows => BulkAsync(rows, StartAsync))
            .BulkAction("Stop", Icons.Stop, rows => BulkAsync(rows, StopAsync))
            .RowAction("Open in browser", Icons.OpenInNew, p => { OpenInBrowser(p); return Task.CompletedTask; }, "Enter")
            .RowAction("Start", Icons.PlayArrow, StartAsync)
            .RowAction("Stop", Icons.Stop, StopAsync)
            .RowAction("Restart", Icons.RestartAlt, RestartAsync)
            .RowAction("Update…", Icons.CloudDownload, UpdateAsync)
            .RowAction("Logs", Icons.ReceiptLong, p => { ShowLogs(p); return Task.CompletedTask; })
            .RowAction("Show compose file", Icons.Description, p => { ShowComposeFile(p); return Task.CompletedTask; })
            .RowAction("Open in Compose", Icons.Stacks, p => { OpenInCompose(p); return Task.CompletedTask; })
            .RowAction("Back up data…", Icons.Backup, BackupAsync)
            .RowAction("Remove…", Icons.Delete, RemoveAsync, "Del", true);
        table.Activated += OpenInBrowser;

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 240 });
        grid.Children.Add(errorHost);
        grid.Children.Add(table.At(1));
        deployedView = grid;
        view.Content = deployedView;
        Layout(header, view);
    }

    private void ShowCatalogue(bool on)
    {
        wizard = null;
        view.Content = on ? catalogue : deployedView;
        SetMode(on ? "Catalogue" : "Deployed");
    }

    private void SetMode(string value)
    {
        // The segmented control raises its callback only on clicks, so it is rebuilt when the view changes from code.
        if (modeValue == value) return;
        modeValue = value;
        mode.Content = Ui.Segmented(["Deployed", "Catalogue"], value, v => ShowCatalogue(v == "Catalogue"));
    }

    private void OpenWizard(SelfHostedApp app)
    {
        if (!EnsureCurrent()) return;
        wizard = new SelfHostedWizard(app, () => Context, EnsureCurrent, project =>
        {
            wizard = null;
            ShowCatalogue(false);
            _ = SafeRefresh();
        }, Host.IsCapture);
        view.Content = wizard;
    }

    public override async Task RefreshAsync()
    {
        if (refreshing) { refreshAgain = true; return; }
        refreshing = true;
        var context = SnapshotContext;
        try
        {
            var (loaded, error) = await SelfHostedOps.LoadAsync(context);
            if (context != SnapshotContext) { refreshAgain = true; return; }
            if (error != null)
            {
                errorHost.Content = Ui.Card(Ui.Columns(10, [0, 1, 0], Ui.Icon(Icons.Error, 20, true, "OnErrorContainer"), Ui.Text(Loc.T("Self-hosted projects could not be listed:") + " " + error, 13, null, "OnErrorContainer", true, false), Ui.Button("Open Diagnostics", null, ButtonKind.Text, () => Host.Navigate("Diagnostics"))), 12, "ErrorContainer", 12).Margin(0, 0, 0, 8);
                subtitle.Text = Loc.T("Could not reach the engine on context") + " " + (context ?? "default") + ".";
                table.SetItems([]);
                return;
            }
            errorHost.Content = null;
            projects = loaded;
            this.loaded = true;
            table.SetItems(projects);
            subtitle.Text = Loc.Line($"{projects.Count} {(projects.Count == 1 ? "project" : "projects")} · {projects.Count(p => p.IsRunning)} running · {SelfHostedCatalogue.All.Count} apps in the catalogue · context {context ?? "default"} · refreshed {DateTime.Now:HH:mm:ss}");
            await SelfHostedOps.FillStatsAsync(projects, context);
            if (context == SnapshotContext && ReferenceEquals(loaded, projects)) table.SetItems(projects);
        }
        finally
        {
            refreshing = false;
            if (refreshAgain) { refreshAgain = false; _ = SafeRefresh(); }
        }
    }

    internal bool EnsureCurrent()
    {
        if (IsCurrent) return true;
        Host.Notify.Error(PageKit.StaleMessage(SnapshotContext));
        return false;
    }

    private static async Task BulkAsync(IReadOnlyList<SelfHostedProject> rows, Func<SelfHostedProject, Task> action)
    {
        foreach (var r in rows) await action(r);
    }

    private void OpenInBrowser(SelfHostedProject p)
    {
        if (p.Url.Length == 0) { Host.Notify.Warning("This project publishes no web port that the app knows about. Start it, or open it from Compose."); return; }
        PageParts.OpenExternal(p.Url);
    }

    private async Task RunAsync(SelfHostedProject p, string label, params string[] command)
    {
        if (!EnsureCurrent()) return;
        if (await Host.Execute($"{label} {p.Name}", SelfHostedOps.ProjectArgs(p, command), true, Context) is { Success: true }) Host.History.Record("selfhosted", $"{label} {p.Name}");
        await SafeRefresh();
    }

    private Task StartAsync(SelfHostedProject p) => RunAsync(p, "Start", "start");
    private Task StopAsync(SelfHostedProject p) => RunAsync(p, "Stop", "stop");
    private Task RestartAsync(SelfHostedProject p) => RunAsync(p, "Restart", "restart");

    private void ShowLogs(SelfHostedProject p) =>
        ResourceTextViewer.ShowStream("Logs · " + p.Name, $"docker compose -p {p.Name} logs --follow --tail 300 · context {Context ?? "default"}", ["compose", "-p", p.Name, "logs", "--follow", "--tail", "300"], Context);

    private void ShowComposeFile(SelfHostedProject p)
    {
        var path = Path.Combine(p.Folder, "compose.yaml");
        if (p.Folder.Length == 0 || !File.Exists(path)) { Host.Notify.Warning(Loc.T("The compose file is not on this computer:") + " " + (p.ConfigFile.Length > 0 ? p.ConfigFile : p.Name)); return; }
        ResourceTextViewer.Show("Compose file · " + p.Name, File.ReadAllText(path), path);
    }

    private void OpenInCompose(SelfHostedProject p)
    {
        var path = Path.Combine(p.Folder, "compose.yaml");
        if (p.Folder.Length == 0 || !File.Exists(path)) { Host.Notify.Warning(Loc.T("The compose file is not on this computer:") + " " + p.Name); return; }
        Host.HandOff["Compose.OpenFile"] = path;
        Host.Navigate("Compose");
    }

    /// <summary>Re-renders compose.yaml with the catalogue's pinned images (the .env file and its secrets stay as they
    /// are), then pulls and recreates changed containers after a confirmation that names the new tags.</summary>
    private async Task UpdateAsync(SelfHostedProject p)
    {
        if (!EnsureCurrent()) return;
        var saved = SelfHostedOps.ReadSaved(p);
        if (p.App is not { } app || saved == null) { Host.Notify.Error(Loc.T("The project's settings file is missing, so it cannot be updated from the catalogue:") + " " + p.Name); return; }
        var changes = SelfHostedCompose.ImageChanges(app, saved);
        var message = changes.Count == 0
            ? Loc.T("Every image already uses the tag pinned in this version of the app. Update pulls them again (in case the registry republished a tag) and recreates changed containers.")
            : Loc.T("The project moves to these images. Read each app's release notes before upgrading across major versions; take a backup first.") + Environment.NewLine + string.Join(Environment.NewLine, changes.Select(c => $"{c.Current} → {c.Next}"));
        if (!await Host.Overlay.ConfirmAsync(Loc.T("Update") + " " + p.Name + "?", message, "Update")) return;
        var options = saved.ToOptions();
        try
        {
            File.WriteAllText(Path.Combine(p.Folder, "compose.yaml"), SelfHostedCompose.ComposeYaml(app, options));
            File.WriteAllText(Path.Combine(p.Folder, "selfhosted.json"), System.Text.Json.JsonSerializer.Serialize(SelfHostedCompose.Saved(app, options, saved.Created) , new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Host.Notify.Error(Loc.T("The compose file could not be written:") + " " + e.Message); return; }
        if (await Host.Execute("Pull images for " + p.Name, SelfHostedOps.ProjectArgs(p, "pull"), true, Context) is not { Success: true }) { await SafeRefresh(); return; }
        if (await Host.Execute("Update " + p.Name, SelfHostedOps.ProjectArgs(p, "up", "-d"), true, Context) is { Success: true })
            Host.History.Record("selfhosted", $"Updated {p.Name}" + (changes.Count > 0 ? ": " + string.Join(", ", changes.Select(c => c.Next)) : ""));
        await SafeRefresh();
    }

    private async Task BackupAsync(SelfHostedProject p)
    {
        if (!EnsureCurrent()) return;
        var (local, endpoint) = await VolumeTransferChecks.EngineLocationAsync(Context);
        if (!local) { Host.Notify.Error(Loc.T("Backups write archives to a folder on this computer, but this engine runs on") + " " + endpoint + ". " + Loc.T("Use Storage › Volumes with a path on that host instead.")); return; }
        var volumes = await SelfHostedOps.VolumesAsync(p.Name, Context);
        if (volumes.Count == 0) { Host.Notify.Info("This project has no named volumes. Data kept in a folder can be copied with File Explorer."); return; }
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Docker volume backups", p.Name + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture));
        var stop = p.IsRunning;
        var stopSwitch = Ui.Switch("Stop the project during the backup", stop, v => stop = v, "Recommended: databases copied while running can be inconsistent. The project is started again afterwards.");
        if (!await AskAsync("Back up " + p.Name, Loc.T("Copies each named volume into a tar.gz archive in") + " " + folder + Environment.NewLine + string.Join(Environment.NewLine, volumes), "Back up", stopSwitch)) return;
        Directory.CreateDirectory(folder);
        if (stop && p.IsRunning && await Host.Execute("Stop " + p.Name, SelfHostedOps.ProjectArgs(p, "stop"), true, Context) is not { Success: true }) return;
        var ok = 0;
        foreach (var volume in volumes)
        {
            var archive = VolumeArchiveCommands.SuggestedArchiveName(volume);
            if (await Host.Execute("Back up volume " + volume, VolumeArchiveCommands.Backup(volume, folder, archive, VolumeArchiveCommands.DefaultHelperImage), true, Context) is { Success: true }) ok++;
        }
        if (stop && p.IsRunning) await Host.Execute("Start " + p.Name, SelfHostedOps.ProjectArgs(p, "start"), true, Context);
        Host.History.Record("selfhosted", $"Backed up {ok} of {volumes.Count} volumes of {p.Name}");
        if (ok == volumes.Count) Host.Notify.Show(NoticeKind.Success, Loc.T("Backup saved to") + " " + folder, "Open folder", () => PageParts.OpenFolder(folder));
        else Host.Notify.Error(Loc.Line($"{ok} of {volumes.Count} volumes were backed up · the output panel has the details"));
        await SafeRefresh();
    }

    private async Task RemoveAsync(SelfHostedProject p)
    {
        if (!EnsureCurrent()) return;
        var volumes = await SelfHostedOps.VolumesAsync(p.Name, Context);
        var saved = SelfHostedOps.ReadSaved(p);
        var dataFolder = saved is { Storage: SelfHostedStorage.Folder } ? Path.Combine(saved.Folder, p.Name) : null;
        var deleteData = false;
        var dataSwitch = Ui.Switch("Also delete data", false, v => deleteData = v, "Off by default. Leave it off to keep the volumes" + (dataFolder != null ? ", the data folder" : "") + " and the project files so the app can be deployed again with its data.");
        if (!await AskAsync("Remove " + p.Name, Loc.T("Stops and removes the containers and network of this project.") + " " + Loc.T("Volumes:") + " " + (volumes.Count == 0 ? Loc.T("none") : string.Join(", ", volumes)), "Continue", dataSwitch, true)) return;
        var affected = new List<string>(p.Containers.Select(c => "container " + c.Service + (c.State == "running" ? " (running)" : "")));
        if (deleteData)
        {
            affected.AddRange(volumes.Select(v => "volume " + v + " (all data, permanently)"));
            if (dataFolder != null) affected.Add("folder " + dataFolder + " (all data, permanently)");
            if (p.Folder.Length > 0) affected.Add("project files " + p.Folder + " (compose.yaml, .env with the passwords)");
        }
        if (!await Host.Overlay.SuperConfirmAsync("Remove self-hosted project", deleteData ? "Removes the project and permanently deletes its data. This cannot be undone." : "Removes the project's containers and network. Volumes and files are kept.", affected)) return;
        var args = deleteData ? SelfHostedOps.ProjectArgs(p, "down", "--volumes", "--remove-orphans") : SelfHostedOps.ProjectArgs(p, "down", "--remove-orphans");
        var result = await Host.Execute("Remove " + p.Name, args, true, Context);
        if (result is { Success: true } && deleteData)
        {
            // down --volumes removes the volumes declared in the file; remove any labelled volume it left (for example when the file was missing).
            foreach (var v in await SelfHostedOps.VolumesAsync(p.Name, Context)) await Host.Execute("Remove volume " + v, ["volume", "rm", v], true, Context);
            try
            {
                if (dataFolder != null && Directory.Exists(dataFolder)) Directory.Delete(dataFolder, true);
                if (p.Folder.Length > 0 && p.Folder.StartsWith(SelfHostedOps.Root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(p.Folder)) Directory.Delete(p.Folder, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Host.Notify.Error(Loc.T("Some files could not be deleted:") + " " + e.Message); }
        }
        if (result is { Success: true }) Host.History.Record("selfhosted", $"Removed {p.Name}" + (deleteData ? " and its data" : " (data kept)"));
        await SafeRefresh();
    }

    /// <summary>A small dialog with one extra control; true when the user confirms.</summary>
    private static Task<bool> AskAsync(string title, string message, string confirm, FrameworkElement extra, bool danger = false)
    {
        var done = new TaskCompletionSource<bool>();
        Action? close = null;
        var ok = Ui.Button(confirm, null, danger ? ButtonKind.Danger : ButtonKind.Filled, () => { done.TrySetResult(true); close!(); });
        var cancel = Ui.Button("Cancel", null, ButtonKind.Text, () => { done.TrySetResult(false); close!(); });
        var actions = Ui.HStack(8, cancel, ok); actions.HorizontalAlignment = HorizontalAlignment.Right;
        var body = Ui.VStack(14, Ui.Title(Loc.Lead(title), 20), Ui.Text(message, 13, null, "OnSurfaceVariant", true, false), extra, actions);
        close = Host.Overlay.Show(body, 560, () => done.TrySetResult(false));
        return done.Task;
    }

    // Counts from the last successful load, for the command palette; null until one has loaded.
    internal override string? PaletteSummary() => !loaded ? null : Loc.Line($"{projects.Count} {(projects.Count == 1 ? "project" : "projects")} · {projects.Count(p => p.IsRunning)} running");

    /// <summary>Capture mode only: opens the setup wizard for Nextcloud on its review step. Nothing is written or deployed.</summary>
    internal bool CaptureReview()
    {
        if (!Host.IsCapture || SelfHostedCatalogue.Find("nextcloud") is not { } app) return false;
        OpenWizard(app);
        wizard?.CaptureReview();
        return wizard != null;
    }

    internal bool CaptureCatalogue()
    {
        if (!Host.IsCapture) return false;
        ShowCatalogue(true);
        return true;
    }

    internal void CaptureReset() => ShowCatalogue(false);

    public override IEnumerable<AppCommand> PageCommands() =>
    [
        new AppCommand { Id = "selfhosted.catalogue", Title = "Set up a self-hosted app…", Group = "Self-hosted", Icon = Icons.Add, Keywords = "nextcloud gitlab forgejo vaultwarden jellyfin immich paperless home assistant uptime kuma adguard nginx proxy grafana prometheus rustfs minio n8n code-server wordpress bookstack freshrss syncthing open webui ollama", Run = () => { Host.Navigate("Self-hosted"); ShowCatalogue(true); } },
        new AppCommand { Id = "selfhosted.refresh", Title = "Refresh self-hosted projects", Group = "Self-hosted", Icon = Icons.Refresh, Run = () => { Host.Navigate("Self-hosted"); ShowCatalogue(false); _ = SafeRefresh(); }, Status = PaletteSummary },
        new AppCommand { Id = "selfhosted.folder", Title = "Open self-hosted project files", Group = "Self-hosted", Icon = Icons.FolderOpen, Keywords = "compose env folder", Run = () => PageParts.OpenFolder(SelfHostedOps.Root) },
    ];
}
