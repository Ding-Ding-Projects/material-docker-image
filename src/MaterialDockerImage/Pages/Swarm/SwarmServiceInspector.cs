using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Inspector for the focused service: identity, an inline replicas stepper, a searchable task list from
/// <c>docker service ps</c> and an inline Update image field with Roll back and Update at the bottom.
/// </summary>
public sealed class SwarmServiceInspector : Border
{
    private readonly SwarmServicesView owner;
    private readonly TextBlock title = Ui.Mono("", 20, "OnSurface", true);
    private readonly TextBlock image = Ui.Mono("", 12, "OnSurfaceVariant", true);
    private readonly WrapPanel chips = new();
    private readonly SearchFilter search = new("Search tasks", 260) { Width = double.NaN };
    private readonly StackPanel tasks = new();
    private readonly TextBlock taskSummary = Ui.Caption("");
    private readonly TextBlock modeText = Ui.Caption("");
    private readonly TextBox replicas = Ui.Input("1");
    private readonly Button minus;
    private readonly Button plus;
    private readonly Button scale;
    private readonly TextBox updateImage = Ui.Input("", "Image reference", true);
    private readonly TextBlock updateHint = Ui.Caption("Starts a rolling update using the service's update policy. Roll back returns to the previous specification.");
    private readonly FrameworkElement content;
    private readonly FrameworkElement empty;
    private IReadOnlyList<SwarmTaskRow> loaded = [];
    private SwarmServiceRow? service;
    private int version;
    private const string MinusGlyph = ""; // Material Symbols "remove"

    public SwarmServiceInspector(SwarmServicesView owner)
    {
        this.owner = owner;
        CornerRadius = new CornerRadius(16);
        Padding = new Thickness(20);
        SetResourceReference(BackgroundProperty, "SurfaceLow");
        title.FontWeight = FontWeights.SemiBold;
        Button Act(string label, string icon, ButtonKind kind, Func<SwarmServiceRow, Task> run)
        {
            var b = Ui.AsyncButton(label, icon, kind, async () => { if (service is { } s) await run(s); });
            b.Margin = new Thickness(0, 0, 8, 0);
            return b;
        }
        var utility = Ui.Wrap(0, Act("Logs", Icons.Terminal, ButtonKind.Text, owner.LogsAsync), Act("Remove", Icons.Delete, ButtonKind.Danger, owner.RemoveAsync));

        // Replicas tile: label and mode, then − field + and Scale in one row.
        minus = Ui.IconButton(MinusGlyph, "Fewer replicas", () => Step(-1));
        plus = Ui.IconButton(Icons.Add, "More replicas", () => Step(1));
        replicas.Width = 56; replicas.TextAlignment = TextAlignment.Center; replicas.VerticalAlignment = VerticalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetName(replicas, Loc.T("Replicas"));
        scale = Ui.AsyncButton("Scale", null, ButtonKind.Tonal, ScaleAsync);
        var stepper = Ui.HStack(6, minus, replicas, plus, scale);
        stepper.VerticalAlignment = VerticalAlignment.Center;
        var tile = Ui.Card(Ui.Columns(12, [1, 0], Ui.VStack(2, Ui.Text("Replicas", 14, FontWeights.SemiBold), modeText), stepper), 12, "SurfaceContainer", 12);

        search.Changed += Render;
        var refresh = Ui.AsyncIconButton(Icons.Refresh, "Refresh tasks", async () => await ShowAsync(service));
        var taskHead = new DockPanel();
        taskHead.Children.Add(refresh.Dock(Dock.Right));
        taskHead.Children.Add(Ui.VStack(2, Ui.Overline("Tasks"), taskSummary));
        var top = Ui.Scroll(Ui.VStack(12, Ui.VStack(4, Ui.Overline("Service"), title, image), chips, utility, tile, taskHead, search, tasks));

        var rollback = Ui.AsyncButton("Roll back", Icons.Undo, ButtonKind.Outlined, async () => { if (service is { } s) await owner.RollbackAsync(s); });
        var update = Ui.AsyncButton("Update", Icons.CloudSync, ButtonKind.Filled, async () => { if (service is { } s) await owner.UpdateImageToAsync(s, updateImage.Text.Trim()); });
        rollback.Height = update.Height = 40;
        var bottom = Ui.VStack(8, Ui.Overline("Update image"), updateImage, updateHint, Ui.Columns(8, [1, 1], rollback, update).Margin(0, 4, 0, 0));
        bottom.Margin = new Thickness(0, 12, 0, 0);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(top);
        layout.Children.Add(bottom.At(1));
        content = layout;
        empty = Ui.EmptyState(Icons.Hub, "Select a service", "Its tasks, nodes and states appear here with logs, scale, update and roll back.");
        Child = empty;
    }

    private void Step(int delta)
    {
        var n = int.TryParse(replicas.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : 0;
        replicas.Text = Math.Clamp(n + delta, 0, 10000).ToString(CultureInfo.InvariantCulture);
    }

    private async Task ScaleAsync()
    {
        if (service is not { } s) return;
        if (!int.TryParse(replicas.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n is < 0 or > 10000)
        {
            AppHost.Current.Notify.Error("Enter a whole number from 0 to 10000.");
            return;
        }
        await owner.ScaleToAsync(s, n);
    }

    public async Task ShowAsync(SwarmServiceRow? selected)
    {
        var v = ++version;
        var changed = selected?.Id != service?.Id;
        service = selected;
        if (selected == null) { Child = empty; return; }
        Child = content;
        title.Text = selected.Name;
        image.Text = selected.Image;
        modeText.Text = Loc.T(selected.Replicated ? "Replicated mode" : "Global mode · one task per node");
        foreach (var c in new FrameworkElement[] { replicas, minus, plus, scale }) c.IsEnabled = selected.Replicated;
        if (changed || !replicas.IsKeyboardFocusWithin)
        {
            replicas.Text = (SwarmRules.ParseReplicas(selected.Replicas)?.Desired ?? 1).ToString(CultureInfo.InvariantCulture);
        }
        if (changed) updateImage.Text = selected.Image.Split('@')[0];
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
            taskSummary.Text = Loc.Line($"{rows.Count} task{(rows.Count == 1 ? "" : "s")} including history · {running} running" + (failed > 0 ? $" · {failed} failed or rejected" : ""));
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

    /// <summary>A task row as in the design: mono name with its error line, node, and a state chip.</summary>
    private static UIElement Row(SwarmTaskRow t)
    {
        var health = SwarmRules.TaskHealth(t.CurrentState);
        var name = Ui.Mono(t.Name.Length > 0 ? t.Name : t.Id, 12.5, "OnSurface", true);
        name.FontWeight = FontWeights.SemiBold;
        var detail = Ui.Text(t.Error.Length > 0 ? t.Error : "No error", 12, null, t.Error.Length > 0 ? "Error" : "OnSurfaceVariant", true, t.Error.Length == 0);
        var node = Ui.Text(t.Node.Length > 0 ? t.Node : Loc.T("Not assigned to a node"), 12.5, null, "OnSurfaceVariant", false, false);
        node.VerticalAlignment = VerticalAlignment.Center;
        var state = Ui.Badge(t.CurrentState.Split(' ')[0], SwarmTabView.ToneOf(health), false);
        state.ToolTip = t.CurrentState + " · " + Loc.T("Desired") + " " + t.DesiredState.ToLowerInvariant();
        var row = Ui.Columns(10, [1, 0, 0], Ui.VStack(2, name, detail), node, state);
        var host = new Border { Child = row, Padding = new Thickness(0, 8, 0, 8), BorderThickness = new Thickness(0, 0, 0, 1) };
        host.SetResourceReference(Border.BorderBrushProperty, "OutlineVariant");
        return host;
    }
}
