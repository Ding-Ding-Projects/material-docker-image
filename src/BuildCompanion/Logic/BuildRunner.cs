using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCompanion;

public sealed record LogLine(bool IsError, string Text);

public sealed record RunResult(RunState State, int? ExitCode, TimeSpan Elapsed, string Message);

/// <summary>Runs a plan, streams output live, kills the whole process tree on cancel and maps the real exit code to a state.</summary>
public sealed class BuildRunner
{
    readonly Func<PlanStep, string?> launcher;

    /// <param name="launcher">Starts a detached launch step and returns an error message, or null when started. Injected so tests never open a window.</param>
    public BuildRunner(Func<PlanStep, string?>? launcher = null) => this.launcher = launcher ?? DefaultLauncher;

    public async Task<RunResult> RunAsync(BuildPlan plan, Action<LogLine> onLine, Action<PlanStep>? onStep, CancellationToken cancel)
    {
        var clock = Stopwatch.StartNew();
        int? lastExit = null;
        foreach (var step in plan.Steps)
        {
            onStep?.Invoke(step);
            if (cancel.IsCancellationRequested) return new RunResult(RunState.Cancelled, lastExit, clock.Elapsed, "Cancelled before " + step.Label + ".");
            if (step.IsLaunch)
            {
                var error = File.Exists(step.FileName) ? launcher(step) : "The application was not found at " + step.FileName + ".";
                if (error is not null)
                {
                    onLine(new LogLine(true, error));
                    return new RunResult(RunStates.FromExit(lastExit, false, true), lastExit, clock.Elapsed, error);
                }
                onLine(new LogLine(false, "Started " + step.FileName));
                continue;
            }
            var (exit, started, cancelled) = await RunProcessAsync(step, onLine, cancel).ConfigureAwait(false);
            if (!started) return new RunResult(RunState.LaunchFailed, null, clock.Elapsed, "Could not start " + step.FileName + ".");
            lastExit = exit;
            if (cancelled) return new RunResult(RunState.Cancelled, exit, clock.Elapsed, "Cancelled while running " + step.Label + ".");
            if (exit != 0) return new RunResult(RunStates.FromExit(exit, false, false), exit, clock.Elapsed, step.Label + " exited with code " + exit + ".");
        }
        return new RunResult(RunStates.FromExit(lastExit, false, false), lastExit, clock.Elapsed, "Completed.");
    }

    static async Task<(int? Exit, bool Started, bool Cancelled)> RunProcessAsync(PlanStep step, Action<LogLine> onLine, CancellationToken cancel)
    {
        var info = new ProcessStartInfo(step.FileName, step.Arguments)
        {
            WorkingDirectory = step.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        using var process = new Process { StartInfo = info };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) onLine(new LogLine(false, e.Data)); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onLine(new LogLine(true, e.Data)); };
        try { if (!process.Start()) return (null, false, false); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            onLine(new LogLine(true, ex.Message));
            return (null, false, false);
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var cancelled = false;
        try { await process.WaitForExitAsync(cancel).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            cancelled = true;
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await Task.Run(() => process.WaitForExit()).ConfigureAwait(false);
        }
        process.WaitForExit(); // flushes the asynchronous output readers
        return (process.ExitCode, true, cancelled);
    }

    static string? DefaultLauncher(PlanStep step)
    {
        try
        {
            using var started = Process.Start(new ProcessStartInfo(step.FileName) { WorkingDirectory = step.WorkingDirectory, UseShellExecute = false });
            return started is null ? "The application did not start." : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return ex.Message; }
    }
}
