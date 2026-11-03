using System.Globalization;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>
/// Everything needed to create (or recreate) one Minecraft server container. Values map one to one onto the
/// documented environment variables of the itzg/minecraft-server and itzg/minecraft-bedrock-server images.
/// </summary>
public sealed class MinecraftServerSpec
{
    public string Name { get; set; } = "survival";
    public MinecraftEdition Edition { get; set; } = MinecraftEdition.Java;
    public string Version { get; set; } = "LATEST";
    /// <summary>itzg TYPE value (Java only).</summary>
    public string Flavour { get; set; } = "VANILLA";
    /// <summary>Server jar URL for TYPE=CUSTOM (CUSTOM_SERVER).</summary>
    public string CustomJarUrl { get; set; } = "";
    public int MemoryGb { get; set; } = 2;
    public bool EulaAccepted { get; set; }
    public string Motd { get; set; } = "A Minecraft server";
    public string Difficulty { get; set; } = "normal";
    public string GameMode { get; set; } = "survival";
    public int MaxPlayers { get; set; } = 10;
    public int ViewDistance { get; set; } = 10;
    public int SimulationDistance { get; set; } = 10;
    public bool Pvp { get; set; } = true;
    public bool OnlineMode { get; set; } = true;
    public bool Whitelist { get; set; }
    public bool EnforceWhitelist { get; set; }
    public int SpawnProtection { get; set; } = 16;
    public string Seed { get; set; } = "";
    public string LevelType { get; set; } = "minecraft:normal";
    public bool Hardcore { get; set; }
    public bool AllowFlight { get; set; }
    public bool CommandBlocks { get; set; }
    public List<string> Ops { get; set; } = new();
    public List<string> WhitelistPlayers { get; set; } = new();
    public List<string> ModrinthProjects { get; set; } = new();
    public List<string> SpigetResources { get; set; } = new();
    public string BindAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 25565;
    public bool RconEnabled { get; set; } = true;
    /// <summary>Environment entries the page does not manage, kept unchanged on recreate.</summary>
    public List<KeyValuePair<string, string>> ExtraEnvironment { get; set; } = new();

    public MinecraftServerSpec Clone()
    {
        var c = (MinecraftServerSpec)MemberwiseClone();
        c.Ops = new(Ops); c.WhitelistPlayers = new(WhitelistPlayers); c.ModrinthProjects = new(ModrinthProjects); c.SpigetResources = new(SpigetResources);
        c.ExtraEnvironment = new(ExtraEnvironment);
        return c;
    }

    public string ContainerName => MinecraftSpecRules.ContainerPrefix + Name;
    public string VolumeName => MinecraftSpecRules.ContainerPrefix + Name + "-data";
    public string Image => MinecraftVersions.Image(Edition, Version);
    public int DefaultPort => Edition == MinecraftEdition.Java ? 25565 : 19132;
}

/// <summary>Flavours, validation, environment mapping and docker argument construction for <see cref="MinecraftServerSpec"/>.</summary>
public static class MinecraftSpecRules
{
    public const string Label = "material-docker-image.minecraft";
    public const string EditionLabel = "material-docker-image.minecraft.edition";
    public const string ContainerPrefix = "mc-";
    public const string Masked = "•••";

    /// <summary>TYPE values documented by itzg/minecraft-server that this page offers.</summary>
    public static readonly IReadOnlyList<string> Flavours = ["VANILLA", "PAPER", "PURPUR", "FABRIC", "FORGE", "NEOFORGE", "QUILT", "SPIGOT", "BUKKIT", "FOLIA", "CUSTOM"];
    public static readonly IReadOnlyList<string> Difficulties = ["peaceful", "easy", "normal", "hard"];
    public static readonly IReadOnlyList<string> GameModes = ["survival", "creative", "adventure", "spectator"];
    public static readonly IReadOnlyList<string> JavaLevelTypes = ["minecraft:normal", "minecraft:flat", "minecraft:large_biomes", "minecraft:amplified"];
    public static readonly IReadOnlyList<string> BedrockLevelTypes = ["DEFAULT", "FLAT", "LEGACY"];

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex ServerName = new("^[a-z0-9][a-z0-9-]{0,30}$", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex Player = new("^[A-Za-z0-9_]{3,16}$", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex Slug = new("^[A-Za-z0-9._:-]{1,80}$", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex ResourceId = new("^[0-9]{1,9}$", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex Ipv4 = new(@"^((25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(25[0-5]|2[0-4]\d|1?\d?\d)$", RegexOptions.CultureInvariant, Timeout);

    public static bool SupportsModrinth(string flavour) => flavour is not ("VANILLA" or "CUSTOM");
    public static bool SupportsSpiget(string flavour) => flavour is "PAPER" or "PURPUR" or "SPIGOT" or "BUKKIT" or "FOLIA";
    /// <summary>Flavours for which the Chunky pre-generator exists as a plugin or mod.</summary>
    public static bool SupportsChunky(string flavour) => flavour is "PAPER" or "PURPUR" or "SPIGOT" or "BUKKIT" or "FOLIA" or "FABRIC" or "FORGE" or "NEOFORGE";
    public static bool IsPluginFlavour(string flavour) => SupportsSpiget(flavour);

    public static string? ValidateName(string? name) =>
        ServerName.IsMatch(name ?? "") ? null : "Use 1 to 31 lowercase letters, digits or hyphens, starting with a letter or digit (for example survival-2).";
    public static string? ValidatePlayer(string? name) =>
        Player.IsMatch(name ?? "") ? null : "Player names are 3 to 16 letters, digits or underscores.";

    /// <summary>Every problem that blocks creating the server, in form order. Empty means valid.</summary>
    public static List<string> Validate(MinecraftServerSpec s)
    {
        var problems = new List<string>();
        if (ValidateName(s.Name) is { } n) problems.Add("Name: " + n);
        if (MinecraftVersions.Validate(s.Edition, s.Version) is { } v) problems.Add("Version: " + v);
        if (s.Edition == MinecraftEdition.Java)
        {
            if (!Flavours.Contains(s.Flavour)) problems.Add("Flavour: choose one of the listed server types.");
            if (s.Flavour == "CUSTOM" && !(Uri.TryCreate(s.CustomJarUrl, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps)) problems.Add("Custom server jar: enter an https:// address of the server jar.");
            if (s.MemoryGb is < 1 or > 16) problems.Add("Memory: choose 1 to 16 GB.");
            if (!JavaLevelTypes.Contains(s.LevelType)) problems.Add("Level type: choose one of the listed world types.");
            if (s.SimulationDistance is < 3 or > 32) problems.Add("Simulation distance: 3 to 32 chunks.");
            if (s.SpawnProtection is < 0 or > 1000) problems.Add("Spawn protection: 0 to 1000 blocks.");
        }
        else
        {
            if (!BedrockLevelTypes.Contains(s.LevelType)) problems.Add("Level type: choose DEFAULT, FLAT or LEGACY.");
            if (s.SimulationDistance is < 4 or > 12) problems.Add("Simulation distance: Bedrock allows 4 to 12 chunks.");
        }
        if (!Difficulties.Contains(s.Difficulty)) problems.Add("Difficulty: choose peaceful, easy, normal or hard.");
        if (!GameModes.Contains(s.GameMode)) problems.Add("Game mode: choose survival, creative, adventure or spectator.");
        if (s.MaxPlayers is < 1 or > 1000) problems.Add("Max players: 1 to 1000.");
        if (s.ViewDistance is < 3 or > 32) problems.Add("View distance: 3 to 32 chunks.");
        if (s.Motd.Contains('\n') || s.Motd.Length > 120) problems.Add("Message of the day: one line, up to 120 characters.");
        if (s.Seed.Length > 64 || s.Seed.Contains('\n')) problems.Add("Seed: one line, up to 64 characters.");
        foreach (var p in s.Ops.Concat(s.WhitelistPlayers)) if (ValidatePlayer(p) != null) { problems.Add($"Player \"{p}\": {ValidatePlayer(p)}"); break; }
        foreach (var p in s.ModrinthProjects) if (!Slug.IsMatch(p)) { problems.Add($"Modrinth project \"{p}\": use the project slug or id, for example chunky or fabric-api."); break; }
        foreach (var p in s.SpigetResources) if (!ResourceId.IsMatch(p)) { problems.Add($"Spigot resource \"{p}\": use the numeric resource id from the SpigotMC address."); break; }
        if (s.ModrinthProjects.Count > 0 && (s.Edition != MinecraftEdition.Java || !SupportsModrinth(s.Flavour))) problems.Add("Modrinth projects need a modded or plugin server type (not Vanilla, Custom or Bedrock).");
        if (s.SpigetResources.Count > 0 && (s.Edition != MinecraftEdition.Java || !SupportsSpiget(s.Flavour))) problems.Add("Spigot resources need Paper, Purpur, Spigot, Bukkit or Folia.");
        if (!Ipv4.IsMatch(s.BindAddress)) problems.Add("Bind address: enter an IPv4 address such as 127.0.0.1 or 0.0.0.0.");
        if (s.Port is < 1 or > 65535) problems.Add("Port: 1 to 65535.");
        if (!s.EulaAccepted) problems.Add("EULA: you must read and accept the Minecraft EULA before the server can start.");
        return problems;
    }

    private static string B(bool value) => value ? "true" : "false";
    private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Environment variables for the container, in a stable order. Never contains the RCON password; that value is only
    /// written to a temporary env file passed with --env-file.
    /// </summary>
    public static List<KeyValuePair<string, string>> Environment(MinecraftServerSpec s)
    {
        var env = new List<KeyValuePair<string, string>>();
        void Add(string k, string v) => env.Add(new(k, v));
        Add("EULA", s.EulaAccepted ? "TRUE" : "FALSE");
        Add("VERSION", s.Version.Trim());
        if (s.Edition == MinecraftEdition.Java)
        {
            Add("TYPE", s.Flavour);
            if (s.Flavour == "CUSTOM") Add("CUSTOM_SERVER", s.CustomJarUrl.Trim());
            Add("MEMORY", I(s.MemoryGb) + "G");
            Add("MOTD", s.Motd);
            Add("DIFFICULTY", s.Difficulty);
            Add("MODE", s.GameMode);
            Add("MAX_PLAYERS", I(s.MaxPlayers));
            Add("VIEW_DISTANCE", I(s.ViewDistance));
            Add("SIMULATION_DISTANCE", I(s.SimulationDistance));
            Add("PVP", B(s.Pvp));
            Add("ONLINE_MODE", B(s.OnlineMode));
            Add("ENABLE_WHITELIST", B(s.Whitelist));
            Add("ENFORCE_WHITELIST", B(s.EnforceWhitelist));
            Add("SPAWN_PROTECTION", I(s.SpawnProtection));
            if (s.Seed.Length > 0) Add("SEED", s.Seed);
            Add("LEVEL_TYPE", s.LevelType);
            Add("HARDCORE", B(s.Hardcore));
            Add("ALLOW_FLIGHT", B(s.AllowFlight));
            Add("ENABLE_COMMAND_BLOCK", B(s.CommandBlocks));
            if (s.Ops.Count > 0) Add("OPS", string.Join(",", s.Ops));
            if (s.WhitelistPlayers.Count > 0) Add("WHITELIST", string.Join(",", s.WhitelistPlayers));
            if (s.ModrinthProjects.Count > 0) Add("MODRINTH_PROJECTS", string.Join(",", s.ModrinthProjects));
            if (s.SpigetResources.Count > 0) Add("SPIGET_RESOURCES", string.Join(",", s.SpigetResources));
            Add("ENABLE_RCON", B(s.RconEnabled));
        }
        else
        {
            Add("SERVER_NAME", s.Motd);
            Add("GAMEMODE", s.GameMode);
            Add("DIFFICULTY", s.Difficulty);
            Add("MAX_PLAYERS", I(s.MaxPlayers));
            Add("VIEW_DISTANCE", I(s.ViewDistance));
            Add("TICK_DISTANCE", I(s.SimulationDistance));
            Add("ONLINE_MODE", B(s.OnlineMode));
            Add("ALLOW_CHEATS", B(s.CommandBlocks));
            if (s.Seed.Length > 0) Add("LEVEL_SEED", s.Seed);
            Add("LEVEL_TYPE", s.LevelType);
            Add("ALLOW_LIST", B(s.Whitelist));
            if (s.WhitelistPlayers.Count > 0) Add("ALLOW_LIST_USERS", string.Join(",", s.WhitelistPlayers));
        }
        foreach (var extra in s.ExtraEnvironment) if (!env.Any(e => e.Key == extra.Key) && extra.Key != "RCON_PASSWORD") env.Add(extra);
        return env;
    }

    public static IReadOnlyList<string> VolumeCreateArguments(MinecraftServerSpec s) =>
        ["volume", "create", "--label", Label + "=" + s.Name, "--label", EditionLabel + "=" + EditionValue(s.Edition), s.VolumeName];

    public static string EditionValue(MinecraftEdition e) => e == MinecraftEdition.Java ? "java" : "bedrock";

    /// <summary>
    /// docker run arguments. When <paramref name="envFile"/> is given it is passed with --env-file (that file carries the
    /// RCON password). <paramref name="containerName"/> overrides the default mc-name (used while recreating).
    /// </summary>
    public static List<string> RunArguments(MinecraftServerSpec s, string? envFile, string? containerName = null)
    {
        var args = new List<string> { "run", "-d", "--name", containerName ?? s.ContainerName,
            "--label", Label + "=" + s.Name, "--label", EditionLabel + "=" + EditionValue(s.Edition),
            "--restart", "unless-stopped" };
        foreach (var (k, v) in Environment(s)) { args.Add("-e"); args.Add(k + "=" + v); }
        if (envFile != null) { args.Add("--env-file"); args.Add(envFile); }
        var port = I(s.Port);
        args.Add("-p");
        args.Add(s.Edition == MinecraftEdition.Java ? $"{s.BindAddress}:{port}:25565" : $"{s.BindAddress}:{port}:19132/udp");
        if (s.Edition == MinecraftEdition.Bedrock) args.Add("-it"); // the Bedrock image needs an interactive TTY for send-command
        args.Add("--mount"); args.Add($"type=volume,source={s.VolumeName},target=/data");
        args.Add(s.Image);
        return args;
    }

    /// <summary>Display text for a command preview: every -e value is shown as NAME=value except secrets, which never appear.</summary>
    public static string Preview(IReadOnlyList<string> args)
    {
        static string Q(string t) => t.Length > 0 && t.All(c => char.IsLetterOrDigit(c) || "-_./:=@,+%".Contains(c)) ? t : "\"" + t.Replace("\"", "\\\"") + "\"";
        var parts = new List<string> { "docker" };
        for (var i = 0; i < args.Count; i++)
        {
            var t = args[i];
            if (i > 0 && args[i - 1] == "--env-file") { parts.Add("<temporary env file: RCON_PASSWORD=" + Masked + ">"); continue; }
            if (i > 0 && args[i - 1] == "-e" && t.StartsWith("RCON_PASSWORD=", StringComparison.Ordinal)) { parts.Add("RCON_PASSWORD=" + Masked); continue; }
            parts.Add(Q(t));
        }
        return string.Join(" ", parts);
    }

    /// <summary>Random RCON password (URL-safe letters and digits, 24 characters).</summary>
    public static string NewPassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(24);
        return new string(bytes.Select(b => alphabet[b % alphabet.Length]).ToArray());
    }

    private static List<string> SplitList(string? value) =>
        (value ?? "").Split([',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>
    /// Rebuilds a spec from a container's labels and environment (docker inspect Config.Env). Unknown variables go to
    /// <see cref="MinecraftServerSpec.ExtraEnvironment"/>; RCON_PASSWORD is returned separately and never stored in the spec.
    /// </summary>
    public static MinecraftServerSpec FromEnvironment(string name, MinecraftEdition edition, IEnumerable<string> env, string? publishedBinding, out string? rconPassword)
    {
        rconPassword = null;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var e in env) { var eq = e.IndexOf('='); if (eq > 0) map[e[..eq]] = e[(eq + 1)..]; }
        var s = new MinecraftServerSpec { Name = name, Edition = edition };
        string Get(string k, string d) => map.TryGetValue(k, out var v) ? v : d;
        int Int(string k, int d) => int.TryParse(Get(k, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : d;
        bool Bool(string k, bool d) => map.TryGetValue(k, out var v) ? v.Equals("true", StringComparison.OrdinalIgnoreCase) : d;
        var managed = new HashSet<string>(StringComparer.Ordinal) { "RCON_PASSWORD" };
        string M(string k, string d) { managed.Add(k); return Get(k, d); }
        int MI(string k, int d) { managed.Add(k); return Int(k, d); }
        bool MB(string k, bool d) { managed.Add(k); return Bool(k, d); }
        if (map.TryGetValue("RCON_PASSWORD", out var pw) && pw.Length > 0) rconPassword = pw;
        s.EulaAccepted = M("EULA", "FALSE").Equals("TRUE", StringComparison.OrdinalIgnoreCase);
        s.Version = M("VERSION", "LATEST");
        if (edition == MinecraftEdition.Java)
        {
            s.Flavour = M("TYPE", "VANILLA").ToUpperInvariant();
            s.CustomJarUrl = M("CUSTOM_SERVER", "");
            var mem = M("MEMORY", "2G");
            s.MemoryGb = int.TryParse(mem.TrimEnd('G', 'g'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var g) ? g : 2;
            s.Motd = M("MOTD", s.Motd);
            s.Difficulty = M("DIFFICULTY", "easy").ToLowerInvariant();
            s.GameMode = M("MODE", "survival").ToLowerInvariant();
            s.MaxPlayers = MI("MAX_PLAYERS", 20);
            s.ViewDistance = MI("VIEW_DISTANCE", 10);
            s.SimulationDistance = MI("SIMULATION_DISTANCE", 10);
            s.Pvp = MB("PVP", true);
            s.OnlineMode = MB("ONLINE_MODE", true);
            s.Whitelist = MB("ENABLE_WHITELIST", false);
            s.EnforceWhitelist = MB("ENFORCE_WHITELIST", false);
            s.SpawnProtection = MI("SPAWN_PROTECTION", 16);
            s.Seed = M("SEED", "");
            s.LevelType = M("LEVEL_TYPE", "minecraft:normal");
            s.Hardcore = MB("HARDCORE", false);
            s.AllowFlight = MB("ALLOW_FLIGHT", false);
            s.CommandBlocks = MB("ENABLE_COMMAND_BLOCK", false);
            s.Ops = SplitList(M("OPS", ""));
            s.WhitelistPlayers = SplitList(M("WHITELIST", ""));
            s.ModrinthProjects = SplitList(M("MODRINTH_PROJECTS", ""));
            s.SpigetResources = SplitList(M("SPIGET_RESOURCES", ""));
            s.RconEnabled = MB("ENABLE_RCON", true);
        }
        else
        {
            s.Motd = M("SERVER_NAME", s.Motd);
            s.GameMode = M("GAMEMODE", "survival").ToLowerInvariant();
            s.Difficulty = M("DIFFICULTY", "easy").ToLowerInvariant();
            s.MaxPlayers = MI("MAX_PLAYERS", 10);
            s.ViewDistance = MI("VIEW_DISTANCE", 10);
            s.SimulationDistance = MI("TICK_DISTANCE", 4);
            s.OnlineMode = MB("ONLINE_MODE", true);
            s.CommandBlocks = MB("ALLOW_CHEATS", false);
            s.Seed = M("LEVEL_SEED", "");
            s.LevelType = M("LEVEL_TYPE", "DEFAULT").ToUpperInvariant();
            s.Whitelist = MB("ALLOW_LIST", false);
            s.WhitelistPlayers = SplitList(M("ALLOW_LIST_USERS", ""));
            s.RconEnabled = false;
            s.LevelType = BedrockLevelTypes.Contains(s.LevelType) ? s.LevelType : "DEFAULT";
        }
        // Image-provided variables (PATH, JAVA_HOME and similar) are not user settings; keep only ones that look like server options.
        foreach (var (k, v) in map)
            if (!managed.Contains(k) && !ImageDefaults.Contains(k)) s.ExtraEnvironment.Add(new(k, v));
        s.Port = s.DefaultPort;
        s.BindAddress = "0.0.0.0";
        if (publishedBinding is { Length: > 0 } binding && ParseBinding(binding) is { } parsed) { s.BindAddress = parsed.Address; s.Port = parsed.Port; }
        return s;
    }

    private static readonly HashSet<string> ImageDefaults = new(StringComparer.Ordinal)
    {
        "PATH", "HOME", "LANG", "LANGUAGE", "LC_ALL", "JAVA_HOME", "JAVA_VERSION", "UID", "GID", "TZ", "HOSTNAME", "TERM",
        "MEMORY_DEFAULT", "ENABLE_RCON_DEFAULT", "SERVER_PORT", "RCON_PORT", "VERSION_DEFAULT", "TYPE_DEFAULT", "ENV", "PACKAGE_BACKUP_URL",
        "APPLY_JAVA_PATCHES", "JAVA_OPTS_DEFAULT", "BOX64_VERSION", "BOX64", "ARCH", "DEBUG_EXEC", "EXTRA_ARGS"
    };

    /// <summary>Parses a docker port binding such as 127.0.0.1:25565 (HostIp:HostPort). Returns null when unparseable.</summary>
    public static (string Address, int Port)? ParseBinding(string binding)
    {
        var colon = binding.LastIndexOf(':');
        if (colon <= 0) return null;
        var address = binding[..colon].Trim('[', ']');
        if (!int.TryParse(binding[(colon + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var port)) return null;
        return (address.Length == 0 || address == "::" ? "0.0.0.0" : address, port);
    }

    /// <summary>Human-readable differences between two specs (environment and port), used before a recreate.</summary>
    public static List<string> Diff(MinecraftServerSpec before, MinecraftServerSpec after)
    {
        var a = Environment(before).ToDictionary(e => e.Key, e => e.Value);
        var b = Environment(after).ToDictionary(e => e.Key, e => e.Value);
        var lines = new List<string>();
        foreach (var key in a.Keys.Union(b.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            a.TryGetValue(key, out var x); b.TryGetValue(key, out var y);
            if (x == y) continue;
            lines.Add(x == null ? $"+ {key}={y}" : y == null ? $"- {key}={x}" : $"~ {key}: {x} → {y}");
        }
        if (before.BindAddress != after.BindAddress || before.Port != after.Port) lines.Add($"~ port: {before.BindAddress}:{before.Port} → {after.BindAddress}:{after.Port}");
        if (before.Image != after.Image) lines.Add($"~ image: {before.Image} → {after.Image}");
        return lines;
    }
}
