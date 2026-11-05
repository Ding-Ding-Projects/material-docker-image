using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Right-hand "Create context" panel: name, description, endpoint host with presets, an optional TLS files section, a live
/// command preview, Test connection and Create context. Test connection reaches the endpoint without creating anything.
/// </summary>
internal sealed class ContextCreatePanel : Border
{
    private static AppHost Host => AppHost.Current;
    private readonly TextBox name = Ui.Input("", "Name", true);
    private readonly TextBox description = Ui.Input("", "Description");
    private readonly TextBox hostBox = Ui.Input("npipe:////./pipe/docker_engine", "Docker endpoint host", true);
    private readonly TextBox ca = Ui.Input("", "CA certificate", true);
    private readonly TextBox cert = Ui.Input("", "Client certificate", true);
    private readonly TextBox key = Ui.Input("", "Client key", true);
    private readonly TextBlock preview = Ui.Mono("", 12, "OnSurfaceVariant", true);
    private readonly TextBlock nameHint = Ui.Caption("Letters, digits, dot, dash and underscore");
    private readonly TextBlock error = Ui.Text("", 13, null, "Error", true, false);
    private readonly ContentControl testResult = new();
    private readonly StackPanel tlsFiles;
    private bool useTls;
    private bool skipVerify;
    private readonly Func<Task> reload;
    public event Action? Closed;

    public ContextCreatePanel(Func<Task> reload)
    {
        this.reload = reload;
        CornerRadius = new CornerRadius(16);
        Padding = new Thickness(20);
        SetResourceReference(BackgroundProperty, "SurfaceLow");
        System.Windows.Automation.AutomationProperties.SetName(this, Loc.T("Create context"));

        var presets = new WrapPanel();
        var presetNote = Ui.Caption("Uses your SSH agent and known_hosts. tcp:// endpoints need TLS below.");
        foreach (var p in ContextEndpoint.Presets)
        {
            var preset = p;
            var b = Ui.Button(preset.Label, null, ButtonKind.Outlined, () => { hostBox.Text = preset.Value; presetNote.Text = preset.Explanation; hostBox.Focus(); hostBox.SelectAll(); });
            b.Margin = new Thickness(0, 0, 8, 8); b.ToolTip = preset.Value;
            presets.Children.Add(b);
        }
        FrameworkElement FileRow(TextBox box, string label, string title)
        {
            var browse = Ui.IconButton(Icons.FolderOpen, Loc.T("Choose") + " " + title.ToLowerInvariant(), () =>
            {
                if (PageKit.PickOpenPath(Loc.T("Choose") + " " + title.ToLowerInvariant(), "Certificates and keys|*.pem;*.crt;*.cer;*.key|All files|*.*") is { } path) box.Text = path;
            });
            return Ui.Columns(8, [-60, 1, 0], Ui.Text(label, 13, null, "OnSurfaceVariant"), box, browse);
        }
        var skip = Ui.Switch("Skip TLS verification", false, v => { skipVerify = v; Update(); }, "Accepts any server certificate. Only for testing; it allows impersonation of the engine.");
        tlsFiles = Ui.VStack(8, FileRow(ca, "CA", "CA certificate"), FileRow(cert, "Cert", "Client certificate"), FileRow(key, "Key", "Client key"), skip);
        tlsFiles.Visibility = Visibility.Collapsed;
        var tlsSwitch = Ui.Switch("TLS files", false, v => { useTls = v; tlsFiles.Visibility = v ? Visibility.Visible : Visibility.Collapsed; Update(); });
        foreach (var box in new[] { name, description, hostBox, ca, cert, key }) box.TextChanged += (_, _) => Update();

        var close = Ui.IconButton(Icons.Close, "Close create context", () => Closed?.Invoke());
        var head = Ui.Columns(8, [1, 0], Ui.Text("Create context", 20, FontWeights.SemiBold), close);
        var command = new Expander { Header = new TextBlock { Text = Loc.T("Command") }, Content = Ui.Card(preview, 12, "SurfaceHigh", 12) };

        var test = Ui.AsyncButton("Test connection", null, ButtonKind.Outlined, TestAsync);
        var create = Ui.AsyncButton("Create context", null, ButtonKind.Filled, CreateAsync);
        var fields = Ui.VStack(12,
            Ui.VStack(4, Ui.Field("Name", name, "A unique name in the docker CLI configuration. Commands select it with --context NAME.", "build-server"), nameHint),
            Ui.Field("Description", description, "Optional text shown in context lists.", "Linux build host in the lab"),
            Ui.VStack(4, Ui.Field("Docker endpoint host", hostBox, "The engine address written as host= in --docker.", "tcp://10.0.0.5:2376"), presets, presetNote),
            tlsSwitch, tlsFiles,
            testResult, command, error);
        var buttons = Ui.Columns(8, [1, 1], test, create);
        foreach (var b in buttons.Children.OfType<Button>()) b.Height = 40;
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(head);
        fields.Margin = new Thickness(0, 14, 0, 0);
        layout.Children.Add(Ui.Scroll(fields).At(1));
        buttons.Margin = new Thickness(0, 12, 0, 0);
        layout.Children.Add(buttons.At(2));
        Child = layout;
        Update();
    }

    public void FocusName() => name.Focus();

    private string Spec() => ContextEndpoint.DockerSpec(hostBox.Text, useTls ? ca.Text : null, useTls ? cert.Text : null, useTls ? key.Text : null, useTls && skipVerify);

    private List<string> Args()
    {
        if (ContextEndpoint.ValidateName(name.Text.Trim()) is { } p) throw new ArgumentException(p);
        if (description.Text.Any(char.IsControl)) throw new ArgumentException("The description cannot contain control characters.");
        var args = new List<string> { "context", "create", name.Text.Trim() };
        if (description.Text.Trim().Length > 0) args.AddRange(["--description", description.Text.Trim()]);
        args.AddRange(["--docker", Spec()]);
        return args;
    }

    private void Update()
    {
        testResult.Content = null;
        error.Text = "";
        try { preview.Text = PageKit.Preview(Args(), null); }
        catch (ArgumentException e) { preview.Text = e.Message; }
    }

    private void Reset()
    {
        name.Text = description.Text = ca.Text = cert.Text = key.Text = "";
        hostBox.Text = "npipe:////./pipe/docker_engine";
    }

    /// <summary>Runs <c>docker version</c> against the typed endpoint with a time limit. Nothing is created.</summary>
    private async Task TestAsync()
    {
        error.Text = "";
        testResult.Content = null;
        string host;
        try { _ = Spec(); host = hostBox.Text.Trim(); }
        catch (ArgumentException e) { error.Text = e.Message; return; }
        var args = new List<string> { "--host", host };
        if (useTls)
        {
            if (skipVerify) args.Add("--tls");
            else if (ca.Text.Trim().Length > 0 || cert.Text.Trim().Length > 0 || key.Text.Trim().Length > 0) args.Add("--tlsverify");
            if (ca.Text.Trim().Length > 0) args.Add("--tlscacert=" + ca.Text.Trim());
            if (cert.Text.Trim().Length > 0) args.Add("--tlscert=" + cert.Text.Trim());
            if (key.Text.Trim().Length > 0) args.Add("--tlskey=" + key.Text.Trim());
        }
        args.AddRange(["version", "--format", "{{json .Server}}"]);
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var watch = Stopwatch.StartNew();
        Core.CommandResult result;
        try { result = await ContextActions.ReadLocal(args, limit.Token); }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or IOException) { result = new Core.CommandResult(-1, "", ex.Message); }
        watch.Stop();
        var probe = ContextCardView.ParseProbe(result);
        var text = probe.Reachable
            ? Loc.T("Test connection: reached Docker Engine") + " " + probe.Version + " " + Loc.T("in") + " " + watch.ElapsedMilliseconds + " ms"
            : Loc.T("Test connection failed:") + " " + probe.Message;
        testResult.Content = Ui.Card(Ui.Columns(10, [-20, 1], Ui.Icon(probe.Reachable ? Icons.CheckCircle : Icons.Error, 20, true, probe.Reachable ? "Success" : "Error"),
            Ui.Text(text, 13, null, probe.Reachable ? "OnSurface" : "Error", true, false)), 12, "SurfaceContainer", 12);
    }

    private async Task CreateAsync()
    {
        error.Text = "";
        List<string> args;
        try { args = Args(); } catch (ArgumentException e) { error.Text = e.Message; return; }
        var result = await Host.Execute("Create context " + args[2], args);
        if (result?.Success != true) { error.Text = PageKit.Failure(result); return; }
        if (!Host.IsCapture) Host.History.Record("context", $"Created context {args[2]} for {hostBox.Text.Trim()}");
        Reset();
        await reload();
        Closed?.Invoke();
    }
}
