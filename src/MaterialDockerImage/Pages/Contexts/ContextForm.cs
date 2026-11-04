using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Create and update forms for docker contexts with endpoint presets, TLS file pickers and a live command preview.</summary>
public static class ContextForm
{
    private static AppHost Host => AppHost.Current;

    /// <summary>Endpoint editor: preset chips, host field, TLS files and skip-verify switch.</summary>
    private sealed class EndpointEditor
    {
        public readonly TextBox HostBox = Ui.Input("", "Docker endpoint", true);
        public readonly TextBox Ca = Ui.Input("", "CA certificate", true);
        public readonly TextBox Cert = Ui.Input("", "Client certificate", true);
        public readonly TextBox Key = Ui.Input("", "Client key", true);
        public bool SkipVerify;
        public event Action? Changed;
        public readonly StackPanel View;

        public EndpointEditor(string initialHost)
        {
            HostBox.Text = initialHost;
            var presets = new WrapPanel();
            var presetNote = Ui.Caption("");
            foreach (var p in ContextEndpoint.Presets)
            {
                var preset = p;
                var b = Ui.Button(preset.Label, null, ButtonKind.Outlined, () => { HostBox.Text = preset.Value; presetNote.Text = preset.Explanation; HostBox.Focus(); HostBox.SelectAll(); });
                b.Margin = new Thickness(0, 0, 8, 8); b.ToolTip = preset.Value;
                presets.Children.Add(b);
            }
            FrameworkElement FileRow(TextBox box, string title, string info)
            {
                var browse = Ui.IconButton(Icons.FolderOpen, Loc.T("Choose") + " " + title.ToLowerInvariant(), () =>
                {
                    if (PageKit.PickOpenPath(Loc.T("Choose") + " " + title.ToLowerInvariant(), "Certificates and keys|*.pem;*.crt;*.cer;*.key|All files|*.*") is { } path) box.Text = path;
                });
                box.TextChanged += (_, _) => Changed?.Invoke();
                return Ui.Field(title, Ui.Columns(8, [1, 0], box, browse), info);
            }
            var skip = Ui.Switch("Skip TLS verification", false, v => { SkipVerify = v; Changed?.Invoke(); }, "skip-tls-verify=true: accept any server certificate. Only for testing; it allows impersonation of the engine.");
            HostBox.TextChanged += (_, _) => Changed?.Invoke();
            View = Ui.VStack(12,
                Ui.Field("Docker endpoint", HostBox, "The engine address written as host= in --docker. Choose a preset and edit the host part.", "tcp://10.0.0.5:2376"),
                presets, presetNote,
                Ui.Card(Ui.VStack(12, Ui.Overline("TLS (tcp:// endpoints)"),
                    FileRow(Ca, "CA certificate", "ca=: the certificate authority that signed the engine's server certificate. The CLI copies it into the context store."),
                    FileRow(Cert, "Client certificate", "cert=: your client certificate. Choose it together with its key."),
                    FileRow(Key, "Client key", "key=: the private key of the client certificate. It is copied into the context store and never shown here."),
                    skip), 14, "SurfaceContainer", 14));
        }

        public string Spec() => ContextEndpoint.DockerSpec(HostBox.Text, Ca.Text, Cert.Text, Key.Text, SkipVerify);
    }

    public static void ShowCreate(Func<Task> reload)
    {
        var name = Ui.Input("", "Name", true);
        var description = Ui.Input("", "Description");
        var endpoint = new EndpointEditor("npipe:////./pipe/docker_engine");
        var preview = Ui.Mono("", 12, "OnSurfaceVariant", true);
        List<string> Args()
        {
            if (ContextEndpoint.ValidateName(name.Text.Trim()) is { } p) throw new ArgumentException(p);
            if (description.Text.Any(char.IsControl)) throw new ArgumentException("The description cannot contain control characters.");
            var args = new List<string> { "context", "create", name.Text.Trim() };
            if (description.Text.Trim().Length > 0) args.AddRange(["--description", description.Text.Trim()]);
            args.AddRange(["--docker", endpoint.Spec()]);
            return args;
        }
        void Update()
        {
            try { preview.Text = PageKit.Preview(Args(), null); }
            catch (ArgumentException e) { preview.Text = e.Message; }
        }
        name.TextChanged += (_, _) => Update(); description.TextChanged += (_, _) => Update(); endpoint.Changed += Update;
        var body = Ui.VStack(14,
            Ui.Field("Name", name, "A unique name in the docker CLI configuration. Commands select it with --context NAME.", "build-server"),
            Ui.Field("Description", description, "Optional text shown in context lists.", "Linux build host in the lab"),
            endpoint.View,
            Ui.Card(Ui.VStack(4, Ui.Overline("Command"), preview), 12, "SurfaceHigh", 12),
            Ui.Caption("Creating a context does not switch to it. Use \"Use in this window\" on its card afterwards."));
        PageKit.Dialog("Create context", "Adds an engine endpoint to the docker CLI configuration.", body, "Create", async () =>
        {
            List<string> args;
            try { args = Args(); } catch (ArgumentException e) { return e.Message; }
            var result = await Host.Execute("Create context " + args[2], args);
            if (result?.Success != true) return PageKit.Failure(result);
            if (!Host.IsCapture) Host.History.Record("context", $"Created context {args[2]} for {endpoint.HostBox.Text.Trim()}");
            await reload();
            return null;
        }, width: 620);
        Update();
    }

    public static void ShowUpdate(ContextEntry entry, Func<Task> reload)
    {
        var description = Ui.Input(entry.Description, "Description");
        var changeEndpoint = false;
        var endpoint = new EndpointEditor(entry.Endpoint);
        endpoint.View.IsEnabled = false;
        var toggle = Ui.Switch("Change the endpoint", false, v => { changeEndpoint = v; endpoint.View.IsEnabled = v; }, "Off keeps the current endpoint and TLS material. On replaces them with the values below.");
        var preview = Ui.Mono("", 12, "OnSurfaceVariant", true);
        List<string> Args()
        {
            if (description.Text.Any(char.IsControl)) throw new ArgumentException("The description cannot contain control characters.");
            var args = new List<string> { "context", "update", "--description", description.Text.Trim() };
            if (changeEndpoint) args.AddRange(["--docker", endpoint.Spec()]);
            args.Add(entry.Name);
            return args;
        }
        void Update() { try { preview.Text = PageKit.Preview(Args(), null); } catch (ArgumentException e) { preview.Text = e.Message; } }
        description.TextChanged += (_, _) => Update(); endpoint.Changed += Update;
        toggle.Checked += (_, _) => Update(); toggle.Unchecked += (_, _) => Update();
        var body = Ui.VStack(14,
            Ui.Field("Description", description, "Shown in context lists. Leave empty to clear it."),
            toggle, endpoint.View,
            Ui.Card(Ui.VStack(4, Ui.Overline("Command"), preview), 12, "SurfaceHigh", 12),
            entry.Name == (Host.Context ?? "") ? Ui.Caption("This window uses this context. After an endpoint change, refresh other destinations to read from the new address.") : null);
        PageKit.Dialog(Loc.T("Update context") + " " + entry.Name, "docker context update", body, "Update", async () =>
        {
            List<string> args;
            try { args = Args(); } catch (ArgumentException e) { return e.Message; }
            var result = await Host.Execute("Update context " + entry.Name, args);
            if (result?.Success != true) return PageKit.Failure(result);
            if (!Host.IsCapture) Host.History.Record("context", $"Updated context {entry.Name}" + (changeEndpoint ? $": endpoint {endpoint.HostBox.Text.Trim()}" : ": description"));
            await reload();
            return null;
        }, width: 620);
        Update();
    }
}
