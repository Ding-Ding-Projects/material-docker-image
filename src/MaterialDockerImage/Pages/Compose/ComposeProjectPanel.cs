using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// The selected project: header with project actions, profile chips, the services table with per-service actions
/// and the last action's real output. Every action is refused while the source has unsaved edits or the engine
/// context changed since the list was loaded.
/// </summary>
public sealed class ComposeProjectPanel : Grid
{
    private static AppHost Host => AppHost.Current;
    private readonly TextBlock title = Ui.Text("", 22, FontWeights.SemiBold, "OnSurface", false, false);
    private readonly TextBlock meta = Ui.Mono("", 12, "OnSurfaceVariant", true);
    private readonly Border status = Ui.Badge("", Tone.Neutral);
    private readonly WrapPanel actions = new() { HorizontalAlignment = HorizontalAlignment.Right };
    private readonly WrapPanel profileChips = new();
    private readonly TextBlock profileNote = Ui.Caption("");
    private readonly TextBlock runningText = Ui.Caption("");
    private readonly DataTable<ComposeServiceRow> services = new("Compose services", "Search services");
    private readonly TextBlock lastAction = Ui.Mono("", 12, "OnSurface", true);
    private readonly TextBlock lastActionTitle = Ui.Overline("Last action");
    private readonly Border lastActionCard;
    private readonly HashSet<string> activeProfiles = new(StringComparer.Ordinal);
    private readonly List<Button> fileOnly = new();
    private ComposeEntry? entry;
    private ComposeTarget? target;
    private int loadVersion;

    public Func<string?> Context { get; set; } = () => null;
    public Func<bool> IsCurrent { get; set; } = () => true;
    public Func<bool> HasUnsavedEdits { get; set; } = () => false;
    /// <summary>Reloads the project list after an action that changes project state.</summary>
    public Func<Task>? AfterAction { get; set; }

    public ComposeProjectPanel()
    {
        Button Act(string label, string icon, ButtonKind kind, Func<Task> run, bool needsFiles)
        {
            var b = Ui.AsyncButton(label, icon, kind, run);
            if (needsFiles) fileOnly.Add(b);
            actions.Children.Add(b.Margin(8, 4, 0, 4));
            return b;
        }
        Act("Logs", Icons.Terminal, ButtonKind.Text, () => LogsAsync(null), false);
        Act("Config", Icons.DataObject, ButtonKind.Text, ConfigAsync, true);
        Act("Build", Icons.Construction, ButtonKind.Text, () => Simple("Build", true, "build"), true);
        Act("Pull", Icons.CloudDownload, ButtonKind.Text, () => Simple("Pull", true, "pull"), true);
        Act("Start", Icons.PlayArrow, ButtonKind.Text, () => Simple("Start", false, "start"), false);
        Act("Stop", Icons.Stop, ButtonKind.Text, () => Simple("Stop", false, "stop"), false);
        Act("Restart", Icons.RestartAlt, ButtonKind.Tonal, () => Simple("Restart", false, "restart"), false);
        Act("Down", Icons.Stop, ButtonKind.Outlined, DownAsync, false);
        Act("Up", Icons.PlayArrow, ButtonKind.Filled, () => UpAsync(null), true);

        services.Key = s => s.Service + "/" + s.Container;
        services.EmptyTitle = "No services";
        services.EmptyMessage = "Compose reported no services for this project.";
        services.Column("Service", s => s.Service, 140, true)
            .Column("Image", s => s.Image, 200, true)
            .ChipColumn("State", s => s.Created ? s.State : "not created", s => s.StateTone, 110)
            .Column("Ports", s => s.Ports, 200, true)
            .Column("Health", s => s.Health.Length > 0 ? s.Health : "—", 90)
            .Column("Status", s => s.Status, 160)
            .Filter("Running", s => s.State == "running")
            .Filter("Not running", s => s.State != "running")
            .RowAction("Logs", Icons.Terminal, s => LogsAsync(s.Service), "Enter")
            .RowAction("Start", Icons.PlayArrow, s => Simple("Start " + s.Service, false, "start", s.Service))
            .RowAction("Stop", Icons.Stop, s => Simple("Stop " + s.Service, false, "stop", s.Service))
            .RowAction("Restart", Icons.RestartAlt, s => Simple("Restart " + s.Service, false, "restart", s.Service))
            .RowAction("Up this service…", Icons.RocketLaunch, s => UpAsync(s.Service))
            .RowAction("Copy container name", Icons.ContentCopy, s => { if (s.Container.Length > 0) Clipboard.SetText(s.Container); return Task.CompletedTask; });
        services.Activated += s => _ = Ui.RunGuarded(services, () => LogsAsync(s.Service));

        // Actions wrap under the title so every button stays reachable at narrow widths.
        actions.HorizontalAlignment = HorizontalAlignment.Left;
        actions.Margin = new Thickness(-8, 4, 0, 0);
        var header = Ui.VStack(4, Ui.HStack(10, title, status), meta, actions);
        var headerCard = Ui.Card(header, 16, "SurfaceLow");

        var profiles = Ui.VStack(6, Ui.HStack(8, Ui.Overline("Profiles"), Ui.InfoButton("Profiles", "Profiles turn on optional services. Services without a profile always run. Selected profiles are passed as --profile to every action of a file-based project.", "--profile observability")), profileChips, profileNote);
        var servicesHeader = new DockPanel();
        servicesHeader.Children.Add(runningText.Dock(Dock.Right));
        servicesHeader.Children.Add(Ui.Text("Services", 16, FontWeights.SemiBold));
        lastActionCard = Ui.Card(Ui.VStack(6, lastActionTitle, new ScrollViewer { Content = lastAction, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }), 12, "SurfaceContainer", 12);
        lastActionCard.Visibility = Visibility.Collapsed;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 220 });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Children.Add(headerCard);
        Children.Add(profiles.At(2));
        Children.Add(servicesHeader.At(4).Margin(0, 0, 0, 8));
        Children.Add(services.At(5));
        Children.Add(lastActionCard.At(6).Margin(0, 12, 0, 0));
    }

    public ComposeEntry? Entry => entry;

    public async Task ShowAsync(ComposeEntry? selected)
    {
        var sameProject = selected != null && entry != null && selected.Key == entry.Key;
        entry = selected;
        if (!sameProject) { activeProfiles.Clear(); lastActionCard.Visibility = Visibility.Collapsed; }
        if (selected == null) { target = null; return; }
        var files = selected.HasLocalFiles ? selected.Files.Select(Path.GetFullPath).ToList() : new List<string>();
        target = new ComposeTarget(selected.Listed ? selected.Name : null, files);
        title.Text = selected.Name;
        var newBadge = Ui.Badge(selected.Status, selected.StatusTone);
        var child = newBadge.Child; newBadge.Child = null; status.Child = child;
        status.SetResourceReference(Border.BackgroundProperty, Ui.ToneKeys(selected.StatusTone).Background);
        meta.Text = target.HasFiles ? string.Join(" · ", target.Files) : selected.Files.Count > 0 ? "Files reported by the engine are not on this computer: " + string.Join(", ", selected.Files) : "No config files reported";
        foreach (var b in fileOnly) { b.IsEnabled = target.HasFiles; b.ToolTip = target.HasFiles ? null : Loc.T("Needs the project's Compose file on this computer. Open it with Open Compose file."); }
        await LoadDetailsAsync();
    }

    public async Task LoadDetailsAsync()
    {
        if (target == null || entry == null) return;
        var version = ++loadVersion;
        var t = target;
        var ctx = Context();
        // Profiles (file-based only).
        if (t.HasFiles)
        {
            var r = await Host.Read(t.Args([], "config", "--profiles"), ctx);
            if (version != loadVersion) return;
            RenderProfiles(r.Success ? r.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList() : null, r.Success ? null : FirstLine(r));
        }
        else RenderProfiles([], "Profiles need the Compose file on this computer.");

        // Services: containers from ps, plus declared services without containers from config --services.
        var rows = new List<ComposeServiceRow>();
        string? psError = null;
        try
        {
            var ps = await Host.ReadRows(t.Args(activeProfiles, "ps", "--all", "--format", "json"), ctx);
            rows.AddRange(ps.Select(ParseService));
        }
        catch (Exception e) when (e is InvalidOperationException or JsonException) { psError = e.Message; }
        if (version != loadVersion) return;
        if (t.HasFiles)
        {
            var declared = await Host.Read(t.Args(activeProfiles, "config", "--services"), ctx);
            if (version != loadVersion) return;
            if (declared.Success)
                foreach (var name in declared.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (!rows.Any(r => r.Service == name)) rows.Add(new ComposeServiceRow(name, "", "", "not created", "No container yet", "", "", false));
        }
        services.SetItems(rows.OrderBy(r => r.Service, StringComparer.OrdinalIgnoreCase));
        services.EmptyMessage = psError != null ? "docker compose ps failed: " + psError : "Compose reported no services for this project.";
        if (psError != null) services.Apply();
        var running = rows.Count(r => r.State == "running");
        runningText.Text = psError != null ? Loc.T("State unavailable") : $"{running} of {rows.Count} running";
    }

    private async Task SafeLoadDetails()
    {
        try { await LoadDetailsAsync(); }
        catch (Exception e) when (e is not OutOfMemoryException) { Host.Notify.Error(e.Message); }
    }

    private static string FirstLine(CommandResult r) => r.StandardError.Trim().Split('\n')[0];

    private void RenderProfiles(List<string>? names, string? note)
    {
        profileChips.Children.Clear();
        profileNote.Text = note ?? "";
        if (names == null || names.Count == 0)
        {
            if (note == null) profileNote.Text = Loc.T("This project defines no profiles.");
            return;
        }
        activeProfiles.IntersectWith(names);
        foreach (var name in names)
        {
            var n = name;
            var chip = Ui.Chip(n, activeProfiles.Contains(n), on => { if (on) activeProfiles.Add(n); else activeProfiles.Remove(n); _ = SafeLoadDetails(); });
            chip.Margin = new Thickness(0, 0, 8, 6);
            profileChips.Children.Add(chip);
        }
        profileNote.Text = activeProfiles.Count == 0 ? Loc.T("No profile selected: only services without a profile take part.") : "Actions include --profile " + string.Join(" --profile ", activeProfiles);
    }

    private static ComposeServiceRow ParseService(JsonElement x)
    {
        var ports = CliValues.Str(x, "Ports");
        var publishers = CliValues.Arr(x, "Publishers").Where(p => CliValues.Long(p, "PublishedPort") > 0)
            .Select(p => $"{(CliValues.Str(p, "URL") is { Length: > 0 } u ? u + ":" : "")}{CliValues.Long(p, "PublishedPort")}->{CliValues.Long(p, "TargetPort")}/{CliValues.Str(p, "Protocol")}").Distinct().ToList();
        if (publishers.Count > 0) ports = string.Join(", ", publishers);
        return new ComposeServiceRow(CliValues.Str(x, "Service"), CliValues.Str(x, "Name"), CliValues.Str(x, "Image"), CliValues.Str(x, "State").ToLowerInvariant(), CliValues.Str(x, "Status"), ports, CliValues.Str(x, "Health"), true);
    }

    /// <summary>Common refusals: no project, stale context, unsaved edits.</summary>
    private bool Refuse(bool needsFiles)
    {
        if (target == null || entry == null) { Host.Notify.Info("Select a Compose project first."); return true; }
        if (!IsCurrent()) { Host.Notify.Error("The engine context changed since this list was loaded. Refresh Compose before acting."); return true; }
        if (HasUnsavedEdits()) { Host.Notify.Error("Save or discard the Compose source edits before running actions, so the engine runs exactly what is on disk."); return true; }
        if (needsFiles && !target.HasFiles) { Host.Notify.Error("This action needs the project's Compose file on this computer. Open the file first."); return true; }
        return false;
    }

    private async Task Run(string label, List<string> args)
    {
        var result = await Host.Execute($"Compose {label} · {entry!.Name}", args, boundContext: Context());
        if (result != null)
        {
            var text = (result.StandardError + "\n" + result.StandardOutput).Trim();
            var lines = text.Split('\n').Where(l => l.Trim().Length > 0).TakeLast(30).ToList();
            lastActionTitle.Text = $"{Loc.T("Last action")} · {label} · {(result.Cancelled ? "canceled" : result.Success ? "completed" : "failed, exit " + result.ExitCode)} · {DateTime.Now:HH:mm:ss}";
            lastAction.Text = lines.Count == 0 ? Loc.T("The command printed no output.") : string.Join("\n", lines);
            lastActionCard.Visibility = Visibility.Visible;
        }
        if (AfterAction != null) await AfterAction();
        await LoadDetailsAsync();
    }

    private async Task Simple(string label, bool needsFiles, string verb, string? service = null)
    {
        if (Refuse(needsFiles)) return;
        var tail = new List<string> { verb };
        if (service != null) { DockerService.ValidateOperand(service, "service"); tail.Add(service); }
        await Run(label, target!.Args(activeProfiles, [.. tail]));
    }

    private async Task UpAsync(string? service)
    {
        if (Refuse(true)) return;
        if (await ComposeDialogs.UpAsync(target!.Describe, service) is not { } o) return;
        if (Refuse(true)) return;
        var tail = new List<string> { "up", "--detach" };
        if (o.Build) tail.Add("--build");
        if (o.RemoveOrphans) tail.Add("--remove-orphans");
        if (o.ForceRecreate) tail.Add("--force-recreate");
        if (o.PullPolicy.Length > 0) tail.AddRange(["--pull", o.PullPolicy]);
        if (service != null) { DockerService.ValidateOperand(service, "service"); tail.Add(service); }
        await Run(service == null ? "Up" : "Up " + service, target.Args(activeProfiles, [.. tail]));
    }

    private async Task DownAsync()
    {
        if (Refuse(false)) return;
        if (await ComposeDialogs.DownAsync(entry!.Name, target!.Describe) is not { } choice) return;
        if (choice.Volumes)
        {
            var volumes = await VolumesOfProject();
            if (!await Host.Overlay.SuperConfirmAsync("Down and remove volumes", $"Stops and removes {entry.Name}'s containers and networks and deletes its volumes and the data in them. This cannot be undone.", volumes)) return;
        }
        if (Refuse(false)) return;
        var tail = new List<string> { "down" };
        if (choice.Volumes) tail.Add("--volumes");
        if (choice.Orphans) tail.Add("--remove-orphans");
        await Run(choice.Volumes ? "Down with volumes" : "Down", target.Args(activeProfiles, [.. tail]));
    }

    /// <summary>Lists volumes labelled with this project so the super confirmation names them.</summary>
    private async Task<List<string>> VolumesOfProject()
    {
        var list = new List<string>();
        try
        {
            var project = entry!.Listed ? entry.Name : null;
            if (project != null)
            {
                var rows = await Host.ReadRows(["volume", "ls", "--filter", "label=com.docker.compose.project=" + project, "--format", "{{json .}}"], Context());
                list.AddRange(rows.Select(r => "volume " + CliValues.Str(r, "Name")));
            }
        }
        catch (Exception e) when (e is InvalidOperationException or JsonException) { list.Add("(could not list volumes: " + e.Message + ")"); }
        if (list.Count == 0) list.Add("No labelled volumes found for this project; anonymous volumes of its containers are also removed.");
        list.Insert(0, "containers and networks of project " + entry!.Name);
        return list;
    }

    private async Task ConfigAsync()
    {
        if (target == null || !target.HasFiles) { Host.Notify.Error("Config needs the project's Compose file on this computer."); return; }
        if (HasUnsavedEdits()) { Host.Notify.Error("Save the Compose source first; Config validates the file on disk."); return; }
        var r = await Host.Read(target.Args(activeProfiles, "config"), Context());
        if (!r.Success) { ResourceTextViewer.Show("Compose config is invalid", r.StandardError, $"docker compose config exited with code {r.ExitCode}"); return; }
        ResourceTextViewer.Show("Resolved config · " + entry!.Name, r.StandardOutput, "Valid. Variables, extends and profiles resolved by docker compose config.");
    }

    private async Task LogsAsync(string? service)
    {
        if (target == null || entry == null) { Host.Notify.Info("Select a Compose project first."); return; }
        if (!IsCurrent()) { Host.Notify.Error("The engine context changed. Refresh Compose first."); return; }
        if (await ComposeDialogs.LogsAsync(service ?? entry.Name) is not { } o) return;
        var tail = new List<string> { "logs", "--tail", o.Tail < 0 ? "all" : o.Tail.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        if (o.Follow) tail.Add("--follow");
        if (o.Timestamps) tail.Add("--timestamps");
        if (service != null) { DockerService.ValidateOperand(service, "service"); tail.Add(service); }
        ResourceTextViewer.ShowStream("Logs · " + (service ?? entry.Name), "docker compose logs" + (o.Follow ? " --follow" : "") + " · " + target.Describe, target.Args(activeProfiles, [.. tail]), Context());
    }

    /// <summary>Validates the given file in the background (docker compose config --quiet).</summary>
    public async Task<(bool Valid, string Message)> ValidateAsync()
    {
        if (target == null || !target.HasFiles) return (false, "No file to validate.");
        var r = await Host.Read(target.Args(activeProfiles, "config", "--quiet"), Context());
        return r.Success ? (true, "docker compose config --quiet reported the file as valid.") : (false, "Invalid: " + (r.StandardError.Trim().Length > 0 ? r.StandardError.Trim() : "exit code " + r.ExitCode));
    }
}
