using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

public sealed record BuilderRow(string Name, string Driver, string Status, string Platforms, bool Current, string Endpoint)
{
    public static BuilderRow From(JsonElement row)
    {
        var nodes = row.TryGetProperty("Nodes", out var n) && n.ValueKind == JsonValueKind.Array ? n.EnumerateArray().ToList() : [];
        var platforms = nodes.SelectMany(node => node.TryGetProperty("Platforms", out var p) && p.ValueKind == JsonValueKind.Array ? p.EnumerateArray().Select(Platform) : []).Distinct().ToList();
        var status = nodes.Select(node => DockerJson.Text(node, "Status")).FirstOrDefault(s => s.Length > 0) ?? DockerJson.Text(row, "Status");
        var endpoint = nodes.Select(node => DockerJson.Text(node, "Endpoint")).FirstOrDefault(s => s.Length > 0) ?? "";
        var current = DockerJson.Text(row, "Current").Equals("true", StringComparison.OrdinalIgnoreCase);
        return new(DockerJson.Text(row, "Name"), DockerJson.Text(row, "Driver"), status.Length == 0 ? "unknown" : status, string.Join(", ", platforms), current, endpoint);
    }
    private static string Platform(JsonElement p)
    {
        if (p.ValueKind == JsonValueKind.String) return p.GetString() ?? "";
        var text = DockerJson.Text(p, "os") + "/" + DockerJson.Text(p, "architecture");
        var variant = DockerJson.Text(p, "variant");
        return variant.Length > 0 ? text + "/" + variant : text;
    }
}

/// <summary>Buildx builders: list, create, inspect, remove, disk usage and cache prune. It never runs buildx use.</summary>
internal sealed class BuildersPanel
{
    private static AppHost Host => AppHost.Current;
    private static readonly Regex BuilderName = new("^[A-Za-z0-9][A-Za-z0-9_.-]{0,62}$");
    private readonly Func<bool> isCurrent;
    private readonly Func<string?> snapshot;
    private readonly TextBlock state = Ui.Caption("");
    private readonly SearchableText detail = new("Search builder output", 120, 360);
    private readonly TextBlock detailTitle = Ui.Text("Select a builder, then Inspect or Disk usage.", 14, FontWeights.SemiBold);
    public DataTable<BuilderRow> Table { get; }
    public Border Card { get; }

    public BuildersPanel(Func<bool> isCurrent, Func<string?> snapshot)
    {
        this.isCurrent = isCurrent; this.snapshot = snapshot;
        Table = new DataTable<BuilderRow>("Builders", "Search builders")
        {
            EmptyTitle = "No builders listed",
            EmptyMessage = "docker buildx ls returned no builders, or buildx is not installed. Diagnostics checks buildx availability.",
            Key = b => b.Name
        };
        Table.Column("Name", b => b.Name + (b.Current ? " (default)" : ""), 200, true)
            .Column("Driver", b => b.Driver, 140)
            .ChipColumn("Status", b => b.Status, b => b.Status.Equals("running", StringComparison.OrdinalIgnoreCase) ? Tone.Success : b.Status.Equals("inactive", StringComparison.OrdinalIgnoreCase) ? Tone.Neutral : Tone.Warning, 110)
            .Column("Platforms", b => b.Platforms, 320)
            .Filter("Running", b => b.Status.Equals("running", StringComparison.OrdinalIgnoreCase))
            .RowAction("Inspect", Icons.Info, b => InspectAsync(b, false), "Enter")
            .RowAction("Inspect and start (bootstrap)", Icons.PlayArrow, b => InspectAsync(b, true))
            .RowAction("Disk usage", Icons.DataUsage, DiskUsageAsync)
            .RowAction("Prune build cache…", Icons.CleaningServices, PruneAsync, null, true)
            .RowAction("Remove builder…", Icons.Delete, RemoveAsync, "Del", true);
        Table.Activated += b => _ = InspectAsync(b, false);
        Table.MinHeight = 260; Table.MaxHeight = 480;
        var create = Ui.AsyncButton("Create builder", Icons.Add, ButtonKind.Tonal, CreateAsync);
        var refresh = Ui.AsyncIconButton(Icons.Refresh, "Refresh builders", RefreshAsync);
        var selected = Ui.HStack(8,
            Ui.AsyncButton("Inspect", Icons.Info, ButtonKind.Text, () => WithFocused(b => InspectAsync(b, false))),
            Ui.AsyncButton("Disk usage", Icons.DataUsage, ButtonKind.Text, () => WithFocused(DiskUsageAsync)),
            Ui.AsyncButton("Prune cache", Icons.CleaningServices, ButtonKind.Text, () => WithFocused(PruneAsync)),
            Ui.AsyncButton("Remove", Icons.Delete, ButtonKind.Text, () => WithFocused(RemoveAsync)));
        var note = Ui.Caption("The default builder is not changed here: this page never runs docker buildx use, because that changes the global default for every tool. Choosing a builder per build will belong to Build and is not implemented yet.");
        var body = Ui.VStack(12, state, Table, new WrapPanel { Children = { selected } }, note, Ui.Card(Ui.VStack(8, detailTitle, detail), 12, "SurfaceContainer", 12));
        Card = PageParts.Section("Builders", "docker buildx", body, create, refresh);
    }

    private async Task WithFocused(Func<BuilderRow, Task> action)
    {
        if (Table.Focused is not { } b) { Host.Notify.Info("Select a builder first."); return; }
        await action(b);
    }

    private bool Stale()
    {
        if (isCurrent()) return false;
        Host.Notify.Warning("The engine context changed after this list loaded. Refresh before acting on builders.");
        return true;
    }

    public async Task RefreshAsync()
    {
        state.Text = Loc.T("Loading builders…");
        try
        {
            var rows = await Host.ReadRows(["buildx", "ls", "--format", "{{json .}}"], snapshot());
            var builders = rows.Select(BuilderRow.From).Where(b => b.Name.Length > 0).ToList();
            Table.SetItems(builders);
            state.Text = $"{builders.Count} builder(s) · {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception e) when (e is InvalidOperationException or JsonException)
        {
            Table.SetItems([]);
            state.Text = Loc.T("Builders could not be listed: ") + e.Message;
        }
    }

    private async Task InspectAsync(BuilderRow b, bool bootstrap)
    {
        if (Stale()) return;
        if (bootstrap)
        {
            if (!await Host.Overlay.ConfirmAsync("Start builder " + b.Name + "?", "Bootstrap starts the builder if it is not running. For the docker-container driver this creates and starts a BuildKit container on the engine.", "Inspect and start")) return;
            var result = await Host.Execute("Bootstrap builder " + b.Name, ["buildx", "inspect", "--bootstrap", b.Name], true, snapshot());
            if (result != null) Show("buildx inspect --bootstrap " + b.Name, result);
            await RefreshAsync();
            return;
        }
        Show("buildx inspect " + b.Name, await Host.Read(["buildx", "inspect", b.Name], snapshot()));
    }

    private async Task DiskUsageAsync(BuilderRow b)
    {
        if (Stale()) return;
        detailTitle.Text = "buildx du --builder " + b.Name + " (running…)";
        Show("buildx du --builder " + b.Name, await Host.Read(["buildx", "du", "--builder", b.Name], snapshot()));
    }

    private void Show(string title, CommandResult result)
    {
        detailTitle.Text = $"{title} · exit {result.ExitCode}";
        detail.Text = (result.StandardOutput + (result.StandardError.Length > 0 ? "\n" + result.StandardError : "")).Trim();
    }

    private async Task PruneAsync(BuilderRow b)
    {
        if (Stale()) return;
        if (!await Host.Overlay.SuperConfirmAsync("Prune build cache", $"Runs docker buildx prune --force --builder {b.Name}. All unused build cache on this builder is deleted; the next builds take longer because layers are rebuilt.", [$"Build cache of builder {b.Name} ({b.Driver})", "Context: " + (snapshot() ?? "current")])) return;
        var result = await Host.Execute("Prune build cache on " + b.Name, ["buildx", "prune", "--force", "--builder", b.Name], true, snapshot());
        if (result != null) Show("buildx prune --builder " + b.Name, result);
    }

    private async Task RemoveAsync(BuilderRow b)
    {
        if (Stale()) return;
        if (b.Driver == "docker") { Host.Notify.Warning("Builders with the docker driver belong to an engine context and cannot be removed with buildx rm."); return; }
        if (!await Host.Overlay.ConfirmAsync("Remove builder " + b.Name + "?", $"Runs docker buildx rm {b.Name}. The builder and its BuildKit state are removed, including its build cache.", "Remove builder", true)) return;
        await Host.Execute("Remove builder " + b.Name, ["buildx", "rm", b.Name], true, snapshot());
        await RefreshAsync();
    }

    private async Task CreateAsync()
    {
        if (Stale()) return;
        var done = new TaskCompletionSource<bool>();
        Action? close = null;
        var name = Ui.Input("", "Builder name", true);
        var driver = Ui.Combo(["docker-container", "kubernetes", "remote"], "docker-container");
        var endpoint = Ui.Input("", "Endpoint", true);
        var platform = Ui.Input("", "Platforms", true);
        var bootstrap = Ui.Switch("Start it now (bootstrap)", true, _ => { });
        var error = Ui.Text("", 13, null, "Error");
        var endpointField = Ui.Field("Endpoint (remote driver)", endpoint, "The BuildKit address for the remote driver, such as tcp://buildkit.example:1234. Leave empty for other drivers.", "tcp://10.0.4.21:1234");
        void Sync() => endpointField.Visibility = driver.SelectedItem as string == "remote" ? Visibility.Visible : Visibility.Collapsed;
        driver.SelectionChanged += (_, _) => Sync();
        Sync();
        async Task Apply()
        {
            var n = name.Text.Trim(); var d = driver.SelectedItem as string ?? "docker-container"; var p = platform.Text.Trim(); var ep = endpoint.Text.Trim();
            if (!BuilderName.IsMatch(n)) { error.Text = Loc.T("Use 1-63 letters, digits, dots, dashes or underscores, starting with a letter or digit."); return; }
            if (p.Length > 0 && !Regex.IsMatch(p, @"^[a-z0-9]+/[a-z0-9_]+(/[a-z0-9]+)?(,[a-z0-9]+/[a-z0-9_]+(/[a-z0-9]+)?)*$")) { error.Text = Loc.T("Platforms are comma-separated os/arch values, for example linux/amd64,linux/arm64."); return; }
            if (d == "remote" && !Regex.IsMatch(ep, @"^(tcp|unix|ssh|docker-container|kube-pod)://\S+$")) { error.Text = Loc.T("The remote driver needs an endpoint such as tcp://host:1234."); return; }
            var args = new List<string> { "buildx", "create", "--name", n, "--driver", d };
            if (p.Length > 0) args.AddRange(["--platform", p]);
            if (bootstrap.IsChecked == true) args.Add("--bootstrap");
            if (d == "remote") args.Add(ep);
            close!();
            var result = await Host.Execute("Create builder " + n, args, true, snapshot());
            done.TrySetResult(result?.Success == true);
        }
        var create = Ui.AsyncButton("Create", Icons.Add, ButtonKind.Filled, Apply);
        var actions = Ui.HStack(8, Ui.Button("Cancel", null, ButtonKind.Text, () => { close!(); done.TrySetResult(false); }), create);
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        var body = Ui.VStack(14,
            Ui.Title("Create builder"),
            Ui.Caption("Runs docker buildx create without --use, so the default builder stays as it is."),
            Ui.Field("Builder name", name, "A unique name for this builder.", "ci-multiarch"),
            Ui.Field("Driver", driver, "docker-container runs BuildKit in a container on the current engine and supports multi-platform builds and cache export. kubernetes runs BuildKit pods in the current kube context. remote connects to a BuildKit daemon you run yourself."),
            endpointField,
            Ui.Field("Platforms (optional)", platform, "Platforms this builder announces. Leave empty to let BuildKit detect them.", "linux/amd64,linux/arm64"),
            bootstrap, error, actions);
        close = Host.Overlay.Show(body, 560, () => done.TrySetResult(false));
        if (await done.Task) await RefreshAsync();
    }
}
