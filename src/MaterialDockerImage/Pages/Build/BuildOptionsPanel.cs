using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage.Pages;

public enum BuildDestination { Load, Push, Oci }

/// <summary>
/// Left configuration card of the Build page: context, tags, target, platform, destination, build arguments,
/// switches and the expandable labels/cache/secrets section. It also assembles the validated buildx arguments.
/// </summary>
public sealed class BuildOptionsPanel : StackPanel
{
    private static AppHost Host => AppHost.Current;
    public static readonly string[] CommonPlatforms = ["linux/amd64", "linux/arm64", "linux/arm/v7", "linux/amd64,linux/arm64", "windows/amd64"];
    private static readonly string[] DestinationNames = ["Load locally", "Push", "OCI archive"];

    public TextBox ContextFolder { get; } = Ui.Input("", "Build context", true);
    public TextBox DockerfilePath { get; } = Ui.Input("", "Dockerfile", true);
    public TagInput Tags { get; } = new();
    public ComboBox Target { get; } = new() { IsEditable = true };
    public ComboBox Platform { get; } = Ui.Combo(CommonPlatforms, "", true);
    public BuildDestination Destination { get; private set; } = BuildDestination.Load;
    private readonly TextBox ociPath = Ui.Input("", "OCI archive path", true);
    private readonly FrameworkElement ociRow;
    private readonly List<NameValue> buildArgItems = new();
    private readonly List<NameValue> labelItems = new();
    private readonly BuildKeyValueRows buildArgs, labels;
    private readonly TextBox cacheFrom = Ui.CodeBox("", 64), cacheTo = Ui.CodeBox("", 64);
    private readonly StackPanel secretRows = new();
    private readonly List<SecretRow> secrets = new();
    private readonly CheckBox pull, noCache;
    private readonly TextBlock platformNote = Ui.Caption("");
    /// <summary>Raised when the user picks a new context folder.</summary>
    public event Action<string>? FolderChosen;
    /// <summary>Raised when the user asks to open a Dockerfile.</summary>
    public event Action? OpenRequested;

    private sealed record SecretRow(TextBox Id, TextBox Path, FrameworkElement Element);

    public BuildOptionsPanel()
    {
        DockerfilePath.IsReadOnly = true;
        DockerfilePath.ToolTip = Loc.T("The file Save writes to. Use Open or Save as to change it; editing a path never redirects a save.");
        var browse = Ui.IconButton(Icons.FolderOpen, "Browse for folder", BrowseFolder);
        var openFile = Ui.IconButton(Icons.FileExport, "Open Dockerfile", () => OpenRequested?.Invoke());
        ContextFolder.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter && Directory.Exists(ContextFolder.Text)) FolderChosen?.Invoke(Path.GetFullPath(ContextFolder.Text)); };

        Target.SetResourceReference(Control.FontFamilyProperty, "MonoFont");
        Target.ToolTip = Loc.T("Leave empty to build the final stage.");
        Platform.SetResourceReference(Control.FontFamilyProperty, "MonoFont");
        Platform.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => UpdatePlatformNote()));
        Platform.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(UpdatePlatformNote);

        var destination = Ui.Segmented(DestinationNames, DestinationNames[0], option =>
        {
            Destination = (BuildDestination)Array.IndexOf(DestinationNames, option);
            ociRow!.Visibility = Destination == BuildDestination.Oci ? Visibility.Visible : Visibility.Collapsed;
            UpdatePlatformNote();
        });
        var ociBrowse = Ui.IconButton(Icons.FolderOpen, "Choose OCI archive", () =>
        {
            var dialog = new SaveFileDialog { Filter = "OCI tar archive|*.tar", FileName = "image.oci.tar", OverwritePrompt = false };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true) ociPath.Text = dialog.FileName;
        });
        ociRow = Ui.Field("OCI archive path", Ui.Columns(4, [1, 0], ociPath, ociBrowse), "The archive file BuildKit writes. An existing file is replaced only after you confirm.", @"C:\images\hello-api.oci.tar");
        ociRow.Visibility = Visibility.Collapsed;

        buildArgs = new BuildKeyValueRows(buildArgItems, "Argument name", "Argument value", "Add argument", ValidateArgName, () => { });
        labels = new BuildKeyValueRows(labelItems, "Label key", "Label value", "Add label", ValidateLabelKey, () => { });
        pull = Ui.Switch("Pull newer base images", false, _ => { }, "Always check the registry for newer FROM images (--pull).");
        noCache = Ui.Switch("Ignore build cache", false, _ => { }, "Rebuild every step from scratch (--no-cache).");

        var addSecret = Ui.Button("Add secret file", Icons.Key, ButtonKind.Text, AddSecret);
        addSecret.HorizontalAlignment = HorizontalAlignment.Left;
        var extras = Ui.VStack(14,
            Ui.Field("Image labels", labels, "Metadata stored in the image, one key and value per row (--label).", "org.opencontainers.image.version = 1.4.0"),
            Ui.Field("Cache from", cacheFrom, "One BuildKit cache source per line. Backends depend on the installed builder. Do not enter access keys or credentials.", "type=local,src=C:/cache\ntype=registry,ref=registry.example.com/team/cache"),
            Ui.Field("Cache to", cacheTo, "One cache export per line. Exporting writes to that destination, so the build asks for confirmation first.", "type=local,dest=C:/cache,mode=max"),
            Ui.Field("BuildKit secret files", Ui.VStack(6, secretRows, addSecret),
                "Choose an existing local file and the id used by RUN --mount=type=secret. Only the path is passed to BuildKit; this application never reads, logs or saves the file contents or the selection.",
                "Id: npmrc  ·  File: C:\\Users\\me\\.npmrc\nRUN --mount=type=secret,id=npmrc,target=/root/.npmrc npm ci"));
        var expander = new Expander { Header = Loc.T("Labels, cache and secrets"), Content = extras };
        System.Windows.Automation.AutomationProperties.SetName(expander, Loc.T("Labels, cache and secrets"));

        Orientation = Orientation.Vertical;
        foreach (var element in new UIElement[]
        {
            Ui.Field("Build context", Ui.Columns(4, [1, 0], ContextFolder, browse), "The folder sent to the builder. COPY and ADD paths are relative to it. Choosing a folder loads its Dockerfile when there is one.", @"C:\Projects\hello-api"),
            Ui.Field("Dockerfile", Ui.Columns(4, [1, 0], DockerfilePath, openFile), "The Dockerfile used for the build (--file). Save writes to this file.", @"C:\Projects\hello-api\Dockerfile"),
            Ui.VStack(4, Ui.Field("Tags", Tags, "One or more image references. Press Enter or comma after each tag. Push uses every tag as a destination.", "hello-api:latest, registry.example.com/team/hello-api:1.4.0"), Tags.ErrorText),
            Ui.Columns(12, [1, 1],
                Ui.Field("Target stage", Target, "Build only up to this named stage (--target). The list comes from the stages in the current editor source.", "runtime"),
                Ui.Field("Platform", Platform, "Target platform (--platform). Separate several with commas; a local load supports only one.", "linux/amd64")),
            platformNote,
            Ui.Field("Destination", destination, "Load locally puts the image in this engine. Push publishes every tag to its registry after confirmation. OCI archive writes a tar file.", null),
            ociRow,
            Ui.Field("Build arguments", buildArgs, "Values for ARG instructions (--build-arg). Names use letters, digits and underscores. Do not put secrets in build arguments; they are stored in the image history.", "NODE_VERSION = 22"),
            pull, noCache, expander
        }) Ui.Add(this, element, 14);
        platformNote.Visibility = Visibility.Collapsed;
    }

    public static string? ValidateArgName(string name) => Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.None, TimeSpan.FromMilliseconds(100)) ? null : "Use letters, digits and underscores, starting with a letter or underscore.";
    public static string? ValidateLabelKey(string key) => key.Length == 0 ? "Enter a label key." : key.StartsWith('-') ? "Label keys cannot begin with a hyphen." : key.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == '=') ? "Label keys cannot contain spaces or '='." : null;

    private void UpdatePlatformNote()
    {
        var count = PlatformList().Length;
        var problem = Destination == BuildDestination.Load && count > 1;
        platformNote.Text = problem ? Loc.T("Load locally supports one platform. Choose Push or OCI archive for a multi-platform build.") : "";
        platformNote.SetResourceReference(TextBlock.ForegroundProperty, "Error");
        platformNote.Visibility = problem ? Visibility.Visible : Visibility.Collapsed;
    }

    public string[] PlatformList() => (Platform.Text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private void BrowseFolder()
    {
        var dialog = new OpenFolderDialog { Title = Loc.T("Choose build context") };
        if (Directory.Exists(ContextFolder.Text)) dialog.InitialDirectory = ContextFolder.Text;
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        ContextFolder.Text = dialog.FolderName;
        FolderChosen?.Invoke(dialog.FolderName);
    }

    private void AddSecret()
    {
        var picker = new OpenFileDialog { Title = Loc.T("Choose a BuildKit secret source file") };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        var id = Ui.Input(SuggestSecretId(picker.FileName), "Secret ID", true);
        var path = Ui.Input(picker.FileName, "Selected local file", true);
        path.IsReadOnly = true;
        SecretRow? row = null;
        var replace = Ui.IconButton(Icons.FolderOpen, "Replace secret file", () =>
        {
            var replacement = new OpenFileDialog { Title = Loc.T("Replace the BuildKit secret source") };
            if (replacement.ShowDialog(Window.GetWindow(this)) == true) path.Text = replacement.FileName;
        });
        var remove = Ui.IconButton(Icons.Delete, "Remove secret", () => { secrets.Remove(row!); secretRows.Children.Remove(row!.Element); });
        var element = Ui.Card(Ui.VStack(8, Ui.Columns(8, [1, 0, 0], Ui.Field("Secret ID", id), replace, remove), Ui.Field("Selected local file", path)), 12, "SurfaceContainer", 12);
        element.Margin = new Thickness(0, 0, 0, 8);
        replace.VerticalAlignment = remove.VerticalAlignment = VerticalAlignment.Bottom;
        row = new SecretRow(id, path, element);
        secrets.Add(row);
        secretRows.Children.Add(element);
    }

    private static string SuggestSecretId(string path)
    {
        var name = Regex.Replace(Path.GetFileName(path).TrimStart('.'), "[^A-Za-z0-9_.-]", "_", RegexOptions.None, TimeSpan.FromMilliseconds(100));
        return name.Length > 0 && (char.IsLetter(name[0]) || name[0] == '_') ? name : "secret";
    }

    /// <summary>Fills the target list from the stage names of the current source, keeping the typed value.</summary>
    public void SetStages(IEnumerable<string> names)
    {
        var text = Target.Text;
        var list = new List<string> { "" };
        list.AddRange(names.Where(n => !int.TryParse(n, out _)).Distinct(StringComparer.OrdinalIgnoreCase));
        if (Target.ItemsSource is List<string> old && old.SequenceEqual(list)) return;
        Target.ItemsSource = list;
        Target.Text = text;
    }

    /// <summary>
    /// Assembles and validates every argument. Returns null when the user declines a confirmation. Throws
    /// ArgumentException with a clear message for invalid input. Secret file contents are never read.
    /// </summary>
    public async Task<List<string>?> CreateArgumentsAsync(string dockerfile)
    {
        var tags = Tags.Tags.ToList();
        foreach (var tag in tags) if (TagInput.Validate(tag) is { } problem) throw new ArgumentException(problem);
        var platforms = PlatformList();
        if (Destination == BuildDestination.Load && platforms.Length > 1) throw new ArgumentException("Local load supports one platform in this editor. Choose registry push or OCI export for a multi-platform build.");
        if (!Directory.Exists(ContextFolder.Text)) throw new ArgumentException("Choose an existing build context folder.");
        string? export = null;
        if (Destination == BuildDestination.Oci)
        {
            if (string.IsNullOrWhiteSpace(ociPath.Text)) throw new ArgumentException("Choose where to write the OCI archive.");
            var path = Path.GetFullPath(ociPath.Text);
            if (!Directory.Exists(Path.GetDirectoryName(path))) throw new ArgumentException("Choose an OCI archive in an existing output directory.");
            export = StructuredInputs.Export(path);
            if (File.Exists(path) && !await Host.Overlay.ConfirmAsync("Replace existing OCI archive?", path, "Replace", true)) return null;
        }
        if (Destination == BuildDestination.Push && tags.Count == 0) throw new ArgumentException("Registry push requires at least one explicit destination tag.");
        var target = (Target.Text ?? "").Trim();
        var options = new BuildOptions
        {
            ContextPath = ContextFolder.Text, Dockerfile = dockerfile, Tags = tags,
            Platforms = platforms.Length == 0 ? Array.Empty<string>() : [string.Join(',', platforms)],
            Target = target.Length == 0 ? null : target,
            BuildArguments = buildArgs.ToDictionary("Build arguments"), Labels = labels.ToDictionary("Image labels"),
            NoCache = noCache.IsChecked == true, Pull = pull.IsChecked == true,
            Load = Destination == BuildDestination.Load, Push = Destination == BuildDestination.Push, Output = export
        };
        var args = DockerService.CreateBuildArguments(options).ToList();
        var context = args[^1]; args.RemoveAt(args.Count - 1);
        foreach (var spec in StructuredInputs.Lines(cacheFrom.Text)) args.AddRange(["--cache-from", StructuredInputs.Cache(spec)]);
        var exportCaches = StructuredInputs.Lines(cacheTo.Text);
        foreach (var spec in exportCaches) args.AddRange(["--cache-to", StructuredInputs.Cache(spec)]);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var secret in secrets)
        {
            var id = secret.Id.Text.Trim();
            if (!ids.Add(id)) throw new ArgumentException("BuildKit secret identifiers must be unique.");
            if (!File.Exists(secret.Path.Text)) throw new ArgumentException("A selected BuildKit secret file is unavailable. Replace it or remove its row.");
            args.AddRange(["--secret", StructuredInputs.Secret(id, secret.Path.Text)]);
        }
        if (Destination == BuildDestination.Push && !await Host.Overlay.ConfirmAsync("Publish image tags to their registries?", string.Join(Environment.NewLine, tags) + Environment.NewLine + Environment.NewLine + Loc.T("The build pushes with the existing Docker sign-in for each registry."), "Build and push"))
            return null;
        if (exportCaches.Length > 0 && !await Host.Overlay.ConfirmAsync("Export build cache to these destinations?", string.Join(Environment.NewLine, exportCaches), "Export cache"))
            return null;
        args.Add(context);
        return args;
    }
}
