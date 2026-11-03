using System.Globalization;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>
/// An inclusive rectangle of chunks (16 × 16 blocks each). Chunk x covers blocks x*16 to x*16+15.
/// </summary>
public readonly record struct MinecraftChunkArea(int MinChunkX, int MinChunkZ, int MaxChunkX, int MaxChunkZ)
{
    public const int ChunkSize = 16;
    public int WidthChunks => MaxChunkX - MinChunkX + 1;
    public int DepthChunks => MaxChunkZ - MinChunkZ + 1;
    public long ChunkCount => (long)WidthChunks * DepthChunks;
    public int WidthBlocks => WidthChunks * ChunkSize;
    public int DepthBlocks => DepthChunks * ChunkSize;
    /// <summary>Block coordinate at the centre of the rectangle (the boundary between the two middle chunks for even sizes).</summary>
    public int CentreBlockX => (MinChunkX * ChunkSize + (MaxChunkX + 1) * ChunkSize) / 2;
    public int CentreBlockZ => (MinChunkZ * ChunkSize + (MaxChunkZ + 1) * ChunkSize) / 2;
    /// <summary>Half-width in blocks along X and Z (the values Chunky's radius command takes).</summary>
    public int RadiusBlocksX => WidthBlocks / 2;
    public int RadiusBlocksZ => DepthBlocks / 2;
    public bool IsSquare => WidthChunks == DepthChunks;

    /// <summary>A rectangle of the given size in chunks, centred on a chunk coordinate (the default is 100 × 100 around 0, 0).</summary>
    public static MinecraftChunkArea Centred(int widthChunks, int depthChunks, int centreChunkX = 0, int centreChunkZ = 0)
    {
        widthChunks = Math.Max(1, widthChunks); depthChunks = Math.Max(1, depthChunks);
        var minX = centreChunkX - widthChunks / 2;
        var minZ = centreChunkZ - depthChunks / 2;
        return new MinecraftChunkArea(minX, minZ, minX + widthChunks - 1, minZ + depthChunks - 1);
    }

    /// <summary>Normalises two corner chunks dragged in any direction into a rectangle.</summary>
    public static MinecraftChunkArea FromCorners(int ax, int az, int bx, int bz) =>
        new(Math.Min(ax, bx), Math.Min(az, bz), Math.Max(ax, bx), Math.Max(az, bz));

    public static MinecraftChunkArea Default => Centred(100, 100);

    /// <summary>RCON commands that configure and start Chunky for this area. Without a world name Chunky uses its default world.</summary>
    public IReadOnlyList<string> ChunkyCommands(string? world = null)
    {
        var list = new List<string>
        {
            FormattableString.Invariant($"chunky center {CentreBlockX} {CentreBlockZ}"),
            "chunky shape " + (IsSquare ? "square" : "rectangle"),
            IsSquare ? FormattableString.Invariant($"chunky radius {RadiusBlocksX}") : FormattableString.Invariant($"chunky radius {RadiusBlocksX} {RadiusBlocksZ}"),
            "chunky start"
        };
        if (!string.IsNullOrWhiteSpace(world)) list.Insert(0, "chunky world " + world);
        return list;
    }
}

/// <summary>Parsers for server output used by the Minecraft page (players, Chunky progress, file listings).</summary>
public static class MinecraftOutput
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex ListLine = new(@"There are (\d+) of a max(?: of)? (\d+) players online:?\s*(.*)$", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex Progress = new(@"\[Chunky\].*?Processed:?\s*([\d,]+) chunks \((\d+(?:\.\d+)?)%\)(?:, ETA: ([\d:]+))?", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex Done = new(@"\[Chunky\].*Task finished for (\S+)", RegexOptions.CultureInvariant, Timeout);

    /// <summary>Parses the reply of the vanilla "list" command. Returns null when the text is not a list reply.</summary>
    public static (int Online, int Max, IReadOnlyList<string> Names)? ParseList(string text)
    {
        var clean = StripFormatting(text).Trim();
        var m = ListLine.Match(clean);
        if (!m.Success) return null;
        var names = m.Groups[3].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        return (int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), names);
    }

    /// <summary>Parses Chunky progress lines ("Processed: 1234 chunks (12.34%), ETA: 0:01:23"). Returns null for other lines.</summary>
    public static (long Chunks, double Percent, string? Eta)? ParseChunkyProgress(string line)
    {
        var m = Progress.Match(line);
        if (!m.Success) return null;
        var chunks = long.Parse(m.Groups[1].Value.Replace(",", ""), CultureInfo.InvariantCulture);
        var percent = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        return (chunks, percent, m.Groups[3].Success ? m.Groups[3].Value : null);
    }

    public static bool IsChunkyFinished(string line) => Done.IsMatch(line);

    /// <summary>Removes Minecraft section-sign colour codes and ANSI colour sequences from command replies.</summary>
    public static string StripFormatting(string text) => Regex.Replace(text, "§.|\u001b\\[[0-9;]*m", "", RegexOptions.None, Timeout);

    /// <summary>Names from ops.json or whitelist.json (arrays of objects with a "name" field). Empty on any problem.</summary>
    public static IReadOnlyList<string> NamesFromJson(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return [];
            return doc.RootElement.EnumerateArray()
                .Where(e => e.ValueKind == System.Text.Json.JsonValueKind.Object && e.TryGetProperty("name", out var n) && n.ValueKind == System.Text.Json.JsonValueKind.String)
                .Select(e => e.GetProperty("name").GetString()!).ToList();
        }
        catch (System.Text.Json.JsonException) { return []; }
    }

    /// <summary>One entry of "ls -la" output from busybox or GNU ls.</summary>
    public sealed record FileEntry(string Name, bool IsDirectory, long Size, string Modified, string Permissions);

    /// <summary>Parses "ls -la" output. Skips the total line and the . and .. entries. Symlink targets are dropped.</summary>
    public static List<FileEntry> ParseLs(string text)
    {
        var list = new List<FileEntry>();
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0 || line.StartsWith("total ", StringComparison.Ordinal)) continue;
            var parts = line.Split(' ', 9, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 9 || parts[0].Length < 10) continue;
            var name = parts[8];
            var arrow = name.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow > 0) name = name[..arrow];
            if (name is "." or "..") continue;
            long.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var size);
            list.Add(new FileEntry(name, parts[0][0] == 'd', size, $"{parts[5]} {parts[6]} {parts[7]}", parts[0]));
        }
        return list;
    }
}
