using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Left configuration card of the Build page: context, tags, target, platforms, call mode, destination, build
/// arguments, switches, the expandable advanced sections and a live command preview. It assembles the validated
/// buildx arguments through <see cref="BuildKitCommand"/>.
/// </summary>
public sealed class BuildOptionsPanel : StackPanel
{
    private static AppHost Host => AppHost.Current;
    private static readonly string[] DestinationNames = ["Load locally", "Push", "OCI archive", "Export files"];
    private static readonly string[] CallNames = ["Build", "Check", "Outline", "Targets"];

    public TextBox ContextFolder { get; } = Ui.Input("", "Build context", true);
    public TextBox DockerfilePath { get; } = Ui.Input("", "Dockerfile", true);
    public TagInput Tags { get; } = new();
    public ComboBox Target { get; } = new() { IsEditable = true };
    public BuildPlatformChips Platforms { get; } = new();
    public BuildKitDestination Destination { get; private set; } = BuildKitDestination.Load;
    public BuildKitCall Call { get; private set; } = BuildKitCall.Build;
    public BuildCommandPreview Preview { get; }
    private readonly TextBox ociPath = Ui.Input("", "OCI archive path", true);
    private readonly TextBox exportFolder = Ui.Input("", "Export folder", true);
    private readonly FrameworkElement ociRow, exportRow, destinationField;
    private readonly BuildKeyValueRows buildArgs, labels;
    private readonly TextBox cacheFrom = Ui.CodeBox("", 64), cacheTo = Ui.CodeBox("", 64);
    private readonly BuildSecretFiles secrets;
    private readonly BuildAdvancedOptions advanced;
    private readonly CheckBox pull, noCache;
    private readonly TextBlock platformNote = Ui.Caption("");
    private readonly TextBlock callNote = Ui.Caption("");
    /// <summary>Raised when the user picks a new context folder.</summary>
    public event Action<string>? FolderChosen;
    /// <summary>Raised when the user asks to open a Dockerfile.</summary>
    public event Action? OpenRequested;
    /// <summary>Raised when the call mode changes (the page relabels its Build button).</summary>
    public event Action? CallChanged;

    public BuildOptionsPanel()
    {
        Preview = new BuildCommandPreview(() => CreatePreview());
        DockerfilePath.IsReadOnly = true;
        DockerfilePath.ToolTip = Loc.T("The file Save writes to. Use Open or Save as to change it; editing a path never redirects a save.");
        var browse = Ui.IconButton(Icons.FolderOpen, "Browse for folder", BrowseFolder);
        var openFile = Ui.IconButton(Icons.FileExport, "Open Dockerfile", () => OpenRequested?.Invoke());
        ContextFolder.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter && Directory.Exists(ContextFolder.Text)) FolderChosen?.Invoke(Path.GetFullPath(ContextFolder.Text)); };
        foreach (var box in new[] { ContextFolder, DockerfilePath, ociPath, exportFolder, cacheFrom, cacheTo }) box.TextChanged += (_, _) => Changed();
        Tags.Changed += Changed;
        Platforms.Changed += () => { UpdatePlatformNote(); Changed(); };

        Target.SetResourceReference(Control.FontFamilyProperty, "MonoFont");
        Target.ToolTip = Loc.T("Leave empty to build the final stage.");
        Target.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Changed()));
        Target.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(Changed);

        var call = Ui.Segmented(CallNames, CallNames[0], option =>
        {
            Call = (BuildKitCall)Array.IndexOf(CallNames, option);
            callNote.Text = Loc.T(Call switch
            {
                BuildKitCall.Check => "Check evaluates the Dockerfile rules and reports warnings. Nothing is built or exported.",
                BuildKitCall.Outline => "Outline lists the build arguments, secrets and SSH ids the chosen target uses. Nothing is built.",
                BuildKitCall.Targets => "Targets lists the stages you can build, with their descriptions. Nothing is built.",
                _ => ""
            });
            callNote.Visibility = Call == BuildKitCall.Build ? Visibility.Collapsed : Visibility.Visible;
            destinationField!.Visibility = Call == BuildKitCall.Build ? Visibility.Visible : Visibility.Collapsed;
            UpdateDestinationRows();
            CallChanged?.Invoke();
            Changed();
        });
        callNote.Visibility = Visibility.Collapsed;

        var destination = Ui.Segmented(DestinationNames, DestinationNames[0], option =>
        {
            Destination = (BuildKitDestination)Array.IndexOf(DestinationNames, option);
            UpdateDestinationRows();
            UpdatePlatformNote();
            Changed();
        });
        destinationField = Ui.Field("Destination", destination, "Load locally puts the image in this engine. Push publishes every tag to its registry after confirmation. OCI archive writes a tar file. Export files writes the final filesystem to a folder (type=local) without creating an image.", null);
        var ociBrowse = Ui.IconButton(Icons.FolderOpen, "Choose OCI archive", () =>
        {
            var dialog = new SaveFileDialog { Filter = "OCI tar archive|*.tar", FileName = "image.oci.tar", OverwritePrompt = false };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true) ociPath.Text = dialog.FileName;
        });
        ociRow = Ui.Field("OCI archive path", Ui.Columns(4, [1, 0], ociPath, ociBrowse), "The archive file BuildKit writes. An existing file is replaced only after you confirm.", @"C:\images\hello-api.oci.tar");
        var exportBrowse = Ui.IconButton(Icons.FolderOpen, "Choose export folder", () =>
        {
            var dialog = new OpenFolderDialog { Title = Loc.T("Choose the folder for exported files") };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true) exportFolder.Text = dialog.FolderName;
        });
        exportRow = Ui.Field("Export folder", Ui.Columns(4, [1, 0], exportFolder, exportBrowse), "BuildKit writes the files of the final stage into this folder (--output type=local,dest=…). Files with the same names are replaced, so a non-empty folder asks for confirmation.", @"C:\Projects\hello-api\dist");

        buildArgs = new BuildKeyValueRows(new List<NameValue>(), "Argument name", "Argument value", "Add argument", ValidateArgName, Changed);
        labels = new BuildKeyValueRows(new List<NameValue>(), "Label key", "Label value", "Add label", ValidateLabelKey, Changed);
        pull = Ui.Switch("Pull newer base images", false, _ => Changed(), "Always check the registry for newer FROM images (--pull).");
        noCache = Ui.Switch("Ignore build cache", false, _ => Changed(), "Rebuild every step from scratch (--no-cache).");
        secrets = new BuildSecretFiles(Changed);
        advanced = new BuildAdvancedOptions(Changed);

        var extras = Ui.VStack(14,
            Ui.Field("Image labels", labels, "Metadata stored in the image, one key and value per row (--label).", "org.opencontainers.image.version = 1.4.0"),
            Ui.Field("Cache from", cacheFrom, "One BuildKit cache source per line. Backends depend on the installed builder. Do not enter access keys or credentials.", "type=local,src=C:/cache\ntype=registry,ref=registry.example.com/team/cache"),
            Ui.Field("Cache to", cacheTo, "One cache export per line. Exporting writes to that destination, so the build asks for confirmation first.", "type=local,dest=C:/cache,mode=max"),
            secrets);
        var expander = new Expander { Header = Loc.T("Labels, cache and secrets"), Content = extras };
        System.Windows.Automation.AutomationProperties.SetName(expander, Loc.T("Labels, cache and secrets"));

        Orientation = Orientation.Vertical;
        foreach (var element in new UIElement[]
        {
            Ui.Field("Build context", Ui.Columns(4, [1, 0], ContextFolder, browse), "The folder sent to the builder. COPY and ADD paths are relative to it. Choosing a folder loads its Dockerfile when there is one.", @"C:\Projects\hello-api"),
            Ui.Field("Dockerfile", Ui.Columns(4, [1, 0], DockerfilePath, openFile), "The Dockerfile used for the build (--file). Save writes to this file.", @"C:\Projects\hello-api\Dockerfile"),
            Ui.VStack(4, Ui.Field("Tags", Tags, "One or more image references. Press Enter or comma after each tag. Push uses every tag as a destination.", "hello-api:latest, registry.example.com/team/hello-api:1.4.0"), Tags.ErrorText),
            Ui.Field("Target stage", Target, "Build only up to this named stage (--target). The list comes from the stages in the current editor source.", "runtime"),
            Ui.VStack(4, Ui.Field("Platforms", Platforms, "Target platforms (--platform). Select none to use the builder's native platform. Load locally supports exactly one; choose Push, OCI archive or Export files for several.", "linux/amd64 + linux/arm64 with Push"), platformNote),
            Ui.VStack(6, Ui.Field("Mode", call, "What buildx does with the Dockerfile (--call). Build runs the build. Check, Outline and Targets inspect it without building and show a structured result.", "--call=targets,format=json"), callNote),
            destinationField, ociRow, exportRow,
            Ui.Field("Build arguments", buildArgs, "Values for ARG instructions (--build-arg). Names use letters, digits and underscores. Do not put secrets in build arguments; they are stored in the image history.", "NODE_VERSION = 22"),
            pull, noCache, expander, advanced.ContextsSection, advanced.AttestationsSection, advanced.SandboxSection,
            Ui.Card(Preview, 12, "SurfaceContainer", 14)
        }) Ui.Add(this, element, 14);
        platformNote.Visibility = Visibility.Collapsed;
        UpdateDestinationRows();
        Loaded += (_, _) => Preview.Invalidate();
    }

    public static string? ValidateArgName(string name) => BuildKitCommand.ValidateArgName(name);
    public static string? ValidateLabelKey(string key) => key.Length == 0 ? "Enter a label key." : key.StartsWith('-') ? "Label keys cannot begin with a hyphen." : key.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == '=') ? "Label keys cannot contain spaces or '='." : null;

    private void Changed() => Preview?.Invalidate();

    private void UpdateDestinationRows()
    {
        var build = Call == BuildKitCall.Build;
        ociRow.Visibility = build && Destination == BuildKitDestination.Oci ? Visibility.Visible : Visibility.Collapsed;
        exportRow.Visibility = build && Destination == BuildKitDestination.Local ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdatePlatformNote()
    {
        var problem = Call == BuildKitCall.Build && Destination == BuildKitDestination.Load && Platforms.Platforms.Count > 1;
        platformNote.Text = problem ? Loc.T("Load locally supports one platform. Choose Push, OCI archive or Export files for a multi-platform build.") : "";
        platformNote.SetResourceReference(TextBlock.ForegroundProperty, "Error");
        platformNote.Visibility = problem ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BrowseFolder()
    {
        var dialog = new OpenFolderDialog { Title = Loc.T("Choose build context") };
        if (Directory.Exists(ContextFolder.Text)) dialog.InitialDirectory = ContextFolder.Text;
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        ContextFolder.Text = dialog.FolderName;
        FolderChosen?.Invoke(dialog.FolderName);
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

    /// <summary>What the result card needs to know about the request after the run.</summary>
    public BuildKitRequestSummary Summary() => new(Destination, Tags.Tags.ToList(), advanced.Sbom, advanced.Provenance);

    /// <summary>Reads every control into a request. Paths are made absolute; nothing is validated here.</summary>
    private BuildKitRequest Snapshot(string dockerfile, string? metadataFile, bool preview)
    {
        static string Full(string path) => path.Trim().Length == 0 ? "" : Path.GetFullPath(path.Trim());
        var context = Full(ContextFolder.Text);
        var output = Destination == BuildKitDestination.Oci ? ociPath.Text : Destination == BuildKitDestination.Local ? exportFolder.Text : null;
        return new BuildKitRequest
        {
            ContextPath = preview && context.Length == 0 ? "<context folder>" : context,
            Dockerfile = preview && dockerfile.Length == 0 ? "<Dockerfile>" : dockerfile,
            Tags = Tags.Tags.ToList(), Platforms = Platforms.Platforms,
            Target = (Target.Text ?? "").Trim() is { Length: > 0 } t ? t : null,
            BuildArguments = buildArgs.Pairs(), Labels = labels.Pairs(),
            NoCache = noCache.IsChecked == true, Pull = pull.IsChecked == true,
            Destination = Destination, Call = Call,
            OutputPath = string.IsNullOrWhiteSpace(output) ? (preview ? "<choose a destination>" : null) : Full(output),
            CacheFrom = StructuredInputs.Lines(cacheFrom.Text), CacheTo = StructuredInputs.Lines(cacheTo.Text),
            SecretFiles = secrets.Files, SecretEnvironment = secrets.EnvironmentSecrets,
            NamedContexts = advanced.NamedContexts, SshDefault = advanced.SshDefault, SshKeys = advanced.SshKeys,
            Sbom = advanced.Sbom, Provenance = advanced.Provenance, Annotations = advanced.Annotations,
            Network = advanced.Network, ShmSize = advanced.ShmSize, Ulimits = advanced.Ulimits, ExtraHosts = advanced.ExtraHosts,
            MetadataFile = metadataFile
        };
    }

    private IReadOnlyList<string> CreatePreview()
    {
        var request = Snapshot(DockerfilePath.Text.Trim(), "<temporary metadata file>", true);
        foreach (var tag in request.Tags) if (TagInput.Validate(tag) is { } problem) throw new ArgumentException(problem);
        return BuildKitCommand.Create(request, _ => true, _ => true);
    }

    /// <summary>
    /// Assembles and validates every argument with real file checks, then asks the confirmations the destination
    /// needs. Returns null when the user declines. Throws ArgumentException with a clear message for invalid input.
    /// Secret file contents and environment values are never read.
    /// </summary>
    public async Task<List<string>?> CreateArgumentsAsync(string dockerfile, string? metadataFile)
    {
        var request = Snapshot(dockerfile, metadataFile, false);
        foreach (var tag in request.Tags) if (TagInput.Validate(tag) is { } problem) throw new ArgumentException(problem);
        var args = BuildKitCommand.Create(request, File.Exists, Directory.Exists);
        foreach (var (_, variable) in request.SecretEnvironment)
            if (Environment.GetEnvironmentVariable(variable) == null)
                throw new ArgumentException($"The environment variable {variable} is not set for this application, so BuildKit would receive no secret. Set it and restart the application, or remove the row.");
        if (request.Call != BuildKitCall.Build) return args;
        if (request.Destination == BuildKitDestination.Oci && File.Exists(request.OutputPath) && !await Host.Overlay.ConfirmAsync("Replace existing OCI archive?", request.OutputPath!, "Replace", true)) return null;
        if (request.Destination == BuildKitDestination.Local && Directory.EnumerateFileSystemEntries(request.OutputPath!).Any()
            && !await Host.Overlay.ConfirmAsync("Export into a folder that is not empty?", request.OutputPath! + Environment.NewLine + Environment.NewLine + Loc.T("Files with the same names are replaced by the build output."), "Export", true)) return null;
        if (request.Destination == BuildKitDestination.Push && !await Host.Overlay.ConfirmAsync("Publish image tags to their registries?", string.Join(Environment.NewLine, request.Tags) + Environment.NewLine + Environment.NewLine + Loc.T("The build pushes with the existing Docker sign-in for each registry."), "Build and push"))
            return null;
        if (request.CacheTo.Count > 0 && !await Host.Overlay.ConfirmAsync("Export build cache to these destinations?", string.Join(Environment.NewLine, request.CacheTo), "Export cache"))
            return null;
        return args;
    }
}
