using System.Text;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Result of <c>docker scout version</c>. Output is the exact text the command printed.</summary>
internal sealed record ScoutAvailability(bool Available, string Version, string Output, int ExitCode)
{
    public string Command => "docker scout version";
}

/// <summary>One Docker Scout analysis of one local image. Failed analyses keep the exact output; findings are never invented.</summary>
internal sealed record ScoutScanResult(string Reference, string ImageId, string? Context, ScoutState State, IReadOnlyList<ScoutFinding> Findings, string Output, DateTime AnalysedUtc)
{
    public ScoutSeverityCounts Counts { get; } = ScoutReport.Count(Findings);
    public bool Succeeded => State == ScoutState.Ready;
    public string AnalysedText => AnalysedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}

/// <summary>Analyses kept in memory for this session only, keyed by engine context and image ID, shared by Images and Security.</summary>
internal static class ScoutSessionCache
{
    private static readonly Dictionary<string, ScoutScanResult> Results = new(StringComparer.Ordinal);
    public static event Action<ScoutScanResult>? Updated;

    private static string Key(string? context, string imageId) => (context ?? "") + "\n" + imageId;

    public static ScoutScanResult? Get(string? context, string imageId) => Results.TryGetValue(Key(context, imageId), out var r) ? r : null;

    public static void Put(ScoutScanResult result)
    {
        if (result.State == ScoutState.Cancelled) return;
        Results[Key(result.Context, result.ImageId)] = result;
        Updated?.Invoke(result);
    }

    public static IReadOnlyList<ScoutScanResult> ForContext(string? context) =>
        Results.Values.Where(r => r.Context == context).OrderBy(r => r.Reference, StringComparer.Ordinal).ToList();
}

/// <summary>Runs Docker Scout commands. Reports are written to a temporary file with --output so large reports are never truncated.</summary>
internal static class ScoutRunner
{
    private static AppHost Host => AppHost.Current;
    private static ScoutAvailability? availability;

    /// <summary>Checks the Scout plugin. The answer is cached for the session until <paramref name="force"/> is set.</summary>
    public static async Task<ScoutAvailability> CheckAsync(string? context, bool force = false, CancellationToken token = default)
    {
        if (!force && availability is { Available: true } known) return known;
        var result = await Host.Read(["scout", "version"], context, token);
        var output = (result.StandardOutput + "\n" + result.StandardError).Trim();
        var state = ScoutStatus.ClassifyVersion(result.ExitCode, output);
        availability = new ScoutAvailability(state == ScoutState.Ready, ScoutStatus.ParseVersion(result.StandardOutput), ScoutReport.StripAnsi(output), result.ExitCode);
        return availability;
    }

    public static ScoutAvailability? Known => availability;

    public static IReadOnlyList<string> CvesArguments(string reference, string outputPath) => ["scout", "cves", "--format", "sarif", "--output", outputPath, reference];

    /// <summary>Background analysis used by the Security scan queue. It never shows the output panel and honours cancellation.</summary>
    public static async Task<ScoutScanResult> ScanBackgroundAsync(string reference, string imageId, string? context, CancellationToken token)
    {
        var path = TempPath("sarif.json");
        try
        {
            CommandResult result;
            try { result = await Host.Read(CvesArguments(reference, path), context, token); }
            catch (OperationCanceledException) { return new ScoutScanResult(reference, imageId, context, ScoutState.Cancelled, [], "", DateTime.UtcNow); }
            return Interpret(reference, imageId, context, result, path);
        }
        finally { TryDelete(path); }
    }

    /// <summary>Foreground analysis with progress in the output panel. Returns null when the operation could not start.</summary>
    public static async Task<ScoutScanResult?> ScanForegroundAsync(string reference, string imageId, string? context)
    {
        var path = TempPath("sarif.json");
        try
        {
            var result = await Host.Execute("Docker Scout analysis of " + reference, CvesArguments(reference, path), true, context, record: true);
            return result == null ? null : Interpret(reference, imageId, context, result, path);
        }
        finally { TryDelete(path); }
    }

    private static ScoutScanResult Interpret(string reference, string imageId, string? context, CommandResult result, string path)
    {
        var output = ScoutReport.StripAnsi((result.StandardError + "\n" + result.StandardOutput).Trim());
        if (result.Cancelled) return new ScoutScanResult(reference, imageId, context, ScoutState.Cancelled, [], output, DateTime.UtcNow);
        if (!result.Success) return new ScoutScanResult(reference, imageId, context, ScoutStatus.ClassifyFailure(output), [], $"docker scout cves exited with code {result.ExitCode}.\n{output}".Trim(), DateTime.UtcNow);
        if (!File.Exists(path)) return new ScoutScanResult(reference, imageId, context, ScoutState.Failed, [], "docker scout cves reported success but wrote no report file.\n" + output, DateTime.UtcNow);
        try
        {
            var findings = ScoutReport.Sort(ScoutReport.ParseSarif(File.ReadAllText(path, Encoding.UTF8)));
            return new ScoutScanResult(reference, imageId, context, ScoutState.Ready, findings, output, DateTime.UtcNow);
        }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException)
        {
            return new ScoutScanResult(reference, imageId, context, ScoutState.Failed, [], Loc.T("The SARIF report could not be read:") + " " + e.Message, DateTime.UtcNow);
        }
    }

    /// <summary>Generates an SBOM as JSON. Returns the command result and the report text (empty on failure).</summary>
    public static async Task<(CommandResult? Result, string Json)> SbomAsync(string reference, string? context)
    {
        var path = TempPath("sbom.json");
        try
        {
            var result = await Host.Execute("Docker Scout SBOM of " + reference, ["scout", "sbom", "--format", "json", "--output", path, reference], true, context);
            var json = result?.Success == true && File.Exists(path) ? await File.ReadAllTextAsync(path, Encoding.UTF8) : "";
            return (result, json);
        }
        finally { TryDelete(path); }
    }

    /// <summary>Runs docker scout quickview in the background and returns its text output.</summary>
    public static async Task<CommandResult> QuickviewAsync(string reference, string? context, CancellationToken token = default) =>
        await Host.Read(["scout", "quickview", reference], context, token);

    public static string TempPath(string suffix)
    {
        var folder = Path.Combine(Path.GetTempPath(), "MaterialDockerImage");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "scout-" + Guid.NewGuid().ToString("N") + "." + suffix);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Opens an advisory page in the default browser after confirmation. Only https links are opened.</summary>
    public static async Task OpenAdvisoryAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) { Host.Notify.Warning("This finding has no https advisory link."); return; }
        if (!await Host.Overlay.ConfirmAsync("Open advisory", "Opens this page in your default browser:\n\n" + url, "Open")) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { Host.Notify.Error(Loc.T("The browser could not be opened:") + " " + e.Message); }
    }
}
