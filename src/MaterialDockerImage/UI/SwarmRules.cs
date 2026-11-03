using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>Health of a replica count or task state, mapped to a tone by the WPF layer.</summary>
public enum SwarmHealth { Unknown, Healthy, Partial, Down }

/// <summary>What <c>docker info --format "{{json .Swarm}}"</c> reports about this engine's membership.</summary>
public sealed record SwarmMembership(string LocalNodeState, bool ControlAvailable, string NodeId, string NodeAddress, string ClusterId, int Nodes, int Managers, string Error)
{
    public bool IsActive => LocalNodeState == "active";
    public bool IsInactive => LocalNodeState is "inactive" or "";
    public bool IsManager => IsActive && ControlAvailable;

    public static SwarmMembership Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return new("inactive", false, "", "", "", 0, 0, "");
        string S(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        int I(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;
        var control = root.TryGetProperty("ControlAvailable", out var c) && c.ValueKind == JsonValueKind.True;
        var cluster = root.TryGetProperty("Cluster", out var cl) ? S(cl, "ID") : "";
        return new(S(root, "LocalNodeState").ToLowerInvariant(), control, S(root, "NodeID"), S(root, "NodeAddr"), cluster, I(root, "Nodes"), I(root, "Managers"), S(root, "Error"));
    }
}

/// <summary>Pure parsing and argument building for the Swarm destination. No WPF references.</summary>
public static class SwarmRules
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);

    /// <summary>Parses "3/3" or "1/3 (max 1 per node)" into running and desired counts.</summary>
    public static (int Running, int Desired)? ParseReplicas(string text)
    {
        var m = Regex.Match(text ?? "", @"^\s*(\d+)\s*/\s*(\d+)", RegexOptions.None, Timeout);
        if (!m.Success) return null;
        return (int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    public static SwarmHealth ReplicaHealth(string text)
    {
        if (ParseReplicas(text) is not { } r) return SwarmHealth.Unknown;
        if (r.Desired == 0) return SwarmHealth.Unknown;
        if (r.Running >= r.Desired) return SwarmHealth.Healthy;
        return r.Running == 0 ? SwarmHealth.Down : SwarmHealth.Partial;
    }

    /// <summary>Maps a task state ("Running 2 hours ago", "Failed 3 seconds ago", "Shutdown") to a health.</summary>
    public static SwarmHealth TaskHealth(string state)
    {
        var first = (state ?? "").Trim().Split(' ', 2)[0].ToLowerInvariant();
        return first switch
        {
            "running" or "complete" => SwarmHealth.Healthy,
            "new" or "pending" or "assigned" or "accepted" or "preparing" or "ready" or "starting" => SwarmHealth.Partial,
            "failed" or "rejected" or "orphaned" => SwarmHealth.Down,
            _ => SwarmHealth.Unknown,
        };
    }

    public static SwarmHealth NodeHealth(string status, string availability)
    {
        if (!string.Equals(status, "Ready", StringComparison.OrdinalIgnoreCase)) return SwarmHealth.Down;
        return string.Equals(availability, "Active", StringComparison.OrdinalIgnoreCase) ? SwarmHealth.Healthy : SwarmHealth.Partial;
    }

    /// <summary>Returns null when the name is a valid Swarm object name (service, stack, secret, config), otherwise the problem.</summary>
    public static string? ValidateName(string name, string what)
    {
        if (string.IsNullOrWhiteSpace(name)) return $"Enter a {what} name.";
        if (name.Length > 63) return $"{Cap(what)} names are limited to 63 characters.";
        if (!Regex.IsMatch(name, "^[a-zA-Z0-9][a-zA-Z0-9_.-]*$", RegexOptions.None, Timeout)) return $"{Cap(what)} names start with a letter or digit and use letters, digits, '_', '.' or '-'.";
        return null;
    }
    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>Returns null when the address is empty or a valid advertise/listen address (IP, IP:port or interface name).</summary>
    public static string? ValidateAddress(string address, bool required)
    {
        if (string.IsNullOrWhiteSpace(address)) return required ? "Enter the manager address, for example 192.168.1.20:2377." : null;
        var a = address.Trim();
        if (a.StartsWith('-') || a.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) return "The address cannot start with a hyphen or contain spaces.";
        if (!Regex.IsMatch(a, @"^(\[[0-9a-fA-F:.]+\]|[A-Za-z0-9_.-]+)(:\d{1,5})?$", RegexOptions.None, Timeout)) return "Use an IP address, host name or interface name, optionally with :port.";
        var colon = a.LastIndexOf(':');
        if (colon > 0 && !a.EndsWith(']') && int.TryParse(a[(colon + 1)..], out var port) && (port < 1 || port > 65535)) return "The port must be between 1 and 65535.";
        return null;
    }

    /// <summary>Returns null when the join token has the SWMTKN-1-… shape, otherwise the problem.</summary>
    public static string? ValidateJoinToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return "Paste the join token printed by docker swarm join-token on a manager.";
        if (!Regex.IsMatch(token.Trim(), "^SWMTKN-1-[0-9a-z]+-[0-9a-z]+$", RegexOptions.None, Timeout)) return "Join tokens look like SWMTKN-1-<cluster hash>-<secret>. Copy the whole token.";
        return null;
    }

    /// <summary>Parses "8080:80", "8080:80/udp" or "80" into a --publish value (published=…,target=…,protocol=…).</summary>
    public static string PublishSpec(string text)
    {
        var m = Regex.Match((text ?? "").Trim(), @"^(?:(\d{1,5}):)?(\d{1,5})(?:/(tcp|udp|sctp))?$", RegexOptions.IgnoreCase, Timeout);
        if (!m.Success) throw new ArgumentException($"\"{text}\" is not a port mapping. Use published:target such as 8080:80, optionally /udp.");
        var target = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        if (target is < 1 or > 65535) throw new ArgumentException("Ports must be between 1 and 65535.");
        var spec = "target=" + target;
        if (m.Groups[1].Success)
        {
            var published = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (published is < 1 or > 65535) throw new ArgumentException("Ports must be between 1 and 65535.");
            spec = "published=" + published + "," + spec;
        }
        if (m.Groups[3].Success) spec += ",protocol=" + m.Groups[3].Value.ToLowerInvariant();
        return spec;
    }

    /// <summary>Validates a placement constraint such as node.role==manager or node.labels.zone!=b.</summary>
    public static string Constraint(string text)
    {
        var t = (text ?? "").Trim();
        if (!Regex.IsMatch(t, @"^[A-Za-z0-9_.-]+\s*(==|!=)\s*[^\s,]+$", RegexOptions.None, Timeout)) throw new ArgumentException($"\"{text}\" is not a constraint. Use attribute==value or attribute!=value, for example node.role==manager.");
        return Regex.Replace(t, @"\s*(==|!=)\s*", "$1", RegexOptions.None, Timeout);
    }

    /// <summary>
    /// Builds <c>service create</c> arguments. Environment values are passed as arguments to the engine but callers must
    /// never log or record them.
    /// </summary>
    public static List<string> CreateServiceArgs(string image, string name, bool global, int replicas, IEnumerable<string> ports, IEnumerable<KeyValuePair<string, string>> environment, IEnumerable<string> constraints)
    {
        if (string.IsNullOrWhiteSpace(image) || image.StartsWith('-') || image.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) throw new ArgumentException("Enter an image reference such as nginx:1.27.");
        var args = new List<string> { "service", "create" };
        if (name.Trim().Length > 0)
        {
            if (ValidateName(name.Trim(), "service") is { } problem) throw new ArgumentException(problem);
            args.AddRange(["--name", name.Trim()]);
        }
        if (global) args.AddRange(["--mode", "global"]);
        else
        {
            if (replicas is < 0 or > 10000) throw new ArgumentException("Replicas must be between 0 and 10000.");
            args.AddRange(["--replicas", replicas.ToString(CultureInfo.InvariantCulture)]);
        }
        foreach (var p in ports.Where(p => !string.IsNullOrWhiteSpace(p))) args.AddRange(["--publish", PublishSpec(p)]);
        foreach (var e in environment)
        {
            if (!Regex.IsMatch(e.Key, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.None, Timeout)) throw new ArgumentException($"Environment variable \"{e.Key}\" must start with a letter or underscore and use letters, digits or underscores.");
            if (e.Value.Any(char.IsControl)) throw new ArgumentException($"The value of {e.Key} contains control characters.");
            args.AddRange(["--env", e.Key + "=" + e.Value]);
        }
        foreach (var c in constraints.Where(c => !string.IsNullOrWhiteSpace(c))) args.AddRange(["--constraint", Constraint(c)]);
        args.Add(image.Trim());
        return args;
    }

    /// <summary>Decodes a config's Spec.Data (base64). Returns UTF-8 text, or a note when the content is binary.</summary>
    public static string DecodeConfigData(string base64)
    {
        if (string.IsNullOrEmpty(base64)) return "";
        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64); }
        catch (FormatException) { return "(The engine returned data that is not valid base64.)"; }
        if (bytes.Any(b => b == 0)) return $"(Binary content, {bytes.Length} bytes. Not shown as text.)";
        return new UTF8Encoding(false, false).GetString(bytes);
    }

    /// <summary>Extracts Spec.Data from <c>docker config inspect</c> output (an array with one object).</summary>
    public static string? ConfigDataFromInspect(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.ValueKind == JsonValueKind.Array ? (doc.RootElement.GetArrayLength() > 0 ? doc.RootElement[0] : default) : doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (root.TryGetProperty("Spec", out var spec) && spec.ValueKind == JsonValueKind.Object && spec.TryGetProperty("Data", out var data) && data.ValueKind == JsonValueKind.String) return data.GetString();
        return null;
    }

    /// <summary>Availability values accepted by <c>docker node update --availability</c>.</summary>
    public static readonly IReadOnlyList<string> Availabilities = ["active", "pause", "drain"];

    public static bool IsIpAddress(string text) => IPAddress.TryParse(text.Trim('[', ']'), out _);
}
