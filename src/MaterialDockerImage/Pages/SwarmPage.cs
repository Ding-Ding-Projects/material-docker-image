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
    private readonly Dictionary<string, RadioButton> tabRadios = new(StringComparer.Ordinal);

    private static UIElement TabContent(string name, string? count) =>
        count == null ? new TextBlock { Text = Loc.T(name) } : Ui.HStack(8, new TextBlock { Text = Loc.T(name) }, Ui.Text(count, 12, null, "OnSurfaceVariant", false, false));
    private readonly Button deployStack;
    private readonly Button createService;
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
            view.Summary += s =>
            {
                // Summaries start with the item count ("4 services · ..."), which the tab label shows beside its name.
                var digits = new string(s.TakeWhile(char.IsDigit).ToArray());
                if (digits.Length > 0 && tabRadios.TryGetValue(n, out var radio)) radio.Content = TabContent(n, digits);
                if (tab == n) { tabSummary = s; UpdateSubtitle(); }
            };
            views[name] = view;
        }
        // Underline tabs as in the design: a 1 px divider under the strip and a 3 px primary line under the selected tab.
        var strip = new WrapPanel();
        var group = "swarm" + Guid.NewGuid().ToString("N");
        foreach (var name in Tabs)
        {
            var n = name;
            var radio = new RadioButton { Style = UnderlineTabs.Style, GroupName = group, Content = TabContent(n, null), IsChecked = n == tab, Padding = new Thickness(16, 0, 16, 0) };
            System.Windows.Automation.AutomationProperties.SetName(radio, Loc.T(n));
            radio.Checked += (_, _) => { tab = n; ShowTab(); };
            tabRadios[n] = radio;
            strip.Children.Add(radio);
        }
        System.Windows.Automation.AutomationProperties.SetName(strip, Loc.T("Swarm sections"));
        tabs = UnderlineTabs.Strip(strip);
        deployStack = Ui.AsyncButton("Deploy stack", Icons.RocketLaunch, ButtonKind.Outlined, () => ((SwarmStacksView)views["Stacks"]).DeployAsync());
        createService = Ui.Button("Create service", Icons.Add, ButtonKind.Filled, () => SwarmServiceForm.Show(() => SnapshotContext, () => IsCurrent, () => LoadTabAsync("Services")));
        deployStack.IsEnabled = createService.IsEnabled = false;
        var header = Ui.Header("Swarm", "Swarm services, stacks, nodes, secrets and configs", out subtitle,
            Ui.AsyncIconButton(Icons.Refresh, "Refresh", RefreshAsync), deployStack, createService);
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
        deployStack.IsEnabled = createService.IsEnabled = false;
        var r = await Host.Read(["info", "--format", "{{json .Swarm}}"], SnapshotContext);
        swarmRead = true;
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
            deployStack.IsEnabled = createService.IsEnabled = true;
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

    // True once docker info has been read for swarm state (successfully or not); the palette shows nothing before that.
    private bool swarmRead;
    internal override string? PaletteSummary() => !swarmRead ? null : PaletteLiveLines.Swarm(state?.IsActive, state?.IsManager == true, state?.Nodes ?? 0);

    public override IEnumerable<AppCommand> PageCommands() =>
    [
        new AppCommand { Id = "swarm.refresh", Title = "Refresh swarm", Group = "Swarm", Icon = Icons.Refresh, Keywords = "swarm services stacks nodes", Run = () => { Host.Navigate("Swarm"); _ = SafeRefresh(); }, Status = PaletteSummary },
        new AppCommand { Id = "swarm.service.create", Title = "Create swarm service…", Group = "Swarm", Icon = Icons.Add, Keywords = "docker service create replicas", CanRun = () => state?.IsManager == true, Run = () => { Host.Navigate("Swarm"); SwarmServiceForm.Show(() => SnapshotContext, () => IsCurrent, () => LoadTabAsync("Services")); } },
    ];
}
