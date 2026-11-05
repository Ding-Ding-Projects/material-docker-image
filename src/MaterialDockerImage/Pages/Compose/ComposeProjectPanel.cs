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
public sealed partial class ComposeProjectPanel : Grid
{
    private static AppHost Host => AppHost.Current;
    private readonly TextBlock title = Ui.Text("", 22, FontWeights.SemiBold, "OnSurface", false, false);
    private readonly TextBlock meta = Ui.Text("", 13, null, "OnSurfaceVariant", true, false);
    private readonly Border status = Ui.Badge("", Tone.Neutral);
    private readonly WrapPanel actions = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, -8, 0) };
    private readonly WrapPanel moreActions = new();
    private readonly Border moreDrawer;
    private readonly WrapPanel profileChips = new();
    private readonly TextBlock profileNote = Ui.Caption("");
    private readonly Border runningBadge = Ui.Badge("", Tone.Neutral, false);
    private readonly Grid lower = new();
    private readonly RowDefinition lowerRow = new() { Height = new GridLength(1, GridUnitType.Star), MinHeight = 280 };
    private readonly Border servicesCard;
    private UIElement? sourceCard;
    private bool stacked;
    private readonly DataTable<ComposeServiceRow> services = new("Compose services", "Search services");
    private readonly TextBlock lastAction = Ui.Mono("", 12, "OnSurface", true);
    private readonly TextBlock lastActionTitle = Ui.Overline("Last action");
    private readonly Border lastActionCard;
    private readonly HashSet<string> activeProfiles = new(StringComparer.Ordinal);
    private readonly List<Button> fileOnly = new();
    private readonly WrapPanel tools = new();
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
        Button Act(string label, string icon, ButtonKind kind, Func<Task> run, bool needsFiles, bool primary)
        {
            var b = Ui.AsyncButton(label, icon, kind, run);
            if (needsFiles) fileOnly.Add(b);
            if (primary) actions.Children.Add(b.Margin(8, 4, 0, 4));
            else moreActions.Children.Add(b.Margin(0, 0, 8, 8));
            return b;
        }
        Act("Logs", Icons.Terminal, ButtonKind.Outlined, () => LogsAsync(null), false, true);
        Act("Build", Icons.Construction, ButtonKind.Outlined, () => Simple("Build", true, "build"), true, true);
        Act("Pull", Icons.CloudDownload, ButtonKind.Outlined, () => Simple("Pull", true, "pull"), true, true);
        Act("Restart", Icons.RestartAlt, ButtonKind.Tonal, () => Simple("Restart", false, "restart"), false, true);
        Act("Down", Icons.Stop, ButtonKind.Tonal, DownAsync, false, true);
        Act("Up", Icons.PlayArrow, ButtonKind.Filled, () => UpAsync(null), true, true);
        // Less common actions sit behind the overflow button so the header keeps the design's six actions.
        Act("Config", Icons.DataObject, ButtonKind.Text, ConfigAsync, true, false);
        Act("Start", Icons.PlayArrow, ButtonKind.Text, () => Simple("Start", false, "start"), false, false);
        Act("Stop", Icons.Stop, ButtonKind.Text, () => Simple("Stop", false, "stop"), false, false);
        BuildTools();

        services.Key = s => s.Service + "/" + s.Container;
        services.EmptyTitle = Loc.T("No services");
        services.EmptyMessage = Loc.T("Compose reported no services for this project.");
        services.Column("Service", s => s.Service, 110, true)
            .Column("Image", s => s.Image, 150, true)
            .ChipColumn("State", s => s.Created ? s.State : "not created", s => s.StateTone, 100)
            .Column("Ports", s => s.Ports, 160, true)
            .Column("Health", s => s.Health.Length > 0 ? s.Health : "—", 70)
            .Column("Status", s => s.Status, 130)
            .Filter("Running", s => s.State == "running")
            .Filter("Not running", s => s.State != "running")
            .RowAction("Logs", Icons.Terminal, s => LogsAsync(s.Service), "Enter")
            .RowAction("Start", Icons.PlayArrow, s => Simple(Loc.T("Start") + " " + s.Service, false, "start", s.Service))
            .RowAction("Stop", Icons.Stop, s => Simple(Loc.T("Stop") + " " + s.Service, false, "stop", s.Service))
            .RowAction("Restart", Icons.RestartAlt, s => Simple(Loc.T("Restart") + " " + s.Service, false, "restart", s.Service))
            .RowAction("Up this service…", Icons.RocketLaunch, s => UpAsync(s.Service))
            .RowAction("Copy container name", Icons.ContentCopy, s => { if (s.Container.Length > 0) ActionSounds.Copy(s.Container); return Task.CompletedTask; });
        services.Activated += s => _ = Ui.RunGuarded(services, () => LogsAsync(s.Service));

        // Header card: title and the six main actions on one row (the actions wrap under the title when narrow),
        // then the profile chips with the running count, then the optional drawer behind the overflow button.
        var more = Ui.IconButton(Icons.MoreVert, "More actions");
        more.Margin = new Thickness(8, 4, 0, 4);
        actions.Children.Add(more);
        var titleRow = Ui.VStack(2, Ui.HStack(10, title, status), meta);
        var topRow = new HeaderPanel(titleRow, actions);

        var profileLabel = Ui.HStack(8, Ui.Overline("Profiles"), Ui.InfoButton("Profiles", "Profiles turn on optional services. Services without a profile always run. Selected profiles are passed as --profile to every action of a file-based project.", "--profile observability"));
        profileLabel.Margin = new Thickness(0, 0, 8, 0);
        profileLabel.VerticalAlignment = VerticalAlignment.Top;
        var profileRow = new DockPanel();
        runningBadge.Margin = new Thickness(8, 0, 0, 0);
        runningBadge.VerticalAlignment = VerticalAlignment.Top;
        profileRow.Children.Add(runningBadge.Dock(Dock.Right));
        profileRow.Children.Add(profileLabel.Dock(Dock.Left));
        profileRow.Children.Add(profileChips);
        profileNote.Visibility = Visibility.Collapsed;

        var drawer = Ui.VStack(6,
            moreActions,
            Ui.HStack(8, Ui.Overline("Environment files"), Ui.InfoButton("Environment files", "Compose substitutes ${VARIABLES} in the file from .env in the project directory. Choosing env files replaces that default with --env-file for every action.", "--env-file .env.staging")), envChips,
            Ui.HStack(8, Ui.Overline("Inspect and tools")).Margin(0, 6, 0, 0), tools);
        moreDrawer = new Border { Child = drawer, Visibility = Visibility.Collapsed, Padding = new Thickness(0, 10, 0, 0) };
        more.Click += (_, _) =>
        {
            moreDrawer.Visibility = moreDrawer.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            System.Windows.Automation.AutomationProperties.SetHelpText(more, moreDrawer.Visibility == Visibility.Visible ? "expanded" : "collapsed");
        };
        var headerCard = Ui.Card(Ui.VStack(12, topRow, profileRow, profileNote, moreDrawer), 16, "SurfaceLow");
        headerCard.Padding = new Thickness(20, 16, 16, 14);

        // Services card: 48 px header, the table, then the last action's real output inside the same card.
        var servicesTitle = Ui.Text("Services", 14, FontWeights.SemiBold).Margin(16, 0, 16, 0);
        servicesTitle.VerticalAlignment = VerticalAlignment.Center;
        var servicesHeader = new Grid { Height = 48 };
        servicesHeader.Children.Add(servicesTitle);
        lastActionCard = Ui.Card(Ui.VStack(6, lastActionTitle, new ScrollViewer { Content = lastAction, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }), 12, "SurfaceContainer", 12);
        lastActionCard.Margin = new Thickness(16, 12, 16, 12);
        lastActionCard.Visibility = Visibility.Collapsed;
        var servicesBody = new Grid();
        servicesBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        servicesBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        servicesBody.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 160 });
        servicesBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        servicesBody.Children.Add(servicesHeader);
        servicesBody.Children.Add(Ui.Divider().At(1));
        servicesBody.Children.Add(services.At(2).Margin(12, 8, 12, 0));
        servicesBody.Children.Add(lastActionCard.At(3));
        servicesCard = Ui.Card(servicesBody, 0, "SurfaceLow");

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
        RowDefinitions.Add(lowerRow);
        Children.Add(headerCard);
        Children.Add(lower.At(2));
        lower.Children.Add(servicesCard);
        ApplyLowerLayout();
    }

    /// <summary>Places the Compose source card beside the services (460 px wide) or under them when the page is narrow.</summary>
    public void SetSource(UIElement source)
    {
        if (sourceCard != null) lower.Children.Remove(sourceCard);
        sourceCard = source;
        lower.Children.Add(source);
        ApplyLowerLayout();
    }

    /// <summary>Narrow pages stack the services over the source with fixed heights and let the caller scroll.</summary>
    public bool Stacked
    {
        get => stacked;
        set { if (stacked == value) return; stacked = value; ApplyLowerLayout(); }
    }

    private void ApplyLowerLayout()
    {
        lower.RowDefinitions.Clear();
        lower.ColumnDefinitions.Clear();
        if (stacked)
        {
            lowerRow.Height = GridLength.Auto; lowerRow.MinHeight = 0;
            lower.RowDefinitions.Add(new RowDefinition { Height = new GridLength(520) });
            lower.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
            lower.RowDefinitions.Add(new RowDefinition { Height = new GridLength(480) });
            Grid.SetRow(servicesCard, 0); Grid.SetColumn(servicesCard, 0);
            if (sourceCard != null) { Grid.SetRow(sourceCard, 2); Grid.SetColumn(sourceCard, 0); }
        }
        else
        {
            lowerRow.Height = new GridLength(1, GridUnitType.Star); lowerRow.MinHeight = 280;
            lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 320 });
            lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(460) });
            Grid.SetRow(servicesCard, 0); Grid.SetColumn(servicesCard, 0);
            if (sourceCard != null) { Grid.SetRow(sourceCard, 0); Grid.SetColumn(sourceCard, 2); }
        }
    }

    public ComposeEntry? Entry => entry;

    public async Task ShowAsync(ComposeEntry? selected)
    {
        var sameProject = selected != null && entry != null && selected.Key == entry.Key;
        entry = selected;
        if (!sameProject) { activeProfiles.Clear(); lastActionCard.Visibility = Visibility.Collapsed; }
        if (selected == null) { target = null; return; }
        var files = selected.HasLocalFiles ? selected.Files.Select(Path.GetFullPath).ToList() : new List<string>();
        if (!sameProject) envFiles.Clear();
        target = new ComposeTarget(selected.Listed ? selected.Name : null, files) { EnvFiles = envFiles.ToList() };
        RenderEnvFiles();
        title.Text = selected.Name;
        var newBadge = Ui.Badge(selected.Status, selected.StatusTone);
        var child = newBadge.Child; newBadge.Child = null; status.Child = child;
        status.SetResourceReference(Border.BackgroundProperty, Ui.ToneKeys(selected.StatusTone).Background);
        meta.Text = target.HasFiles ? string.Join(" · ", target.Files) : selected.Files.Count > 0 ? Loc.T("Files reported by the engine are not on this computer:") + " " + string.Join(", ", selected.Files) : "No config files reported";
        foreach (var b in fileOnly.Concat(fileOnlyTools)) { b.IsEnabled = target.HasFiles; b.ToolTip = target.HasFiles ? null : Loc.T("Needs the project's Compose file on this computer. Open it with Open Compose file."); }
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
        if (psError != null) SetBadge(runningBadge, Loc.T("State unavailable"), Tone.Neutral);
        else SetBadge(runningBadge, $"{running} of {rows.Count} running", rows.Count == 0 || running == 0 ? Tone.Neutral : running == rows.Count ? Tone.Success : Tone.Warning);
    }

    private static void SetBadge(Border target, string text, Tone tone)
    {
        var replacement = Ui.Badge(text, tone, false);
        var child = replacement.Child; replacement.Child = null; target.Child = child;
        target.SetResourceReference(Border.BackgroundProperty, Ui.ToneKeys(tone).Background);
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
        profileNote.Visibility = Visibility.Visible;
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
        if (o.DryRun) tail.Insert(0, "--dry-run");
        if (o.Build) tail.Add("--build");
        if (o.RemoveOrphans) tail.Add("--remove-orphans");
        if (o.ForceRecreate) tail.Add("--force-recreate");
        if (o.PullPolicy.Length > 0) tail.AddRange(["--pull", o.PullPolicy]);
        if (service != null) { DockerService.ValidateOperand(service, "service"); tail.Add(service); }
        await Run((o.DryRun ? "Dry run · " : "") + (service == null ? "Up" : "Up " + service), target.Args(activeProfiles, [.. tail]));
    }

    private async Task DownAsync()
    {
        if (Refuse(false)) return;
        if (await ComposeDialogs.DownAsync(entry!.Name, target!.Describe) is not { } choice) return;
        if (choice.Volumes && !choice.DryRun)
        {
            var volumes = await VolumesOfProject();
            if (!await Host.Overlay.SuperConfirmAsync("Down and remove volumes", $"Stops and removes {entry.Name}'s containers and networks and deletes its volumes and the data in them. This cannot be undone.", volumes)) return;
        }
        if (Refuse(false)) return;
        var tail = new List<string> { "down" };
        if (choice.DryRun) tail.Insert(0, "--dry-run");
        if (choice.Volumes) tail.Add("--volumes");
        if (choice.Orphans) tail.Add("--remove-orphans");
        await Run((choice.DryRun ? "Dry run · " : "") + (choice.Volumes ? "Down with volumes" : "Down"), target.Args(activeProfiles, [.. tail]));
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
        return r.Success ? (true, "docker compose config --quiet reported the file as valid.") : (false, Loc.T("Invalid:") + " " + (r.StandardError.Trim().Length > 0 ? r.StandardError.Trim() : "exit code " + r.ExitCode));
    }
}
