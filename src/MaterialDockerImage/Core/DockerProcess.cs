using System.Diagnostics;
using System.Text;

namespace MaterialDockerImage.Core;

public sealed record DockerOutput(string Text, bool IsError);
public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError, bool Cancelled = false, bool Truncated = false)
{
    public bool Success => ExitCode == 0 && !Cancelled;
    public void EnsureSuccess() { if (!Success) throw new DockerCommandException(this); }
}
public sealed class DockerCommandException(CommandResult result) : Exception(result.Cancelled ? "Operation cancelled." : $"Docker exited with code {result.ExitCode}: {result.StandardError}")
{
    public CommandResult Result { get; } = result;
}
public interface IDockerProcessRunner
{
    Task<CommandResult> RunAsync(string executable, IReadOnlyList<string> arguments, string? workingDirectory, IProgress<DockerOutput>? output, CancellationToken cancellationToken);
}

public sealed class DockerProcessRunner(int captureLimit = 2 * 1024 * 1024) : IDockerProcessRunner
{
    public async Task<CommandResult> RunAsync(string executable, IReadOnlyList<string> arguments, string? workingDirectory, IProgress<DockerOutput>? output, CancellationToken cancellationToken)
    {
        if (captureLimit < 1024) throw new ArgumentOutOfRangeException(nameof(captureLimit));
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        if (workingDirectory is not null) start.WorkingDirectory = workingDirectory;
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        process.Start();
        var stdout = new StringBuilder(); var stderr = new StringBuilder();
        var truncated = 0;
        async Task DrainAsync(StreamReader reader, StringBuilder destination, bool isError)
        {
            var buffer = new char[4096];
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory())) != 0)
            {
                var text = new string(buffer, 0, read);
                var available = captureLimit - destination.Length;
                if (available > 0) destination.Append(buffer, 0, Math.Min(available, read));
                if (read > available) Interlocked.Exchange(ref truncated, 1);
                // Streams are always drained even after the retained transcript is full.
                output?.Report(new DockerOutput(text, isError));
            }
        }
        var reads = Task.WhenAll(DrainAsync(process.StandardOutput, stdout, false), DrainAsync(process.StandardError, stderr, true));
        bool cancelled = false;
        using var registration = cancellationToken.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } });
        try { await process.WaitForExitAsync(cancellationToken); }
        catch (OperationCanceledException) { cancelled = true; await process.WaitForExitAsync(); }
        await reads;
        return new CommandResult(process.ExitCode, stdout.ToString(), stderr.ToString(), cancelled || cancellationToken.IsCancellationRequested, truncated != 0);
    }
}
