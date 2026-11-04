using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>Values the run editor collects for the health-check flags. Empty text means "not set".</summary>
public sealed record HealthSettings(string? Command, string? Interval, string? Timeout, string? Retries, string? StartPeriod, string? StartInterval, bool Disable);

/// <summary>Validation and argument assembly for container health checks, device mappings and GPU requests.</summary>
public static partial class StructuredInputs
{
    private static readonly TimeSpan ContainerTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex DurationPart = new(@"([0-9]+(?:\.[0-9]+)?)(ns|us|ms|s|m|h)", RegexOptions.None, ContainerTimeout);
    private static readonly Regex DurationShape = new(@"^(?:[0-9]+(?:\.[0-9]+)?(?:ns|us|ms|s|m|h))+$", RegexOptions.None, ContainerTimeout);

    /// <summary>Validates a Go-style duration such as 30s, 1m30s or 500ms and returns it unchanged. Null or blank yields an empty string.</summary>
    public static string Duration(string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        value = value.Trim();
        if (!DurationShape.IsMatch(value)) throw new ArgumentException($"{label} must be a duration such as 30s, 1m30s or 500ms.");
        double nanoseconds = 0;
        foreach (Match part in DurationPart.Matches(value))
        {
            var amount = double.Parse(part.Groups[1].Value, CultureInfo.InvariantCulture);
            nanoseconds += amount * part.Groups[2].Value switch { "ns" => 1d, "us" => 1e3, "ms" => 1e6, "s" => 1e9, "m" => 6e10, _ => 3.6e12 };
        }
        if (nanoseconds == 0) throw new ArgumentException($"{label} must be longer than zero, or left empty to use the engine default.");
        if (nanoseconds < 1e6) throw new ArgumentException($"{label} must be at least 1ms.");
        return value;
    }

    /// <summary>Validates a positive whole number. Null or blank yields null.</summary>
    public static int? PositiveInt(string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1) throw new ArgumentException($"{label} must be a positive whole number.");
        return number;
    }

    /// <summary>Builds the docker run health-check flags in a stable order.</summary>
    public static IReadOnlyList<string> HealthArguments(HealthSettings settings)
    {
        var args = new List<string>();
        var command = string.IsNullOrWhiteSpace(settings.Command) ? null : settings.Command.Trim();
        var interval = Duration("Interval", settings.Interval);
        var timeout = Duration("Timeout", settings.Timeout);
        var start = Duration("Start period", settings.StartPeriod);
        var startInterval = Duration("Start interval", settings.StartInterval);
        var retries = PositiveInt("Retries", settings.Retries);
        if (settings.Disable)
        {
            if (command != null || interval.Length > 0 || timeout.Length > 0 || start.Length > 0 || startInterval.Length > 0 || retries != null)
                throw new ArgumentException("Disable the image health check cannot be combined with a health command or timing options.");
            args.Add("--no-healthcheck");
            return args;
        }
        if (command != null) { Token(command); args.Add("--health-cmd"); args.Add(command); }
        if (interval.Length > 0) { args.Add("--health-interval"); args.Add(interval); }
        if (timeout.Length > 0) { args.Add("--health-timeout"); args.Add(timeout); }
        if (retries != null) { args.Add("--health-retries"); args.Add(retries.Value.ToString(CultureInfo.InvariantCulture)); }
        if (start.Length > 0) { args.Add("--health-start-period"); args.Add(start); }
        if (startInterval.Length > 0) { args.Add("--health-start-interval"); args.Add(startInterval); }
        return args;
    }

    /// <summary>Returns the --device value host:container:permissions. Permissions are any non-repeating subset of r, w and m; blank means rwm.</summary>
    public static string Device(string host, string? container, string? permissions)
    {
        host = host.Trim(); container = container?.Trim() ?? ""; permissions = permissions?.Trim() ?? "";
        foreach (var path in new[] { host, container })
        {
            if (path.Length == 0) continue;
            Token(path);
            if (!path.StartsWith('/') || path.Contains(':') || path.Contains(',') || path.Contains(' ')) throw new ArgumentException("Device paths must be absolute (for example /dev/ttyUSB0) and cannot contain colons, commas or spaces.");
        }
        if (host.Length == 0) throw new ArgumentException("A device mapping needs a host device path.");
        if (permissions.Length == 0) permissions = "rwm";
        if (!Regex.IsMatch(permissions, "^[rwm]{1,3}$", RegexOptions.None, ContainerTimeout) || permissions.Distinct().Count() != permissions.Length) throw new ArgumentException("Device permissions are a combination of r, w and m, each used at most once, for example rw.");
        return host + ":" + (container.Length > 0 ? container : host) + ":" + permissions;
    }

    /// <summary>Validates every device row together so two rows cannot claim the same container path.</summary>
    public static IReadOnlyList<string> Devices(IEnumerable<(string Host, string Container, string Permissions)> rows)
    {
        var values = new List<string>(); var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (row.Host.Trim().Length == 0 && row.Container.Trim().Length == 0) continue;
            var value = Device(row.Host, row.Container, row.Permissions);
            var target = value.Split(':')[1];
            if (!targets.Add(target)) throw new ArgumentException($"Container device path {target} is mapped more than once.");
            values.Add(value);
        }
        return values;
    }

    /// <summary>Validates one --device-cgroup-rule such as "c 189:* rwm" (type, major:minor with * wildcards, permissions).</summary>
    public static string DeviceCgroupRule(string rule)
    {
        rule = rule.Trim(); Token(rule);
        var match = Regex.Match(rule, @"^([abc]) ([0-9]+|\*):([0-9]+|\*) ([rwm]{1,3})$", RegexOptions.None, ContainerTimeout);
        if (!match.Success || match.Groups[4].Value.Distinct().Count() != match.Groups[4].Value.Length) throw new ArgumentException("A cgroup device rule looks like \"c 189:* rwm\": type a, b or c, major:minor (or *), then r, w and m permissions.");
        return rule;
    }

    /// <summary>Returns the --gpus value. spec is all, a positive count, or device=ID[,ID]; capabilities are optional comma or space separated names.</summary>
    public static string Gpus(string spec, string? capabilities)
    {
        spec = spec.Trim().Trim('"'); Token(spec);
        string value;
        if (spec.Equals("all", StringComparison.OrdinalIgnoreCase)) value = "all";
        else if (Regex.IsMatch(spec, @"^[1-9][0-9]*$", RegexOptions.None, ContainerTimeout)) value = spec;
        else if (Regex.IsMatch(spec, @"^device=[A-Za-z0-9][A-Za-z0-9_.:-]*(,[A-Za-z0-9][A-Za-z0-9_.:-]*)*$", RegexOptions.None, ContainerTimeout)) value = "\"" + spec + "\"";
        else throw new ArgumentException("GPUs must be all, a positive count, or device IDs such as device=0,1.");
        var caps = (capabilities ?? "").Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var cap in caps) if (!Regex.IsMatch(cap, @"^[a-z][a-z0-9_-]*$", RegexOptions.None, ContainerTimeout)) throw new ArgumentException($"GPU capability \"{cap}\" must be lowercase letters, such as compute, utility or video.");
        if (caps.Distinct(StringComparer.Ordinal).Count() != caps.Length) throw new ArgumentException("Each GPU capability can be listed once.");
        return caps.Length == 0 ? value : value + ",\"capabilities=" + string.Join(",", caps) + "\"";
    }
}
