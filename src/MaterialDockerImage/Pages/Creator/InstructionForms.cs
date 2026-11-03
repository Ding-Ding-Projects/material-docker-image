using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Builds the inline structured form for one instruction. Every edit writes straight into the instruction and calls
/// <c>changed</c>. Validation messages from <see cref="DockerfileModel.Validate"/> are shown under the form.
/// </summary>
public sealed class InstructionForm : StackPanel
{
    private readonly DockerInstruction instruction;
    private readonly Action changed;
    private readonly Func<IReadOnlyList<string>> earlierStages;
    private readonly StackPanel fields = new();
    private readonly TextBlock problems = Ui.Text("", 13, null, "Error");

    public static readonly string[] Signals = ["SIGTERM", "SIGINT", "SIGQUIT", "SIGHUP", "SIGUSR1", "SIGUSR2", "SIGKILL"];

    public InstructionForm(DockerInstruction instruction, Func<IReadOnlyList<string>> earlierStages, Action changed)
    {
        this.instruction = instruction; this.earlierStages = earlierStages;
        this.changed = () => { changed(); ShowProblems(); };
        Children.Add(fields);
        problems.Margin = new Thickness(0, 10, 0, 0);
        Children.Add(problems);
        Build();
    }

    private void ShowProblems()
    {
        var list = DockerfileModel.Validate(instruction);
        problems.Text = string.Join(Environment.NewLine, list);
        problems.Visibility = list.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Build()
    {
        fields.Children.Clear();
        foreach (var element in Fields()) Ui.Add(fields, element, 12);
        ShowProblems();
    }

    private TextBox Text(string value, string label, Action<string> set, bool mono = true)
    {
        var box = Ui.Input(value, label, mono);
        box.TextChanged += (_, _) => { set(box.Text); changed(); };
        return box;
    }

    /// <summary>Editable combo bound through get/set; typing and picking both update the instruction.</summary>
    private ComboBox EditableCombo(IEnumerable<string> items, Func<string> get, string label, Action<string> set)
    {
        var combo = Ui.Combo(items, get(), true);
        combo.Text = get();
        combo.SetResourceReference(Control.FontFamilyProperty, "MonoFont");
        System.Windows.Automation.AutomationProperties.SetName(combo, Loc.T(label));
        void Sync(string? text) { text ??= ""; if (text == get()) return; set(text); changed(); }
        combo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Sync(combo.Text)));
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string s) Sync(s); };
        return combo;
    }

    private FrameworkElement Mode(bool exec, string shellLabel, Action<bool> set)
    {
        string[] names = [shellLabel, "Exec form (JSON)"];
        return Ui.Field("Form", Ui.Segmented(names, exec ? names[1] : names[0], option =>
        {
            set(option == names[1]);
            changed();
            Dispatcher.BeginInvoke(Build);
        }), "Exec form runs the program directly and passes stop signals to it. Shell form runs through /bin/sh -c and allows pipes, variables and &&.", "Exec: [\"node\", \"server.js\"]\nShell: npm run start && echo done");
    }

    private IEnumerable<UIElement> Fields()
    {
        var i = instruction;
        switch (i.Kind)
        {
            case InstructionKind.Arg:
                yield return Ui.Columns(12, [1, 1],
                    Ui.Field("Name", Text(i.Name, "Name", v => i.Name = v), "The build argument name. Pass a value with --build-arg NAME=value.", "NODE_VERSION"),
                    Ui.Field("Default value", Text(i.Value, "Default value", v => i.Value = v), "Used when the build does not pass a value.", "22"));
                yield return Ui.Switch("Has a default value", i.HasValue, v => { i.HasValue = v; changed(); });
                break;
            case InstructionKind.Env:
                yield return Ui.Field("Variables", new KeyValueRows(i.Pairs, "Name", "Value", "Add variable", n => DockerfileModel.IsVariableName(n) ? null : "Use letters, digits and underscores, starting with a letter or underscore.", changed),
                    "Environment variables set in the image and every container started from it. Never put secrets here.", "NODE_ENV = production");
                break;
            case InstructionKind.Label:
                yield return Ui.Field("Labels", new KeyValueRows(i.Pairs, "Key", "Value", "Add label", BuildOptionsPanel.ValidateLabelKey, changed),
                    "Image metadata. OCI keys such as org.opencontainers.image.source are understood by registries.", "org.opencontainers.image.source = https://example.com/repo");
                break;
            case InstructionKind.Workdir:
                yield return Ui.Field("Directory", Text(i.Value, "Directory", v => i.Value = v), "Working directory for later instructions and the container. Created if missing. Use an absolute path.", "/app");
                break;
            case InstructionKind.User:
                yield return Ui.Field("User", EditableCombo(["node", "app", "10001", "10001:10001", "nobody", "root"], () => i.Value, "User", v => i.Value = v), "User name or UID (optionally :group) for later RUN steps and the container. The user must exist in the image.", "10001:10001");
                break;
            case InstructionKind.StopSignal:
                yield return Ui.Field("Signal", EditableCombo(Signals, () => i.Value, "Signal", v => i.Value = v), "Signal docker stop sends to the main process.", "SIGTERM");
                break;
            case InstructionKind.OnBuild:
                yield return Ui.Field("Instruction", Text(i.Value, "Instruction", v => i.Value = v), "A complete instruction that runs when another Dockerfile uses this image in FROM.", "COPY . /app");
                break;
            case InstructionKind.Copy:
            case InstructionKind.Add:
                yield return Ui.Field("Sources", new ListRows(i.Sources, "Source", "Add source", changed), i.Kind == InstructionKind.Copy ? "Paths in the build context, or in the source stage when From stage is set. Wildcards are allowed." : "Local paths, URLs or git repositories. Local tar archives are extracted.", "package.json\npackage-lock.json");
                yield return Ui.Field("Destination", Text(i.Destination, "Destination", v => i.Destination = v), "Path inside the image. End with / to copy into a directory.", "./");
                if (i.Kind == InstructionKind.Copy)
                    yield return Ui.Field("From stage", EditableCombo(new[] { "" }.Concat(earlierStages()), () => i.From, "From stage", v => i.From = v), "Copy from an earlier stage (by name) or an image instead of the build context.", "build");
                yield return Ui.Columns(12, [1, 1],
                    Ui.Field("Owner", Text(i.Chown, "Owner", v => i.Chown = v), "Sets owner (and group) of the copied files (--chown).", "node:node"),
                    Ui.Field("Permissions", Text(i.Chmod, "Permissions", v => i.Chmod = v), "Octal mode for the copied files (--chmod).", "755"));
                yield return Ui.Switch("Link layer (--link)", i.Link, v => { i.Link = v; changed(); }, "Copies into an independent layer so later base image changes do not invalidate it.");
                break;
            case InstructionKind.Run:
                yield return Mode(i.ExecForm, "Shell command", exec =>
                {
                    if (exec && !i.ExecForm) i.Arguments = DockerfileModel.SimpleArgv(i.Command) ?? (i.Command.Trim().Length > 0 ? ["/bin/sh", "-c", i.Command.Trim()] : []);
                    if (!exec && i.ExecForm) i.Command = string.Join(" ", i.Arguments.Select(a => a.Any(char.IsWhiteSpace) ? DockerfileModel.QuoteValue(a) : a));
                    i.ExecForm = exec;
                });
                if (i.ExecForm) yield return Ui.Field("Arguments", new ListRows(i.Arguments, "Argument", "Add argument", changed), "Program and arguments, one per row. No shell processing happens.", "/usr/local/bin/setup\n--quiet");
                else
                {
                    var command = Ui.CodeBox(i.Command, 72);
                    command.TextChanged += (_, _) => { i.Command = command.Text; changed(); };
                    yield return Ui.Field("Command", command, "Shell command. For several lines end each line with a backslash, or join commands with &&.", "apt-get update && \\\n    apt-get install -y --no-install-recommends curl && \\\n    rm -rf /var/lib/apt/lists/*");
                }
                yield return Ui.Field("Cache mounts", new ListRows(i.CacheTargets, "Cache target", "Add cache mount", changed), "Directories BuildKit keeps between builds (--mount=type=cache). Use package manager caches so reinstalling is fast.", "/root/.npm\n/root/.cache/pip\n/go/pkg/mod");
                yield return Ui.Field("Secret mounts", new ListRows(i.SecretIds, "Secret id", "Add secret mount", changed), "Secret ids made available to this step only (--mount=type=secret). Provide the file in Build under Labels, cache and secrets. Secrets are not stored in the image.", "npmrc");
                break;
            case InstructionKind.Expose:
                yield return Ui.Columns(12, [1, 1],
                    Ui.Field("Port", Text(i.Port, "Port", v => i.Port = v), "Port the service listens on. Documentation only; publish it when you run the container.", "8080"),
                    Ui.Field("Protocol", Ui.Segmented(["tcp", "udp"], i.Protocol.Trim().Length == 0 ? "tcp" : i.Protocol.Trim().ToLowerInvariant(), v => { i.Protocol = v; changed(); }, false)));
                break;
            case InstructionKind.Volume:
                yield return Ui.Field("Mount points", new ListRows(i.Sources, "Path", "Add path", changed), "Paths that hold data outside the container's writable layer. Each run gets an anonymous volume unless you mount one.", "/data");
                break;
            case InstructionKind.Entrypoint:
            case InstructionKind.Cmd:
                yield return Mode(i.ExecForm, "Shell form", exec =>
                {
                    if (exec && !i.ExecForm) i.Arguments = DockerfileModel.SimpleArgv(i.Command) ?? (i.Command.Trim().Length > 0 ? ["/bin/sh", "-c", i.Command.Trim()] : []);
                    if (!exec && i.ExecForm) i.Command = string.Join(" ", i.Arguments.Select(a => a.Any(char.IsWhiteSpace) ? DockerfileModel.QuoteValue(a) : a));
                    i.ExecForm = exec;
                });
                if (i.ExecForm) yield return Ui.Field("Arguments", new ListRows(i.Arguments, "Argument", "Add argument", changed),
                    i.Kind == InstructionKind.Cmd ? "Default command, or default arguments for ENTRYPOINT. Replaced by arguments given to docker run." : "The program the container always runs. Arguments to docker run are appended.", "node\ndist/server.js");
                else yield return Ui.Field("Command", Text(i.Command, "Command", v => i.Command = v), "Runs through /bin/sh -c.", "node dist/server.js");
                break;
            case InstructionKind.Healthcheck:
                yield return Ui.Switch("Disable the inherited health check (NONE)", i.Disabled, v => { i.Disabled = v; changed(); Dispatcher.BeginInvoke(Build); });
                if (i.Disabled) break;
                yield return Ui.Columns(12, [1, 1],
                    Ui.Field("Interval", Text(i.Interval, "Interval", v => i.Interval = v), "Time between checks.", "30s"),
                    Ui.Field("Timeout", Text(i.Timeout, "Timeout", v => i.Timeout = v), "A check slower than this counts as failed.", "5s"));
                yield return Ui.Columns(12, [1, 1],
                    Ui.Field("Start period", Text(i.StartPeriod, "Start period", v => i.StartPeriod = v), "Failures during start-up do not count.", "10s"),
                    Ui.Field("Retries", Text(i.Retries, "Retries", v => i.Retries = v), "Consecutive failures before the container is unhealthy.", "3"));
                yield return Mode(i.ExecForm, "Shell form", exec =>
                {
                    if (exec && !i.ExecForm) i.Arguments = DockerfileModel.SimpleArgv(i.Command) ?? (i.Command.Trim().Length > 0 ? ["/bin/sh", "-c", i.Command.Trim()] : []);
                    if (!exec && i.ExecForm) i.Command = string.Join(" ", i.Arguments.Select(a => a.Any(char.IsWhiteSpace) ? DockerfileModel.QuoteValue(a) : a));
                    i.ExecForm = exec;
                });
                if (i.ExecForm) yield return Ui.Field("Arguments", new ListRows(i.Arguments, "Argument", "Add argument", changed), "Check program and arguments. Exit code 0 means healthy.", "/app/healthcheck");
                else yield return Ui.Field("Command", Text(i.Command, "Command", v => i.Command = v), "Exit code 0 means healthy. The tool you call (wget, curl) must exist in the image.", "wget -qO- http://localhost:8080/health || exit 1");
                break;
            case InstructionKind.Shell:
                yield return Ui.Field("Shell and arguments", new ListRows(i.Arguments, "Argument", "Add argument", changed), "Shell used by later shell-form RUN, CMD and ENTRYPOINT.", "/bin/bash\n-o\npipefail\n-c");
                break;
            case InstructionKind.Comment:
            {
                var text = Ui.CodeBox(i.Value, 56);
                text.TextChanged += (_, _) => { i.Value = text.Text; changed(); };
                yield return Ui.Field("Comment", text, "Each line becomes a # comment line.", "Install production dependencies only");
                break;
            }
            case InstructionKind.Raw:
            {
                var text = Ui.CodeBox(i.Value, 72);
                text.TextChanged += (_, _) => { i.Value = text.Text; changed(); };
                yield return Ui.Caption("This source was kept exactly as written because the Creator does not model it. It is rendered unchanged.");
                yield return Ui.Field("Source", text);
                break;
            }
        }
    }
}
