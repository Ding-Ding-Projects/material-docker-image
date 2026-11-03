using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>One row of <c>docker stack ls</c>.</summary>
public sealed record SwarmStackRow(string Name, string Services, string Orchestrator);

/// <summary>Stacks: list, the services of the focused stack, deploy from a Compose file, and remove.</summary>
public sealed class SwarmStacksView : SwarmTabView
{
    private readonly DataTable<SwarmStackRow> table = new("Swarm stacks", "Search stacks");
    private readonly DataTable<SwarmServiceRow> services = new("Stack services", "Search stack services");
    private readonly TextBlock servicesTitle = Ui.Text("Select a stack", 15, FontWeights.SemiBold);
    private int version;

    public SwarmStacksView(Func<string?> context, Func<bool> isCurrent) : base(context, isCurrent)
    {
        table.Key = s => s.Name;
        table.EmptyTitle = "No stacks";
        table.EmptyMessage = "No stack is deployed on this swarm. Deploy one from a Compose file.";
        table.Column("Name", s => s.Name, 220, true)
            .Column("Services", s => s.Services, 90)
            .Column("Orchestrator", s => s.Orchestrator, 120)
            .RowAction("Show services", Icons.ViewList, s => ShowServicesAsync(s), "Enter")
            .RowAction("Copy name", Icons.ContentCopy, s => { PageKit.Copy(s.Name, "the stack name"); return Task.CompletedTask; })
            .RowAction("Remove…", Icons.Delete, RemoveAsync, "Del", true);
        table.FocusedChanged += s => _ = Guard(() => ShowServicesAsync(s));
        table.Activated += s => _ = Guard(() => ShowServicesAsync(s));
        services.EmptyTitle = "No services";
        services.EmptyMessage = "Select a stack to see its services.";
        services.Column("Name", s => s.Name, 220, true)
            .ChipColumn("Replicas", s => s.Replicas, s => ToneOf(s.Health), 110)
            .Column("Mode", s => s.Mode, 100)
            .Column("Image", s => s.Image, 240, true)
            .Column("Ports", s => s.Ports, 160, true);
        var deploy = Ui.AsyncButton("Deploy stack…", Icons.RocketLaunch, ButtonKind.Filled, DeployAsync);
        var right = Ui.Card(Ui.Rows(10, [0, 1], servicesTitle, services), 16, "SurfaceLow", 16);
        LayoutWith(Ui.Wrap(8, deploy), Ui.SplitInspector(table, right, 1150, 520));
    }

    public override async Task LoadAsync()
    {
        var rows = await Host.ReadRows(["stack", "ls", "--format", "{{json .}}"], Context());
        var items = rows.Select(r => new SwarmStackRow(CliValues.Str(r, "Name"), CliValues.Str(r, "Services"), CliValues.Str(r, "Orchestrator"))).ToList();
        table.SetItems(items);
        SetSummary($"{items.Count} stack{(items.Count == 1 ? "" : "s")}");
        await ShowServicesAsync(table.Focused);
    }

    private async Task<List<SwarmServiceRow>> StackServices(string stack)
    {
        var rows = await Host.ReadRows(["stack", "services", "--format", "{{json .}}", stack], Context());
        return rows.Select(r => new SwarmServiceRow(CliValues.Str(r, "ID"), CliValues.Str(r, "Name"), CliValues.Str(r, "Mode"), CliValues.Str(r, "Replicas"), CliValues.Str(r, "Image"), CliValues.Str(r, "Ports"))).ToList();
    }

    private async Task ShowServicesAsync(SwarmStackRow? stack)
    {
        var v = ++version;
        if (stack == null) { servicesTitle.Text = Loc.T("Select a stack"); services.SetItems([]); return; }
        servicesTitle.Text = Loc.T("Services of") + " " + stack.Name;
        try
        {
            var list = await StackServices(stack.Name);
            if (v != version) return;
            services.EmptyMessage = "The stack reported no services.";
            services.SetItems(list);
        }
        catch (InvalidOperationException e)
        {
            if (v != version) return;
            services.EmptyMessage = "docker stack services failed: " + e.Message;
            services.SetItems([]);
        }
    }

    private async Task DeployAsync()
    {
        if (Stale()) return;
        var file = Ui.Input("", "Compose file", true);
        var browse = Ui.IconButton(Icons.FolderOpen, "Choose a Compose file", () => { if (PageKit.PickOpenPath("Choose a Compose file", "Compose files|*.yml;*.yaml|All files|*.*") is { } p) file.Text = p; });
        var name = Ui.Input("", "Stack name", true);
        file.TextChanged += (_, _) => { if (name.Text.Length == 0 && File.Exists(file.Text)) name.Text = PageKit.SafeFileName(Path.GetFileName(Path.GetDirectoryName(file.Text)) ?? "").ToLowerInvariant(); };
        var prune = false; var registryAuth = false;
        var body = Ui.VStack(14,
            Ui.Field("Compose file", Ui.Columns(8, [1, 0], file, browse), "The docker CLI reads this file on this computer and sends the resulting services to the swarm. build sections are ignored; images must already be in a registry.", "C:\\src\\shop\\compose.yaml"),
            Ui.Field("Stack name", name, "Prefix for every service, network and volume of the stack. Deploying again with the same name updates the stack.", "shop"),
            Ui.Switch("Prune services no longer in the file", false, v => prune = v, "--prune removes services of this stack that the file no longer defines."),
            Ui.Switch("Send registry authentication", false, v => registryAuth = v, "--with-registry-auth sends this computer's registry credentials to the swarm so nodes can pull private images."));
        PageKit.Dialog("Deploy stack", "docker stack deploy", body, "Deploy", async () =>
        {
            var path = file.Text.Trim();
            if (!File.Exists(path)) return "Choose an existing Compose file.";
            if (SwarmRules.ValidateName(name.Text.Trim(), "stack") is { } problem) return problem;
            if (!await Host.Overlay.ConfirmAsync("Deploy stack " + name.Text.Trim(), $"Creates or updates the services of stack {name.Text.Trim()} on context {Context() ?? "current"} from {path}." + (prune ? " Services of the stack that the file no longer defines are removed." : ""), "Deploy")) return "Deployment canceled.";
            if (Stale()) return PageKit.StaleMessage(Context());
            var args = new List<string> { "stack", "deploy", "--compose-file", Path.GetFullPath(path) };
            if (prune) args.Add("--prune");
            if (registryAuth) args.Add("--with-registry-auth");
            args.Add(name.Text.Trim());
            var result = await Run("Deploy stack " + name.Text.Trim(), args);
            if (result?.Success != true) return PageKit.Failure(result);
            await AfterChange();
            return null;
        }, width: 620);
    }

    private async Task RemoveAsync(SwarmStackRow stack)
    {
        if (Stale()) return;
        List<string> affected;
        try { affected = (await StackServices(stack.Name)).Select(s => $"service {s.Name} ({s.Replicas}, {s.Image})").ToList(); }
        catch (InvalidOperationException e) { affected = ["(services could not be listed: " + e.Message + ")"]; }
        affected.Add($"networks of stack {stack.Name}, plus its secrets and configs");
        if (!await Host.Overlay.SuperConfirmAsync("Remove stack " + stack.Name, "Every service of the stack and all their tasks are stopped and deleted, with the stack's networks, secrets and configs. Volumes are kept.", affected)) return;
        if (Stale()) return;
        await Run("Remove stack " + stack.Name, ["stack", "rm", stack.Name]);
        await AfterChange();
    }
}
