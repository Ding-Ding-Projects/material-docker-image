using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>One row of <c>docker service ls</c>.</summary>
public sealed record SwarmServiceRow(string Id, string Name, string Mode, string Replicas, string Image, string Ports, string Stack = "", string Updated = "")
{
    /// <summary>Name with the owning stack (or "standalone") on a second line, as the design shows it.</summary>
    public string NameWithStack => Name + "\n" + (Stack.Length > 0 ? Loc.T("stack") + " " + Stack : Loc.T("standalone"));
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
        table.EmptyTitle = Loc.T("No services");
        table.EmptyMessage = Loc.T("This swarm runs no services yet. Create one, or deploy a stack from a Compose file.");
        table.Column("Name", s => s.NameWithStack, 190)
            .Column("Mode", s => s.Mode, 96)
            .ChipColumn("Replicas", s => s.Replicas, s => ToneOf(s.Health), 84)
            .Column("Image", s => s.Image, 230, true)
            .Column("Ports", s => s.Ports, 130, true)
            .Column("Updated", s => s.Updated, 100)
            .Filter("Not converged", s => s.Health is SwarmHealth.Partial or SwarmHealth.Down)
            .Filter("Global", s => !s.Replicated)
            .RowAction("Inspect", Icons.DataObject, s => InspectAsync("service", s.Id, Loc.T("Service") + " " + s.Name), "Enter")
            .RowAction("Logs…", Icons.Terminal, LogsAsync)
            .RowAction("Scale…", Icons.UnfoldMore, ScaleAsync)
            .RowAction("Update image…", Icons.CloudSync, UpdateImageAsync)
            .RowAction("Roll back…", Icons.Undo, RollbackAsync)
            .RowAction("Copy name", Icons.ContentCopy, s => { PageKit.Copy(s.Name, "the service name"); return Task.CompletedTask; })
            .RowAction("Remove…", Icons.Delete, RemoveAsync, "Del", true)
            .BulkAction("Remove selected…", Icons.Delete, BulkRemoveAsync, true);
        table.Activated += s => _ = Guard(() => InspectAsync("service", s.Id, Loc.T("Service") + " " + s.Name));
        table.FocusedChanged += s => _ = Guard(() => inspector.ShowAsync(s));
        var split = Ui.SplitInspector(table, inspector, 1150, 420, summary: () => table.Focused is { } f ? $"{f.Name} · {f.Replicas} · {f.Image}" : null);
        table.FocusedChanged += _ => (split.Tag as Action)?.Invoke();
        LayoutContent(split);
    }

    public override async Task LoadAsync()
    {
        var rows = await Host.ReadRows(["service", "ls", "--format", "{{json .}}"], Context());
        var meta = await ReadMetaAsync(rows.Select(r => CliValues.Str(r, "ID")).Where(i => i.Length > 0).ToList());
        var items = rows.Select(r =>
        {
            var id = CliValues.Str(r, "ID");
            var (stack, updated) = meta.TryGetValue(id, out var m) ? m : ("", "");
            return new SwarmServiceRow(id, CliValues.Str(r, "Name"), CliValues.Str(r, "Mode"), CliValues.Str(r, "Replicas"), CliValues.Str(r, "Image"), CliValues.Str(r, "Ports"), stack, updated);
        }).ToList();
        table.SetItems(items);
        var converged = items.Count(i => i.Health == SwarmHealth.Healthy);
        SetSummary(Loc.Line($"{items.Count} service{(items.Count == 1 ? "" : "s")} · {converged} at desired replicas"));
        await inspector.ShowAsync(table.Focused);
    }

    /// <summary>Stack namespace label and relative update time per service from <c>docker service inspect</c>; missing data stays blank.</summary>
    private async Task<Dictionary<string, (string Stack, string Updated)>> ReadMetaAsync(IReadOnlyList<string> ids)
    {
        var map = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        if (ids.Count == 0) return map;
        try
        {
            var rows = await Host.ReadRows(["service", "inspect", "--format", "{{json .}}", .. ids], Context());
            foreach (var r in rows)
            {
                var id = CliValues.Str(r, "ID");
                var stack = "";
                if (r.TryGetProperty("Spec", out var spec) && spec.TryGetProperty("Labels", out var labels) && labels.ValueKind == System.Text.Json.JsonValueKind.Object
                    && labels.TryGetProperty("com.docker.stack.namespace", out var ns) && ns.ValueKind == System.Text.Json.JsonValueKind.String) stack = ns.GetString() ?? "";
                var updated = DateTimeOffset.TryParse(CliValues.Str(r, "UpdatedAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? AgoText(at) : "";
                if (id.Length > 0) map[id] = (stack, updated);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or System.Text.Json.JsonException) { }
        return map;
    }

    private static string AgoText(DateTimeOffset at)
    {
        var span = DateTimeOffset.UtcNow - at;
        if (span.TotalSeconds < 60) return Loc.T("just now");
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours} h ago";
        return $"{(int)span.TotalDays} d ago";
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
        var value = await Host.Overlay.PromptAsync(Loc.T("Scale") + " " + s.Name, "Replicas", current,
            $"Currently {s.Replicas}. The swarm starts or stops tasks until this many run. 0 stops every task but keeps the service.",
            v => int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 0 and <= 10000 ? null : "Enter a whole number from 0 to 10000.");
        if (value == null || Stale()) return;
        await Run($"Scale {s.Name} to {value}", ["service", "scale", s.Name + "=" + value]);
        await AfterChange();
    }

    /// <summary>Scales to a count chosen in the inspector's stepper, without the prompt.</summary>
    public async Task ScaleToAsync(SwarmServiceRow s, int count)
    {
        if (Stale()) return;
        if (!s.Replicated) { Host.Notify.Error($"{s.Name} runs in global mode: one task per eligible node. Only replicated services can be scaled."); return; }
        if (count is < 0 or > 10000) { Host.Notify.Error("Enter a whole number from 0 to 10000."); return; }
        await Run($"Scale {s.Name} to {count}", ["service", "scale", s.Name + "=" + count.ToString(CultureInfo.InvariantCulture)]);
        await AfterChange();
    }

    /// <summary>Starts a rolling update to the image typed in the inspector, without the prompt.</summary>
    public async Task UpdateImageToAsync(SwarmServiceRow s, string image)
    {
        if (Stale()) return;
        if (PageKit.ValidateReference(image) is { } error) { Host.Notify.Error(error); return; }
        await Run($"Update {s.Name} to {image}", ["service", "update", "--image", image, s.Id]);
        await AfterChange();
    }

    public async Task UpdateImageAsync(SwarmServiceRow s)
    {
        if (Stale()) return;
        var current = s.Image.Split('@')[0];
        var value = await Host.Overlay.PromptAsync(Loc.T("Update image of") + " " + s.Name, "Image", current,
            "Starts a rolling update to this image using the service's update policy. The previous specification is kept for Roll back.",
            v => PageKit.ValidateReference(v));
        if (value == null || Stale()) return;
        await Run($"Update {s.Name} to {value}", ["service", "update", "--image", value, s.Id]);
        await AfterChange();
    }

    public async Task RollbackAsync(SwarmServiceRow s)
    {
        if (Stale()) return;
        if (!await Host.Overlay.ConfirmAsync(Loc.T("Roll back") + " " + s.Name, "Returns the service to its previous specification (image, environment, ports and other settings) using the rollback policy. Only one previous specification is kept.", "Roll back")) return;
        if (Stale()) return;
        await Run(Loc.T("Roll back") + " " + s.Name, ["service", "rollback", s.Id]);
        await AfterChange();
    }

    public async Task RemoveAsync(SwarmServiceRow s)
    {
        if (Stale()) return;
        if (!await Host.Overlay.ConfirmAsync(Loc.T("Remove service") + " " + s.Name, $"Stops every task of {s.Name} ({s.Replicas}) and deletes the service definition. Volumes, secrets and configs it uses are kept.", "Remove", true)) return;
        if (Stale()) return;
        await Run(Loc.T("Remove service") + " " + s.Name, ["service", "rm", s.Id]);
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
