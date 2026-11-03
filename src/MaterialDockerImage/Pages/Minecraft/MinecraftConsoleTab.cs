using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Live server console: follows docker logs in a searchable viewer and sends commands through rcon-cli (Java) or
/// send-command (Bedrock), with Up/Down command history and quick commands.
/// </summary>
internal sealed class MinecraftConsoleTab : MinecraftTab
{
    private readonly ResourceTextViewer viewer = new("Server console", false, 20000, 240);
    private readonly TextBox input = Ui.Input("", "Server command", true);
    private readonly TextBlock state = Ui.Caption("");
    private readonly List<string> history = new();
    private readonly StringBuilder pending = new();
    private readonly System.Windows.Threading.DispatcherTimer flush = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly Button send;
    private CancellationTokenSource? stream;
    private int historyIndex;
    private string? streamingId;

    public MinecraftConsoleTab(MinecraftServerDetail detail) : base(detail)
    {
        flush.Tick += (_, _) => Drain();
        send = Ui.AsyncButton("Send", Icons.ArrowForward, ButtonKind.Filled, SendAsync);
        input.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; await Ui.RunGuarded(send, SendAsync); }
            else if (e.Key == Key.Up && history.Count > 0) { historyIndex = Math.Max(0, historyIndex - 1); input.Text = history[historyIndex]; input.CaretIndex = input.Text.Length; e.Handled = true; }
            else if (e.Key == Key.Down && history.Count > 0) { historyIndex = Math.Min(history.Count, historyIndex + 1); input.Text = historyIndex < history.Count ? history[historyIndex] : ""; input.CaretIndex = input.Text.Length; e.Handled = true; }
        };
        var quick = new WrapPanel();
        void Quick(string label, string command, bool needsArgument)
        {
            var b = Ui.Button(label, null, ButtonKind.Outlined, () =>
            {
                if (needsArgument) { input.Text = command; input.Focus(); input.CaretIndex = input.Text.Length; }
                else { input.Text = command; _ = Ui.RunGuarded(send, SendAsync); }
            }, false);
            b.ToolTip = needsArgument ? Loc.T("Inserts the command; complete it and press Enter.") : Loc.T("Sends: ") + command;
            b.Margin = new Thickness(0, 0, 8, 8);
            quick.Children.Add(b);
        }
        Quick("list", "list", false);
        Quick("say…", "say ", true);
        Quick("time set day", "time set day", false);
        Quick("weather clear", "weather clear", false);
        Quick("save-all", "save-all", false);
        Quick("whitelist add…", "whitelist add ", true);
        Quick("op…", "op ", true);
        var reconnect = Ui.IconButton(Icons.Sync, "Reconnect log stream", () => StartStream(true));
        var bar = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        bar.Children.Add(send.Dock(Dock.Right));
        input.Margin = new Thickness(0, 0, 8, 0);
        bar.Children.Add(input);
        var footer = Ui.VStack(6, bar, quick, Ui.Columns(8, [1, 0], state, reconnect));
        Children.Add(footer.Dock(Dock.Bottom));
        Children.Add(viewer);
    }

    public override void OnShown()
    {
        var s = Server;
        input.IsEnabled = s.IsRunning && (!s.IsJava || s.Spec.RconEnabled);
        input.ToolTip = s.IsJava ? Loc.T("Runs: docker exec <container> rcon-cli <command>. Up and Down recall earlier commands.") : Loc.T("Runs: docker exec <container> send-command <command>. Bedrock replies appear in the log above.");
        if (streamingId != s.Id || stream == null) StartStream(false);
        UpdateState();
    }

    public override void OnHidden() { stream?.Cancel(); stream = null; flush.Stop(); }

    private void UpdateState()
    {
        var s = Server;
        state.Text = !s.IsRunning ? Loc.T("The server is not running: logs show the last run; commands need a running server.")
            : s.IsJava && !s.Spec.RconEnabled ? Loc.T("RCON is off for this server, so commands cannot be sent. Turn it on in Properties.")
            : s.IsJava ? Loc.T("Commands go through RCON; replies appear in the console as ‹ lines.") : Loc.T("Commands go to the server console; Bedrock replies appear in the log stream.");
    }

    private void StartStream(bool clear)
    {
        stream?.Cancel();
        var s = Server;
        streamingId = s.Id;
        if (clear || viewer.AllText.Length == 0) viewer.SetText("");
        var cts = new CancellationTokenSource();
        stream = cts;
        flush.Start();
        _ = Run();
        async Task Run()
        {
            var progress = new Progress<DockerOutput>(o => { if (stream == cts) pending.Append(o.Text); });
            try
            {
                var r = await Host.Docker.ExecuteAsync(["container", "logs", "--follow", "--tail", "300", s.Id], Context, progress, cts.Token);
                await Task.Delay(250);
                if (stream != cts) return;
                Drain();
                viewer.Append(r.Cancelled ? "— log stream stopped —" : r.Success ? "— log stream ended (the container stopped) —" : "— docker logs failed: " + MinecraftOps.Trim(r) + " —");
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException or IOException)
            {
                viewer.Append("— could not start the log stream: " + e.Message + " —");
            }
            finally { if (stream == cts) stream = null; cts.Dispose(); }
        }
    }

    private void Drain()
    {
        if (pending.Length == 0) return;
        var text = pending.ToString();
        pending.Clear();
        viewer.Append(text);
    }

    private async Task SendAsync()
    {
        var command = input.Text.Trim().TrimStart('/');
        if (command.Length == 0) return;
        if (!EnsureCurrent()) return;
        if (history.Count == 0 || history[^1] != command) history.Add(command);
        historyIndex = history.Count;
        input.Clear();
        Drain();
        viewer.Append("› " + command);
        var r = await MinecraftOps.CommandAsync(Server, command, Context);
        if (r.Success)
        {
            var reply = MinecraftOutput.StripFormatting(r.StandardOutput).TrimEnd();
            if (reply.Length > 0) viewer.Append(string.Join("\n", reply.Replace("\r\n", "\n").Split('\n').Select(l => "‹ " + l)));
            else if (Server.IsJava) viewer.Append("‹ (no reply)");
        }
        else viewer.Append("‹ failed: " + MinecraftOps.Trim(r));
    }
}
