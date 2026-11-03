using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Typed form sections for a <see cref="MinecraftServerSpec"/>, shared by the create wizard and the Properties tab. Each
/// section edits the spec in place and calls <c>changed</c> after every edit.
/// </summary>
internal static class MinecraftSpecEditor
{
    private static readonly Dictionary<string, string> FlavourHelp = new()
    {
        ["VANILLA"] = "The official server from Mojang. No plugins or mods.",
        ["PAPER"] = "High-performance fork of Spigot. Runs Bukkit/Spigot/Paper plugins. The usual choice for plugins.",
        ["PURPUR"] = "Paper fork with extra gameplay options. Runs Paper plugins.",
        ["FOLIA"] = "Paper fork that ticks regions in parallel. Many plugins are not compatible.",
        ["SPIGOT"] = "Plugin server built from BuildTools. Paper is usually faster.",
        ["BUKKIT"] = "The original plugin API server (CraftBukkit).",
        ["FABRIC"] = "Lightweight mod loader. Add mods from Modrinth (for example fabric-api).",
        ["QUILT"] = "Fabric-compatible mod loader.",
        ["FORGE"] = "Long-standing mod loader for large mod packs.",
        ["NEOFORGE"] = "Community fork of Forge for 1.20.1 and newer.",
        ["CUSTOM"] = "Any server jar you provide by https address (CUSTOM_SERVER).",
    };

    public static FrameworkElement VersionSection(MinecraftServerSpec spec, Func<string?> context, Action changed)
    {
        var picker = new MinecraftVersionPicker(spec.Edition, spec.Version, context, v => { spec.Version = v; changed(); });
        if (spec.Edition == MinecraftEdition.Bedrock)
            return MinecraftControls.Section("Version", "Bedrock Dedicated Server from itzg/minecraft-bedrock-server.", picker);
        var help = Ui.Caption(FlavourHelp.GetValueOrDefault(spec.Flavour, ""));
        var url = MinecraftControls.Text("Custom server jar address", spec.CustomJarUrl, v => { spec.CustomJarUrl = v.Trim(); changed(); }, "An https:// address the image downloads the server jar from (CUSTOM_SERVER). Only used with the Custom type.", "https://example.com/server.jar", true);
        url.Visibility = spec.Flavour == "CUSTOM" ? Visibility.Visible : Visibility.Collapsed;
        var combo = Ui.Combo(MinecraftSpecRules.Flavours, spec.Flavour);
        combo.Width = 260; combo.HorizontalAlignment = HorizontalAlignment.Left;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not string f) return;
            spec.Flavour = f;
            help.Text = Loc.T(FlavourHelp.GetValueOrDefault(f, ""));
            url.Visibility = f == "CUSTOM" ? Visibility.Visible : Visibility.Collapsed;
            changed();
        };
        var type = Ui.Field("Server type", combo, "The TYPE the itzg image installs. It decides whether plugins (Paper family) or mods (Fabric, Forge family) can be added.", "PAPER for plugins\nFABRIC for mods");
        return MinecraftControls.Section("Version and server type", "The image downloads the chosen server software when the container first starts.", picker, type, help, url);
    }

    public static FrameworkElement ResourcesSection(MinecraftServerSpec spec, Action changed)
    {
        var children = new List<UIElement>();
        if (spec.Edition == MinecraftEdition.Java)
        {
            var slider = new Slider { Minimum = 1, Maximum = 16, Value = spec.MemoryGb, IsSnapToTickEnabled = true, TickFrequency = 1, Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
            var value = Ui.Text(spec.MemoryGb + " GB", 14, FontWeights.SemiBold);
            slider.ValueChanged += (_, _) => { spec.MemoryGb = (int)slider.Value; value.Text = spec.MemoryGb + " GB"; changed(); };
            children.Add(Ui.Field("Memory", Ui.HStack(12, slider, value), "Java heap size passed as MEMORY (both initial and maximum). 2 GB suits a few players on Vanilla or Paper; mod packs often need 6 GB or more. The engine host needs this much free memory plus some overhead.", "MEMORY=4G"));
        }
        else children.Add(Ui.Caption("Bedrock Dedicated Server manages its own memory; there is no heap setting."));

        var warn = Ui.Card(Ui.Columns(10, [0, 1], Ui.Icon(Icons.Warning, 20, true, "OnWarningContainer"), Ui.Text("All interfaces (0.0.0.0) makes the server reachable from other computers on the network and, with port forwarding, the internet. Keep online mode and a whitelist on.", 13, null, "OnWarningContainer", true, true)), 12, "WarningContainer", 12);
        void UpdateWarn() => warn.Visibility = spec.BindAddress == "0.0.0.0" ? Visibility.Visible : Visibility.Collapsed;
        var custom = Ui.Input(spec.BindAddress is "127.0.0.1" or "0.0.0.0" ? "" : spec.BindAddress, "Specific address", true);
        custom.Width = 200; custom.HorizontalAlignment = HorizontalAlignment.Left;
        var mode = spec.BindAddress == "127.0.0.1" ? "This computer only" : spec.BindAddress == "0.0.0.0" ? "All interfaces" : "Specific address";
        custom.Visibility = mode == "Specific address" ? Visibility.Visible : Visibility.Collapsed;
        custom.TextChanged += (_, _) => { spec.BindAddress = custom.Text.Trim(); UpdateWarn(); changed(); };
        var seg = Ui.Segmented(["This computer only", "All interfaces", "Specific address"], mode, m =>
        {
            custom.Visibility = m == "Specific address" ? Visibility.Visible : Visibility.Collapsed;
            spec.BindAddress = m == "This computer only" ? "127.0.0.1" : m == "All interfaces" ? "0.0.0.0" : custom.Text.Trim();
            UpdateWarn(); changed();
        });
        seg.MaxWidth = 520; seg.HorizontalAlignment = HorizontalAlignment.Left;
        UpdateWarn();
        children.Add(Ui.Field("Listen on", Ui.VStack(8, seg, custom), "Host address the port is published on. This computer only (127.0.0.1) is the safe default; players on this computer connect to localhost. With a remote engine context this is the engine host, not this computer.", spec.Edition == MinecraftEdition.Java ? "-p 127.0.0.1:25565:25565" : "-p 127.0.0.1:19132:19132/udp"));
        children.Add(warn);
        children.Add(MinecraftControls.Number("Port", spec.Port, 1, 65535, v => { spec.Port = v; changed(); }, spec.Edition == MinecraftEdition.Java ? "Host TCP port players connect to. 25565 is the Minecraft default, so players can omit it." : "Host UDP port players connect to. 19132 is the Bedrock default.", spec.Edition == MinecraftEdition.Java ? "25565" : "19132"));
        if (spec.Edition == MinecraftEdition.Java)
            children.Add(Ui.Switch("Remote console (RCON)", spec.RconEnabled, v => { spec.RconEnabled = v; changed(); }, "Needed for the console, players, graceful saves, backups without pauses and world pre-generation. The port is not published; rcon-cli runs inside the container. A random password is generated and passed through a temporary env file."));
        return MinecraftControls.Section("Resources and network", null, children.ToArray());
    }

    public static FrameworkElement GameplaySection(MinecraftServerSpec spec, Action changed)
    {
        var java = spec.Edition == MinecraftEdition.Java;
        var motd = MinecraftControls.Text(java ? "Message of the day" : "Server name", spec.Motd, v => { spec.Motd = v; changed(); }, java ? "Shown under the server name in the multiplayer list (MOTD)." : "Name shown in the Bedrock server list (SERVER_NAME).", "Friday night survival");
        var difficulty = Ui.Segmented(MinecraftSpecRules.Difficulties, spec.Difficulty, v => { spec.Difficulty = v; changed(); }, false);
        difficulty.MaxWidth = 460; difficulty.HorizontalAlignment = HorizontalAlignment.Left;
        var mode = Ui.Segmented(MinecraftSpecRules.GameModes, spec.GameMode, v => { spec.GameMode = v; changed(); }, false);
        mode.MaxWidth = 520; mode.HorizontalAlignment = HorizontalAlignment.Left;
        var levelTypes = java ? MinecraftSpecRules.JavaLevelTypes : MinecraftSpecRules.BedrockLevelTypes;
        var level = Ui.Combo(levelTypes, levelTypes.Contains(spec.LevelType) ? spec.LevelType : levelTypes[0]);
        level.Width = 260; level.HorizontalAlignment = HorizontalAlignment.Left;
        level.SelectionChanged += (_, _) => { if (level.SelectedItem is string s) { spec.LevelType = s; changed(); } };
        var seedBox = Ui.Input(spec.Seed, "Seed", true);
        seedBox.TextChanged += (_, _) => { spec.Seed = seedBox.Text; changed(); };
        var random = Ui.IconButton(Icons.Cached, "Random seed", () => seedBox.Text = System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue).ToString(System.Globalization.CultureInfo.InvariantCulture));
        var seedRow = new DockPanel(); seedRow.Children.Add(random.Dock(Dock.Right)); seedRow.Children.Add(seedBox.Margin(0, 0, 8, 0));

        var fields = new List<FrameworkElement>
        {
            MinecraftControls.Number("Max players", spec.MaxPlayers, 1, 1000, v => { spec.MaxPlayers = v; changed(); }, "Most players allowed online at once (MAX_PLAYERS).", "10"),
            MinecraftControls.Number("View distance", spec.ViewDistance, 3, 32, v => { spec.ViewDistance = v; changed(); }, "How many chunks around each player are sent to clients (VIEW_DISTANCE). Higher values use more memory and bandwidth.", "10", "chunks"),
            java
                ? MinecraftControls.Number("Simulation distance", spec.SimulationDistance, 3, 32, v => { spec.SimulationDistance = v; changed(); }, "How many chunks around each player are ticked: crops grow and mobs move (SIMULATION_DISTANCE).", "10", "chunks")
                : MinecraftControls.Number("Tick distance", spec.SimulationDistance, 4, 12, v => { spec.SimulationDistance = v; changed(); }, "How many chunks around each player are ticked (TICK_DISTANCE).", "4", "chunks"),
        };
        if (java) fields.Add(MinecraftControls.Number("Spawn protection", spec.SpawnProtection, 0, 1000, v => { spec.SpawnProtection = v; changed(); }, "Radius in blocks around spawn that only operators can change (SPAWN_PROTECTION). 0 turns it off.", "16", "blocks"));

        var switches = new WrapPanel();
        void Sw(string label, bool value, Action<bool> set, string help)
        {
            var s = Ui.Switch(label, value, v => { set(v); changed(); }, help);
            s.Width = 300; s.Margin = new Thickness(0, 0, 16, 12); s.VerticalAlignment = VerticalAlignment.Top;
            switches.Children.Add(s);
        }
        if (java) Sw("PvP", spec.Pvp, v => spec.Pvp = v, "Players can damage each other (PVP).");
        Sw("Online mode", spec.OnlineMode, v => spec.OnlineMode = v, "Check accounts with Microsoft. Turn off only for offline LAN play; anyone could then join with any name.");
        Sw(java ? "Whitelist" : "Allow list", spec.Whitelist, v => spec.Whitelist = v, java ? "Only listed players can join (ENABLE_WHITELIST)." : "Only listed players can join (ALLOW_LIST).");
        if (java) Sw("Enforce whitelist", spec.EnforceWhitelist, v => spec.EnforceWhitelist = v, "Kick online players who are removed from the whitelist (ENFORCE_WHITELIST).");
        if (java) Sw("Hardcore", spec.Hardcore, v => spec.Hardcore = v, "Players are banned or put in spectator mode when they die (HARDCORE). Applies when the world is first created.");
        if (java) Sw("Allow flight", spec.AllowFlight, v => spec.AllowFlight = v, "Do not kick players for flying; needed by some mods and plugins (ALLOW_FLIGHT).");
        Sw(java ? "Command blocks" : "Cheats", spec.CommandBlocks, v => spec.CommandBlocks = v, java ? "Allow command blocks to run (ENABLE_COMMAND_BLOCK)." : "Allow cheat commands (ALLOW_CHEATS).");

        return MinecraftControls.Section("World and gameplay", "Seed and level type apply when the world is first generated; changing them later does not regenerate an existing world.",
            motd,
            Ui.Field("Difficulty", difficulty, "Mob damage and hunger. Peaceful removes hostile mobs.", "normal"),
            Ui.Field("Game mode", mode, "Default game mode for new players.", "survival"),
            MinecraftControls.Grid2(
                Ui.Field("Level type", level, java ? "World preset used when the world is first generated (LEVEL_TYPE)." : "World generator used when the world is first generated (LEVEL_TYPE).", java ? "minecraft:flat" : "FLAT"),
                Ui.Field("Seed", seedRow, "Text or number that decides terrain generation (" + (java ? "SEED" : "LEVEL_SEED") + "). Leave empty for a random world.", "-4172144997902289642")),
            MinecraftControls.Grid2(fields.ToArray()),
            switches);
    }

    public static FrameworkElement PlayersSection(MinecraftServerSpec spec, Action changed)
    {
        var java = spec.Edition == MinecraftEdition.Java;
        var children = new List<UIElement>();
        if (java)
            children.Add(Ui.Field("Operators", new MinecraftListRows(spec.Ops, "Player name", "Add operator", MinecraftSpecRules.ValidatePlayer, v => { spec.Ops = v; changed(); }), "Players with full command access (OPS). Applied when the container starts.", "Steve"));
        else
            children.Add(Ui.Caption("Bedrock operators are identified by Xbox user ids (XUIDs). Add them in the console with the op command once the player has joined."));
        children.Add(Ui.Field(java ? "Whitelisted players" : "Allow-listed players", new MinecraftListRows(spec.WhitelistPlayers, "Player name", "Add player", MinecraftSpecRules.ValidatePlayer, v => { spec.WhitelistPlayers = v; changed(); }), java ? "Players allowed to join when the whitelist is on (WHITELIST)." : "Players allowed to join when the allow list is on (ALLOW_LIST_USERS).", "Alex"));
        if (java)
        {
            var modrinthNote = Ui.Caption("");
            var spigetNote = Ui.Caption("");
            void Notes()
            {
                modrinthNote.Text = Loc.T(MinecraftSpecRules.SupportsModrinth(spec.Flavour) ? "Available for " + spec.Flavour + "." : "Not available for " + spec.Flavour + ": choose Paper, Purpur, Folia, Spigot, Bukkit, Fabric, Quilt, Forge or NeoForge.");
                spigetNote.Text = Loc.T(MinecraftSpecRules.SupportsSpiget(spec.Flavour) ? "Available for " + spec.Flavour + "." : "Not available for " + spec.Flavour + ": choose Paper, Purpur, Folia, Spigot or Bukkit.");
            }
            Notes();
            children.Add(Ui.VStack(4, Ui.Field("Modrinth projects", new MinecraftListRows(spec.ModrinthProjects, "Project slug", "Add project", v => System.Text.RegularExpressions.Regex.IsMatch(v, "^[A-Za-z0-9._:-]{1,80}$") ? null : "Use the project slug or id from its Modrinth address.", v => { spec.ModrinthProjects = v; changed(); Notes(); }), "Mods or plugins downloaded from Modrinth when the container starts (MODRINTH_PROJECTS). The image picks the build matching the server version and loader.", "chunky\nfabric-api\nluckperms"), modrinthNote));
            children.Add(Ui.VStack(4, Ui.Field("Spigot resources", new MinecraftListRows(spec.SpigetResources, "Resource id", "Add resource", v => System.Text.RegularExpressions.Regex.IsMatch(v, "^[0-9]{1,9}$") ? null : "Use the number at the end of the SpigotMC resource address.", v => { spec.SpigetResources = v; changed(); Notes(); }), "Plugins downloaded from SpigotMC through the Spiget API when the container starts (SPIGET_RESOURCES).", "9089 (EssentialsX)"), spigetNote));
        }
        return MinecraftControls.Section("Players and add-ons", null, children.ToArray());
    }
}
