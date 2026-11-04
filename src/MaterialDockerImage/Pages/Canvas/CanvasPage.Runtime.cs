using System.Text.Json;
using System.Windows;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Runtime view: containers on the left, networks and volumes on the right, edges from inspected memberships and mounts.</summary>
public sealed partial class CanvasPage
{
    private const int MaxContainers = 100;
    private const double RuntimeNodeHeight = 108, RuntimeGap = 28, RuntimeColumnGap = 220;

    private sealed record RuntimeContainer(string Id, string Name, string State, string Image, JsonElement Record);

    private async Task DrawRuntimeAsync()
    {
        var context = SnapshotContext;
        var generation = SnapshotGeneration;
        subtitle.Text = Loc.T("Reading containers…");
        IReadOnlyList<JsonElement> rows;
        try { rows = await Host.ReadRows(["container", "ls", "--all", "--format", "{{json .}}"], context); }
        catch (Exception e) when (e is InvalidOperationException or JsonException)
        {
            if (!Host.IsCurrent(context, generation) || view != View.Runtime) return;
            ShowRuntimeError(e.Message);
            return;
        }
        if (!Host.IsCurrent(context, generation) || view != View.Runtime) return;
        var ids = rows.Select(x => DockerJson.Text(x, "ID")).Where(x => x.Length > 0).Take(MaxContainers).ToList();
        SetWarning(rows.Count > MaxContainers ? $"Showing the first {MaxContainers} of {rows.Count} containers." : null);
        if (ids.Count == 0)
        {
            surface.Clear(10, 10);
            SetObjects([], "");
            subtitle.Text = $"{Loc.T("No containers")} · {Loc.T("context")} {context ?? Loc.T("current")}";
            SetEmpty(Ui.EmptyState(Icons.DeployedCode, "No containers", "The selected engine reports no containers. Run an image from Images or start a Compose project to see its topology.",
                Ui.Button("Open Images", Icons.Layers, ButtonKind.Filled, () => Host.Navigate("Images"))));
            ShowInspectorHint("No containers to inspect.");
            return;
        }
        var inspect = await Host.Read(["container", "inspect", .. ids], context);
        if (!Host.IsCurrent(context, generation) || view != View.Runtime) return;
        if (!inspect.Success || inspect.Truncated)
        {
            ShowRuntimeError(inspect.Truncated ? "The inspection output exceeded the capture limit." : inspect.StandardError.Trim().Length > 0 ? inspect.StandardError.Trim() : $"docker exited with code {inspect.ExitCode}");
            return;
        }
        List<RuntimeContainer> containers;
        try
        {
            using var document = JsonDocument.Parse(inspect.StandardOutput);
            containers = document.RootElement.EnumerateArray().Select(c => c.Clone()).Select(c => new RuntimeContainer(
                DockerJson.Text(c, "Id"), DockerJson.Text(c, "Name").TrimStart('/'),
                c.TryGetProperty("State", out var s) ? DockerJson.Text(s, "Status") : "unknown",
                c.TryGetProperty("Config", out var cfg) ? DockerJson.Text(cfg, "Image") : "", c)).ToList();
        }
        catch (JsonException e) { ShowRuntimeError(Loc.T("The inspection output could not be read:") + " " + e.Message); return; }
        DrawRuntime(containers, context);
    }

    private void ShowRuntimeError(string message)
    {
        surface.Clear(10, 10);
        SetObjects([], "");
        subtitle.Text = Loc.T("Topology unavailable");
        SetEmpty(Ui.EmptyState(Icons.Error, "Topology unavailable", message + " " + Loc.T("Any previous graph was cleared because it may be stale."),
            Ui.AsyncButton("Retry", Icons.Refresh, ButtonKind.Filled, RefreshAsync), Ui.Button("Diagnostics", Icons.Stethoscope, ButtonKind.Text, () => Host.Navigate("Diagnostics"))));
        ShowInspectorHint("Nothing to inspect.");
    }

    private void DrawRuntime(List<RuntimeContainer> containers, string? context)
    {
        SetEmpty(null);
        var networks = new SortedDictionary<string, List<int>>(StringComparer.Ordinal);
        var volumes = new SortedDictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < containers.Count; i++)
        {
            var c = containers[i].Record;
            if (c.TryGetProperty("NetworkSettings", out var ns) && ns.TryGetProperty("Networks", out var nets) && nets.ValueKind == JsonValueKind.Object)
                foreach (var n in nets.EnumerateObject()) { if (!networks.TryGetValue(n.Name, out var list)) networks[n.Name] = list = new(); list.Add(i); }
            if (c.TryGetProperty("Mounts", out var mounts) && mounts.ValueKind == JsonValueKind.Array)
                foreach (var m in mounts.EnumerateArray())
                    if (DockerJson.Text(m, "Type") == "volume") { var name = DockerJson.Text(m, "Name"); if (!volumes.TryGetValue(name, out var list)) volumes[name] = list = new(); list.Add(i); }
        }
        var resources = networks.Select(n => (Kind: "network", Name: n.Key, Members: n.Value)).Concat(volumes.Select(v => (Kind: "volume", Name: v.Key, Members: v.Value))).ToList();
        const double left = 40, top = 56;
        var right = left + GraphSurface.NodeWidth + RuntimeColumnGap;
        var height = top + Math.Max(containers.Count, resources.Count) * (RuntimeNodeHeight + RuntimeGap) + 40;
        surface.Clear(right + GraphSurface.NodeWidth + 40, Math.Max(400, height));
        surface.AddLabel(left, 24, Loc.T("Containers"));
        surface.AddLabel(right, 24, Loc.T("Networks and volumes"));

        for (var r = 0; r < resources.Count; r++)
        {
            var y = top + r * (RuntimeNodeHeight + RuntimeGap) + RuntimeNodeHeight / 2;
            var isNetwork = resources[r].Kind == "network";
            foreach (var index in resources[r].Members)
                surface.AddEdge(new Point(left + GraphSurface.NodeWidth, top + index * (RuntimeNodeHeight + RuntimeGap) + RuntimeNodeHeight / 2), new Point(right - 2, y), "", isNetwork ? "Primary" : "Tertiary", !isNetwork);
        }
        var items = new List<GraphObject>();
        for (var i = 0; i < containers.Count; i++)
        {
            var c = containers[i];
            var running = c.State == "running";
            var content = Ui.VStack(4,
                Ui.Columns(8, [0, 1, 0], Ui.Icon(Icons.DeployedCode, 20, true, running ? "Success" : "Outline"), Ui.Text(c.Name, 15, FontWeights.SemiBold, "OnSurface", false, false), Ui.Badge(c.State, running ? Tone.Success : c.State is "exited" or "dead" ? Tone.Error : Tone.Neutral)),
                Ui.Mono(c.Image, 12, "OnSurfaceVariant"),
                Ui.Text(c.Id.Length >= 12 ? c.Id[..12] : c.Id, 12, null, "Outline", false, false));
            var key = "container:" + c.Id;
            surface.AddNode(left, top + i * (RuntimeNodeHeight + RuntimeGap), GraphSurface.NodeWidth, content, $"Container {c.Name}, {c.State}", key, () => SelectContainer(c)).Height = RuntimeNodeHeight;
            items.Add(new(Icons.DeployedCode, c.Name, c.State, 0, key, () => SelectContainer(c)));
        }
        for (var r = 0; r < resources.Count; r++)
        {
            var (kind, name, members) = resources[r];
            var isNetwork = kind == "network";
            var content = Ui.VStack(4,
                Ui.Columns(8, [0, 1], Ui.Icon(isNetwork ? Icons.Lan : Icons.Database, 20, true, isNetwork ? "Primary" : "Tertiary"), Ui.Text(name, 15, FontWeights.SemiBold, "OnSurface", false, false)),
                Ui.Caption($"{Loc.T(isNetwork ? "Network" : "Volume")} · {members.Count} {Loc.T(members.Count == 1 ? "attachment" : "attachments")}"));
            var key = kind + ":" + name;
            var attached = members.Select(m => containers[m].Name).ToList();
            surface.AddNode(right, top + r * (RuntimeNodeHeight + RuntimeGap), GraphSurface.NodeWidth, content, $"{kind} {name}, {members.Count} attachments", key, () => SelectResource(kind, name, attached), isNetwork ? "SurfaceContainer" : "SurfaceContainer").Height = RuntimeNodeHeight;
            items.Add(new(isNetwork ? Icons.Lan : Icons.Database, name, Loc.T(isNetwork ? "network" : "volume"), 0, key, () => SelectResource(kind, name, attached)));
        }
        SetObjects(items, $"{Loc.T("Observed")} {DateTimeOffset.Now:HH:mm:ss}. {Loc.T("Edges come from inspected network memberships and volume mounts.")}");
        subtitle.Text = $"{containers.Count} {Loc.T(containers.Count == 1 ? "container" : "containers")} · {networks.Count} {Loc.T(networks.Count == 1 ? "network" : "networks")} · {volumes.Count} {Loc.T(volumes.Count == 1 ? "volume" : "volumes")} · {Loc.T("context")} {context ?? Loc.T("current")}";
        if (containers.Count > 0) SelectContainer(containers[0]);
        Dispatcher.BeginInvoke(surface.Fit, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void SelectContainer(RuntimeContainer c)
    {
        SyncSelection("container:" + c.Id);
        inspector.Children.Clear();
        inspector.Children.Add(Ui.VStack(4, Ui.Overline("Container"), Ui.Title(c.Name, 22), Ui.Mono(c.Id.Length >= 12 ? c.Id[..12] : c.Id, 12, "Outline"), Ui.Badge(c.State, c.State == "running" ? Tone.Success : Tone.Neutral)));
        inspector.Children.Add(Section("Image", Ui.Mono(c.Image, 13, "OnSurface", true)));
        var record = c.Record;
        if (record.TryGetProperty("NetworkSettings", out var ns) && ns.TryGetProperty("Networks", out var nets) && nets.ValueKind == JsonValueKind.Object)
        {
            var lines = nets.EnumerateObject().Select(n => n.Name + (DockerJson.Text(n.Value, "IPAddress") is { Length: > 0 } ip ? " · " + ip : "")).ToList();
            if (lines.Count > 0) inspector.Children.Add(Section("Networks", Ui.Mono(string.Join("\n", lines), 12, "OnSurfaceVariant", true)));
        }
        if (record.TryGetProperty("Mounts", out var mounts) && mounts.ValueKind == JsonValueKind.Array)
        {
            var lines = mounts.EnumerateArray().Select(m => $"{DockerJson.Text(m, "Type")} {(DockerJson.Text(m, "Type") == "volume" ? DockerJson.Text(m, "Name") : DockerJson.Text(m, "Source"))} → {DockerJson.Text(m, "Destination")}" + (DockerJson.Text(m, "RW") == "False" || DockerJson.Text(m, "RW") == "false" ? " (read-only)" : "")).ToList();
            if (lines.Count > 0) inspector.Children.Add(Section("Mounts", Ui.Mono(string.Join("\n", lines), 12, "OnSurfaceVariant", true)));
        }
        if (record.TryGetProperty("NetworkSettings", out var ns2) && ns2.TryGetProperty("Ports", out var ports) && ports.ValueKind == JsonValueKind.Object)
        {
            var lines = new List<string>();
            foreach (var p in ports.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Array) foreach (var b in p.Value.EnumerateArray()) lines.Add($"{DockerJson.Text(b, "HostIp")}:{DockerJson.Text(b, "HostPort")} → {p.Name}");
                else lines.Add(p.Name + " " + Loc.T("(not published)"));
            if (lines.Count > 0) inspector.Children.Add(Section("Ports", Ui.Mono(string.Join("\n", lines), 12, "OnSurfaceVariant", true)));
        }
        var actions = Ui.Wrap(8,
            Ui.AsyncButton("Inspect", Icons.DataObject, ButtonKind.Outlined, () => InspectAsync("container", c.Id, c.Name)),
            c.State == "running" ? null : Ui.AsyncButton("Start", Icons.PlayArrow, ButtonKind.Tonal, () => ContainerActionAsync("start", c)),
            c.State == "running" ? Ui.AsyncButton("Restart", Icons.RestartAlt, ButtonKind.Tonal, () => ContainerActionAsync("restart", c)) : null,
            c.State == "running" ? Ui.AsyncButton("Stop", Icons.Stop, ButtonKind.Tonal, () => ContainerActionAsync("stop", c)) : null,
            Ui.Button("Open Containers", Icons.DeployedCode, ButtonKind.Text, () => Host.Navigate("Containers")));
        inspector.Children.Add(actions.Margin(0, 16, 0, 0));
    }

    private void SelectResource(string kind, string name, IReadOnlyList<string> attached)
    {
        SyncSelection(kind + ":" + name);
        inspector.Children.Clear();
        inspector.Children.Add(Ui.VStack(4, Ui.Overline(kind == "network" ? "Network" : "Volume"), Ui.Title(name, 22)));
        inspector.Children.Add(Section("Attached containers", Ui.Mono(string.Join("\n", attached), 12, "OnSurfaceVariant", true)));
        inspector.Children.Add(Ui.Wrap(8,
            Ui.AsyncButton("Inspect", Icons.DataObject, ButtonKind.Outlined, () => InspectAsync(kind, name, name)),
            Ui.Button("Open Storage", Icons.Database, ButtonKind.Text, () => Host.Navigate("Storage"))).Margin(0, 16, 0, 0));
    }

    private async Task InspectAsync(string kind, string reference, string label)
    {
        if (!IsCurrent) { Host.Notify.Warning("The engine context changed after this topology was loaded. Refresh before inspecting."); return; }
        var result = await Host.Read([kind, "inspect", reference], SnapshotContext);
        if (!result.Success) { Host.Notify.Error($"Inspect {label} failed: " + (result.StandardError.Trim().Length > 0 ? result.StandardError.Trim() : "exit " + result.ExitCode)); return; }
        ShowJson($"{Loc.T(kind == "container" ? "Container" : kind == "network" ? "Network" : "Volume")} {label}", result.StandardOutput.Trim());
    }

    private async Task ContainerActionAsync(string action, RuntimeContainer c)
    {
        if (!IsCurrent) { Host.Notify.Warning("The engine context changed after this topology was loaded. Refresh before acting."); return; }
        if (action != "start" && !await Host.Overlay.ConfirmAsync($"{char.ToUpperInvariant(action[0])}{action[1..]} {c.Name}?", $"{Loc.T("Container")} {c.Name} ({(c.Id.Length >= 12 ? c.Id[..12] : c.Id)}) · {Loc.T("context")} {SnapshotContext ?? Loc.T("current")}", char.ToUpperInvariant(action[0]) + action[1..], action == "stop"))
            return;
        var result = await Host.Execute($"{char.ToUpperInvariant(action[0])}{action[1..]} container {c.Name}", ["container", action, c.Id], boundContext: SnapshotContext);
        if (result != null) await RefreshAsync();
    }
}
