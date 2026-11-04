using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>One-shot command runner (docker container exec) plus a launcher for an interactive terminal in a separate console window.</summary>
internal sealed class ContainerExec : StackPanel
{
    private static AppHost Host => AppHost.Current;
    private readonly ContainersPage page;
    private readonly TextBox command = Ui.Input("", "Command", true);
    private readonly ComboBox mode = Ui.Combo(["Simple command", "Argv JSON"], "Simple command");
    private readonly TextBox user = Ui.Input("", "User");
    private readonly TextBox workdir = Ui.Input("", "Working directory", true);
    private readonly ComboBox shell = Ui.Combo(["sh", "bash", "ash", "/bin/sh", "powershell", "cmd"], "sh", true);
    private readonly TextBlock argvPreview = Ui.Caption("");
    private readonly SearchableText output = new("Command output", "exec-output.txt", "Search output") { Height = 260 };
    private ContainerRow? container;

    public ContainerExec(ContainersPage page)
    {
        this.page = page;
        System.Windows.Automation.AutomationProperties.SetName(mode, Loc.T("Command format"));
        command.TextChanged += (_, _) => PreviewArgv();
        mode.SelectionChanged += (_, _) => PreviewArgv();
        command.KeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; await Ui.RunGuarded(command, RunAsync); } };
        var run = Ui.AsyncButton("Run command", Icons.PlayArrow, ButtonKind.Filled, RunAsync);
        var terminal = Ui.Button("Open terminal", Icons.OpenInNew, ButtonKind.Tonal, OpenTerminal);
        Children.Add(Ui.VStack(12,
            Ui.Text("Run one command", 15, FontWeights.SemiBold),
            Ui.Caption("Runs docker container exec once and shows the complete output here. No shell is involved on this computer: Simple command splits on spaces and honours quotes; pipes and redirections are passed literally. To use shell features, run sh -c \"…\" inside the container."),
            Ui.Columns(8, [-160, 1], Ui.Field("Format", mode), Ui.Field("Command", command, "Simple: words separated by spaces, quotes group words. Argv JSON: an explicit array of strings.", "ls -la /app\n[\"sh\", \"-c\", \"ps aux | head\"]")),
            argvPreview,
            Ui.Columns(12, [1, 1], Ui.Field("User", user, "--user runs the command as this user or UID[:GID]. Empty uses the container user.", "root"), Ui.Field("Working directory", workdir, "--workdir for this command only.", "/app")),
            run,
            output,
            Ui.Divider(),
            Ui.Text("Interactive terminal", 15, FontWeights.SemiBold),
            Ui.Caption("Opens a separate console window running docker exec -it with the shell below. It is a real terminal outside this app; close that window or type exit to end the session."),
            Ui.Columns(8, [-200, 0], Ui.Field("Shell", shell, "The program to start interactively. Use one that exists in the image (Alpine has sh and ash, Debian-based images have bash).", "bash"), terminal.Margin(0, 22, 0, 0))));
        PreviewArgv();
    }

    public void SetContainer(ContainerRow? row) { container = row; }

    private string[] Parse() => mode.SelectedItem as string == "Argv JSON" ? StructuredInputs.Argv(command.Text) : PageKit.SplitCommand(command.Text);

    private void PreviewArgv()
    {
        try
        {
            var argv = Parse();
            argvPreview.Text = argv.Length == 0 ? "Enter a command." : "argv: " + System.Text.Json.JsonSerializer.Serialize(argv, new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            argvPreview.SetResourceReference(TextBlock.ForegroundProperty, "OnSurfaceVariant");
        }
        catch (ArgumentException e)
        {
            argvPreview.Text = e.Message;
            argvPreview.SetResourceReference(TextBlock.ForegroundProperty, "Error");
        }
    }

    private async Task RunAsync()
    {
        if (container == null || !page.EnsureCurrent()) return;
        if (!container.IsRunning) { Host.Notify.Warning("Commands can run only in a running container. Start or unpause it first."); return; }
        var argv = Parse();
        if (argv.Length == 0) { Host.Notify.Warning("Enter a command to run."); return; }
        DockerService.ValidateOperand(argv[0], "command");
        var args = new List<string> { "container", "exec" };
        if (user.Text.Trim() is { Length: > 0 } u) { DockerService.ValidateOperand(u, "user"); args.AddRange(["--user", u]); }
        if (workdir.Text.Trim() is { Length: > 0 } w) { DockerService.ValidateOperand(w, "working directory"); args.AddRange(["--workdir", w]); }
        args.Add(container.Id);
        args.AddRange(argv);
        var result = await Host.Execute($"Exec in {container.Name}: {argv[0]}", args, boundContext: page.Context);
        if (result == null) return;
        var text = result.StandardOutput + (result.StandardError.Length > 0 ? (result.StandardOutput.Length > 0 ? "\n" : "") + "[stderr]\n" + result.StandardError : "");
        output.FileName = PageKit.SafeFileName(container.Name) + "-exec.txt";
        output.SetText(text + $"\n[exit code {result.ExitCode}{(result.Truncated ? ", output truncated at the capture limit" : "")}]");
    }

    private void OpenTerminal()
    {
        if (container == null || !page.EnsureCurrent()) return;
        if (!container.IsRunning) { Host.Notify.Warning("An interactive terminal needs a running container."); return; }
        var program = shell.Text.Trim();
        if (program.Length == 0) program = "sh";
        DockerService.ValidateOperand(program, "shell");
        if (program.Any(c => char.IsWhiteSpace(c) || c is '"' or '&' or '|' or '<' or '>' or '^')) { Host.Notify.Error("The shell must be a single program name or path without spaces or shell characters."); return; }
        // Every token is validated to contain no whitespace or quotes, so a plain space-joined argument string is exact.
        var tokens = new List<string>();
        if (page.Context is { Length: > 0 } ctx) { DockerService.ValidateOperand(ctx, "context"); tokens.AddRange(["--context", ctx]); }
        tokens.AddRange(["exec", "-it", container.Id, program]);
        if (tokens.Any(t => t.Any(c => char.IsWhiteSpace(c) || c == '"'))) { Host.Notify.Error("The engine context name contains characters that cannot be passed to a console window."); return; }
        var start = new ProcessStartInfo("docker", string.Join(" ", tokens)) { UseShellExecute = true };
        try
        {
            Process.Start(start)?.Dispose();
            Host.Notify.Info($"Opened a separate console window with {program} in {container.Name}. Type exit there to end the session.");
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            Host.Notify.Error(Loc.T("The terminal could not be opened:") + " " + e.Message + ". Check that docker is installed and on PATH.");
        }
    }
}
