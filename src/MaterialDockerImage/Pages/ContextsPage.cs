using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Docker contexts as cards with live reachability, plus the docker CLI's plugins. Selecting a context changes only
/// this window; the CLI's current context is never changed.
/// </summary>
public sealed class ContextsPage : PageBase
{
    public override string Key => "Contexts";
    private readonly TextBlock subtitle;
    private readonly SearchFilter search = new("Search contexts", 300);
    private readonly System.Windows.Controls.Primitives.UniformGrid cards = new() { Columns = 3, Margin = new Thickness(-6, 0, 0, 0) }; // no negative right margin: it pushed the last card past the clip
    private readonly ContentControl cardsState = new();
    private readonly ContextPluginsPanel plugins = new();
    private readonly ContextActions actions;
    private readonly ContextCreatePanel createPanel;
    private readonly ColumnDefinition asideColumn = new() { Width = new GridLength(0) };
    private readonly ScrollViewer bodyScroll;
    private Button inspectButton = null!, exportButton = null!, removeButton = null!;
    private string? selectedName;
    private const double AsideWidth = 420;
    private List<ContextEntry> entries = new();
    private readonly List<ContextCardView> views = new();
    private readonly Dictionary<string, ContextProbe> probes = new(StringComparer.Ordinal);
    private CancellationTokenSource? probing;
    private string? cliCurrent;

    public ContextsPage()
    {
        actions = new ContextActions(RefreshAsync, () => cliCurrent);
        createPanel = new ContextCreatePanel(RefreshAsync) { Visibility = Visibility.Collapsed, Margin = new Thickness(16, 0, 0, 0) };
        createPanel.Closed += () => { asideColumn.Width = new GridLength(0); createPanel.Visibility = Visibility.Collapsed; Resize(); };
        var import = Ui.AsyncButton("Import", Icons.Download, ButtonKind.Outlined, () => actions.ImportAsync());
        exportButton = Ui.AsyncButton("Export", Icons.Upload, ButtonKind.Outlined, () => Selected() is { } e ? actions.ExportAsync(e) : Task.CompletedTask);
        inspectButton = Ui.AsyncButton("Inspect", Icons.DataObject, ButtonKind.Outlined, () => Selected() is { } e ? actions.InspectAsync(e) : Task.CompletedTask);
        removeButton = Ui.AsyncButton("Remove", Icons.Delete, ButtonKind.Outlined, () => Selected() is { } e ? actions.RemoveAsync(e) : Task.CompletedTask);
        removeButton.SetResourceReference(Control.ForegroundProperty, "Error");
        foreach (var b in new[] { exportButton, inspectButton, removeButton }) b.IsEnabled = false;
        var header = Ui.Header("Contexts", "Engine contexts and plugins", out subtitle,
            Ui.AsyncIconButton(Icons.Refresh, "Refresh", RefreshAsync),
            import, exportButton, inspectButton, removeButton,
            Ui.Button("Create", Icons.Add, ButtonKind.Filled, OpenCreate));
        search.Changed += RenderCards;
        var cardsHead = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        cardsHead.Children.Add(search.Dock(Dock.Left));
        cardsHead.Children.Add(Ui.Caption("Select a card to act on it with Export, Inspect and Remove. Use in this window switches only this window. The docker CLI's current context and other windows are unchanged.").Margin(16, 0, 0, 0));
        var body = Ui.VStack(20,
            Ui.VStack(0, cardsHead, cards, cardsState),
            plugins,
            ConfigNote());
        bodyScroll = Ui.Scroll(body);
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(asideColumn);
        content.Children.Add(bodyScroll.At(0, 0));
        content.Children.Add(createPanel.At(0, 1));
        Layout(header, content);
        // Design: three cards per row that stretch to the row; fewer columns when the window (or the room beside the create panel) is narrow.
        SizeChanged += (_, _) => Resize();
    }

    private void Resize()
    {
        var room = ActualWidth - (createPanel.Visibility == Visibility.Visible ? AsideWidth + 16 : 0);
        cards.Columns = room >= 860 ? 3 : room >= 560 ? 2 : 1;
    }

    /// <summary>Opens the right-hand Create context panel.</summary>
    private void OpenCreate()
    {
        createPanel.Visibility = Visibility.Visible;
        asideColumn.Width = new GridLength(AsideWidth + 16);
        Resize();
        createPanel.FocusName();
    }

    private ContextEntry? Selected() => entries.FirstOrDefault(e => e.Name == selectedName);

    private void Select(ContextEntry entry)
    {
        selectedName = entry.Name;
        RenderCards();
    }

    private static Border ConfigNote()
    {
        var configured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_CONFIG"));
        var folder = configured ? "%DOCKER_CONFIG%" : "%USERPROFILE%\\.docker";
        var hostVariable = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_HOST"));
        var contextVariable = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_CONTEXT"));
        var text = Ui.VStack(6,
            Ui.Text("Docker CLI configuration", 14, FontWeights.SemiBold),
            Ui.Caption("Contexts and plugins belong to the docker CLI on this computer. Context metadata is stored under the folder below (contexts\\meta), TLS material under contexts\\tls, and CLI plugins in cli-plugins."),
            Ui.Mono(folder + (configured ? "  (set by the DOCKER_CONFIG environment variable)" : ""), 12, "OnSurface", true),
            hostVariable ? Ui.Text("DOCKER_HOST is set in this environment. The docker CLI then ignores the current context for commands without --context; this window always passes its context explicitly.", 12, null, "OnWarningContainer", true, true) : null,
            contextVariable ? Ui.Text("DOCKER_CONTEXT is set in this environment and overrides the CLI's current context for commands without --context.", 12, null, "OnWarningContainer", true, true) : null);
        var row = new DockPanel();
        row.Children.Add(Ui.Icon(Icons.Info, 22, false, "OnSurfaceVariant").Dock(Dock.Left).Margin(0, 0, 12, 0));
        row.Children.Add(text);
        return Ui.Card(row, 16, "SurfaceContainer", 16);
    }

    public override async Task RefreshAsync()
    {
        probing?.Cancel();
        var r = await ContextActions.ReadLocal(["context", "ls", "--format", "{{json .}}"]);
        if (!r.Success)
        {
            entries = new();
            subtitle.Text = Loc.T("docker context ls failed:") + " " + (r.StandardError.Trim().Split('\n').FirstOrDefault() ?? $"exit code {r.ExitCode}");
            RenderCards();
            cardsState.Content = Ui.EmptyState(Icons.SyncProblem, "Contexts unavailable", "The docker CLI did not list contexts. Check that docker is installed and on PATH, then refresh.", Ui.Button("Open Diagnostics", Icons.Stethoscope, ButtonKind.Tonal, () => Host.Navigate("Diagnostics")));
            await plugins.LoadAsync();
            return;
        }
        try
        {
            entries = Core.DockerJson.ParseRows(r.StandardOutput).Select(x => new ContextEntry(
                CliValues.Str(x, "Name"), CliValues.Str(x, "Description"), CliValues.Str(x, "DockerEndpoint"),
                CliValues.Bool(x, "Current"), CliValues.Str(x, "Error"))).Where(e => e.Name.Length > 0).ToList();
        }
        catch (JsonException e) { entries = new(); subtitle.Text = Loc.T("The context list could not be parsed:") + " " + e.Message; }
        cliCurrent = entries.FirstOrDefault(e => e.CliCurrent)?.Name;
        probes.Clear();
        var window = actions.WindowContext;
        subtitle.Text = Loc.Line($"{entries.Count} context{(entries.Count == 1 ? "" : "s")} · this window uses {window ?? "the CLI's current context"}" + (cliCurrent != null ? $" · CLI current: {cliCurrent}" : ""));
        RenderCards();
        _ = ProbeAllAsync();
        await plugins.LoadAsync();
    }

    private void RenderCards()
    {
        cards.Children.Clear(); views.Clear();
        var window = actions.WindowContext;
        if (selectedName == null || entries.All(e => e.Name != selectedName)) selectedName = entries.FirstOrDefault(e => e.Name == window)?.Name ?? entries.FirstOrDefault()?.Name;
        foreach (var b in new[] { exportButton, inspectButton, removeButton }) b.IsEnabled = selectedName != null;
        var shown = entries.Where(e => search.Matches(e.SearchText)).ToList();
        foreach (var e in shown)
        {
            var view = new ContextCardView(e, e.Name == window, actions, e.Name == selectedName, Select) { Margin = new Thickness(6, 0, 6, 12) };
            if (probes.TryGetValue(e.Name, out var p)) view.SetProbe(p);
            views.Add(view);
            cards.Children.Add(view);
        }
        cardsState.Content = entries.Count == 0
            ? Ui.EmptyState(Icons.DeviceHub, "No contexts", "The docker CLI listed no contexts. Create one to reach another engine.", Ui.Button("Create context", Icons.Add, ButtonKind.Filled, OpenCreate))
            : shown.Count == 0 ? Ui.EmptyState(Icons.FilterList, "No matches", search.Status.Length > 0 ? search.Status : "No context matches the search.") : null;
    }

    /// <summary>Probes every context in parallel with a time limit; results fill the cards as they arrive.</summary>
    private async Task ProbeAllAsync()
    {
        probing = new CancellationTokenSource();
        var token = probing.Token;
        var tasks = entries.Where(e => e.Error.Length == 0).Select(async e =>
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
            limit.CancelAfter(TimeSpan.FromSeconds(10));
            Core.CommandResult result;
            try { result = await Host.Read(["version", "--format", "{{json .Server}}"], e.Name, limit.Token); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException) { result = new Core.CommandResult(-1, "", ex.Message); }
            if (token.IsCancellationRequested) return;
            var probe = ContextCardView.ParseProbe(result);
            probes[e.Name] = probe;
            views.FirstOrDefault(v => v.Entry.Name == e.Name)?.SetProbe(probe);
        });
        foreach (var e in entries.Where(e => e.Error.Length > 0))
        {
            var probe = new ContextProbe(false, "", "", "", e.Error);
            probes[e.Name] = probe;
            views.FirstOrDefault(v => v.Entry.Name == e.Name)?.SetProbe(probe);
        }
        try { await Task.WhenAll(tasks); }
        catch (OperationCanceledException) { }
    }

    private static async Task GuardAsync(Func<Task> action)
    {
        try { await action(); } catch (Exception e) when (e is not OutOfMemoryException) { Host.Notify.Error(e.Message); }
    }

    public override IEnumerable<AppCommand> PageCommands() =>
    [
        new AppCommand { Id = "contexts.create", Title = "Create context…", Group = "Contexts", Icon = Icons.Add, Keywords = "docker context create endpoint tls ssh", Run = () => { Host.Navigate("Contexts"); OpenCreate(); } },
        new AppCommand { Id = "contexts.import", Title = "Import context…", Group = "Contexts", Icon = Icons.Download, Keywords = "docker context import", Run = () => { Host.Navigate("Contexts"); _ = GuardAsync(actions.ImportAsync); } },
        new AppCommand { Id = "contexts.refresh", Title = "Refresh contexts and plugins", Group = "Contexts", Icon = Icons.Refresh, Keywords = "context ls plugins reachability", Run = () => { Host.Navigate("Contexts"); _ = SafeRefresh(); } },
    ];
}
