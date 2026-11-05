using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Right-hand panel for the focused container: header with lifecycle buttons that follow the observed state, and tabs for
/// Overview, Logs, Stats, Processes, Files, Exec and Config.
/// </summary>
internal sealed partial class ContainerInspector : Border
{
    private static AppHost Host => AppHost.Current;
    private static readonly string[] Tabs = ["Overview", "Logs", "Stats", "Processes", "Files", "Exec", "Config"];
    private readonly ContainersPage page;
    private readonly ContentControl headerHost = new();
    private readonly ContentControl body = new();
    private readonly ContainerLogs logs = new();
    private readonly ContainerExec exec;
    private readonly Grid root = new();
    // One row of equal-width tabs (44 high, no wrapping) under a hairline, as in the design.
    private readonly Grid tabsGrid = new();
    private readonly Border tabsBar = new() { BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 12, 0, 12) };
    private string tab = "Overview";
    private ContainerRow? current;
    private JsonElement? inspect;
    private string inspectJson = "";
    private int version;
    private int inspectVersion;
    private readonly System.Windows.Threading.DispatcherTimer healthTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    public ContainerInspector(ContainersPage page)
    {
        this.page = page;
        exec = new ContainerExec(page);
        Width = 420;
        Margin = new Thickness(16, 0, 0, 0);
        CornerRadius = new CornerRadius(16);
        Padding = new Thickness(16);
        SetResourceReference(BackgroundProperty, "SurfaceLow");
        System.Windows.Automation.AutomationProperties.SetName(this, Loc.T("Selected container"));
        var group = "inspector" + Guid.NewGuid().ToString("N");
        tabsBar.SetResourceReference(Border.BorderBrushProperty, "SurfaceHigh");
        tabsBar.Child = tabsGrid;
        foreach (var name in Tabs)
        {
            var radio = new RadioButton { Template = TabTemplate(), GroupName = group, Content = new TextBlock { Text = Loc.T(name), TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 13 }, IsChecked = name == tab, Padding = new Thickness(2, 0, 2, 0), ToolTip = Loc.T(name), Cursor = System.Windows.Input.Cursors.Hand, FocusVisualStyle = null };
            System.Windows.Automation.AutomationProperties.SetName(radio, Loc.T(name));
            radio.Checked += (_, _) => { tab = name; ShowTab(); };
            tabsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(radio, tabsGrid.ColumnDefinitions.Count - 1);
            tabsGrid.Children.Add(radio);
        }
        System.Windows.Automation.AutomationProperties.SetName(tabsBar, Loc.T("Container details"));
        var tabsScroll = tabsBar;
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(headerHost);
        root.Children.Add(tabsScroll.At(1));
        root.Children.Add(body.At(2));
        Child = root;
        healthTimer.Tick += (_, _) => RefreshHealth();
        IsVisibleChanged += (_, _) => { if (IsVisible) healthTimer.Start(); else healthTimer.Stop(); };
        Show(null);
    }

    /// <summary>While the Overview shows a running container with a health check, re-reads it so status, failing streak and probe output stay live.</summary>
    private void RefreshHealth()
    {
        if (current is not { IsRunning: true } row || tab != "Overview" || inspect is not { } info || !ContainerInspectFacts.Health(info).HasHealthcheck) return;
        _ = LoadInspectAsync(row, version, ++inspectVersion, true);
    }

    private static string HealthSignature(JsonElement? info)
    {
        if (info is not { } e) return "";
        var h = ContainerInspectFacts.Health(e);
        return h.Status + "|" + h.FailingStreak + "|" + (h.Probes.Count > 0 ? h.Probes[0].Ended : "");
    }

    /// <summary>Stops any background log stream (called when the page is hidden).</summary>
    public void Stop() => logs.Stop();

    /// <summary>Reloads the logs when the page becomes visible again with the Logs tab open.</summary>
    public void Resume() { if (current != null && tab == "Logs") logs.Reload(); }

    public void Show(ContainerRow? row)
    {
        var previous = current;
        var same = row != null && previous != null && row.Id == previous.Id;
        current = row;
        if (same)
        {
            // Same container observed again (for example by auto-refresh): update the header; reload details only when the state changed.
            if (row == previous) return;
            headerHost.Content = Header(row!);
            exec.SetContainer(row);
            if (row!.State != previous!.State || row.Status != previous.Status) _ = LoadInspectAsync(row, version, ++inspectVersion);
            return;
        }
        version++;
        logs.SetContainer(row, page.Context);
        exec.SetContainer(row);
        if (row == null)
        {
            headerHost.Content = null;
            tabsBar.Visibility = Visibility.Collapsed;
            body.Content = Ui.EmptyState(Icons.DeployedCode, "No container selected", "Select a container to see its configuration, logs, live statistics, processes and file changes.");
            return;
        }
        tabsBar.Visibility = Visibility.Visible;
        headerHost.Content = Header(row);
        inspect = null; inspectJson = "";
        ShowTab();
        _ = LoadInspectAsync(row, version, ++inspectVersion);
    }

    private UIElement Header(ContainerRow row)
    {
        var title = Ui.VStack(2,
            Ui.Text(row.Name, 18, FontWeights.SemiBold, "OnSurface", false, false),
            Ui.Text($"{row.Image} · {row.Status}", 13, null, "OnSurfaceVariant", true, false));
        Button Life(string icon, string name, Func<Task> action, bool enabled)
        {
            var b = Ui.AsyncIconButton(icon, name, action);
            b.IsEnabled = enabled;
            return b;
        }
        // Lifecycle actions are round buttons on a SurfaceHigh fill; Remove is an outlined red button pushed to the right.
        Button Round(Button b, string? foreground = null)
        {
            b.SetResourceReference(Control.BackgroundProperty, "SurfaceHigh");
            b.SetResourceReference(Control.ForegroundProperty, foreground ?? "OnSurface");
            return b;
        }
        var lifecycle = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var b in new Button[]
        {
            Round(Life(Icons.PlayArrow, "Start", () => page.LifecycleAsync("start", row), row.IsStopped)),
            Round(Life(Icons.Stop, "Stop", () => page.LifecycleAsync("stop", row), row.IsRunning || row.IsRestarting)),
            Round(Life(Icons.RestartAlt, "Restart", () => page.LifecycleAsync("restart", row), row.IsRunning || row.IsStopped)),
            row.IsPaused ? Round(Life(Icons.PlayArrow, "Unpause", () => page.LifecycleAsync("unpause", row), true)) : Round(Life(Icons.Pause, "Pause", () => page.LifecycleAsync("pause", row), row.IsRunning)),
            Round(Life(Icons.Bolt, "Kill", () => page.KillAsync(row), row.IsRunning || row.IsPaused || row.IsRestarting), "Error"),
        }) { b.Margin = new Thickness(0, 0, 4, 4); lifecycle.Children.Add(b); }
        var more = Ui.IconButton(Icons.MoreVert, "More container actions", () => { });
        more.Click += (_, _) => page.ShowMoreMenu(more, row);
        more.Margin = new Thickness(0, 0, 4, 4);
        lifecycle.Children.Add(more);
        var remove = Ui.AsyncButton("Remove", Icons.Delete, ButtonKind.Outlined, () => page.RemoveOneAsync(row));
        remove.SetResourceReference(Control.ForegroundProperty, "Error");
        remove.Margin = new Thickness(8, 0, 0, 4);
        var buttons = new DockPanel { Margin = new Thickness(0, 8, 0, 0), LastChildFill = true };
        buttons.Children.Add(remove.Dock(System.Windows.Controls.Dock.Right));
        buttons.Children.Add(lifecycle);
        var exec = Ui.Button("Exec", Icons.Terminal, ButtonKind.Tonal, () => SelectTab("Exec"));
        exec.Height = exec.MinHeight = 36;
        exec.Padding = new Thickness(14, 0, 14, 0);
        exec.VerticalAlignment = VerticalAlignment.Top;
        return Ui.VStack(0, Ui.Columns(8, [1, 0, 0], title, Ui.Badge(row.StateLabel, row.StateTone).Margin(0, 2, 0, 0), exec), buttons);
    }

    /// <summary>Shows a tab by name (the header's Exec button uses it) and keeps the tab row in step.</summary>
    private void SelectTab(string name)
    {
        foreach (var radio in tabsGrid.Children.OfType<RadioButton>())
            if (AutomationProperties.GetName(radio) == Loc.T(name)) { radio.IsChecked = true; return; }
    }

    /// <summary>A secondary tab: transparent, 3px Primary bar and Primary semibold text when selected.</summary>
    private static ControlTemplate TabTemplate()
    {
        var template = new ControlTemplate(typeof(RadioButton));
        var border = new FrameworkElementFactory(typeof(Border), "bar");
        border.SetValue(FrameworkElement.HeightProperty, 44.0);
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 3));
        border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        template.VisualTree = border;
        var on = new Trigger { Property = System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, Value = true };
        on.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Primary"), "bar"));
        on.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Primary")));
        on.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        template.Triggers.Add(on);
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("HoverLayer"), "bar"));
        template.Triggers.Add(hover);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("PressLayer"), "bar"));
        template.Triggers.Add(focus);
        return template;
    }

    private async Task LoadInspectAsync(ContainerRow row, int myVersion, int myInspect, bool quiet = false)
    {
        try
        {
            var result = await Host.Read(["container", "inspect", row.Id], page.Context);
            if (myVersion != version || myInspect != inspectVersion) return;
            if (!result.Success) { if (quiet) return; inspect = null; inspectJson = ""; if (tab is "Overview" or "Config") body.Content = Error("docker container inspect failed: " + result.StandardError.Trim()); return; }
            var first = inspectJson.Length == 0;
            var before = HealthSignature(inspect);
            inspectJson = result.StandardOutput;
            inspect = PageKit.FirstInspect(inspectJson);
            if (quiet && before == HealthSignature(inspect)) return;
            if (tab == "Overview" || (tab == "Config" && first)) ShowTab();
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException)
        {
            if (myVersion == version) body.Content = Error(e.Message);
        }
    }

    private static UIElement Error(string text) => Ui.Text(text, 13, null, "Error", true, false);

    private void ShowTab()
    {
        if (current == null) return;
        if (tab != "Logs") logs.Stop();
        switch (tab)
        {
            case "Overview": body.Content = inspect is { } info ? Ui.Scroll(Overview(info)) : Ui.Caption("Reading container details…"); break;
            case "Logs": body.Content = logs; logs.Reload(); break;
            case "Stats": body.Content = StatsView(current); break;
            case "Processes": body.Content = ProcessesView(current); break;
            case "Files": body.Content = FilesView(current); break;
            case "Exec": body.Content = Ui.Scroll(exec); break;
            case "Config": body.Content = ConfigView(); break;
        }
    }

    private UIElement Overview(JsonElement info)
    {
        var state = PageKit.Child(info, "State");
        var health = state is { } s ? PageKit.Field(s, "Health", "Status") : "";
        var cmd = PageKit.Strings(PageKit.Child(info, "Config", "Cmd")).ToList();
        var entry = PageKit.Strings(PageKit.Child(info, "Config", "Entrypoint")).ToList();
        var envNames = PageKit.Strings(PageKit.Child(info, "Config", "Env")).ToList();
        var ports = new List<string>();
        if (PageKit.Child(info, "NetworkSettings", "Ports") is { ValueKind: JsonValueKind.Object } p)
        {
            foreach (var port in p.EnumerateObject())
            {
                if (port.Value.ValueKind != JsonValueKind.Array) { ports.Add(port.Name + " (not published)"); continue; }
                foreach (var binding in port.Value.EnumerateArray()) ports.Add($"{PageKit.Field(binding, "HostIp")}:{PageKit.Field(binding, "HostPort")} → {port.Name}");
            }
        }
        var networks = PageKit.Child(info, "NetworkSettings", "Networks") is { ValueKind: JsonValueKind.Object } n
            ? n.EnumerateObject().Select(x => x.Name + (PageKit.Field(x.Value, "IPAddress") is { Length: > 0 } ip ? "  " + ip : "")).ToList() : [];
        var mounts = PageKit.Child(info, "Mounts") is { ValueKind: JsonValueKind.Array } m
            ? m.EnumerateArray().Select(x => $"{PageKit.Field(x, "Type")}  {(PageKit.Field(x, "Name") is { Length: > 0 } nm ? nm : PageKit.Field(x, "Source"))} → {PageKit.Field(x, "Destination")}{(PageKit.Field(x, "RW") == "False" || PageKit.Field(x, "RW") == "false" ? "  (read-only)" : "")}").ToList() : [];

        var envList = new StackPanel();
        void RenderEnv(bool values)
        {
            envList.Children.Clear();
            foreach (var e in envNames)
            {
                var eq = e.IndexOf('=');
                envList.Children.Add(Ui.Mono(values ? e : (eq < 0 ? e : e[..eq]) + "=•••", 12, "OnSurface", true));
            }
        }
        RenderEnv(false);
        var reveal = Ui.Switch("Show values", false, RenderEnv, "Values can include credentials. They are shown only while this is on and are never logged.");

        return Ui.VStack(14,
            Ui.VStack(6,
                PageKit.Fact("ID", PageKit.Field(info, "Id"), true),
                PageKit.Fact("Image", PageKit.Field(info, "Config", "Image")),
                PageKit.Fact("Image ID", PageKit.Field(info, "Image"), true),
                PageKit.Fact("Entrypoint", entry.Count > 0 ? JsonSerializer.Serialize(entry) : "", true),
                PageKit.Fact("Command", cmd.Count > 0 ? JsonSerializer.Serialize(cmd) : "", true),
                PageKit.Fact("Created", PageKit.FormatTime(PageKit.Field(info, "Created"))),
                PageKit.Fact("State", state is { } st ? PageKit.Field(st, "Status") + (health.Length > 0 ? " · " + health : "") : ""),
                PageKit.Fact("Started", state is { } st2 ? PageKit.FormatTime(PageKit.Field(st2, "StartedAt")) : ""),
                PageKit.Fact("Finished", state is { } st3 && PageKit.Field(st3, "Status") != "running" ? PageKit.FormatTime(PageKit.Field(st3, "FinishedAt")) : ""),
                PageKit.Fact("Exit code", state is { } st4 ? PageKit.Field(st4, "ExitCode") : ""),
                PageKit.Fact("Restart count", PageKit.Field(info, "RestartCount")),
                PageKit.Fact("Restart policy", PageKit.Field(info, "HostConfig", "RestartPolicy", "Name"))),
            Section("Ports", ports),
            Section("Networks", networks),
            Section("Mounts", mounts),
            Ui.VStack(6, Ui.Overline("Environment"), envNames.Count == 0 ? Ui.Caption("No environment variables.") : Ui.VStack(6, reveal, envList)));
    }

    private static UIElement HealthSection(JsonElement info)
    {
        var facts = ContainerInspectFacts.Health(info);
        if (!facts.HasHealthcheck) return Ui.VStack(6, Ui.Overline("Health check"), Ui.Caption("This container has no health check. Add one when you run it, or define HEALTHCHECK in the image."));
        if (facts.Disabled) return Ui.VStack(6, Ui.Overline("Health check"), Ui.Caption("The health check is disabled for this container."));
        var rows = new List<UIElement>
        {
            PageKit.Fact("Status", facts.Status.Length > 0 ? facts.Status : "not reported yet"),
            PageKit.Fact("Failing streak", facts.Status.Length > 0 ? facts.FailingStreak.ToString(CultureInfo.InvariantCulture) : ""),
            PageKit.Fact("Command", facts.Test, true),
            PageKit.Fact("Interval", facts.Interval),
            PageKit.Fact("Timeout", facts.Timeout),
            PageKit.Fact("Retries", facts.Retries > 0 ? facts.Retries.ToString(CultureInfo.InvariantCulture) : ""),
            PageKit.Fact("Start period", facts.StartPeriod),
            PageKit.Fact("Start interval", facts.StartInterval),
        };
        UIElement probes = facts.Probes.Count == 0
            ? Ui.Caption("No probe has finished yet.")
            : Ui.VStack(6, facts.Probes.Take(5).Select(p => (UIElement)Ui.VStack(2,
                Ui.Mono($"{PageKit.FormatTime(p.Ended)}  exit {p.ExitCode.ToString(CultureInfo.InvariantCulture)}", 12, p.ExitCode == 0 ? "OnSurface" : "Error", true),
                p.Output.Length > 0 ? Ui.Mono(p.Output, 12, "OnSurfaceVariant", true) : Ui.Caption("No output."))).ToArray());
        return Ui.VStack(6, Ui.Overline("Health check"), Ui.VStack(6, rows.ToArray()), Ui.Overline("Last probes"), probes);
    }

    private static UIElement HardwareSection(JsonElement info)
    {
        var devices = ContainerInspectFacts.Devices(info).ToList();
        var rules = ContainerInspectFacts.CgroupRules(info).ToList();
        var gpus = ContainerInspectFacts.GpuRequests(info).ToList();
        if (devices.Count == 0 && rules.Count == 0 && gpus.Count == 0) return Ui.VStack(6, Ui.Overline("Devices and GPUs"), Ui.Caption("No device mappings, device rules or GPU requests."));
        var items = new List<UIElement>();
        void Add(string title, IReadOnlyList<string> lines) { if (lines.Count > 0) { items.Add(Ui.Caption(title)); items.AddRange(lines.Select(l => (UIElement)Ui.Mono(l, 12, "OnSurface", true))); } }
        Add("Device mappings", devices);
        Add("Device cgroup rules", rules);
        Add("GPU requests", gpus);
        var error = ContainerInspectFacts.StartError(info);
        var note = gpus.Count > 0 || devices.Count > 0 ? ContainerInspectFacts.HardwareFailureNote(error) : "";
        if (error.Length > 0 && note.Length > 0) { items.Add(Ui.Text(error, 12, null, "Error", true, false)); items.Add(Ui.Caption(note)); }
        else if (gpus.Count > 0) items.Add(Ui.Caption("A GPU request is only honoured when the engine host has a GPU driver such as the NVIDIA Container Toolkit. Otherwise the container does not start."));
        return Ui.VStack(6, Ui.Overline("Devices and GPUs"), Ui.VStack(2, items.ToArray()));
    }

    private static UIElement Section(string title, IReadOnlyList<string> items) =>
        Ui.VStack(6, Ui.Overline(title), items.Count == 0 ? Ui.Caption("None.") : Ui.VStack(2, items.Select(i => (UIElement)Ui.Mono(i, 12, "OnSurface", true)).ToArray()));

    private UIElement ConfigView()
    {
        if (inspectJson.Length == 0) return Ui.Caption("Reading container details…");
        var masked = PageKit.FormatInspect(inspectJson, true) ?? inspectJson;
        var full = PageKit.FormatInspect(inspectJson, false) ?? inspectJson;
        var view = new SearchableText("Container configuration", PageKit.SafeFileName(current?.Name ?? "container") + "-inspect.json", "Search configuration");
        view.Search.Width = 200;
        view.SetText(masked);
        view.Extra.Children.Add(Ui.Switch("Show environment values", false, on => view.SetText(on ? full : masked)));
        return view;
    }
}
