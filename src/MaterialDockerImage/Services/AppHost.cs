using System.Windows;
using MaterialDockerImage.Core;
using MaterialDockerImage.Shell;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Services;

/// <summary>Shared services every page uses: engine access, settings, history, notifications, overlays and navigation.</summary>
public sealed class AppHost
{
    public static AppHost Current { get; private set; } = null!;
    public DockerService Docker { get; } = new();
    public UserSettings Settings { get; }
    public HistoryStore History { get; } = new();
    public Notifier Notify { get; }
    public OverlayHost Overlay { get; }
    public OperationsDock Dock { get; }
    public CommandRegistry Commands { get; } = new();
    /// <summary>Session streak of completed operations; shown as a chip when the Momentum mode is on.</summary>
    public MomentumTracker Momentum { get; } = new();
    /// <summary>Evaluates scheduled language and theme rules and the optional local source file.</summary>
    public ScheduleRuntime Schedules { get; private set; } = null!;
    public UpdateService Updates { get; }
    /// <summary>PIN and authenticator credentials for the locks, kept apart from settings.json. Capture mode keeps them in memory only.</summary>
    public LockStore Locks { get; }
    public bool IsCapture { get; init; }
    public string? Context { get; private set; }
    public long Generation { get; private set; }
    public event Action? ContextChanged;
    public event Action? SettingsChanged;
    public Action<string> Navigate { get; set; } = _ => { };
    public Action<string, string?> NavigateWith { get; set; } = (_, _) => { };
    private CancellationTokenSource? operation;
    public bool Busy => operation != null;
    public event Action<bool>? BusyChanged;
    /// <summary>Hand-off slots between destinations (for example Creator to Build, Images to Run).</summary>
    public Dictionary<string, object> HandOff { get; } = new(StringComparer.Ordinal);

    public AppHost(UserSettings settings, Notifier notify, OverlayHost overlay, OperationsDock dock, bool capture)
    {
        Settings = settings; Updates = new UpdateService(() => settings.AutoUpdateCheck); Notify = notify; Overlay = overlay; Dock = dock; IsCapture = capture;
        Context = string.IsNullOrWhiteSpace(settings.Context) ? null : settings.Context;
        dock.CancelRequested += () => { operation?.Cancel(); Notify.Warning("Cancellation requested. Engine-side work may continue; refresh and reconcile before retrying."); };
        Locks = new LockStore(capture ? null : LockStore.DefaultPath, DpapiProtector.IsAvailable ? new DpapiProtector() : null);
        Current = this;
        Schedules = new ScheduleRuntime(this);
    }

    /// <summary>Tells the window to re-read settings that changed in memory only (a scheduled override), without saving them.</summary>
    public void NotifySettingsChanged() => SettingsChanged?.Invoke();

    public bool IsLocked(string id) => ElementLocks.IsLocked(Settings, id);

    /// <summary>Disables a live control (palette row, accessibility menu) while its settings row is locked, so a lock cannot be walked around.</summary>
    public T Lockable<T>(string id, T control) where T : UIElement
    {
        if (IsLocked(id)) { control.IsEnabled = false; control.SetValue(System.Windows.Automation.AutomationProperties.HelpTextProperty, Loc.T("Locked in Settings. Unlock it there to change it.")); }
        return control;
    }

    public void SetContext(string? context)
    {
        if (context == Context) return;
        Context = context;
        Generation++;
        Settings.Context = context ?? "";
        SaveSettings(null);
        ContextChanged?.Invoke();
    }

    public bool IsCurrent(string? snapshotContext, long snapshotGeneration) => SourceSafety.IsCurrentSnapshot(snapshotContext, snapshotGeneration, Context, Generation);

    /// <summary>Saves settings and, when an action is named, records the change in local history.</summary>
    public void SaveSettings(string? historyAction)
    {
        // Capture mode changes theme, language and context in memory only; it must never overwrite the user's saved settings.
        if (IsCapture) { SettingsChanged?.Invoke(); return; }
        try { Settings.Save(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Notify.Error("Settings could not be saved: " + e.Message); return; }
        if (historyAction != null && !IsCapture) History.Record("settings", historyAction, new Dictionary<string, string> { ["settings/settings.json"] = Settings.ToJson() });
        SettingsChanged?.Invoke();
    }

    /// <summary>
    /// Runs one foreground operation with streamed output in the dock. Only one runs at a time; a second request
    /// is refused rather than queued. The returned result is the real process outcome.
    /// </summary>
    public async Task<CommandResult?> Execute(string label, IReadOnlyList<string> args, bool displayOutput = true, string? boundContext = null, string? standardInput = null, bool record = true)
    {
        if (operation != null) { Notify.Warning("Another operation is active. Cancel it or wait for its actual result."); return null; }
        operation = new CancellationTokenSource();
        BusyChanged?.Invoke(true);
        Dock.Begin(label);
        var context = boundContext ?? Context;
        try
        {
            var progress = displayOutput ? new Progress<DockerOutput>(line => { foreach (var part in line.Text.Replace("\r\n", "\n").Split('\n')) if (part.Length > 0) Dock.Append(part); }) : null;
            var result = standardInput == null
                ? await Docker.ExecuteAsync(args, context, progress, operation.Token)
                : await Docker.ExecuteWithInputAsync(args, standardInput, context, progress, operation.Token);
            Dock.End(result.Success, result.Cancelled, result.ExitCode);
            if (!displayOutput && !result.Success && result.StandardError.Length > 0) Dock.Append(result.StandardError.Trim());
            Momentum.Record(result.Success);
            Notify.NextCue = SoundCueRules.ForOperation(args, result.Success, result.Cancelled);
            if (record && !IsCapture) History.Record("operation", $"{label} · {(result.Cancelled ? "canceled" : result.Success ? "completed" : "failed, exit " + result.ExitCode)} · context {context ?? "current"}");
            if (result.Cancelled) Notify.Warning(Loc.Voice($"{label} was canceled. Refresh before retrying.", "Stopped.", "停咗。"));
            else if (result.Success) Notify.Success(Loc.Voice($"{label} completed.", "Done!", "搞掂！"));
            else Notify.Error(Loc.Voice($"{label} failed with exit code {result.ExitCode}. The output panel has the details.", "That did not work.", "出事喇。"));
            return result;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or ArgumentException or IOException or InvalidOperationException or NotSupportedException)
        {
            Dock.End(false, false, -1);
            Dock.Append(e.Message);
            Notify.Error(label + ": " + (e is System.ComponentModel.Win32Exception ? "the docker command was not found. Install Docker or add it to PATH, then run Diagnostics." : e.Message));
            return null;
        }
        finally
        {
            operation.Dispose(); operation = null;
            BusyChanged?.Invoke(false);
        }
    }

    /// <summary>Background read used for lists and inspections. It does not block foreground operations or write output.</summary>
    public async Task<CommandResult> Read(IReadOnlyList<string> args, string? context = null, CancellationToken token = default)
    {
        try { return await Docker.ExecuteAsync(args, context ?? Context, null, token); }
        catch (System.ComponentModel.Win32Exception) { return new CommandResult(-1, "", "The docker command was not found. Install Docker or add it to PATH."); }
    }

    /// <summary>Background read returning parsed JSON rows (one object per line or an array).</summary>
    public async Task<IReadOnlyList<System.Text.Json.JsonElement>> ReadRows(IReadOnlyList<string> args, string? context = null, CancellationToken token = default)
    {
        var result = await Read(args, context, token);
        if (!result.Success) throw new InvalidOperationException(result.StandardError.Trim().Length > 0 ? result.StandardError.Trim() : $"docker exited with code {result.ExitCode}");
        if (result.Truncated) throw new InvalidOperationException("The response exceeded the capture limit. Narrow the selection and retry.");
        return DockerJson.ParseRows(result.StandardOutput);
    }
}
