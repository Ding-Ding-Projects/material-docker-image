using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MaterialDockerImage.Services;

namespace MaterialDockerImage.Pages;

/// <summary>One BuildKit Dockerfile step as reported by a real run: the bracketed stage label, its position and result.</summary>
/// <param name="Stage">The stage label inside the brackets ("deps", "stage-1"); null when BuildKit printed none (single-stage files).</param>
/// <param name="Status">"cached", "done", "error", "canceled" or "running".</param>
public sealed record BuildStepFact(string? Stage, int Index, int Total, string Status, double? Seconds);

/// <summary>
/// A finished build of the Build editor's Dockerfile in this session. <see cref="SourceHash"/> is the hash of the
/// editor text when the build started, so footers can be hidden as soon as the text differs.
/// </summary>
public sealed record CanvasBuildRun(string SourceHash, string Target, DateTimeOffset FinishedAt, bool Success, bool Cancelled,
    IReadOnlyList<BuildStepFact> Steps, string Reference, string Context)
{
    /// <summary>Size of the built image in bytes from docker image inspect, when it was read after the build.</summary>
    public long? ImageBytes { get; init; }
}

/// <summary>The footer a stage card shows for the latest build: short text and a longer explanation.</summary>
public sealed record StageBuildFooter(string Text, string Detail, string Outcome);

/// <summary>
/// Framework-free mapping from a real BuildKit run to Canvas stage footers ("48 MB · cached · from the build at 14:02").
/// Steps are matched to stages by the stage label BuildKit prints ("[deps 2/4]", "[stage-1 1/3]", or no label when the
/// Dockerfile has one stage). Nothing is shown when the Dockerfile text changed since that build.
/// </summary>
public static class CanvasBuildFooters
{
    /// <summary>The latest finished build of the Build editor's Dockerfile in this session (memory only).</summary>
    public static CanvasBuildRun? Latest { get; private set; }
    public static event Action? Changed;

    public static void Record(CanvasBuildRun run) { Latest = run; Changed?.Invoke(); }

    /// <summary>Stores the image size read after the build, only when it belongs to the run that is still the latest.</summary>
    public static void SetImageBytes(CanvasBuildRun run, long bytes)
    {
        if (!ReferenceEquals(Latest, run)) return;
        Latest = run with { ImageBytes = bytes };
        Changed?.Invoke();
    }

    /// <summary>Every template used here, for the Cantonese coverage check.</summary>
    public static readonly string[] Templates =
    [
        "cached", "failed at step {0}", "canceled", "{0} of {1} steps cached", "{0} built image", "from the build at {0}",
        "Results BuildKit reported for this stage in the build at {0}. The Dockerfile text has not changed since that build.",
    ];

    /// <summary>Hash of Dockerfile text with line endings normalized, so a CRLF round trip does not hide the footers.</summary>
    public static string Hash(string source)
    {
        var bytes = Encoding.UTF8.GetBytes(source.Replace("\r\n", "\n"));
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    /// <summary>True when a BuildKit stage label belongs to the stage at <paramref name="index"/> named <paramref name="name"/>.</summary>
    public static bool Matches(string? label, int index, string name, int stageCount) =>
        label == null ? stageCount == 1
        : string.Equals(label, name, StringComparison.OrdinalIgnoreCase) || string.Equals(label, "stage-" + index.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    /// <summary>
    /// Footers keyed by stage index for the stages that took part in <paramref name="run"/>. Empty when there is no run
    /// or when <paramref name="currentSource"/> is not the text that was built.
    /// </summary>
    public static IReadOnlyDictionary<int, StageBuildFooter> Map(IReadOnlyList<(int Index, string Name)> stages, CanvasBuildRun? run, string currentSource)
    {
        var result = new Dictionary<int, StageBuildFooter>();
        if (run == null || stages.Count == 0 || Hash(currentSource) != run.SourceHash) return result;
        var target = stages.FirstOrDefault(s => run.Target.Length > 0 && string.Equals(s.Name, run.Target, StringComparison.OrdinalIgnoreCase));
        var targetIndex = run.Target.Length > 0 && target.Name != null ? target.Index : stages[^1].Index;
        var at = run.FinishedAt.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
        var atFull = run.FinishedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        foreach (var (index, name) in stages)
        {
            var steps = run.Steps.Where(s => Matches(s.Stage, index, name, stages.Count)).ToList();
            if (steps.Count == 0) continue;
            var cached = steps.Count(s => s.Status == "cached");
            var parts = new List<string>();
            string outcome;
            if (steps.FirstOrDefault(s => s.Status == "error") is { } failed)
            {
                outcome = "failed";
                parts.Add(F("failed at step {0}", failed.Index + "/" + failed.Total));
            }
            else if (steps.Any(s => s.Status is "canceled" or "running"))
            {
                outcome = "canceled";
                parts.Add(Loc.T("canceled"));
            }
            else
            {
                outcome = cached == steps.Count ? "cached" : "done";
                if (index == targetIndex && run.Success && run.ImageBytes is { } bytes) parts.Add(F("{0} built image", FormatBytes(bytes)));
                if (cached == steps.Count) parts.Add(Loc.T("cached"));
                else
                {
                    var seconds = steps.Where(s => s.Status != "cached").Sum(s => s.Seconds ?? 0);
                    parts.Add(FormatSeconds(seconds));
                    if (cached > 0) parts.Add(F("{0} of {1} steps cached", cached.ToString(CultureInfo.InvariantCulture), steps.Count.ToString(CultureInfo.InvariantCulture)));
                }
            }
            parts.Add(F("from the build at {0}", at));
            var detail = F("Results BuildKit reported for this stage in the build at {0}. The Dockerfile text has not changed since that build.", atFull);
            result[index] = new StageBuildFooter(string.Join(" · ", parts), detail, outcome);
        }
        return result;
    }

    private static string F(string template, params string[] values)
    {
        var text = Loc.T(template);
        for (var i = 0; i < values.Length; i++) text = text.Replace("{" + i + "}", values[i]);
        return text;
    }

    /// <summary>Same decimal units as the rest of the application (1 MB = 1,000,000 bytes).</summary>
    public static string FormatBytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var i = 0; while (bytes >= 1000 && i < units.Length - 1) { bytes /= 1000; i++; }
        return i == 0 ? bytes.ToString("0", CultureInfo.InvariantCulture) + " " + units[i] : bytes.ToString("0.#", CultureInfo.InvariantCulture) + " " + units[i];
    }

    public static string FormatSeconds(double seconds) => seconds < 60
        ? seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s"
        : $"{(int)(seconds / 60)} min {(int)(seconds % 60):00} s";
}
