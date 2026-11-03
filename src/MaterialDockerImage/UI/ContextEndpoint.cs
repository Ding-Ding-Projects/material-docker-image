using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>Endpoint presets offered by the context form. The value is a starting point the user edits.</summary>
public sealed record ContextEndpointPreset(string Label, string Value, string Explanation);

/// <summary>
/// Pure rules for Docker contexts: name validation, the <c>--docker</c> endpoint specification, removal refusals and
/// whether an endpoint addresses this computer. No WPF references so the rules can be unit tested.
/// </summary>
public static class ContextEndpoint
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);

    public static readonly IReadOnlyList<ContextEndpointPreset> Presets =
    [
        new("Windows named pipe", "npipe:////./pipe/docker_engine", "The local Docker Desktop or Windows engine on this computer."),
        new("Unix socket", "unix:///var/run/docker.sock", "A local Linux engine socket, for example inside WSL."),
        new("TCP with TLS", "tcp://engine.example.internal:2376", "A remote engine over TCP. Port 2376 is the convention for TLS; supply the CA, certificate and key."),
        new("SSH", "ssh://user@engine.example.internal", "A remote engine reached through SSH. The docker CLI uses your SSH configuration and keys."),
    ];

    /// <summary>Returns null when the name is a valid context name, otherwise the problem.</summary>
    public static string? ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Enter a context name.";
        if (!Regex.IsMatch(name, "^[a-zA-Z0-9][a-zA-Z0-9_.+-]*$", RegexOptions.None, Timeout)) return "Context names start with a letter or digit and use letters, digits, '_', '.', '+' or '-'.";
        if (name.Length > 128) return "Context names are limited to 128 characters.";
        return null;
    }

    /// <summary>Returns null when the host is an accepted endpoint URL, otherwise the problem.</summary>
    public static string? ValidateHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return "Enter the engine address, for example npipe:////./pipe/docker_engine.";
        if (host.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)) || host.Contains(',') || host.Contains('"')) return "The address cannot contain spaces, commas, quotes or control characters.";
        if (!Regex.IsMatch(host, "^(npipe|unix|tcp|ssh|fd)://.+", RegexOptions.IgnoreCase, Timeout)) return "Use an npipe://, unix://, tcp:// or ssh:// address.";
        return null;
    }

    /// <summary>
    /// Builds the value for <c>docker context create --docker</c>: <c>host=…[,ca=…][,cert=…][,key=…][,skip-tls-verify=true]</c>.
    /// Throws ArgumentException with a clear message when a part cannot be represented.
    /// </summary>
    public static string DockerSpec(string host, string? ca, string? cert, string? key, bool skipTlsVerify)
    {
        if (ValidateHost(host) is { } problem) throw new ArgumentException(problem);
        var parts = new List<string> { "host=" + host.Trim() };
        void AddPath(string name, string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (path.Contains(',') || path.Contains('"') || path.Any(char.IsControl)) throw new ArgumentException($"The {name} path cannot contain commas, quotes or control characters. Move the file to a simpler path.");
            parts.Add(name + "=" + path.Trim());
        }
        AddPath("ca", ca); AddPath("cert", cert); AddPath("key", key);
        if (!string.IsNullOrWhiteSpace(cert) != !string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A client certificate and its key are used together. Choose both files or neither.");
        if (skipTlsVerify) parts.Add("skip-tls-verify=true");
        return string.Join(",", parts);
    }

    /// <summary>Returns the reason a context cannot be removed from this window, or null when removal may be offered.</summary>
    public static string? RemovalRefusal(string name, string? windowContext, string? cliCurrent)
    {
        if (string.Equals(name, "default", StringComparison.Ordinal)) return "The default context is built into the docker CLI and cannot be removed.";
        var used = windowContext ?? cliCurrent;
        if (string.Equals(name, used, StringComparison.Ordinal)) return "This window uses this context. Switch this window to another context first.";
        return null;
    }

    /// <summary>True when the endpoint addresses an engine on this computer (named pipe or Unix socket).</summary>
    public static bool IsLocal(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return true;
        return host.StartsWith("npipe:", StringComparison.OrdinalIgnoreCase) || host.StartsWith("unix:", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Short transport name for a card chip.</summary>
    public static string Transport(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return "unknown";
        var i = host.IndexOf("://", StringComparison.Ordinal);
        return i > 0 ? host[..i].ToLowerInvariant() : "unknown";
    }
}
