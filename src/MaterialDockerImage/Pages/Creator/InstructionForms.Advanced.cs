using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Forms for the modern instruction options: RUN mounts, network, security and heredocs; COPY and ADD flags; ONBUILD.</summary>
public sealed partial class InstructionForm
{
    private AddSourceKind? addKind;

    private static readonly InstructionKind[] OnBuildKinds =
    [
        InstructionKind.Run, InstructionKind.Copy, InstructionKind.Add, InstructionKind.Env, InstructionKind.Arg, InstructionKind.Workdir, InstructionKind.User,
        InstructionKind.Expose, InstructionKind.Label, InstructionKind.Volume, InstructionKind.Cmd, InstructionKind.Entrypoint, InstructionKind.Healthcheck,
        InstructionKind.StopSignal, InstructionKind.Shell,
    ];

    private void Rebuild() => Dispatcher.BeginInvoke(Build);

    private IEnumerable<UIElement> RunFields(DockerInstruction i)
    {
        DockerfileModel.MigrateLegacyMounts(i);
        yield return Ui.Switch("Use heredoc", i.Heredoc, on =>
        {
            if (on && !i.Heredoc)
            {
                if (i.HeredocBody.Trim().Length == 0) i.HeredocBody = ScriptFromCommand(i);
                if (i.HeredocDelimiter.Trim().Length == 0) i.HeredocDelimiter = "EOF";
            }
            if (!on && i.Heredoc && i.Command.Trim().Length == 0 && !i.ExecForm) i.Command = CommandFromScript(i.HeredocBody);
            i.Heredoc = on;
            changed();
            Rebuild();
        }, "Writes the step as a multi-line script (RUN <<EOF … EOF) instead of joining commands with && and backslashes.");
        if (i.Heredoc)
        {
            var script = Ui.CodeBox(i.HeredocBody, 110);
            script.TextChanged += (_, _) => { i.HeredocBody = script.Text; changed(); };
            yield return Ui.Field("Script", script, "Each line runs in order. Start with set -e (or a shebang such as #!/usr/bin/env python3) so a failing line stops the build.", "set -ex\napt-get update\napt-get install -y --no-install-recommends curl");
            yield return Ui.Columns(12, [1, 1],
                Ui.Field("Interpreter", Text(i.Interpreter, "Interpreter", v => i.Interpreter = v), "Program that reads the script, written after the delimiter. Empty runs the script with the default shell, or with the interpreter named in its shebang line.", "bash\npython3"),
                Ui.Field("Delimiter", Text(i.HeredocDelimiter, "Delimiter", v => i.HeredocDelimiter = v), "Word that ends the script. It must not appear alone on a line inside the script.", "EOF"));
            yield return Ui.Switch("Do not expand variables (quoted delimiter)", i.HeredocQuoted, v => { i.HeredocQuoted = v; changed(); }, "Writes <<\"EOF\" so $VARIABLES in the script are left for the shell at run time instead of being replaced during the build.");
        }
        else
        {
            yield return ModeFor(i, "Shell command");
            if (i.ExecForm) yield return Ui.Field("Arguments", new ListRows(i.Arguments, "Argument", "Add argument", changed), "Program and arguments, one per row. No shell processing happens.", "/usr/local/bin/setup\n--quiet");
            else
            {
                var command = Ui.CodeBox(i.Command, 72);
                command.TextChanged += (_, _) => { i.Command = command.Text; changed(); };
                yield return Ui.Field("Command", command, "Shell command. For several lines end each line with a backslash, join commands with &&, or switch on Use heredoc.", "apt-get update && \\\n    apt-get install -y --no-install-recommends curl && \\\n    rm -rf /var/lib/apt/lists/*");
            }
        }
        yield return Ui.Field("Mounts", new CreatorMountEditor(i.Mounts, changed),
            "Filesystems available to this step only (--mount). cache keeps package downloads between builds, bind reads files from the context or another stage without copying them, tmpfs gives scratch space, secret and ssh pass credentials that never enter the image.",
            "type=cache,target=/root/.npm\ntype=bind,source=package.json,target=package.json\ntype=secret,id=npmrc,target=/root/.npmrc");
        var networkNames = new[] { "Default", "None", "Host" };
        var network = i.Network.Trim().ToLowerInvariant() switch { "none" => "None", "host" => "Host", _ => "Default" };
        var networkNote = Ui.Caption(i.Network.Trim() == "host" ? "Host networking needs the network.host entitlement: start the builder with it and build with --allow network.host." : "");
        networkNote.Visibility = i.Network.Trim() == "host" ? Visibility.Visible : Visibility.Collapsed;
        yield return Ui.Field("Network", Ui.VStack(6, Ui.Segmented(networkNames, network, v =>
        {
            i.Network = v switch { "None" => "none", "Host" => "host", _ => i.Network.Trim() == "default" ? "default" : "" };
            changed();
            Rebuild();
        }), networkNote), "Network for this step (--network). None blocks downloads so the step can only use files already in the build; Host uses the build machine's network.", "--network=none pip install --find-links wheels app");
        var insecure = string.Equals(i.Security.Trim(), "insecure", StringComparison.OrdinalIgnoreCase);
        var warning = Ui.Card(Ui.Columns(10, [0, 1], Ui.Icon(Icons.Warning, 20, true, "OnWarningContainer"),
            Ui.Text("Insecure mode runs this step without the sandbox, like docker run --privileged. The builder must allow the security.insecure entitlement and the build must pass --allow security.insecure; add a comment above the step that says why it is needed.", 13, null, "OnWarningContainer")), 12, "WarningContainer", 12);
        warning.Visibility = insecure ? Visibility.Visible : Visibility.Collapsed;
        yield return Ui.Field("Security", Ui.VStack(8, Ui.Segmented(["Sandbox", "Insecure"], insecure ? "Insecure" : "Sandbox", v =>
        {
            i.Security = v == "Insecure" ? "insecure" : i.Security.Trim() == "sandbox" ? "sandbox" : "";
            changed();
            Rebuild();
        }), warning), "Security mode for this step (--security). Sandbox is the default.", "--security=insecure");
    }

    /// <summary>The shell/exec segmented control shared by RUN, CMD, ENTRYPOINT and HEALTHCHECK.</summary>
    private FrameworkElement ModeFor(DockerInstruction i, string shellLabel) => Mode(i.ExecForm, shellLabel, exec =>
    {
        if (exec && !i.ExecForm) i.Arguments = DockerfileModel.SimpleArgv(i.Command) ?? (i.Command.Trim().Length > 0 ? ["/bin/sh", "-c", i.Command.Trim()] : []);
        if (!exec && i.ExecForm) i.Command = string.Join(" ", i.Arguments.Select(a => a.Any(char.IsWhiteSpace) ? DockerfileModel.QuoteValue(a) : a));
        i.ExecForm = exec;
    });

    /// <summary>Turns "a && \ b" into one command per line for the heredoc editor.</summary>
    private static string ScriptFromCommand(DockerInstruction i)
    {
        var command = i.ExecForm ? string.Join(" ", i.Arguments) : i.Command;
        var joined = Regex.Replace(command.Replace("\r\n", "\n"), @"\\\s*\n\s*", " ", RegexOptions.None, TimeSpan.FromMilliseconds(100));
        var lines = joined.Split("&&").Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        return lines.Count == 0 ? "set -e\n" : "set -e\n" + string.Join("\n", lines);
    }

    /// <summary>Joins heredoc lines back into one shell command with &&.</summary>
    private static string CommandFromScript(string script)
    {
        var lines = script.Replace("\r\n", "\n").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith('#') && x != "set -e" && x != "set -ex").ToList();
        return string.Join(" && \\\n    ", lines);
    }

    private IEnumerable<UIElement> CopyFields(DockerInstruction i)
    {
        var isCopy = i.Kind == InstructionKind.Copy;
        if (isCopy)
        {
            yield return Ui.Switch("Inline file (heredoc)", i.Heredoc, on => { i.Heredoc = on; if (on && i.HeredocDelimiter.Trim().Length == 0) i.HeredocDelimiter = "EOF"; changed(); Rebuild(); },
                "Writes the file content directly in the Dockerfile (COPY <<EOF /path … EOF) instead of copying it from the context.");
        }
        if (isCopy && i.Heredoc)
        {
            var body = Ui.CodeBox(i.HeredocBody, 96);
            body.TextChanged += (_, _) => { i.HeredocBody = body.Text; changed(); };
            yield return Ui.Field("File content", body, "Text written to the destination file. ${VARIABLES} are expanded during the build unless the delimiter is quoted.", "#!/bin/sh\nexec node server.js");
            yield return Ui.Columns(12, [1, 1],
                Ui.Field("Destination file", Text(i.Destination, "Destination", v => i.Destination = v), "Path of the file to create inside the image.", "/usr/local/bin/start.sh"),
                Ui.Field("Delimiter", Text(i.HeredocDelimiter, "Delimiter", v => i.HeredocDelimiter = v), "Word that ends the content.", "EOF"));
            yield return Ui.Switch("Do not expand variables (quoted delimiter)", i.HeredocQuoted, v => { i.HeredocQuoted = v; changed(); });
        }
        else if (isCopy)
        {
            yield return Ui.Field("Sources", new ListRows(i.Sources, "Source", "Add source", changed), "Paths in the build context, or in the source stage when From stage is set. Wildcards are allowed, ** matches any number of folders.", "package.json\npackage-lock.json\n./src/**/*.go");
            yield return Ui.Field("Destination", Text(i.Destination, "Destination", v => i.Destination = v), "Path inside the image. End with / to copy into a directory.", "./");
            yield return Ui.Field("From stage", EditableCombo(new[] { "" }.Concat(earlierStages()), () => i.From, "From stage", v => i.From = v), "Copy from an earlier stage (by name) or an image instead of the build context.", "build");
        }
        else
        {
            foreach (var element in AddSourceFields(i)) yield return element;
        }
        yield return Ui.Columns(12, [1, 1],
            Ui.Field("Owner", Text(i.Chown, "Owner", v => i.Chown = v), "Sets owner (and group) of the copied files (--chown).", "node:node"),
            Ui.Field("Permissions", Text(i.Chmod, "Permissions", v => i.Chmod = v), "Octal mode for the copied files (--chmod).", "755"));
        yield return Ui.Switch("Link layer (--link)", i.Link, v => { i.Link = v; changed(); }, "Copies into an independent layer so later base image changes do not invalidate it.");
        if (isCopy && !i.Heredoc)
            yield return Ui.Switch("Keep parent folders (--parents)", i.Parents, v => { i.Parents = v; changed(); }, "Recreates the source folder structure under the destination, for example ./x/a.txt becomes /dest/x/a.txt. Needs docker/dockerfile:1.20 or later.");
        if (!i.Heredoc)
            yield return Ui.Field("Exclude", new CreatorChipsInput(i.Excludes, "Add exclude pattern", changed, v => v.Any(char.IsWhiteSpace) ? "Patterns cannot contain spaces." : null),
                "Files matching any of these patterns are not copied (--exclude). Uses the same rules as .dockerignore. Needs docker/dockerfile:1.19 or later.", "*.md\n**/*_test.go");
    }

    private IEnumerable<UIElement> AddSourceFields(DockerInstruction i)
    {
        var kind = addKind ?? (i.Sources.Any(s => s.Trim().Length > 0) ? AddSourceKinds.Common(i) : AddSourceKind.Local);
        string[] names = ["Local files", "URL", "Git repository"];
        var current = kind switch { AddSourceKind.Url => names[1], AddSourceKind.Git => names[2], _ => names[0] };
        yield return Ui.Field("Source kind", Ui.Segmented(names, current, v =>
        {
            var next = v == names[1] ? AddSourceKind.Url : v == names[2] ? AddSourceKind.Git : AddSourceKind.Local;
            addKind = next;
            if (next != AddSourceKind.Git) i.KeepGitDir = false;
            if (next == AddSourceKind.Local) i.Checksum = "";
            if (i.Sources.Any(s => s.Trim().Length > 0 && AddSourceKinds.Of(s) != next)) i.Sources = [""];
            changed();
            Rebuild();
        }), "Local files are copied from the build context and local tar archives are extracted. A URL is downloaded. A Git repository is cloned at the reference after #.", "https://example.com/tool.tar.gz\nhttps://github.com/moby/buildkit.git#v0.26.2\ngit@github.com:org/repo.git#main");
        if (kind == AddSourceKind.Local)
            yield return Ui.Field("Sources", new ListRows(i.Sources, "Source", "Add source", changed), "Paths in the build context. Prefer COPY unless you want local tar archives extracted.", "vendor.tar.gz");
        else
        {
            if (i.Sources.Count == 0) i.Sources.Add("");
            yield return Ui.Field(kind == AddSourceKind.Git ? "Repository" : "URL", Text(i.Sources[0], kind == AddSourceKind.Git ? "Repository" : "URL", v => i.Sources[0] = v),
                kind == AddSourceKind.Git ? "HTTPS or SSH address of the repository; add #branch, #tag or #commit to choose the reference. SSH addresses need --ssh on the build." : "HTTP or HTTPS address of the file to download.",
                kind == AddSourceKind.Git ? "https://github.com/moby/buildkit.git#v0.26.2\ngit@github.com:org/repo.git#main" : "https://example.com/tool.tar.gz");
            yield return Ui.Field("Checksum", Text(i.Checksum, "Checksum", v => i.Checksum = v),
                kind == AddSourceKind.Git ? "Commit hash the reference must resolve to (--checksum). The build fails if it does not." : "sha256: digest of the file (--checksum). The build fails if the download differs.",
                kind == AddSourceKind.Git ? "be1f38e" : "sha256:24454f830cdb571e2c4ad15481119c43b3cafd48dd869a9b2945d1036d1dc68d");
            if (kind == AddSourceKind.Git)
                yield return Ui.Switch("Keep the .git folder (--keep-git-dir)", i.KeepGitDir, v => { i.KeepGitDir = v; changed(); }, "Keeps repository metadata so tools such as git describe work inside the build.");
        }
        yield return Ui.Field("Destination", Text(i.Destination, "Destination", v => i.Destination = v), "Path inside the image. End with / to add into a directory.", "/src/");
    }

    private IEnumerable<UIElement> OnBuildFields(DockerInstruction i)
    {
        var kinds = OnBuildKinds.Select(DockerfileModel.Keyword).ToList();
        var combo = Ui.Combo(kinds, i.Inner != null ? DockerfileModel.Keyword(i.Inner.Kind) : null);
        if (i.Inner == null) combo.SelectedIndex = -1;
        combo.SetResourceReference(Control.FontFamilyProperty, "MonoFont");
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not string keyword) return;
            var kind = OnBuildKinds.First(k => DockerfileModel.Keyword(k) == keyword);
            if (i.Inner?.Kind == kind) return;
            i.Inner = DockerfileModel.Create(kind);
            i.Value = "";
            changed();
            Rebuild();
        };
        yield return Ui.Field("Instruction type", combo, "ONBUILD stores this instruction in the image; it runs when another Dockerfile uses the image in FROM.", "ONBUILD COPY . /app\nONBUILD RUN npm ci");
        if (i.Inner != null)
        {
            var nested = new InstructionForm(i.Inner, earlierStages, changed);
            yield return Ui.Card(nested, 12, "SurfaceContainer", 12);
        }
        else
            yield return Ui.Field("Instruction", Text(i.Value, "Instruction", v => i.Value = v), "A complete instruction written by hand. Choose a type above to edit it with a form instead.", "COPY . /app");
    }
}
