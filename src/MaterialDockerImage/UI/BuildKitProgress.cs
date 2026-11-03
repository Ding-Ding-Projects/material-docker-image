using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

public enum BuildKitStepStatus { Running, Cached, Done, Error, Canceled }

/// <summary>One BuildKit vertex as reported by <c>--progress=plain</c>.</summary>
public sealed class BuildKitStep
{
    public const int MaxOutputLines = 400;
    private readonly List<string> output = new();

    public int Id { get; init; }
    /// <summary>The vertex text after the bracketed label (for example "RUN npm ci").</summary>
    public string Name { get; set; } = "";
    /// <summary>The full header as printed (for example "[deps 2/4] RUN npm ci").</summary>
    public string Header { get; set; } = "";
    /// <summary>Stage or label inside the brackets (for example "deps", "internal", "auth"); null when absent.</summary>
    public string? Stage { get; set; }
    public int? Index { get; set; }
    public int? Total { get; set; }
    public BuildKitStepStatus Status { get; set; } = BuildKitStepStatus.Running;
    /// <summary>Duration reported by BuildKit (DONE x.ys) or measured when the step closed otherwise.</summary>
    public double? Seconds { get; set; }
    /// <summary>Clock reading (seconds since the run started) when the step first appeared.</summary>
    public double StartedAt { get; set; }
    public string? Error { get; set; }
    /// <summary>True for Dockerfile instructions (labels with an index such as 2/4).</summary>
    public bool IsInstruction => Index != null;
    public bool IsInternal => string.Equals(Stage, "internal", StringComparison.Ordinal) || string.Equals(Stage, "auth", StringComparison.Ordinal);
    public IReadOnlyList<string> Output => output;
    /// <summary>Number of output lines dropped because the bounded buffer was full.</summary>
    public int DroppedLines { get; private set; }

    internal void AddOutput(string line)
    {
        output.Add(line);
        if (output.Count > MaxOutputLines) { output.RemoveAt(0); DroppedLines++; }
    }

    /// <summary>Elapsed seconds: the reported duration, or the live time for a running step.</summary>
    public double Elapsed(double now) => Seconds ?? Math.Max(0, now - StartedAt);
}

/// <summary>
/// Incremental parser for BuildKit plain progress output. Feed it whole lines in order; it keeps one
/// <see cref="BuildKitStep"/> per vertex id and records the exported image digest and names. Lines that do not
/// belong to a vertex (the failure summary, warnings) are kept in <see cref="GeneralLines"/>.
/// </summary>
public sealed class BuildKitProgress
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex VertexLine = new(@"^#(?<id>\d+) (?<rest>.*)$", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex Done = new(@"^DONE (?<s>\d+(?:\.\d+)?)s$", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex Timed = new(@"^(?<t>\d+\.\d+) (?<text>.*)$", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex Label = new(@"^\[(?<label>[^\]]+)\]\s*(?<text>.*)$", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex Position = new(@"^(?:(?<stage>.+) )?(?<i>\d+)/(?<n>\d+)$", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex Digest = new(@"sha256:[0-9a-f]{64}", RegexOptions.CultureInvariant, Timeout);
    public const int MaxGeneralLines = 400;

    private readonly Dictionary<int, BuildKitStep> byId = new();
    private readonly List<BuildKitStep> steps = new();
    private readonly List<string> general = new();
    private readonly List<string> names = new();

    public IReadOnlyList<BuildKitStep> Steps => steps;
    public IReadOnlyList<string> GeneralLines => general;
    /// <summary>Image references reported by "naming to".</summary>
    public IReadOnlyList<string> ImageNames => names;
    /// <summary>The manifest list, manifest or image digest the exporter reported, preferring the manifest list.</summary>
    public string? ResultDigest { get; private set; }
    /// <summary>The "ERROR: failed to solve" summary line, when the build failed.</summary>
    public string? FailureSummary { get; private set; }
    private int digestRank;
    /// <summary>The builder banner (for example "\"default\" instance using docker driver").</summary>
    public string? Builder { get; private set; }

    public int CachedCount => steps.Count(s => s.Status == BuildKitStepStatus.Cached && s.IsInstruction);
    public int InstructionCount => steps.Count(s => s.IsInstruction);
    public double LongestSeconds(double now) => steps.Count == 0 ? 0 : steps.Max(s => s.Elapsed(now));

    /// <summary>Parses one line. Returns the step the line belongs to, or null for a general line.</summary>
    public BuildKitStep? Feed(string line, double now = 0)
    {
        if (line.Length == 0) return null;
        var match = VertexLine.Match(line);
        var id = -1;
        if (!match.Success || !int.TryParse(match.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out id) || id == 0)
        {
            // #0 is the "building with … instance" banner rather than a vertex.
            if (id == 0 && match.Groups["rest"].Value.StartsWith("building with ", StringComparison.Ordinal)) Builder = match.Groups["rest"].Value[14..];
            AddGeneral(line);
            return null;
        }
        var rest = match.Groups["rest"].Value;
        if (!byId.TryGetValue(id, out var step))
        {
            step = new BuildKitStep { Id = id, StartedAt = now };
            SetHeader(step, rest);
            byId[id] = step;
            steps.Add(step);
            return step;
        }
        if (rest == step.Header) return step; // BuildKit repeats the header when it switches back to a vertex.
        if (rest == "CACHED") { step.Status = BuildKitStepStatus.Cached; step.Seconds ??= 0; return step; }
        if (rest == "CANCELED") { Close(step, BuildKitStepStatus.Canceled, now); return step; }
        if (rest.StartsWith("ERROR: ", StringComparison.Ordinal) || rest == "ERROR")
        {
            step.Error = rest.Length > 7 ? rest[7..] : "";
            Close(step, BuildKitStepStatus.Error, now);
            step.AddOutput(rest);
            return step;
        }
        var done = Done.Match(rest);
        if (done.Success)
        {
            if (step.Status != BuildKitStepStatus.Cached) step.Status = BuildKitStepStatus.Done;
            step.Seconds = double.Parse(done.Groups["s"].Value, CultureInfo.InvariantCulture);
            return step;
        }
        var timed = Timed.Match(rest);
        var text = timed.Success ? timed.Groups["text"].Value : rest;
        step.AddOutput(text);
        ObserveExport(text);
        return step;
    }

    /// <summary>Marks steps that never closed once the process has ended.</summary>
    public void Finish(bool success, bool cancelled, double now)
    {
        foreach (var step in steps.Where(s => s.Status == BuildKitStepStatus.Running))
            Close(step, success ? BuildKitStepStatus.Done : BuildKitStepStatus.Canceled, now);
    }

    /// <summary>All lines that belong to one step, prefixed like the raw log, for filtering a log view.</summary>
    public static string StepText(BuildKitStep step)
    {
        var sb = new StringBuilder();
        sb.Append('#').Append(step.Id).Append(' ').AppendLine(step.Header);
        if (step.DroppedLines > 0) sb.Append('#').Append(step.Id).Append(" … ").Append(step.DroppedLines).AppendLine(" earlier lines not kept");
        foreach (var line in step.Output) sb.Append('#').Append(step.Id).Append(' ').AppendLine(line);
        sb.Append('#').Append(step.Id).Append(' ').Append(step.Status switch
        {
            BuildKitStepStatus.Cached => "CACHED",
            BuildKitStepStatus.Done => "DONE " + (step.Seconds ?? 0).ToString("0.0", CultureInfo.InvariantCulture) + "s",
            BuildKitStepStatus.Error => "ERROR",
            BuildKitStepStatus.Canceled => "CANCELED",
            _ => "running"
        });
        return sb.ToString();
    }

    private static void Close(BuildKitStep step, BuildKitStepStatus status, double now)
    {
        step.Status = status;
        step.Seconds ??= Math.Max(0, now - step.StartedAt);
    }

    private static void SetHeader(BuildKitStep step, string header)
    {
        step.Header = header;
        step.Name = header;
        var label = Label.Match(header);
        if (!label.Success) return;
        step.Name = label.Groups["text"].Value;
        var inside = label.Groups["label"].Value.Trim();
        var position = Position.Match(inside);
        if (position.Success)
        {
            step.Index = int.Parse(position.Groups["i"].Value, CultureInfo.InvariantCulture);
            step.Total = int.Parse(position.Groups["n"].Value, CultureInfo.InvariantCulture);
            step.Stage = position.Groups["stage"].Success && position.Groups["stage"].Value.Length > 0 ? position.Groups["stage"].Value : null;
        }
        else step.Stage = inside;
    }

    private void ObserveExport(string text)
    {
        if (text.StartsWith("naming to ", StringComparison.Ordinal))
        {
            var name = text[10..];
            if (name.EndsWith(" done", StringComparison.Ordinal)) name = name[..^5];
            name = name.Trim();
            if (name.Length > 0 && !names.Contains(name)) names.Add(name);
            return;
        }
        var rank = text.StartsWith("exporting manifest list ", StringComparison.Ordinal) ? 3
            : text.StartsWith("exporting manifest ", StringComparison.Ordinal) ? 2
            : text.StartsWith("writing image ", StringComparison.Ordinal) ? 1 : 0;
        if (rank == 0 || rank < digestRank) return;
        var digest = Digest.Match(text);
        if (!digest.Success) return;
        ResultDigest = digest.Value;
        digestRank = rank;
    }

    private void AddGeneral(string line)
    {
        general.Add(line);
        if (general.Count > MaxGeneralLines) general.RemoveAt(0);
        if (line.StartsWith("ERROR: ", StringComparison.Ordinal) && FailureSummary == null) FailureSummary = line[7..];
    }
}

/// <summary>Turns streamed output chunks into whole lines; a trailing partial line waits for the next chunk.</summary>
public sealed class BuildKitLineSplitter
{
    private readonly StringBuilder pending = new();

    public IEnumerable<string> Push(string chunk)
    {
        var lines = new List<string>();
        foreach (var c in chunk)
        {
            if (c == '\n') { lines.Add(pending.ToString().TrimEnd('\r')); pending.Clear(); }
            else pending.Append(c);
        }
        if (pending.Length > 64 * 1024) { lines.Add(pending.ToString()); pending.Clear(); }
        return lines;
    }

    /// <summary>Returns the remaining partial line, if any.</summary>
    public string? Flush()
    {
        if (pending.Length == 0) return null;
        var text = pending.ToString().TrimEnd('\r');
        pending.Clear();
        return text;
    }
}
