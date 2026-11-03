using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.Pages;

/// <summary>Tolerant readers for the JSON and human-readable size text the docker CLI prints.</summary>
public static class CliValues
{
    public static JsonElement? Get(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var name in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out var next)) return null;
            current = next;
        }
        return current;
    }

    public static string Str(JsonElement element, params string[] path)
    {
        var value = Get(element, path);
        if (value is not { } v) return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Null or JsonValueKind.Undefined => "",
            _ => v.ToString()
        };
    }

    public static long Long(JsonElement element, params string[] path)
    {
        var value = Get(element, path);
        if (value is not { } v) return 0;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) return n;
        return long.TryParse(Str(element, path), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }

    public static bool Bool(JsonElement element, params string[] path)
    {
        var value = Get(element, path);
        if (value is not { } v) return false;
        if (v.ValueKind == JsonValueKind.True) return true;
        if (v.ValueKind == JsonValueKind.False) return false;
        return string.Equals(Str(element, path), "true", StringComparison.OrdinalIgnoreCase);
    }

    public static IEnumerable<JsonElement> Arr(JsonElement element, params string[] path)
    {
        var value = Get(element, path);
        return value is { ValueKind: JsonValueKind.Array } v ? v.EnumerateArray().ToList() : [];
    }

    public static IEnumerable<KeyValuePair<string, JsonElement>> Obj(JsonElement element, params string[] path)
    {
        var value = Get(element, path);
        return value is { ValueKind: JsonValueKind.Object } v ? v.EnumerateObject().Select(p => new KeyValuePair<string, JsonElement>(p.Name, p.Value)).ToList() : [];
    }

    private static readonly Regex SizePattern = new(@"^\s*([0-9]+(?:\.[0-9]+)?)\s*([kKmMgGtTpP]?)(i?)[bB]\s*$", RegexOptions.CultureInvariant);

    /// <summary>Parses docker size text such as "1.84GB", "512MiB", "0B" or "12.3kB". Returns null when the text is not a size.</summary>
    public static double? ParseSize(string text)
    {
        var m = SizePattern.Match(text ?? "");
        if (!m.Success) return null;
        var value = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var binary = m.Groups[3].Value.Length > 0;
        var step = binary ? 1024d : 1000d;
        var power = m.Groups[2].Value.ToUpperInvariant() switch { "K" => 1, "M" => 2, "G" => 3, "T" => 4, "P" => 5, _ => 0 };
        return value * Math.Pow(step, power);
    }

    /// <summary>Parses "23.40%" into 23.4. Returns null for "--" or other non-numeric text.</summary>
    public static double? ParsePercent(string text)
    {
        var t = (text ?? "").Trim().TrimEnd('%');
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>Parses "1.2kB / 3.4MB" into two byte counts.</summary>
    public static (double? First, double? Second) ParsePair(string text)
    {
        var parts = (text ?? "").Split('/', 2);
        return (ParseSize(parts[0]), parts.Length > 1 ? ParseSize(parts[1]) : null);
    }

    /// <summary>Extracts "Total reclaimed space: X" (or similar) from prune output, or null when the engine did not report it.</summary>
    public static string? ReclaimedSpace(string output)
    {
        var m = Regex.Match(output ?? "", @"(?im)^\s*Total(?: reclaimed space)?:?\s*(?:reclaimed space:?)?\s*([0-9.]+\s*[kKMGTP]?i?B)\s*$");
        if (m.Success) return m.Groups[1].Value.Trim();
        m = Regex.Match(output ?? "", @"(?i)reclaimed space:\s*([0-9.]+\s*[kKMGTP]?i?B)");
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }
}
