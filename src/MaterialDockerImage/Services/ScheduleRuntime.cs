using System.IO;
using System.Windows.Threading;
using MaterialDockerImage.Pages;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Services;

/// <summary>
/// Applies scheduled language and theme rules. It evaluates on a timer, when the rules change and when the optional local source file
/// changes. The scheduled value is only lent to the live settings; the saved settings keep the person's own choice (see UserSettings.ApplyScheduled).
/// The source file is read from disk only: no network request is ever made.
/// </summary>
public sealed class ScheduleRuntime
{
    private readonly AppHost host;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(20) };
    private readonly DispatcherTimer fileDebounce = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private FileSystemWatcher? watcher;
    private string watchedPath = "";
    private string lastNotice = "";
    public ScheduleFileValues? File { get; private set; }
    /// <summary>Empty when the file is fine or no file is chosen, otherwise the reason it is not used.</summary>
    public string FileProblem { get; private set; } = "";
    public ScheduleOutcome Last { get; private set; } = new(null, null, Array.Empty<string>());
    public event Action? Evaluated;

    public ScheduleRuntime(AppHost host)
    {
        this.host = host;
        timer.Tick += (_, _) => Evaluate();
        fileDebounce.Tick += (_, _) => { fileDebounce.Stop(); ReloadFile(); };
    }

    public void Start()
    {
        if (host.IsCapture) return;
        ReloadFile();
        timer.Start();
    }

    /// <summary>Re-reads the source file (if one is chosen), re-points the watcher and evaluates the rules.</summary>
    public void ReloadFile()
    {
        var path = host.Settings.ScheduleSourceFile;
        File = null; FileProblem = "";
        if (path.Length > 0)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) FileProblem = "The schedule source file does not exist.";
                else
                {
                    var parsed = ScheduleEvaluator.ParseFile(info.Length > ScheduleEvaluator.MaxFileBytes ? "" : System.IO.File.ReadAllText(path), info.Length);
                    if (parsed.Error.Length > 0) FileProblem = parsed.Error; else File = parsed;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { FileProblem = "The schedule source file could not be read: " + e.Message; }
        }
        Watch(path);
        if (FileProblem.Length > 0 && FileProblem != lastNotice)
        {
            lastNotice = FileProblem;
            host.Notify.Show(NoticeKind.Warning, FileProblem + " Rules that use the file are paused and your own settings stay in effect.", "Open schedules", () => host.NavigateWith("Settings", "set.schedules"));
        }
        else if (FileProblem.Length == 0) lastNotice = "";
        Evaluate();
    }

    private void Watch(string path)
    {
        if (path == watchedPath) return;
        watchedPath = path;
        watcher?.Dispose(); watcher = null;
        if (path.Length == 0) return;
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (directory == null || !Directory.Exists(directory)) return;
            watcher = new FileSystemWatcher(directory, Path.GetFileName(path)) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size, EnableRaisingEvents = true };
            void Changed(object? s, EventArgs e) => timer.Dispatcher.BeginInvoke(() => { fileDebounce.Stop(); fileDebounce.Start(); });
            watcher.Changed += Changed; watcher.Created += Changed; watcher.Deleted += Changed; watcher.Renamed += Changed;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException) { watcher = null; }
    }

    public void Evaluate()
    {
        if (host.IsCapture) return;
        var outcome = ScheduleEvaluator.Evaluate(host.Settings.ScheduleRules, DateTime.Now, File);
        Last = outcome;
        if (host.Settings.ApplyScheduled(outcome.Language, outcome.Theme))
        {
            SettingRows.SyncRuntime();
            host.NotifySettingsChanged();
            var parts = new List<string>();
            if (outcome.Language != null) parts.Add("language " + outcome.Language);
            if (outcome.Theme != null) parts.Add("theme " + outcome.Theme);
            host.Notify.Info(parts.Count > 0 ? "A schedule is now applying " + string.Join(" and ", parts) + ". Your saved choices are unchanged." : "The schedule ended, so your own language and theme are back.");
        }
        Evaluated?.Invoke();
    }
}
