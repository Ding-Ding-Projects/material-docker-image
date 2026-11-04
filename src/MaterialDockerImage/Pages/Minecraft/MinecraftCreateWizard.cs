using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Guided creation of a Minecraft server container: target and edition, version and type, resources and network, world
/// and gameplay, players and add-ons, then review with the EULA and a live command preview.
/// </summary>
internal sealed class MinecraftCreateWizard : Grid
{
    private static AppHost Host => AppHost.Current;
    private static readonly string[] Steps = ["Target and edition", "Version", "Resources and network", "World and gameplay", "Players and add-ons", "Review and create"];
    private readonly Func<string?> context;
    private readonly Func<bool> ensureCurrent;
    private readonly Action<string?> finished;
    private readonly IReadOnlyCollection<string> existingNames;
    private MinecraftServerSpec spec = new();
    private int step;
    private readonly WrapPanel stepBar = new();
    private readonly ContentControl body = new();
    private readonly TextBlock stepError = Ui.Text("", 13, null, "Error", true, false);
    private readonly Button back, next, create;
    private TextBox? preview;

    public MinecraftCreateWizard(Func<string?> context, Func<bool> ensureCurrent, IReadOnlyCollection<string> existingNames, Action<string?> finished)
    {
        this.context = context; this.ensureCurrent = ensureCurrent; this.finished = finished; this.existingNames = existingNames;
        spec.Name = UniqueName("survival");
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        stepBar.Margin = new Thickness(0, 0, 0, 12);
        System.Windows.Automation.AutomationProperties.SetName(stepBar, Loc.T("Steps"));
        Children.Add(stepBar);
        Children.Add(Ui.Scroll(body).At(1));
        back = Ui.Button("Back", Icons.ArrowBack, ButtonKind.Text, () => Go(step - 1));
        next = Ui.Button("Next", Icons.ArrowForward, ButtonKind.Filled, () => { if (CheckStep()) Go(step + 1); });
        create = Ui.AsyncButton("Create server", Icons.RocketLaunch, ButtonKind.Filled, CreateAsync);
        var cancel = Ui.Button("Cancel", null, ButtonKind.Text, () => finished(null));
        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var right = Ui.HStack(8, back, next, create);
        footer.Children.Add(right.Dock(Dock.Right));
        footer.Children.Add(cancel.Dock(Dock.Left));
        footer.Children.Add(stepError.Margin(12, 0, 12, 0));
        Children.Add(footer.At(2));
        Go(0);
    }

    private string UniqueName(string stem)
    {
        var name = stem; var i = 2;
        while (existingNames.Contains(name)) name = stem + "-" + i++;
        return name;
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
            var dot = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Child = done ? Ui.Icon(Icons.Check, 16, true, "OnPrimary") : Ui.Text((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), 13, FontWeights.SemiBold, i == step ? "OnPrimary" : "OnSurfaceVariant", false, false) };
            if (dot.Child is TextBlock tb) tb.HorizontalAlignment = HorizontalAlignment.Center;
            dot.SetResourceReference(Border.BackgroundProperty, i <= step ? "Primary" : "SurfaceHighest");
            var label = Ui.Text(Steps[i], 13, i == step ? FontWeights.SemiBold : FontWeights.Normal, i == step ? "OnSurface" : "OnSurfaceVariant", false);
            var item = new Button { Style = (Style)Application.Current.FindResource("Btn.Text"), Content = Ui.HStack(8, dot, label), Margin = new Thickness(0, 0, 4, 4), IsEnabled = i <= step };
            System.Windows.Automation.AutomationProperties.SetName(item, $"Step {i + 1}: {Loc.T(Steps[i])}" + (i == step ? " (current)" : ""));
            item.Click += (_, _) => Go(index);
            stepBar.Children.Add(item);
        }
        back.IsEnabled = step > 0;
        next.Visibility = step < Steps.Length - 1 ? Visibility.Visible : Visibility.Collapsed;
        create.Visibility = step == Steps.Length - 1 ? Visibility.Visible : Visibility.Collapsed;
        preview = null;
        body.Content = step switch
        {
            0 => TargetStep(),
            1 => MinecraftSpecEditor.VersionSection(spec, context, Changed),
            2 => MinecraftSpecEditor.ResourcesSection(spec, Changed),
            3 => MinecraftSpecEditor.GameplaySection(spec, Changed),
            4 => MinecraftSpecEditor.PlayersSection(spec, Changed),
            _ => ReviewStep(),
        };
    }

    private void Changed()
    {
        if (preview != null) preview.Text = PreviewText();
    }

    private string PreviewText()
    {
        var lines = new List<string>
        {
            MinecraftSpecRules.Preview(MinecraftSpecRules.VolumeCreateArguments(spec)),
            MinecraftSpecRules.Preview(MinecraftSpecRules.RunArguments(spec, spec.Edition == MinecraftEdition.Java && spec.RconEnabled ? "env-file" : null)),
        };
        return string.Join(Environment.NewLine + Environment.NewLine, lines);
    }

    /// <summary>Problems belonging to the current step (by the field prefix Validate uses).</summary>
    private bool CheckStep()
    {
        var prefixes = step switch
        {
            0 => new[] { "Name" },
            1 => new[] { "Version", "Flavour", "Custom server jar" },
            2 => new[] { "Memory", "Bind address", "Port" },
            3 => new[] { "Difficulty", "Game mode", "Max players", "View distance", "Simulation distance", "Spawn protection", "Level type", "Message of the day", "Seed" },
            4 => new[] { "Player", "Modrinth", "Spigot" },
            _ => Array.Empty<string>(),
        };
        var problems = MinecraftSpecRules.Validate(spec).Where(p => prefixes.Any(x => p.StartsWith(x, StringComparison.Ordinal))).ToList();
        if (step == 0 && existingNames.Contains(spec.Name)) problems.Add("Name: a server with this name already exists.");
        stepError.Text = string.Join("  ", problems);
        return problems.Count == 0;
    }

    private FrameworkElement TargetStep()
    {
        var group = "mctarget" + Guid.NewGuid().ToString("N");
        RadioButton Target(string title, string detail, bool available, bool selected)
        {
            var r = new RadioButton { GroupName = group, IsChecked = selected, IsEnabled = available, Margin = new Thickness(0, 0, 0, 8), Content = Ui.VStack(2, Ui.Text(title, 14, FontWeights.SemiBold), Ui.Caption(detail)) };
            System.Windows.Automation.AutomationProperties.SetName(r, Loc.T(title));
            return r;
        }
        var engine = context() ?? "default";
        var targets = Ui.VStack(0,
            Target(Loc.T("Docker engine: context") + " " + engine, "Runs the server as a container on the selected engine context. Contexts with an ssh:// endpoint run it on that remote host.", true, true),
            Target("Local Windows process (not available in this app)", "This app manages servers only as Docker containers. Running a bare Java or Bedrock process would need a separate process supervisor, which this app does not include.", false, false),
            Target("SSH process (not available in this app)", "This app has no SSH process runner. To use a remote machine, give it Docker and add an ssh:// engine context in Contexts.", false, false));
        var name = Ui.Input(spec.Name, "Server name", true);
        name.Width = 260; name.HorizontalAlignment = HorizontalAlignment.Left;
        var nameError = Ui.Text("", 12, null, "Error", true, false);
        var resources = Ui.Caption("");
        void UpdateName() { resources.Text = $"Container {spec.ContainerName} · world volume {spec.VolumeName}"; nameError.Text = MinecraftSpecRules.ValidateName(spec.Name) ?? (existingNames.Contains(spec.Name) ? "A server with this name already exists." : ""); }
        name.TextChanged += (_, _) => { spec.Name = name.Text.Trim(); UpdateName(); };
        UpdateName();
        var edition = Ui.Segmented(["Java", "Bedrock"], spec.Edition == MinecraftEdition.Java ? "Java" : "Bedrock", v =>
        {
            var e = v == "Java" ? MinecraftEdition.Java : MinecraftEdition.Bedrock;
            if (e == spec.Edition) return;
            spec.Edition = e;
            spec.Version = "LATEST";
            spec.Port = spec.DefaultPort;
            spec.LevelType = e == MinecraftEdition.Java ? "minecraft:normal" : "DEFAULT";
            spec.SimulationDistance = e == MinecraftEdition.Java ? 10 : 4;
            spec.ModrinthProjects.Clear(); spec.SpigetResources.Clear(); spec.Ops.Clear();
            spec.RconEnabled = e == MinecraftEdition.Java;
        }, false);
        edition.Width = 280; edition.HorizontalAlignment = HorizontalAlignment.Left;
        return Ui.VStack(16,
            MinecraftControls.Section("Where the server runs", Loc.T("Every container and volume this page creates is labelled") + " " + MinecraftSpecRules.Label + "=<name>, and only those are listed.", targets),
            MinecraftControls.Section("Server", null,
                Ui.Field("Edition", edition, "Java Edition uses itzg/minecraft-server (TCP 25565) and supports plugins and mods. Bedrock Edition uses itzg/minecraft-bedrock-server (UDP 19132) for phones, consoles and Windows Bedrock clients.", "Java"),
                Ui.VStack(4, Ui.Field("Server name", name, "Lowercase letters, digits and hyphens. Used for the container (mc-<name>) and the world volume (mc-<name>-data).", "survival"), nameError, resources)));
    }

    private FrameworkElement ReviewStep()
    {
        var problems = MinecraftSpecRules.Validate(spec).Where(p => !p.StartsWith("EULA", StringComparison.Ordinal)).ToList();
        if (existingNames.Contains(spec.Name)) problems.Add("Name: a server with this name already exists.");
        var problemList = problems.Count == 0
            ? (UIElement)Ui.Card(Ui.Columns(10, [0, 1], Ui.Icon(Icons.CheckCircle, 20, true, "OnSuccessContainer"), Ui.Text("Every setting is valid.", 13, null, "OnSuccessContainer")), 12, "SuccessContainer", 12)
            : Ui.Card(Ui.VStack(4, problems.Select(p => (UIElement)Ui.Text("• " + p, 13, null, "OnErrorContainer", true, false)).ToArray()), 12, "ErrorContainer", 12);
        var facts = Ui.VStack(6,
            PageKit.Fact("Edition", spec.Edition.ToString()),
            PageKit.Fact("Version", spec.Version, true),
            spec.Edition == MinecraftEdition.Java ? PageKit.Fact("Server type", spec.Flavour) : null,
            PageKit.Fact("Image", spec.Image, true),
            PageKit.Fact("Address", $"{spec.BindAddress}:{spec.Port}", true),
            PageKit.Fact("World volume", spec.VolumeName, true),
            PageKit.Fact("Engine context", context() ?? "default", true));
        var eula = Ui.Check("I have read and accept the Minecraft End User License Agreement", spec.EulaAccepted, v => { spec.EulaAccepted = v; Changed(); });
        var link = Ui.AsyncButton("Read the Minecraft EULA", Icons.OpenInNew, ButtonKind.Text, async () =>
        {
            if (await Host.Overlay.ConfirmAsync("Open the Minecraft EULA?", "This opens https://aka.ms/MinecraftEULA in your web browser.", "Open"))
                PageParts.OpenExternal("https://aka.ms/MinecraftEULA");
        });
        link.HorizontalAlignment = HorizontalAlignment.Left;
        preview = Ui.CodeBox(PreviewText(), 120);
        preview.IsReadOnly = true;
        System.Windows.Automation.AutomationProperties.SetName(preview, Loc.T("Command preview"));
        return Ui.VStack(16,
            MinecraftControls.Section("Check", null, problemList, facts),
            MinecraftControls.Section("Minecraft EULA", "The server only starts after you accept Mojang's EULA. This app never accepts it for you; EULA=TRUE is set only when the box is ticked.", link, eula),
            MinecraftControls.Section("Command preview", "Exactly what runs on the engine. The RCON password is generated at creation, written to a temporary env file that is deleted afterwards, and never shown.", preview));
    }

    private async Task CreateAsync()
    {
        var problems = MinecraftSpecRules.Validate(spec);
        if (existingNames.Contains(spec.Name)) problems.Add("Name: a server with this name already exists.");
        if (problems.Count > 0) { stepError.Text = string.Join("  ", problems); return; }
        if (!ensureCurrent()) return;
        if (await MinecraftOps.CreateAsync(spec.Clone(), context())) finished(spec.Name);
    }
}
