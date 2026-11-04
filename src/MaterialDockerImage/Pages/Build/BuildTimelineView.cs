using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Live BuildKit step timeline: stat tiles, one row per vertex (status icon, stage chip, step text, duration bar,
/// CACHED badge) and the raw log, which a click on a step filters to that step's lines. Shared by Build and Bake.
/// </summary>
public sealed class BuildTimelineView : Border
{
    private const int MaxLogLines = 5000;
    private readonly StackPanel content = new();
    private readonly FrameworkElement empty;
    private readonly TextBlock title = Ui.Text("", 16, FontWeights.SemiBold);
    private readonly TextBlock commandText = Ui.Mono("", 12, "OnSurfaceVariant", true);
    private readonly Border statusBadge = new();
    private readonly Button cancel;
    private readonly ProgressBar busy = new() { IsIndeterminate = true, Height = 4, Visibility = Visibility.Collapsed };
    private readonly BuildStatTile totalTile = new("Total time", Icons.Timer), cachedTile = new("Cached", Icons.Cached), stepsTile = new("Steps", Icons.Timeline), digestTile = new("Result digest", Icons.Fingerprint, true);
    private readonly Border failure;
    private readonly TextBlock failureText = Ui.Text("", 13, null, "OnErrorContainer");
    private readonly ContentControl resultSlot = new() { Focusable = false };
    private readonly StackPanel rows = new();
    private readonly TextBlock stepsCaption = Ui.Caption("");
    private readonly ReadOnlySearchText log = new("Search build log", 200, 420);
    private readonly TextBlock logTitle = Ui.Overline("Raw log");
    private readonly Button clearFilter;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Dictionary<int, BuildTimelineRow> rowById = new();
    private readonly List<string> lines = new();
    private BuildKitStep? selected;
    private bool running, logDirty;
    private readonly System.Diagnostics.Stopwatch watch = new();
    private double finishedAt;
    private string command = "";

    public BuildKitProgress Progress { get; private set; } = new();
    public event Action? CancelRequested;
    /// <summary>The command of the latest run, formatted for copying.</summary>
    public string Command => command;

    public BuildTimelineView(string emptyMessage, params UIElement[] emptyActions)
    {
        cancel = Ui.Button("Cancel", Icons.Stop, ButtonKind.Outlined, () => CancelRequested?.Invoke());
        cancel.Visibility = Visibility.Collapsed;
        var copy = Ui.IconButton(Icons.ContentCopy, "Copy command", () => { if (command.Length > 0) { ActionSounds.Copy(command); AppHost.Current.Notify.Info("Copied the build command."); } });
        var actions = Ui.HStack(8, statusBadge, cancel, copy);
        actions.VerticalAlignment = VerticalAlignment.Top;
        var head = new DockPanel();
        head.Children.Add(actions.Dock(System.Windows.Controls.Dock.Right));
        head.Children.Add(Ui.VStack(4, title, commandText).Margin(0, 0, 12, 0));

        var tiles = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4 };
        foreach (var tile in new[] { totalTile, cachedTile, stepsTile, digestTile }) { tile.Margin = new Thickness(0, 0, 8, 8); tiles.Children.Add(tile); }
        tiles.SizeChanged += (_, e) => tiles.Columns = e.NewSize.Width < 560 ? 2 : 4;

        failure = Ui.Card(Ui.Columns(10, [0, 1], Ui.Icon(Icons.Error, 20, true, "OnErrorContainer"), failureText), 12, "ErrorContainer", 12);
        failure.Visibility = Visibility.Collapsed;

        clearFilter = Ui.Button("Show all lines", Icons.Close, ButtonKind.Text, () => Select(null));
        clearFilter.Visibility = Visibility.Collapsed;
        var logHead = new DockPanel { Margin = new Thickness(0, 4, 0, 6) };
        logHead.Children.Add(clearFilter.Dock(System.Windows.Controls.Dock.Right));
        logHead.Children.Add(logTitle);

        var stepsHead = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        stepsHead.Children.Add(stepsCaption.Dock(System.Windows.Controls.Dock.Right));
        stepsHead.Children.Add(Ui.Overline("Steps"));
        var stepsScroll = new ScrollViewer { Content = rows, MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        var stepsCard = Ui.Card(Ui.VStack(0, stepsHead, stepsScroll), 12, "SurfaceContainer", 14);

        foreach (var element in new UIElement[] { head, busy, tiles, failure, resultSlot, stepsCard, Ui.VStack(0, logHead, log) }) Ui.Add(content, element, 12);
        content.Visibility = Visibility.Collapsed;
        empty = Ui.EmptyState(Icons.ViewTimeline, "No build has run here yet", emptyMessage, emptyActions);
        empty.Margin = new Thickness(0, 48, 0, 48);
        Child = Ui.Scroll(new Grid { Children = { empty, content } });
        timer.Tick += (_, _) => Render();
    }

    /// <summary>Requests cancellation of the running command (same as the Cancel button).</summary>
    public void Cancel() => CancelRequested?.Invoke();

    /// <summary>Puts a result view (metadata card, call result) under the stat tiles; null clears it.</summary>
    public void SetResult(UIElement? element) => resultSlot.Content = element;

    public void Begin(string label, string formattedCommand)
    {
        Progress = new BuildKitProgress();
        rowById.Clear(); rows.Children.Clear(); lines.Clear(); selected = null;
        running = true; finishedAt = 0; logDirty = true;
        watch.Restart();
        command = formattedCommand;
        title.Text = Loc.T(label);
        commandText.Text = formattedCommand;
        SetStatus("Running", Tone.Primary);
        cancel.Visibility = Visibility.Visible; cancel.IsEnabled = true;
        busy.Visibility = Visibility.Visible;
        failure.Visibility = Visibility.Collapsed;
        resultSlot.Content = null;
        empty.Visibility = Visibility.Collapsed; content.Visibility = Visibility.Visible;
        timer.Start();
        Render();
    }

    public void Feed(string line)
    {
        Progress.Feed(line, watch.Elapsed.TotalSeconds);
        lines.Add(line);
        if (lines.Count > MaxLogLines) lines.RemoveRange(0, lines.Count - MaxLogLines);
        logDirty = true;
    }

    public void End(bool success, bool cancelled, int exitCode)
    {
        var now = finishedAt = watch.Elapsed.TotalSeconds;
        watch.Stop();
        running = false;
        Progress.Finish(success, cancelled, now);
        timer.Stop();
        cancel.Visibility = Visibility.Collapsed;
        busy.Visibility = Visibility.Collapsed;
        if (cancelled) SetStatus("Canceled", Tone.Warning);
        else if (success) SetStatus("Completed", Tone.Success);
        else SetStatus(Loc.T("Failed · exit") + " " + exitCode, Tone.Error);
        if (!success && !cancelled)
        {
            var failed = Progress.Steps.LastOrDefault(s => s.Status == BuildKitStepStatus.Error);
            failureText.Text = Progress.FailureSummary ?? failed?.Error ?? Loc.T("The build failed. The log below has the details.");
            failure.Visibility = Visibility.Visible;
            if (failed != null) selected = failed;
        }
        logDirty = true;
        Render();
    }

    private void SetStatus(string text, Tone tone)
    {
        var (bg, fg) = Ui.ToneKeys(tone);
        statusBadge.Child = Ui.Text(Loc.T(text), 12, FontWeights.SemiBold, fg, false, false);
        statusBadge.CornerRadius = new CornerRadius(12); statusBadge.Padding = new Thickness(10, 4, 10, 4);
        statusBadge.VerticalAlignment = VerticalAlignment.Center;
        statusBadge.SetResourceReference(BackgroundProperty, bg);
    }

    private void Select(BuildKitStep? step)
    {
        selected = selected == step ? null : step;
        logDirty = true;
        Render();
    }

    private void Render()
    {
        var now = running ? watch.Elapsed.TotalSeconds : finishedAt;
        var steps = Progress.Steps;
        var longest = Math.Max(0.1, Progress.LongestSeconds(now));
        foreach (var step in steps)
        {
            if (!rowById.TryGetValue(step.Id, out var row))
            {
                row = new BuildTimelineRow(step, () => Select(step));
                rowById[step.Id] = row;
                rows.Children.Add(row);
            }
            row.Update(now, longest, step == selected);
        }
        var instructions = Progress.InstructionCount;
        totalTile.Value = FormatSeconds(now);
        totalTile.Detail = running ? Loc.T("running") : Loc.T("wall clock");
        cachedTile.Value = $"{Progress.CachedCount} / {instructions}";
        cachedTile.Detail = instructions == 0 ? Loc.T("no Dockerfile steps yet") : $"{Math.Round(100.0 * Progress.CachedCount / instructions)}% {Loc.T("of instructions")}";
        stepsTile.Value = steps.Count.ToString(CultureInfo.InvariantCulture);
        stepsTile.Detail = $"{steps.Count(s => s.Status == BuildKitStepStatus.Running)} {Loc.T("running")} · {steps.Count(s => s.Status == BuildKitStepStatus.Error)} {Loc.T("failed")}";
        digestTile.Value = Progress.ResultDigest is { } d ? d.Replace("sha256:", "")[..Math.Min(12, d.Length - 7)] : "—";
        digestTile.Detail = Progress.ResultDigest == null ? Loc.T(running ? "waiting for export" : "no image digest reported") : Progress.ImageNames.FirstOrDefault() ?? "sha256";
        digestTile.CopyText = Progress.ResultDigest;
        stepsCaption.Text = Progress.Builder is { } b ? Loc.T("Builder") + " " + b : "";
        if (logDirty)
        {
            logDirty = false;
            if (selected != null)
            {
                logTitle.Text = $"{Loc.T("Log for step")} #{selected.Id}";
                clearFilter.Visibility = Visibility.Visible;
                log.Text = BuildKitProgress.StepText(selected);
            }
            else
            {
                logTitle.Text = Loc.T("Raw log");
                clearFilter.Visibility = Visibility.Collapsed;
                var sb = new StringBuilder();
                foreach (var line in lines) sb.AppendLine(line);
                log.Text = sb.ToString();
                if (running && !log.Search.IsActive) log.Children.OfType<TextBox>().FirstOrDefault()?.ScrollToEnd();
            }
        }
    }

    public static string FormatSeconds(double seconds) => seconds < 60
        ? seconds.ToString("0.0", CultureInfo.InvariantCulture) + "s"
        : $"{(int)(seconds / 60)}m {(int)(seconds % 60):00}s";
}

/// <summary>A tonal tile with one big value, a label and a detail line.</summary>
public sealed class BuildStatTile : Border
{
    private readonly TextBlock valueText = Ui.Text("—", 24, FontWeights.SemiBold, "OnSurface", false, false);
    private readonly TextBlock detailText = Ui.Text("", 12, null, "OnSurfaceVariant", false, false);
    private readonly Button? copy;
    private string? copyText;

    public string Value { set => valueText.Text = value; }
    public string Detail { set => detailText.Text = value; }
    public string? CopyText { set { copyText = value; if (copy != null) copy.Visibility = value == null ? Visibility.Collapsed : Visibility.Visible; } }

    public BuildStatTile(string label, string icon, bool mono = false)
    {
        if (mono) valueText.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        var head = new DockPanel();
        if (mono)
        {
            copy = Ui.IconButton(Icons.ContentCopy, "Copy " + Loc.T(label).ToLowerInvariant(), () => { if (copyText != null) { ActionSounds.Copy(copyText); AppHost.Current.Notify.Info("Copied the digest."); } });
            copy.Width = copy.Height = copy.MinHeight = 28; copy.Margin = new Thickness(0, -6, -6, -6);
            copy.Visibility = Visibility.Collapsed;
            head.Children.Add(copy.Dock(System.Windows.Controls.Dock.Right));
        }
        head.Children.Add(Ui.HStack(6, Ui.Icon(icon, 16, false, "Primary"), Ui.Overline(label)));
        Child = Ui.VStack(4, head, valueText, detailText);
        Padding = new Thickness(14, 12, 14, 12);
        CornerRadius = new CornerRadius(14);
        SetResourceReference(BackgroundProperty, "SurfaceContainer");
        System.Windows.Automation.AutomationProperties.SetName(this, Loc.T(label));
    }
}
