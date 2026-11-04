using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

public sealed record SetupContext(string Name, string Description, string Endpoint, bool CliDefault);

/// <summary>Content of the four setup steps. Each step reads real data only and never changes global Docker state.</summary>
internal sealed class SetupSteps
{
    private static AppHost Host => AppHost.Current;
    private readonly Action changed;
    public bool Detected { get; private set; }
    public string? DetectSummary { get; private set; }

    // Step 1
    private readonly StackPanel detectBody = new();
    // Step 2
    private readonly StackPanel contextList = new();
    private readonly SearchFilter contextSearch = new("Search contexts", 260);
    private readonly TextBlock contextState = Ui.Caption("");
    private List<SetupContext> contexts = new();
    private readonly Dictionary<string, (CheckStatus Status, string Text)> reach = new();
    // Step 3
    private readonly TextBox folder = Ui.Input("", "Project folder", true);
    private readonly TextBlock folderState = Ui.Caption("");

    public SetupSteps(Action changed)
    {
        this.changed = changed;
        contextSearch.Changed += RenderContexts;
        folder.Text = Host.Settings.BuildFolder;
        folder.LostFocus += (_, _) => SetFolder(folder.Text);
        folder.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) SetFolder(folder.Text); };
        DescribeFolder();
    }

    public FrameworkElement Detect() => Ui.VStack(12,
        Ui.Caption("Runs docker version --format \"{{json .}}\" on the selected context. It only reads; nothing is installed or changed."),
        Ui.AsyncButton("Run detection", Icons.PlayArrow, ButtonKind.Tonal, DetectAsync),
        detectBody);

    public async Task DetectAsync()
    {
        detectBody.Children.Clear();
        detectBody.Children.Add(Ui.Caption("Detecting…"));
        var r = await Host.Read(["version", "--format", "{{json .}}"]);
        detectBody.Children.Clear();
        JsonElement? json = null;
        try { var start = r.StandardOutput.IndexOf('{'); if (start >= 0) { using var doc = JsonDocument.Parse(r.StandardOutput[start..]); json = doc.RootElement.Clone(); } }
        catch (JsonException) { }
        string Part(string section, string name) => json is { } j && j.TryGetProperty(section, out var s) && s.ValueKind == JsonValueKind.Object ? DockerJson.Text(s, name) : "";
        var client = Part("Client", "Version");
        var server = Part("Server", "Version");
        var api = Part("Server", "ApiVersion");
        if (client.Length == 0)
        {
            Detected = false;
            DetectSummary = null;
            detectBody.Children.Add(Status(Icons.Error, "Error", "Docker CLI not found",
                r.ExitCode == -1 ? "The docker command could not be started. Install Docker Desktop (or another Docker-compatible engine with its CLI), make sure docker.exe is on PATH, then run detection again." : $"docker exited with code {r.ExitCode}.",
                r.StandardError));
        }
        else if (server.Length == 0)
        {
            Detected = false;
            DetectSummary = null;
            detectBody.Children.Add(Status(Icons.Warning, "Warning", $"Docker CLI {client} found, engine not reachable",
                "The CLI works, but the engine did not answer. Start Docker Desktop (or your engine service) and wait until it reports that it is running, or pick a different context in step 2. Then run detection again.",
                r.StandardError));
        }
        else
        {
            Detected = true;
            DetectSummary = $"Docker CLI {client} · Engine {server} · API {api}";
            var grid = new WrapPanel();
            foreach (var (label, value) in new[] { ("Docker CLI", client), ("Engine", server), ("Engine API", api), ("Engine OS", Part("Server", "Os") + "/" + Part("Server", "Arch")) })
                grid.Children.Add(Ui.VStack(2, Ui.Overline(label), Ui.Mono(value, 14)).Margin(0, 0, 28, 8));
            detectBody.Children.Add(Status(Icons.CheckCircle, "Success", "Docker detected", $"Context: {Host.Context ?? "CLI default"}", null));
            detectBody.Children.Add(grid);
            if (Host.Settings.SetupStep < 1) { Host.Settings.SetupStep = 1; Host.SaveSettings(null); }
        }
        changed();
    }

    private static FrameworkElement Status(string icon, string brush, string title, string message, string? output)
    {
        var stack = Ui.VStack(8, Ui.HStack(10, Ui.Icon(icon, 22, true, brush), Ui.Text(title, 15, FontWeights.SemiBold)), Ui.Text(message, 14, null, "OnSurfaceVariant"));
        if (!string.IsNullOrWhiteSpace(output)) stack.Children.Add(PageParts.OutputBox(PageParts.Sanitize(output.Trim()), 160));
        return Ui.Card(stack, 14, "SurfaceContainer", 12);
    }

    public FrameworkElement Context()
    {
        var refresh = Ui.AsyncButton("Reload contexts", Icons.Refresh, ButtonKind.Text, LoadContextsAsync);
        return Ui.VStack(12,
            Ui.Caption("The app talks to Docker through a context. Choosing one here applies to this app only; the global Docker context is never changed (no docker context use)."),
            Ui.Columns(12, [0, 1, 0], contextSearch, contextState, refresh),
            contextList);
    }

    public async Task LoadContextsAsync()
    {
        contextState.Text = Loc.T("Loading contexts…");
        try
        {
            var rows = await Host.ReadRows(["context", "ls", "--format", "{{json .}}"], "");
            contexts = rows.Select(x => new SetupContext(DockerJson.Text(x, "Name"), DockerJson.Text(x, "Description"), DockerJson.Text(x, "DockerEndpoint"), DockerJson.Text(x, "Current").Equals("true", StringComparison.OrdinalIgnoreCase))).Where(c => c.Name.Length > 0).ToList();
            contextState.Text = $"{contexts.Count} context(s)";
        }
        catch (Exception e) when (e is InvalidOperationException or JsonException)
        {
            contexts = new();
            contextState.Text = Loc.T("Contexts could not be listed: ") + e.Message;
        }
        RenderContexts();
        foreach (var c in contexts.ToList()) _ = ProbeAsync(c.Name);
    }

    private async Task ProbeAsync(string name)
    {
        reach[name] = (CheckStatus.Running, "Checking…");
        RenderContexts();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        CommandResult r;
        try { r = await Host.Read(["version", "--format", "{{.Server.Version}}"], name, timeout.Token); }
        catch (OperationCanceledException) { r = new CommandResult(-1, "", "No answer within 8 seconds.", true); }
        reach[name] = r.Success && r.StandardOutput.Trim().Length > 0 ? (CheckStatus.Passed, Loc.T("Reachable · engine") + " " + r.StandardOutput.Trim()) : (CheckStatus.Failed, r.Cancelled ? "No answer within 8 seconds" : "Not responding");
        RenderContexts();
    }

    private void RenderContexts()
    {
        contextList.Children.Clear();
        var selected = Host.Context;
        var shown = contexts.Where(c => contextSearch.Matches($"{c.Name} {c.Description} {c.Endpoint}")).ToList();
        if (contexts.Count == 0) { contextList.Children.Add(Ui.Caption("No contexts were listed. Run detection in step 1, or reload.")); return; }
        if (shown.Count == 0) { contextList.Children.Add(Ui.Caption(contextSearch.Status.Length > 0 ? contextSearch.Status : "No contexts match the search.")); return; }
        var group = "ctx" + GetHashCode();
        foreach (var c in shown)
        {
            var isSelected = selected == c.Name || (selected == null && c.CliDefault);
            var (status, text) = reach.TryGetValue(c.Name, out var v) ? v : (CheckStatus.NotRun, "Not checked");
            var chip = Ui.Badge(text, status switch { CheckStatus.Passed => Tone.Success, CheckStatus.Failed => Tone.Error, CheckStatus.Running => Tone.Primary, _ => Tone.Neutral });
            var head = new DockPanel();
            head.Children.Add(chip.Dock(Dock.Right));
            head.Children.Add(Ui.Text(c.Name + (c.CliDefault ? " · CLI default" : ""), 15, FontWeights.SemiBold, "OnSurface", false, false));
            var content = Ui.VStack(4, head, Ui.Mono(c.Endpoint, 12, "OnSurfaceVariant", true), c.Description.Length > 0 ? Ui.Caption(c.Description) : null);
            content.Margin = new Thickness(8, 0, 0, 0);
            var radio = new RadioButton { GroupName = group, IsChecked = isSelected, Content = content, Padding = new Thickness(12), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            radio.SetResourceReference(Control.ForegroundProperty, "OnSurface");
            System.Windows.Automation.AutomationProperties.SetName(radio, $"{c.Name}, {text}");
            var name = c.Name;
            radio.Checked += (_, _) =>
            {
                if (Host.Context == name) return;
                Host.SetContext(name);
                Host.History.Record("settings", "Engine context for the app set to " + name);
                Host.Notify.Info($"This app now uses context {name}. The global Docker context was not changed.");
                changed();
            };
            var card = new Border { Child = radio, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(isSelected ? 2 : 1), Margin = new Thickness(0, 0, 0, 8) };
            card.SetResourceReference(Border.BorderBrushProperty, isSelected ? "Primary" : "OutlineVariant");
            card.SetResourceReference(Border.BackgroundProperty, isSelected ? "SurfaceHigh" : "SurfaceContainer");
            contextList.Children.Add(card);
        }
    }

    public FrameworkElement Folder()
    {
        var browse = Ui.Button("Browse…", Icons.FolderOpen, ButtonKind.Tonal, () =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("Choose a project folder") };
            if (Directory.Exists(folder.Text)) dialog.InitialDirectory = folder.Text;
            if (dialog.ShowDialog() != true) return;
            folder.Text = dialog.FolderName;
            SetFolder(dialog.FolderName);
        });
        var row = new DockPanel();
        row.Children.Add(browse.Dock(Dock.Right).Margin(8, 0, 0, 0));
        row.Children.Add(folder);
        return Ui.VStack(12,
            Ui.Caption("Pick the folder that holds your Dockerfile or Compose file. Build uses it as the build context. This step is optional."),
            Ui.Field("Project folder", row, "The build context folder sent to the engine when you build. Choose the project root, not a parent folder with many unrelated files, because everything in it (minus .dockerignore entries) is sent.", @"C:\src\hello-api"),
            folderState);
    }

    private void SetFolder(string value)
    {
        value = value.Trim().Trim('"');
        if (value.Length > 0 && !Directory.Exists(value)) { folderState.Text = Loc.T("That folder does not exist."); return; }
        if (value != Host.Settings.BuildFolder)
        {
            Host.Settings.BuildFolder = value;
            Host.SaveSettings(value.Length == 0 ? "Project folder cleared" : "Project folder set to " + Path.GetFileName(Path.TrimEndingDirectorySeparator(value)));
        }
        DescribeFolder();
        changed();
    }

    private void DescribeFolder()
    {
        var path = Host.Settings.BuildFolder;
        if (path.Length == 0) { folderState.Text = Loc.T("No folder chosen. You can choose one later in Build."); return; }
        if (!Directory.Exists(path)) { folderState.Text = Loc.T("The saved folder no longer exists. Choose another."); return; }
        try
        {
            var files = Directory.EnumerateFiles(path).Select(Path.GetFileName).OfType<string>().ToList();
            var dockerfiles = files.Where(f => f.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".Dockerfile", StringComparison.OrdinalIgnoreCase) || f.StartsWith("Dockerfile.", StringComparison.OrdinalIgnoreCase)).ToList();
            var compose = files.Where(f => f is "compose.yaml" or "compose.yml" or "docker-compose.yml" or "docker-compose.yaml").ToList();
            var parts = new List<string>();
            if (dockerfiles.Count > 0) parts.Add(Loc.T("Dockerfile found:") + " " + string.Join(", ", dockerfiles));
            if (compose.Count > 0) parts.Add(Loc.T("Compose file found:") + " " + string.Join(", ", compose));
            if (files.Any(f => f.Equals(".dockerignore", StringComparison.OrdinalIgnoreCase))) parts.Add(".dockerignore present");
            folderState.Text = parts.Count > 0 ? string.Join(" · ", parts) : Loc.T("No Dockerfile or Compose file in this folder. You can create one with Creator.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { folderState.Text = Loc.T("The folder could not be read:") + " " + e.Message; }
    }

    public FrameworkElement Personalize()
    {
        var s = Host.Settings;
        var theme = Ui.Field("Theme", Ui.Segmented(["System", "Light", "Dark"], s.Theme, v => { s.Theme = v; Host.SaveSettings("Theme set to " + v); }), "System follows the Windows app theme.");
        // Under School mode the language choice is absent, not disabled.
        if (s.SchoolMode) return Ui.VStack(16, Ui.Caption("You can change these and many more later in Settings."), theme);
        return Ui.VStack(16,
            Ui.Caption("You can change these and many more later in Settings."),
            theme,
            Ui.Field("Language", Ui.Segmented(["English", "Cantonese", "Bilingual"], s.Language, v => { s.Language = v; Loc.Language = v; Host.SaveSettings("Language set to " + v); }), "Labels with a Cantonese translation change; other text stays in English. Pages built earlier refresh their labels when reopened."));
    }
}
