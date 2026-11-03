using System.Text.Json;

namespace MaterialDockerImage.Pages;

/// <summary>One line of <c>docker compose events --json</c>.</summary>
public sealed record ComposeEventRow(string Time, string Type, string Action, string Service, string Container, string Attributes)
{
    public static ComposeEventRow? Parse(string line)
    {
        var t = line.Trim();
        if (!t.StartsWith('{')) return null;
        try
        {
            using var doc = JsonDocument.Parse(t);
            var r = doc.RootElement;
            var attributes = CliValues.Obj(r, "attributes").Select(p => p.Key + "=" + p.Value.ToString()).ToList();
            var time = CliValues.Str(r, "time");
            if (DateTimeOffset.TryParse(time, out var parsed)) time = parsed.ToLocalTime().ToString("HH:mm:ss.fff");
            var id = CliValues.Str(r, "id");
            return new ComposeEventRow(time, CliValues.Str(r, "type"), CliValues.Str(r, "action"), CliValues.Str(r, "service"), id.Length > 12 ? id[..12] : id, string.Join(", ", attributes));
        }
        catch (JsonException) { return null; }
    }
}

/// <summary>Reads the resolved Compose model to find services that declare <c>develop.watch</c> rules.</summary>
public static class ComposeWatchSupport
{
    /// <summary>Service names with at least one develop.watch rule in <c>docker compose config --format json</c> output.</summary>
    public static IReadOnlyList<string> ServicesWithWatch(string configJson)
    {
        using var doc = JsonDocument.Parse(configJson);
        var result = new List<string>();
        foreach (var service in CliValues.Obj(doc.RootElement, "services"))
            if (CliValues.Arr(service.Value, "develop", "watch").Any()) result.Add(service.Key);
        return result;
    }
}
