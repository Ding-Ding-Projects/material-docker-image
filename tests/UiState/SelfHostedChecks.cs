using System.Diagnostics;
using System.Text.RegularExpressions;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

/// <summary>Checks for the Self-hosted catalogue: pinned images, rendered Compose and .env files, secrets, ports and names.</summary>
public static class SelfHostedChecks
{
    static int count;

    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        count++;
        Console.WriteLine("PASS " + name);
    }

    /// <summary>Every option variant of an app: each database choice, named volumes and a folder.</summary>
    static IEnumerable<(SelfHostedApp App, SelfHostedOptions Options)> Variants()
    {
        foreach (var app in SelfHostedCatalogue.All)
            foreach (var database in app.Databases.Count == 0 ? [SelfHostedDatabase.None] : app.Databases)
                foreach (var storage in new[] { SelfHostedStorage.NamedVolumes, SelfHostedStorage.Folder })
                {
                    var o = SelfHostedRules.Defaults(app, [], "Europe/London");
                    o.Database = database; o.Storage = storage; o.Folder = @"D:\Docker data"; o.PublicHost = "192.168.1.20"; o.TrustedHosts = ["192.168.1.20", "10.0.0.5"];
                    foreach (var port in app.Ports) o.HostPorts[port.Key] = port.DefaultHostPort + 10000; // chosen ports differ from the defaults
                    SelfHostedRules.GenerateSecrets(app, o);
                    yield return (app, o);
                }
    }

    public static int Run()
    {
        count = 0;
        Catalogue();
        Rendering();
        Rules();
        ComposeConfig();
        Localization();
        return count;
    }

    static void Catalogue()
    {
        var all = SelfHostedCatalogue.All;
        Check(all.Count >= 20 && all.Select(a => a.Id).Distinct().Count() == all.Count, $"the catalogue has {all.Count} apps with unique ids");
        string[] required = ["nextcloud", "gitlab", "forgejo", "vaultwarden", "jellyfin", "immich", "paperless", "homeassistant", "homeassistantmaterial", "uptimekuma", "adguardhome", "nginxproxymanager", "grafana", "n8n", "codeserver", "wordpress", "bookstack", "freshrss", "syncthing", "openwebui", "rustfs"];
        Check(required.All(id => SelfHostedCatalogue.Find(id) != null), "the catalogue holds every requested app");
        Check(SelfHostedCatalogue.Categories.All(c => all.Any(a => a.Category == c)), "every category has at least one app");
        var images = all.SelectMany(a => a.AllImages()).Distinct().ToList();
        var floating = images.Where(i => SelfHostedCompose.Tag(i) is "" or "latest" or "stable" or "release" || !Regex.IsMatch(SelfHostedCompose.Tag(i), @"\d")).ToList();
        Check(floating.Count == 0, "every image is pinned to a versioned tag, never latest" + (floating.Count > 0 ? ": " + string.Join(", ", floating) : ""));
        Check(images.Count >= 24, $"the catalogue pins {images.Count} distinct images");
        foreach (var app in all)
        {
            var problems = new List<string>();
            if (app.Description.Length is < 20 or > 120) problems.Add("description length");
            if (app.MinMemoryMb < 128 || app.MinDiskGb < 1) problems.Add("resource needs");
            if (!app.DocsUrl.StartsWith("https://", StringComparison.Ordinal)) problems.Add("docs URL");
            if (app.Licence.Length < 3 || app.FirstLogin.Length < 20) problems.Add("licence or first-login hint");
            if (app.Ports.Count == 0 || app.Ports.Count(p => p.Web) != 1) problems.Add("exactly one web port");
            if (app.Ports.Select(p => p.Key).Distinct().Count() != app.Ports.Count) problems.Add("duplicate port keys");
            if (app.Ports.All(p => p.Key != app.HealthPort) || !app.HealthPath.StartsWith('/')) problems.Add("health check");
            if (app.Volumes.Count == 0) problems.Add("volumes");
            if (app.Ports.Any(p => p.Protocol is not ("tcp" or "udp" or "tcp+udp"))) problems.Add("protocol");
            var services = app.Services(SelfHostedRules.Defaults(app, [], "Etc/UTC")).Select(s => s.Name).ToHashSet();
            if (app.Ports.Any(p => !services.Contains(p.Service)) || app.Volumes.Any(v => !services.Contains(v.Service))) problems.Add("port or volume on an unknown service");
            Check(problems.Count == 0, $"{app.Id}: catalogue entry is complete" + (problems.Count > 0 ? ": " + string.Join(", ", problems) : ""));
        }
        var haMaterial = SelfHostedCatalogue.Find("homeassistantmaterial")!;
        Check(haMaterial.AllImages().SequenceEqual(["ghcr.io/ding-ding-projects/home-assistant-material:" + SelfHostedCompose.Tag(haMaterial.AllImages().First())]) && haMaterial.HealthPath == "/manifest.json"
            && haMaterial.Ports.Single().ContainerPort == 8123 && haMaterial.Volumes.Single().ContainerPath == "/config",
            "Home Assistant Material runs its published image on port 8123 with /config and the manifest health check");
        Check(SelfHostedCatalogue.Find("gitlab")!.MinMemoryMb >= 4096 && SelfHostedCatalogue.Find("gitlab")!.Note!.Contains("memory"), "GitLab states its memory need");
        var immich = SelfHostedCatalogue.Find("immich")!.AllImages();
        Check(immich.Any(i => i.StartsWith("ghcr.io/immich-app/immich-server:")) && immich.Any(i => i.StartsWith("ghcr.io/immich-app/immich-machine-learning:")) && immich.Any(i => i.Contains("vectorchord")) && immich.Any(i => i.StartsWith("valkey/valkey:")),
            "Immich uses the official multi-service layout with the vector extension database");
        Check(SelfHostedCatalogue.Find("nextcloud")!.ImagesFor(SelfHostedDatabase.MariaDb).Contains(SelfHostedCatalogue.MariaDbImage) && SelfHostedCatalogue.Find("nextcloud")!.ImagesFor(SelfHostedDatabase.PostgreSql).Contains(SelfHostedCatalogue.PostgresImage)
            && SelfHostedCatalogue.Find("nextcloud")!.ImagesFor(SelfHostedDatabase.MariaDb).Contains(SelfHostedCatalogue.RedisImage), "Nextcloud offers MariaDB or PostgreSQL, both with Redis");
    }

    static void Rendering()
    {
        foreach (var (app, o) in Variants())
        {
            var files = SelfHostedCompose.Render(app, o);
            var compose = files.Compose;
            var tag = $"{app.Id} ({o.Database}, {o.Storage})";
            var leaked = o.Secrets.Values.Where(v => compose.Contains(v, StringComparison.Ordinal) || files.Settings.Contains(v, StringComparison.Ordinal)).ToList();
            Check(leaked.Count == 0 && o.Secrets.Count == app.SecretsFor(o.Database).Count() && o.Secrets.Values.All(v => files.Env.Contains(v, StringComparison.Ordinal)), tag + ": secrets are only in .env, never in compose.yaml or the settings file");
            var services = app.Services(o);
            var blocks = ServiceBlocks(compose);
            Check(blocks.Count == services.Count && services.All(s => blocks.ContainsKey(s.Name)), tag + ": every service is rendered");
            Check(blocks.Values.All(b => b.Contains("    restart: unless-stopped\n") && b.Contains($"      {SelfHostedRules.Label}: \"{app.Id}\"\n")), tag + ": every service has a restart policy and the self-hosted label");
            Check(!compose.Contains(":latest") && services.All(s => compose.Contains("image: " + SelfHostedCompose.Q(s.Image))), tag + ": images are the pinned references");
            var publishedCount = Regex.Matches(compose, "published: \"(\\d+)\"").Count;
            var expected = app.Ports.Sum(p => p.Protocol.Split('+').Length);
            Check(publishedCount == expected && app.Ports.All(p => compose.Contains($"published: \"{o.Port(app, p.Key)}\"")), tag + ": every container port is published on its chosen host port");
            Check(!Regex.IsMatch(compose, @"host_ip|127\.0\.0\.1:\d+:"), tag + ": ports are published on all interfaces, as Docker Desktop does by default");
            var referenced = Regex.Matches(compose, @"\$\{([A-Z_][A-Z0-9_]*)").Select(m => m.Groups[1].Value).Distinct().ToList();
            var defined = SelfHostedCompose.EnvVariables(app, o).Select(e => e.Key).ToHashSet();
            var missing = referenced.Where(r => !defined.Contains(r)).ToList();
            Check(missing.Count == 0, tag + ": every ${VAR} in compose.yaml is defined in .env" + (missing.Count > 0 ? ": " + string.Join(", ", missing) : ""));
            var unused = app.SecretsFor(o.Database).Where(s => !compose.Contains("${" + s.Key + ":?", StringComparison.Ordinal)).Select(s => s.Key).ToList();
            Check(unused.Count == 0, tag + ": every generated secret is passed to the containers through a .env reference" + (unused.Count > 0 ? ": " + string.Join(", ", unused) : ""));
            var literal = services.SelectMany(s => s.Environment).Where(e => Regex.IsMatch(e.Key, "PASSWORD|PASSWD|SECRET|TOKEN|_KEY$|^APP_KEY$") && !e.Value.Contains("${", StringComparison.Ordinal)).Select(e => e.Key).ToList();
            Check(literal.Count == 0, tag + ": no password, key or token is written into compose.yaml as a literal" + (literal.Count > 0 ? ": " + string.Join(", ", literal) : ""));
            var envKeys = Regex.Matches(files.Env, "^([A-Z_][A-Z0-9_]*)=", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
            Check(envKeys.Count == envKeys.Distinct().Count() && envKeys.Count == defined.Count, tag + ": .env lists each variable once");
            var volumeKeys = app.VolumesFor(o.Database).Where(v => SelfHostedCompose.IsNamed(o, v)).Select(v => v.Key).ToList();
            var binds = app.VolumesFor(o.Database).Where(v => !SelfHostedCompose.IsNamed(o, v)).ToList();
            Check(volumeKeys.All(k => Regex.IsMatch(compose, $"^  {Regex.Escape(k)}:\\n    labels:", RegexOptions.Multiline)) && binds.All(b => compose.Contains("source: " + SelfHostedCompose.Q(SelfHostedCompose.HostFolder(o, b)))),
                tag + ": data uses labelled named volumes or folders under the chosen folder");
            Check(o.Storage == SelfHostedStorage.NamedVolumes ? binds.Count == 0 : app.VolumesFor(o.Database).Where(v => v.Database).All(v => volumeKeys.Contains(v.Key)), tag + ": databases stay in named volumes in folder mode");
            Check(SelfHostedRules.ValidateProject(o.Project, []) == null && compose.Contains("name: \"" + o.Project + "\"\n"), tag + ": the project name is valid and set");
            var saved = SelfHostedCompose.ReadSaved(files.Settings);
            Check(saved != null && saved.App == app.Id && saved.Project == o.Project && SelfHostedCompose.ComposeYaml(app, saved.ToOptions()) == compose, tag + ": the saved settings re-render the same compose.yaml");
            var masked = SelfHostedCompose.MaskedEnv(app, o);
            Check(o.Secrets.Values.All(v => !masked.Contains(v, StringComparison.Ordinal)) && SelfHostedRules.Mask(files.Env, o).Contains("••••••••") == (o.Secrets.Count > 0), tag + ": review and masked text never show a secret");
        }
        var nc = SelfHostedCatalogue.Find("nextcloud")!;
        var ncOptions = SelfHostedRules.Defaults(nc, [], "Asia/Hong_Kong");
        ncOptions.Database = SelfHostedDatabase.PostgreSql;
        SelfHostedRules.GenerateSecrets(nc, ncOptions);
        Check(!ncOptions.Secrets.ContainsKey("DB_ROOT_PASSWORD") && SelfHostedCompose.ComposeYaml(nc, ncOptions).Contains("/var/lib/postgresql/data") && !SelfHostedCompose.ComposeYaml(nc, ncOptions).Contains("/var/lib/mysql"), "Nextcloud with PostgreSQL has no MariaDB root password or MariaDB volume");
        var gitlab = SelfHostedCatalogue.Find("gitlab")!;
        var gl = SelfHostedCompose.ComposeYaml(gitlab, SelfHostedRules.Defaults(gitlab, [], "Etc/UTC"));
        Check(gl.Contains("external_url 'http://${PUBLIC_HOST}:${PORT_WEB}'") && gl.Contains("gitlab_shell_ssh_port'] = ${PORT_SSH}") && gl.Contains("listen_port'] = 80"), "GitLab sets external_url from the chosen host and port and keeps nginx on port 80 inside");
        var adguard = SelfHostedCatalogue.Find("adguardhome")!;
        var ag = SelfHostedCompose.ComposeYaml(adguard, SelfHostedRules.Defaults(adguard, [], "Etc/UTC"));
        Check(Regex.Matches(ag, "target: 53\\n        published: \"53\"\\n        protocol: (tcp|udp)").Count == 2, "a tcp+udp port is published for both protocols");
        Check(SelfHostedCompose.EnvValue("Europe/London") == "Europe/London" && SelfHostedCompose.EnvValue("localhost 127.0.0.1") == "'localhost 127.0.0.1'" && SelfHostedCompose.EnvValue("base64:ab+/=") == "base64:ab+/=", ".env values are plain tokens or single-quoted literals");
        Check(Throws(() => SelfHostedCompose.EnvValue("it's")) && Throws(() => SelfHostedCompose.EnvValue("a\nb")), ".env values with quotes or line breaks are refused");
        Check(SelfHostedCompose.Repository("ghcr.io/immich-app/postgres:14-x@sha256:ab") == "ghcr.io/immich-app/postgres" && SelfHostedCompose.Tag("localhost:5000/app:1.2") == "1.2" && SelfHostedCompose.Tag("localhost:5000/app") == "", "image repository and tag are split at the last colon after the last slash");
        var saved1 = SelfHostedCompose.Saved(gitlab, SelfHostedRules.Defaults(gitlab, [], "Etc/UTC"), DateTimeOffset.UnixEpoch) with { Images = ["gitlab/gitlab-ce:18.0.0-ce.0"] };
        var changes = SelfHostedCompose.ImageChanges(gitlab, saved1);
        Check(changes.Count == 1 && changes[0].Current == "gitlab/gitlab-ce:18.0.0-ce.0" && changes[0].Next == gitlab.AllImages()[0] && SelfHostedCompose.ImageChanges(gitlab, saved1 with { Images = [.. gitlab.AllImages()] }).Count == 0, "Update lists exactly the images whose pinned tag changed");
        var args = SelfHostedCompose.ComposeArgs("demo", @"C:\x\demo", "up", "-d");
        Check(args.SequenceEqual(new[] { "compose", "-p", "demo", "-f", Path.Combine(@"C:\x\demo", "compose.yaml"), "--env-file", Path.Combine(@"C:\x\demo", ".env"), "up", "-d" }), "deploy runs compose with the project name, compose.yaml and .env as separate arguments");
    }

    static bool Throws(Action a) { try { a(); return false; } catch (ArgumentException) { return true; } }

    static Dictionary<string, string> ServiceBlocks(string compose)
    {
        var start = compose.IndexOf("services:\n", StringComparison.Ordinal) + "services:\n".Length;
        var end = compose.IndexOf("\nvolumes:\n", start, StringComparison.Ordinal);
        var section = end < 0 ? compose[start..] : compose[start..(end + 1)];
        var result = new Dictionary<string, string>();
        foreach (Match m in Regex.Matches(section, @"^  ([a-z0-9-]+):\n((?:    .*\n|      .*\n)*)", RegexOptions.Multiline)) result[m.Groups[1].Value] = m.Groups[2].Value;
        return result;
    }

    static void Rules()
    {
        Check(SelfHostedRules.ValidateProject("nextcloud", []) == null && SelfHostedRules.ValidateProject("my_cloud-2", []) == null, "valid project names are accepted");
        Check(new[] { "", "Nextcloud", "-cloud", "a b", "cloud!", new string('a', 64) }.All(n => SelfHostedRules.ValidateProject(n, []) != null), "invalid project names are refused");
        Check(SelfHostedRules.ValidateProject("cloud", ["cloud"]) != null, "a project name already used on the engine is refused");
        var nc = SelfHostedCatalogue.Find("nextcloud")!;
        var defaults = SelfHostedRules.Defaults(nc, ["nextcloud", "nextcloud-2"], "Asia/Hong_Kong");
        Check(defaults.Project == "nextcloud-3" && defaults.TimeZone == "Asia/Hong_Kong" && defaults.Database == SelfHostedDatabase.MariaDb && defaults.AdminUser == "admin" && defaults.Port(nc, "web") == 8080, "defaults pick a unique project, the system time zone, the first database and default ports");
        Check(SelfHostedRules.Defaults(nc, [], "not a zone!").TimeZone == "Etc/UTC", "an unusable system time zone falls back to Etc/UTC");
        var used = new HashSet<int> { 8080 };
        var inUseProblems = SelfHostedRules.ValidatePorts(nc, defaults, used);
        Check(inUseProblems.Count == 1 && inUseProblems[0].Contains("8080"), "a host port in use is reported");
        var gitlab = SelfHostedCatalogue.Find("gitlab")!;
        var go = SelfHostedRules.Defaults(gitlab, [], "Etc/UTC");
        go.HostPorts["ssh"] = go.HostPorts["web"];
        Check(SelfHostedRules.ValidatePorts(gitlab, go, new HashSet<int>()).Any(p => p.Contains("already used by")), "two ports of one app on the same host port are refused");
        go.HostPorts["ssh"] = 70000;
        Check(SelfHostedRules.ValidatePorts(gitlab, go, new HashSet<int>()).Any(p => p.Contains("between 1 and 65535")), "an out-of-range host port is refused");
        Check(SelfHostedRules.ValidateAdmin(nc, "admin") == null && SelfHostedRules.ValidateAdmin(nc, "a") != null && SelfHostedRules.ValidateAdmin(SelfHostedCatalogue.Find("nginxproxymanager")!, "admin") != null && SelfHostedRules.ValidateAdmin(SelfHostedCatalogue.Find("nginxproxymanager")!, "me@example.org") == null, "administrator names and e-mail addresses are validated per app");
        Check(SelfHostedRules.ValidateFolder(@"D:\Docker data") == null && SelfHostedRules.ValidateFolder("data") != null && SelfHostedRules.ValidateFolder("D:\\a$b") != null, "the data folder must be a full path without quotes or $");
        Check(SelfHostedRules.ValidateTimeZone("America/Argentina/Buenos_Aires") == null && SelfHostedRules.ValidateTimeZone("Etc/GMT+8") == null && SelfHostedRules.ValidateTimeZone("x;rm") != null, "time zones are IANA names");
        var secrets = Enumerable.Range(0, 50).Select(_ => SelfHostedRules.GenerateSecret(32)).ToList();
        Check(secrets.Distinct().Count() == 50 && secrets.All(s => s.Length == 32 && s.All(c => SelfHostedRules.SecretAlphabet.Contains(c))), "generated secrets are 32 random letters and digits and never repeat");
        Check(SelfHostedRules.GenerateSecret(4).Length == 16, "secrets are never shorter than 16 characters");
        var bs = SelfHostedCatalogue.Find("bookstack")!;
        var bo = SelfHostedRules.Defaults(bs, [], "Etc/UTC");
        SelfHostedRules.GenerateSecrets(bs, bo);
        Check(bo.Secrets["APP_KEY"].StartsWith("base64:") && Convert.FromBase64String(bo.Secrets["APP_KEY"][7..]).Length == 32, "BookStack gets a Laravel base64 application key of 32 random bytes");
        var keep = bo.Secrets["DB_PASSWORD"];
        SelfHostedRules.GenerateSecrets(bs, bo);
        Check(bo.Secrets["DB_PASSWORD"] == keep, "generating again keeps secrets that already exist");
        Check(SelfHostedRules.Validate(bs, bo, [], new HashSet<int>()).Count == 0 && SelfHostedRules.Validate(bs, new SelfHostedOptions { Project = "x" }, [], new HashSet<int>()).Any(p => p.StartsWith("Secrets:")), "complete options validate and missing secrets are reported");
        Check(SelfHostedRules.PublishedPort("0.0.0.0:8080->80/tcp, [::]:8080->80/tcp", 80) == 8080 && SelfHostedRules.PublishedPort("0.0.0.0:2224->22/tcp, 0.0.0.0:8929->80/tcp", 22) == 2224 && SelfHostedRules.PublishedPort("80/tcp", 80) == null, "published host ports are read from docker ps");
        Check(SelfHostedRules.ParseSize("50MiB") == 50 * 1024 * 1024 && SelfHostedRules.ParseSize("12kB") == 12000 && SelfHostedRules.ParseSize("1.5GiB") == 1.5 * 1024 * 1024 * 1024 && SelfHostedRules.ParseSize("0B") == 0 && SelfHostedRules.ParseSize("n/a") == null, "docker stats memory sizes are read in binary and decimal units");
        Check(SelfHostedRules.MemoryText(512) == "512 MB" && SelfHostedRules.MemoryText(2048) == "2 GB" && SelfHostedRules.MemoryText(1536) == "1.5 GB", "memory needs read as whole megabytes or gigabytes");
        var labels = SelfHostedRules.ParseLabels("com.docker.compose.project=cloud,material-docker-image.selfhosted=nextcloud,com.docker.compose.service=app");
        Check(labels["com.docker.compose.project"] == "cloud" && labels[SelfHostedRules.Label] == "nextcloud" && labels.Count == 3, "container labels are parsed");
        Check(SelfHostedRules.IsVirtualAdapter("vEthernet (Default Switch)", "Hyper-V Virtual Ethernet Adapter #2") && SelfHostedRules.IsVirtualAdapter("vEthernet (WSL (Hyper-V firewall))", "Hyper-V Virtual Ethernet Adapter") && SelfHostedRules.IsVirtualAdapter("Ethernet 3", "VirtualBox Host-Only Ethernet Adapter") && SelfHostedRules.IsVirtualAdapter("Tailscale", "Tailscale Tunnel")
            && !SelfHostedRules.IsVirtualAdapter("Wi-Fi", "Intel(R) Wi-Fi 6E AX211 160MHz") && !SelfHostedRules.IsVirtualAdapter("Ethernet", "Realtek PCIe GbE Family Controller"), "virtual, VPN and container adapters are left out of LAN addresses");
        Check(SelfHostedRules.LanAddresses().All(a => System.Net.IPAddress.TryParse(a.Address, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !a.Address.StartsWith("127.") && !a.Address.StartsWith("169.254.")), "LAN addresses are real IPv4 addresses, never loopback or link-local");
        var masked = SelfHostedRules.Mask("up -d PASSWORD=abcdefghijklmnop1234", new SelfHostedOptions { Secrets = { ["P"] = "abcdefghijklmnop1234" } });
        Check(!masked.Contains("abcdefghijklmnop1234") && masked.Contains("••••••••"), "secret values are masked in any text");
    }

    /// <summary>docker compose config accepts every rendered variant (only when the docker CLI with Compose is available; it needs no engine).</summary>
    static void ComposeConfig()
    {
        if (!DockerComposeAvailable()) { Console.WriteLine("SKIP docker compose config: the docker CLI with Compose is not available; structural checks above still ran"); return; }
        var root = Path.Combine(Path.GetTempPath(), "mdi-selfhosted-" + Guid.NewGuid().ToString("N"));
        try
        {
            var checkedCount = 0;
            foreach (var (app, o) in Variants())
            {
                var dir = Path.Combine(root, $"{app.Id}-{o.Database}-{o.Storage}".ToLowerInvariant());
                Directory.CreateDirectory(dir);
                o.Folder = Path.Combine(root, "data");
                var files = SelfHostedCompose.Render(app, o);
                File.WriteAllText(Path.Combine(dir, "compose.yaml"), files.Compose);
                File.WriteAllText(Path.Combine(dir, ".env"), files.Env);
                var (exit, output, error) = Run("docker", SelfHostedCompose.ComposeArgs(o.Project, dir, "config", "--quiet"));
                Check(exit == 0, $"{app.Id} ({o.Database}, {o.Storage}): docker compose config accepts the rendered project" + (exit != 0 ? ": " + error.Trim() + output.Trim() : ""));
                checkedCount++;
            }
            // A Compose file without its .env must be refused with the variable named, so a missing secret never starts an app with an empty password.
            var nc = SelfHostedCatalogue.Find("nextcloud")!;
            var no = SelfHostedRules.Defaults(nc, [], "Etc/UTC");
            var bare = Path.Combine(root, "bare");
            Directory.CreateDirectory(bare);
            File.WriteAllText(Path.Combine(bare, "compose.yaml"), SelfHostedCompose.ComposeYaml(nc, no));
            File.WriteAllText(Path.Combine(bare, ".env"), "TZ=Etc/UTC\n");
            var (bareExit, _, bareError) = Run("docker", SelfHostedCompose.ComposeArgs("bare", bare, "config", "--quiet"));
            Check(bareExit != 0 && bareError.Contains("is not set in .env"), "Compose refuses a project whose .env lacks a referenced secret");
            Check(checkedCount > SelfHostedCatalogue.All.Count, $"docker compose config checked {checkedCount} variants");
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }

    static bool DockerComposeAvailable()
    {
        try { return Run("docker", ["compose", "version"]).Exit == 0; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    static (int Exit, string Output, string Error) Run(string file, IEnumerable<string> args)
    {
        var info = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        var output = p.StandardOutput.ReadToEndAsync();
        var error = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(60_000)) { p.Kill(true); return (-1, "", "timed out"); }
        return (p.ExitCode, output.Result, error.Result);
    }

    static void Localization()
    {
        var prior = Loc.Language;
        try
        {
            Loc.Language = "Cantonese";
            bool Cjk(string t) => t.Any(c => c >= '一' && c <= '鿿');
            var texts = new List<string> { "Self-hosted" };
            foreach (var app in SelfHostedCatalogue.All)
            {
                texts.Add(app.Description); texts.Add(app.FirstLogin); texts.Add(app.CategoryText);
                if (app.Note != null) texts.Add(app.Note);
                texts.AddRange(app.Ports.Select(p => p.Purpose));
                texts.AddRange(app.Volumes.Select(v => v.Purpose));
                texts.AddRange(app.Secrets.Select(s => s.Purpose));
            }
            var untranslated = texts.Distinct().Where(t => !Cjk(Loc.T(t))).ToList();
            Check(untranslated.Count == 0, "every catalogue description, hint, note, category and purpose has Cantonese" + (untranslated.Count > 0 ? ": " + string.Join(" | ", untranslated.Take(6)) : ""));
            var problem = Loc.T("Project name: A Compose project with this name already exists on this engine.");
            var port = Loc.T("Web interface: port 8080 is already in use on this engine or computer.");
            Check(Cjk(problem) && Cjk(port) && port.Contains("8080"), "validation messages are translated with their values kept");
        }
        finally { Loc.Language = prior; }
    }
}
