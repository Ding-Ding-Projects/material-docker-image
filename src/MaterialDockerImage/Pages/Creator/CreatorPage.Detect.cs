using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage.Pages;

/// <summary>
/// "Detected project" card: reads manifest files in a chosen folder (like docker init), shows the findings with
/// their evidence files, and offers a prefilled draft plus optional .dockerignore and compose.yaml.
/// </summary>
public sealed partial class CreatorPage
{
    private readonly StackPanel detectHost = new();
    private ProjectDetection? detection;
    private bool detectIgnore = true, detectCompose = true;

    private FrameworkElement BuildDetectSection()
    {
        var again = Ui.AsyncIconButton(Icons.FolderOpen, "Choose project folder…", ChooseAndDetectAsync);
        again.Width = again.Height = again.MinHeight = 32; again.FontSize = 18;
        var title = Ui.Text("Detected project", 15, FontWeights.SemiBold, "OnSurface", false);
        var head = Ui.Columns(8, [0, 1, 0, 0], Ui.Icon(Icons.ManageSearch, 20, false, "Primary"), title,
            Ui.InfoButton("Detected project",
                "Reads package.json, lockfiles, pyproject.toml, requirements.txt, go.mod, *.csproj, Cargo.toml, pom.xml, build.gradle and index.html to suggest a Dockerfile with matching versions, port and entry point. Files are only read; nothing in the folder runs.",
                "package.json + package-lock.json + engines.node >=20 → node:20-alpine with npm ci and a cache mount"),
            again);
        ShowDetection();
        return Ui.Card(Ui.VStack(12, head, detectHost), 16, "SurfaceLow", 16);
    }

    private async Task ChooseAndDetectAsync()
    {
        var dialog = new OpenFolderDialog { Title = Loc.T("Choose the project folder to detect") };
        var current = detection?.Folder ?? IgnoreContextFolder();
        if (Directory.Exists(current)) dialog.InitialDirectory = current;
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var folder = dialog.FolderName;
        detection = await Task.Run(() => ProjectDetector.Detect(folder));
        detectIgnore = detection.ExistingDockerignore.Length == 0;
        detectCompose = detection.ExistingCompose.Length == 0 && detection.Recognised;
        ignoreContext = detection.Folder;
        UpdateIgnoreContextText();
        ShowDetection();
        if (!detection.Recognised) Host.Notify.Info(Loc.T("No supported project files were found in") + " " + folder + ".");
    }

    private void ShowDetection()
    {
        detectHost.Children.Clear();
        if (detection == null)
        {
            var choose = Ui.AsyncButton("Choose project folder…", Icons.ManageSearch, ButtonKind.Tonal, ChooseAndDetectAsync);
            choose.HorizontalAlignment = HorizontalAlignment.Stretch;
            detectHost.Children.Add(Ui.Caption("Choose a folder to detect its language, versions, port and entry point."));
            Ui.Add(detectHost, choose, 12);
            return;
        }
        var d = detection;
        var folder = Ui.Mono(d.Folder, 12, "OnSurfaceVariant", false);
        folder.ToolTip = d.Folder;
        Ui.Add(detectHost, folder, 0);
        foreach (var finding in d.Findings) Ui.Add(detectHost, FindingRow(finding), 8);
        if (!d.Recognised)
        {
            Ui.Add(detectHost, Ui.Caption("No supported project files were found. Start from a starter instead, or choose the folder that holds the project manifest."), 12);
            return;
        }
        var facts = new List<string>();
        if (d.Version.Length > 0) facts.Add(Loc.T("version") + " " + d.Version);
        if (d.Port.Length > 0) facts.Add(Loc.T("port") + " " + d.Port);
        if (d.Entry.Length > 0) facts.Add(Loc.T("entry") + " " + d.Entry);
        Ui.Add(detectHost, Ui.Text(Loc.T("Suggested:") + " " + d.Suggestion + (facts.Count > 0 ? " (" + string.Join(" · ", facts) + ")" : "") + ".", 13, null, "OnSurfaceVariant", true, false), 12);
        var ignoreSwitch = Ui.Switch("Also create .dockerignore", detectIgnore, v => detectIgnore = v, d.ExistingDockerignore.Length > 0 ? "The folder already has one; it is only replaced after you confirm." : "Shown in the .dockerignore tab.");
        var composeSwitch = Ui.Switch("Also create compose.yaml", detectCompose, v => { detectCompose = v; if (!v) ShowCompose(null); }, d.ExistingCompose.Length > 0 ? $"The folder already has {d.ExistingCompose}." : "Shown in the compose.yaml tab.");
        var apply = Ui.AsyncButton("Apply suggestion", Icons.DoneAll, ButtonKind.Filled, ApplyDetectionAsync);
        apply.HorizontalAlignment = HorizontalAlignment.Stretch;
        var write = Ui.AsyncButton("Write to folder…", Icons.Save, ButtonKind.Text, WriteDetectionAsync);
        write.HorizontalAlignment = HorizontalAlignment.Left;
        write.ToolTip = Loc.T("Writes the Dockerfile and the files switched on above into the project folder. Existing files are only replaced after you confirm.");
        Ui.Add(detectHost, ignoreSwitch, 8);
        Ui.Add(detectHost, composeSwitch, 4);
        Ui.Add(detectHost, apply, 12);
        Ui.Add(detectHost, write, 4);
    }

    /// <summary>One finding as a tile: what was found, and the files it was read from.</summary>
    private static UIElement FindingRow(ProjectFinding finding)
    {
        var warning = finding.Category == "Warning";
        var label = Ui.Text(finding.Label, 14, FontWeights.SemiBold, warning ? "Warning" : "OnSurface", true, false);
        var from = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        from.SetResourceReference(TextBlock.ForegroundProperty, "OnSurfaceVariant");
        from.Inlines.Add(new System.Windows.Documents.Run(Loc.T("from") + " "));
        var files = new System.Windows.Documents.Run(string.Join(", ", finding.Evidence));
        files.SetResourceReference(System.Windows.Documents.TextElement.FontFamilyProperty, "MonoFont");
        from.Inlines.Add(files);
        var stack = Ui.VStack(2, label, from);
        if (finding.Evidence.Count == 0) from.Visibility = Visibility.Collapsed;
        if (finding.Detail.Length > 0) stack.Children.Add(Ui.Text(finding.Detail, 12, null, "OnSurfaceVariant", true, false).Margin(0, 2, 0, 0));
        var tile = Ui.Card(stack, 0, "SurfaceContainer", 12);
        tile.Padding = new Thickness(12, 10, 12, 10);
        tile.ToolTip = Loc.T(finding.Category);
        System.Windows.Automation.AutomationProperties.SetName(tile, $"{Loc.T(finding.Category)}: {finding.Label}. {Loc.T("Evidence")}: {string.Join(", ", finding.Evidence)}");
        return tile;
    }

    private async Task ApplyDetectionAsync()
    {
        if (detection is not { Recognised: true } d) return;
        var hasContent = doc.Stages.Count > 0 || doc.Preamble.Count > 0;
        if (hasContent && !await Host.Overlay.ConfirmAsync("Apply the detected suggestion?", $"{d.Suggestion} for {d.Folder}." + Environment.NewLine + Environment.NewLine + Loc.T("This replaces the current draft. The current draft is saved in local history first, so you can restore it from Activity."), "Replace draft"))
            return;
        var suggestion = ProjectDetector.Suggest(d);
        if (!detectIgnore) suggestion.IgnorePatterns = doc.IgnorePatterns.ToList();
        ReplaceDraft(suggestion, "applying the detected " + d.Suggestion + " suggestion");
        ShowCompose(detectCompose ? ProjectDetector.ComposeFor(d, doc) : null);
        ignoreContext = d.Folder;
        UpdateIgnoreContextText();
        Host.Notify.Success(Loc.T("Draft created for") + " " + d.Suggestion + ". " + Loc.T("Review the checks, then use Write to folder or Save as."));
    }

    private async Task WriteDetectionAsync()
    {
        if (detection is not { Recognised: true } d) return;
        FlushCommit();
        if (doc.Stages.Count == 0) throw new InvalidOperationException("Apply the suggestion or build a draft before writing files.");
        var files = new Dictionary<string, string> { [Path.Combine(d.Folder, "Dockerfile")] = DockerfileModel.Render(doc) };
        if (detectIgnore) files[Path.Combine(d.Folder, ".dockerignore")] = Dockerignore.Render(doc.IgnorePatterns);
        if (detectCompose)
        {
            if (composeText.Length == 0) ShowCompose(ProjectDetector.ComposeFor(d, doc));
            files[Path.Combine(d.Folder, "compose.yaml")] = composeText;
        }
        if (!await WriteProjectFilesAsync(files, "Wrote project files from the Creator")) return;
        savedPath = Path.Combine(d.Folder, "Dockerfile");
        savedText = files[savedPath];
        UpdateSubtitle();
        if (detectCompose && d.ExistingCompose.Length > 0 && d.ExistingCompose != "compose.yaml")
            Host.Notify.Warning($"The folder also has {d.ExistingCompose}; Docker Compose prefers compose.yaml when both exist.");
    }
}
