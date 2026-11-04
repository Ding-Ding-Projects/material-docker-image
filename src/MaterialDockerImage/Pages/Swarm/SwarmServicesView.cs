using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>One row of <c>docker service ls</c>.</summary>
public sealed record SwarmServiceRow(string Id, string Name, string Mode, string Replicas, string Image, string Ports)
{
    public SwarmHealth Health => SwarmRules.ReplicaHealth(Replicas);
    public bool Replicated => Mode.StartsWith("replicated", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Swarm services: table with replica chips, an inspector with tasks and actions, create, scale, update, rollback, remove.</summary>
public sealed class SwarmServicesView : SwarmTabView
{
    private readonly DataTable<SwarmServiceRow> table = new("Swarm services", "Search services");
    private readonly SwarmServiceInspector inspector;

    public SwarmServicesView(Func<string?> context, Func<bool> isCurrent) : base(context, isCurrent)
    {
        inspector = new SwarmServiceInspector(this);
        table.Key = s => s.Id;
        table.EmptyTitle = "No services";
        table.EmptyMessage = "This swarm runs no services yet. Create one, or deploy a stack from a Compose file.";
        table.Column("Name", s => s.Name, 200, true)
            .ChipColumn("Mode", s => s.Mode, s => s.Replicated ? Tone.Neutral : Tone.Tertiary, 110)
            .ChipColumn("Replicas", s => s.Replicas, s => ToneOf(s.Health), 120)
            .Column("Image", s => s.Image, 260, true)
            .Column("Ports", s => s.Ports, 180, true)
            .Filter("Not converged", s => s.Health is SwarmHealth.Partial or SwarmHealth.Down)
            .Filter("Global", s => !s.Replicated)
            .RowAction("Inspect", Icons.DataObject, s => InspectAsync("service", s.Id, "Service " + s.Name), "Enter")
            .RowAction("Logs…", Icons.Terminal, LogsAsync)
            .RowAction("Scale…", Icons.UnfoldMore, ScaleAsync)
            .RowAction("Update image…", Icons.CloudSync, UpdateImageAsync)
            .RowAction("Roll back…", Icons.Undo, RollbackAsync)
            .RowAction("Copy name", Icons.ContentCopy, s => { PageKit.Copy(s.Name, "the service name"); return Task.CompletedTask; })
            .RowAction("Remove…", Icons.Delete, RemoveAsync, "Del", true)
            .BulkAction("Remove selected…", Icons.Delete, BulkRemoveAsync, true);
        table.Activated += s => _ = Guard(() => InspectAsync("service", s.Id, "Service " + s.Name));
        table.FocusedChanged += s => _ = Guard(() => inspector.ShowAsync(s));
        var create = Ui.Button("Create service", Icons.Add, ButtonKind.Filled, () => SwarmServiceForm.Show(Context, IsCurrent, AfterChange));
        var split = Ui.SplitInspector(table, inspector, 1150, 420, summary: () => table.Focused is { } f ? $"{f.Name} · {f.Replicas} · {f.Image}" : null);
        table.FocusedChanged += _ => (split.Tag as Action)?.Invoke();
        LayoutWith(Ui.Wrap(8, create), split);
    }

    public override async Task LoadAsync()
    {
        var rows = await Host.ReadRows(["service", "ls", "--format", "{{json .}}"], Context());
        var items = rows.Select(r => new SwarmServiceRow(CliValues.Str(r, "ID"), CliValues.Str(r, "Name"), CliValues.Str(r, "Mode"), CliValues.Str(r, "Replicas"), CliValues.Str(r, "Image"), CliValues.Str(r, "Ports"))).ToList();
        table.SetItems(items);
        var converged = items.Count(i => i.Health == SwarmHealth.Healthy);
        SetSummary($"{items.Count} service{(items.Count == 1 ? "" : "s")} · {converged} at desired replicas");
        await inspector.ShowAsync(table.Focused);
    }

    public async Task LogsAsync(SwarmServiceRow s)
    {
        if (await ComposeDialogs.LogsAsync("service " + s.Name) is not { } o) return;
        var args = new List<string> { "service", "logs", "--tail", o.Tail < 0 ? "all" : o.Tail.ToString(CultureInfo.InvariantCulture) };
        if (o.Follow) args.Add("--follow");
        if (o.Timestamps) args.Add("--timestamps");
        args.Add(s.Id);
        ResourceTextViewer.ShowStream("Logs · " + s.Name, "docker service logs" + (o.Follow ? " --follow" : "") + " · all tasks of the service", args, Context());
    }

    public async Task ScaleAsync(SwarmServiceRow s)
    {
        if (Stale()) return;
        if (!s.Replicated) { Host.Notify.Error($"{s.Name} runs in global mode: one task per eligible node. Only replicated services can be scaled."); return; }
        var current = SwarmRules.ParseReplicas(s.Replicas)?.Desired.ToString(CultureInfo.InvariantCulture) ?? "1";
        var value = await Host.Overlay.PromptAsync("Scale " + s.Name, "Replicas", current,
            $"Currently {s.Replicas}. The swarm starts or stops tasks until this many run. 0 stops every task but keeps the service.",
            v => int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 0 and <= 10000 ? null : "Enter a whole number from 0 to 10000.");
        if (value == null || Stale()) return;
        await Run($"Scale {s.Name} to {value}", ["service", "scale", s.Name + "=" + value]);
        await AfterChange();
    }

    public async Task UpdateImageAsync(SwarmServiceRow s)
    {
        if (Stale()) return;
        var current = s.Image.Split('@')[0];
        var value = await Host.Overlay.PromptAsync("Update image of " + s.Name, "Image", current,
            "Starts a rolling update to this image using the service's update policy. The previous specification is kept for Roll back.",
            v => PageKit.ValidateReference(v));
        if (value == null || Stale()) return;
        await Run($"Update {s.Name} to {value}", ["service", "update", "--image", value, s.Id]);
        await AfterChange();
    }

    public async Task RollbackAsync(SwarmServiceRow s)
    {
        if (Stale()) return;
        if (!await Host.Overlay.ConfirmAsync("Roll back " + s.Name, "Returns the service to its previous specification (image, environment, ports and other settings) using the rollback policy. Only one previous specification is kept.", "Roll back")) return;
        if (Stale()) return;
        await Run("Roll back " + s.Name, ["service", "rollback", s.Id]);
        await AfterChange();
    }

    public async Task RemoveAsync(SwarmServiceRow s)
    {
        if (Stale()) return;
        if (!await Host.Overlay.ConfirmAsync("Remove service " + s.Name, $"Stops every task of {s.Name} ({s.Replicas}) and deletes the service definition. Volumes, secrets and configs it uses are kept.", "Remove", true)) return;
        if (Stale()) return;
        await Run("Remove service " + s.Name, ["service", "rm", s.Id]);
        await AfterChange();
    }

    private async Task BulkRemoveAsync(IReadOnlyList<SwarmServiceRow> items)
    {
        if (Stale() || items.Count == 0) return;
        if (!table.Search.IsValid) { Host.Notify.Error("The search pattern is invalid. Fix it before acting on a selection."); return; }
        if (!await Host.Overlay.SuperConfirmAsync($"Remove {items.Count} services", "Every listed service and all of its tasks are stopped and deleted. Volumes, secrets and configs are kept.", items.Select(s => $"{s.Name} ({s.Replicas}, {s.Image})").ToList())) return;
        if (Stale()) return;
        await Run($"Remove {items.Count} services", ["service", "rm", .. items.Select(s => s.Id)]);
        await AfterChange();
    }

    /// <summary>Tasks of one service for the inspector.</summary>
    public async Task<IReadOnlyList<SwarmTaskRow>> TasksAsync(SwarmServiceRow s)
    {
        var rows = await Host.ReadRows(["service", "ps", "--no-trunc", "--format", "{{json .}}", s.Id], Context());
        return rows.Select(r => new SwarmTaskRow(CliValues.Str(r, "ID"), CliValues.Str(r, "Name"), CliValues.Str(r, "Node"), CliValues.Str(r, "DesiredState"), CliValues.Str(r, "CurrentState"), CliValues.Str(r, "Error"), CliValues.Str(r, "Image"))).ToList();
    }
}

/// <summary>One task from <c>docker service ps</c>.</summary>
public sealed record SwarmTaskRow(string Id, string Name, string Node, string DesiredState, string CurrentState, string Error, string Image)
{
    public string SearchText => $"{Name} {Node} {DesiredState} {CurrentState} {Error} {Image}";
}
