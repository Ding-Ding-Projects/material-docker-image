using System.Net;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

public sealed record NetworkRow(string Id, string Name, string Driver, string Scope, bool Internal, bool IPv6, bool Attachable, IReadOnlyList<string> Subnets, IReadOnlyList<string> Containers, string Created)
{
    public bool BuiltIn => Name is "bridge" or "host" or "none";
    public string SubnetText => Subnets.Count == 0 ? "—" : string.Join(", ", Subnets);
}

/// <summary>Networks list with create panel, connect/disconnect, inspect, remove and prune.</summary>
public sealed class NetworksView : Grid
{
    private static AppHost Host => AppHost.Current;
    private readonly DataTable<NetworkRow> table = new("Networks", "Search networks");
    private readonly NetworkCreatePanel createPanel = new();
    private readonly ColumnDefinition panelColumn = new() { Width = new GridLength(0) };
    private readonly Func<string?> context;
    private readonly Func<bool> isCurrent;
    private readonly Action<string> setSummary;
    public Func<Task>? Reload { get; set; }

    public NetworksView(Func<string?> boundContext, Func<bool> current, Action<string> summary)
    {
        context = boundContext; isCurrent = current; setSummary = summary;
        table.Key = n => n.Id;
        table.EmptyTitle = "No networks";
        table.EmptyMessage = "The engine reported no networks. Every engine normally has bridge, host and none; check the engine connection in Diagnostics.";
        table.Column("Name", n => n.Name, 220, true)
            .ChipColumn("Driver", n => n.Driver, n => n.BuiltIn ? Tone.Neutral : Tone.Primary, 100)
            .Column("Scope", n => n.Scope, 70)
            .Column("Subnet", n => n.SubnetText, 170, true)
            .Column("Containers", n => n.Containers.Count.ToString(), 90)
            .Column("Internal", n => n.Internal ? "Yes" : "No", 70)
            .Column("IPv6", n => n.IPv6 ? "Yes" : "No", 60)
            .Filter("User-defined", n => !n.BuiltIn)
            .Filter("Unused", n => n.Containers.Count == 0)
            .RowAction("Inspect", Icons.DataObject, InspectAsync, "Enter")
            .RowAction("Connect container…", Icons.Link, ConnectAsync)
            .RowAction("Disconnect container…", Icons.Cable, DisconnectAsync)
            .RowAction("Copy name", Icons.ContentCopy, n => { Clipboard.SetText(n.Name); return Task.CompletedTask; })
            .RowAction("Remove…", Icons.Delete, RemoveAsync, "Del", true)
            .BulkAction("Remove selected…", Icons.Delete, BulkRemoveAsync, true);
        table.Activated += n => _ = Guarded(() => InspectAsync(n));
        createPanel.Closed += () => { panelColumn.Width = new GridLength(0); createPanel.Visibility = Visibility.Collapsed; };
        createPanel.SetExistingSubnets(() => table.Items.SelectMany(n => n.Subnets).ToList());
        createPanel.Create = async (args, name) =>
        {
            if (Stale()) return false;
            var result = await Host.Execute("Create network " + name, args, boundContext: context());
            if (result?.Success != true) return false;
            if (Reload != null) await Reload();
            return true;
        };
        var create = Ui.Button("Create network", Icons.Add, ButtonKind.Filled, () => { panelColumn.Width = new GridLength(380); createPanel.Visibility = Visibility.Visible; });
        var connect = Ui.AsyncButton("Connect container…", Icons.Link, ButtonKind.Tonal, () => table.Focused is { } n ? ConnectAsync(n) : NoSelection());
        var prune = Ui.AsyncButton("Prune…", Icons.CleaningServices, ButtonKind.Outlined, PruneAsync);
        var left = Ui.Rows(8, [0, 1], Ui.Wrap(8, create, connect, prune), table);
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(panelColumn);
        Children.Add(left);
        createPanel.Margin = new Thickness(16, 0, 0, 0);
        createPanel.Visibility = Visibility.Collapsed;
        Children.Add(createPanel.At(0, 1));
    }

    private static Task NoSelection() { Host.Notify.Info("Select a network first."); return Task.CompletedTask; }
    private static async Task Guarded(Func<Task> action) { try { await action(); } catch (Exception e) when (e is not OutOfMemoryException) { Host.Notify.Error(e.Message); } }
    private bool Stale()
    {
        if (isCurrent()) return false;
        Host.Notify.Error("This list belongs to an earlier engine context. Refresh before acting.");
        return true;
    }

    public async Task LoadAsync()
    {
        var ctx = context();
        var rows = await Host.ReadRows(["network", "ls", "--format", "{{json .}}"], ctx);
        var ids = rows.Select(r => CliValues.Str(r, "ID")).Where(i => i.Length > 0).ToList();
        var details = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (ids.Count > 0)
        {
            var inspect = await Host.Read(["network", "inspect", .. ids], ctx);
            if (inspect.Success)
            {
                try
                {
                    using var doc = JsonDocument.Parse(inspect.StandardOutput);
                    foreach (var n in doc.RootElement.EnumerateArray()) details[CliValues.Str(n, "Id")] = n.Clone();
                }
                catch (JsonException) { }
            }
        }
        var items = rows.Select(r =>
        {
            var id = CliValues.Str(r, "ID");
            var d = details.FirstOrDefault(p => p.Key.StartsWith(id, StringComparison.Ordinal)).Value;
            var has = d.ValueKind == JsonValueKind.Object;
            var subnets = has ? CliValues.Arr(d, "IPAM", "Config").Select(c => CliValues.Str(c, "Subnet")).Where(s => s.Length > 0).ToList() : [];
            var containers = has ? CliValues.Obj(d, "Containers").Select(c => CliValues.Str(c.Value, "Name")).Where(s => s.Length > 0).ToList() : [];
            return new NetworkRow(id, CliValues.Str(r, "Name"), CliValues.Str(r, "Driver"), CliValues.Str(r, "Scope"),
                has ? CliValues.Bool(d, "Internal") : CliValues.Bool(r, "Internal"),
                has ? CliValues.Bool(d, "EnableIPv6") : CliValues.Bool(r, "IPv6"),
                has && CliValues.Bool(d, "Attachable"), subnets, containers, CliValues.Str(r, "CreatedAt"));
        }).ToList();
        table.SetItems(items);
        setSummary($"{items.Count} network{(items.Count == 1 ? "" : "s")} · {items.Count(i => i.BuiltIn)} built-in · {items.Count(i => !i.BuiltIn && i.Containers.Count == 0)} user-defined without containers");
    }

    private async Task InspectAsync(NetworkRow n)
    {
        var r = await Host.Read(["network", "inspect", n.Name], context());
        if (!r.Success) { Host.Notify.Error($"Inspect {n.Name} failed: {r.StandardError.Trim()}"); return; }
        ResourceTextViewer.Show("Network " + n.Name, r.StandardOutput, $"docker network inspect · {n.Containers.Count} container(s)");
    }

    private async Task ConnectAsync(NetworkRow n)
    {
        if (Stale()) return;
        if (n.Name is "host" or "none") { Host.Notify.Warning($"Containers cannot be connected to the built-in {n.Name} network after creation."); return; }
        var running = await Host.ReadRows(["container", "ls", "--format", "{{json .}}"], context());
        var candidates = running.Select(r => CliValues.Str(r, "Names")).Where(x => x.Length > 0 && !n.Containers.Contains(x)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        if (candidates.Count == 0) { Host.Notify.Info($"No running container is available to connect to {n.Name}. Every running container is already connected, or none is running."); return; }
        var picker = new SearchablePicker("Running containers", candidates);
        var alias = Ui.Input("", "Alias", true);
        var ip = Ui.Input("", "IPv4 address", true);
        var error = Ui.Text("", 13, null, "Error");
        var done = new TaskCompletionSource<List<string>?>();
        Action? close = null;
        void Apply()
        {
            if (picker.Selected is not { } container) { error.Text = Loc.T("Pick a container."); return; }
            var args = new List<string> { "network", "connect" };
            var a = alias.Text.Trim();
            if (a.Length > 0) { if (a.StartsWith('-') || a.Any(char.IsWhiteSpace)) { error.Text = Loc.T("Aliases cannot contain spaces or begin with '-'."); return; } args.AddRange(["--alias", a]); }
            var address = ip.Text.Trim();
            if (address.Length > 0)
            {
                if (!IPAddress.TryParse(address, out var parsed) || parsed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) { error.Text = Loc.T("Enter a valid IPv4 address or leave it empty."); return; }
                args.AddRange(["--ip", address]);
            }
            args.AddRange([n.Name, container]);
            done.TrySetResult(args); close!();
        }
        var body = Ui.VStack(14,
            Ui.Title("Connect to " + n.Name, 20),
            Ui.Field("Container", picker, "Only running containers that are not already on this network are listed."),
            Ui.Columns(12, [1, 1],
                Ui.Field("Alias (optional)", alias, "An extra DNS name other containers on this network can use to reach this one.", "api"),
                Ui.Field("IPv4 address (optional)", ip, "A fixed address inside this network's subnet. Only user-defined networks with a configured subnet accept it.", n.Subnets.FirstOrDefault() is { } s ? s.Split('/')[0] : "172.28.0.10")),
            error,
            Ui.Columns(8, [1, 0, 0], new Border(), Ui.Button("Cancel", null, ButtonKind.Text, () => { done.TrySetResult(null); close!(); }), Ui.Button("Connect", Icons.Link, ButtonKind.Filled, Apply)));
        close = Host.Overlay.Show(body, 560, () => done.TrySetResult(null));
        if (await done.Task is not { } connectArgs) return;
        if (Stale()) return;
        await Host.Execute($"Connect {connectArgs[^1]} to {n.Name}", connectArgs, boundContext: context());
        if (Reload != null) await Reload();
    }

    private async Task DisconnectAsync(NetworkRow n)
    {
        if (Stale()) return;
        if (n.Containers.Count == 0) { Host.Notify.Info($"No containers are connected to {n.Name}."); return; }
        var picker = new SearchablePicker("Connected containers", n.Containers.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList());
        var force = Ui.Check("Force disconnect", false);
        var done = new TaskCompletionSource<(string, bool)?>();
        Action? close = null;
        var body = Ui.VStack(14,
            Ui.Title("Disconnect from " + n.Name, 20),
            Ui.Field("Container", picker),
            Ui.Field("Force", force, "Forces the disconnect even when the engine reports the endpoint is in use. Use only when a normal disconnect fails."),
            Ui.Columns(8, [1, 0, 0], new Border(), Ui.Button("Cancel", null, ButtonKind.Text, () => { done.TrySetResult(null); close!(); }),
                Ui.Button("Disconnect", Icons.Cable, ButtonKind.Danger, () => { if (picker.Selected is { } c) { done.TrySetResult((c, force.IsChecked == true)); close!(); } })));
        close = Host.Overlay.Show(body, 520, () => done.TrySetResult(null));
        if (await done.Task is not { } choice) return;
        if (Stale()) return;
        var args = new List<string> { "network", "disconnect" };
        if (choice.Item2) args.Add("--force");
        args.AddRange([n.Name, choice.Item1]);
        await Host.Execute($"Disconnect {choice.Item1} from {n.Name}", args, boundContext: context());
        if (Reload != null) await Reload();
    }

    private async Task RemoveAsync(NetworkRow n)
    {
        if (Stale()) return;
        if (n.BuiltIn) { Host.Notify.Warning($"{n.Name} is a built-in network and cannot be removed."); return; }
        var detail = n.Containers.Count > 0 ? $" It still has {n.Containers.Count} container(s) attached ({string.Join(", ", n.Containers)}); the engine refuses to remove it until they disconnect." : "";
        if (!await Host.Overlay.ConfirmAsync("Remove network " + n.Name + "?", "Containers that use this network by name will fail to start until it is created again." + detail, "Remove", true)) return;
        if (Stale()) return;
        await Host.Execute("Remove network " + n.Name, ["network", "rm", n.Name], boundContext: context());
        if (Reload != null) await Reload();
    }

    private async Task BulkRemoveAsync(IReadOnlyList<NetworkRow> items)
    {
        if (Stale()) return;
        var builtIn = items.Where(i => i.BuiltIn).ToList();
        var removable = items.Where(i => !i.BuiltIn).ToList();
        if (builtIn.Count > 0) Host.Notify.Warning("Built-in networks are skipped: " + string.Join(", ", builtIn.Select(b => b.Name)));
        if (removable.Count == 0) return;
        if (!await Host.Overlay.SuperConfirmAsync($"Remove {removable.Count} networks", "Each listed network is removed. Networks with attached containers are refused by the engine.", removable.Select(r => r.Name + (r.Containers.Count > 0 ? $" ({r.Containers.Count} attached)" : "")).ToList())) return;
        if (Stale()) return;
        await Host.Execute($"Remove {removable.Count} networks", ["network", "rm", .. removable.Select(r => r.Name)], boundContext: context());
        if (Reload != null) await Reload();
    }

    private async Task PruneAsync()
    {
        if (Stale()) return;
        var candidates = table.Items.Where(n => !n.BuiltIn && n.Containers.Count == 0).Select(n => n.Name).ToList();
        if (candidates.Count == 0) candidates.Add("No unused user-defined networks in the last refresh. The engine decides the final set when the prune runs.");
        if (!await Host.Overlay.SuperConfirmAsync("Prune unused networks", "Removes every user-defined network with no containers. Built-in networks are never removed. Stopped containers that reference a removed network will fail to start.", candidates)) return;
        if (Stale()) return;
        await Host.Execute("Prune networks", ["network", "prune", "-f"], boundContext: context());
        if (Reload != null) await Reload();
    }
}

/// <summary>A searchable single-choice list used by pickers in dialogs.</summary>
public sealed class SearchablePicker : StackPanel
{
    private readonly ListBox list = new() { MaxHeight = 220, MinHeight = 80 };
    private readonly SearchFilter search;
    private readonly IReadOnlyList<string> items;
    public string? Selected => list.SelectedItem as string;

    public SearchablePicker(string name, IReadOnlyList<string> values)
    {
        items = values;
        search = new SearchFilter("Search " + name.ToLowerInvariant(), 300) { Width = double.NaN, Margin = new Thickness(0, 0, 0, 8) };
        System.Windows.Automation.AutomationProperties.SetName(list, Loc.T(name));
        search.Changed += Apply;
        Children.Add(search);
        Children.Add(list);
        Apply();
        if (list.Items.Count > 0) list.SelectedIndex = 0;
    }

    private void Apply()
    {
        var keep = Selected;
        list.ItemsSource = items.Where(search.Matches).ToList();
        if (keep != null && list.Items.Contains(keep)) list.SelectedItem = keep;
        else if (list.Items.Count > 0) list.SelectedIndex = 0;
    }
}
