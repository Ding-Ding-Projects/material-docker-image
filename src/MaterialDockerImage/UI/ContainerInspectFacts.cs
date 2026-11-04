using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace MaterialDockerImage.UI;

/// <summary>One recorded health probe from docker inspect (State.Health.Log).</summary>
public sealed record HealthProbe(string Started, string Ended, int ExitCode, string Output);

/// <summary>Configured and observed health information for one container.</summary>
public sealed record HealthFacts(bool HasHealthcheck, bool Disabled, string Status, int FailingStreak, string Test, string Interval, string Timeout, string StartPeriod, string StartInterval, int Retries, IReadOnlyList<HealthProbe> Probes);

/// <summary>Pure readers for the health, device and GPU parts of docker container inspect output.</summary>
public static class ContainerInspectFacts
{
    private const int MaxProbeOutput = 400;

    private static JsonElement? Child(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var name in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out var next) || next.ValueKind == JsonValueKind.Null) return null;
            current = next;
        }
        return current;
    }

    private static string Text(JsonElement? element) => element is { ValueKind: JsonValueKind.String } e ? e.GetString() ?? "" : element is { } n ? n.ToString() : "";

    private static long Number(JsonElement? element) => element is { ValueKind: JsonValueKind.Number } e && e.TryGetInt64(out var v) ? v : 0;

    /// <summary>Formats a Go nanosecond duration the way docker run accepts it, for example 90000000000 becomes 1m30s.</summary>
    public static string FormatDuration(long nanoseconds)
    {
        if (nanoseconds <= 0) return "";
        if (nanoseconds < 1_000_000) return nanoseconds.ToString(CultureInfo.InvariantCulture) + "ns";
        var milliseconds = nanoseconds / 1_000_000;
        var parts = new List<string>();
        var hours = milliseconds / 3_600_000; milliseconds %= 3_600_000;
        var minutes = milliseconds / 60_000; milliseconds %= 60_000;
        var seconds = milliseconds / 1000; milliseconds %= 1000;
        if (hours > 0) parts.Add(hours + "h");
        if (minutes > 0) parts.Add(minutes + "m");
        if (seconds > 0) parts.Add(seconds + "s");
        if (milliseconds > 0) parts.Add(milliseconds + "ms");
        return string.Concat(parts);
    }

    /// <summary>Reads Config.Healthcheck and State.Health. Returns facts with HasHealthcheck false when neither exists.</summary>
    public static HealthFacts Health(JsonElement info)
    {
        var tests = Child(info, "Config", "Healthcheck", "Test") is { ValueKind: JsonValueKind.Array } t ? t.EnumerateArray().Select(x => Text(x)).ToList() : [];
        var disabled = tests.Count > 0 && tests[0] == "NONE";
        string test = "";
        if (!disabled && tests.Count > 0)
            test = tests[0] switch { "CMD-SHELL" => string.Join(" ", tests.Skip(1)), "CMD" => "[" + string.Join(", ", tests.Skip(1).Select(x => "\"" + x + "\"")) + "]", _ => string.Join(" ", tests) };
        var config = Child(info, "Config", "Healthcheck");
        var health = Child(info, "State", "Health");
        var probes = new List<HealthProbe>();
        if (health is { } h && Child(h, "Log") is { ValueKind: JsonValueKind.Array } log)
        {
            foreach (var entry in log.EnumerateArray().Reverse())
            {
                var output = Text(Child(entry, "Output")).Trim();
                if (output.Length > MaxProbeOutput) output = output[..MaxProbeOutput] + "…";
                probes.Add(new HealthProbe(Text(Child(entry, "Start")), Text(Child(entry, "End")), (int)Number(Child(entry, "ExitCode")), output));
            }
        }
        return new HealthFacts(
            config != null || health != null,
            disabled,
            health is { } hh ? Text(Child(hh, "Status")) : "",
            health is { } h2 ? (int)Number(Child(h2, "FailingStreak")) : 0,
            test,
            config is { } c ? FormatDuration(Number(Child(c, "Interval"))) : "",
            config is { } c2 ? FormatDuration(Number(Child(c2, "Timeout"))) : "",
            config is { } c3 ? FormatDuration(Number(Child(c3, "StartPeriod"))) : "",
            config is { } c4 ? FormatDuration(Number(Child(c4, "StartInterval"))) : "",
            config is { } c5 ? (int)Number(Child(c5, "Retries")) : 0,
            probes);
    }

    /// <summary>Lines such as "/dev/ttyUSB0 → /dev/ttyUSB0 (rwm)" from HostConfig.Devices.</summary>
    public static IReadOnlyList<string> Devices(JsonElement info) =>
        Child(info, "HostConfig", "Devices") is { ValueKind: JsonValueKind.Array } a
            ? a.EnumerateArray().Select(d => $"{Text(Child(d, "PathOnHost"))} → {Text(Child(d, "PathInContainer"))} ({Text(Child(d, "CgroupPermissions"))})").ToList() : [];

    /// <summary>Raw rules from HostConfig.DeviceCgroupRules.</summary>
    public static IReadOnlyList<string> CgroupRules(JsonElement info) =>
        Child(info, "HostConfig", "DeviceCgroupRules") is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray().Select(x => Text(x)).Where(x => x.Length > 0).ToList() : [];

    /// <summary>One line per HostConfig.DeviceRequests entry: driver, count or IDs, capabilities and options.</summary>
    public static IReadOnlyList<string> GpuRequests(JsonElement info)
    {
        var lines = new List<string>();
        if (Child(info, "HostConfig", "DeviceRequests") is not { ValueKind: JsonValueKind.Array } requests) return lines;
        foreach (var request in requests.EnumerateArray())
        {
            var driver = Text(Child(request, "Driver"));
            var count = Number(Child(request, "Count"));
            var ids = Child(request, "DeviceIDs") is { ValueKind: JsonValueKind.Array } idList ? idList.EnumerateArray().Select(x => Text(x)).ToList() : [];
            var capabilities = Child(request, "Capabilities") is { ValueKind: JsonValueKind.Array } outer
                ? string.Join("; ", outer.EnumerateArray().Select(set => set.ValueKind == JsonValueKind.Array ? string.Join(", ", set.EnumerateArray().Select(x => Text(x))) : Text(set))) : "";
            var devices = ids.Count > 0 ? "device IDs " + string.Join(", ", ids) : count == -1 ? "all devices" : count > 0 ? count + (count == 1 ? " device" : " devices") : "no device selection";
            var options = Child(request, "Options") is { ValueKind: JsonValueKind.Object } o ? string.Join(", ", o.EnumerateObject().Select(p => p.Name + "=" + Text(p.Value))) : "";
            lines.Add($"{(driver.Length > 0 ? driver : "default driver")} · {devices} · capabilities: {(capabilities.Length > 0 ? capabilities : "none listed")}{(options.Length > 0 ? " · options: " + options : "")}");
        }
        return lines;
    }

    /// <summary>
    /// An honest explanation when GPU or device access could not be granted, from a docker error message. Returns an empty string when the text
    /// does not look like a hardware-support failure.
    /// </summary>
    public static string HardwareFailureNote(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "";
        if (message.Contains("could not select device driver", StringComparison.OrdinalIgnoreCase) || message.Contains("nvidia-container", StringComparison.OrdinalIgnoreCase))
            return "This engine has no GPU support for the requested GPUs. GPU access needs a driver on the engine host, for example the NVIDIA Container Toolkit or WSL GPU support. Remove the GPU request, or run on an engine that provides one.";
        if (message.Contains("error gathering device information", StringComparison.OrdinalIgnoreCase) || (message.Contains("--device", StringComparison.OrdinalIgnoreCase) && message.Contains("no such file", StringComparison.OrdinalIgnoreCase)))
            return "A host device path was not found on the engine host. Device paths belong to the machine running the engine, which may not be this computer.";
        return "";
    }

    /// <summary>Returns the engine-reported start error for a container, or an empty string.</summary>
    public static string StartError(JsonElement info) => Text(Child(info, "State", "Error"));
}
