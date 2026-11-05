using System.Diagnostics;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Foreground runner for BuildKit commands (build, bake). It mirrors <see cref="AppHost.Execute"/>: one operation at a
/// time, streamed output in the operations dock, local history and a notification with the real exit status. It also
/// feeds every line to a <see cref="BuildTimelineView"/> so steps can be shown as they happen. Build and Bake share one
/// active slot; <see cref="AppHost.Busy"/> does not see these runs, so callers disable their own buttons while
/// <see cref="AnyActive"/> is true.
/// </summary>
public sealed class BuildRunner
{
    private static AppHost Host => AppHost.Current;
    private static BuildRunner? active;
    private CancellationTokenSource? cancellation;

    /// <summary>True while any build or bake started by this application is running.</summary>
    public static bool AnyActive => active != null;
    /// <summary>Raised on the UI thread when a run starts (true) or ends (false).</summary>
    public static event Action<bool>? ActiveChanged;

    public BuildTimelineView Timeline { get; }
    public bool Running => cancellation != null;

    public BuildRunner(BuildTimelineView timeline)
    {
        Timeline = timeline;
        timeline.CancelRequested += () =>
        {
            if (cancellation == null) return;
            cancellation.Cancel();
            Host.Notify.Warning("Cancellation requested. Engine-side work may continue; refresh and reconcile before retrying.");
        };
    }

    /// <summary>
    /// Runs one buildx command. <paramref name="workingDirectory"/> is used by bake so relative contexts resolve from
    /// the bake file's folder. <paramref name="dockerfilePath"/> and <paramref name="dockerfileSource"/> record the saved
    /// Dockerfile of an image build so the Images layer table can relate history rows to its lines. Returns the real
    /// result, or null when nothing ran.
    /// </summary>
    public async Task<CommandResult?> RunAsync(string label, IReadOnlyList<string> args, string? context, string? workingDirectory = null, string reference = "", string dockerfilePath = "", string dockerfileSource = "")
    {
        if (Host.Busy || active != null) { Host.Notify.Warning("Another operation is active. Cancel it or wait for its actual result."); return null; }
        active = this;
        cancellation = new CancellationTokenSource();
        ActiveChanged?.Invoke(true);
        var started = DateTimeOffset.Now;
        var watch = Stopwatch.StartNew();
        var output = new BuildKitLineSplitter();
        var errors = new BuildKitLineSplitter();
        void Deliver(string line) { Host.Dock.Append(line); Timeline.Feed(line); }
        void DockCancel() => cancellation?.Cancel();
        Host.Dock.Begin(label);
        Host.Dock.CancelRequested += DockCancel;
        Timeline.Begin(label, BuildKitCommand.Format(args));
        try
        {
            var progress = new Progress<DockerOutput>(chunk => { foreach (var line in (chunk.IsError ? errors : output).Push(chunk.Text)) Deliver(line); });
            var result = workingDirectory == null
                ? await Host.Docker.ExecuteAsync(args, context, progress, cancellation.Token)
                : await RunInFolderAsync(args, context, workingDirectory, progress, cancellation.Token);
            if (output.Flush() is { } a) Deliver(a);
            if (errors.Flush() is { } b) Deliver(b);
            Host.Dock.End(result.Success, result.Cancelled, result.ExitCode);
            Timeline.End(result.Success, result.Cancelled, result.ExitCode);
            var status = result.Cancelled ? "canceled" : result.Success ? "completed" : "failed, exit " + result.ExitCode;
            if (!Host.IsCapture) Host.History.Record("operation", $"{label} · {status} · context {context ?? "current"}");
            BuildSessionHistory.Add(new BuildSessionEntry(label, reference, started, watch.Elapsed.TotalSeconds,
                result.Cancelled ? "Canceled" : result.Success ? "Completed" : "Failed", Timeline.Progress.ResultDigest ?? "",
                Timeline.Progress.CachedCount, Timeline.Progress.InstructionCount, context ?? "") { DockerfilePath = dockerfilePath, DockerfileSource = dockerfileSource });
            if (result.Cancelled) Host.Notify.Warning(Loc.Voice($"{label} was canceled. Refresh before retrying.", "Stopped.", "停咗。"));
            else if (result.Success) Host.Notify.Success(Loc.Voice($"{label} completed.", "Done!", "搞掂！"));
            else Host.Notify.Error(Loc.Voice($"{label} failed with exit code {result.ExitCode}. The timeline and output panel have the details.", "That did not work.", "出事喇。"));
            return result;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or ArgumentException or IOException or InvalidOperationException or NotSupportedException)
        {
            Host.Dock.End(false, false, -1);
            Host.Dock.Append(e.Message);
            Timeline.End(false, false, -1);
            Host.Notify.Error(label + ": " + (e is System.ComponentModel.Win32Exception ? "the docker command was not found. Install Docker or add it to PATH, then run Diagnostics." : e.Message));
            return null;
        }
        finally
        {
            Host.Dock.CancelRequested -= DockCancel;
            cancellation.Dispose(); cancellation = null;
            active = null;
            ActiveChanged?.Invoke(false);
            Host.RaiseOperationEnded();
        }
    }

    /// <summary>Background read in a working folder (used for bake --print and --list); writes nothing to the dock.</summary>
    public static async Task<CommandResult> ReadInFolderAsync(IReadOnlyList<string> args, string? context, string workingDirectory, CancellationToken token = default)
    {
        try { return await RunInFolderAsync(args, context, workingDirectory, null, token); }
        catch (System.ComponentModel.Win32Exception) { return new CommandResult(-1, "", "The docker command was not found. Install Docker or add it to PATH."); }
    }

    /// <summary>
    /// Same validation as <see cref="DockerService.ExecuteAsync"/> (bound context, no control characters, never a
    /// global context change), but with a working directory.
    /// </summary>
    private static Task<CommandResult> RunInFolderAsync(IReadOnlyList<string> args, string? context, string workingDirectory, IProgress<DockerOutput>? progress, CancellationToken token)
    {
        if (!Directory.Exists(workingDirectory)) throw new DirectoryNotFoundException(Loc.T("The working folder does not exist:") + " " + workingDirectory);
        if (args.Count >= 2 && args[0] == "context" && args[1] == "use") throw new ArgumentException("Global context changes are not supported.");
        var full = new List<string>();
        if (!string.IsNullOrWhiteSpace(context)) { DockerService.ValidateOperand(context, nameof(context)); full.AddRange(["--context", context]); }
        foreach (var arg in args)
        {
            if (arg.IndexOfAny(['\0', '\r', '\n']) >= 0) throw new ArgumentException("Control characters are not allowed.");
            full.Add(arg);
        }
        return new DockerProcessRunner().RunAsync("docker", full, workingDirectory, progress, token);
    }
}

/// <summary>A build started from this application in this session.</summary>
public sealed record BuildSessionEntry(string Label, string Reference, DateTimeOffset Started, double Seconds, string Status, string Digest, int Cached, int Instructions, string Context)
{
    /// <summary>The saved Dockerfile the build used; empty for bakes and other calls.</summary>
    public string DockerfilePath { get; init; } = "";
    /// <summary>The Dockerfile text at the time of the build (memory only, like the rest of the session history).</summary>
    public string DockerfileSource { get; init; } = "";
}

/// <summary>Builds and bakes started in this session, newest first (memory only, never persisted).</summary>
public static class BuildSessionHistory
{
    private static readonly List<BuildSessionEntry> entries = new();
    public static IReadOnlyList<BuildSessionEntry> Entries => entries;
    public static event Action? Changed;

    public static void Add(BuildSessionEntry entry)
    {
        entries.Insert(0, entry);
        if (entries.Count > 100) entries.RemoveAt(entries.Count - 1);
        Changed?.Invoke();
    }
}
