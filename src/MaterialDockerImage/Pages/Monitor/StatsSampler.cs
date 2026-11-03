using System.Text.Json;
using MaterialDockerImage.Core;

namespace MaterialDockerImage.Pages;

/// <summary>One real reading of one container from <c>docker container stats --no-stream</c>.</summary>
public sealed record StatsSample(
    DateTimeOffset Time, string Id, string Name,
    double? CpuPercent, double? MemoryBytes, double? MemoryLimitBytes, double? MemoryPercent,
    double? NetIn, double? NetOut, double? BlockRead, double? BlockWrite, long? Pids,
    string CpuText, string MemText, string NetText, string BlockText, string PidsText)
{
    public static StatsSample Parse(JsonElement row, DateTimeOffset when)
    {
        var mem = CliValues.Str(row, "MemUsage");
        var net = CliValues.Str(row, "NetIO");
        var block = CliValues.Str(row, "BlockIO");
        var pids = CliValues.Str(row, "PIDs");
        var (memUsed, memLimit) = CliValues.ParsePair(mem);
        var (netIn, netOut) = CliValues.ParsePair(net);
        var (blockRead, blockWrite) = CliValues.ParsePair(block);
        var id = CliValues.Str(row, "ID");
        if (id.Length == 0) id = CliValues.Str(row, "Container");
        var name = CliValues.Str(row, "Name");
        if (name.Length == 0) name = id;
        return new StatsSample(when, id, name,
            CliValues.ParsePercent(CliValues.Str(row, "CPUPerc")), memUsed, memLimit, CliValues.ParsePercent(CliValues.Str(row, "MemPerc")),
            netIn, netOut, blockRead, blockWrite, long.TryParse(pids, out var p) ? p : null,
            CliValues.Str(row, "CPUPerc"), mem, net, block, pids);
    }
}

/// <summary>
/// Keeps the last <see cref="Capacity"/> real samples per container, keyed by container name.
/// It never fills gaps or extrapolates; a missed poll simply leaves no point.
/// </summary>
public sealed class StatsHistory
{
    public const int Capacity = 120;
    private readonly Dictionary<string, List<StatsSample>> series = new(StringComparer.Ordinal);
    public DateTimeOffset? LastSuccess { get; private set; }
    public DateTimeOffset? LastAttempt { get; private set; }
    public string? LastError { get; private set; }
    /// <summary>Names reported by the most recent successful sample (the running set).</summary>
    public IReadOnlyList<string> Current { get; private set; } = [];

    public IReadOnlyList<StatsSample> For(string name) => series.TryGetValue(name, out var list) ? list : [];
    public IEnumerable<string> Known => series.Keys;

    public void Add(IReadOnlyList<StatsSample> samples, DateTimeOffset when)
    {
        LastAttempt = when; LastSuccess = when; LastError = null;
        foreach (var s in samples)
        {
            if (!series.TryGetValue(s.Name, out var list)) series[s.Name] = list = new();
            list.Add(s);
            if (list.Count > Capacity) list.RemoveRange(0, list.Count - Capacity);
        }
        Current = samples.Select(s => s.Name).ToList();
    }

    public void Fail(string error, DateTimeOffset when) { LastAttempt = when; LastError = error; }

    public void Clear() { series.Clear(); Current = []; LastSuccess = null; LastAttempt = null; LastError = null; }
}

/// <summary>Runs one stats read for all running containers in the bound engine context.</summary>
public static class StatsReader
{
    public static async Task<IReadOnlyList<StatsSample>> ReadAsync(string? context, CancellationToken token)
    {
        var host = Services.AppHost.Current;
        var rows = await host.ReadRows(["container", "stats", "--no-stream", "--format", "{{json .}}"], context, token);
        var now = DateTimeOffset.Now;
        return rows.Select(r => StatsSample.Parse(r, now)).Where(s => s.Name.Length > 0).ToList();
    }
}
