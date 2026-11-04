using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Swarm mode of the bound engine. Inactive engines get an honest card with Init and Join; managers get Services,
/// Stacks, Nodes, Secrets and Configs tabs; workers and other states get an explanation and Leave.
/// </summary>
public sealed class SwarmPage : PageBase
{
    public override string Key => "Swarm";
    private static readonly string[] Tabs = ["Services", "Stacks", "Nodes", "Secrets", "Configs"];
    private readonly TextBlock subtitle;
    private readonly ContentControl host = new();
    private readonly ContentControl tabHost = new();
    private readonly Dictionary<string, SwarmTabView> views = new(StringComparer.Ordinal);
    private readonly SwarmMembershipPanel membership;
    private readonly Border tabs;
    private string tab = "Services";
    private string tabSummary = "";
    private SwarmMembership? state;
    private readonly HashSet<string> loaded = new(StringComparer.Ordinal);

    public SwarmPage()
    {
        membership = new SwarmMembershipPanel(() => SnapshotContext, () => IsCurrent, RefreshAsync);
        SwarmTabView Make(string name) => name switch
        {
            "Services" => new SwarmServicesView(() => SnapshotContext, () => IsCurrent),
            "Stacks" => new SwarmStacksView(() => SnapshotContext, () => IsCurrent),
            "Nodes" => new SwarmNodesView(() => SnapshotContext, () => IsCurrent),
            "Secrets" => new SwarmSecretsView(() => SnapshotContext, () => IsCurrent),
            _ => new SwarmConfigsView(() => SnapshotContext, () => IsCurrent),
        };
        foreach (var name in Tabs)
        {
            var view = Make(name);
            var n = name;
            view.Reload = () => LoadTabAsync(n);
            view.Summary += s => { if (tab == n) { tabSummary = s; UpdateSubtitle(); } };
            views[name] = view;
        }
        tabs = Ui.Segmented(Tabs, tab, v => { tab = v; ShowTab(); });
        tabs.MaxWidth = 620;
        tabs.HorizontalAlignment = HorizontalAlignment.Left;
        var header = Ui.Header("Swarm", "Swarm services, stacks, nodes, secrets and configs", out subtitle,
            Ui.AsyncButton("Refresh", Icons.Refresh, ButtonKind.Tonal, RefreshAsync));
        Layout(header, host);
        host.Content = Ui.EmptyState(Icons.Hub, "Swarm", "Reading swarm state from the engine…");
    }

    private void UpdateSubtitle()
    {
        var ctx = "context " + (SnapshotContext ?? "current");
        subtitle.Text = Loc.Line(state == null ? ctx
            : !state.IsActive ? $"Swarm mode {state.LocalNodeState} · {ctx}"
            : !state.IsManager ? $"Worker node · {ctx}"
            : (tabSummary.Length > 0 ? tabSummary + " · " : "") + $"{state.Nodes} node{(state.Nodes == 1 ? "" : "s")} · {ctx}");
    }

    public override async Task RefreshAsync()
    {
        loaded.Clear();
        var r = await Host.Read(["info", "--format", "{{json .Swarm}}"], SnapshotContext);
        if (!r.Success)
        {
            state = null;
            host.Content = Ui.EmptyState(Icons.SyncProblem, "Engine unavailable", "docker info failed: " + (r.StandardError.Trim().Split('\n').FirstOrDefault() ?? $"exit code {r.ExitCode}"),
                Ui.Button("Open Diagnostics", Icons.Stethoscope, ButtonKind.Tonal, () => Host.Navigate("Diagnostics")));
            UpdateSubtitle();
            return;
        }
        try { state = SwarmMembership.Parse(r.StandardOutput.Trim()); }
        catch (JsonException e)
        {
            state = null;
            host.Content = Ui.EmptyState(Icons.SyncProblem, "Swarm state unreadable", Loc.T("The engine's swarm information could not be parsed:") + " " + e.Message);
            UpdateSubtitle();
            return;
        }
        if (state.IsInactive) host.Content = membership.InactiveView();
        else if (!state.IsManager) host.Content = membership.LimitedView(state);
        else
        {
            // The tab strip and tab host are reused across refreshes, so detach them from the previous layout first.
            if (tabs.Parent is Panel p1) p1.Children.Remove(tabs);
            if (tabHost.Parent is Panel p2) p2.Children.Remove(tabHost);
            host.Content = Ui.Rows(12, [0, 0, 1], membership.SummaryStrip(state), tabs, tabHost);
            tabSummary = "";
            tabHost.Content = views[tab];
            await LoadTabAsync(tab);
        }
        UpdateSubtitle();
    }

    private void ShowTab()
    {
        tabSummary = "";
        tabHost.Content = views[tab];
        UpdateSubtitle();
        if (IsVisible && state?.IsManager == true && !loaded.Contains(tab)) _ = SafeLoad(tab);
    }

    private async Task SafeLoad(string name)
    {
        try { await LoadTabAsync(name); }
        catch (Exception e) when (e is not OutOfMemoryException) { Host.Notify.Error($"{Loc.T(name)}: {e.Message}"); }
    }

    private async Task LoadTabAsync(string name)
    {
        loaded.Add(name);
        await views[name].LoadAsync();
    }

    public override IEnumerable<AppCommand> PageCommands() =>
    [
        new AppCommand { Id = "swarm.refresh", Title = "Refresh swarm", Group = "Swarm", Icon = Icons.Refresh, Keywords = "swarm services stacks nodes", Run = () => { Host.Navigate("Swarm"); _ = SafeRefresh(); } },
        new AppCommand { Id = "swarm.service.create", Title = "Create swarm service…", Group = "Swarm", Icon = Icons.Add, Keywords = "docker service create replicas", CanRun = () => state?.IsManager == true, Run = () => { Host.Navigate("Swarm"); SwarmServiceForm.Show(() => SnapshotContext, () => IsCurrent, () => LoadTabAsync("Services")); } },
    ];
}
