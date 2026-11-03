using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>
/// Modern Dockerfile checks (ids DF100 to DF121): BuildKit features, cache mounts, secrets, package manager
/// hygiene and cross-compilation. Messages follow the Dockerfile reference and the build best practices.
/// </summary>
public static partial class DockerfileLint
{
    private static readonly Regex SecretFileName = new(@"(^|/)(\.npmrc|\.yarnrc\.yml|\.pypirc|\.netrc|\.git-credentials|\.dockercfg|\.env(\.[A-Za-z0-9_-]+)?|id_rsa|id_dsa|id_ecdsa|id_ed25519|[^/]+\.pem|[^/]+\.key|credentials(\.json)?)$", RegexOptions.IgnoreCase, RegexTimeout);
    private static readonly Regex CurlPipe = new(@"\b(curl|wget)\b[^|;&\n]*\|\s*(sudo\s+)?(sh|bash|zsh|dash|ash)\b", RegexOptions.None, RegexTimeout);
    private static readonly Regex Sudo = new(@"(^|[;&|(]\s*|\s)sudo\s+(-\S+\s+)*", RegexOptions.Multiline, RegexTimeout);
    private static readonly Regex PipInstall = new(@"\bpip3?\s+install\b", RegexOptions.None, RegexTimeout);
    private static readonly Regex ApkAdd = new(@"\bapk\s+add\b", RegexOptions.None, RegexTimeout);
    private static readonly Regex OldSyntax = new(@"^(docker\.io/)?docker/dockerfile:1\.(\d+)", RegexOptions.None, RegexTimeout);

    /// <summary>Package manager commands and the cache directories BuildKit should keep for them.</summary>
    private static readonly (string Name, Regex Pattern, string[] Targets)[] PackageCaches =
    [
        ("npm", new(@"\bnpm\s+(ci|install|i)\b", RegexOptions.None, RegexTimeout), ["/root/.npm"]),
        ("Yarn", new(@"\byarn\s+(install\b|--frozen-lockfile)|\byarn\s*($|&&)", RegexOptions.None, RegexTimeout), ["/usr/local/share/.cache/yarn"]),
        ("pnpm", new(@"\bpnpm\s+(install|i|fetch)\b", RegexOptions.None, RegexTimeout), ["/root/.local/share/pnpm/store"]),
        ("pip", new(@"\bpip3?\s+(install|wheel)\b", RegexOptions.None, RegexTimeout), ["/root/.cache/pip"]),
        ("uv", new(@"\buv\s+(sync|pip\s+install)\b", RegexOptions.None, RegexTimeout), ["/root/.cache/uv"]),
        ("apt", new(@"\bapt(-get)?\s+(-\S+\s+)*install\b", RegexOptions.None, RegexTimeout), ["/var/cache/apt", "/var/lib/apt"]),
        ("Go", new(@"\bgo\s+(build|install|test|mod\s+download)\b", RegexOptions.None, RegexTimeout), ["/go/pkg/mod", "/root/.cache/go-build"]),
        ("Cargo", new(@"\bcargo\s+(build|fetch|install)\b", RegexOptions.None, RegexTimeout), ["/usr/local/cargo/registry"]),
        ("Maven", new(@"\bmvn\b", RegexOptions.None, RegexTimeout), ["/root/.m2"]),
        ("Gradle", new(@"(\bgradle|\./gradlew)\s+\S", RegexOptions.None, RegexTimeout), ["/root/.gradle"]),
    ];

    /// <summary>Dockerfile frontend minor version (1.x) that introduced each feature, per the Dockerfile reference.</summary>
    private static IEnumerable<(string Feature, int Minor)> FeaturesUsed(DockerfileDocument document)
    {
        foreach (var raw in document.Stages.SelectMany(s => s.Instructions).Concat(document.Preamble))
        {
            var i = raw.Kind == InstructionKind.OnBuild && raw.Inner != null ? raw.Inner : raw;
            if (i.Heredoc) yield return ("heredocs", 4);
            if (i.Kind == InstructionKind.Run)
            {
                var mounts = DockerfileModel.AllMounts(i);
                if (mounts.Count > 0) yield return ("RUN --mount", 2);
                if (mounts.Any(m => m.Type == RunMounts.Secret && m.Env.Trim().Length > 0)) yield return ("secret mounts as environment variables", 10);
                if (i.Network.Trim().Length > 0) yield return ("RUN --network", 3);
                if (i.Security.Trim().Length > 0) yield return ("RUN --security", 20);
            }
            if (i.Parents) yield return ("COPY --parents", 20);
            if (i.Excludes.Count > 0) yield return ("--exclude", 19);
            if (i.Checksum.Trim().Length > 0) yield return ("ADD --checksum", 6);
            if (i.Link) yield return ("--link", 4);
        }
    }

    private static void CheckAdvanced(DockerfileDocument document, List<LintResult> results)
    {
        CheckSyntaxHeader(document, results);
        if (document.Escape.Trim().Length > 0 && document.Escape.Trim() != "\\")
            results.Add(new("DF115", LintSeverity.Warning, "Escape directive changes the line continuation",
                $"# escape={document.Escape.Trim()} makes {document.Escape.Trim()} the escape and continuation character. The Creator writes multi-line commands with a backslash, so review every continued line, or remove the directive.", -1, -1,
                new LintFix("Remove directive", d => d.Escape = "")));
        for (var p = 0; p < document.Preamble.Count; p++) CheckMaintainer(document.Preamble, -1, p, results);
        for (var s = 0; s < document.Stages.Count; s++)
        {
            var stage = document.Stages[s];
            CheckCrossCompile(document, s, results);
            var workdirSeen = IsEarlierStage(document, s, stage.BaseImage.Trim());
            for (var i = 0; i < stage.Instructions.Count; i++)
            {
                var instruction = stage.Instructions[i];
                CheckMaintainer(stage.Instructions, s, i, results);
                switch (instruction.Kind)
                {
                    case InstructionKind.Workdir:
                        workdirSeen = true;
                        break;
                    case InstructionKind.Run:
                        CheckRunAdvanced(document, s, i, results);
                        break;
                    case InstructionKind.Copy:
                    case InstructionKind.Add:
                        CheckCopyAdvanced(document, s, i, workdirSeen, results);
                        break;
                    case InstructionKind.Env:
                        CheckPathOverwrite(s, i, instruction, results);
                        break;
                    case InstructionKind.Raw when Regex.IsMatch(instruction.Value, @"^\s*EXPOSE\b", RegexOptions.IgnoreCase, RegexTimeout):
                        results.Add(new("DF110", LintSeverity.Error, "EXPOSE has an invalid port",
                            "EXPOSE takes ports from 1 to 65535 or ranges such as 8000-8010, each optionally followed by /tcp or /udp, or a variable such as ${PORT}. This line is kept as written because it does not match that form.", s, i));
                        break;
                }
            }
            CheckMergeableRuns(s, stage, results);
        }
    }

    private static void CheckSyntaxHeader(DockerfileDocument document, List<LintResult> results)
    {
        var features = FeaturesUsed(document).ToList();
        if (!document.SyntaxHeader)
        {
            if (features.Count > 0)
            {
                var names = string.Join(", ", features.Select(f => f.Feature).Distinct().Take(4));
                results.Add(new("DF116", LintSeverity.Warning, "Modern syntax without a syntax header",
                    $"This Dockerfile uses {names}. Add # syntax=docker/dockerfile:1 so BuildKit always uses a Dockerfile frontend that understands them, whatever version the engine bundles.", -1, -1,
                    new LintFix("Add header", d => { d.SyntaxHeader = true; d.Syntax = DockerfileModel.DefaultSyntax; })));
            }
            else
                results.Add(new("DF100", LintSeverity.Info, "No # syntax header",
                    "The # syntax=docker/dockerfile:1 parser directive makes BuildKit fetch the current stable Dockerfile frontend, so new instructions and fixes are available without upgrading the engine.", -1, -1,
                    new LintFix("Add header", d => { d.SyntaxHeader = true; d.Syntax = DockerfileModel.DefaultSyntax; })));
            return;
        }
        var pinned = OldSyntax.Match(document.Syntax.Trim());
        if (!pinned.Success || !int.TryParse(pinned.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor)) return;
        var newer = features.Where(f => f.Minor > minor).ToList();
        if (newer.Count == 0) return;
        var needed = newer.Max(f => f.Minor);
        results.Add(new("DF116", LintSeverity.Error, "Syntax header is older than the features used",
            $"{document.Syntax.Trim()} does not support {string.Join(", ", newer.Select(f => f.Feature).Distinct())} (needs docker/dockerfile:1.{needed} or later). Use docker/dockerfile:1 to follow the current stable release.", -1, -1,
            new LintFix("Use docker/dockerfile:1", d => d.Syntax = DockerfileModel.DefaultSyntax)));
    }

    private static void CheckMaintainer(List<DockerInstruction> list, int s, int i, List<LintResult> results)
    {
        var instruction = list[i];
        if (instruction.Kind != InstructionKind.Raw) return;
        var m = Regex.Match(instruction.Value, @"^\s*MAINTAINER\s+(.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout);
        if (!m.Success) return;
        var author = m.Groups[1].Value;
        results.Add(new("DF114", LintSeverity.Warning, "MAINTAINER is deprecated",
            "The MAINTAINER instruction is deprecated. Record the author with LABEL org.opencontainers.image.authors, which registries and tools read.", s, i,
            new LintFix("Use LABEL", d =>
            {
                var target = s < 0 ? d.Preamble : d.Stages[s].Instructions;
                target[i] = new DockerInstruction { Kind = InstructionKind.Label, Pairs = { new NameValue("org.opencontainers.image.authors", author) } };
            })));
    }

    private static void CheckCrossCompile(DockerfileDocument document, int s, List<LintResult> results)
    {
        var stage = document.Stages[s];
        var platform = stage.Platform.Trim();
        if (DockerfilePlatform.UsesTargetArgs(stage) && !platform.Contains("BUILDPLATFORM", StringComparison.Ordinal))
            results.Add(new("DF113", LintSeverity.Info, "Cross-compilation stage runs on the target platform",
                $"{Label(document, s)} uses TARGETOS, TARGETARCH or TARGETPLATFORM in its commands. Start it with FROM --platform=$BUILDPLATFORM so the compiler runs natively and only the output targets the requested platform; emulating the compiler is much slower.", s, -1,
                new LintFix("Build on BUILDPLATFORM", d => DockerfilePlatform.UseBuildPlatform(d, s))));
        else if (platform.Length > 0 && !platform.Contains('$'))
            results.Add(new("DF113", LintSeverity.Info, "Stage is pinned to one platform",
                $"FROM --platform={platform} always uses that platform, so multi-platform builds produce the same image for every target. Use $BUILDPLATFORM for build tools or $TARGETPLATFORM for the runtime, or remove the flag.", s, -1));
    }

    private static void CheckRunAdvanced(DockerfileDocument document, int s, int i, List<LintResult> results)
    {
        var run = document.Stages[s].Instructions[i];
        var script = DockerfileModel.RunScript(run);
        var mounts = DockerfileModel.AllMounts(run);
        bool HasCache(string target) => mounts.Any(m => m.Type == RunMounts.Cache && (m.Target.Trim().TrimEnd('/') == target || m.Target.Trim().StartsWith(target + "/", StringComparison.Ordinal)));

        foreach (var (name, pattern, targets) in PackageCaches)
        {
            if (!pattern.IsMatch(script)) continue;
            if (name == "pip" && script.Contains("--no-cache-dir", StringComparison.Ordinal)) continue;
            var missing = targets.Where(t => !HasCache(t)).ToList();
            if (missing.Count == 0) continue;
            var isApt = name == "apt";
            results.Add(new("DF101", LintSeverity.Info, $"{name} runs without a cache mount",
                $"Add RUN --mount=type=cache,target={string.Join(" and ", missing)} so {name} keeps downloaded packages between builds without storing them in the image layer." +
                (isApt ? " apt needs sharing=locked, and the Debian and Ubuntu images delete downloaded packages unless /etc/apt/apt.conf.d/docker-clean is removed first; the fix adds that step." : ""),
                s, i, new LintFix("Add cache mount", d =>
                {
                    var list = d.Stages[s].Instructions;
                    var target = list[i];
                    DockerfileModel.MigrateLegacyMounts(target);
                    foreach (var path in missing) target.Mounts.Add(new RunMount { Type = RunMounts.Cache, Target = path, Sharing = isApt ? "locked" : "" });
                    if (isApt && !list.Any(x => x.Kind == InstructionKind.Run && x.Command.Contains("docker-clean", StringComparison.Ordinal)))
                        list.Insert(i, new DockerInstruction { Kind = InstructionKind.Run, Command = "rm -f /etc/apt/apt.conf.d/docker-clean; echo 'Binary::apt::APT::Keep-Downloaded-Packages \"true\";' > /etc/apt/apt.conf.d/keep-cache" });
                })));
        }

        if (CurlPipe.IsMatch(script))
            results.Add(new("DF104", LintSeverity.Warning, "Downloaded script piped into a shell",
                "curl … | sh runs whatever the server returns, and a failed download can run half a script. Download to a file, verify its checksum (or use ADD --checksum), then run it.", s, i));

        if (Sudo.IsMatch(script))
            results.Add(new("DF105", LintSeverity.Warning, "sudo in a build step",
                "RUN already executes as the current USER. sudo adds a dependency and unpredictable TTY and signal behaviour; switch with USER root before the step and back to the unprivileged user afterwards.", s, i,
                run.Heredoc || run.ExecForm ? null : new LintFix("Remove sudo", d => { var t = d.Stages[s].Instructions[i]; t.Command = Sudo.Replace(t.Command, m => m.Groups[1].Value); })));

        if (PipInstall.IsMatch(script) && !script.Contains("--no-cache-dir", StringComparison.Ordinal) && !HasCache("/root/.cache/pip"))
            results.Add(new("DF106", LintSeverity.Info, "pip install keeps its download cache",
                "pip stores downloaded wheels in /root/.cache/pip, which ends up in the layer. Add --no-cache-dir, or use a cache mount for /root/.cache/pip to keep the cache outside the image.", s, i,
                run.Heredoc || run.ExecForm ? null : new LintFix("Add --no-cache-dir", d => { var t = d.Stages[s].Instructions[i]; t.Command = PipInstall.Replace(t.Command, m => m.Value + " --no-cache-dir"); })));

        if (ApkAdd.IsMatch(script) && !script.Contains("--no-cache", StringComparison.Ordinal) && !HasCache("/var/cache/apk") && !HasCache("/etc/apk/cache"))
            results.Add(new("DF107", LintSeverity.Info, "apk add keeps the package index",
                "apk add --no-cache fetches the index on the fly and leaves nothing in /var/cache/apk, so the layer stays small.", s, i,
                run.Heredoc || run.ExecForm ? null : new LintFix("Add --no-cache", d => { var t = d.Stages[s].Instructions[i]; t.Command = ApkAdd.Replace(t.Command, m => m.Value + " --no-cache"); })));

        if (string.Equals(run.Security.Trim(), "insecure", StringComparison.OrdinalIgnoreCase))
        {
            var previous = i > 0 ? document.Stages[s].Instructions[i - 1] : null;
            if (previous?.Kind != InstructionKind.Comment)
                results.Add(new("DF112", LintSeverity.Warning, "Insecure RUN without an explanation",
                    "RUN --security=insecure runs the step without the sandbox, like docker run --privileged. The build needs --allow security.insecure and a builder with the security.insecure entitlement. Add a comment that says why this step needs it.", s, i,
                    new LintFix("Add comment", d => d.Stages[s].Instructions.Insert(i, new DockerInstruction { Kind = InstructionKind.Comment, Value = "Needs --allow security.insecure: explain why this step needs elevated privileges." }))));
        }
        if (string.Equals(run.Network.Trim(), "host", StringComparison.OrdinalIgnoreCase))
            results.Add(new("DF117", LintSeverity.Info, "RUN uses the host network",
                "RUN --network=host needs the network.host entitlement on the builder and --allow network.host on the build. Prefer the default network unless the step must reach a service on the build host.", s, i));
    }

    private static void CheckCopyAdvanced(DockerfileDocument document, int s, int i, bool workdirSeen, List<LintResult> results)
    {
        var instruction = document.Stages[s].Instructions[i];
        var keyword = DockerfileModel.Keyword(instruction.Kind);
        var destination = instruction.Destination.Trim();
        if (!workdirSeen && destination.Length > 0 && !destination.StartsWith('/') && !destination.StartsWith('$') && !Regex.IsMatch(destination, @"^[A-Za-z]:[\\/]", RegexOptions.None, RegexTimeout))
            results.Add(new("DF109", LintSeverity.Warning, $"{keyword} to a relative path without WORKDIR",
                $"{keyword} … {destination} is relative to the working directory, which is whatever the base image set (often /). Set WORKDIR first so the destination is explicit.", s, i,
                new LintFix("Add WORKDIR /app", d => d.Stages[s].Instructions.Insert(i, new DockerInstruction { Kind = InstructionKind.Workdir, Value = "/app" }))));

        if (instruction.From.Trim().Length == 0)
        {
            var secretSources = instruction.Sources.Select(x => x.Trim()).Where(x => SecretFileName.IsMatch(x)).ToList();
            if (secretSources.Count > 0)
            {
                var source = secretSources[0];
                var name = source.Split('/').Last();
                var list = document.Stages[s].Instructions;
                var nextRun = list.FindIndex(i + 1, x => x.Kind == InstructionKind.Run);
                var onlySource = instruction.Sources.Count(x => x.Trim().Length > 0) == 1;
                var id = Regex.Replace(name.TrimStart('.'), "[^A-Za-z0-9_.-]", "_", RegexOptions.None, RegexTimeout);
                if (id.Length == 0 || !char.IsLetter(id[0])) id = "secret_" + id;
                var target = destination.EndsWith('/') ? destination + name : destination is "." or "./" || destination.Length == 0 ? name : destination;
                results.Add(new("DF102", LintSeverity.Warning, $"Secret file copied with {keyword}",
                    $"{source} looks like a credential file. {keyword} stores it in an image layer where anyone with the image can read it, even if a later step deletes it. Mount it only for the step that needs it with RUN --mount=type=secret,id={id},target={target} and pass it with docker buildx build --secret id={id},src={source}.",
                    s, i, onlySource && nextRun > i ? new LintFix("Use secret mount", d =>
                    {
                        var steps = d.Stages[s].Instructions;
                        var run = steps[nextRun];
                        DockerfileModel.MigrateLegacyMounts(run);
                        run.Mounts.Add(new RunMount { Type = RunMounts.Secret, Id = id, Target = target, Required = true });
                        steps.RemoveAt(i);
                    }) : null));
            }
        }
        else if (instruction.Kind == InstructionKind.Copy && !IsStageName(document, instruction.From.Trim()) && LooksLikeImage(instruction.From.Trim()) && !instruction.From.Contains('$'))
        {
            var (_, tag, digest) = DockerfileModel.SplitImage(instruction.From.Trim());
            if (digest.Length == 0 && (tag.Length == 0 || string.Equals(tag, "latest", StringComparison.OrdinalIgnoreCase)))
                results.Add(new("DF103", LintSeverity.Warning, "COPY --from uses the latest tag",
                    $"--from={instruction.From.Trim()} resolves to whatever latest points to at build time. Pin a version tag or a digest so the copied files do not change silently.", s, i));
        }

        if (instruction.Kind == InstructionKind.Add && instruction.Checksum.Trim().Length == 0 && instruction.Sources.Count > 0)
        {
            var kind = AddSourceKinds.Common(instruction);
            if (kind != AddSourceKind.Local)
                results.Add(new("DF120", LintSeverity.Info, kind == AddSourceKind.Git ? "Git source without a pinned commit" : "Download without a checksum",
                    kind == AddSourceKind.Git
                        ? "ADD --checksum=<commit> verifies that the Git reference still points to the commit you reviewed."
                        : "ADD --checksum=sha256:<digest> makes the build fail if the downloaded file changes, instead of silently using different content.",
                    s, i, new LintFix("Add checksum", PromptTitle: "Pin the ADD source", PromptLabel: kind == AddSourceKind.Git ? "Commit hash" : "SHA-256 checksum",
                        PromptInitial: kind == AddSourceKind.Git ? "" : "sha256:",
                        PromptHelp: kind == AddSourceKind.Git ? "Enter the full or abbreviated commit hash the reference must resolve to." : "Enter sha256: followed by the 64 hexadecimal characters of the file digest.",
                        ValidateValue: v => kind == AddSourceKind.Git
                            ? (Regex.IsMatch(v, "^[0-9a-f]{7,64}$", RegexOptions.None, RegexTimeout) ? null : "A commit hash has 7 to 64 lowercase hexadecimal characters.")
                            : (Regex.IsMatch(v, "^sha256:[0-9a-f]{64}$", RegexOptions.None, RegexTimeout) ? null : "Enter sha256: followed by 64 lowercase hexadecimal characters."),
                        ApplyValue: (d, v) => d.Stages[s].Instructions[i].Checksum = v)));
        }
    }

    private static bool IsStageName(DockerfileDocument document, string reference) =>
        document.Stages.Any(x => string.Equals(x.Name.Trim(), reference, StringComparison.OrdinalIgnoreCase)) || int.TryParse(reference, NumberStyles.None, CultureInfo.InvariantCulture, out _);

    private static void CheckPathOverwrite(int s, int i, DockerInstruction env, List<LintResult> results)
    {
        var pair = env.Pairs.FirstOrDefault(p => p.Name.Trim() == "PATH");
        if (pair == null || pair.Value.Contains("$PATH", StringComparison.Ordinal) || pair.Value.Contains("${PATH}", StringComparison.Ordinal)) return;
        results.Add(new("DF111", LintSeverity.Warning, "ENV PATH replaces the inherited PATH",
            $"PATH={pair.Value} drops the directories the base image put on PATH, so common tools stop resolving. Append the existing value: PATH={pair.Value}:$PATH.", s, i,
            new LintFix("Append $PATH", d =>
            {
                var target = d.Stages[s].Instructions[i].Pairs.FirstOrDefault(p => p.Name.Trim() == "PATH");
                if (target != null) target.Value = target.Value.TrimEnd(':') + ":$PATH";
            })));
    }

    private static bool IsPlainRun(DockerInstruction x) => x.Kind == InstructionKind.Run && !x.ExecForm && !x.Heredoc && DockerfileModel.AllMounts(x).Count == 0
        && x.Network.Trim().Length == 0 && x.Security.Trim().Length == 0 && x.Command.Trim().Length > 0;

    private static void CheckMergeableRuns(int s, DockerfileStage stage, List<LintResult> results)
    {
        var list = stage.Instructions;
        for (var i = 0; i + 1 < list.Count; i++)
        {
            if (!IsPlainRun(list[i]) || !IsPlainRun(list[i + 1])) continue;
            // apt-get update followed by install has its own check (DF020).
            if (AptUpdate.IsMatch(list[i].Command) && !AptInstall.IsMatch(list[i].Command)) continue;
            var first = i;
            results.Add(new("DF108", LintSeverity.Info, "Consecutive RUN instructions",
                "Each RUN creates a layer. Joining related commands with && in one RUN makes fewer layers and lets a cleanup step remove files before they are committed.", s, i,
                new LintFix("Merge", d =>
                {
                    var steps = d.Stages[s].Instructions;
                    steps[first].Command = steps[first].Command.TrimEnd() + " && \\\n    " + steps[first + 1].Command.Trim();
                    steps.RemoveAt(first + 1);
                })));
            while (i + 1 < list.Count && IsPlainRun(list[i + 1])) i++;
        }
    }
}
