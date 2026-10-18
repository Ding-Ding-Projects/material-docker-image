using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using MaterialDockerImage.Core;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage;

public partial class MainWindow : Window
{
    private readonly DockerService docker = new();
    private readonly UserSettings settings = UserSettings.Load();
    private readonly Dictionary<string, TabItem> tabs = new();
    private readonly TextBox folder = new(), tag = new(), target = new(), platforms = new(), buildArgs = new();
    private readonly TextBox dockerfile = new() { AcceptsReturn = true, AcceptsTab = true, FontFamily = new FontFamily("Cascadia Mono, Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.NoWrap, MinHeight = 280 };
    private readonly TextBox composeFile = new(), composeSource = new() { AcceptsReturn = true, AcceptsTab = true, FontFamily = new FontFamily("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 250 };
    private readonly CheckBox noCache = new(), pullBase = new();
    private CancellationTokenSource? operation;
    private bool dirtyDockerfile, dirtyCompose, loading;
    private string dockerfilePath = "";
    private string loadedComposePath = "";
    private bool resettingContexts;
    private long contextGeneration;
    private readonly string[] destinations = ["Build", "Images", "Containers", "Compose", "Canvas", "Volumes", "Networks", "Diagnostics", "Settings", "Setup"];
    private string T(string text) => UiText.Get(text, settings.Language);
    private string? Context => Contexts.SelectedItem as string;

    public MainWindow()
    {
        InitializeComponent(); ApplyTheme(); FontSize = Math.Clamp(settings.FontSize, 12, 22);
        folder.Text = settings.BuildFolder; tag.Text = settings.ImageTag;
        noCache.Content = T("No cache"); pullBase.Content = T("Pull base images");
        dockerfile.TextChanged += (_, _) => { if (!loading) dirtyDockerfile = true; };
        composeSource.TextChanged += (_, _) => { if (!loading) dirtyCompose = true; };
        foreach (var name in destinations) Navigation.Children.Add(Button(name, () => OpenPage(name)));
        RefreshContext.Click += async (_, _) => await RefreshContexts();
        Contexts.SelectionChanged += (_, _) =>
        {
            if (resettingContexts) return;
            settings.Context = Context ?? ""; SaveSettings(); EngineState.Text = T("Not checked"); contextGeneration++;
            foreach (var key in new[] { "Images", "Containers", "Volumes", "Networks", "Canvas" })
                if (tabs.Remove(key, out var stale)) Workspaces.Items.Remove(stale);
            graphCanvas = null; graphObjects = null; graphInspector = null; graphState = null;
            Notify("Engine context changed. Resource views were invalidated; reopen and refresh before acting.");
        };
        CancelOperation.Click += (_, _) => { operation?.Cancel(); Notice.Text = "Cancellation requested. Engine-side work may continue; reconcile before retrying."; };
        ClearOutput.Click += (_, _) => Output.Clear();
        NewTab.Click += (_, _) => ShowCatalog();
        Closing += WindowClosing;
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            var index = e.Key - Key.D1;
            var shortcuts = new[] { "Build", "Images", "Containers", "Compose", "Canvas", "Diagnostics", "Settings", "Setup" };
            if (index >= 0 && index < shortcuts.Length) { OpenPage(shortcuts[index]); e.Handled = true; }
        };
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unavailable";
        var metadata = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(x => x.Key == "BuildTimestamp")?.Value;
        Provenance.Text = $"{T("Version")} {version}  ·  {T("Updated")}: " + (DateTimeOffset.TryParse(metadata, out var timestamp) ? timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") : "provenance unavailable");
        OpenPage(settings.SetupComplete ? "Build" : "Setup");
        Loaded += async (_, _) => await RefreshContexts();
    }
    private void SaveSettings() { try { settings.Save(); } catch (Exception e) { Notice.Text = "Settings could not be saved: " + e.Message; } }
    private Button Button(string label, Action action, bool primary = false)
    {
        var b = new Button { Content = T(label), HorizontalContentAlignment = HorizontalAlignment.Center };
        if (primary) { b.SetResourceReference(Control.BackgroundProperty, "Accent"); b.SetResourceReference(Control.ForegroundProperty, "AccentInk"); }
        b.Click += (_, _) => { try { action(); } catch (Exception e) { Notify(e.Message); } }; return b;
    }
    private Button AsyncButton(string label, Func<Task> action, bool primary = false)
    {
        var b = Button(label, () => { }, primary);
        b.Click += async (_, _) => { if (!b.IsEnabled) return; b.IsEnabled = false; try { await action(); } catch (Exception e) { Notify(e.Message); } finally { b.IsEnabled = true; } }; return b;
    }
    private StackPanel Page(string title, string subtitle)
    {
        var p = new StackPanel { Margin = new Thickness(12) };
        p.Children.Add(new TextBlock { Text = T(title), FontSize = 30, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 6) });
        p.Children.Add(new TextBlock { Text = subtitle, Opacity = .7, Margin = new Thickness(0, 0, 0, 20) }); return p;
    }
    private void Field(Panel p, string label, Control control)
    {
        p.Children.Add(new TextBlock { Text = T(label), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        System.Windows.Automation.AutomationProperties.SetName(control, T(label)); p.Children.Add(control);
    }
    private WrapPanel Actions(Panel p) { var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 12) }; p.Children.Add(row); return row; }
    private void Notify(string text) { Notice.Text = text; }
    private void OpenPage(string name)
    {
        if (tabs.TryGetValue(name, out var found)) { Workspaces.SelectedItem = found; return; }
        FrameworkElement content = name switch
        {
            "Build" => BuildPage(), "Images" => ImagesPage(), "Containers" => ContainersPage(), "Compose" => ComposePage(),
            "Canvas" => CanvasPage(), "Volumes" => ResourcesPage("volume"), "Networks" => ResourcesPage("network"),
            "Diagnostics" => DiagnosticsPage(), "Settings" => SettingsPage(), "Setup" => SetupPage(), _ => new TextBlock()
        };
        var tab = new TabItem { Header = T(name), Content = content is Grid ? content : new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
        tabs.Add(name, tab); Workspaces.Items.Add(tab); Workspaces.SelectedItem = tab;
    }
    private void ShowCatalog()
    {
        var p = Page("New tab", "Choose a destination. Existing workspaces are focused without duplicating drafts.");
        var search = new SearchFilter(); p.Children.Add(search); var list = new ListBox { MinHeight = 280, Margin = new Thickness(0, 12, 0, 0) }; p.Children.Add(list);
        void Filter() { list.ItemsSource = destinations.Where(search.Matches).ToArray(); }
        search.Changed += Filter; Filter();
        var dialog = Dialog("New tab", p); p.Children.Add(Button("Open", () => { if (list.SelectedItem is string name) { dialog.Close(); OpenPage(name); } })); dialog.ShowDialog();
    }
    private Window Dialog(string title, FrameworkElement content) => new() { Owner = this, Title = T(title), Width = 620, Height = 600, MinWidth = 460, MinHeight = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, Background = (Brush)FindResource("Surface"), Foreground = (Brush)FindResource("Ink"), Resources = Resources };
    private string? Prompt(string title, string label, string initial = "")
    {
        var p = Page(title, "Review the exact value before applying."); var input = new TextBox { Text = initial }; Field(p, label, input); string? result = null;
        var d = Dialog(title, p); d.Height = 330; var row = Actions(p);
        row.Children.Add(Button("Apply", () => { if (string.IsNullOrWhiteSpace(input.Text) || input.Text.Trim().StartsWith('-')) { Notify("Enter a non-option value."); return; } result = input.Text.Trim(); d.Close(); }, true));
        row.Children.Add(Button("Cancel", d.Close)); d.ShowDialog(); return result;
    }
    private bool Confirm(string operationName, string reference) => MessageBox.Show(this, $"{operationName}\n\nResource: {reference}\nEngine context: {Context ?? "current"}\n\nThis changes the selected resource. Continue?", operationName, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    private async Task<CommandResult?> Execute(string label, IReadOnlyList<string> args, bool displayOutput = true, string? boundContext = null)
    {
        if (operation != null) { Notify("Another operation is active. Cancel it or wait for its actual result."); return null; }
        operation = new CancellationTokenSource(); CancelOperation.IsEnabled = true; Contexts.IsEnabled = false; RefreshContext.IsEnabled = false; Notice.Text = T("Working") + ": " + label;
        var watch = Stopwatch.StartNew(); Output.AppendText($"\n[{DateTimeOffset.Now:HH:mm:ss}] {label}\n");
        var progress = displayOutput ? new Progress<DockerOutput>(line => { if (Output.Text.Length > 300000) Output.Text = Output.Text[^200000..]; Output.AppendText(line.Text + Environment.NewLine); Output.ScrollToEnd(); }) : null;
        try
        {
            var result = await docker.ExecuteAsync(args, boundContext ?? Context, progress, operation.Token);
            Notice.Text = result.Cancelled ? $"Canceled after {watch.Elapsed.TotalSeconds:F1}s. Engine-side outcome requires refresh." : result.Success ? $"{label}: completed in {watch.Elapsed.TotalSeconds:F1}s (exit 0)." : $"{label}: failed (exit {result.ExitCode}). Review output and engine connection.";
            if (!displayOutput && !result.Success) Output.AppendText(result.StandardError + Environment.NewLine);
            return result;
        }
        catch (Exception e) { Notify(label + ": " + e.Message); return null; }
        finally { operation.Dispose(); operation = null; CancelOperation.IsEnabled = false; Contexts.IsEnabled = true; RefreshContext.IsEnabled = true; }
    }
    private async Task RefreshContexts()
    {
        try
        {
            var savedContext = Context ?? settings.Context;
            var requestedGeneration = contextGeneration;
            var values = await docker.ListContextsAsync();
            if (operation != null || contextGeneration != requestedGeneration) return;
            resettingContexts = true;
            Contexts.ItemsSource = values.Select(x => x.Name).ToArray();
            Contexts.SelectedItem = values.Any(x => x.Name == savedContext) ? savedContext : values.FirstOrDefault(x => x.Current)?.Name ?? values.FirstOrDefault()?.Name;
            resettingContexts = false;
            if (savedContext != Context) { contextGeneration++; foreach (var key in new[] { "Images", "Containers", "Volumes", "Networks", "Canvas" }) if (tabs.Remove(key,out var stale)) Workspaces.Items.Remove(stale); }
            settings.Context = Context ?? ""; SaveSettings();
            EngineState.Text = values.Count > 0 ? "Context available · connection unchecked" : "No contexts";
        }
        catch (Exception e) { resettingContexts = false; EngineState.Text = "Docker unavailable"; Notify("Docker discovery: " + e.Message); }
    }
    private FrameworkElement BuildPage()
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var p = Page("Build", "Create an image from your own Dockerfile and local context."); Field(p, "Build context", folder);
        p.Children.Add(Button("Browse", () => { var dialog = new OpenFolderDialog { Title = "Choose build context" }; if (dialog.ShowDialog(this) == true) { folder.Text = dialog.FolderName; settings.BuildFolder = folder.Text; WorkspaceName.Text = Path.GetFileName(folder.Text); SaveSettings(); var path = Path.Combine(folder.Text, "Dockerfile"); if (File.Exists(path)) LoadDockerfile(path); } }));
        Field(p, "Image tag", tag); Field(p, "Target stage", target); Field(p, "Platforms", platforms); platforms.ToolTip = "Comma-separated platforms, for example linux/amd64. A local load supports a single platform.";
        Field(p, "Build arguments", buildArgs); buildArgs.AcceptsReturn = true; buildArgs.Height = 80; buildArgs.ToolTip = "One NAME=value per line. Do not put secrets in build arguments.";
        p.Children.Add(noCache); p.Children.Add(pullBase);
        p.Children.Add(AsyncButton("Build image", BuildImage, true)); p.Children.Add(Button("Canvas", () => OpenPage("Canvas")));
        grid.Children.Add(new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var editor = new DockPanel { Margin = new Thickness(18, 10, 0, 0) }; Grid.SetColumn(editor, 1); var tools = new WrapPanel(); DockPanel.SetDock(tools, Dock.Top);
        tools.Children.Add(Button("Load Dockerfile", () => { var d = new OpenFileDialog { Filter = "Dockerfile|Dockerfile;*.dockerfile|All files|*.*" }; if (d.ShowDialog(this) == true) LoadDockerfile(d.FileName); }));
        tools.Children.Add(Button("Save Dockerfile", SaveDockerfile)); tools.Children.Add(Button("Undo", () => dockerfile.Undo()));
        editor.Children.Add(tools); editor.Children.Add(dockerfile); grid.Children.Add(editor); return grid;
    }
    private void LoadDockerfile(string path)
    {
        if (dirtyDockerfile && !Confirm("Discard unsaved Dockerfile edits", dockerfilePath.Length > 0 ? dockerfilePath : "current draft")) return;
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidOperationException("Dockerfile is larger than the 1 MiB editor limit.");
        loading = true; dockerfile.Text = File.ReadAllText(path); loading = false; dirtyDockerfile = false; dockerfilePath = path;
        Notify("Loaded Dockerfile. Source has not been executed.");
    }
    private void SaveDockerfile()
    {
        if (dockerfilePath.Length == 0) { var d = new SaveFileDialog { FileName = "Dockerfile", InitialDirectory = Directory.Exists(folder.Text) ? folder.Text : "" }; if (d.ShowDialog(this) != true) return; dockerfilePath = d.FileName; }
        File.WriteAllText(dockerfilePath, dockerfile.Text); dirtyDockerfile = false; Notify("Dockerfile saved.");
    }
    private async Task BuildImage()
    {
        if (!Directory.Exists(folder.Text)) throw new ArgumentException("Choose an existing build context folder.");
        if (dirtyDockerfile || !File.Exists(dockerfilePath)) { SaveDockerfile(); if (dirtyDockerfile || !File.Exists(dockerfilePath)) return; }
        var args = new List<string> { "buildx", "build", "--progress", "plain", "--load", "--file", dockerfilePath };
        AddOption(args, "--tag", tag.Text); AddOption(args, "--target", target.Text); AddOption(args, "--platform", platforms.Text);
        foreach (var value in buildArgs.Text.Split(['\r','\n'], StringSplitOptions.RemoveEmptyEntries)) { if (!value.Contains('=')) throw new ArgumentException("Build arguments use NAME=value, one per line."); args.AddRange(["--build-arg", value]); }
        if (noCache.IsChecked == true) args.Add("--no-cache"); if (pullBase.IsChecked == true) args.Add("--pull"); args.Add(Path.GetFullPath(folder.Text));
        settings.BuildFolder = folder.Text; settings.ImageTag = tag.Text; SaveSettings(); await Execute("Build image", args);
    }
    private static void AddOption(List<string> args, string option, string value) { if (!string.IsNullOrWhiteSpace(value)) { if (value.Trim().StartsWith('-')) throw new ArgumentException("Option values cannot begin with a hyphen."); args.Add(option); args.Add(value.Trim()); } }
    private (Grid Root, StackPanel Header, WrapPanel Actions, DataGrid Table, TextBlock Empty, SearchFilter Search) CollectionPage(string title, string subtitle)
    {
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = Page(title, subtitle); var actions = Actions(header); var filter = new SearchFilter(); header.Children.Add(filter); root.Children.Add(header);
        var table = new DataGrid { Margin = new Thickness(12, 0, 12, 12) }; Grid.SetRow(table, 1); root.Children.Add(table);
        var empty = new TextBlock { Text = "Refresh to load real engine records. No sample records are shown.", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 380, Opacity = .7 }; Grid.SetRow(empty, 1); root.Children.Add(empty);
        filter.Changed += () => { var view = CollectionViewSource.GetDefaultView(table.ItemsSource); if (view != null) { view.Filter = o => filter.Matches(o.ToString() ?? ""); view.Refresh(); } };
        return (root, header, actions, table, empty, filter);
    }
    private FrameworkElement ImagesPage()
    {
        var snapshotContext = Context; var snapshotGeneration = contextGeneration;
        async Task<CommandResult?> Run(string label, IReadOnlyList<string> args) { if(!SourceSafety.IsCurrentSnapshot(snapshotContext,snapshotGeneration,Context,contextGeneration)) { Notify("This resource snapshot is stale. Refresh it in the selected engine."); return null; } return await Execute(label,args,boundContext:snapshotContext); }

        var page = CollectionPage("Images", "Local image inventory for the selected engine. Select a row to act on its immutable ID.");
        async Task Refresh() { try { var rows = await docker.ListImagesAsync(snapshotContext); if(!SourceSafety.IsCurrentSnapshot(snapshotContext,snapshotGeneration,Context,contextGeneration)) return; page.Table.ItemsSource = rows; page.Empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed; page.Empty.Text = "No images in this engine."; } catch (Exception e) { Notify(e.Message); } }
        DockerImage? Selected() => page.Table.SelectedItem as DockerImage;
        page.Actions.Children.Add(AsyncButton("Refresh", Refresh));
        page.Actions.Children.Add(AsyncButton("Pull image", async () => { var value = Prompt("Pull image", "Image reference"); if (value != null) { await Run("Pull image", ["image","pull",value]); await Refresh(); } }));
        page.Actions.Children.Add(Button("Run", () => { if (Selected() is { } image) RunDialog(image.ID,snapshotContext,snapshotGeneration); else Notify("Select an image first."); }, true));
        page.Actions.Children.Add(AsyncButton("Tag", async () => { if (Selected() is { } image) { var value = Prompt("Tag image", "New image tag"); if (value != null) { await Run("Tag image", ["image","tag",image.ID,value]); await Refresh(); } } }));
        page.Actions.Children.Add(AsyncButton("Inspect", async () => { if (Selected() is { } image) await Inspect("image", image.ID,snapshotContext,snapshotGeneration); }));
        page.Actions.Children.Add(AsyncButton("Save archive", async () => { if (Selected() is { } image) { var d = new SaveFileDialog { Filter = "Tar archive|*.tar" }; if (d.ShowDialog(this) == true) await Run("Save image archive", ["image","save","--output",d.FileName,image.ID]); } }));
        page.Actions.Children.Add(AsyncButton("Load archive", async () => { var d = new OpenFileDialog { Filter = "Image archive|*.tar;*.tar.gz;*.tgz|All files|*.*" }; if (d.ShowDialog(this) == true) { await Run("Load image archive", ["image","load","--input",d.FileName]); await Refresh(); } }));
        page.Actions.Children.Add(AsyncButton("Remove", async () => { if (Selected() is { } image && Confirm("Remove image", image.ID)) { await Run("Remove image", ["image","rm",image.ID]); await Refresh(); } })); return page.Root;
    }
    private void RunDialog(string image, string? snapshotContext, long snapshotGeneration)
    {
        var p = Page("Run container", "Create a detached container from " + image + ". Ports bind to loopback by default. No privileged mode is enabled.");
        var name = new TextBox(); var port = new TextBox(); var network = new TextBox(); var mount = new TextBox(); var readOnly = new CheckBox { Content = "Read-only root filesystem" };
        Field(p,"Container name",name); Field(p,"Published port (host:container)",port); Field(p,"Network (optional)",network); Field(p,"Mount (source:destination:ro)",mount); p.Children.Add(readOnly);
        var d = Dialog("Run container", p);
        p.Children.Add(AsyncButton("Run", async () => { var args = new List<string> { "container","run","--detach" }; AddOption(args,"--name",name.Text); AddOption(args,"--network",network.Text); AddOption(args,"--volume",mount.Text); if (port.Text.Length > 0) { if (!System.Text.RegularExpressions.Regex.IsMatch(port.Text, @"^\d{1,5}:\d{1,5}(?:/(?:tcp|udp))?$")) throw new ArgumentException("Use host:container port numbers, optionally /tcp or /udp."); AddOption(args,"--publish","127.0.0.1:"+port.Text); } if (readOnly.IsChecked == true) args.Add("--read-only"); args.Add(image); if(!SourceSafety.IsCurrentSnapshot(snapshotContext,snapshotGeneration,Context,contextGeneration)) throw new InvalidOperationException("The engine changed. Close this dialog and refresh the image inventory."); var result = await Execute("Run container",args,boundContext:snapshotContext); if (result?.Success == true) d.Close(); }, true)); d.ShowDialog();
    }
    private FrameworkElement ContainersPage()
    {
        var snapshotContext = Context; var snapshotGeneration = contextGeneration;
        async Task<CommandResult?> Run(string label, IReadOnlyList<string> args) { if(!SourceSafety.IsCurrentSnapshot(snapshotContext,snapshotGeneration,Context,contextGeneration)) { Notify("This resource snapshot is stale. Refresh it in the selected engine."); return null; } return await Execute(label,args,boundContext:snapshotContext); }

        var page = CollectionPage("Containers", "Observed lifecycle and published ports. Actions affect only the selected container.");
        async Task Refresh() { try { var rows = await docker.ListContainersAsync(snapshotContext); if(!SourceSafety.IsCurrentSnapshot(snapshotContext,snapshotGeneration,Context,contextGeneration)) return; page.Table.ItemsSource = rows; page.Empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed; page.Empty.Text = "No containers in this engine."; } catch (Exception e) { Notify(e.Message); } }
        page.Actions.Children.Add(AsyncButton("Refresh",Refresh));
        foreach (var pair in new[] { ("Start","start"), ("Stop","stop"), ("Restart","restart"), ("Remove","rm") })
            page.Actions.Children.Add(AsyncButton(pair.Item1, async () => { if (page.Table.SelectedItem is DockerContainer c && (pair.Item2 == "start" || Confirm(pair.Item1 + " container",c.ID))) { await Run(pair.Item1 + " container",["container",pair.Item2,c.ID]); await Refresh(); } }));
        page.Actions.Children.Add(AsyncButton("Inspect", async () => { if (page.Table.SelectedItem is DockerContainer c) await Inspect("container",c.ID,snapshotContext,snapshotGeneration); }));
        page.Actions.Children.Add(AsyncButton("Logs", async () => { if (page.Table.SelectedItem is DockerContainer c) await Run("Container logs (last 500 lines)",["container","logs","--tail","500",c.ID]); }));
        page.Actions.Children.Add(AsyncButton("Statistics", async () => { if (page.Table.SelectedItem is DockerContainer c) await Run("Container statistics",["container","stats","--no-stream",c.ID]); })); return page.Root;
    }
    private async Task Inspect(string kind, string id, string? snapshotContext = null, long? snapshotGeneration = null)
    {
        if(snapshotGeneration.HasValue && !SourceSafety.IsCurrentSnapshot(snapshotContext,snapshotGeneration.Value,Context,contextGeneration)) { Notify("Stale resource snapshot. Refresh before inspection."); return; }
        var result = await Execute("Inspect " + kind,[kind,"inspect",id],false,snapshotContext);
        if (result?.Success == true) { var p = Page("Inspect", kind + " " + id); var source = new TextBox { Text = result.StandardOutput, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 380, FontFamily = new FontFamily("Consolas") }; p.Children.Add(source); Dialog("Inspect",p).ShowDialog(); }
    }
    private FrameworkElement ResourcesPage(string kind)
    {
        var snapshotContext = Context; var snapshotGeneration = contextGeneration;
        async Task<CommandResult?> Run(string label, IReadOnlyList<string> args) { if(!SourceSafety.IsCurrentSnapshot(snapshotContext,snapshotGeneration,Context,contextGeneration)) { Notify("This resource snapshot is stale. Refresh it in the selected engine."); return null; } return await Execute(label,args,boundContext:snapshotContext); }

        var page = CollectionPage(kind == "volume" ? "Volumes" : "Networks", "Create, inspect and remove resources on the selected engine. Removal never uses force.");
        async Task Refresh() { try { System.Collections.IEnumerable rows; if (kind == "volume") rows = await docker.ListVolumesAsync(snapshotContext); else rows = await docker.ListNetworksAsync(snapshotContext); if(!SourceSafety.IsCurrentSnapshot(snapshotContext,snapshotGeneration,Context,contextGeneration)) return; page.Table.ItemsSource = rows; page.Empty.Visibility = page.Table.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed; page.Empty.Text = "No resources reported."; } catch (Exception e) { Notify(e.Message); } }
        string? Selected() => page.Table.SelectedItem switch { DockerVolume v => v.Name, DockerNetwork n => n.ID, _ => null };
        page.Actions.Children.Add(AsyncButton("Refresh",Refresh)); page.Actions.Children.Add(AsyncButton("Create",async () => { var name = Prompt("Create " + kind,"Name"); if (name != null) { await Run("Create " + kind,[kind,"create",name]); await Refresh(); } },true));
        page.Actions.Children.Add(AsyncButton("Inspect",async () => { if (Selected() is { } id) await Inspect(kind,id,snapshotContext,snapshotGeneration); }));
        page.Actions.Children.Add(AsyncButton("Remove",async () => { if (Selected() is { } id && Confirm("Remove " + kind + (kind == "volume" ? " and its stored data" : ""),id)) { await Run("Remove " + kind,[kind,"rm",id]); await Refresh(); } })); return page.Root;
    }
    private FrameworkElement ComposePage()
    {
        var p = Page("Compose","Edit a loaded Compose file, save it explicitly, then run project-scoped actions. Changing a path never redirects a save. Down does not remove volumes."); Field(p,"Compose file",composeFile);
        void LoadSource(string path)
        {
            if (dirtyCompose && !Confirm("Discard unsaved Compose edits","current draft")) return;
            var resolved = Path.GetFullPath(path);
            if (!File.Exists(resolved) || new FileInfo(resolved).Length > 1024*1024) throw new ArgumentException("Choose an existing Compose file within the 1 MiB editor limit.");
            var content = File.ReadAllText(resolved);
            loadedComposePath = resolved; composeFile.Text = resolved; loading = true; composeSource.Text = content; loading = false; dirtyCompose = false;
            Notify("Compose source loaded from the exact selected file.");
        }
        var row = Actions(p);
        row.Children.Add(Button("Browse",() => { var d = new OpenFileDialog { Filter = "Compose YAML|*.yaml;*.yml|All files|*.*" }; if(d.ShowDialog(this)==true) LoadSource(d.FileName); }));
        row.Children.Add(Button("Load",() => LoadSource(composeFile.Text)));
        row.Children.Add(Button("Save",() => { var path = SourceSafety.RequireLoadedPath(loadedComposePath,composeFile.Text); File.WriteAllText(path,composeSource.Text); dirtyCompose=false; Notify("Compose file saved to its loaded path."); }));
        row.Children.Add(Button("Save As",() => { var d = new SaveFileDialog { Filter="Compose YAML|*.yaml;*.yml",FileName="compose.yaml",OverwritePrompt=true }; if(d.ShowDialog(this)!=true)return; if(!Confirm("Save Compose source to this exact destination",d.FileName))return; File.WriteAllText(d.FileName,composeSource.Text);loadedComposePath=Path.GetFullPath(d.FileName);composeFile.Text=loadedComposePath;dirtyCompose=false;Notify("Compose source saved to the reviewed destination."); }));
        foreach (var action in new[] { "config","up","down","ps","logs","build" }) row.Children.Add(AsyncButton(action,async () => { var path=SourceSafety.RequireLoadedPath(loadedComposePath,composeFile.Text); if(!File.Exists(path))throw new ArgumentException("The loaded Compose file is unavailable."); if (dirtyCompose) throw new InvalidOperationException("Save Compose edits before executing."); if (action == "down" && !Confirm("Stop and remove Compose project containers",path)) return; var args = new List<string> { "compose","--file",path,action }; if(action == "up") args.Add("--detach"); if(action == "logs") args.AddRange(["--tail","500"]); await Execute("Compose " + action,args); }));
        p.Children.Add(composeSource); return p;
    }
    private FrameworkElement DiagnosticsPage()
    {
        var p = Page("Diagnostics","Read-only checks use the selected engine. Results are actual process output with explicit exit status.");
        foreach(var item in new[] { ("Client and server version",new[]{"version"}), ("Engine information",new[]{"info"}), ("Disk usage",new[]{"system","df"}), ("Builder capabilities",new[]{"buildx","ls"}), ("Compose version",new[]{"compose","version"}), ("Engine health",new[]{"info","--format","{{.ServerVersion}}"}) }) p.Children.Add(AsyncButton(item.Item1,async()=> { var r = await Execute(item.Item1,item.Item2); EngineState.Text = r?.Success == true ? "Connected" : "Connection or capability check failed"; })); return p;
    }
    private FrameworkElement SettingsPage()
    {
        var p = Page("Settings","Preferences stay in this user's local application data. Theme and font size apply immediately; language applies after reopening the application.");
        var theme = new ComboBox { ItemsSource = new[]{"Dark","Light"}, SelectedItem = settings.Theme }; Field(p,"Theme",theme); theme.SelectionChanged += (_,_) => { settings.Theme = theme.SelectedItem?.ToString() ?? "Dark"; ApplyTheme(); SaveSettings(); };
        var language = new ComboBox { ItemsSource = new[]{"English","Cantonese","Bilingual"}, SelectedItem = settings.Language }; Field(p,"Language",language); language.SelectionChanged += (_,_) => { settings.Language = language.SelectedItem?.ToString() ?? "English"; SaveSettings(); Notify("Language saved. Reopen the application to refresh all labels."); };
        var size = new Slider { Minimum = 12, Maximum = 22, Value = settings.FontSize, TickFrequency = 1, IsSnapToTickEnabled = true }; Field(p,"Font size",size); size.ValueChanged += (_,_) => { FontSize = size.Value; settings.FontSize = size.Value; SaveSettings(); };
        var motion = new CheckBox { Content = T("Reduced motion"), IsChecked = settings.ReducedMotion }; motion.Checked += (_,_) => { settings.ReducedMotion = true; SaveSettings(); }; motion.Unchecked += (_,_) => { settings.ReducedMotion = false; SaveSettings(); }; p.Children.Add(motion);
        p.Children.Add(Button("Setup",()=>OpenPage("Setup")));
        p.Children.Add(new TextBlock { Text = "Implementation limits: full personal wording import, shared school mode, narration, full regex parity, advanced appearance editing and complete localization are not yet implemented. No conformance claim is made for these controls.", Margin = new Thickness(0,24,0,0), Opacity = .7 }); return p;
    }
    private FrameworkElement SetupPage()
    {
        var p = Page("Setup","Connect a Docker-compatible CLI and engine. Setup never installs or changes system services silently.");
        var state = new TextBlock { Margin = new Thickness(0,8,0,12), FontSize = 20 }; p.Children.Add(state);
        void Update() => state.Text = settings.SetupStep switch { 0 => "Step 1 of 3 · Detect Docker and connect to the engine", 1 => "Step 2 of 3 · Choose an engine context in the header", _ => "Step 3 of 3 · Open a build folder or use the image inventory" };
        Update();
        p.Children.Add(AsyncButton("Detect Docker",async()=> { var result = await Execute("Detect Docker",["version"]); if(result?.Success == true) { settings.SetupStep = Math.Max(1,settings.SetupStep); EngineState.Text = "Connected"; SaveSettings(); Update(); } else { EngineState.Text = "Unavailable"; Notify("Install or start your Docker engine, then retry detection. Setup is incomplete."); } },true));
        p.Children.Add(AsyncButton("Refresh contexts",RefreshContexts));
        var row = Actions(p); row.Children.Add(Button("Back",()=> { settings.SetupStep = Math.Max(0,settings.SetupStep-1); SaveSettings(); Update(); }));
        row.Children.Add(Button("Next",()=> { if(settings.SetupStep == 0) { Notify("Run successful Docker detection first."); return; } settings.SetupStep = Math.Min(2,settings.SetupStep+1); SaveSettings(); Update(); }));
        row.Children.Add(Button("Finish",()=> { if(settings.SetupStep < 2) { Notify("Complete the required detection and context steps first."); return; } settings.SetupComplete = true; SaveSettings(); OpenPage("Build"); }));
        p.Children.Add(Button("Explore without an engine",()=>OpenPage("Build"))); return p;
    }
    private void ApplyTheme()
    {
        var light = settings.Theme == "Light";
        foreach(var pair in new Dictionary<string,string> { ["Surface"] = light ? "#F5F7FC":"#101419", ["Panel"] = light ? "#FFFFFF":"#1A2027", ["Field"] = light ? "#E8EDF5":"#252C35", ["Ink"] = light ? "#17212E":"#E1E7EF", ["Muted"] = light ? "#4F6074":"#AAB7C8", ["Outline"] = light ? "#BCC8D8":"#455365" }) Resources[pair.Key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Value));
    }
    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (operation != null) { Notify("Cancel the active operation and wait for its result before closing."); e.Cancel = true; return; }
        if ((dirtyDockerfile || dirtyCompose) && !Confirm("Discard unsaved source edits", "Dockerfile or Compose draft")) e.Cancel = true;
        if(!e.Cancel) SaveSettings();
    }
}
