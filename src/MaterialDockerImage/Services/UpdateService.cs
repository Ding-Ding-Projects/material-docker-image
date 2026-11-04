using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Services;

public enum UpdateState { Idle, Skipped, Checking, UpToDate, Downloading, Ready, Failed }

/// <summary>
/// Background update check against the published Squirrel feed. It only acts when the app was installed by Squirrel
/// (an Update.exe beside the app folder), never restarts the app by itself, and reports every outcome in plain words.
/// The comparison and feed parsing live in <see cref="UpdateFeed"/>.
/// </summary>
public sealed class UpdateService
{
    public const string FeedBase = "https://github.com/Ding-Ding-Projects/material-docker-image/releases/latest/download";
    public const string PackageId = "MaterialDockerImage";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly Func<bool> enabled;
    private bool running;
    public UpdateState State { get; private set; } = UpdateState.Idle;
    public string Message { get; private set; } = "No update check has run in this session.";
    public string ReadyVersion { get; private set; } = "";
    public event Action? Changed;

    public UpdateService(Func<bool> enabled) { this.enabled = enabled; }

    /// <summary>The Squirrel updater when this copy is an installed one; null for a portable, development or captured run.</summary>
    public static string? UpdaterPath()
    {
        var baseDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Path.GetFileName(baseDirectory).StartsWith("app-", StringComparison.OrdinalIgnoreCase)) return null;
        var updater = Path.GetFullPath(Path.Combine(baseDirectory, "..", "Update.exe"));
        return File.Exists(updater) ? updater : null;
    }

    public static string RunningVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "";
        return UpdateFeed.CleanRunningVersion(informational);
    }

    private void Set(UpdateState state, string message) { State = state; Message = message; Changed?.Invoke(); }

    /// <summary>Checks and stages an update. <paramref name="manual"/> ignores the setting so a person can always ask.</summary>
    public async Task CheckAsync(bool manual, CancellationToken token = default)
    {
        if (running) return;
        if (!manual && !enabled()) { Set(UpdateState.Skipped, "Automatic update checks are turned off in Settings."); return; }
        var updater = UpdaterPath();
        if (updater == null) { Set(UpdateState.Skipped, "This copy was not installed by the installer (portable or development run), so it does not update itself."); return; }
        running = true;
        try
        {
            Set(UpdateState.Checking, "Checking for a newer version…");
            string text;
            using (var response = await Http.GetAsync(FeedBase + "/RELEASES", HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > UpdateFeed.MaxFeedChars) throw new InvalidDataException("The update feed is larger than the 256 KB limit.");
                text = await response.Content.ReadAsStringAsync(token);
            }
            var decision = UpdateFeed.Decide(RunningVersion(), PackageId, UpdateFeed.ParseReleases(text));
            if (!decision.UpdateAvailable) { Set(UpdateState.UpToDate, decision.Message); return; }
            Set(UpdateState.Downloading, "Downloading version " + decision.NewestVersion + " in the background…");
            var start = new ProcessStartInfo(updater) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--update=" + FeedBase);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("The updater could not be started.");
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
            limit.CancelAfter(TimeSpan.FromMinutes(10));
            try { await process.WaitForExitAsync(limit.Token); }
            catch (OperationCanceledException) { try { process.Kill(); } catch (InvalidOperationException) { } throw; }
            if (process.ExitCode != 0) throw new InvalidOperationException("The updater exited with code " + process.ExitCode + ".");
            ReadyVersion = decision.NewestVersion;
            Set(UpdateState.Ready, "Version " + decision.NewestVersion + " is ready. Restart when you are done with your work.");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException or InvalidDataException or InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            Set(UpdateState.Failed, "The update check did not finish: " + e.Message + " The current version keeps working.");
        }
        finally { running = false; }
    }

    /// <summary>Starts the newest installed version and returns true; the caller then closes this window. Only ever called from an explicit click.</summary>
    public bool RestartIntoUpdate()
    {
        var updater = UpdaterPath();
        if (updater == null || State != UpdateState.Ready) return false;
        var start = new ProcessStartInfo(updater) { UseShellExecute = false };
        start.ArgumentList.Add("--processStart");
        start.ArgumentList.Add("MaterialDockerImage.exe");
        Process.Start(start)?.Dispose();
        return true;
    }
}
