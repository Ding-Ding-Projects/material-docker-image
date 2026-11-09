using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>Catalogue grouping shown as filter chips on the Self-hosted destination.</summary>
public enum SelfHostedCategory { FilesAndSync, Development, Media, HomeAndMonitoring, Network, Productivity, AI }

/// <summary>Database engine for apps that offer a choice.</summary>
public enum SelfHostedDatabase { None, MariaDb, PostgreSql }

/// <summary>Where persistent data lives: Docker named volumes (default) or folders under a chosen folder.</summary>
public enum SelfHostedStorage { NamedVolumes, Folder }

/// <summary>One published port. Protocol is "tcp", "udp" or "tcp+udp" (both, on the same host port).</summary>
public sealed record SelfHostedPort(string Key, string Service, int ContainerPort, int DefaultHostPort, string Protocol, string Purpose, bool Web = false);

/// <summary>One persistent location. Database volumes stay named volumes even in folder mode (see <see cref="SelfHostedStorage"/>).</summary>
public sealed record SelfHostedVolume(string Key, string Service, string ContainerPath, string Purpose, bool Database = false, SelfHostedDatabase Only = SelfHostedDatabase.None);

/// <summary>A generated secret written only to the project's .env file. <see cref="Only"/> limits it to one database choice.</summary>
public sealed record SelfHostedSecret(string Key, string Purpose, int Length = 32, bool LaravelKey = false, SelfHostedDatabase Only = SelfHostedDatabase.None);

/// <summary>One container in a rendered project.</summary>
public sealed class SelfHostedServiceSpec
{
    public required string Name { get; init; }
    public required string Image { get; init; }
    /// <summary>Environment values in Compose syntax. Values that come from the user or are secret are references to .env variables.</summary>
    public List<KeyValuePair<string, string>> Environment { get; init; } = [];
    public List<string> Command { get; init; } = [];
    /// <summary>Container health check test in exec form, for example ["CMD-SHELL", "pg_isready"].</summary>
    public List<string> Healthcheck { get; init; } = [];
    public string HealthStartPeriod { get; init; } = "30s";
    public List<string> DependsOnHealthy { get; init; } = [];
    public List<string> DependsOnStarted { get; init; } = [];
    public string? ShmSize { get; init; }
}

/// <summary>Values chosen in the setup wizard. Secrets are kept here only until they are written to the .env file.</summary>
public sealed class SelfHostedOptions
{
    public string Project { get; set; } = "";
    public Dictionary<string, int> HostPorts { get; set; } = new(StringComparer.Ordinal);
    public SelfHostedStorage Storage { get; set; } = SelfHostedStorage.NamedVolumes;
    public string Folder { get; set; } = "";
    public string TimeZone { get; set; } = "Etc/UTC";
    public string AdminUser { get; set; } = "";
    public SelfHostedDatabase Database { get; set; } = SelfHostedDatabase.None;
    /// <summary>Host name or address used in links the app builds for itself (GitLab external_url, Forgejo ROOT_URL).</summary>
    public string PublicHost { get; set; } = "localhost";
    /// <summary>Extra host names the app should accept (Nextcloud trusted domains).</summary>
    public List<string> TrustedHosts { get; set; } = [];
    public Dictionary<string, string> Secrets { get; set; } = new(StringComparer.Ordinal);

    public int Port(SelfHostedApp app, string key) => HostPorts.TryGetValue(key, out var p) ? p : app.Ports.First(x => x.Key == key).DefaultHostPort;
}

/// <summary>Rules shared by the wizard, the renderer and the checks.</summary>
public static partial class SelfHostedRules
{
    /// <summary>Label on every service and volume this app creates. Projects without it are never listed or touched.</summary>
    public const string Label = "material-docker-image.selfhosted";
    public const string SecretAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProjectPattern();
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9._@-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex AdminPattern();
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_+-]*(/[A-Za-z0-9_+-]+){0,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex TimeZonePattern();
    [GeneratedRegex("^[A-Za-z0-9]([A-Za-z0-9.-]{0,251}[A-Za-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex HostPattern();

    /// <summary>Compose project names: lowercase letters, digits, dashes and underscores, starting with a letter or digit.</summary>
    public static string? ValidateProject(string name, IEnumerable<string> existing)
    {
        if (string.IsNullOrEmpty(name)) return "Enter a project name.";
        if (!ProjectPattern().IsMatch(name)) return "Use lowercase letters, digits, dashes and underscores, starting with a letter or digit (at most 63 characters).";
        if (existing.Contains(name, StringComparer.Ordinal)) return "A Compose project with this name already exists on this engine.";
        return null;
    }

    public static string? ValidateAdmin(SelfHostedApp app, string user)
    {
        if (app.AdminUser == null) return null;
        if (app.AdminIsEmail) return Regex.IsMatch(user, "^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) ? null : "Enter an e-mail address for the administrator.";
        return AdminPattern().IsMatch(user) ? null : "Use 2 to 64 letters, digits, dots, dashes, underscores or @, starting with a letter.";
    }

    public static string? ValidateTimeZone(string zone) => TimeZonePattern().IsMatch(zone) ? null : "Enter an IANA time zone such as Europe/London or Asia/Hong_Kong.";

    public static string? ValidateHost(string host) => HostPattern().IsMatch(host) ? null : "Enter a host name or IPv4 address.";

    public static string? ValidateFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return "Choose a folder for the data.";
        if (!Path.IsPathFullyQualified(folder)) return "Use a full path such as D:\\Docker data.";
        if (folder.IndexOfAny(['"', '\'', '\n', '\r', '$', '`']) >= 0) return "The folder path cannot contain quotes, $ or line breaks.";
        return null;
    }

    /// <summary>Problems with the chosen host ports: range, duplicates inside the app and conflicts with ports already in use.</summary>
    public static List<string> ValidatePorts(SelfHostedApp app, SelfHostedOptions options, IReadOnlySet<int> inUse)
    {
        var problems = new List<string>();
        var seen = new Dictionary<int, string>();
        foreach (var port in app.Ports)
        {
            var host = options.Port(app, port.Key);
            if (host is < 1 or > 65535) { problems.Add($"{port.Purpose}: {host} is not a port between 1 and 65535."); continue; }
            if (seen.TryGetValue(host, out var other)) problems.Add($"{port.Purpose}: port {host} is already used by {other} in this setup.");
            else seen[host] = port.Purpose;
            if (inUse.Contains(host)) problems.Add($"{port.Purpose}: port {host} is already in use on this engine or computer.");
        }
        return problems;
    }

    /// <summary>All problems for a complete set of options (the review step and Deploy refuse while any remain).</summary>
    public static List<string> Validate(SelfHostedApp app, SelfHostedOptions options, IEnumerable<string> existingProjects, IReadOnlySet<int> inUse)
    {
        var problems = new List<string>();
        if (ValidateProject(options.Project, existingProjects) is { } p) problems.Add("Project name: " + p);
        problems.AddRange(ValidatePorts(app, options, inUse));
        if (options.Storage == SelfHostedStorage.Folder && ValidateFolder(options.Folder) is { } f) problems.Add("Data folder: " + f);
        if (ValidateTimeZone(options.TimeZone) is { } t) problems.Add("Time zone: " + t);
        if (ValidateAdmin(app, options.AdminUser) is { } a) problems.Add("Administrator: " + a);
        if (ValidateHost(options.PublicHost) is { } h) problems.Add("Address: " + h);
        if (app.Databases.Count > 0 && !app.Databases.Contains(options.Database)) problems.Add("Database: choose one of the supported databases.");
        foreach (var secret in app.SecretsFor(options.Database))
            if (!options.Secrets.TryGetValue(secret.Key, out var value) || value.Length < 16) problems.Add("Secrets: " + secret.Key + " has not been generated.");
        return problems;
    }

    /// <summary>A cryptographically random secret from letters and digits, safe in .env files, URLs and connection strings.</summary>
    public static string GenerateSecret(int length) => RandomNumberGenerator.GetString(SecretAlphabet, Math.Max(16, length));

    /// <summary>Fills every secret the app needs for the chosen database, replacing none that already exist.</summary>
    public static void GenerateSecrets(SelfHostedApp app, SelfHostedOptions options)
    {
        foreach (var secret in app.SecretsFor(options.Database))
        {
            if (options.Secrets.ContainsKey(secret.Key)) continue;
            options.Secrets[secret.Key] = secret.LaravelKey ? "base64:" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) : GenerateSecret(secret.Length);
        }
    }

    /// <summary>Default options for an app: unique project name, default ports, system time zone, default database and admin name.</summary>
    public static SelfHostedOptions Defaults(SelfHostedApp app, IEnumerable<string> existingProjects, string timeZone)
    {
        var existing = existingProjects.ToHashSet(StringComparer.Ordinal);
        var name = app.Id; var i = 2;
        while (existing.Contains(name)) name = app.Id + "-" + i++;
        var options = new SelfHostedOptions
        {
            Project = name,
            TimeZone = ValidateTimeZone(timeZone) == null ? timeZone : "Etc/UTC",
            AdminUser = app.AdminUser ?? "",
            Database = app.Databases.Count > 0 ? app.Databases[0] : SelfHostedDatabase.None,
        };
        foreach (var port in app.Ports) options.HostPorts[port.Key] = port.DefaultHostPort;
        return options;
    }

    /// <summary>Replaces each secret value in a text with a mask, so previews and messages never show one.</summary>
    public static string Mask(string text, SelfHostedOptions options)
    {
        foreach (var value in options.Secrets.Values.Where(v => v.Length > 0).OrderByDescending(v => v.Length)) text = text.Replace(value, "••••••••", StringComparison.Ordinal);
        return text;
    }

    /// <summary>The first published host port for a container port in a docker ps "Ports" value such as
    /// "0.0.0.0:8080->80/tcp, [::]:8080->80/tcp". Returns null when the port is not published.</summary>
    public static int? PublishedPort(string ports, int containerPort)
    {
        foreach (Match m in Regex.Matches(ports ?? "", @"(?:[\d.]+|\[[0-9a-fA-F:]*\]):(\d+)->(\d+)/(tcp|udp)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            if (int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) == containerPort) return int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        return null;
    }

    /// <summary>Bytes from a docker stats size such as "50.5MiB", "12kB" or "1.2GiB"; null when it cannot be read.</summary>
    public static double? ParseSize(string text)
    {
        var m = Regex.Match((text ?? "").Trim(), @"^(\d+(?:\.\d+)?)\s*([kKMGT]?)(i?)B$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!m.Success) return null;
        var value = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var step = m.Groups[3].Value == "i" ? 1024.0 : 1000.0;
        var power = "kMGT".IndexOf(char.ToUpperInvariant(m.Groups[2].Value.FirstOrDefault()) == 'K' ? 'k' : m.Groups[2].Value.FirstOrDefault()) + 1;
        return value * Math.Pow(step, power);
    }

    /// <summary>Parses the comma-separated "Labels" value of docker ps into a dictionary.</summary>
    public static Dictionary<string, string> ParseLabels(string labels)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in (labels ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq > 0) result[part[..eq]] = part[(eq + 1)..];
        }
        return result;
    }

    /// <summary>True for adapters that do not reach other devices on the LAN: Hyper-V and WSL switches, VPN tunnels,
    /// VirtualBox and VMware host-only adapters, Docker networks and loopback.</summary>
    public static bool IsVirtualAdapter(string name, string description)
    {
        var text = (name + " " + description).ToLowerInvariant();
        string[] markers = ["hyper-v", "vethernet", "wsl", "virtualbox", "vmware", "docker", "loopback", "pseudo", "tap-", "tap ", "wireguard", "tailscale", "zerotier", "vpn", "teredo", "isatap", "npcap", "bluetooth"];
        return markers.Any(text.Contains);
    }

    /// <summary>IPv4 addresses of adapters that are up, not loopback and not virtual, with the adapter name as a label.</summary>
    public static List<(string Address, string Adapter)> LanAddresses()
    {
        var result = new List<(string, string)>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                if (IsVirtualAdapter(nic.Name, nic.Description)) continue;
                foreach (var address in nic.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address.Address)) continue;
                    var bytes = address.Address.GetAddressBytes();
                    if (bytes[0] == 169 && bytes[1] == 254) continue; // link-local: no DHCP lease, not reachable from the LAN
                    result.Add((address.Address.ToString(), nic.Name));
                }
            }
        }
        catch (NetworkInformationException) { }
        return result;
    }

    /// <summary>TCP and UDP ports with a listener on this computer.</summary>
    public static HashSet<int> LocalListeners()
    {
        var ports = new HashSet<int>();
        try
        {
            var props = IPGlobalProperties.GetIPGlobalProperties();
            foreach (var e in props.GetActiveTcpListeners()) ports.Add(e.Port);
            foreach (var e in props.GetActiveUdpListeners()) ports.Add(e.Port);
        }
        catch (NetworkInformationException) { }
        return ports;
    }
}
