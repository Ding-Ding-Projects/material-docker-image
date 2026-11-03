using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Swarm membership surfaces: the honest "not part of a swarm" card with Init and Join, the summary strip for an active
/// engine, and Leave. Init changes the engine, so it is confirmed; Leave with --force needs the super confirmation.
/// </summary>
public sealed class SwarmMembershipPanel
{
    private static AppHost Host => AppHost.Current;
    private readonly Func<string?> context;
    private readonly Func<bool> isCurrent;
    private readonly Func<Task> reload;

    public SwarmMembershipPanel(Func<string?> context, Func<bool> isCurrent, Func<Task> reload) { this.context = context; this.isCurrent = isCurrent; this.reload = reload; }

    private bool Stale()
    {
        if (isCurrent()) return false;
        Host.Notify.Error(PageKit.StaleMessage(context()));
        return true;
    }

    /// <summary>The card shown when the engine is not in a swarm.</summary>
    public FrameworkElement InactiveView()
    {
        var advertise = Ui.Input("", "Advertise address", true);
        var init = Ui.AsyncButton("Init swarm…", Icons.Hub, ButtonKind.Filled, () => InitAsync(advertise.Text.Trim()));
        var initCard = Ui.Card(Ui.VStack(12,
            Ui.HStack(10, Ui.Icon(Icons.Hub, 22, true, "Primary"), Ui.Text("Start a new swarm", 16, FontWeights.SemiBold)),
            Ui.Caption("This engine becomes the first manager of a new single-node swarm. You can add nodes later with join tokens."),
            Ui.Field("Advertise address", advertise, "Optional. The address other nodes use to reach this manager. Needed when the engine has several network interfaces.", "192.168.1.20 or eth0:2377"),
            init), 20, "SurfaceContainer", 16);
        var token = new PasswordBox();
        var manager = Ui.Input("", "Manager address", true);
        var join = Ui.AsyncButton("Join swarm…", Icons.Login, ButtonKind.Tonal, () => JoinAsync(token, manager.Text.Trim()));
        var joinCard = Ui.Card(Ui.VStack(12,
            Ui.HStack(10, Ui.Icon(Icons.Link, 22, true, "Primary"), Ui.Text("Join an existing swarm", 16, FontWeights.SemiBold)),
            Ui.Caption("Run docker swarm join-token worker (or manager) on a manager to get the token and address."),
            Ui.Field("Join token", token, "The worker or manager token decides the role this engine gets. It is hidden on screen and not recorded in local history.", "SWMTKN-1-3pu6hszjas19x…-1awxwuwd3z9j1z3puu7rcgdbx"),
            Ui.Field("Manager address", manager, "A manager's address and swarm port.", "192.168.1.20:2377"),
            join), 20, "SurfaceContainer", 16);
        var intro = Ui.VStack(8,
            Ui.Icon(Icons.Hub, 44, false, "Outline"),
            Ui.Text("This engine is not part of a swarm", 20, FontWeights.SemiBold),
            Ui.Text("docker info reports swarm mode as inactive. Services, stacks, nodes, secrets and configs exist only in swarm mode. Starting or joining a swarm changes this engine's configuration; containers that are already running keep running.", 14, null, "OnSurfaceVariant", true, false));
        foreach (var t in intro.Children.OfType<TextBlock>()) t.TextAlignment = TextAlignment.Center;
        intro.MaxWidth = 640;
        intro.HorizontalAlignment = HorizontalAlignment.Center;
        var cards = PageParts.Responsive(initCard, joinCard, 900);
        cards.MaxWidth = 960;
        return Ui.Scroll(Ui.VStack(24, intro, cards).Margin(0, 24, 0, 24));
    }

    /// <summary>Stat strip for an active engine, with Leave.</summary>
    public FrameworkElement SummaryStrip(SwarmMembership m)
    {
        static Border Tile(string label, string value, string icon, bool mono = false)
        {
            var big = mono ? Ui.Mono(value, 16, "OnSurface") : Ui.Text(value, 24, FontWeights.SemiBold, "OnSurface", false, false);
            var b = Ui.Card(Ui.VStack(4, Ui.HStack(6, Ui.Icon(icon, 16, false, "OnSurfaceVariant"), Ui.Overline(label)), big), 14, "SurfaceContainer", 14);
            b.MinWidth = 150; b.Margin = new Thickness(0, 0, 12, 12);
            return b;
        }
        var wrap = new WrapPanel();
        wrap.Children.Add(Tile("Role", m.IsManager ? "Manager" : "Worker", Icons.Person));
        if (m.IsManager) { wrap.Children.Add(Tile("Nodes", m.Nodes.ToString(), Icons.Lan)); wrap.Children.Add(Tile("Managers", m.Managers.ToString(), Icons.Hub)); }
        wrap.Children.Add(Tile("Node address", m.NodeAddress.Length > 0 ? m.NodeAddress : "—", Icons.Router, true));
        if (m.ClusterId.Length > 0) wrap.Children.Add(Tile("Cluster", m.ClusterId.Length > 12 ? m.ClusterId[..12] : m.ClusterId, Icons.Fingerprint, true));
        var leave = Ui.AsyncButton("Leave swarm…", Icons.Logout, ButtonKind.Outlined, () => LeaveAsync(m));
        leave.VerticalAlignment = VerticalAlignment.Top;
        var row = new DockPanel();
        row.Children.Add(leave.Dock(Dock.Right));
        row.Children.Add(wrap);
        return row;
    }

    /// <summary>Card for workers and for pending, locked or error states, where swarm objects cannot be listed.</summary>
    public FrameworkElement LimitedView(SwarmMembership m)
    {
        var (title, message) = m.LocalNodeState switch
        {
            "active" => ("This engine is a worker node", "Workers run tasks but cannot list or manage services, stacks, nodes, secrets or configs. Switch this window to a manager's context to manage the swarm, or promote this node from a manager."),
            "pending" => ("Joining a swarm", "The engine is waiting to finish joining. Refresh in a moment."),
            "locked" => ("The swarm is locked", "Autolock is on and this manager restarted. Unlock it with docker swarm unlock and the unlock key on the engine host."),
            _ => ("Swarm state: " + m.LocalNodeState, m.Error.Length > 0 ? m.Error : "The engine reported a swarm state this page cannot manage."),
        };
        return Ui.VStack(16,
            SummaryStrip(m),
            Ui.EmptyState(Icons.Hub, title, message + (m.Error.Length > 0 && m.LocalNodeState != "error" ? " Engine message: " + m.Error : "")));
    }

    private async Task InitAsync(string advertise)
    {
        if (Stale()) return;
        if (SwarmRules.ValidateAddress(advertise, false) is { } problem) { Host.Notify.Error(problem); return; }
        if (!await Host.Overlay.ConfirmAsync("Start a swarm on this engine",
            $"docker swarm init turns engine context {context() ?? "current"} into the first manager of a new swarm. It creates the ingress network and a cluster certificate authority, and opens the swarm ports (2377, 7946, 4789). You can undo it with Leave swarm (--force on the last manager).", "Init swarm")) return;
        if (Stale()) return;
        var args = new List<string> { "swarm", "init" };
        if (advertise.Length > 0) args.AddRange(["--advertise-addr", advertise]);
        var result = await Host.Execute("Init swarm", args, boundContext: context());
        if (result?.Success == true && !Host.IsCapture) Host.History.Record("swarm", $"Initialized a swarm on context {context() ?? "current"}");
        await reload();
    }

    private async Task JoinAsync(PasswordBox tokenBox, string address)
    {
        if (Stale()) return;
        var token = tokenBox.Password.Trim();
        if (SwarmRules.ValidateJoinToken(token) is { } p1) { Host.Notify.Error(p1); return; }
        if (SwarmRules.ValidateAddress(address, true) is { } p2) { Host.Notify.Error(p2); return; }
        if (!await Host.Overlay.ConfirmAsync("Join swarm", $"Engine context {context() ?? "current"} joins the swarm managed at {address}. The token decides whether it joins as a worker or a manager.", "Join")) return;
        if (Stale()) return;
        // The label names the address only; the token is passed as an argument and never recorded.
        var result = await Host.Execute("Join swarm at " + address, ["swarm", "join", "--token", token, address], boundContext: context());
        if (result?.Success == true) tokenBox.Clear();
        await reload();
    }

    private async Task LeaveAsync(SwarmMembership m)
    {
        if (Stale()) return;
        var force = false;
        var forceSwitch = Ui.Switch("Force (--force)", false, v => force = v, "Required for a manager. On the last manager this destroys the swarm: every service, stack, secret and config is lost.");
        var choice = new TaskCompletionSource<bool>();
        Action? close = null;
        var body = Ui.VStack(14,
            Ui.Title("Leave swarm", 20),
            Ui.Text(m.IsManager
                ? $"This engine is a manager of a swarm with {m.Managers} manager{(m.Managers == 1 ? "" : "s")} and {m.Nodes} node{(m.Nodes == 1 ? "" : "s")}. A manager can only leave with --force; demote it first when other managers remain so the cluster keeps its quorum."
                : "This worker stops running swarm tasks and leaves the swarm. A manager still lists it as Down until it is removed there.", 14, null, "OnSurfaceVariant", true, false),
            forceSwitch,
            Ui.Columns(8, [1, 0, 0], new Border(), Ui.Button("Cancel", null, ButtonKind.Text, () => { choice.TrySetResult(false); close!(); }), Ui.Button("Continue", null, ButtonKind.Danger, () => { choice.TrySetResult(true); close!(); })));
        close = Host.Overlay.Show(body, 560, () => choice.TrySetResult(false));
        if (!await choice.Task) return;
        if (force)
        {
            var affected = new List<string> { $"swarm membership of context {context() ?? "current"} ({m.NodeAddress})" };
            if (m.IsManager && m.Managers <= 1) affected.Add("the whole swarm: every service, stack, secret, config and node registration");
            if (!await Host.Overlay.SuperConfirmAsync("Force leave swarm", "Leaving with --force cannot be undone. Rejoining needs a new join token from a remaining manager.", affected)) return;
        }
        else if (!await Host.Overlay.ConfirmAsync("Leave swarm", "Running swarm tasks on this engine are stopped.", "Leave", true)) return;
        if (Stale()) return;
        var args = new List<string> { "swarm", "leave" };
        if (force) args.Add("--force");
        var result = await Host.Execute(force ? "Leave swarm (force)" : "Leave swarm", args, boundContext: context());
        if (result?.Success == true && !Host.IsCapture) Host.History.Record("swarm", $"Left the swarm on context {context() ?? "current"}" + (force ? " with --force" : ""));
        await reload();
    }
}
