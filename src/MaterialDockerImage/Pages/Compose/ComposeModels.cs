using System.Text;
using System.Text.Json;
using MaterialDockerImage.Core;
using MaterialDockerImage.UI;
using MaterialDockerImage.Services;

namespace MaterialDockerImage.Pages;

/// <summary>A project in the left list: either reported by <c>docker compose ls</c>, a recently opened file, or both.</summary>
public sealed record ComposeEntry(string Name, string Status, IReadOnlyList<string> Files, bool Listed, bool Recent)
{
    /// <summary>Config files that exist on this computer (remote engines can report paths that are not local).</summary>
    public IReadOnlyList<string> LocalFiles => Files.Where(File.Exists).ToList();
    public bool HasLocalFiles => Files.Count > 0 && Files.All(File.Exists);
    public string Key => Listed ? "ls:" + Name : "file:" + (Files.FirstOrDefault() ?? Name);
    public Tone StatusTone => Status.StartsWith("running", StringComparison.OrdinalIgnoreCase) ? Tone.Success
        : Status.StartsWith("exited", StringComparison.OrdinalIgnoreCase) || Status.StartsWith("dead", StringComparison.OrdinalIgnoreCase) ? Tone.Error
        : Status.StartsWith("paused", StringComparison.OrdinalIgnoreCase) || Status.StartsWith("restarting", StringComparison.OrdinalIgnoreCase) ? Tone.Warning
        : Tone.Neutral;
    public string SearchText => Name + " " + Status + " " + string.Join(" ", Files);
}

/// <summary>One service row: from <c>compose ps</c> when a container exists, otherwise from the resolved config.</summary>
public sealed record ComposeServiceRow(string Service, string Container, string Image, string State, string Status, string Ports, string Health, bool Created)
{
    public Tone StateTone => State switch
    {
        "running" => Health is "unhealthy" ? Tone.Error : Tone.Success,
        "exited" or "dead" => Tone.Error,
        "paused" or "restarting" or "created" => Tone.Warning,
        _ => Tone.Neutral
    };
}

/// <summary>How actions address a project: by its files (and name when known), or by name only when no files are available.</summary>
public sealed record ComposeTarget(string? ProjectName, IReadOnlyList<string> Files)
{
    public bool HasFiles => Files.Count > 0;
    public string? Directory => HasFiles ? Path.GetDirectoryName(Files[0]) : null;
    /// <summary>Explicit --env-file values. Empty means Compose reads the project directory's .env by default.</summary>
    public IReadOnlyList<string> EnvFiles { get; init; } = [];
    /// <summary>The project directory's .env file when it exists and no explicit env file replaces it.</summary>
    public string? DefaultEnvFile => HasFiles && EnvFiles.Count == 0 && Directory is { } d && File.Exists(Path.Combine(d, ".env")) ? Path.Combine(d, ".env") : null;

    /// <summary>Builds <c>compose [--project-directory D --file F…] [--project-name N] [--profile P…] tail…</c>.</summary>
    public List<string> Args(IEnumerable<string> profiles, params string[] tail)
    {
        var args = new List<string> { "compose" };
        if (HasFiles)
        {
            var dir = Directory!;
            if (!System.IO.Directory.Exists(dir)) throw new DirectoryNotFoundException(Loc.T("The project directory no longer exists:") + " " + dir);
            args.AddRange(["--project-directory", dir]);
            foreach (var f in Files)
            {
                if (!File.Exists(f)) throw new FileNotFoundException("A Compose file of this project no longer exists.", f);
                args.AddRange(["--file", Path.GetFullPath(f)]);
            }
        }
        foreach (var e in EnvFiles)
        {
            if (!File.Exists(e)) throw new FileNotFoundException("An env file chosen for this project no longer exists.", e);
            args.AddRange(["--env-file", Path.GetFullPath(e)]);
        }
        if (!string.IsNullOrWhiteSpace(ProjectName)) { DockerService.ValidateOperand(ProjectName, "project name"); args.AddRange(["--project-name", ProjectName]); }
        else if (!HasFiles) throw new InvalidOperationException("This project has neither files nor a name.");
        if (HasFiles) foreach (var p in profiles) { DockerService.ValidateOperand(p, "profile"); args.AddRange(["--profile", p]); }
        foreach (var t in tail) args.Add(t);
        return args;
    }

    public string Describe => HasFiles ? string.Join(", ", Files) : $"project {ProjectName} (no local files)";
}

/// <summary>Recently opened Compose files, stored in the application's own data directory (not in settings.json).</summary>
public static class ComposeRecentFiles
{
    private const int Limit = 15;
    public static string StorePath => Path.Combine(UserSettings.DataDirectory, "compose-recent.json");

    public static List<string> Load()
    {
        try
        {
            if (!File.Exists(StorePath) || new FileInfo(StorePath).Length > 65536) return new();
            var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(StorePath)) ?? new();
            return list.Where(p => !string.IsNullOrWhiteSpace(p) && Path.IsPathFullyQualified(p)).Distinct(StringComparer.OrdinalIgnoreCase).Take(Limit).ToList();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or NotSupportedException) { return new(); }
    }

    public static void Add(string path)
    {
        var list = Load();
        list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, Path.GetFullPath(path));
        Save(list.Take(Limit).ToList());
    }

    public static void Remove(string path)
    {
        var list = Load();
        list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        Save(list);
    }

    private static void Save(List<string> list)
    {
        if (Services.AppHost.Current?.IsCapture == true) return;
        try
        {
            Directory.CreateDirectory(UserSettings.DataDirectory);
            var temporary = StorePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(temporary, StorePath, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Services.AppHost.Current?.Notify.Warning(Loc.T("Recent Compose files could not be saved:") + " " + e.Message); }
    }
}
