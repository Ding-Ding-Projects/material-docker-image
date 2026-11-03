using System.Globalization;
using System.Text;
using System.Text.Json;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Engine operations for Minecraft servers. Every call is bound to the engine context the caller loaded its list from and
/// runs the docker CLI with an argument list (never a shell on this computer).
/// </summary>
internal static class MinecraftOps
{
    private static AppHost Host => AppHost.Current;
    public const string HelperImage = "busybox:1.36";

    /// <summary>Loads every container and volume carrying the page label.</summary>
    public static async Task<(List<MinecraftServer> Servers, List<string> OrphanVolumes)> LoadAsync(string? context)
    {
        var rows = await Host.ReadRows(["container", "ls", "--all", "--no-trunc", "--filter", "label=" + MinecraftSpecRules.Label, "--format", "{{json .}}"], context);
        var ids = rows.Select(r => DockerJson.Text(r, "ID")).Where(id => id.Length > 0).ToList();
        var servers = new List<MinecraftServer>();
        if (ids.Count > 0)
        {
            var inspect = await Host.Read(["container", "inspect", .. ids], context);
            if (!inspect.Success) throw new InvalidOperationException(inspect.StandardError.Trim().Length > 0 ? inspect.StandardError.Trim() : "docker container inspect failed.");
            using var doc = JsonDocument.Parse(inspect.StandardOutput);
            foreach (var c in doc.RootElement.EnumerateArray())
                if (MinecraftServer.FromInspect(c) is { } server) servers.Add(server);
        }
        var volumes = await Host.ReadRows(["volume", "ls", "--filter", "label=" + MinecraftSpecRules.Label, "--format", "{{json .}}"], context);
        var used = servers.Select(s => s.VolumeName).ToHashSet(StringComparer.Ordinal);
        var orphans = volumes.Select(v => DockerJson.Text(v, "Name")).Where(n => n.Length > 0 && !used.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();
        return (servers.OrderBy(s => s.Name, StringComparer.Ordinal).ToList(), orphans);
    }

    /// <summary>Runs one server command through RCON (Java) or send-command (Bedrock) as a background read.</summary>
    public static async Task<CommandResult> CommandAsync(MinecraftServer server, string command, string? context)
    {
        if (server.IsJava && !server.Spec.RconEnabled) return new CommandResult(-1, "", "RCON is turned off for this server, so commands cannot be sent. Turn it on in Properties and recreate the container.");
        if (!server.IsRunning) return new CommandResult(-1, "", "The server is not running. Start it first.");
        return server.IsJava
            ? await Host.Read(["exec", server.Id, "rcon-cli", command], context)
            : await Host.Read(["exec", server.Id, "send-command", command], context);
    }

    /// <summary>Fills Players for running Java servers with RCON. Others keep an honest reason.</summary>
    public static async Task FillPlayersAsync(IEnumerable<MinecraftServer> servers, string? context)
    {
        var tasks = new List<Task>();
        foreach (var s in servers)
        {
            if (!s.IsRunning) { s.Players = "—"; continue; }
            if (!s.IsJava) { s.Players = "unknown (Bedrock has no RCON)"; continue; }
            if (!s.Spec.RconEnabled) { s.Players = "unknown (RCON off)"; continue; }
            tasks.Add(Fill(s));
        }
        await Task.WhenAll(tasks);
        async Task Fill(MinecraftServer s)
        {
            var r = await CommandAsync(s, "list", context);
            var parsed = r.Success ? MinecraftOutput.ParseList(r.StandardOutput) : null;
            if (parsed is { } p) { s.Players = $"{p.Online} / {p.Max}"; s.OnlineNames = p.Names; }
            else s.Players = "unknown (server still starting?)";
        }
    }

    /// <summary>Writes a temporary env file holding only RCON_PASSWORD; the caller deletes it.</summary>
    private static string WriteEnvFile(string password)
    {
        var dir = Path.Combine(Path.GetTempPath(), "MaterialDockerImage");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "mc-" + Guid.NewGuid().ToString("N") + ".env");
        File.WriteAllText(path, "RCON_PASSWORD=" + password + "\n", new UTF8Encoding(false));
        return path;
    }

    private static void DeleteQuietly(string? path)
    {
        if (path == null) return;
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Host.Notify.Warning("The temporary env file could not be deleted: " + path + ". Delete it manually; it contains the RCON password."); }
    }

    /// <summary>Runs docker run for the spec with the password (if any) supplied through a temporary env file.</summary>
    private static async Task<CommandResult?> RunAsync(MinecraftServerSpec spec, string? password, string? context, string label, string? containerName = null)
    {
        string? envFile = null;
        try
        {
            if (password != null) envFile = WriteEnvFile(password);
            return await Host.Execute(label, MinecraftSpecRules.RunArguments(spec, envFile, containerName), true, context);
        }
        finally { DeleteQuietly(envFile); }
    }

    /// <summary>Creates the labelled volume and the server container. Returns true when docker run reported success.</summary>
    public static async Task<bool> CreateAsync(MinecraftServerSpec spec, string? context)
    {
        var existing = await Host.Read(["container", "inspect", "--format", "{{.Id}}", spec.ContainerName], context);
        if (existing.Success) { Host.Notify.Error($"A container named {spec.ContainerName} already exists on this engine. Choose another server name."); return false; }
        var volume = await Host.Read(["volume", "inspect", "--format", "{{json .Labels}}", spec.VolumeName], context);
        if (volume.Success)
        {
            var reuse = await Host.Overlay.ConfirmAsync("Reuse the existing world volume?", $"The volume {spec.VolumeName} already exists, probably from a server that was removed while keeping its world. The new server will use the data in it (world, settings files and plugins).", "Reuse volume");
            if (!reuse) return false;
        }
        else
        {
            var created = await Host.Execute($"Create volume {spec.VolumeName}", MinecraftSpecRules.VolumeCreateArguments(spec), true, context);
            if (created is not { Success: true }) return false;
        }
        var password = spec.Edition == MinecraftEdition.Java && spec.RconEnabled ? MinecraftSpecRules.NewPassword() : null;
        var run = await RunAsync(spec, password, context, $"Create Minecraft server {spec.Name}");
        if (run is { Success: true })
        {
            Host.History.Record("minecraft", $"Created Minecraft server {spec.Name} ({MinecraftSpecRules.EditionValue(spec.Edition)} {spec.Version}, {spec.Image})");
            return true;
        }
        return false;
    }

    /// <summary>Saves the world through RCON when possible, then stops the container with a 60 second grace period.</summary>
    public static async Task<bool> StopAsync(MinecraftServer server, string? context)
    {
        // rcon-cli stop would end the server process, but the unless-stopped restart policy would then start it again,
        // so the world is flushed through RCON and the container is stopped by the engine (the image forwards the
        // signal to the server, which saves and shuts down cleanly).
        if (server.CanRcon)
        {
            var flush = await CommandAsync(server, "save-all flush", context);
            if (!flush.Success) Host.Notify.Warning("save-all flush did not succeed (" + Trim(flush) + "). Stopping anyway; the server saves on a clean shutdown.");
        }
        var r = await Host.Execute($"Stop Minecraft server {server.Name}", ["container", "stop", "--time", "60", server.Id], true, context);
        return r is { Success: true };
    }

    public static async Task<bool> StartAsync(MinecraftServer server, string? context) =>
        await Host.Execute($"Start Minecraft server {server.Name}", ["container", "start", server.Id], true, context) is { Success: true };

    public static async Task<bool> RestartAsync(MinecraftServer server, string? context)
    {
        if (server.CanRcon) await CommandAsync(server, "save-all flush", context);
        return await Host.Execute($"Restart Minecraft server {server.Name}", ["container", "restart", "--time", "60", server.Id], true, context) is { Success: true };
    }

    /// <summary>
    /// Replaces the container with one built from <paramref name="spec"/>, keeping the volume and the RCON password. The old
    /// container is renamed first and restored if the new one cannot be created.
    /// </summary>
    public static async Task<bool> RecreateAsync(MinecraftServer server, MinecraftServerSpec spec, string? context)
    {
        var password = await ReadPasswordAsync(server, context);
        if (spec.Edition == MinecraftEdition.Java && spec.RconEnabled && password == null) password = MinecraftSpecRules.NewPassword();
        var wasRunning = server.IsRunning;
        if (wasRunning && !await StopAsync(server, context)) return false;
        var parked = server.ContainerName + "-replaced-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        if (await Host.Execute($"Set aside the current container of {server.Name}", ["container", "rename", server.Id, parked], true, context) is not { Success: true }) return false;
        // The server keeps its original container name and volume.
        var mountSpec = spec.Clone();
        var run = await RunAsync(mountSpec, password, context, $"Recreate Minecraft server {server.Name}", server.ContainerName);
        if (run is { Success: true })
        {
            await Host.Execute($"Remove the previous container of {server.Name}", ["container", "rm", parked], true, context);
            Host.History.Record("minecraft", $"Recreated Minecraft server {server.Name} with new settings: " + string.Join("; ", MinecraftSpecRules.Diff(server.Spec, spec).Select(MaskDiff)));
            return true;
        }
        Host.Notify.Error("The new container could not be created. Restoring the previous container.");
        await Host.Execute($"Remove the failed container of {server.Name}", ["container", "rm", "--force", server.ContainerName], true, context);
        await Host.Execute($"Restore the previous container of {server.Name}", ["container", "rename", parked, server.ContainerName], true, context);
        if (wasRunning) await Host.Execute($"Start Minecraft server {server.Name}", ["container", "start", server.ContainerName], true, context);
        return false;
    }

    private static string MaskDiff(string line) => line.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase) ? line.Split(':', '=')[0] + " (secret changed)" : line;

    /// <summary>Reads RCON_PASSWORD from the container configuration so a recreate keeps it. The value never leaves this method's caller chain.</summary>
    private static async Task<string?> ReadPasswordAsync(MinecraftServer server, string? context)
    {
        var r = await Host.Read(["container", "inspect", "--format", "{{json .Config.Env}}", server.Id], context);
        if (!r.Success) return null;
        try
        {
            var env = JsonSerializer.Deserialize<List<string>>(r.StandardOutput) ?? [];
            return env.FirstOrDefault(e => e.StartsWith("RCON_PASSWORD=", StringComparison.Ordinal))?["RCON_PASSWORD=".Length..];
        }
        catch (JsonException) { return null; }
    }

    public static async Task<bool> RemoveAsync(MinecraftServer server, bool removeVolume, string? context)
    {
        if (await Host.Execute($"Remove Minecraft server {server.Name}", ["container", "rm", "--force", "--volumes=false", server.Id], true, context) is not { Success: true }) return false;
        if (removeVolume && await Host.Execute($"Remove world volume {server.VolumeName}", ["volume", "rm", server.VolumeName], true, context) is not { Success: true }) return false;
        Host.History.Record("minecraft", $"Removed Minecraft server {server.Name}" + (removeVolume ? " and its world volume " + server.VolumeName : "; kept world volume " + server.VolumeName));
        return true;
    }

    public static string BackupPrefix(MinecraftServer server) => "mc-" + server.Name + "-";

    /// <summary>
    /// Archives the /data volume into a .tar.gz in <paramref name="folder"/>: RCON save-off and save-all flush, a short-lived
    /// busybox container tars the volume read-only, docker cp copies the archive here, then save-on. Returns the file path.
    /// </summary>
    public static async Task<string?> BackupAsync(MinecraftServer server, string folder, string? context)
    {
        Directory.CreateDirectory(folder);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var target = Path.Combine(folder, BackupPrefix(server) + stamp + ".tar.gz");
        var partial = target + ".partial";
        var helper = server.ContainerName + "-backup-" + stamp;
        var savedOff = false;
        try
        {
            if (server.CanRcon)
            {
                var off = await CommandAsync(server, "save-off", context);
                savedOff = off.Success;
                var flush = await CommandAsync(server, "save-all flush", context);
                if (!flush.Success) Host.Notify.Warning("save-all flush did not succeed; the archive may miss the last few seconds of changes.");
            }
            else if (server.IsRunning && server.IsJava) Host.Notify.Warning("RCON is off, so saving could not be paused. The archive is taken from the live volume.");
            else if (server.IsRunning) Host.Notify.Warning("Bedrock servers cannot pause saving through this page. The archive is taken from the live volume.");
            var create = await Host.Execute($"Prepare backup of {server.Name}", ["container", "create", "--name", helper, "--label", MinecraftSpecRules.Label + "=" + server.Name,
                "--mount", $"type=volume,source={server.VolumeName},target=/data,readonly", HelperImage, "tar", "czf", "/backup.tar.gz", "-C", "/data", "."], true, context);
            if (create is not { Success: true }) return null;
            if (await Host.Execute($"Archive world of {server.Name}", ["container", "start", "--attach", helper], true, context) is not { Success: true }) return null;
            if (await Host.Execute($"Copy backup of {server.Name} to this computer", ["container", "cp", helper + ":/backup.tar.gz", partial], true, context) is not { Success: true }) return null;
            var info = new FileInfo(partial);
            if (!info.Exists || info.Length == 0) { Host.Notify.Error("The backup archive was not written or is empty."); return null; }
            File.Move(partial, target);
            Host.History.Record("minecraft", $"Backed up Minecraft server {server.Name} to {target} ({Ui.FormatBytes(info.Length)})");
            return target;
        }
        finally
        {
            if (savedOff) { var on = await CommandAsync(server, "save-on", context); if (!on.Success) Host.Notify.Error("save-on failed. Run save-on in the console so the server keeps saving."); }
            await Host.Read(["container", "rm", "--force", helper], context);
            try { if (File.Exists(partial)) File.Delete(partial); } catch (IOException) { }
        }
    }

    /// <summary>Replaces the contents of the server volume with a .tar.gz archive. The caller confirms and stops the server first.</summary>
    public static async Task<bool> RestoreAsync(MinecraftServer server, string archive, string? context)
    {
        var helper = server.ContainerName + "-restore-" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        try
        {
            var create = await Host.Execute($"Prepare restore of {server.Name}", ["container", "create", "--name", helper, "--label", MinecraftSpecRules.Label + "=" + server.Name,
                "--mount", $"type=volume,source={server.VolumeName},target=/data", HelperImage, "sh", "-c", "tar tzf /restore.tar.gz > /dev/null && find /data -mindepth 1 -delete && tar xzf /restore.tar.gz -C /data"], true, context);
            if (create is not { Success: true }) return false;
            if (await Host.Execute($"Upload backup for {server.Name}", ["container", "cp", archive, helper + ":/restore.tar.gz"], true, context) is not { Success: true }) return false;
            if (await Host.Execute($"Restore world of {server.Name}", ["container", "start", "--attach", helper], true, context) is not { Success: true }) return false;
            Host.History.Record("minecraft", $"Restored Minecraft server {server.Name} from {archive}");
            return true;
        }
        finally { await Host.Read(["container", "rm", "--force", helper], context); }
    }

    public static string Trim(CommandResult r)
    {
        var text = (r.StandardError.Trim().Length > 0 ? r.StandardError : r.StandardOutput).Trim();
        return text.Length == 0 ? "exit code " + r.ExitCode : text.Length > 300 ? text[..300] + "…" : text;
    }
}
