using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>One row of <c>docker node ls</c>.</summary>
public sealed record SwarmNodeRow(string Id, string Hostname, string Status, string Availability, string ManagerStatus, string EngineVersion, bool Self)
{
    public bool IsManager => ManagerStatus.Length > 0;
    public string Role => ManagerStatus.Length == 0 ? "Worker" : ManagerStatus;
    public SwarmHealth Health => SwarmRules.NodeHealth(Status, Availability);
}

/// <summary>Nodes: status, availability and role chips, promote, demote, drain, pause and activate.</summary>
public sealed class SwarmNodesView : SwarmTabView
{
    private readonly DataTable<SwarmNodeRow> table = new("Swarm nodes", "Search nodes");

    public SwarmNodesView(Func<string?> context, Func<bool> isCurrent) : base(context, isCurrent)
    {
        table.Key = n => n.Id;
        table.EmptyTitle = Loc.T("No nodes");
        table.EmptyMessage = Loc.T("The swarm reported no nodes. Node lists are available on manager nodes only.");
        table.Column("Hostname", n => n.Hostname + (n.Self ? "  (this engine)" : ""), 220, true)
            .ChipColumn("Status", n => n.Status, n => n.Status.Equals("Ready", StringComparison.OrdinalIgnoreCase) ? Tone.Success : Tone.Error, 100)
            .ChipColumn("Availability", n => n.Availability, n => ToneOf(n.Health), 110)
            .ChipColumn("Role", n => n.Role, n => n.ManagerStatus switch { "Leader" => Tone.Primary, "" => Tone.Neutral, "Unreachable" => Tone.Error, _ => Tone.Tertiary }, 120)
            .Column("Engine", n => n.EngineVersion, 110)
            .Column("ID", n => n.Id, 220, true)
            .Filter("Managers", n => n.IsManager)
            .Filter("Not active", n => !n.Availability.Equals("Active", StringComparison.OrdinalIgnoreCase))
            .RowAction("Inspect", Icons.DataObject, n => InspectAsync("node", n.Id, Loc.T("Node") + " " + n.Hostname), "Enter")
            .RowAction("Set active", Icons.PlayArrow, n => AvailabilityAsync(n, "active"))
            .RowAction("Pause…", Icons.Pause, n => AvailabilityAsync(n, "pause"))
            .RowAction("Drain…", Icons.Stop, n => AvailabilityAsync(n, "drain"))
            .RowAction("Promote to manager…", Icons.ArrowUpward, PromoteAsync)
            .RowAction("Demote to worker…", Icons.ArrowDownward, DemoteAsync)
            .RowAction("Copy ID", Icons.ContentCopy, n => { PageKit.Copy(n.Id, "the node ID"); return Task.CompletedTask; });
        table.Activated += n => _ = Guard(() => InspectAsync("node", n.Id, Loc.T("Node") + " " + n.Hostname));
        LayoutWith(Ui.Caption("Availability: active nodes take new tasks, paused nodes keep their tasks but take no new ones, drained nodes move their tasks elsewhere. Managers hold the cluster state; keep an odd number of them."), table);
    }

    public override async Task LoadAsync()
    {
        var rows = await Host.ReadRows(["node", "ls", "--format", "{{json .}}"], Context());
        var items = rows.Select(r => new SwarmNodeRow(CliValues.Str(r, "ID"), CliValues.Str(r, "Hostname"), CliValues.Str(r, "Status"), CliValues.Str(r, "Availability"), CliValues.Str(r, "ManagerStatus"), CliValues.Str(r, "EngineVersion"), CliValues.Bool(r, "Self"))).ToList();
        table.SetItems(items);
        SetSummary(Loc.Line($"{items.Count} node{(items.Count == 1 ? "" : "s")} · {items.Count(n => n.IsManager)} manager{(items.Count(n => n.IsManager) == 1 ? "" : "s")} · {items.Count(n => n.Health == SwarmHealth.Healthy)} ready and active"));
    }

    private async Task AvailabilityAsync(SwarmNodeRow n, string availability)
    {
        if (Stale()) return;
        if (n.Availability.Equals(availability, StringComparison.OrdinalIgnoreCase)) { Host.Notify.Info($"{n.Hostname} is already {availability}."); return; }
        if (availability != "active")
        {
            var explanation = availability == "drain"
                ? $"The swarm stops every task on {n.Hostname} and starts replacements on other nodes. Services without capacity elsewhere run fewer tasks until the node is active again."
                : $"{n.Hostname} keeps its running tasks but receives no new ones.";
            if (!await Host.Overlay.ConfirmAsync((availability == "drain" ? "Drain " : "Pause ") + n.Hostname, explanation, availability == "drain" ? "Drain" : "Pause", availability == "drain")) return;
            if (Stale()) return;
        }
        await Run($"Set {n.Hostname} {availability}", ["node", "update", "--availability", availability, n.Id]);
        await AfterChange();
    }

    private async Task PromoteAsync(SwarmNodeRow n)
    {
        if (Stale()) return;
        if (n.IsManager) { Host.Notify.Info($"{n.Hostname} is already a manager."); return; }
        if (!await Host.Overlay.ConfirmAsync(Loc.T("Promote") + " " + n.Hostname, "The node becomes a manager and takes part in the cluster's Raft consensus. An even number of managers does not add fault tolerance.", "Promote")) return;
        if (Stale()) return;
        await Run(Loc.T("Promote") + " " + n.Hostname, ["node", "promote", n.Id]);
        await AfterChange();
    }

    private async Task DemoteAsync(SwarmNodeRow n)
    {
        if (Stale()) return;
        if (!n.IsManager) { Host.Notify.Info($"{n.Hostname} is already a worker."); return; }
        var managers = table.Items.Count(x => x.IsManager);
        if (!await Host.Overlay.ConfirmAsync("Demote " + n.Hostname, $"The node stops being a manager. This swarm has {managers} manager{(managers == 1 ? "" : "s")}; demoting the last manager is refused by the engine, and losing the majority of managers makes the swarm unmanageable." + (n.Self ? " This is the engine this window is connected to; after demotion it can no longer list or manage swarm objects." : ""), "Demote", true)) return;
        if (Stale()) return;
        await Run(Loc.T("Demote") + " " + n.Hostname, ["node", "demote", n.Id]);
        await AfterChange();
    }
}
