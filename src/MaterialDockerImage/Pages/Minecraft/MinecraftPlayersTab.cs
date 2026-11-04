using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.UI;
using MaterialDockerImage.Services;

namespace MaterialDockerImage.Pages;

/// <summary>One player shown on the Players tab with the lists they appear in.</summary>
internal sealed record MinecraftPlayerRow(string Name, bool Online, bool Operator, bool Whitelisted)
{
    public string Status => Online ? "online" : "offline";
    public string Roles => string.Join(", ", new[] { Operator ? "operator" : null, Whitelisted ? "whitelisted" : null }.Where(x => x != null));
}

/// <summary>
/// Online players from rcon-cli list, operators and whitelist from ops.json and whitelist.json in /data, and actions that
/// run the matching server commands (op, deop, whitelist add/remove, kick) through RCON.
/// </summary>
internal sealed class MinecraftPlayersTab : MinecraftTab
{
    private readonly DataTable<MinecraftPlayerRow> table = new("Players", "Filter players");
    private readonly ContentControl notice = new();
    private readonly TextBlock summary = Ui.Caption("");
    private readonly TextBox name = Ui.Input("", "Player name", true);
    private int version;

    public MinecraftPlayersTab(MinecraftServerDetail detail) : base(detail)
    {
        table.Key = p => p.Name;
        table.EmptyTitle = Loc.T("No players known yet");
        table.EmptyMessage = Loc.T("Nobody is online and the operator and whitelist files are empty. Add a player below.");
        table.ChipColumn("Status", p => p.Status, p => p.Online ? Tone.Success : Tone.Neutral, 100)
            .Column("Name", p => p.Name, 200, true)
            .Column("Roles", p => p.Roles, 220)
            .Filter("Online", p => p.Online)
            .Filter("Operators", p => p.Operator)
            .Filter("Whitelisted", p => p.Whitelisted)
            .RowAction("Make operator", Icons.Shield, p => RunAsync("op " + p.Name))
            .RowAction("Remove operator", Icons.Shield, p => RunAsync("deop " + p.Name))
            .RowAction("Add to whitelist", Icons.PlaylistAddCheck, p => RunAsync("whitelist add " + p.Name))
            .RowAction("Remove from whitelist", Icons.Close, p => RunAsync("whitelist remove " + p.Name))
            .RowAction("Kick…", Icons.Logout, KickAsync, null, true);
        name.Width = 220;
        var add = Ui.Wrap(8,
            name,
            Ui.AsyncButton("Add to whitelist", Icons.PlaylistAddCheck, ButtonKind.Tonal, () => AddAsync("whitelist add ")),
            Ui.AsyncButton("Make operator", Icons.Shield, ButtonKind.Tonal, () => AddAsync("op ")),
            Ui.AsyncIconButton(Icons.Refresh, "Refresh players", () => LoadAsync(++version)));
        var top = Ui.VStack(10, notice, Ui.Field("Player", add, "Java player names are 3 to 16 letters, digits or underscores. Commands run through RCON on the running server, so changes apply immediately and are saved by the server in ops.json and whitelist.json.", "Steve"), summary);
        top.Margin = new Thickness(0, 0, 0, 12);
        Children.Add(top.Dock(Dock.Top));
        Children.Add(table);
    }

    public override void OnShown() => _ = LoadAsync(++version);

    private async Task LoadAsync(int mine)
    {
        var s = Server;
        if (!s.IsJava)
        {
            notice.Content = Notice("Bedrock has no RCON, so player lists cannot be read here. Use the Console tab: allowlist add <name>, op <name> and list; replies appear in the log stream.");
            table.SetItems([]);
            return;
        }
        if (!s.IsRunning) { notice.Content = Notice("Start the server to read players. Operators and whitelist entries set in Properties apply at the next start.", Tone.Warning); table.SetItems([]); return; }
        if (!s.Spec.RconEnabled) { notice.Content = Notice("RCON is off for this server. Turn it on in Properties to manage players here.", Tone.Warning); table.SetItems([]); return; }
        notice.Content = null;
        var listTask = MinecraftOps.CommandAsync(s, "list", Context);
        var opsTask = Host.Read(["exec", s.Id, "cat", "/data/ops.json"], Context);
        var whiteTask = Host.Read(["exec", s.Id, "cat", "/data/whitelist.json"], Context);
        await Task.WhenAll(listTask, opsTask, whiteTask);
        if (mine != version) return;
        var list = listTask.Result.Success ? MinecraftOutput.ParseList(listTask.Result.StandardOutput) : null;
        var online = list?.Names ?? [];
        var ops = opsTask.Result.Success ? MinecraftOutput.NamesFromJson(opsTask.Result.StandardOutput) : [];
        var white = whiteTask.Result.Success ? MinecraftOutput.NamesFromJson(whiteTask.Result.StandardOutput) : [];
        var names = online.Concat(ops).Concat(white).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
        table.SetItems(names.Select(n => new MinecraftPlayerRow(n, online.Contains(n, StringComparer.OrdinalIgnoreCase), ops.Contains(n, StringComparer.OrdinalIgnoreCase), white.Contains(n, StringComparer.OrdinalIgnoreCase))));
        summary.Text = (list is { } l ? $"{l.Online} of {l.Max} online" : Loc.T("Online players unknown:") + " " + MinecraftOps.Trim(listTask.Result))
            + $" · {ops.Count} operators · {white.Count} whitelisted · whitelist {(s.Spec.Whitelist ? "on" : "off")} · read {DateTime.Now:HH:mm:ss}";
    }

    private async Task AddAsync(string command)
    {
        var n = name.Text.Trim();
        if (MinecraftSpecRules.ValidatePlayer(n) is { } problem) { Host.Notify.Error(problem); return; }
        await RunAsync(command + n);
        name.Clear();
    }

    private async Task KickAsync(MinecraftPlayerRow p)
    {
        if (!await Host.Overlay.ConfirmAsync(Loc.T("Kick") + " " + p.Name + "?", "Disconnects the player. They can join again unless removed from the whitelist.", "Kick", true)) return;
        await RunAsync("kick " + p.Name);
    }

    private async Task RunAsync(string command)
    {
        if (!EnsureCurrent()) return;
        var r = await MinecraftOps.CommandAsync(Server, command, Context);
        var reply = MinecraftOutput.StripFormatting(r.StandardOutput).Trim();
        if (r.Success) Host.Notify.Info(command + ": " + (reply.Length > 0 ? reply : "sent"));
        else Host.Notify.Error(command + " failed: " + MinecraftOps.Trim(r));
        await LoadAsync(++version);
    }
}
