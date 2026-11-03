using System.Globalization;
using System.Text.Json;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// One Minecraft server: a container labelled material-docker-image.minecraft=&lt;name&gt; plus its settings rebuilt from the
/// container environment. Players is filled in after the list loads (null means not known).
/// </summary>
internal sealed class MinecraftServer
{
    public required string Name { get; init; }
    public required string Id { get; init; }
    public required string ContainerName { get; init; }
    public required string Image { get; init; }
    public required string State { get; init; }
    public required string Status { get; init; }
    public required MinecraftEdition Edition { get; init; }
    public required MinecraftServerSpec Spec { get; init; }
    public required DateTimeOffset? Created { get; init; }
    public required DateTimeOffset? StartedAt { get; init; }
    public required string? Binding { get; init; }
    public required string VolumeName { get; init; }
    public string LevelName { get; init; } = "world";
    /// <summary>Online/max text from rcon-cli list, or a reason why it is unknown.</summary>
    public string Players { get; set; } = "unknown";
    public IReadOnlyList<string> OnlineNames { get; set; } = [];

    public bool IsRunning => State == "running";
    public bool IsJava => Edition == MinecraftEdition.Java;
    public bool CanRcon => IsJava && IsRunning && Spec.RconEnabled;
    public string EditionText => IsJava ? "Java" : "Bedrock";
    public string FlavourText => IsJava ? Title(Spec.Flavour) : "Bedrock Dedicated Server";
    public string PortText => Binding == null ? "not published" : Binding + (IsJava ? "/tcp" : "/udp");
    public string MemoryText => IsJava ? Spec.MemoryGb + " GB heap" : "managed by the server";
    public string CreatedText => Created?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "unknown";
    public string UptimeText => IsRunning && StartedAt is { } t ? MinecraftFormat.Duration(DateTimeOffset.UtcNow - t) : "not running";

    public Tone StateTone => State switch
    {
        "running" => Tone.Success,
        "restarting" => Tone.Warning,
        "paused" => Tone.Warning,
        "dead" => Tone.Error,
        "created" => Tone.Primary,
        _ => Tone.Neutral
    };

    private static string Title(string type) => type switch
    {
        "NEOFORGE" => "NeoForge",
        _ => type.Length == 0 ? "Vanilla" : type[..1] + type[1..].ToLowerInvariant()
    };

    /// <summary>Builds a server from one element of docker container inspect output. Returns null when it lacks the page label.</summary>
    public static MinecraftServer? FromInspect(JsonElement c)
    {
        var labels = c.TryGetProperty("Config", out var config) && config.TryGetProperty("Labels", out var l) && l.ValueKind == JsonValueKind.Object ? l : default;
        if (labels.ValueKind != JsonValueKind.Object || !labels.TryGetProperty(MinecraftSpecRules.Label, out var nameValue)) return null;
        var name = nameValue.GetString() ?? "";
        var edition = labels.TryGetProperty(MinecraftSpecRules.EditionLabel, out var e) && e.GetString() == "bedrock" ? MinecraftEdition.Bedrock : MinecraftEdition.Java;
        var env = config.TryGetProperty("Env", out var envArray) && envArray.ValueKind == JsonValueKind.Array
            ? envArray.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new List<string>();
        var binding = FirstBinding(c);
        var spec = MinecraftSpecRules.FromEnvironment(name, edition, env, binding, out _);
        var state = c.TryGetProperty("State", out var s) ? s : default;
        var volume = spec.VolumeName;
        if (c.TryGetProperty("Mounts", out var mounts) && mounts.ValueKind == JsonValueKind.Array)
            foreach (var m in mounts.EnumerateArray())
                if (DockerJson.Text(m, "Destination") == "/data" && DockerJson.Text(m, "Type") == "volume") volume = DockerJson.Text(m, "Name");
        var level = env.FirstOrDefault(x => x.StartsWith("LEVEL=", StringComparison.Ordinal))?[6..];
        return new MinecraftServer
        {
            Name = name,
            Id = DockerJson.Text(c, "Id"),
            ContainerName = DockerJson.Text(c, "Name").TrimStart('/'),
            Image = config.TryGetProperty("Image", out var img) ? img.GetString() ?? "" : "",
            State = state.ValueKind == JsonValueKind.Object ? DockerJson.Text(state, "Status") : "unknown",
            Status = state.ValueKind == JsonValueKind.Object ? DockerJson.Text(state, "Status") + (DockerJson.Text(state, "ExitCode") is { Length: > 0 } code && code != "0" ? " (exit " + code + ")" : "") : "",
            Edition = edition,
            Spec = spec,
            Created = Time(DockerJson.Text(c, "Created")),
            StartedAt = state.ValueKind == JsonValueKind.Object ? Time(DockerJson.Text(state, "StartedAt")) : null,
            Binding = binding,
            VolumeName = volume,
            LevelName = string.IsNullOrWhiteSpace(level) ? "world" : level,
        };
    }

    private static string? FirstBinding(JsonElement c)
    {
        if (!c.TryGetProperty("HostConfig", out var host) || !host.TryGetProperty("PortBindings", out var ports) || ports.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in ports.EnumerateObject())
        {
            if (p.Value.ValueKind != JsonValueKind.Array) continue;
            foreach (var b in p.Value.EnumerateArray())
            {
                var ip = DockerJson.Text(b, "HostIp");
                return (ip.Length == 0 ? "0.0.0.0" : ip) + ":" + DockerJson.Text(b, "HostPort");
            }
        }
        return null;
    }

    private static DateTimeOffset? Time(string text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) && t.Year > 1 ? t : null;
}

internal static class MinecraftFormat
{
    public static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        if (span.TotalDays >= 1) return $"{(int)span.TotalDays} d {span.Hours} h";
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours} h {span.Minutes} min";
        return $"{(int)span.TotalMinutes} min";
    }
}
