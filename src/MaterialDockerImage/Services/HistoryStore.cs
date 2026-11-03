using System.IO.Compression;
using System.Text;
using LibGit2Sharp;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Services;

public sealed record HistoryEntry(string Sha, DateTimeOffset When, string Action, string Summary, IReadOnlyList<string> Paths, IReadOnlyList<string> Labels)
{
    public string ShortSha => Sha[..Math.Min(10, Sha.Length)];
    public string WhenText => When.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string PathsText => string.Join(", ", Paths);
    public string LabelsText => string.Join(", ", Labels);
}

/// <summary>
/// Append-only local version history kept in its own Git repository beside the application's data directory.
/// It never creates a repository inside a user's project folder. A failure here never fails the user's operation.
/// </summary>
public sealed class HistoryStore
{
    private readonly object gate = new();
    public string Root { get; }
    public string? LastError { get; private set; }
    public event Action? Changed;

    public HistoryStore(string? root = null)
    {
        Root = root ?? Path.Combine(UserSettings.DataDirectory, "history");
    }

    private Repository Open()
    {
        Directory.CreateDirectory(Root);
        if (!Repository.IsValid(Root))
        {
            Repository.Init(Root);
            File.WriteAllText(Path.Combine(Root, "README.txt"), "Local version history for Material Docker Image. Every change is a new revision; restores are new revisions too.\n");
            using var fresh = new Repository(Root);
            Commands.Stage(fresh, "README.txt");
            var who = Who();
            fresh.Commit("init: history created\n\nAction: init", who, who);
        }
        return new Repository(Root);
    }

    private static Signature Who() => new("Material Docker Image", "history@material-docker-image.invalid", DateTimeOffset.Now);

    public static string SafeName(string text)
    {
        var builder = new StringBuilder();
        foreach (var c in text) builder.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
        var result = builder.ToString().Trim('.');
        return result.Length == 0 ? "item" : result.Length > 80 ? result[..80] : result;
    }

    /// <summary>Records one revision. Files are paths relative to the history root (forward slashes) and their full content.</summary>
    public bool Record(string action, string summary, IReadOnlyDictionary<string, string>? files = null)
    {
        try
        {
            lock (gate)
            {
                using var repo = Open();
                var written = new List<string>();
                foreach (var file in files ?? new Dictionary<string, string>())
                {
                    if (file.Key.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(file.Key)) throw new ArgumentException("History paths must be relative.");
                    var full = Path.Combine(Root, file.Key.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                    File.WriteAllText(full, file.Value);
                    written.Add(file.Key);
                }
                var log = Path.Combine(Root, "activity", "events.log");
                Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                File.AppendAllText(log, $"{DateTimeOffset.Now:o}\t{action}\t{summary.Replace('\n', ' ')}\n");
                written.Add("activity/events.log");
                foreach (var path in written) Commands.Stage(repo, path);
                var who = Who();
                repo.Commit($"{action}: {OneLine(summary)}\n\nAction: {action}", who, who, new CommitOptions { AllowEmptyCommit = true });
            }
            LastError = null;
            Changed?.Invoke();
            return true;
        }
        catch (Exception e) when (e is LibGit2SharpException or IOException or UnauthorizedAccessException or ArgumentException or DllNotFoundException or TypeInitializationException)
        {
            LastError = e.Message;
            return false;
        }
    }

    private static string OneLine(string text) { var line = text.Replace('\r', ' ').Replace('\n', ' ').Trim(); return line.Length > 120 ? line[..120] + "…" : line; }

    public IReadOnlyList<HistoryEntry> List(int limit = 2000)
    {
        try
        {
            lock (gate)
            {
                using var repo = Open();
                var labels = repo.Tags.Where(t => t.FriendlyName.StartsWith("label/", StringComparison.Ordinal))
                    .GroupBy(t => t.Target.Sha).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(t => t.FriendlyName[6..]).ToArray());
                var result = new List<HistoryEntry>();
                foreach (var commit in repo.Commits.QueryBy(new CommitFilter { SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time }).Take(limit))
                {
                    var action = commit.Message.Split('\n').FirstOrDefault(l => l.StartsWith("Action: ", StringComparison.Ordinal))?[8..].Trim() ?? "change";
                    var paths = new List<string>();
                    var parent = commit.Parents.FirstOrDefault();
                    if (parent != null) foreach (var change in repo.Diff.Compare<TreeChanges>(parent.Tree, commit.Tree)) if (change.Path != "activity/events.log") paths.Add(change.Path);
                    result.Add(new(commit.Sha, commit.Author.When, action, commit.MessageShort, paths, labels.TryGetValue(commit.Sha, out var l) ? l : Array.Empty<string>()));
                }
                LastError = null;
                return result;
            }
        }
        catch (Exception e) when (e is LibGit2SharpException or IOException or UnauthorizedAccessException or DllNotFoundException or TypeInitializationException)
        {
            LastError = e.Message;
            return Array.Empty<HistoryEntry>();
        }
    }

    public string Diff(string sha)
    {
        try
        {
            lock (gate)
            {
                using var repo = Open();
                var commit = repo.Lookup<Commit>(sha) ?? throw new ArgumentException("Unknown revision.");
                var parent = commit.Parents.FirstOrDefault();
                var patch = repo.Diff.Compare<Patch>(parent?.Tree, commit.Tree);
                return patch.Content.Length == 0 ? "No file content changed in this revision." : patch.Content;
            }
        }
        catch (Exception e) when (e is LibGit2SharpException or IOException or ArgumentException or DllNotFoundException) { return "Diff unavailable: " + e.Message; }
    }

    public string? FileAt(string sha, string path)
    {
        try
        {
            lock (gate)
            {
                using var repo = Open();
                var commit = repo.Lookup<Commit>(sha);
                return commit?[path]?.Target is Blob blob ? blob.GetContentText() : null;
            }
        }
        catch (Exception e) when (e is LibGit2SharpException or IOException or DllNotFoundException) { LastError = e.Message; return null; }
    }

    /// <summary>A restore is recorded as a new revision whose content equals the chosen revision's file.</summary>
    public string? Restore(string sha, string path)
    {
        var content = FileAt(sha, path);
        if (content == null) return null;
        Record("restore", $"Restored {path} from {sha[..Math.Min(10, sha.Length)]}", new Dictionary<string, string> { [path] = content });
        return content;
    }

    public bool Label(string sha, string name)
    {
        try
        {
            lock (gate)
            {
                using var repo = Open();
                repo.ApplyTag("label/" + SafeName(name), sha);
            }
            Changed?.Invoke();
            return true;
        }
        catch (Exception e) when (e is LibGit2SharpException or IOException or ArgumentException or DllNotFoundException) { LastError = e.Message; return false; }
    }

    /// <summary>Exports the listed revisions as a ZIP holding a JSON index and one patch per revision.</summary>
    public void Export(IEnumerable<HistoryEntry> entries, string zipPath)
    {
        var list = entries.ToList();
        using var stream = File.Create(zipPath);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        var index = zip.CreateEntry("index.json");
        using (var writer = new StreamWriter(index.Open(), new UTF8Encoding(false)))
            writer.Write(System.Text.Json.JsonSerializer.Serialize(new { schema = "material-docker-image/history-export", version = 1, encoding = "utf-8", lineEndings = "LF", revisions = list }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        foreach (var entry in list)
        {
            var file = zip.CreateEntry($"patches/{entry.When.ToUniversalTime():yyyyMMddTHHmmss}-{entry.ShortSha}.patch");
            using var writer = new StreamWriter(file.Open(), new UTF8Encoding(false));
            writer.Write(Diff(entry.Sha));
        }
    }
}
