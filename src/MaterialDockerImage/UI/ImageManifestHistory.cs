using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>One row of <c>docker image history --no-trunc --format "{{json .}}"</c>.</summary>
public sealed record ImageLayerEntry(int Index, string Id, string CreatedBy, string Keyword, string Instruction, long Bytes, string SizeText, string CreatedSince, string CreatedAt, string Comment)
{
    /// <summary>Metadata-only steps (CMD, ENV, LABEL…) add no filesystem content.</summary>
    public bool Empty => Bytes == 0;
}

/// <summary>Parses image history rows and cleans the recorded build instruction for display.</summary>
public static class ImageLayerHistory
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex SizePattern = new(@"^([0-9]+(?:\.[0-9]+)?)\s*([kKMGTP]?i?B)$", RegexOptions.None, RegexTimeout);
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "ADD", "ARG", "CMD", "COPY", "ENTRYPOINT", "ENV", "EXPOSE", "HEALTHCHECK", "LABEL", "MAINTAINER", "ONBUILD", "RUN", "SHELL", "STOPSIGNAL", "USER", "VOLUME", "WORKDIR"
    };

    /// <summary>Parses one JSON object per line (or a JSON array). Rows are returned newest first, as docker prints them.</summary>
    public static IReadOnlyList<ImageLayerEntry> Parse(string output)
    {
        var elements = new List<JsonElement>();
        var trimmed = output.Trim();
        if (trimmed.StartsWith('['))
        {
            using var doc = JsonDocument.Parse(trimmed);
            elements.AddRange(doc.RootElement.EnumerateArray().Select(e => e.Clone()));
        }
        else
        {
            foreach (var line in trimmed.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                using var doc = JsonDocument.Parse(line);
                elements.Add(doc.RootElement.Clone());
            }
        }
        var list = new List<ImageLayerEntry>();
        for (var i = 0; i < elements.Count; i++)
        {
            var e = elements[i];
            var createdBy = ScoutReport.Str(e, "CreatedBy");
            var (keyword, instruction) = Clean(createdBy);
            var sizeText = ScoutReport.Str(e, "Size");
            list.Add(new ImageLayerEntry(i, ScoutReport.Str(e, "ID"), createdBy, keyword, instruction, ParseSize(sizeText), sizeText,
                ScoutReport.Str(e, "CreatedSince"), ScoutReport.Str(e, "CreatedAt"), ScoutReport.Str(e, "Comment")));
        }
        return list;
    }

    /// <summary>
    /// Removes the legacy builder's <c>/bin/sh -c #(nop)</c> prefix and BuildKit's <c># buildkit</c> suffix, and splits the
    /// Dockerfile keyword when one is present. Shell commands recorded without a keyword are shown as RUN.
    /// </summary>
    public static (string Keyword, string Instruction) Clean(string createdBy)
    {
        var text = Regex.Replace(createdBy.Replace("\r", " ").Replace("\n", " ").Replace("\t", " "), @"\s{2,}", " ", RegexOptions.None, RegexTimeout).Trim();
        if (text.EndsWith("# buildkit", StringComparison.Ordinal)) text = text[..^"# buildkit".Length].TrimEnd();
        const string nop = "/bin/sh -c #(nop)";
        if (text.StartsWith(nop, StringComparison.Ordinal)) text = text[nop.Length..].TrimStart();
        else if (text.StartsWith("/bin/sh -c ", StringComparison.Ordinal)) text = "RUN " + text["/bin/sh -c ".Length..].TrimStart();
        else if (text.StartsWith("cmd /S /C ", StringComparison.OrdinalIgnoreCase)) text = "RUN " + text["cmd /S /C ".Length..].TrimStart();
        var space = text.IndexOf(' ');
        var first = space > 0 ? text[..space] : text;
        if (Keywords.Contains(first)) return (first, space > 0 ? text[(space + 1)..].Trim() : "");
        return ("", text);
    }

    /// <summary>Parses docker's size text (for example 182MB, 1.2GB, 512kB, 0B) into bytes; 0 when unknown.</summary>
    public static long ParseSize(string text)
    {
        var m = SizePattern.Match(text.Trim());
        if (!m.Success || !double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return 0;
        var unit = m.Groups[2].Value.ToUpperInvariant();
        var power = unit[0] switch { 'K' => 1, 'M' => 2, 'G' => 3, 'T' => 4, 'P' => 5, _ => 0 };
        return (long)Math.Round(value * Math.Pow(unit.Contains('I') ? 1024 : 1000, power));
    }
}
