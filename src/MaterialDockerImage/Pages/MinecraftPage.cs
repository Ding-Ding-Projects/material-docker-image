using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Minecraft servers as Docker containers (itzg/minecraft-server for Java Edition, itzg/minecraft-bedrock-server for
/// Bedrock). Lists only containers and volumes labelled material-docker-image.minecraft. Creation lives in
/// Minecraft/MinecraftCreateWizard.cs, per-server tabs in Minecraft/MinecraftServerDetail.cs.
/// </summary>
public sealed class MinecraftPage : PageBase
{
    public override string Key => "Minecraft";
    private readonly DataTable<MinecraftServer> table = new("Minecraft servers", "Filter by name, version, type or port");
    private readonly TextBlock subtitle;
    private readonly ContentControl view = new();
    private readonly ContentControl errorHost = new();
    private readonly ContentControl orphanHost = new();
    private readonly FrameworkElement listView;
    private readonly Grid header;
    // Board layout: server cards on top, the selected server's console and controls below. The table stays reachable as a list view.
    private readonly UniformGrid cardsPanel = new() { Columns = 3 };
    private readonly ScrollViewer cardsScroll;
    private readonly ContentControl inlineHost = new();
    private readonly ContentControl contentHost = new();
    private readonly FrameworkElement cardsView;
    private MinecraftServerDetail? inline;
    private MinecraftControlsAside? aside;
    private string? selectedName;
    private MinecraftServerDetail? detail;
    private List<MinecraftServer> servers = new();
    private bool refreshing;
    // A request that arrives mid-refresh (for example an engine context switch) runs once the current one ends.
    private bool refreshAgain;
    public override bool Busy => refreshing || refreshAgain;
    /// <summary>Engine context the open detail view or wizard belongs to; both close when the context changes.</summary>
    private string? viewContext;

    internal string? Context => SnapshotContext;

    public MinecraftPage()
    {
        header = Ui.Header("Minecraft", "Minecraft servers in containers: create, configure, run and back up", out subtitle,
            Ui.Chip("List view", false, asList => { contentHost.Content = asList ? table : cardsView; }),
            Ui.AsyncIconButton(Icons.Refresh, "Refresh", SafeRefresh),
            Ui.Button("Create server", Icons.Add, ButtonKind.Filled, OpenWizard));

        table.Key = s => s.Name;
        table.EmptyTitle = Loc.T("No Minecraft servers yet");
        table.EmptyMessage = Loc.T("Create a server to run Minecraft Java or Bedrock Edition in a container on this engine. Only containers this page created (labelled") + " " + MinecraftSpecRules.Label + ") are listed.";
        table.ChipColumn("State", s => s.State, s => s.StateTone, 100)
            .Column("Name", s => s.Name, 150)
            .ChipColumn("Edition", s => s.EditionText, s => s.IsJava ? Tone.Primary : Tone.Tertiary, 90)
            .Column("Version", s => s.Spec.Version, 110, true)
            .Column("Type", s => s.FlavourText, 110)
            .Column("Port", s => s.PortText, 170, true)
            .Column("Players", s => s.Players, 130)
            .Column("Memory", s => s.MemoryText, 120)
            .Column("Created", s => s.CreatedText, 130)
            .Filter("Running", s => s.IsRunning)
            .Filter("Java", s => s.IsJava)
            .Filter("Bedrock", s => !s.IsJava)
            .BulkAction("Start", Icons.PlayArrow, rows => BulkAsync(rows, StartAsync))
            .BulkAction("Stop", Icons.Stop, rows => BulkAsync(rows, StopAsync))
            .RowAction("Open", Icons.OpenInNew, s => { OpenDetail(s, "Overview"); return Task.CompletedTask; }, "Enter")
            .RowAction("Start", Icons.PlayArrow, StartAsync)
            .RowAction("Stop", Icons.Stop, StopAsync)
            .RowAction("Restart", Icons.RestartAlt, RestartAsync)
            .RowAction("Console", Icons.Terminal, s => { OpenDetail(s, "Console"); return Task.CompletedTask; })
            .RowAction("Logs", Icons.ReceiptLong, s => { ShowLogs(s); return Task.CompletedTask; })
            .RowAction("Backup", Icons.Backup, BackupAsync)
            .RowAction("Remove…", Icons.Delete, RemoveAsync, "Del", true);
        table.Activated += s => OpenDetail(s, "Overview");

        cardsScroll = Ui.Scroll(cardsPanel);
        cardsScroll.MaxHeight = 300;
        cardsScroll.SizeChanged += (_, _) => cardsPanel.Columns = Math.Clamp((int)(cardsScroll.ActualWidth / 300), 1, 3);
        var cards = new Grid();
        cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cards.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 240 });
        cards.Children.Add(cardsScroll.At(0));
        cards.Children.Add(inlineHost.At(1));
        cardsView = cards;
        contentHost.Content = cardsView;
        IsVisibleChanged += (_, _) => { if (IsVisible) inline?.ReloadTab(); else inline?.Close(); };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 240 });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(new Expander { Header = Loc.T("Where servers can run"), Content = TargetsStrip().Margin(0, 8, 0, 0), IsExpanded = false, Margin = new Thickness(0, 0, 0, 8) });
        grid.Children.Add(errorHost.At(1));
        grid.Children.Add(contentHost.At(2));
        grid.Children.Add(orphanHost.At(3));
        listView = grid;
        view.Content = listView;
        Layout(header, view);
    }

    /// <summary>The four execution targets the product describes, with the honest availability of each in this app.</summary>
    private static FrameworkElement TargetsStrip()
    {
        Border Tile(string icon, string title, string detail, bool available)
        {
            var badge = Ui.Badge(available ? "Available" : "Not available in this app", available ? Tone.Success : Tone.Neutral, true);
            var stack = Ui.VStack(6, Ui.HStack(8, Ui.Icon(icon, 20, available, available ? "Primary" : "Outline"), Ui.Text(title, 14, FontWeights.SemiBold)), badge, Ui.Caption(detail));
            var tile = Ui.Card(stack, 14, available ? "SurfaceContainer" : "SurfaceLow", 14);
            tile.Width = 260; tile.Margin = new Thickness(0, 0, 10, 10);
            return tile;
        }
        var wrap = new WrapPanel();
        wrap.Children.Add(Tile(Icons.DeployedCode, "Local Docker", "Containers on the selected engine context.", true));
        wrap.Children.Add(Tile(Icons.Lan, "Docker over SSH", "Select an engine context with an ssh:// endpoint in Contexts; servers then run on that host.", true));
        wrap.Children.Add(Tile(Icons.DeveloperBoard, "Local Windows process", "This app runs servers only as containers; it has no process supervisor for bare server processes.", false));
        wrap.Children.Add(Tile(Icons.Terminal, "SSH process", "This app has no SSH process runner. Install Docker on the remote host and use an ssh:// context instead.", false));
        System.Windows.Automation.AutomationProperties.SetName(wrap, Loc.T("Where servers can run"));
        return wrap;
    }

    public override async Task RefreshAsync()
    {
        if (refreshing) { refreshAgain = true; return; }
        refreshing = true;
        var context = SnapshotContext;
        try
        {
            List<MinecraftServer> loaded; List<string> orphans;
            try { (loaded, orphans) = await MinecraftOps.LoadAsync(context); }
            catch (Exception e) when (e is InvalidOperationException or System.Text.Json.JsonException)
            {
                errorHost.Content = Ui.Card(Ui.Columns(10, [0, 1, 0], Ui.Icon(Icons.Error, 20, true, "OnErrorContainer"), Ui.Text(Loc.T("Minecraft servers could not be listed:") + " " + e.Message, 13, null, "OnErrorContainer", true, false), Ui.Button("Open Diagnostics", null, ButtonKind.Text, () => Host.Navigate("Diagnostics"))), 12, "ErrorContainer", 12).Margin(0, 0, 0, 8);
                subtitle.Text = Loc.T("Could not reach the engine on context") + " " + (context ?? "default") + ".";
                table.SetItems([]);
                return;
            }
            if (context != SnapshotContext) { refreshAgain = true; return; }
            if (!ReferenceEquals(view.Content, listView) && viewContext != context)
            {
                detail?.Close(); detail = null;
                view.Content = listView;
                Host.Notify.Info("The engine context changed, so the open Minecraft server view was closed.");
            }
            errorHost.Content = null;
            servers = loaded;
            table.SetItems(servers);
            SyncCards();
            orphanHost.Content = orphans.Count == 0 ? null : OrphanCard(orphans);
            subtitle.Text = Loc.Line($"{servers.Count} {(servers.Count == 1 ? "server" : "servers")} · {servers.Count(s => s.IsRunning)} running · context {context ?? "default"} · refreshed {DateTime.Now:HH:mm:ss}");
            detail?.Update(servers.FirstOrDefault(s => s.Name == detail.Server.Name));
            await MinecraftOps.FillPlayersAsync(servers, context);
            if (context != SnapshotContext || !ReferenceEquals(loaded, servers)) return;
            table.SetItems(servers);
            SyncCards();
            detail?.Update(servers.FirstOrDefault(s => s.Name == detail.Server.Name));
        }
        finally
        {
            refreshing = false;
            if (refreshAgain) { refreshAgain = false; _ = SafeRefresh(); }
        }
    }

    /// <summary>Rebuilds the server cards and keeps the console and controls below them on the selected server.</summary>
    private void SyncCards()
    {
        var chosen = servers.FirstOrDefault(s => s.Name == selectedName) ?? servers.FirstOrDefault();
        selectedName = chosen?.Name;
        cardsPanel.Children.Clear();
        foreach (var s in servers)
        {
            var name = s.Name;
            cardsPanel.Children.Add(MinecraftServerCards.Card(s, name == selectedName, () => SelectServer(name)));
        }
        if (chosen == null)
        {
            inline?.Close(); inline = null; aside = null;
            inlineHost.Content = Ui.EmptyState(Icons.Public, "No Minecraft servers yet", "Create a server to run Minecraft Java or Bedrock Edition in a container on this engine.");
            return;
        }
        if (inline == null || inline.Server.Name != chosen.Name)
        {
            inline?.Close();
            inline = new MinecraftServerDetail(this, chosen, "Console", true);
            aside = new MinecraftControlsAside(this, chosen, inline);
            inlineHost.Content = Ui.SplitInspector(inline, aside.Root, 1000, 340, 16);
        }
        else { inline.Update(chosen); aside!.Update(chosen); }
    }

    private void SelectServer(string name)
    {
        if (name == selectedName) return;
        selectedName = name;
        SyncCards();
    }

    private FrameworkElement OrphanCard(List<string> volumes)
    {
        var rows = Ui.VStack(6, volumes.Select(v =>
        {
            var name = v;
            var remove = Ui.AsyncButton("Remove…", Icons.Delete, ButtonKind.Text, () => RemoveOrphanAsync(name));
            remove.SetResourceReference(Control.ForegroundProperty, "Error");
            return (UIElement)Ui.Columns(8, [1, 0], Ui.Mono(name), remove);
        }).ToArray());
        return Ui.Card(Ui.VStack(10, Ui.VStack(2, Ui.Text("Kept world volumes", 15, FontWeights.SemiBold), Ui.Caption("Labelled volumes without a server, usually kept when a server was removed. Creating a server with the same name reuses its world.")), rows), 16, "SurfaceLow", 16).Margin(0, 12, 0, 0);
    }

    private async Task RemoveOrphanAsync(string volume)
    {
        if (!EnsureCurrent()) return;
        if (!await Host.Overlay.SuperConfirmAsync("Delete world volume", $"Permanently deletes the volume {volume} and every world, setting and plugin file in it. This cannot be undone.", [volume])) return;
        if (await Host.Execute($"Remove world volume {volume}", ["volume", "rm", volume], true, Context) is { Success: true }) Host.History.Record("minecraft", "Removed kept world volume " + volume);
        await SafeRefresh();
    }

    internal bool EnsureCurrent()
    {
        if (IsCurrent) return true;
        Host.Notify.Error(PageKit.StaleMessage(SnapshotContext));
        return false;
    }

    private void OpenWizard()
    {
        inline?.Close();
        detail?.Close();
        detail = null;
        var wizard = new MinecraftCreateWizard(() => Context, EnsureCurrent, servers.Select(s => s.Name).ToList(), name =>
        {
            view.Content = listView;
            inline?.ReloadTab();
            if (name != null) _ = OpenAfterCreateAsync(name);
        });
        viewContext = Context;
        view.Content = wizard;
    }

    private async Task OpenAfterCreateAsync(string name)
    {
        await SafeRefresh();
        if (servers.FirstOrDefault(s => s.Name == name) is { } s) OpenDetail(s, "Console");
    }

    internal void OpenDetail(MinecraftServer server, string tab)
    {
        inline?.Close();
        detail?.Close();
        detail = new MinecraftServerDetail(this, server, tab);
        viewContext = Context;
        view.Content = detail;
    }

    internal void CloseDetail()
    {
        detail?.Close();
        detail = null;
        view.Content = listView;
        inline?.ReloadTab();
        _ = SafeRefresh();
    }

    internal void ShowLogs(MinecraftServer s) =>
        ResourceTextViewer.ShowStream("Logs: " + s.Name, $"docker logs --follow --tail 500 {s.ContainerName} · context {Context ?? "default"}", ["container", "logs", "--follow", "--tail", "500", s.Id], Context);

    private async Task BulkAsync(IReadOnlyList<MinecraftServer> rows, Func<MinecraftServer, Task> action)
    {
        foreach (var r in rows) await action(r);
    }

    internal async Task StartAsync(MinecraftServer s)
    {
        if (!EnsureCurrent()) return;
        if (!s.Spec.EulaAccepted) { Host.Notify.Error("This server's EULA is not accepted (EULA=FALSE), so it cannot start. Accept it in Properties and recreate the container."); return; }
        await MinecraftOps.StartAsync(s, Context);
        await SafeRefresh();
    }

    internal async Task StopAsync(MinecraftServer s)
    {
        if (!EnsureCurrent()) return;
        await MinecraftOps.StopAsync(s, Context);
        await SafeRefresh();
    }

    internal async Task RestartAsync(MinecraftServer s)
    {
        if (!EnsureCurrent()) return;
        await MinecraftOps.RestartAsync(s, Context);
        await SafeRefresh();
    }

    internal async Task BackupAsync(MinecraftServer s)
    {
        if (!EnsureCurrent()) return;
        var folder = MinecraftPrefs.Current.EffectiveBackupFolder;
        var path = await MinecraftOps.BackupAsync(s, folder, Context);
        if (path != null) Host.Notify.Show(NoticeKind.Success, "Backup saved to " + path, "Open folder", () => PageParts.OpenFolder(folder));
        detail?.ReloadTab();
    }

    internal async Task RemoveAsync(MinecraftServer s)
    {
        if (!EnsureCurrent()) return;
        var proceed = await Host.Overlay.ConfirmAsync(Loc.T("Remove Minecraft server") + " " + s.Name + "?", $"Stops and removes the container {s.ContainerName}. The world volume {s.VolumeName} is kept unless you choose to delete it in the next step.", "Continue", true);
        if (!proceed) return;
        var keep = await AskVolumeAsync(s);
        if (keep == null) return;
        var affected = new List<string> { "container " + s.ContainerName + (s.IsRunning ? " (running)" : "") };
        if (keep == false) affected.Add("volume " + s.VolumeName + " (all world data, permanently)");
        if (!await Host.Overlay.SuperConfirmAsync("Remove Minecraft server", keep == false ? "Removes the server container and permanently deletes its world volume." : "Removes the server container. The world volume is kept and listed under Kept world volumes.", affected)) return;
        if (s.CanRcon) await MinecraftOps.CommandAsync(s, "save-all flush", Context);
        await MinecraftOps.RemoveAsync(s, keep == false, Context);
        if (detail?.Server.Name == s.Name) CloseDetail(); else await SafeRefresh();
    }

    /// <summary>Returns true to keep the volume, false to delete it, null when canceled.</summary>
    private Task<bool?> AskVolumeAsync(MinecraftServer s)
    {
        var done = new TaskCompletionSource<bool?>();
        Action? close = null;
        var delete = Ui.Check(Loc.T("Also delete the world volume") + " " + s.VolumeName, false);
        var ok = Ui.Button("Continue", null, ButtonKind.Danger, () => { done.TrySetResult(delete.IsChecked != true); close!(); });
        var cancel = Ui.Button("Cancel", null, ButtonKind.Text, () => { done.TrySetResult(null); close!(); });
        var actions = Ui.HStack(8, cancel, ok); actions.HorizontalAlignment = HorizontalAlignment.Right;
        var body = Ui.VStack(14, Ui.Title("World data"), Ui.Caption("Off by default. Leave it off to keep the world so a new server with the same name can reuse it, or back it up first from the Backups tab."), delete, actions);
        close = Host.Overlay.Show(body, 520, () => done.TrySetResult(null));
        return done.Task;
    }

    public override IEnumerable<AppCommand> PageCommands() =>
    [
        new AppCommand { Id = "minecraft.create", Title = "Create Minecraft server…", Group = "Minecraft", Icon = Icons.Add, Keywords = "minecraft new server java bedrock paper fabric", Run = OpenWizard },
        new AppCommand { Id = "minecraft.refresh", Title = "Refresh Minecraft servers", Group = "Minecraft", Icon = Icons.Refresh, Run = () => _ = SafeRefresh() },
        new AppCommand { Id = "minecraft.backups", Title = "Open Minecraft backups folder", Group = "Minecraft", Icon = Icons.FolderOpen, Keywords = "backup folder", Run = () => PageParts.OpenFolder(MinecraftPrefs.Current.EffectiveBackupFolder) },
    ];
}
