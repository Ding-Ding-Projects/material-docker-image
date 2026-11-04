using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Argument assembly and execution for <see cref="RunEditor"/>. Every token is validated before it can run.</summary>
internal sealed partial class RunEditor
{
    private static string? Optional(System.Windows.Controls.TextBox box) => string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();

    private static bool Matches(string value, string pattern) => Regex.IsMatch(value, pattern, RegexOptions.None, RegexTimeout);

    private static readonly Regex SecretName = new(@"(?i)(pass(word|wd)?|secret|token|api[_-]?key|private[_-]?key|credential|auth|pwd)", RegexOptions.None, TimeSpan.FromMilliseconds(100));

    private static IEnumerable<string> SplitList(string text) => text.Split([' ', ',', ';', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Builds the complete docker argument list (without the global --context, which the host adds).</summary>
    private IReadOnlyList<string> BuildArguments()
    {
        var containerName = Optional(name);
        if (containerName != null && !Matches(containerName, @"^[a-zA-Z0-9][a-zA-Z0-9_.-]*$")) throw new ArgumentException("Container name must start with a letter or digit and use only letters, digits, underscores, periods and hyphens.");
        var host = Optional(hostname);
        if (host != null && !Matches(host, @"^[A-Za-z0-9]([A-Za-z0-9-]{0,62})(\.[A-Za-z0-9]([A-Za-z0-9-]{0,62}))*$")) throw new ArgumentException("Hostname uses letters, digits, hyphens and dots.");
        var command = StructuredInputs.Argv(argv.Text);
        if ((tty.IsChecked == true || interactive.IsChecked == true) && detach.IsChecked != true) throw new ArgumentException("Keep STDIN open and Allocate a TTY require Detach in this app. Attach to the container later from Containers.");
        if (autoRemove.IsChecked == true && restart.SelectedItem as string is { } rp && rp != "no") throw new ArgumentException("Remove when it exits cannot be combined with a restart policy other than no.");

        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in env)
        {
            var key = row.Name.Text.Trim();
            if (key.Length == 0 && row.Value.Text.Length == 0) continue;
            if (!Matches(key, @"^[A-Za-z_][A-Za-z0-9_]*$")) throw new ArgumentException($"Environment name \"{key}\" must start with a letter or underscore and use letters, digits and underscores.");
            StructuredInputs.Token(row.Value.Text);
            if (!environment.TryAdd(key, row.Value.Text)) throw new ArgumentException($"Environment name {key} appears more than once.");
        }

        var policy = restart.SelectedItem as string ?? "no";
        if (policy == "on-failure" && Optional(restartRetries) is { } retries)
        {
            if (!int.TryParse(retries, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 1) throw new ArgumentException("Maximum retries must be a positive whole number.");
            policy += ":" + n.ToString(CultureInfo.InvariantCulture);
        }
        else if (policy != "on-failure" && Optional(restartRetries) != null) throw new ArgumentException("Maximum retries applies only to the on-failure restart policy.");

        var networkName = network.SelectedItem as string is { } nw && nw != "(engine default)" ? nw : null;
        var options = new RunOptions
        {
            Image = image,
            Name = containerName,
            Detached = detach.IsChecked == true,
            RemoveOnExit = autoRemove.IsChecked == true,
            ReadOnly = readOnlyRoot.IsChecked == true,
            Environment = environment,
            Labels = StructuredInputs.Pairs(labels.Text, true),
            Network = networkName,
            RestartPolicy = policy == "no" ? null : policy,
            WorkingDirectory = Optional(workdir),
            User = Optional(user),
            EntryPoint = Optional(entrypoint),
            Cpus = StructuredInputs.Cpus(cpus.Text.Trim()),
            Memory = StructuredInputs.Memory(memory.Text.Trim()),
            Command = command,
        };
        var args = DockerService.CreateRunArguments(options).ToList();
        // CreateRunArguments ends with the image and the command; flags it does not model are inserted before them.
        args.RemoveRange(args.Count - 1 - command.Length, 1 + command.Length);

        void Flag(string flag, string value) { DockerService.ValidateOperand(value, flag); args.Add(flag); args.Add(value); }

        if (host != null) Flag("--hostname", host);
        if (init.IsChecked == true) args.Add("--init");
        if (interactive.IsChecked == true) args.Add("--interactive");
        if (tty.IsChecked == true) args.Add("--tty");

        foreach (var row in ports)
        {
            if (row.Host.Text.Trim().Length == 0 && row.Container.Text.Trim().Length == 0) continue;
            Flag("--publish", StructuredInputs.Port(row.Address.Text.Trim(), row.Host.Text.Trim(), row.Container.Text.Trim(), row.Protocol.SelectedItem as string ?? "tcp"));
        }

        if (Optional(envFile) is { } file)
        {
            var full = Path.GetFullPath(file);
            if (!File.Exists(full)) throw new ArgumentException(Loc.T("The environment file does not exist on this computer:") + " " + full);
            Flag("--env-file", full);
        }

        foreach (var row in mounts)
        {
            if (row.Source.Text.Trim().Length == 0 && row.Target.Text.Trim().Length == 0) continue;
            Flag("--mount", StructuredInputs.Mount(row.Type.SelectedItem as string ?? "bind", row.Source.Text.Trim(), row.Target.Text.Trim(), row.ReadOnly.IsChecked == true));
        }
        foreach (var row in tmpfs)
        {
            var path = row.Path.Text.Trim();
            if (path.Length == 0) continue;
            if (!path.StartsWith('/') || path.Contains(':') || path.Contains(',')) throw new ArgumentException("tmpfs path must be an absolute container path without colons or commas.");
            var opts = row.Options.Text.Trim();
            if (opts.Length > 0 && !Matches(opts, @"^[A-Za-z0-9_-]+(=[A-Za-z0-9_.-]+)?(,[A-Za-z0-9_-]+(=[A-Za-z0-9_.-]+)?)*$")) throw new ArgumentException("tmpfs options are comma-separated key or key=value entries, such as size=64m,mode=1777.");
            Flag("--tmpfs", opts.Length > 0 ? path + ":" + opts : path);
        }

        foreach (var a in SplitList(alias.Text))
        {
            if (networkName == null) throw new ArgumentException("Network aliases need a selected user-defined network.");
            if (!Matches(a, @"^[A-Za-z0-9][A-Za-z0-9_.-]*$")) throw new ArgumentException($"Network alias \"{a}\" uses letters, digits, underscores, periods and hyphens.");
            Flag("--network-alias", a);
        }

        if (StructuredInputs.Memory(memoryReservation.Text.Trim()) is { } reservation) Flag("--memory-reservation", reservation);
        if (Optional(pids) is { } pidText)
        {
            if (!int.TryParse(pidText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var limit) || (limit < 1 && limit != -1)) throw new ArgumentException("Process limit must be a positive whole number, or -1 for unlimited.");
            args.Add("--pids-limit"); args.Add(limit.ToString(CultureInfo.InvariantCulture));
        }
        if (Optional(gpus) is { } gpu) Flag("--gpus", StructuredInputs.Gpus(gpu, gpuCapabilities.Text));
        else if (Optional(gpuCapabilities) != null) throw new ArgumentException("GPU capabilities need a GPU request: enter all, a count or device IDs.");
        foreach (var device in StructuredInputs.Devices(devices.Select(row => (row.Host.Text, row.Container.Text, row.Permissions.Text)))) Flag("--device", device);
        foreach (var rule in StructuredInputs.Lines(cgroupRules.Text)) Flag("--device-cgroup-rule", StructuredInputs.DeviceCgroupRule(rule));

        foreach (var item in StructuredInputs.HealthArguments(new HealthSettings(Optional(healthCmd), Optional(healthInterval), Optional(healthTimeout), Optional(healthRetries), Optional(healthStart), Optional(healthStartInterval), noHealthcheck.IsChecked == true)))
            args.Add(item);

        if (noNewPrivileges.IsChecked == true) { args.Add("--security-opt"); args.Add("no-new-privileges:true"); }
        if (capDropAll.IsChecked == true) { args.Add("--cap-drop"); args.Add("ALL"); }
        foreach (var cap in SplitList(capAdd.Text))
        {
            if (!Matches(cap, @"^(CAP_)?[A-Za-z_]+$")) throw new ArgumentException($"Capability \"{cap}\" must be a name such as NET_BIND_SERVICE.");
            if (cap.Equals("ALL", StringComparison.OrdinalIgnoreCase) || cap.Equals("SYS_ADMIN", StringComparison.OrdinalIgnoreCase) || cap.Equals("CAP_SYS_ADMIN", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Adding {cap.ToUpperInvariant()} is close to privileged mode and is not offered by this editor.");
            Flag("--cap-add", cap.ToUpperInvariant());
        }

        if (logDriver.Text is { Length: > 0 } driver && driver != "(engine default)")
        {
            if (!Matches(driver, @"^[a-z0-9][a-z0-9_.-]*$")) throw new ArgumentException("Log driver names use lowercase letters, digits, underscores, periods and hyphens.");
            Flag("--log-driver", driver);
        }
        foreach (var pair in StructuredInputs.Pairs(logOptions.Text, true)) Flag("--log-opt", pair.Key + "=" + pair.Value);
        if (Optional(platform) is { } plat)
        {
            if (!Matches(plat, @"^[a-z0-9]+/[a-z0-9_]+(/[a-z0-9]+)?$")) throw new ArgumentException("Platform uses os/arch[/variant], for example linux/amd64.");
            Flag("--platform", plat);
        }
        if (pull.SelectedItem as string is { } pp && pp != "missing") Flag("--pull", pp);

        args.Add(image);
        foreach (var token in command) { StructuredInputs.Token(token); args.Add(token); }
        return args;
    }

    private void Update()
    {
        if (close == null) return;
        portWarning.Visibility = ports.Any(p => IPAddress.TryParse(p.Address.Text.Trim(), out var ip) && (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))) ? Visibility.Visible : Visibility.Collapsed;
        envWarning.Visibility = env.Any(e => e.Name.Text.Length > 0 && SecretName.IsMatch(e.Name.Text)) ? Visibility.Visible : Visibility.Collapsed;
        hardwareWarning.Visibility = Optional(gpus) != null || devices.Any(d => d.Host.Text.Trim().Length > 0) || Optional(cgroupRules) != null ? Visibility.Visible : Visibility.Collapsed;
        SetCount("Ports", ports.Count);
        SetCount("Environment", env.Count + (Optional(envFile) != null ? 1 : 0));
        SetCount("Mounts", mounts.Count + tmpfs.Count);
        SetCount("Security", (noNewPrivileges.IsChecked == true ? 1 : 0) + (readOnlyRoot.IsChecked == true ? 1 : 0) + (capDropAll.IsChecked == true ? 1 : 0));
        try
        {
            lastArgs = BuildArguments();
            preview.Text = PageKit.Preview(lastArgs, context);
            problem.Text = "";
        }
        catch (Exception e) when (e is ArgumentException or IOException)
        {
            lastArgs = null;
            problem.Text = e.Message;
        }
    }

    private void SetCount(string key, int count)
    {
        if (navCounts.TryGetValue(key, out var text)) text.Text = count > 0 ? count.ToString(CultureInfo.InvariantCulture) : "";
    }

    private async Task RunAsync()
    {
        if (!Host.IsCurrent(context, generation)) { problem.Text = PageKit.StaleMessage(context) + " Close this editor and refresh."; return; }
        IReadOnlyList<string> args;
        try { args = BuildArguments(); }
        catch (Exception e) when (e is ArgumentException or IOException) { problem.Text = e.Message; return; }
        problem.Text = "";
        resultText.Text = Loc.T("Creating the container on context ") + (context ?? "default") + Loc.T(". Waiting for the actual process result.");
        var result = await Host.Execute("Run container from " + image, args, boundContext: context);
        if (result?.Success == true)
        {
            var output = result.StandardOutput.Trim();
            var id = detach.IsChecked == true && Regex.IsMatch(output, "^[0-9a-f]{12,64}$", RegexOptions.None, RegexTimeout) ? output[..12] : null;
            Host.Notify.Success(id != null ? $"Container {id} created from {image}." : $"The container from {image} finished with exit code 0.");
            close?.Invoke();
            if (afterSuccess != null) await afterSuccess();
        }
        else
        {
            resultText.Text = Loc.T("The container was not created.") + " " + PageKit.Failure(result) + " The output panel has the full transcript." + (ContainerInspectFacts.HardwareFailureNote(result?.StandardError) is { Length: > 0 } hardwareNote ? " " + Loc.T(hardwareNote) : "");
        }
    }
}
