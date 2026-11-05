using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Live CPU and memory charts sampled from <c>docker container stats --no-stream</c>. Polling runs only while the
/// page is visible and Live is on; every drawn point is a real sample from the engine context the page is bound to.
/// </summary>
public sealed class MonitorPage : PageBase
{
    public override string Key => "Monitor";
    private static readonly int[] Intervals = [1, 2, 5, 10];

    private readonly StatsHistory history = new();
    private readonly DispatcherTimer timer = new();
    private readonly LiveChart cpuChart = new() { SeriesName = "CPU", SeriesBrushKey = "Primary", MinimumCeiling = 10, MinHeight = 220 };
    private readonly LiveChart memChart = new() { SeriesName = "Memory", SeriesBrushKey = "Tertiary", MinimumCeiling = 1024 * 1024, MinHeight = 160 };
    private readonly TextBlock subtitle;
    private readonly TextBlock cpuTitle = Ui.Caption("CPU");
    private readonly TextBlock cpuValue = Ui.Text("", 40, FontWeights.SemiBold, "OnSurface", false, false);
    private readonly TextBlock cpuDetail = Ui.Text("", 12, null, "OnSurfaceVariant");
    private readonly Border cpuDelta = new() { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 3, 10, 3), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed };
    private readonly TextBlock cpuDeltaText = Ui.Text("", 12, null, "OnWarningContainer", false, false);
    private readonly TextBlock memTitle = Ui.Caption("Memory");
    private readonly TextBlock memValue = Ui.Text("", 24, FontWeights.SemiBold, "OnSurface", false, false);
    private readonly TextBlock memDetail = Ui.Text("", 12, null, "OnSurfaceVariant", false, false);
    private readonly TextBlock stateText = Ui.Caption("");
    private readonly SearchFilter search = new("Search containers", 260);
    private readonly System.Windows.Controls.Primitives.UniformGrid cards = new() { Columns = 2, Margin = new Thickness(-6) };
    private readonly ContentControl cardsEmpty = new();
    private readonly Dictionary<string, ContainerStatCard> cardByName = new(StringComparer.Ordinal);
    private bool live = true;
    private bool reading;
    private string? focused;
    private CancellationTokenSource? readCancel;
    private readonly Grid main;

    public MonitorPage()
    {
        timer.Tick += async (_, _) => await Tick();
        ApplyInterval();
        var intervalOptions = Intervals.Select(i => $"{i} s").ToList();
        var interval = Ui.Combo(intervalOptions, $"{CurrentInterval} s");
        interval.MinWidth = 96;
        interval.SelectionChanged += (_, _) =>
        {
            if (interval.SelectedItem is not string v) return;
            var seconds = int.Parse(v.Split(' ')[0], CultureInfo.InvariantCulture);
            if (seconds == Host.Settings.MonitorIntervalSeconds) return;
            Host.Settings.MonitorIntervalSeconds = seconds;
            Host.SaveSettings($"Monitor sampling interval set to {seconds} s");
            ApplyInterval();
        };
        interval.ToolTip = Loc.T("How often the page asks the engine for one stats reading of every running container. Shorter intervals give smoother charts and cost more engine work. Saved with your settings.");
        var liveToggle = Ui.Segmented(["Paused", "Live"], "Live", v => SetLive(v == "Live"));
        liveToggle.HorizontalAlignment = HorizontalAlignment.Left;
        liveToggle.VerticalAlignment = VerticalAlignment.Bottom;
        liveToggle.ToolTip = Loc.T("Live polls while this page is visible. Paused keeps the samples already taken and stops asking the engine. Polling also stops whenever you leave this page.");
        System.Windows.Automation.AutomationProperties.SetName(liveToggle, Loc.T("Sampling"));
        var header = Ui.Header("Live monitor", "Live CPU, memory, network and disk from docker stats", out subtitle,
            Ui.Field("Sampling interval", interval),
            liveToggle,
            Ui.AsyncIconButton(Icons.Refresh, "Take one sample now", () => Tick(force: true)));

        cpuChart.Format = v => v.ToString("0.0", CultureInfo.CurrentCulture) + "%";
        memChart.Format = v => Ui.FormatBytes(v);
        cpuDelta.Child = cpuDeltaText;
        cpuDelta.SetResourceReference(Border.BackgroundProperty, "WarningContainer");
        var cpuValues = Ui.HStack(10, cpuValue, cpuDelta);
        cpuDetail.TextAlignment = TextAlignment.Right; cpuDetail.MaxWidth = 380; cpuDetail.VerticalAlignment = VerticalAlignment.Bottom;
        var cpuHead = new DockPanel();
        cpuHead.Children.Add(cpuDetail.Dock(Dock.Right));
        cpuHead.Children.Add(Ui.VStack(2, cpuTitle, cpuValues));
        var cpuCard = Ui.Card(Ui.Rows(8, [0, 1], cpuHead, cpuChart), 16, "SurfaceLow");
        cpuCard.Padding = new Thickness(20, 16, 20, 12);
        var memHead = new DockPanel();
        memDetail.VerticalAlignment = VerticalAlignment.Bottom; memDetail.Margin = new Thickness(10, 0, 0, 3);
        memHead.Children.Add(memDetail.Dock(Dock.Right));
        memHead.Children.Add(memValue.Dock(Dock.Right));
        memHead.Children.Add(memTitle);
        var memCard = Ui.Card(Ui.Rows(6, [0, 1], memHead, memChart), 16, "SurfaceLow");
        memCard.Padding = new Thickness(20, 16, 20, 12);

        search.Width = double.NaN;
        search.Changed += FilterCards;
        var side = Ui.Rows(10, [0, 0, 1],
            Ui.Text("Containers", 16, FontWeights.SemiBold),
            search,
            new Grid { Children = { Ui.Scroll(cards), cardsEmpty } });
        // Design: CPU card on top, then the memory chart beside a 2 x 2 grid of container cards.
        main = Ui.Columns(12, [1, -520], memCard, side);
        var body = Ui.Rows(8, [1, 0], Ui.Rows(12, [5, 4], cpuCard, main), stateText);
        Layout(header, body);
        IsVisibleChanged += (_, _) => UpdateTimer();
        SizeChanged += (_, _) => main.ColumnDefinitions[2].Width = new GridLength(ActualWidth < 1100 ? 340 : 520);
        SetCharts();
    }

    private static int CurrentInterval => Intervals.Contains(AppHost.Current.Settings.MonitorIntervalSeconds) ? AppHost.Current.Settings.MonitorIntervalSeconds : 2;
    private void ApplyInterval()
    {
        timer.Interval = TimeSpan.FromSeconds(CurrentInterval);
        cpuChart.WindowSeconds = memChart.WindowSeconds = StatsHistory.Capacity * CurrentInterval;
        cpuChart.GapSeconds = memChart.GapSeconds = Math.Max(5, CurrentInterval * 3.5);
        UpdateTimer();
    }

    private void SetLive(bool value) { live = value; UpdateTimer(); SetCharts(); }

    private void UpdateTimer()
    {
        var run = live && IsVisible && !Host.IsCapture;
        if (run && !timer.IsEnabled) { timer.Start(); _ = Tick(); }
        else if (!run && timer.IsEnabled) { timer.Stop(); readCancel?.Cancel(); }
    }

    public override async Task RefreshAsync()
    {
        history.Clear();
        cardByName.Clear();
        cards.Children.Clear();
        if (!IsCurrent) return;
        await Tick(force: true);
    }

    private async Task Tick(bool force = false)
    {
        if (reading) { SetCharts(); return; }
        if (!force && (!live || !IsVisible)) return;
        if (!IsCurrent) { stateText.Text = Loc.T("The engine context changed. Refreshing for the new context."); return; }
        reading = true;
        readCancel = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(10, CurrentInterval * 4)));
        var context = SnapshotContext;
        var generation = SnapshotGeneration;
        try
        {
            var samples = await StatsReader.ReadAsync(context, readCancel.Token);
            if (generation != SnapshotGeneration) return;
            history.Add(samples, DateTimeOffset.Now);
        }
        catch (OperationCanceledException) { history.Fail("The stats read was canceled or timed out.", DateTimeOffset.Now); }
        catch (Exception e) when (e is InvalidOperationException or System.Text.Json.JsonException or ArgumentException)
        {
            history.Fail(e.Message, DateTimeOffset.Now);
        }
        finally { reading = false; readCancel?.Dispose(); readCancel = null; }
        Render();
    }

    private void Render()
    {
        var running = history.Current.ToHashSet(StringComparer.Ordinal);
        foreach (var name in history.Known.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            if (!cardByName.TryGetValue(name, out var card))
            {
                card = new ContainerStatCard(name) { Margin = new Thickness(6) };
                card.Picked += Pick;
                cardByName[name] = card;
                cards.Children.Add(card);
            }
            card.Update(history.For(name), running.Contains(name));
        }
        if (focused == null || !cardByName.ContainsKey(focused)) focused = history.Current.FirstOrDefault() ?? cardByName.Keys.FirstOrDefault();
        foreach (var c in cardByName.Values) c.IsSelectedCard = c.ContainerName == focused;
        FilterCards();
        var count = history.Current.Count;
        subtitle.Text = Loc.Line(history.LastSuccess == null
            ? Loc.T("Live CPU, memory, network and disk from docker stats")
            : $"{count} running container{(count == 1 ? "" : "s")} · window {FormatWindow(StatsHistory.Capacity * CurrentInterval)} · docker stats every {CurrentInterval} s");
        stateText.Text = history.LastError != null
            ? $"Latest read failed: {history.LastError}" + (history.LastSuccess is { } ok ? $" Last good sample {(int)(DateTimeOffset.Now - ok).TotalSeconds} s ago." : "")
            : history.LastSuccess is { } t ? $"Last sample {t.ToLocalTime():HH:mm:ss} · context {SnapshotContext ?? "current"} · keeps the last {StatsHistory.Capacity} samples per container" : "";
        SetCharts();
    }

    private static string FormatWindow(int seconds) => seconds >= 120 ? $"{seconds / 60} min" : $"{seconds} s";

    private void Pick(string name) { focused = name; foreach (var c in cardByName.Values) c.IsSelectedCard = c.ContainerName == name; SetCharts(); }

    private void FilterCards()
    {
        var shown = 0;
        foreach (var card in cardByName.Values)
        {
            var ok = search.Matches(card.ContainerName);
            card.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
            if (ok) shown++;
        }
        cardsEmpty.Content = cardByName.Count == 0
            ? Ui.EmptyState(Icons.Monitoring, history.LastSuccess == null && history.LastError == null ? "Waiting for the first sample" : history.LastError != null ? "The engine did not answer" : "No running containers",
                history.LastError ?? (history.LastSuccess == null ? "The first stats read is in progress." : "Start a container to see live charts. Nothing is shown until the engine reports a real sample."),
                Ui.Button("Open Containers", Icons.DeployedCode, ButtonKind.Tonal, () => Host.Navigate("Containers")))
            : shown == 0 ? Ui.EmptyState(Icons.FilterList, "No matches", search.Status.Length > 0 ? search.Status : "No containers match the search.") : null;
        cardsEmpty.Visibility = cardsEmpty.Content == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetCharts()
    {
        var samples = focused == null ? [] : history.For(focused);
        var interval = CurrentInterval;
        ChartState state; string? text = null;
        if (history.LastAttempt == null) state = ChartState.Loading;
        else if (Host.IsCapture) { state = ChartState.Paused; text = "Single sample"; }
        else if (!live) state = ChartState.Paused;
        else if (history.LastError != null || history.LastSuccess is { } t && (DateTimeOffset.Now - t).TotalSeconds > interval * 3)
        {
            state = history.LastSuccess == null ? ChartState.Error : ChartState.Stale;
            if (history.LastSuccess is { } ok) text = $"Last sample {(int)(DateTimeOffset.Now - ok).TotalSeconds} s ago";
        }
        else if (focused != null && !history.Current.Contains(focused)) { state = ChartState.Stale; text = samples.Count > 0 ? $"Not running · last sample {(int)(DateTimeOffset.Now - samples[^1].Time).TotalSeconds} s ago" : null; }
        else state = ChartState.Live;

        cpuChart.SeriesName = "CPU" + (focused != null ? ", " + focused : "");
        memChart.SeriesName = "Memory" + (focused != null ? ", " + focused : "");
        cpuChart.SetData(samples.Where(s => s.CpuPercent.HasValue).Select(s => new ChartPoint(s.Time, s.CpuPercent!.Value)).ToList(), state, text);
        memChart.SetData(samples.Where(s => s.MemoryBytes.HasValue).Select(s => new ChartPoint(s.Time, s.MemoryBytes!.Value)).ToList(), state, text);
        cpuTitle.Text = Loc.T("CPU") + (focused != null ? " · " + focused : "");
        memTitle.Text = Loc.T("Memory") + (focused != null ? " · " + focused : "");
        var cpuPoints = samples.Where(s => s.CpuPercent.HasValue).ToList();
        cpuDelta.Visibility = Visibility.Collapsed;
        if (cpuPoints.Count > 1)
        {
            var latest = cpuPoints[^1];
            var earlier = cpuPoints.LastOrDefault(s => (latest.Time - s.Time).TotalSeconds >= 30);
            if (earlier != null && Math.Abs(latest.CpuPercent!.Value - earlier.CpuPercent!.Value) >= 0.1)
            {
                var d = latest.CpuPercent!.Value - earlier.CpuPercent!.Value;
                cpuDeltaText.Text = Loc.Line($"{(d >= 0 ? "+" : "-")}{Math.Abs(d):0.0} points in {(int)(latest.Time - earlier.Time).TotalSeconds} s");
                cpuDelta.Visibility = Visibility.Visible;
            }
        }
        var cpu = samples.Where(s => s.CpuPercent.HasValue).Select(s => s.CpuPercent!.Value).ToList();
        cpuValue.Text = cpu.Count > 0 ? $"{cpu[^1]:0.0}%" : "";
        cpuDetail.Text = cpu.Count > 0 ? $"Average {cpu.Average():0.0}% · peak {cpu.Max():0.0}% over {cpu.Count} sample{(cpu.Count == 1 ? "" : "s")} · 100% equals one full CPU core" : Loc.T("No samples yet");
        var last = samples.Count > 0 ? samples[^1] : null;
        memValue.Text = last?.MemoryBytes is { } used ? Ui.FormatBytes(used) : "";
        memDetail.Text = last?.MemoryBytes != null
            ? (last.MemoryLimitBytes is { } limit ? $"of {Ui.FormatBytes(limit)} limit" : "") + (last.MemoryPercent is { } pct ? (last.MemoryLimitBytes != null ? " · " : "") + $"{pct:0.0}%" : "")
            : Loc.T("No samples yet");
    }

    public override void OnShown() => UpdateTimer();

    public override IEnumerable<AppCommand> PageCommands() =>
    [
        new AppCommand { Id = "monitor.pause", Title = "Pause or resume live monitor", Group = "Monitor", Icon = Icons.Pause, Keywords = "stats live charts", Run = () => { Host.Navigate("Monitor"); SetLive(!live); } },
        new AppCommand { Id = "monitor.sample", Title = "Take one stats sample now", Group = "Monitor", Icon = Icons.Monitoring, Keywords = "stats cpu memory", Run = () => { Host.Navigate("Monitor"); _ = Tick(force: true); } },
    ];
}
