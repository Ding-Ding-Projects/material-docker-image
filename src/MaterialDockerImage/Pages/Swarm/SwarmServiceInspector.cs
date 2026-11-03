using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Inspector for the focused service: identity, actions and a searchable task timeline from <c>docker service ps</c>.</summary>
public sealed class SwarmServiceInspector : Border
{
    private readonly SwarmServicesView owner;
    private readonly TextBlock title = Ui.Mono("", 18, "OnSurface", true);
    private readonly TextBlock image = Ui.Mono("", 12, "OnSurfaceVariant", true);
    private readonly WrapPanel chips = new();
    private readonly SearchFilter search = new("Search tasks", 260) { Width = double.NaN };
    private readonly StackPanel tasks = new();
    private readonly TextBlock taskSummary = Ui.Caption("");
    private readonly ScrollViewer content;
    private readonly FrameworkElement empty;
    private IReadOnlyList<SwarmTaskRow> loaded = [];
    private SwarmServiceRow? service;
    private int version;

    public SwarmServiceInspector(SwarmServicesView owner)
    {
        this.owner = owner;
        CornerRadius = new CornerRadius(16);
        Padding = new Thickness(18);
        SetResourceReference(BackgroundProperty, "SurfaceLow");
        title.FontWeight = FontWeights.SemiBold;
        Button Act(string label, string icon, ButtonKind kind, Func<SwarmServiceRow, Task> run, bool danger = false)
        {
            var b = Ui.AsyncButton(label, icon, danger ? ButtonKind.Danger : kind, async () => { if (service is { } s) await run(s); });
            b.Margin = new Thickness(0, 0, 8, 8);
            return b;
        }
        var actions = new WrapPanel();
        actions.Children.Add(Act("Logs", Icons.Terminal, ButtonKind.Tonal, owner.LogsAsync));
        actions.Children.Add(Act("Scale", Icons.UnfoldMore, ButtonKind.Tonal, owner.ScaleAsync));
        actions.Children.Add(Act("Update image", Icons.CloudSync, ButtonKind.Outlined, owner.UpdateImageAsync));
        actions.Children.Add(Act("Roll back", Icons.Undo, ButtonKind.Outlined, owner.RollbackAsync));
        actions.Children.Add(Act("Remove", Icons.Delete, ButtonKind.Text, owner.RemoveAsync, true));
        search.Changed += Render;
        var refresh = Ui.AsyncIconButton(Icons.Refresh, "Refresh tasks", async () => await ShowAsync(service));
        var taskHead = new DockPanel();
        taskHead.Children.Add(refresh.Dock(Dock.Right));
        taskHead.Children.Add(Ui.VStack(2, Ui.Text("Tasks", 15, FontWeights.SemiBold), taskSummary));
        content = Ui.Scroll(Ui.VStack(12, Ui.VStack(4, title, image), chips, actions, Ui.Divider(), taskHead, search, tasks));
        empty = Ui.EmptyState(Icons.Hub, "Select a service", "Its tasks, nodes and states appear here with logs, scale, update and roll back.");
        Child = empty;
    }

    public async Task ShowAsync(SwarmServiceRow? selected)
    {
        var v = ++version;
        service = selected;
        if (selected == null) { Child = empty; return; }
        Child = content;
        title.Text = selected.Name;
        image.Text = selected.Image;
        chips.Children.Clear();
        foreach (var chip in new[] { Ui.Badge(selected.Mode, selected.Replicated ? Tone.Neutral : Tone.Tertiary), Ui.Badge(selected.Replicas, SwarmTabView.ToneOf(selected.Health)), selected.Ports.Length > 0 ? Ui.Badge(selected.Ports, Tone.Primary) : null })
            if (chip != null) { chip.Margin = new Thickness(0, 0, 6, 6); chips.Children.Add(chip); }
        taskSummary.Text = Loc.T("Loading…");
        try
        {
            var rows = await owner.TasksAsync(selected);
            if (v != version) return;
            loaded = rows;
            var running = rows.Count(r => r.CurrentState.StartsWith("Running", StringComparison.OrdinalIgnoreCase));
            var failed = rows.Count(r => SwarmRules.TaskHealth(r.CurrentState) == SwarmHealth.Down);
            taskSummary.Text = $"{rows.Count} task{(rows.Count == 1 ? "" : "s")} including history · {running} running" + (failed > 0 ? $" · {failed} failed or rejected" : "");
        }
        catch (InvalidOperationException e)
        {
            if (v != version) return;
            loaded = [];
            taskSummary.Text = "docker service ps failed: " + e.Message;
        }
        Render();
    }

    private void Render()
    {
        tasks.Children.Clear();
        var shown = loaded.Where(t => search.Matches(t.SearchText)).ToList();
        if (loaded.Count == 0) { tasks.Children.Add(Ui.Caption("No tasks reported for this service.")); return; }
        if (shown.Count == 0) { tasks.Children.Add(Ui.Caption(search.Status.Length > 0 ? search.Status : "No tasks match the search.")); return; }
        foreach (var t in shown) tasks.Children.Add(Row(t));
    }

    /// <summary>A timeline row: state icon on a rail, task name, node, desired and current state chips and any error.</summary>
    private static UIElement Row(SwarmTaskRow t)
    {
        var health = SwarmRules.TaskHealth(t.CurrentState);
        var (icon, brush) = health switch
        {
            SwarmHealth.Healthy => (Icons.CheckCircle, "Success"),
            SwarmHealth.Partial => (Icons.Pending, "Warning"),
            SwarmHealth.Down => (Icons.Error, "Error"),
            _ => (Icons.Stop, "OnSurfaceVariant"),
        };
        var rail = new Grid { Width = 28 };
        var line = new Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Center };
        line.SetResourceReference(Border.BackgroundProperty, "OutlineVariant");
        var dot = Ui.Icon(icon, 20, true, brush);
        dot.VerticalAlignment = VerticalAlignment.Top;
        var dotHost = new Border { Child = dot, VerticalAlignment = VerticalAlignment.Top, Padding = new Thickness(0, 2, 0, 2) };
        dotHost.SetResourceReference(Border.BackgroundProperty, "SurfaceLow");
        rail.Children.Add(line); rail.Children.Add(dotHost);
        var desiredTone = string.Equals(t.DesiredState, "Running", StringComparison.OrdinalIgnoreCase) ? Tone.Primary : Tone.Neutral;
        var body = Ui.VStack(4,
            Ui.Mono(t.Name.Length > 0 ? t.Name : t.Id, 13, "OnSurface", true),
            Ui.Caption(t.Node.Length > 0 ? Loc.T("Node") + ": " + t.Node : Loc.T("Not assigned to a node")),
            Ui.Wrap(6, Ui.Badge(Loc.T("Desired") + " " + t.DesiredState.ToLowerInvariant(), desiredTone), Ui.Badge(t.CurrentState, SwarmTabView.ToneOf(health))),
            t.Error.Length > 0 ? Ui.Text(t.Error, 12, null, "Error", true, false) : null);
        body.Margin = new Thickness(10, 0, 0, 14);
        var row = new DockPanel();
        row.Children.Add(rail.Dock(Dock.Left));
        row.Children.Add(body);
        return row;
    }
}
