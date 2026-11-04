using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace BuildCompanion;

public sealed record SetupRow(string Glyph, Brush Brush, string Path, string Status)
{
    // Screen readers announce list items by ToString(); keep it to the meaningful part.
    public override string ToString() => $"{Path}: {Status}";
}

public partial class MainWindow : Window
{
    const string GlyphCheck = "", GlyphWarn = "", GlyphInfo = "";
    const int MaxLogCharacters = 400_000;

    readonly string[] arguments;
    readonly string baseDirectory;
    readonly BuildRunner runner = new();
    readonly ConcurrentQueue<LogLine> pending = new();
    readonly StringBuilder fullLog = new();
    readonly DispatcherTimer ticker = new() { Interval = TimeSpan.FromMilliseconds(120) };
    CompanionLanguage language = CompanionLanguage.English;
    string? repo;
    CancellationTokenSource? cancellation;
    PhaseTracker tracker = new();
    RunState state = RunState.Idle;
    int? lastExit;
    TimeSpan elapsed;
    DateTime startedAt;
    string? statusOverride;

    public MainWindow(string[] args, string baseDirectory)
    {
        arguments = args;
        this.baseDirectory = baseDirectory;
        InitializeComponent();
        ticker.Tick += (_, _) => Tick();
        ticker.Start();
        var lookup = RepoLocator.Find(args, baseDirectory);
        repo = lookup.Path;
        if (lookup.Error is not null) Append(new LogLine(true, lookup.Error));
        Loaded += (_, _) => { if (repo is null) PickRepository(); ApplyLanguage(); };
        ApplyLanguage();
    }

    string T(string key) => CompanionStrings.Get(key, language);

    void ApplyLanguage()
    {
        Title = T("title");
        TitleText.Text = T("title");
        RepoLabel.Text = T("repo");
        RepoPath.Text = repo ?? T("noRepo");
        LanguageButton.Content = T("language") + ": " + (language == CompanionLanguage.English ? "EN" : language == CompanionLanguage.Cantonese ? "粵" : "EN · 粵");
        ChangeRepoButton.Content = T("change");
        SetupHeading.Text = T("setup");
        SetupNote.Text = T("setupNote");
        VersionLabel.Text = T("version");
        VersionHint.Text = T("versionHint");
        BuildButton.Content = T("build");
        BuildRunButton.Content = T("buildRun");
        InstallerButton.Content = T("installer");
        InstallerNote.Text = T("installerNote");
        CancelButton.Content = T("cancel");
        CopyButton.Content = T("copyLog");
        ShortcutText.Text = T("shortcuts");
        AutoName(LanguageButton, "language"); AutoName(ChangeRepoButton, "change"); AutoName(BuildButton, "build");
        AutoName(BuildRunButton, "buildRun"); AutoName(InstallerButton, "installer"); AutoName(CancelButton, "cancel"); AutoName(CopyButton, "copyLog");
        AutomationProperties.SetName(VersionBox, T("version"));
        AutomationProperties.SetHelpText(VersionBox, T("versionHint"));
        AutomationProperties.SetName(LogBox, T("log"));
        AutomationProperties.SetName(StateText, T("running"));
        AutomationProperties.SetName(Progress, T("phase"));
        RefreshSetup();
        RefreshStatus();
        OnVersionChanged(null, null!);
    }

    void AutoName(DependencyObject element, string key) => AutomationProperties.SetName(element, T(key));

    void RefreshSetup()
    {
        var rows = new List<SetupRow>();
        if (repo is not null)
            foreach (var item in SetupCheck.Inspect(repo))
            {
                var ok = item.Present;
                var glyph = ok ? GlyphCheck : item.Required ? GlyphWarn : GlyphInfo;
                var brush = (Brush)FindResource(ok ? "Success" : item.Required ? "Error" : "Warning");
                var status = ok ? T("present") : item.Required ? T("missing") : T("prepared");
                rows.Add(new SetupRow(glyph, brush, item.RelativePath, status));
            }
        SetupList.ItemsSource = rows;
        var ready = repo is not null && SetupCheck.CanBuild(SetupCheck.Inspect(repo));
        var idle = state != RunState.Running;
        BuildButton.IsEnabled = ready && idle;
        BuildRunButton.IsEnabled = ready && idle;
        InstallerButton.IsEnabled = ready && idle && ArgumentValidation.IsValidVersion(VersionBox.Text.Trim());
        CancelButton.IsEnabled = state == RunState.Running;
    }

    void OnVersionChanged(object? sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (VersionError is null) return;
        var text = VersionBox.Text.Trim();
        var bad = text.Length > 0 && !ArgumentValidation.IsValidVersion(text);
        VersionError.Visibility = bad ? Visibility.Visible : Visibility.Collapsed;
        VersionError.Text = T("versionBad");
        if (state != RunState.Running && repo is not null)
        {
            BuildButton.IsEnabled = !bad && SetupCheck.CanBuild(SetupCheck.Inspect(repo));
            BuildRunButton.IsEnabled = BuildButton.IsEnabled;
            InstallerButton.IsEnabled = !bad && text.Length > 0 && BuildButton.IsEnabled;
        }
    }

    void RefreshStatus()
    {
        var (key, brush, fg) = state switch
        {
            RunState.Running => ("running", "SecondaryContainer", "OnSecondaryContainer"),
            RunState.Succeeded => ("succeeded", "SuccessContainer", "OnSuccessContainer"),
            RunState.Failed => ("failed", "ErrorContainer", "OnErrorContainer"),
            RunState.LaunchFailed => ("launchFailed", "ErrorContainer", "OnErrorContainer"),
            RunState.Cancelled => ("cancelled", "WarningContainer", "OnWarningContainer"),
            _ => ("idle", "SecondaryContainer", "OnSecondaryContainer"),
        };
        StateText.Text = T(key);
        StateChip.Background = (Brush)FindResource(brush);
        StateText.Foreground = (Brush)FindResource(fg);
        AutomationProperties.SetName(StateText, T(key));
        PhaseText.Text = state == RunState.Running ? T("phase") + ": " + T("phase" + (int)tracker.Current) : statusOverride ?? "";
        ElapsedText.Text = T("elapsed") + " " + elapsed.ToString(@"hh\:mm\:ss");
        ExitText.Text = T("exitCode") + ": " + (lastExit?.ToString() ?? "-");
        Progress.IsIndeterminate = state == RunState.Running;
        if (state != RunState.Running) Progress.Value = state == RunState.Succeeded ? 100 : 0;
    }

    void Tick()
    {
        var wrote = false;
        var batch = new StringBuilder();
        while (pending.TryDequeue(out var line))
        {
            var text = (line.IsError ? "[err] " : "") + line.Text + "\r\n";
            batch.Append(text);
            fullLog.Append(text);
            if (state == RunState.Running) tracker.Observe(line.Text);
            wrote = true;
        }
        if (wrote)
        {
            LogBox.AppendText(batch.ToString());
            if (LogBox.Text.Length > MaxLogCharacters) LogBox.Text = LogBox.Text[^(MaxLogCharacters / 2)..];
            LogBox.ScrollToEnd();
        }
        if (state == RunState.Running) { elapsed = DateTime.UtcNow - startedAt; RefreshStatus(); }
    }

    void Append(LogLine line) => pending.Enqueue(line);

    async Task Start(BuildAction action)
    {
        if (state == RunState.Running || repo is null) return;
        BuildPlan plan;
        try { plan = BuildPlan.Create(action, repo, VersionBox.Text); }
        catch (ArgumentException ex) { Append(new LogLine(true, ex.Message)); statusOverride = ex.Message; state = RunState.Failed; RefreshStatus(); return; }

        LogBox.Clear();
        fullLog.Clear();
        tracker = new PhaseTracker();
        statusOverride = null;
        lastExit = null;
        state = RunState.Running;
        startedAt = DateTime.UtcNow;
        elapsed = TimeSpan.Zero;
        cancellation = new CancellationTokenSource();
        foreach (var step in plan.Steps) Append(new LogLine(false, "> " + (step.IsLaunch ? step.FileName : step.FileName + " " + step.Arguments)));
        RefreshSetup();
        RefreshStatus();
        RunResult result;
        try { result = await runner.RunAsync(plan, Append, null, cancellation.Token); }
        catch (Exception ex) { result = new RunResult(RunState.Failed, null, DateTime.UtcNow - startedAt, ex.Message); Append(new LogLine(true, ex.Message)); }
        cancellation.Dispose();
        cancellation = null;
        state = result.State;
        lastExit = result.ExitCode;
        elapsed = result.Elapsed;
        statusOverride = result.Message;
        Append(new LogLine(result.State != RunState.Succeeded, $"{result.State}: {result.Message} (exit code {(result.ExitCode?.ToString() ?? "none")}, {result.Elapsed:hh\\:mm\\:ss})"));
        Tick();
        RefreshSetup();
        RefreshStatus();
    }

    async void OnBuild(object sender, RoutedEventArgs e) => await Start(BuildAction.Build);
    async void OnBuildRun(object sender, RoutedEventArgs e) => await Start(BuildAction.BuildAndRun);
    async void OnInstaller(object sender, RoutedEventArgs e) => await Start(BuildAction.BuildInstaller);
    void OnCancel(object sender, RoutedEventArgs e) => cancellation?.Cancel();

    void OnCopy(object sender, RoutedEventArgs e)
    {
        Tick();
        try { Clipboard.SetText(fullLog.ToString()); statusOverride = T("copied"); RefreshStatus(); }
        catch (System.Runtime.InteropServices.ExternalException) { statusOverride = "Clipboard unavailable."; RefreshStatus(); }
    }

    void OnLanguage(object sender, RoutedEventArgs e)
    {
        language = CompanionStrings.Next(language);
        ApplyLanguage();
    }

    void OnChangeRepo(object sender, RoutedEventArgs e) { if (state != RunState.Running) PickRepository(); }

    void PickRepository()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = T("pickRepo") };
        if (dialog.ShowDialog() != true) { ApplyLanguage(); return; }
        var lookup = RepoLocator.Find(new[] { "--repo", dialog.FolderName }, baseDirectory);
        if (lookup.Path is null) { Append(new LogLine(true, lookup.Error ?? "build.bat not found.")); statusOverride = lookup.Error; }
        else repo = lookup.Path;
        ApplyLanguage();
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (e.Key == Key.Escape && state == RunState.Running) { cancellation?.Cancel(); e.Handled = true; }
        else if (ctrl && !shift && e.Key == Key.B && BuildButton.IsEnabled) { _ = Start(BuildAction.Build); e.Handled = true; }
        else if (ctrl && !shift && e.Key == Key.R && BuildRunButton.IsEnabled) { _ = Start(BuildAction.BuildAndRun); e.Handled = true; }
        else if (ctrl && !shift && e.Key == Key.I && InstallerButton.IsEnabled) { _ = Start(BuildAction.BuildInstaller); e.Handled = true; }
        else if (ctrl && shift && e.Key == Key.C) { OnCopy(this, new RoutedEventArgs()); e.Handled = true; }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        cancellation?.Cancel();
        base.OnClosing(e);
    }
}
