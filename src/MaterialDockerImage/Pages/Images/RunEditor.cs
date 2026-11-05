using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Wide overlay that creates a container from a local image. Every field maps to a real docker container run flag; the
/// preview shows exactly the argument list that runs (environment values masked). Environment values are never written
/// to local history. The editor closes only after the engine reports success.
/// </summary>
internal sealed partial class RunEditor
{
    private static AppHost Host => AppHost.Current;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private readonly string image;
    private readonly string? context;
    private readonly long generation;
    private readonly Func<Task>? afterSuccess;
    private Action? close;

    // Identity
    private readonly TextBox name = Ui.Input("", "Container name");
    private readonly TextBox hostname = Ui.Input("", "Hostname");
    // Command
    private readonly TextBox argv = Ui.Input("", "Command argv JSON", true);
    private readonly TextBox entrypoint = Ui.Input("", "Entrypoint", true);
    private readonly TextBox user = Ui.Input("", "User");
    private readonly TextBox workdir = Ui.Input("", "Working directory", true);
    private readonly CheckBox detach = Ui.Switch("Detach", true, _ => { }, "Run in the background (--detach) and return the container ID.");
    private readonly CheckBox autoRemove = Ui.Switch("Remove when it exits", false, _ => { }, "--rm deletes the container and its anonymous volumes when it stops.");
    private readonly CheckBox init = Ui.Switch("Init process", false, _ => { }, "--init runs a tiny init as PID 1 that forwards signals and reaps zombies.");
    private readonly CheckBox interactive = Ui.Switch("Keep STDIN open", false, _ => { }, "--interactive. Only useful with a TTY and a later attach.");
    private readonly CheckBox tty = Ui.Switch("Allocate a TTY", false, _ => { }, "--tty. Requires Detach in this app; attach later from Containers.");
    // Ports
    private readonly StackPanel portList = new();
    private readonly List<PortRow> ports = new();
    private readonly Border portWarning;
    // Environment
    private readonly StackPanel envList = new();
    private readonly List<EnvRow> env = new();
    private readonly TextBox envFile = Ui.Input("", "Environment file", true);
    private readonly Border envWarning;
    // Mounts
    private readonly StackPanel mountList = new();
    private readonly List<MountRow> mounts = new();
    private readonly StackPanel tmpfsList = new();
    private readonly List<TmpfsRow> tmpfs = new();
    // Network
    private readonly ComboBox network = Ui.Combo(["(engine default)"]);
    private readonly TextBox alias = Ui.Input("", "Network aliases");
    // Resources
    private readonly TextBox cpus = Ui.Input("", "CPU limit");
    private readonly TextBox memory = Ui.Input("", "Memory limit");
    private readonly TextBox memoryReservation = Ui.Input("", "Memory reservation");
    private readonly TextBox pids = Ui.Input("", "Process limit");
    private readonly TextBox gpus = Ui.Input("", "GPUs", true);
    private readonly TextBox gpuCapabilities = Ui.Input("", "GPU capabilities", true);
    private readonly TextBox cgroupRules = Ui.CodeBox("", 60);
    private readonly StackPanel deviceList = new();
    private readonly List<DeviceRow> devices = new();
    private readonly Border hardwareWarning;
    // Restart and health
    private readonly ComboBox restart = Ui.Combo(["no", "always", "unless-stopped", "on-failure"], "no");
    private readonly TextBox restartRetries = Ui.Input("", "Maximum retries");
    private readonly TextBox healthCmd = Ui.Input("", "Health command", true);
    private readonly TextBox healthInterval = Ui.Input("", "Interval");
    private readonly TextBox healthRetries = Ui.Input("", "Retries");
    private readonly TextBox healthTimeout = Ui.Input("", "Timeout");
    private readonly TextBox healthStart = Ui.Input("", "Start period");
    private readonly TextBox healthStartInterval = Ui.Input("", "Start interval");
    private readonly CheckBox noHealthcheck = Ui.Switch("Disable the image health check", false, _ => { }, "--no-healthcheck turns off any HEALTHCHECK defined in the image.");
    // Security
    private readonly CheckBox readOnlyRoot = Ui.Switch("Read-only root filesystem", false, _ => { }, "--read-only. Writes go only to mounts and tmpfs.");
    private readonly CheckBox noNewPrivileges = Ui.Switch("No new privileges", true, _ => { }, "--security-opt no-new-privileges:true. Processes cannot gain privileges through setuid binaries.");
    private readonly CheckBox capDropAll = Ui.Switch("Drop all capabilities", false, _ => { }, "--cap-drop ALL. Add back only what the process needs below.");
    private readonly TextBox capAdd = Ui.Input("", "Capabilities to add", true);
    // Advanced
    private readonly TextBox labels = Ui.CodeBox("", 70);
    private readonly ComboBox logDriver = Ui.Combo(["(engine default)", "json-file", "local", "none", "syslog", "journald", "gelf", "fluentd", "awslogs", "splunk", "etwlogs", "gcplogs"], "(engine default)", true);
    private readonly TextBox logOptions = Ui.CodeBox("", 60);
    private readonly TextBox platform = Ui.Input("", "Platform", true);
    private readonly ComboBox pull = Ui.Combo(["missing", "always", "never"], "missing");

    private readonly TextBox preview = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 96, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(0) };
    private readonly TextBlock problem = Ui.Text("", 13, null, "Error", true, false);
    private readonly TextBlock resultText = Ui.Text("", 13, null, "OnSurfaceVariant", true, false);
    private readonly TextBlock imageFacts = Ui.Caption("Reading image details…");
    private readonly Button runButton;
    private readonly Dictionary<string, TextBlock> navCounts = new();
    private readonly Dictionary<string, FrameworkElement> sections = new();
    private IReadOnlyList<string>? lastArgs;
    private string[] exposedPorts = [];

    private sealed record PortRow(Grid View, TextBox Address, TextBox Host, TextBox Container, ComboBox Protocol);
    private sealed record EnvRow(Grid View, TextBox Name, TextBox Value);
    private sealed record MountRow(Grid View, ComboBox Type, TextBox Source, TextBox Target, CheckBox ReadOnly, Button Browse);
    private sealed record TmpfsRow(Grid View, TextBox Path, TextBox Options);
    private sealed record DeviceRow(Grid View, TextBox Host, TextBox Container, ComboBox Permissions);

    public static void Show(string image, string? context, long generation, Func<Task>? afterSuccess = null)
    {
        DockerService.ValidateOperand(image, "image");
        var editor = new RunEditor(image, context, generation, afterSuccess);
        editor.Open();
    }

    private RunEditor(string image, string? context, long generation, Func<Task>? afterSuccess)
    {
        this.image = image; this.context = context; this.generation = generation; this.afterSuccess = afterSuccess;
        preview.SetResourceReference(Control.FontFamilyProperty, "MonoFont");
        preview.SetResourceReference(Control.BackgroundProperty, "SurfaceLowest");
        preview.FontSize = 12.5;
        System.Windows.Automation.AutomationProperties.SetName(preview, Loc.T("Command preview"));
        portWarning = Warning("Publishing to 0.0.0.0 or :: makes the port reachable from other machines on your network and can bypass Windows Firewall rules. Keep 127.0.0.1 unless another device needs access.");
        envWarning = Warning("A variable name looks like a secret. Environment values are stored in the container configuration and anyone with access to this engine can read them with docker inspect. Prefer an environment file or a secret store.");
        hardwareWarning = Warning("GPU and device access give the container direct access to host hardware. Only grant it to images you trust. GPUs require driver support on the engine host (for example the NVIDIA Container Toolkit or WSL GPU support).");
        runButton = Ui.AsyncButton("Run", Icons.PlayArrow, ButtonKind.Filled, RunAsync);
    }

    private static Border Warning(string text)
    {
        var row = Ui.Columns(10, [0, 1], Ui.Icon(Icons.Warning, 18, true, "OnWarningContainer"), Ui.Text(text, 13, null, "OnWarningContainer"));
        var card = Ui.Card(row, 12, "WarningContainer", 12);
        card.Visibility = Visibility.Collapsed;
        return card;
    }

    private void Open()
    {
        var head = Ui.Columns(8, [1, 0],
            Ui.VStack(2, Ui.Text(Loc.T("Run container from") + " " + image, 24, FontWeights.Normal, "OnSurface", true, false), imageFacts),
            Ui.IconButton(Icons.Close, "Close", () => close?.Invoke()));

        var form = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        AddSection(form, "Identity", Icons.Label, BuildIdentity());
        AddSection(form, "Command", Icons.Terminal, BuildCommand());
        AddSection(form, "Ports", Icons.Lan, BuildPorts());
        AddSection(form, "Environment", Icons.TextFields, BuildEnvironment());
        AddSection(form, "Mounts", Icons.FolderOpen, BuildMounts());
        AddSection(form, "Network", Icons.Hub, BuildNetwork());
        AddSection(form, "Resources", Icons.Memory, BuildResources());
        AddSection(form, "Restart and health", Icons.RestartAlt, BuildRestart());
        AddSection(form, "Security", Icons.Shield, BuildSecurity());
        AddSection(form, "Advanced", Icons.Tune, BuildAdvanced());
        var formScroll = Ui.Scroll(form);

        var nav = new ListBox { Width = 232, Margin = new Thickness(0, 0, 16, 0) };
        System.Windows.Automation.AutomationProperties.SetName(nav, Loc.T("Sections"));
        foreach (var key in sections.Keys)
        {
            var count = Ui.Text("", 12, FontWeights.SemiBold, "OnSurfaceVariant", false, false);
            navCounts[key] = count;
            var item = new ListBoxItem { Content = Ui.Columns(6, [1, 0], Ui.Text(key, 14, null, "OnSurface", false), count), Tag = key };
            System.Windows.Automation.AutomationProperties.SetName(item, Loc.T(key));
            nav.Items.Add(item);
        }
        nav.SelectionChanged += (_, _) =>
        {
            if (nav.SelectedItem is not ListBoxItem { Tag: string key } || !sections.TryGetValue(key, out var target) || !target.IsVisible) return;
            var top = target.TransformToAncestor(form).Transform(new Point(0, 0)).Y;
            formScroll.ScrollToVerticalOffset(top);
        };

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(nav);
        body.Children.Add(formScroll.At(0, 1));

        var copy = Ui.IconButton(Icons.ContentCopy, "Copy command", () => PageKit.Copy(preview.Text, "the command (environment values masked)"));
        var previewCard = Ui.Card(Ui.Columns(8, [0, 1, 0], Ui.Icon(Icons.Terminal, 18), preview, copy), 10, "SurfaceLowest", 12);
        var actions = Ui.HStack(8, Ui.Button("Cancel", null, ButtonKind.Text, () => close?.Invoke()), runButton);
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        var footer = Ui.VStack(8, previewCard, problem, Ui.Columns(12, [1, 0], resultText, actions));

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 160 });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(head.Margin(0, 0, 0, 16));
        root.Children.Add(body.At(1));
        root.Children.Add(footer.At(2).Margin(0, 16, 0, 0));
        // No fixed height: the long form asks for all available space, so the card grows to the overlay's maximum height and the form scrolls.

        root.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, e) => { if (e.OriginalSource != preview) Update(); }));
        root.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((_, _) => Update()));
        root.AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler((_, _) => Update()));
        root.AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, e) => { if (e.OriginalSource != nav) Update(); }));

        var shell = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        close = Host.Overlay.Show(shell, 1140);
        Update();
        _ = LoadDetailsAsync();
    }

    private void AddSection(StackPanel form, string title, string icon, UIElement content)
    {
        var heading = Ui.Columns(10, [0, 1], Ui.Icon(icon, 20, false, "Primary"), Ui.Text(title, 16, FontWeights.SemiBold));
        var card = Ui.Card(Ui.VStack(12, heading, content), 16, "SurfaceLow", 16);
        card.Margin = new Thickness(0, 0, 0, 12);
        sections[title] = card;
        form.Children.Add(card);
    }

    private static Grid Two(UIElement a, UIElement b) => Ui.Columns(12, [1, 1], a, b);

    private UIElement BuildIdentity() => Ui.VStack(12,
        Ui.Field("Image", Ui.Mono(image, 13, "OnSurface", true), "The image this container is created from. Change it by choosing a different image in the Images list."),
        Two(Ui.Field("Container name", name, "Optional. Docker generates a random name when empty. Letters, digits, underscores, periods and hyphens.", "hello-api"),
            Ui.Field("Hostname", hostname, "--hostname sets the host name the container sees for itself. Leave empty for the short container ID.", "api-1")));

    private UIElement BuildCommand() => Ui.VStack(12,
        Ui.Field("Command argv JSON", argv, "Overrides the image CMD. An explicit JSON array of strings passed directly to Docker, never to a host shell. Leave empty to keep the image command.", "[\"node\", \"dist/server.js\", \"--port\", \"8080\"]"),
        Two(Ui.Field("Entrypoint", entrypoint, "--entrypoint replaces the image ENTRYPOINT with a single executable. Arguments belong in the command above.", "/usr/local/bin/node"),
            Ui.Field("User", user, "--user runs the process as this user name or UID[:GID] inside the container.", "1000:1000")),
        Ui.Field("Working directory", workdir, "--workdir sets the directory inside the container where the process starts.", "/app"),
        Two(Ui.VStack(8, detach, autoRemove, init), Ui.VStack(8, interactive, tty)),
        Ui.Caption("Without Detach the operation stays active and streams the container output until the process exits. Canceling stops following the output; the container may keep running."));

    private UIElement BuildPorts()
    {
        var add = Ui.Button("Add port", Icons.Add, ButtonKind.Text, () => { AddPort("127.0.0.1", "", "", "tcp"); Update(); });
        var exposed = Ui.Button("Add exposed ports", Icons.Add, ButtonKind.Text, () =>
        {
            foreach (var p in exposedPorts)
            {
                var parts = p.Split('/');
                if (ports.Any(r => r.Container.Text.Trim() == parts[0])) continue;
                AddPort("127.0.0.1", parts[0], parts[0], parts.Length > 1 ? parts[1] : "tcp");
            }
            if (exposedPorts.Length == 0) Host.Notify.Info("This image does not declare exposed ports.");
            Update();
        });
        var header = Ui.Columns(8, [1.4, 1, 1, -90, -40], Ui.Overline("Bind address"), Ui.Overline("Host port"), Ui.Overline("Container port"), Ui.Overline("Protocol"), new Border());
        return Ui.VStack(8,
            Ui.Columns(4, [1, 0], Ui.Caption("Each mapping has its own bind address. Bindings default to loopback (127.0.0.1) so only this computer can reach them."), Ui.InfoButton("Published ports", "--publish address:hostPort:containerPort/protocol. The bind address chooses which host network interfaces accept connections. 0.0.0.0 (IPv4) or :: (IPv6) means every interface.", "127.0.0.1:8080:8080/tcp")),
            header, portList, Ui.HStack(8, add, exposed), portWarning);
    }

    private void AddPort(string address, string host, string container, string protocol)
    {
        var a = Ui.Input(address, "Bind address", true); var h = Ui.Input(host, "Host port"); var c = Ui.Input(container, "Container port");
        var p = Ui.Combo(["tcp", "udp"], protocol); System.Windows.Automation.AutomationProperties.SetName(p, Loc.T("Protocol"));
        PortRow? row = null;
        var remove = Ui.IconButton(Icons.Delete, "Remove port", () => { ports.Remove(row!); portList.Children.Remove(row!.View); Update(); });
        var grid = Ui.Columns(8, [1.4, 1, 1, -90, -40], a, h, c, p, remove);
        grid.Margin = new Thickness(0, 0, 0, 6);
        row = new PortRow(grid, a, h, c, p);
        ports.Add(row); portList.Children.Add(grid);
    }

    private UIElement BuildEnvironment()
    {
        var add = Ui.Button("Add variable", Icons.Add, ButtonKind.Text, () => { AddEnv("", ""); Update(); });
        var browse = Ui.Button("Choose file", Icons.FolderOpen, ButtonKind.Outlined, () =>
        {
            var path = PageKit.PickOpenPath("Choose an environment file", "Environment files|*.env;.env;*.txt|All files|*.*");
            if (path != null) envFile.Text = path;
        });
        return Ui.VStack(8,
            Ui.Caption("Values are passed as --env NAME=value. They are shown masked in the preview and never written to local history. Do not enter credentials: anyone with access to this engine can read them with docker inspect."),
            Ui.Columns(8, [1, 1.6, -40], Ui.Overline("Name"), Ui.Overline("Value"), new Border()),
            envList, add, envWarning,
            Ui.Field("Environment file", Ui.Columns(8, [1, 0], envFile, browse), "--env-file reads NAME=value lines from a file on this computer. Only the path is passed; this app does not read or store the file contents.", @"C:\Projects\hello-api\.env"));
    }

    private void AddEnv(string key, string value)
    {
        var k = Ui.Input(key, "Variable name", true); var v = Ui.Input(value, "Variable value", true);
        EnvRow? row = null;
        var remove = Ui.IconButton(Icons.Delete, "Remove variable", () => { env.Remove(row!); envList.Children.Remove(row!.View); Update(); });
        var grid = Ui.Columns(8, [1, 1.6, -40], k, v, remove);
        grid.Margin = new Thickness(0, 0, 0, 6);
        row = new EnvRow(grid, k, v);
        env.Add(row); envList.Children.Add(grid);
    }

    private UIElement BuildMounts()
    {
        var add = Ui.Button("Add mount", Icons.Add, ButtonKind.Text, () => { AddMount(); Update(); });
        var addTmpfs = Ui.Button("Add tmpfs", Icons.Add, ButtonKind.Text, () => { AddTmpfs(); Update(); });
        return Ui.VStack(8,
            Ui.Caption("Bind sources are paths on the engine host, which may be remote; Browse picks a local folder only, so confirm the engine can reach it. Mounts are read-only by default; turn Read-only off only when the container must write."),
            Ui.Columns(8, [-110, 1.4, 1, -120, -40], Ui.Overline("Type"), Ui.Overline("Source (host path or volume)"), Ui.Overline("Container path"), Ui.Overline("Access"), new Border()),
            mountList, add,
            Ui.Columns(4, [1, 0], Ui.Overline("tmpfs mounts"), Ui.InfoButton("tmpfs", "--tmpfs mounts a memory-backed file system at an absolute container path. Options are comma-separated, such as size and mode. Contents disappear when the container stops.", "/tmp  size=64m,mode=1777")),
            tmpfsList, addTmpfs);
    }

    private void AddMount()
    {
        var type = Ui.Combo(["bind", "volume"], "bind"); System.Windows.Automation.AutomationProperties.SetName(type, Loc.T("Mount type"));
        var source = Ui.Input("", "Mount source", true); var target = Ui.Input("", "Container path", true);
        var ro = Ui.Check("Read-only", true);
        Button browse = null!;
        browse = Ui.IconButton(Icons.FolderOpen, "Browse local source folder", () =>
        {
            var picker = new OpenFolderDialog { Title = Loc.T("Choose a local bind source; verify it is accessible to this engine") };
            if (picker.ShowDialog() == true) source.Text = picker.FolderName;
        });
        type.SelectionChanged += (_, _) => browse.IsEnabled = type.SelectedItem as string == "bind";
        MountRow? row = null;
        var remove = Ui.IconButton(Icons.Delete, "Remove mount", () => { mounts.Remove(row!); mountList.Children.Remove(row!.View); Update(); });
        var grid = Ui.Columns(8, [-110, 1.4, 1, -120, -40], type, Ui.Columns(4, [1, 0], source, browse), target, ro, remove);
        grid.Margin = new Thickness(0, 0, 0, 6);
        row = new MountRow(grid, type, source, target, ro, browse);
        mounts.Add(row); mountList.Children.Add(grid);
    }

    private void AddTmpfs()
    {
        var path = Ui.Input("/tmp", "tmpfs path", true); var options = Ui.Input("", "tmpfs options", true);
        TmpfsRow? row = null;
        var remove = Ui.IconButton(Icons.Delete, "Remove tmpfs", () => { tmpfs.Remove(row!); tmpfsList.Children.Remove(row!.View); Update(); });
        var grid = Ui.Columns(8, [1, 1, -40], path, options, remove);
        grid.Margin = new Thickness(0, 0, 0, 6);
        row = new TmpfsRow(grid, path, options);
        tmpfs.Add(row); tmpfsList.Children.Add(grid);
    }

    private UIElement BuildNetwork() => Ui.VStack(12,
        Two(Ui.Field("Network", network, "--network attaches the container to an existing network on this engine. The list comes from docker network ls.", "hello-net"),
            Ui.Field("Network aliases", alias, "--network-alias adds extra DNS names on a user-defined network. Separate several with spaces or commas.", "api api-v1")),
        Ui.Caption("Host networking and privileged mode are not offered by this editor."));

    private UIElement BuildResources()
    {
        var addDevice = Ui.Button("Add device", Icons.Add, ButtonKind.Text, () => { AddDevice(); Update(); });
        return Ui.VStack(12,
            Two(Ui.Field("CPU limit", cpus, "--cpus limits how many CPU cores the container may use, as a decimal number.", "1.5"),
                Ui.Field("Memory limit", memory, "--memory is a hard limit. Use bytes or b/k/m/g units. The kernel stops the process if it exceeds the limit.", "512m")),
            Two(Ui.Field("Memory reservation", memoryReservation, "--memory-reservation is a soft limit applied when the host is short of memory. It must be below the memory limit.", "256m"),
                Ui.Field("Process limit", pids, "--pids-limit caps how many processes and threads the container can create.", "200")),
            Two(Ui.Field("GPUs", gpus, "--gpus requests GPU access: all, a count, or device IDs. Requires GPU support on the engine host; an engine without it refuses to start the container.", "all   or   2   or   device=0,1"),
                Ui.Field("GPU capabilities", gpuCapabilities, "Optional capabilities for the GPU request, separated by spaces or commas. Leave empty for the driver default.", "compute utility")),
            Ui.Columns(4, [1, 0], Ui.Overline("Devices"), Ui.InfoButton("Devices", "--device maps a host device into the container with read (r), write (w) and mknod (m) permissions. Device paths belong to the engine host.", "/dev/ttyUSB0:/dev/ttyUSB0:rw")),
            deviceList, addDevice,
            Ui.Field("Device cgroup rules", cgroupRules, "--device-cgroup-rule allows access to devices by type and major:minor numbers, one rule per line, for example c 189:* rwm. Rules apply to devices that appear later, such as hot-plugged USB.", "c 189:* rwm"),
            hardwareWarning);
    }

    private void AddDevice()
    {
        var host = Ui.Input("", "Host device path", true); var container = Ui.Input("", "Container device path", true);
        var perms = Ui.Combo(["rwm", "rw", "rm", "wm", "r", "w", "m"], "rwm", true);System.Windows.Automation.AutomationProperties.SetName(perms, Loc.T("Device permissions"));
        DeviceRow? row = null;
        var remove = Ui.IconButton(Icons.Delete, "Remove device", () => { devices.Remove(row!); deviceList.Children.Remove(row!.View); Update(); });
        var grid = Ui.Columns(8, [1, 1, -90, -40], host, container, perms, remove);
        grid.Margin = new Thickness(0, 0, 0, 6);
        row = new DeviceRow(grid, host, container, perms);
        devices.Add(row); deviceList.Children.Add(grid);
    }

    private UIElement BuildRestart() => Ui.VStack(12,
        Two(Ui.Field("Restart policy", restart, "--restart decides whether the engine restarts the container after it exits or after the engine restarts.", "unless-stopped"),
            Ui.Field("Maximum retries", restartRetries, "Only for on-failure: stop retrying after this many restarts. Leave empty for no limit.", "5")),
        Ui.Field("Health command", healthCmd, "--health-cmd runs inside the container through its shell (CMD-SHELL). Exit code 0 means healthy.", "curl -fsS http://localhost:8080/health || exit 1"),
        Ui.Columns(12, [1, 1, 1],
            Ui.Field("Interval", healthInterval, "--health-interval: time between checks, as a Go duration such as 30s or 1m30s.", "30s"),
            Ui.Field("Retries", healthRetries, "--health-retries: consecutive failures before the container is unhealthy. A positive whole number.", "3"),
            Ui.Field("Timeout", healthTimeout, "--health-timeout: maximum time one check may take.", "5s")),
        Ui.Columns(12, [1, 1],
            Ui.Field("Start period", healthStart, "--health-start-period: initial grace period during which failures do not count.", "1m"),
            Ui.Field("Start interval", healthStartInterval, "--health-start-interval: time between checks during the start period. Needs Docker Engine 25 or later.", "2s")),
        noHealthcheck);

    private UIElement BuildSecurity() => Ui.VStack(10,
        noNewPrivileges, readOnlyRoot, capDropAll,
        Ui.Field("Capabilities to add", capAdd, "--cap-add grants Linux capabilities. Separate several with spaces or commas. Combine with Drop all capabilities to grant only what is needed.", "NET_BIND_SERVICE CHOWN"));

    private UIElement BuildAdvanced() => Ui.VStack(12,
        Ui.Field("Labels", labels, "--label metadata, one key=value per line.", "com.example.team=payments"),
        Two(Ui.Field("Log driver", logDriver, "--log-driver chooses where container output goes. Leave on engine default unless you need a different driver.", "local"),
            Ui.Field("Pull policy", pull, "--pull: missing pulls only when the image is not local (default), always checks the registry, never uses only the local image.", "missing")),
        Ui.Field("Log options", logOptions, "--log-opt key=value pairs for the log driver, one per line.", "max-size=10m\nmax-file=3"),
        Ui.Field("Platform", platform, "--platform selects the image variant when an image supports several, as os/arch[/variant].", "linux/amd64"));

    private async Task LoadDetailsAsync()
    {
        try
        {
            var inspect = await Host.Read(["image", "inspect", image], context);
            if (inspect.Success && PageKit.FirstInspect(inspect.StandardOutput) is { } info)
            {
                var id = PageKit.Field(info, "Id");
                exposedPorts = PageKit.Child(info, "Config", "ExposedPorts") is { ValueKind: System.Text.Json.JsonValueKind.Object } exp ? exp.EnumerateObject().Select(p => p.Name).ToArray() : [];
                var imageUser = PageKit.Field(info, "Config", "User");
                var os = PageKit.Field(info, "Os"); var arch = PageKit.Field(info, "Architecture");
                imageFacts.Text = $"{(id.Length > 19 ? id[..19] : id)} · {os}/{arch} · " + (exposedPorts.Length > 0 ? "exposes " + string.Join(", ", exposedPorts) : "no exposed ports") + " · runs as " + (imageUser.Length > 0 ? imageUser : "root (image default)") + " · context " + (context ?? "default");
            }
            else imageFacts.Text = Loc.T("Image details could not be read:") + " " + inspect.StandardError.Trim();
            var nets = await Host.ReadRows(["network", "ls", "--format", "{{json .}}"], context);
            var names = new List<string> { "(engine default)" };
            names.AddRange(nets.Select(n => DockerJson.Text(n, "Name")).Where(n => n.Length > 0).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
            network.ItemsSource = names; network.SelectedIndex = 0;
        }
        catch (Exception e) when (e is InvalidOperationException or System.Text.Json.JsonException)
        {
            Host.Notify.Warning(Loc.T("Network list could not be read:") + " " + e.Message);
        }
    }
}
