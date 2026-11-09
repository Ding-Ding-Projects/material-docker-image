using System.Globalization;
using System.Net.Http;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>One deployed self-hosted project: containers carrying the self-hosted label, grouped by Compose project.</summary>
internal sealed class SelfHostedProject
{
    public required string Name { get; init; }
    public required string AppId { get; init; }
    public SelfHostedApp? App => SelfHostedCatalogue.Find(AppId);
    public string AppName => App?.Name ?? AppId;
    public List<(string Id, string Service, string State, string Ports)> Containers { get; init; } = [];
    public string ConfigFile { get; init; } = "";
    public string Folder => ConfigFile.Length > 0 ? Path.GetDirectoryName(ConfigFile) ?? "" : "";
    public int? WebPort { get; init; }
    public string UrlHost { get; set; } = "localhost";
    public string Url => WebPort is { } p ? $"http://{UrlHost}:{p.ToString(CultureInfo.InvariantCulture)}" : "";
    public int Running => Containers.Count(c => c.State == "running");
    public bool IsRunning => Running > 0;
    public string State => Running == Containers.Count && Running > 0 ? "Running" : Running > 0 ? "Partly running" : "Stopped";
    public Tone StateTone => Running == Containers.Count && Running > 0 ? Tone.Success : Running > 0 ? Tone.Warning : Tone.Neutral;
    public string ContainersText => $"{Running} / {Containers.Count}";
    public double? CpuPercent { get; set; }
    public double? MemoryBytes { get; set; }
    public string CpuText => CpuPercent is { } c ? c.ToString("0.0", CultureInfo.InvariantCulture) + " %" : "";
    public string MemoryText => MemoryBytes is { } m ? Ui.FormatBytes(m) : "";
}

/// <summary>Engine and file operations for the Self-hosted destination. Only projects whose containers carry
/// <see cref="SelfHostedRules.Label"/> are listed or acted on.</summary>
internal static class SelfHostedOps
{
    private static AppHost Host => AppHost.Current;

    /// <summary>Folder that holds compose.yaml, .env and selfhosted.json for each project.</summary>
    public static string Root => Path.Combine(UserSettings.DataDirectory, "selfhosted");
    public static string ProjectFolder(string project) => Path.Combine(Root, project);

    /// <summary>The engine host for URLs: this computer for local contexts, otherwise the host name of the context endpoint.</summary>
    public static async Task<(bool Local, string Host)> EngineHostAsync(string? context)
    {
        var (local, endpoint) = await VolumeTransferChecks.EngineLocationAsync(context);
        if (local) return (true, "localhost");
        return (false, Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Host.Length > 0 ? uri.Host : endpoint);
    }

    public static async Task<(List<SelfHostedProject> Projects, string? Error)> LoadAsync(string? context)
    {
        IReadOnlyList<JsonElement> rows;
        try { rows = await Host.ReadRows(["container", "ls", "--all", "--filter", "label=" + SelfHostedRules.Label, "--format", "{{json .}}"], context); }
        catch (Exception e) when (e is InvalidOperationException or JsonException) { return ([], e.Message); }
        var (local, host) = await EngineHostAsync(context);
        var groups = new Dictionary<string, (string App, string Config, List<(string, string, string, string)> Containers)>(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            var labels = SelfHostedRules.ParseLabels(CliValues.Str(r, "Labels"));
            if (!labels.TryGetValue(SelfHostedRules.Label, out var app) || !labels.TryGetValue("com.docker.compose.project", out var project)) continue;
            if (!groups.TryGetValue(project, out var g)) groups[project] = g = (app, labels.GetValueOrDefault("com.docker.compose.project.config_files", ""), []);
            g.Containers.Add((CliValues.Str(r, "ID"), labels.GetValueOrDefault("com.docker.compose.service", ""), CliValues.Str(r, "State"), CliValues.Str(r, "Ports")));
        }
        var projects = new List<SelfHostedProject>();
        foreach (var (name, g) in groups.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            var app = SelfHostedCatalogue.Find(g.App);
            int? web = null;
            if (app != null)
                foreach (var c in g.Containers.Where(c => c.Item2 == app.WebPort.Service))
                    web ??= SelfHostedRules.PublishedPort(c.Item4, app.WebPort.ContainerPort);
            projects.Add(new SelfHostedProject { Name = name, AppId = g.App, Containers = g.Containers, ConfigFile = g.Config.Split(',')[0], WebPort = web, UrlHost = local ? "localhost" : host });
        }
        return (projects, null);
    }

    /// <summary>Adds CPU and memory from one docker stats sample; leaves the values empty when stats are unavailable.</summary>
    public static async Task FillStatsAsync(IReadOnlyList<SelfHostedProject> projects, string? context)
    {
        var running = projects.SelectMany(p => p.Containers.Where(c => c.State == "running").Select(c => (p, c.Id))).ToList();
        if (running.Count == 0) return;
        IReadOnlyList<JsonElement> rows;
        try { rows = await Host.ReadRows(["stats", "--no-stream", "--format", "{{json .}}", .. running.Select(r => r.Id)], context); }
        catch (Exception e) when (e is InvalidOperationException or JsonException) { return; }
        foreach (var row in rows)
        {
            var id = CliValues.Str(row, "ID");
            var owner = running.FirstOrDefault(r => r.Id.StartsWith(id, StringComparison.Ordinal) || id.StartsWith(r.Id, StringComparison.Ordinal)).p;
            if (owner == null) continue;
            if (double.TryParse(CliValues.Str(row, "CPUPerc").TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var cpu)) owner.CpuPercent = (owner.CpuPercent ?? 0) + cpu;
            if (SelfHostedRules.ParseSize(CliValues.Str(row, "MemUsage").Split('/')[0]) is { } mem) owner.MemoryBytes = (owner.MemoryBytes ?? 0) + mem;
        }
    }

    /// <summary>Host ports already taken: ports published by containers on the engine, plus listeners on this computer for a local engine.</summary>
    public static async Task<HashSet<int>> UsedPortsAsync(string? context, bool local)
    {
        var used = local ? SelfHostedRules.LocalListeners() : [];
        try
        {
            var rows = await Host.ReadRows(["container", "ls", "--format", "{{json .}}"], context);
            foreach (var r in rows) foreach (var (port, _) in EditorValueFormats.PublishedHostPorts(CliValues.Str(r, "Ports"))) used.Add(port);
        }
        catch (Exception e) when (e is InvalidOperationException or JsonException) { }
        return used;
    }

    /// <summary>Compose project names the engine already knows, including stopped ones.</summary>
    public static async Task<List<string>> ExistingProjectsAsync(string? context)
    {
        try { return (await Host.ReadRows(["compose", "ls", "--all", "--format", "json"], context)).Select(r => CliValues.Str(r, "Name")).Where(n => n.Length > 0).ToList(); }
        catch (Exception e) when (e is InvalidOperationException or JsonException) { return []; }
    }

    /// <summary>Writes compose.yaml, selfhosted.json and .env. The .env file is created with an access list that only
    /// lets the current Windows account read it, before any secret is written.</summary>
    public static string WriteFiles(SelfHostedFiles files, string project, bool replace = false)
    {
        var folder = ProjectFolder(project);
        if (!replace && File.Exists(Path.Combine(folder, "compose.yaml"))) throw new IOException($"Files for a project named {project} already exist in {folder}. Remove that project or choose another name.");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "compose.yaml"), files.Compose);
        File.WriteAllText(Path.Combine(folder, "selfhosted.json"), files.Settings);
        WritePrivate(Path.Combine(folder, ".env"), files.Env);
        return folder;
    }

    /// <summary>Creates or replaces a file readable and writable by the current user only (inheritance removed).</summary>
    public static void WritePrivate(string path, string text)
    {
        var user = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("The current Windows account could not be identified.");
        var security = new FileSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
        if (File.Exists(path)) File.Delete(path);
        using (var stream = new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.None, security))
        using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
            writer.Write(text);
    }

    /// <summary>True when only the current user has access to the file (checked after writing the .env file).</summary>
    public static bool IsPrivate(string path)
    {
        var user = WindowsIdentity.GetCurrent().User;
        var rules = new FileInfo(path).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
        return rules.Count > 0 && rules.All(r => r.IdentityReference.Equals(user) && r.AccessControlType == AccessControlType.Allow);
    }

    public enum HealthResult { Healthy, Failed, TimedOut, Canceled }

    /// <summary>Waits until the app answers on its health path with a status below 500, a container reports unhealthy
    /// or exits, the timeout passes or the user cancels. Reports progress as text.</summary>
    public static async Task<(HealthResult Result, string Detail)> WaitHealthyAsync(SelfHostedApp app, string project, int port, string host, string? context, TimeSpan timeout, IProgress<string> progress, CancellationToken token)
    {
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(5) };
        var url = $"http://{(host == "localhost" ? "127.0.0.1" : host)}:{port.ToString(CultureInfo.InvariantCulture)}{app.HealthPath}";
        var started = DateTime.UtcNow;
        var last = "";
        while (DateTime.UtcNow - started < timeout)
        {
            if (token.IsCancellationRequested) return (HealthResult.Canceled, last);
            try
            {
                var rows = await Host.ReadRows(["compose", "-p", project, "ps", "--all", "--format", "json"], context, token);
                foreach (var r in rows)
                {
                    var state = CliValues.Str(r, "State"); var health = CliValues.Str(r, "Health"); var service = CliValues.Str(r, "Service");
                    if (health == "unhealthy") return (HealthResult.Failed, Loc.T($"The {service} container reports unhealthy. Open its logs for the reason."));
                    if (state is "exited" or "dead" && CliValues.Str(r, "ExitCode") != "0") return (HealthResult.Failed, Loc.T($"The {service} container stopped ({CliValues.Str(r, "Status")}). Open its logs for the reason."));
                }
            }
            catch (Exception e) when (e is InvalidOperationException or JsonException) { }
            catch (OperationCanceledException) { return (HealthResult.Canceled, last); }
            try
            {
                using var response = await http.GetAsync(url, token);
                var code = (int)response.StatusCode;
                if (code < 500) return (HealthResult.Healthy, Loc.T($"{url} answered HTTP {code.ToString(CultureInfo.InvariantCulture)}."));
                last = Loc.T($"{url} answered HTTP {code.ToString(CultureInfo.InvariantCulture)}; waiting for the app to finish starting.");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return (HealthResult.Canceled, last); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { last = Loc.T($"No answer from {url} yet; waiting for the app to start."); }
            progress.Report(last + " · " + Loc.T("Elapsed") + " " + ((int)(DateTime.UtcNow - started).TotalSeconds).ToString(CultureInfo.InvariantCulture) + " s");
            try { await Task.Delay(3000, token); } catch (OperationCanceledException) { return (HealthResult.Canceled, last); }
        }
        return (HealthResult.TimedOut, last);
    }

    /// <summary>Named volumes that belong to the project (by the Compose project label).</summary>
    public static async Task<List<string>> VolumesAsync(string project, string? context)
    {
        try { return (await Host.ReadRows(["volume", "ls", "--filter", "label=com.docker.compose.project=" + project, "--format", "{{json .}}"], context)).Select(r => CliValues.Str(r, "Name")).Where(n => n.Length > 0).ToList(); }
        catch (Exception e) when (e is InvalidOperationException or JsonException) { return []; }
    }

    /// <summary>Compose arguments for an existing project: with its files when they are on this computer, otherwise by project name.</summary>
    public static List<string> ProjectArgs(SelfHostedProject p, params string[] command)
    {
        var folder = p.Folder;
        if (folder.Length > 0 && File.Exists(Path.Combine(folder, "compose.yaml")) && File.Exists(Path.Combine(folder, ".env"))) return SelfHostedCompose.ComposeArgs(p.Name, folder, command);
        return ["compose", "-p", p.Name, .. command];
    }

    public static SelfHostedSaved? ReadSaved(SelfHostedProject p)
    {
        var path = Path.Combine(p.Folder, "selfhosted.json");
        return p.Folder.Length > 0 && File.Exists(path) ? SelfHostedCompose.ReadSaved(File.ReadAllText(path)) : null;
    }
}
