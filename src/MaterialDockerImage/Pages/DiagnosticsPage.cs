using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Read-only engine checks with real output, a sanitized report export, a live events stream and an optional smoke test.</summary>
public sealed class DiagnosticsPage : PageBase
{
    public override string Key => "Diagnostics";
    private readonly List<DiagnosticCheck> checks = DiagnosticChecks.Create();
    private readonly StackPanel rows = new();
    private readonly WrapPanel pills = new();
    private readonly TextBlock summary = Ui.Caption("Checks have not run yet.");
    private readonly TextBlock smokeState = Ui.Caption("");
    private readonly EventsPanel events;
    private readonly SearchFilter checkSearch = new("Search checks", 240);
    private readonly Dictionary<string, Expander> expanders = new();
    private CancellationTokenSource? runAll;

    public DiagnosticsPage()
    {
        events = new EventsPanel(() => SnapshotContext);
        checkSearch.Changed += Render;
        var export = Ui.Button("Export sanitized report", Icons.FileExport, ButtonKind.Outlined, () => DiagnosticReport.Preview(checks, SnapshotContext));
        var all = Ui.AsyncButton("Run all checks", Icons.PlayArrow, ButtonKind.Filled, RunAllAsync);
        var header = Ui.Header("Diagnostics", "Read-only checks. Nothing on your system is changed.", out _, export, all);
        var checksCard = PageParts.Section("Checks", null, Ui.VStack(10, Ui.Columns(12, [0, 1], checkSearch, summary), pills, rows));
        var smoke = PageParts.Section("Smoke test (optional)", "Pulls and runs hello-world, then removes the container. This one does change the engine.",
            Ui.VStack(8, Ui.Caption("The container gets a unique label and name, so only that container is removed. The hello-world image stays in the image list; remove it from Images if you do not want it."), smokeState),
            Ui.AsyncButton("Run hello-world smoke test…", Icons.Science, ButtonKind.Tonal, SmokeAsync));
        var body = Ui.Scroll(Ui.VStack(12, PageParts.Responsive(checksCard, events.Card, 1250, 1.1, 1), smoke).Margin(0, 0, 8, 16));
        Layout(header, body);
        IsVisibleChanged += (_, _) => { if (!IsVisible) { events.Stop("Stopped because the page was hidden"); runAll?.Cancel(); } };
        Render();
    }

    public override Task RefreshAsync()
    {
        // Checks run only when asked; refreshing clears results and stops a stream that belong to another engine context.
        if (events.Running) events.Stop("Stopped because the page was refreshed or the engine context changed");
        foreach (var c in checks) { c.Status = CheckStatus.NotRun; c.Last = null; c.RanAt = null; }
        Render();
        return Task.CompletedTask;
    }

    private void Render()
    {
        rows.Children.Clear();
        foreach (var check in checks.Where(c => checkSearch.Matches($"{c.Label} {c.Command} {c.Last?.Value} {c.Last?.Detail}")))
            rows.Children.Add(Row(check));
        if (rows.Children.Count == 0) rows.Children.Add(Ui.Caption(checkSearch.Status.Length > 0 ? checkSearch.Status : "No checks match the search."));
        int Count(CheckStatus s) => checks.Count(c => c.Status == s);
        pills.Children.Clear();
        if (checks.Any(c => c.Status != CheckStatus.NotRun))
        {
            pills.Children.Add(Ui.Badge($"{Count(CheckStatus.Passed)} passed", Tone.Success, false).Margin(0, 0, 8, 0));
            pills.Children.Add(Ui.Badge($"{Count(CheckStatus.Warning)} warning", Count(CheckStatus.Warning) > 0 ? Tone.Warning : Tone.Neutral, false).Margin(0, 0, 8, 0));
            pills.Children.Add(Ui.Badge($"{Count(CheckStatus.Failed)} failed", Count(CheckStatus.Failed) > 0 ? Tone.Error : Tone.Neutral, false));
        }
        var ran = checks.Where(c => c.RanAt != null).Select(c => c.RanAt!.Value).DefaultIfEmpty().Max();
        summary.Text = checks.All(c => c.Status == CheckStatus.NotRun) ? Loc.T("Checks have not run yet.")
            : $"{Count(CheckStatus.Passed)} passed · {Count(CheckStatus.Warning)} warning · {Count(CheckStatus.Failed)} failed · {Count(CheckStatus.NotRun)} not run" + (ran != default ? $" · last run {ran.ToLocalTime():HH:mm:ss}" : "");
    }

    private FrameworkElement Row(DiagnosticCheck check)
    {
        var (icon, brush, tone, label) = check.Status switch
        {
            CheckStatus.Passed => (Icons.CheckCircle, "Success", Tone.Success, "Passed"),
            CheckStatus.Warning => (Icons.Warning, "Warning", Tone.Warning, "Warning"),
            CheckStatus.Failed => (Icons.Error, "Error", Tone.Error, "Failed"),
            CheckStatus.Running => (Icons.Pending, "Primary", Tone.Primary, "Running"),
            _ => (Icons.HourglassEmpty, "Outline", Tone.Neutral, "Not run")
        };
        var head = new DockPanel();
        head.Children.Add(Ui.Icon(icon, 22, true, brush).Margin(0, 0, 12, 0).Dock(Dock.Left));
        var run = Ui.AsyncButton("Run", null, ButtonKind.Text, () => RunAsync(check, CancellationToken.None));
        System.Windows.Automation.AutomationProperties.SetName(run, Loc.T("Run check: ") + Loc.T(check.Label));
        run.IsEnabled = check.Status != CheckStatus.Running;
        head.Children.Add(run.Dock(Dock.Right));
        head.Children.Add(Ui.Badge(check.Last?.Value is { Length: > 0 } v ? v : label, tone).Margin(8, 0, 8, 0).Dock(Dock.Right));
        head.Children.Add(Ui.VStack(2, Ui.Text(check.Label, 14, FontWeights.SemiBold), Ui.Caption(check.Last?.Detail ?? check.Command)));
        var stack = Ui.VStack(4, head);
        if (check.Last is { } last)
        {
            var detail = Ui.VStack(6,
                Ui.Mono("> " + last.Command, 12, "OnSurfaceVariant", true),
                Ui.Caption($"Exit code {(last.ExitCode?.ToString() ?? "n/a")} · {label} · ran {check.RanAt?.ToLocalTime():HH:mm:ss}"),
                PageParts.OutputBox(last.Output.Length > 0 ? last.Output : "(no output)", 220),
                Ui.Button("Copy output", Icons.ContentCopy, ButtonKind.Text, () => ActionSounds.Copy(last.Output)));
            var expander = new Expander { Header = Loc.T("Output and exit code"), Content = detail, IsExpanded = expanders.TryGetValue(check.Id, out var old) && old.IsExpanded };
            expanders[check.Id] = expander;
            stack.Children.Add(expander.Margin(34, 0, 0, 0));
        }
        var line = new Border { Child = stack, Padding = new Thickness(8, 10, 8, 10), MinHeight = 64, BorderThickness = new Thickness(0, 0, 0, 1) };
        line.SetResourceReference(Border.BorderBrushProperty, "SurfaceHigh");
        return line;
    }

    private async Task RunAsync(DiagnosticCheck check, CancellationToken token)
    {
        var context = SnapshotContext;
        check.Status = CheckStatus.Running;
        Render();
        try
        {
            var outcome = await check.Run(context, token);
            if (!IsCurrent) { check.Status = CheckStatus.NotRun; check.Last = null; Host.Notify.Warning("The engine context changed while the check ran. Run it again."); return; }
            check.Last = outcome; check.Status = outcome.Status; check.RanAt = DateTimeOffset.Now;
        }
        catch (OperationCanceledException) { check.Status = CheckStatus.NotRun; }
        catch (Exception e) when (e is InvalidOperationException or IOException or ArgumentException or System.Text.Json.JsonException)
        {
            check.Last = new(CheckStatus.Failed, "Error", e.Message, check.Command, null, e.Message);
            check.Status = CheckStatus.Failed; check.RanAt = DateTimeOffset.Now;
        }
        finally { Render(); }
    }

    private async Task RunAllAsync()
    {
        runAll?.Cancel();
        var source = runAll = new CancellationTokenSource();
        try { foreach (var check in checks) { if (source.IsCancellationRequested) break; await RunAsync(check, source.Token); } }
        finally { if (runAll == source) runAll = null; source.Dispose(); }
        var failed = checks.Count(c => c.Status == CheckStatus.Failed);
        if (failed > 0) Host.Notify.Warning($"{failed} check(s) failed. Expand a check to see its output and exit code.");
        else Host.Notify.Success(Loc.Voice("All checks finished without failures.", "Healthy enough!", "幾健康！"));
    }

    private async Task SmokeAsync()
    {
        if (!IsCurrent) { Host.Notify.Warning("The engine context changed after this page loaded. Refresh before running the smoke test."); return; }
        var id = Guid.NewGuid().ToString("N")[..12];
        var name = "mdi-smoke-" + id;
        var label = "material-docker-image.smoke=" + id;
        if (!await Host.Overlay.ConfirmAsync("Run the hello-world smoke test?",
            $"This pulls hello-world if it is not present, runs it as container {name} with label {label} on context {SnapshotContext ?? "CLI default"}, and then removes that container. The image stays.", "Run smoke test")) return;
        smokeState.Text = Loc.T("Creating and running the container…");
        var run = await Host.Execute("Smoke test: run hello-world", ["run", "--name", name, "--label", label, "hello-world"], true, SnapshotContext);
        var created = run == null ? "not started (another operation was active or docker is missing)" : run.Success ? "passed (exit 0, hello-world printed its message)" : $"failed with exit code {run.ExitCode}";
        smokeState.Text = $"Creation and run: {created}. Removing…";
        if (run == null) { smokeState.Text = $"Creation and run: {created}. Teardown: not needed."; return; }
        // Remove only the container carrying this run's unique label, whether or not the run succeeded.
        var ids = await Host.Read(["container", "ls", "--all", "--quiet", "--filter", "label=" + label], SnapshotContext);
        var found = ids.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string teardown;
        if (!ids.Success) teardown = "could not list containers to remove: " + PageParts.Shorten(ids.StandardError.Trim(), 160);
        else if (found.Length == 0) teardown = "no container with this label was created, so nothing was removed";
        else
        {
            var rm = await Host.Execute("Smoke test: remove container", ["container", "rm", "--force", .. found], true, SnapshotContext);
            teardown = rm == null ? "not run (another operation was active); remove " + name + " from Containers" : rm.Success ? $"removed {found.Length} container(s)" : $"failed with exit code {rm.ExitCode}; remove {name} from Containers";
        }
        smokeState.Text = $"Creation and run: {created}.\nTeardown: {teardown}.";
    }

    public override IEnumerable<AppCommand> PageCommands() =>
    [
        new AppCommand { Id = "diagnostics.run", Title = "Run all diagnostic checks", Group = "Diagnostics", Icon = Icons.Stethoscope, Keywords = "health check engine version", Run = () => { Host.Navigate(Key); _ = RunAllAsync(); } },
        new AppCommand { Id = "diagnostics.report", Title = "Export sanitized diagnostics report…", Group = "Diagnostics", Icon = Icons.FileExport, Keywords = "support report export", Run = () => { Host.Navigate(Key); DiagnosticReport.Preview(checks, SnapshotContext); } },
        new AppCommand { Id = "diagnostics.events", Title = "Stream engine events", Group = "Diagnostics", Icon = Icons.EventNote, Keywords = "docker events live", Run = () => { Host.Navigate(Key); events.Start(); } },
    ];
}
