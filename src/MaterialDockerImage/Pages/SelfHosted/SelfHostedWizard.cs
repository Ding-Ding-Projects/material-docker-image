using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Guided setup of one catalogue app: options (project, ports, storage, database, time zone, administrator), the
/// generated secrets (shown only here), a review of the exact compose.yaml, then deployment with a health wait and
/// the addresses to open it from.
/// </summary>
internal sealed class SelfHostedWizard : Grid
{
    private static AppHost Host => AppHost.Current;
    private static readonly string[] Steps = ["Options", "Secrets", "Review", "Deploy"];
    private readonly SelfHostedApp app;
    private readonly Func<string?> context;
    private readonly Func<bool> ensureCurrent;
    private readonly Action<string?> finished;
    private readonly bool capture;
    private readonly SelfHostedOptions options;
    private readonly WrapPanel stepBar = new();
    private readonly ContentControl body = new();
    private readonly TextBlock stepError = Ui.Text("", 13, null, "Error", true, false);
    private readonly Button back, next, deploy, cancel;
    private List<string> existing = [];
    private HashSet<int> inUse = [];
    private bool engineLocal = true;
    private string engineHost = "localhost";
    private List<(string Address, string Adapter)> lan = [];
    private bool portsChecked;
    private int step;
    private bool deploying;
    private CancellationTokenSource? healthCancel;
    private Action? refreshOptions;

    public SelfHostedApp App => app;

    public SelfHostedWizard(SelfHostedApp app, Func<string?> context, Func<bool> ensureCurrent, Action<string?> finished, bool capture = false)
    {
        this.app = app; this.context = context; this.ensureCurrent = ensureCurrent; this.finished = finished; this.capture = capture;
        options = SelfHostedRules.Defaults(app, LocalProjectFolders(), SystemTimeZone());
        SelfHostedRules.GenerateSecrets(app, options);
        if (!capture)
        {
            lan = SelfHostedRules.LanAddresses();
            options.PublicHost = lan.Count > 0 ? lan[0].Address : "localhost";
            options.TrustedHosts = lan.Select(l => l.Address).ToList();
        }
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        stepBar.Margin = new Thickness(0, 0, 0, 12);
        System.Windows.Automation.AutomationProperties.SetName(stepBar, Loc.T("Steps"));
        var title = Ui.HStack(12, Ui.Icon(SelfHostedCatalogueView.CategoryIcon(app.Category), 22, true, "Primary"), Ui.Text(Loc.T("Set up") + " " + app.Name, 18, FontWeights.SemiBold, "OnSurface", true, false));
        Children.Add(Ui.VStack(8, title, stepBar));
        Children.Add(Ui.Scroll(body).At(1));
        back = Ui.Button("Back", Icons.ArrowBack, ButtonKind.Text, () => Go(step - 1));
        next = Ui.Button("Next", Icons.ArrowForward, ButtonKind.Filled, () => { if (CheckStep()) Go(step + 1); });
        deploy = Ui.AsyncButton("Deploy", Icons.RocketLaunch, ButtonKind.Filled, DeployAsync);
        cancel = Ui.Button("Cancel", null, ButtonKind.Text, () => { healthCancel?.Cancel(); finished(null); });
        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        footer.Children.Add(Ui.HStack(8, back, next, deploy).Dock(Dock.Right));
        footer.Children.Add(cancel.Dock(Dock.Left));
        footer.Children.Add(stepError.Margin(12, 0, 12, 0));
        Children.Add(footer.At(2));
        Go(0);
        if (!capture) _ = LoadEngineFactsAsync();
    }

    private static string SystemTimeZone()
    {
        var id = TimeZoneInfo.Local.Id;
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana)) return iana;
        return id.Contains('/') ? id : "Etc/UTC";
    }

    private static List<string> LocalProjectFolders()
    {
        try { return Directory.Exists(SelfHostedOps.Root) ? Directory.GetDirectories(SelfHostedOps.Root).Select(d => Path.GetFileName(d)).ToList() : []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    private IEnumerable<string> Taken => existing.Concat(LocalProjectFolders());

    /// <summary>Reads the engine's Compose projects, published ports and location (for ports in use and the URL host).</summary>
    private async Task LoadEngineFactsAsync()
    {
        var ctx = context();
        (engineLocal, engineHost) = await SelfHostedOps.EngineHostAsync(ctx);
        existing = await SelfHostedOps.ExistingProjectsAsync(ctx);
        inUse = await SelfHostedOps.UsedPortsAsync(ctx, engineLocal);
        portsChecked = true;
        if (!engineLocal) { options.PublicHost = engineHost; options.TrustedHosts = [engineHost]; }
        if (SelfHostedRules.ValidateProject(options.Project, Taken) != null)
        {
            var fresh = SelfHostedRules.Defaults(app, Taken, options.TimeZone);
            options.Project = fresh.Project;
        }
        foreach (var port in app.Ports)
        {
            // Move a default host port that is taken to the next free one, so the defaults deploy without editing.
            var p = options.Port(app, port.Key);
            while (inUse.Contains(p) || options.HostPorts.Any(h => h.Key != port.Key && h.Value == p)) p++;
            if (p <= 65535) options.HostPorts[port.Key] = p;
        }
        if (step == 0) refreshOptions?.Invoke();
    }

    private void Go(int target)
    {
        step = Math.Clamp(target, 0, Steps.Length - 1);
        stepError.Text = "";
        stepBar.Children.Clear();
        for (var i = 0; i < Steps.Length; i++)
        {
            var index = i;
            var done = i < step;
            var dot = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Child = done ? Ui.Icon(Icons.Check, 16, true, "OnPrimary") : Ui.Text((i + 1).ToString(CultureInfo.InvariantCulture), 13, FontWeights.SemiBold, i == step ? "OnPrimary" : "OnSurfaceVariant", false, false) };
            if (dot.Child is FrameworkElement fe) fe.HorizontalAlignment = HorizontalAlignment.Center;
            dot.SetResourceReference(Border.BackgroundProperty, i <= step ? "Primary" : "SurfaceHighest");
            var label = Ui.Text(Steps[i], 13, i == step ? FontWeights.SemiBold : FontWeights.Normal, i == step ? "OnSurface" : "OnSurfaceVariant", false);
            var item = new Button { Style = (Style)Application.Current.FindResource("Btn.Text"), Content = Ui.HStack(8, dot, label), Margin = new Thickness(0, 0, 4, 4), IsEnabled = i <= step && i < 3 && !deploying };
            System.Windows.Automation.AutomationProperties.SetName(item, $"Step {i + 1}: {Loc.T(Steps[i])}" + (i == step ? " (current)" : ""));
            item.Click += (_, _) => Go(index);
            stepBar.Children.Add(item);
        }
        back.IsEnabled = step is > 0 and < 3;
        back.Visibility = step < 3 ? Visibility.Visible : Visibility.Collapsed;
        next.Visibility = step < 2 ? Visibility.Visible : Visibility.Collapsed;
        deploy.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        refreshOptions = null;
        body.Content = step switch
        {
            0 => OptionsStep(),
            1 => SecretsStep(),
            2 => ReviewStep(),
            _ => body.Content,
        };
    }

    private List<string> Problems() => SelfHostedRules.Validate(app, options, Taken, inUse);

    private bool CheckStep()
    {
        if (step == 0)
        {
            var problems = Problems().Where(p => !p.StartsWith("Secrets:", StringComparison.Ordinal)).ToList();
            stepError.Text = string.Join("  ", problems.Select(Loc.T));
            return problems.Count == 0;
        }
        return true;
    }

    // ------------------------------------------------------------------ options

    private FrameworkElement OptionsStep()
    {
        var root = new ContentControl();
        void Build() => root.Content = OptionsContent();
        refreshOptions = Build;
        Build();
        return root;
    }

    private FrameworkElement OptionsContent()
    {
        var project = Ui.Input(options.Project, "Project name", true);
        project.Width = 280; project.HorizontalAlignment = HorizontalAlignment.Left;
        var projectError = Ui.Text("", 12, null, "Error", true, false);
        var projectFiles = Ui.Caption("");
        void ProjectChanged()
        {
            options.Project = project.Text.Trim();
            projectError.Text = SelfHostedRules.ValidateProject(options.Project, Taken) is { } e ? Loc.T(e) : "";
            projectFiles.Text = Loc.T("Files are written to") + " " + SelfHostedOps.Display(SelfHostedOps.ProjectFolder(options.Project.Length > 0 ? options.Project : "…"));
        }
        project.TextChanged += (_, _) => ProjectChanged();
        ProjectChanged();

        var portRows = new List<UIElement>();
        var portErrors = Ui.Text("", 12, null, "Error", true, false);
        void PortsChanged() => portErrors.Text = string.Join("  ", SelfHostedRules.ValidatePorts(app, options, inUse).Select(Loc.T));
        foreach (var port in app.Ports)
        {
            var key = port.Key;
            var box = Ui.Input(options.Port(app, key).ToString(CultureInfo.InvariantCulture), port.Purpose, true);
            box.Width = 120; box.HorizontalAlignment = HorizontalAlignment.Left;
            box.TextChanged += (_, _) => { options.HostPorts[key] = int.TryParse(box.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : 0; PortsChanged(); };
            var detail = Loc.T("Container port") + " " + port.ContainerPort.ToString(CultureInfo.InvariantCulture) + "/" + port.Protocol + " · " + Loc.T("service") + " " + port.Service;
            portRows.Add(Ui.Field(port.Purpose, box, detail, port.DefaultHostPort.ToString(CultureInfo.InvariantCulture)));
        }
        PortsChanged();
        var portStatus = Ui.Caption(capture ? "" : portsChecked
            ? Loc.T("Checked against ports published on this engine") + (engineLocal ? " " + Loc.T("and listeners on this computer.") : ".")
            : Loc.T("Checking ports in use…"));

        var folderBox = Ui.Input(options.Folder, "Data folder", true);
        folderBox.TextChanged += (_, _) => options.Folder = folderBox.Text.Trim();
        var folderRow = VolumeTransferChecks.FolderPicker(folderBox, "Choose the data folder");
        var folderField = Ui.Field("Data folder", folderRow, "Each volume gets a subfolder named after the project and the volume. Databases stay in named volumes: on Windows folders they are slow and can fail ownership checks.", "D:\\Docker data");
        folderField.Visibility = options.Storage == SelfHostedStorage.Folder ? Visibility.Visible : Visibility.Collapsed;
        var storage = Ui.Segmented(["Named volumes", "Folder"], options.Storage == SelfHostedStorage.Folder ? "Folder" : "Named volumes", v =>
        {
            options.Storage = v == "Folder" ? SelfHostedStorage.Folder : SelfHostedStorage.NamedVolumes;
            folderField.Visibility = options.Storage == SelfHostedStorage.Folder ? Visibility.Visible : Visibility.Collapsed;
        });
        storage.Width = 320; storage.HorizontalAlignment = HorizontalAlignment.Left;
        var volumeList = Ui.VStack(4, app.VolumesFor(options.Database).Select(v => (UIElement)Ui.Columns(10, [-18, 0, 1], Ui.Icon(v.Database ? Icons.Database : Icons.FolderOpen, 16), Ui.Mono(v.Key, 12, "OnSurfaceVariant"), Ui.Text(v.Purpose, 12, null, "OnSurfaceVariant"))).ToArray());

        var sections = new List<UIElement>
        {
            MinecraftControls.Section("Project", "A Compose project groups the app's containers, network and volumes under one name.",
                Ui.VStack(4, Ui.Field("Project name", project, "Lowercase letters, digits, dashes and underscores. It must not be used by another Compose project on this engine.", app.Id), projectError, projectFiles)),
            MinecraftControls.Section("Ports", "Host ports are published on every network interface of the engine host, so other devices can connect.", Ui.VStack(10, [.. portRows, portErrors, portStatus])),
            MinecraftControls.Section("Data", "Named volumes are managed by Docker and are the most reliable choice; a folder keeps the files where you can see them.", Ui.VStack(10, storage, folderField, volumeList)),
        };
        if (app.Databases.Count > 0)
        {
            string Name(SelfHostedDatabase d) => d == SelfHostedDatabase.PostgreSql ? "PostgreSQL" : "MariaDB";
            var db = Ui.Segmented(app.Databases.Select(Name).ToList(), Name(options.Database), v =>
            {
                options.Database = v == "PostgreSQL" ? SelfHostedDatabase.PostgreSql : SelfHostedDatabase.MariaDb;
                options.Secrets.Clear();
                SelfHostedRules.GenerateSecrets(app, options);
                refreshOptions?.Invoke();
            }, false);
            db.Width = 280; db.HorizontalAlignment = HorizontalAlignment.Left;
            sections.Add(MinecraftControls.Section("Database", null, db));
        }
        var zone = Ui.Input(options.TimeZone, "Time zone", true);
        zone.Width = 260; zone.HorizontalAlignment = HorizontalAlignment.Left;
        var zoneError = Ui.Text("", 12, null, "Error", true, false);
        zone.TextChanged += (_, _) => { options.TimeZone = zone.Text.Trim(); zoneError.Text = SelfHostedRules.ValidateTimeZone(options.TimeZone) is { } e ? Loc.T(e) : ""; };
        var hostChoices = new List<string> { "localhost" };
        hostChoices.InsertRange(0, lan.Select(l => l.Address));
        if (!engineLocal) hostChoices.Insert(0, engineHost);
        var address = Ui.Combo(hostChoices.Distinct(), options.PublicHost, true);
        address.Width = 260; address.HorizontalAlignment = HorizontalAlignment.Left;
        address.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => options.PublicHost = address.Text.Trim()));
        address.SelectionChanged += (_, _) => { if (address.SelectedItem is string s) options.PublicHost = s; };
        var settings = new List<UIElement>
        {
            Ui.VStack(4, Ui.Field("Time zone", zone, "IANA time zone used for logs and schedules. The default is this computer's time zone.", "Europe/London"), zoneError),
            Ui.Field("Address for links", address, "Host name or IP address the app uses in links it creates (for example GitLab clone URLs). Choose the LAN address other devices use.", "192.168.1.20"),
        };
        if (app.AdminUser != null)
        {
            var admin = Ui.Input(options.AdminUser, app.AdminIsEmail ? "Administrator e-mail" : "Administrator name", true);
            admin.Width = 260; admin.HorizontalAlignment = HorizontalAlignment.Left;
            var adminError = Ui.Text("", 12, null, "Error", true, false);
            admin.TextChanged += (_, _) => { options.AdminUser = admin.Text.Trim(); adminError.Text = SelfHostedRules.ValidateAdmin(app, options.AdminUser) is { } e ? Loc.T(e) : ""; };
            settings.Add(Ui.VStack(4, Ui.Field(app.AdminIsEmail ? "Administrator e-mail" : "Administrator name", admin, "The app creates this administrator on first start with the generated password.", app.AdminUser), adminError));
        }
        sections.Add(MinecraftControls.Section("Settings", null, Ui.VStack(12, settings.ToArray())));
        sections.Add(NeedsCard());
        return Ui.VStack(16, sections.ToArray());
    }

    private Border NeedsCard()
    {
        var lines = new List<UIElement>
        {
            Ui.Text(Loc.T("Needs at least") + " " + SelfHostedRules.MemoryText(app.MinMemoryMb) + " " + Loc.T("of memory for Docker and") + " " + app.MinDiskGb.ToString(CultureInfo.InvariantCulture) + " GB " + Loc.T("of disk."), 13, null, "OnSurface", true, false),
            Ui.Text(Loc.T("Licence:") + " " + app.Licence, 12, null, "OnSurfaceVariant", true, false),
        };
        if (app.Note != null) lines.Add(Ui.Text(app.Note, 12, null, "OnSurfaceVariant"));
        return Ui.Card(Ui.Columns(12, [-20, 1], Ui.Icon(Icons.Memory, 20), Ui.VStack(6, lines.ToArray())), 14, "SurfaceContainer", 12);
    }

    // ------------------------------------------------------------------ secrets

    private FrameworkElement SecretsStep()
    {
        var secrets = app.SecretsFor(options.Database).ToList();
        if (secrets.Count == 0)
            return Ui.Card(Ui.Columns(12, [-20, 1], Ui.Icon(Icons.Info, 20), Ui.Text("This app needs no generated secrets; it asks for its administrator account in its own first-run page.", 13)), 14, "SurfaceContainer", 12);
        var warning = Ui.Card(Ui.Columns(12, [-20, 1], Ui.Icon(Icons.Warning, 20, true, "OnWarningContainer"),
            Ui.Text("These values are shown only during this setup. They are saved only in the project's .env file, which only your Windows account can read, and never in logs, history, notifications or exports. Copy the ones you need into a password manager now.", 13, null, "OnWarningContainer")), 14, "WarningContainer", 12);
        var rows = new List<UIElement>();
        foreach (var secret in secrets)
        {
            var key = secret.Key;
            var shown = false;
            var value = Ui.Mono("••••••••••••", 13);
            var toggle = Ui.IconButton(Icons.Visibility, Loc.T("Show or hide") + ": " + Loc.T(secret.Purpose));
            toggle.Click += (_, _) => { shown = !shown; value.Text = shown ? options.Secrets[key] : "••••••••••••"; };
            var copy = Ui.IconButton(Icons.ContentCopy, Loc.T("Copy") + ": " + Loc.T(secret.Purpose), () => PageKit.Copy(options.Secrets[key], Loc.T(secret.Purpose)));
            rows.Add(Ui.Card(Ui.Columns(12, [1, 0, 0], Ui.VStack(4, Ui.Text(secret.Purpose, 13, FontWeights.SemiBold), Ui.Mono(key, 11, "OnSurfaceVariant"), value), toggle, copy), 12, "SurfaceContainer", 12));
        }
        var regenerate = Ui.Button("Generate new values", Icons.Refresh, ButtonKind.Tonal, () => { options.Secrets.Clear(); SelfHostedRules.GenerateSecrets(app, options); Go(1); });
        regenerate.HorizontalAlignment = HorizontalAlignment.Left;
        return Ui.VStack(12, [warning, .. rows, regenerate,
            Ui.Caption(Loc.T("Generated with the system's cryptographic random number generator:") + " " + string.Join(", ", secrets.Select(s => s.LaravelKey ? "32 bytes" : s.Length.ToString(CultureInfo.InvariantCulture) + " " + Loc.T("letters and digits")).Distinct()) + ".")]);
    }

    // ------------------------------------------------------------------ review

    internal FrameworkElement ReviewStep()
    {
        var problems = Problems();
        var problemList = problems.Count == 0
            ? (UIElement)Ui.Card(Ui.Columns(10, [0, 1], Ui.Icon(Icons.CheckCircle, 20, true, "OnSuccessContainer"), Ui.Text("Every setting is valid.", 13, null, "OnSuccessContainer")), 12, "SuccessContainer", 12)
            : Ui.Card(Ui.VStack(4, problems.Select(p => (UIElement)Ui.Text("• " + Loc.T(p), 13, null, "OnErrorContainer", true, false)).ToArray()), 12, "ErrorContainer", 12);
        var files = SelfHostedCompose.Render(app, options);
        var folder = SelfHostedOps.ProjectFolder(options.Project);
        var images = app.Services(options).Select(s => s.Image).ToList();
        var facts = Ui.VStack(6,
            PageKit.Fact("App", app.Name),
            PageKit.Fact("Project", options.Project, true),
            PageKit.Fact("Engine context", context() ?? "default", true),
            PageKit.Fact("Images", string.Join(Environment.NewLine, images), true),
            PageKit.Fact("Ports", string.Join(", ", app.Ports.Select(p => $"{options.Port(app, p.Key)} → {p.ContainerPort}/{p.Protocol}")), true),
            PageKit.Fact("Data", options.Storage == SelfHostedStorage.Folder ? options.Folder : Loc.T("Named volumes")),
            PageKit.Fact("Files", SelfHostedOps.Display(folder), true));
        var compose = Ui.CodeBox(files.Compose, 260);
        compose.IsReadOnly = true;
        compose.MaxHeight = 520;
        System.Windows.Automation.AutomationProperties.SetName(compose, "compose.yaml");
        var env = Ui.CodeBox(SelfHostedCompose.MaskedEnv(app, options), 120);
        env.IsReadOnly = true;
        System.Windows.Automation.AutomationProperties.SetName(env, ".env");
        var command = Ui.Mono(SelfHostedOps.Display(SelfHostedRules.Mask(PageKit.Preview(SelfHostedCompose.ComposeArgs(options.Project, folder, "up", "-d"), context()), options)), 12, "OnSurfaceVariant", true);
        var left = Ui.VStack(16,
            MinecraftControls.Section("Check", null, problemList, facts),
            NeedsCard(),
            MinecraftControls.Section("Command", "Runs on the selected engine context with the files above. Images are pulled on first deploy, which can take several minutes.", command));
        var right = Ui.VStack(16,
            MinecraftControls.Section("compose.yaml", "Exactly what is deployed. It holds no password or key; those are referenced from .env.", compose),
            MinecraftControls.Section(".env", "Secret values are masked here; the file itself is readable only by your Windows account.", env));
        return PageParts.Responsive(left, right, 1100, 1, 1.3);
    }

    // ------------------------------------------------------------------ deploy

    private async Task DeployAsync()
    {
        if (capture) return;
        var problems = Problems();
        if (problems.Count > 0) { stepError.Text = string.Join("  ", problems.Select(Loc.T)); return; }
        if (!ensureCurrent()) return;
        var ctx = context();
        string folder;
        try { folder = SelfHostedOps.WriteFiles(SelfHostedCompose.Render(app, options), options.Project); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException) { stepError.Text = Loc.T("The project files could not be written:") + " " + e.Message; return; }
        if (!SelfHostedOps.IsPrivate(Path.Combine(folder, ".env"))) Host.Notify.Warning("The .env file could not be limited to your Windows account. Check its permissions before sharing this computer.");
        deploying = true;
        Go(3);
        var progress = Ui.Text("", 13, null, "OnSurfaceVariant");
        var bar = new ProgressBar { IsIndeterminate = true, Height = 4, Margin = new Thickness(0, 4, 0, 4) };
        var stopWaiting = Ui.Button("Stop waiting", Icons.Stop, ButtonKind.Tonal, () => healthCancel?.Cancel());
        stopWaiting.HorizontalAlignment = HorizontalAlignment.Left;
        stopWaiting.Visibility = Visibility.Collapsed;
        body.Content = MinecraftControls.Section(Loc.T("Deploying {0}").Replace("{0}", app.Name), Loc.T("Pulling images and starting containers. The output panel shows each step."), progress, bar, stopWaiting);
        progress.Text = Loc.T("Starting the project…");
        var result = await Host.Execute($"Deploy {app.Name} ({options.Project})", SelfHostedCompose.ComposeArgs(options.Project, folder, "up", "-d"), true, ctx);
        if (result is not { Success: true })
        {
            deploying = false;
            Host.History.Record("selfhosted", $"Deploy of {app.Name} as {options.Project} failed");
            ShowResult(false, Loc.T("docker compose up did not complete. The output panel has the reason; the files stay in") + " " + SelfHostedOps.Display(folder) + ".", folder);
            return;
        }
        Host.History.Record("selfhosted", $"Deployed {app.Name} as {options.Project} on context {ctx ?? "default"}");
        stopWaiting.Visibility = Visibility.Visible;
        healthCancel = new CancellationTokenSource();
        var port = options.Port(app, app.HealthPort);
        var (health, detail) = await SelfHostedOps.WaitHealthyAsync(app, options.Project, port, engineLocal ? "localhost" : engineHost, ctx, app.HealthTimeout, new Progress<string>(t => progress.Text = t), healthCancel.Token);
        deploying = false;
        switch (health)
        {
            case SelfHostedOps.HealthResult.Healthy: ShowResult(true, detail, folder); break;
            case SelfHostedOps.HealthResult.Canceled: ShowResult(true, Loc.T("Stopped waiting. The containers keep starting; refresh the list to see their state."), folder, false); break;
            case SelfHostedOps.HealthResult.TimedOut: ShowResult(true, Loc.T("The app did not answer within") + " " + (int)app.HealthTimeout.TotalMinutes + " " + Loc.T("minutes. It may still be starting; check its logs.") + " " + detail, folder, false); break;
            default: ShowResult(false, detail, folder); break;
        }
    }

    private void ShowResult(bool started, string detail, string folder, bool healthy = true)
    {
        Go(3);
        cancel.Content = Loc.T("Done");
        var web = app.WebPort;
        var hostPort = options.Port(app, web.Key);
        var localUrl = $"http://{(engineLocal ? "localhost" : engineHost)}:{hostPort.ToString(CultureInfo.InvariantCulture)}";
        var status = started && healthy
            ? Ui.Card(Ui.Columns(10, [0, 1], Ui.Icon(Icons.CheckCircle, 20, true, "OnSuccessContainer"), Ui.Text(Loc.T("{0} is running.").Replace("{0}", app.Name) + " " + detail, 13, null, "OnSuccessContainer", true, false)), 12, "SuccessContainer", 12)
            : started
                ? Ui.Card(Ui.Columns(10, [0, 1], Ui.Icon(Icons.Pending, 20, true, "OnWarningContainer"), Ui.Text(detail, 13, null, "OnWarningContainer", true, false)), 12, "WarningContainer", 12)
                : Ui.Card(Ui.Columns(10, [0, 1], Ui.Icon(Icons.Error, 20, true, "OnErrorContainer"), Ui.Text(detail, 13, null, "OnErrorContainer", true, false)), 12, "ErrorContainer", 12);
        var parts = new List<UIElement> { status };
        if (started)
        {
            var urls = new List<UIElement> { UrlRow(engineLocal ? Loc.T("This computer") : Loc.T("Engine host") + " " + engineHost, localUrl) };
            if (engineLocal)
                foreach (var (address, adapter) in lan) urls.Add(UrlRow(Loc.T("LAN") + " · " + adapter, $"http://{address}:{hostPort.ToString(CultureInfo.InvariantCulture)}"));
            if (engineLocal && lan.Count == 0) urls.Add(Ui.Caption("No LAN address was found on a physical network adapter that is up, so only this computer can open the app."));
            var notes = new List<UIElement>();
            if (engineLocal) notes.Add(Ui.Caption("Docker Desktop publishes ports on every interface. Windows Firewall may still need to allow the port before other devices can connect."));
            else notes.Add(Ui.Caption(Loc.T("The engine runs on") + " " + engineHost + Loc.T(", so the app is reached through that host, not this computer.")));
            parts.Add(MinecraftControls.Section("Addresses", null, [.. urls, .. notes]));
            parts.Add(MinecraftControls.Section("First sign-in", null, Ui.Text(app.FirstLogin, 13)));
        }
        var openCompose = Ui.Button("Open in Compose", Icons.Stacks, ButtonKind.Tonal, () => { Host.HandOff["Compose.OpenFile"] = Path.Combine(folder, "compose.yaml"); finished(options.Project); Host.Navigate("Compose"); });
        var done = Ui.Button("Back to projects", Icons.ArrowBack, ButtonKind.Text, () => finished(options.Project));
        parts.Add(Ui.HStack(8, openCompose, done));
        body.Content = Ui.VStack(16, parts.ToArray());
    }

    private static UIElement UrlRow(string label, string url)
    {
        var open = Ui.Button("Open in browser", Icons.OpenInNew, ButtonKind.Filled, () => PageParts.OpenExternal(url));
        var copy = Ui.IconButton(Icons.ContentCopy, "Copy URL", () => PageKit.Copy(url, "the address"));
        return Ui.Columns(12, [1, 0, 0], Ui.VStack(2, Ui.Text(label, 12, null, "OnSurfaceVariant", true, false), Ui.Mono(url, 14)), open, copy);
    }

    /// <summary>Capture mode only: shows the review step with defaults, without reading the engine or writing files.</summary>
    internal void CaptureReview() => Go(2);
}
