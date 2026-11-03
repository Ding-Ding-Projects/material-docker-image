using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

public enum LintSeverity { Error, Warning, Info, Pass }

/// <summary>
/// An automatic fix. <see cref="Apply"/> mutates the same document that was linted. When <see cref="PromptTitle"/>
/// is set, the UI asks for a value first (validated by <see cref="ValidateValue"/>) and calls <see cref="ApplyValue"/>.
/// </summary>
public sealed record LintFix(string Label, Action<DockerfileDocument>? Apply = null, string? PromptTitle = null, string? PromptLabel = null, string? PromptInitial = null, string? PromptHelp = null, Func<string, string?>? ValidateValue = null, Action<DockerfileDocument, string>? ApplyValue = null)
{
    public bool NeedsValue => PromptTitle != null;
}

/// <summary>One check result. StageIndex is -1 for document-wide results; InstructionIndex is -1 for stage-level results.</summary>
public sealed record LintResult(string Id, LintSeverity Severity, string Title, string Message, int StageIndex = -1, int InstructionIndex = -1, LintFix? Fix = null);

/// <summary>Pure checks over the structured Dockerfile model. Every rule is deterministic and has a stable id.</summary>
public static partial class DockerfileLint
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>Suggested tags offered when a base image is unpinned. These are suggestions only; the user confirms the value.</summary>
    public static readonly IReadOnlyDictionary<string, string> SuggestedTags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["node"] = "22-alpine", ["python"] = "3.13-slim", ["golang"] = "1.24-alpine", ["rust"] = "1-slim", ["nginx"] = "1.27-alpine",
        ["alpine"] = "3.21", ["ubuntu"] = "24.04", ["debian"] = "bookworm-slim", ["eclipse-temurin"] = "21-jre", ["maven"] = "3-eclipse-temurin-21",
        ["mcr.microsoft.com/dotnet/sdk"] = "9.0", ["mcr.microsoft.com/dotnet/aspnet"] = "9.0", ["httpd"] = "2.4-alpine", ["redis"] = "7-alpine", ["postgres"] = "17-alpine",
    };

    private static readonly string[] SecretWords = ["PASSWORD", "PASSWD", "TOKEN", "SECRET", "KEY", "APIKEY", "CREDENTIAL", "CREDENTIALS", "PRIVATE"];

    public static IReadOnlyList<LintResult> Check(DockerfileDocument document)
    {
        var results = new List<LintResult>();
        if (document.Stages.Count == 0)
        {
            results.Add(new("DF000", LintSeverity.Error, "No stages", "Add a stage with a base image, or apply a starter."));
            return results;
        }

        CheckInstructionValidity(document, results);
        CheckDuplicateStages(document, results);
        for (var s = 0; s < document.Stages.Count; s++)
        {
            CheckBaseImage(document, s, results);
            CheckCopyFrom(document, s, results);
            CheckStageInstructions(document, s, results);
        }
        CheckFinalStage(document, results);
        CheckSecretNames(document, results);
        CheckAdvanced(document, results);
        var skipped = SkippedChecks(document);
        if (skipped.Count > 0) results.RemoveAll(r => skipped.Contains("all") || (BuildKitName(r.Id) is { } name && skipped.Contains(name)));
        return results.OrderBy(r => r.Severity).ThenBy(r => r.StageIndex).ThenBy(r => r.InstructionIndex).ToList();
    }

    public static int CountProblems(IEnumerable<LintResult> results) => results.Count(r => r.Severity != LintSeverity.Pass);

    // ------------------------------------------------------------ rules

    private static void CheckInstructionValidity(DockerfileDocument document, List<LintResult> results)
    {
        for (var s = 0; s < document.Stages.Count; s++)
        {
            var stage = document.Stages[s];
            foreach (var problem in DockerfileModel.ValidateStage(stage))
                results.Add(new("DF001", LintSeverity.Error, $"Stage {Label(document, s)} is incomplete", problem, s));
            for (var i = 0; i < stage.Instructions.Count; i++)
                foreach (var problem in DockerfileModel.Validate(stage.Instructions[i]))
                    results.Add(new("DF002", LintSeverity.Error, $"{DockerfileModel.Keyword(stage.Instructions[i].Kind)} in {Label(document, s)} needs attention", problem, s, i));
        }
        for (var i = 0; i < document.Preamble.Count; i++)
            foreach (var problem in DockerfileModel.Validate(document.Preamble[i]))
                results.Add(new("DF002", LintSeverity.Error, "Global instruction needs attention", problem, -1, i));
    }

    private static void CheckDuplicateStages(DockerfileDocument document, List<LintResult> results)
    {
        var groups = document.Stages.Select((stage, index) => (Name: stage.Name.Trim(), Index: index)).Where(x => x.Name.Length > 0).GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups.Where(g => g.Count() > 1))
            foreach (var duplicate in group.Skip(1))
                results.Add(new("DF070", LintSeverity.Error, "Duplicate stage name", $"Stage name \"{duplicate.Name}\" is used more than once. Docker refuses duplicate stage names.", duplicate.Index, -1,
                    new LintFix("Rename", d => d.Stages[duplicate.Index].Name = DockerfileModel.UniqueStageName(d, duplicate.Name))));
    }

    private static void CheckBaseImage(DockerfileDocument document, int s, List<LintResult> results)
    {
        var stage = document.Stages[s];
        var image = stage.BaseImage.Trim();
        if (image.Length == 0 || image.Contains('$') || string.Equals(image, "scratch", StringComparison.OrdinalIgnoreCase)) return;
        if (IsEarlierStage(document, s, image)) return;
        var (repository, tag, digest) = DockerfileModel.SplitImage(image);
        if (digest.Length > 0) return;
        if (tag.Length == 0 || string.Equals(tag, "latest", StringComparison.OrdinalIgnoreCase))
        {
            SuggestedTags.TryGetValue(StripDockerHub(repository), out var suggestion);
            results.Add(new("DF010", LintSeverity.Warning, "Base image is not pinned",
                $"{image} {(tag.Length == 0 ? "has no tag, so it means latest" : "uses latest")}, which can change between builds. Pin a version tag, or a digest for fully repeatable builds.",
                s, -1, new LintFix("Pin", PromptTitle: "Pin base image", PromptLabel: "Tag or digest", PromptInitial: suggestion ?? "",
                    PromptHelp: $"Enter a tag such as {suggestion ?? "1.2"} or a digest such as sha256:… for {repository}. Check the tag exists in the registry before building.",
                    ValidateValue: ValidatePin, ApplyValue: (d, value) => d.Stages[s].BaseImage = ApplyPin(repository, value))));
        }
        else
        {
            results.Add(new("DF011", LintSeverity.Info, "Base image is not pinned to a digest",
                $"{image} can change when the tag is republished. Pin it to a digest for repeatable builds (docker buildx imagetools inspect {image} shows the digest).",
                s, -1, new LintFix("Pin", PromptTitle: "Pin base image to a digest", PromptLabel: "Digest", PromptInitial: "sha256:",
                    PromptHelp: $"Paste the digest for {image}. The tag is kept for readability: {image}@sha256:….",
                    ValidateValue: v => Regex.IsMatch(v, "^sha256:[0-9a-f]{64}$", RegexOptions.None, RegexTimeout) ? null : "A digest is sha256: followed by 64 lowercase hexadecimal characters.",
                    ApplyValue: (d, value) => d.Stages[s].BaseImage = image + "@" + value)));
        }
    }

    private static string? ValidatePin(string value)
    {
        if (Regex.IsMatch(value, "^sha256:[0-9a-f]{64}$", RegexOptions.None, RegexTimeout)) return null;
        if (string.Equals(value, "latest", StringComparison.OrdinalIgnoreCase)) return "Choose a specific version instead of latest.";
        return Regex.IsMatch(value, @"^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$", RegexOptions.None, RegexTimeout) ? null : "A tag uses letters, digits, '.', '_' and '-' (up to 128 characters); a digest is sha256:<64 hex>.";
    }
    private static string ApplyPin(string repository, string value) => value.StartsWith("sha256:", StringComparison.Ordinal) ? repository + "@" + value : repository + ":" + value;
    private static string StripDockerHub(string repository)
    {
        foreach (var prefix in new[] { "docker.io/library/", "docker.io/", "library/" })
            if (repository.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return repository[prefix.Length..];
        return repository;
    }

    private static bool IsEarlierStage(DockerfileDocument document, int s, string reference) =>
        document.Stages.Take(s).Any(x => string.Equals(x.Name.Trim(), reference, StringComparison.OrdinalIgnoreCase));

    private static void CheckCopyFrom(DockerfileDocument document, int s, List<LintResult> results)
    {
        var stage = document.Stages[s];
        for (var i = 0; i < stage.Instructions.Count; i++)
        {
            var instruction = stage.Instructions[i];
            if (instruction.Kind != InstructionKind.Copy) continue;
            var from = instruction.From.Trim();
            if (from.Length == 0 || from.Contains('$')) continue;
            if (int.TryParse(from, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                if (number >= s) results.Add(new("DF071", LintSeverity.Error, "COPY --from refers to a later stage", $"Stage index {number} is not an earlier stage of {Label(document, s)}.", s, i));
                continue;
            }
            var index = document.Stages.FindIndex(x => string.Equals(x.Name.Trim(), from, StringComparison.OrdinalIgnoreCase));
            if (index == s) results.Add(new("DF071", LintSeverity.Error, "COPY --from refers to its own stage", $"{Label(document, s)} cannot copy from itself.", s, i));
            else if (index > s) results.Add(new("DF071", LintSeverity.Error, "COPY --from refers to a later stage", $"\"{from}\" is defined after {Label(document, s)}. Move that stage earlier.", s, i));
            else if (index < 0 && !LooksLikeImage(from))
                results.Add(new("DF071", LintSeverity.Error, "COPY --from refers to an unknown stage", $"No stage is named \"{from}\". Docker would try to pull an image called {from}.", s, i));
        }
    }
    private static bool LooksLikeImage(string reference) => reference.Contains(':') || reference.Contains('/') || reference.Contains('@');

    private static readonly Regex AptUpdate = new(@"\bapt(-get)?\s+update\b", RegexOptions.None, RegexTimeout);
    private static readonly Regex AptInstall = new(@"\bapt(-get)?\s+(-\S+\s+)*install\b", RegexOptions.None, RegexTimeout);
    private static readonly Regex InstallCommand = new(@"\b(npm\s+(ci|install|i)\b|yarn(\s+install)?\s*($|&&)|pnpm\s+(install|i)\b|pip3?\s+install\b|uv\s+(sync|pip\s+install)\b|go\s+mod\s+download\b|mvn\b.*dependency:|cargo\s+fetch\b|dotnet\s+restore\b|bundle\s+install\b|composer\s+install\b)", RegexOptions.None, RegexTimeout);

    private static void CheckStageInstructions(DockerfileDocument document, int s, List<LintResult> results)
    {
        var stage = document.Stages[s];
        var cmdIndexes = new List<int>();
        for (var i = 0; i < stage.Instructions.Count; i++)
        {
            var instruction = stage.Instructions[i];
            var index = i;
            switch (instruction.Kind)
            {
                case InstructionKind.Run when !instruction.ExecForm && !instruction.Heredoc:
                    CheckApt(document, s, i, instruction, results);
                    break;
                case InstructionKind.Add:
                    if (instruction.Sources.Count > 0 && instruction.Sources.All(IsLocalPlainSource))
                        results.Add(new("DF030", LintSeverity.Warning, "ADD used for local files", "COPY is clearer for local files: ADD also downloads URLs and extracts archives, which can surprise you.", s, i,
                            new LintFix("Use COPY", d => d.Stages[s].Instructions[index].Kind = InstructionKind.Copy)));
                    break;
                case InstructionKind.Cmd:
                case InstructionKind.Entrypoint:
                    if (instruction.Kind == InstructionKind.Cmd) cmdIndexes.Add(i);
                    if (!instruction.ExecForm)
                    {
                        var argv = DockerfileModel.SimpleArgv(instruction.Command);
                        results.Add(new("DF031", LintSeverity.Warning, $"Shell-form {DockerfileModel.Keyword(instruction.Kind)}",
                            "Shell form runs through /bin/sh -c, so the process does not receive stop signals directly." + (argv == null ? " This command uses shell features; convert it by hand or wrap it in a script." : ""),
                            s, i, argv == null ? null : new LintFix("Use exec form", d => { var target = d.Stages[s].Instructions[index]; target.ExecForm = true; target.Arguments = argv; target.Command = ""; })));
                    }
                    break;
                case InstructionKind.Workdir:
                    var path = instruction.Value.Trim();
                    if (path.Length > 0 && !path.StartsWith('/') && !path.StartsWith('$') && !Regex.IsMatch(path, @"^[A-Za-z]:[\\/]", RegexOptions.None, RegexTimeout))
                        results.Add(new("DF072", LintSeverity.Warning, "Relative WORKDIR", $"WORKDIR {path} depends on the previous working directory. Use an absolute path.", s, i,
                            new LintFix("Make absolute", d => d.Stages[s].Instructions[index].Value = "/" + path.TrimStart('.', '/'))));
                    break;
            }
        }
        if (cmdIndexes.Count > 1)
        {
            var earlier = cmdIndexes.Take(cmdIndexes.Count - 1).ToList();
            results.Add(new("DF073", LintSeverity.Warning, "Multiple CMD instructions", $"{Label(document, s)} has {cmdIndexes.Count} CMD instructions; only the last one takes effect.", s, earlier[0],
                new LintFix("Remove earlier CMD", d => { foreach (var k in earlier.OrderByDescending(x => x)) d.Stages[s].Instructions.RemoveAt(k); })));
        }
        CheckCacheOrder(document, s, results);
    }

    private static bool IsLocalPlainSource(string source)
    {
        var s = source.Trim();
        if (Regex.IsMatch(s, @"^(https?://|git@|git://|ssh://)", RegexOptions.IgnoreCase, RegexTimeout)) return false;
        return !Regex.IsMatch(s, @"\.(tar|tar\.gz|tgz|tar\.bz2|tbz2|tar\.xz|txz|tar\.zst)$", RegexOptions.IgnoreCase, RegexTimeout);
    }

    private static void CheckApt(DockerfileDocument document, int s, int i, DockerInstruction run, List<LintResult> results)
    {
        var command = run.Command;
        var hasUpdate = AptUpdate.IsMatch(command);
        var hasInstall = AptInstall.IsMatch(command);
        if (hasUpdate && !hasInstall)
        {
            var stage = document.Stages[s];
            var next = i + 1 < stage.Instructions.Count ? stage.Instructions[i + 1] : null;
            var canMerge = next is { Kind: InstructionKind.Run, ExecForm: false } && AptInstall.IsMatch(next.Command) && DockerfileModel.AllMounts(next).Count == 0 && !next.Heredoc && !stage.Instructions[i].Heredoc;
            results.Add(new("DF020", LintSeverity.Warning, "apt-get update without install", "Run apt-get update and apt-get install in the same RUN; otherwise a cached update layer can install stale package versions.", s, i,
                canMerge ? new LintFix("Merge with next RUN", d =>
                {
                    var list = d.Stages[s].Instructions;
                    list[i].Command = list[i].Command.TrimEnd() + " && \\\n    " + list[i + 1].Command.Trim();
                    list.RemoveAt(i + 1);
                }) : null));
        }
        if (!hasInstall) return;
        if (!command.Contains("--no-install-recommends", StringComparison.Ordinal))
            results.Add(new("DF021", LintSeverity.Info, "apt-get install pulls recommended packages", "Add --no-install-recommends to keep the image smaller.", s, i,
                new LintFix("Add flag", d => { var target = d.Stages[s].Instructions[i]; target.Command = AptInstall.Replace(target.Command, m => m.Value + " --no-install-recommends", 1); })));
        var cachesApt = DockerfileModel.AllMounts(run).Any(m => m.Type == RunMounts.Cache && (m.Target.Trim().StartsWith("/var/lib/apt", StringComparison.Ordinal) || m.Target.Trim().StartsWith("/var/cache/apt", StringComparison.Ordinal)));
        if (!cachesApt && !command.Contains("/var/lib/apt/lists", StringComparison.Ordinal))
            results.Add(new("DF022", LintSeverity.Info, "apt lists are not cleaned up", "Remove /var/lib/apt/lists/* in the same RUN so the package index does not stay in the layer.", s, i,
                new LintFix("Add cleanup", d => { var target = d.Stages[s].Instructions[i]; target.Command = target.Command.TrimEnd() + " && \\\n    rm -rf /var/lib/apt/lists/*"; })));
    }

    private static readonly (Regex Pattern, string Sources, string Destination)[] Manifests =
    [
        (new(@"\b(npm|pnpm|yarn)\b", RegexOptions.None, RegexTimeout), "package*.json", "./"),
        (new(@"\buv\b", RegexOptions.None, RegexTimeout), "pyproject.toml uv.lock", "./"),
        (new(@"\bpip3?\b", RegexOptions.None, RegexTimeout), "requirements.txt", "./"),
        (new(@"\bgo\s+mod\b", RegexOptions.None, RegexTimeout), "go.mod go.sum", "./"),
        (new(@"\bdotnet\b", RegexOptions.None, RegexTimeout), "*.csproj", "./"),
        (new(@"\bcargo\b", RegexOptions.None, RegexTimeout), "Cargo.toml Cargo.lock", "./"),
        (new(@"\bmvn\b", RegexOptions.None, RegexTimeout), "pom.xml", "./"),
        (new(@"\bbundle\b", RegexOptions.None, RegexTimeout), "Gemfile Gemfile.lock", "./"),
        (new(@"\bcomposer\b", RegexOptions.None, RegexTimeout), "composer.json composer.lock", "./"),
    ];

    private static void CheckCacheOrder(DockerfileDocument document, int s, List<LintResult> results)
    {
        var list = document.Stages[s].Instructions;
        var copyAll = list.FindIndex(x => x.Kind == InstructionKind.Copy && x.From.Trim().Length == 0 && x.Sources.Any(src => src.Trim() is "." or "./"));
        if (copyAll < 0) return;
        var install = -1;
        for (var k = copyAll + 1; k < list.Count; k++)
            if (list[k].Kind == InstructionKind.Run && InstallCommand.IsMatch(list[k].ExecForm ? string.Join(" ", list[k].Arguments) : list[k].Command)) { install = k; break; }
        if (install < 0) return;
        // A manifest copied earlier means the author already split the layers.
        if (list.Take(copyAll).Any(x => x.Kind == InstructionKind.Copy && x.From.Trim().Length == 0)) return;
        var text = list[install].ExecForm ? string.Join(" ", list[install].Arguments) : list[install].Command;
        var manifest = Manifests.FirstOrDefault(m => m.Pattern.IsMatch(text));
        var installIndex = install;
        results.Add(new("DF050", LintSeverity.Warning, "COPY . . before installing dependencies",
            "Any source change invalidates the dependency install cache. Copy only the dependency manifests first, install, then copy the rest.", s, copyAll,
            manifest.Pattern == null ? null : new LintFix("Reorder for caching", d =>
            {
                var target = d.Stages[s].Instructions;
                var run = target[installIndex];
                target.RemoveAt(installIndex);
                target.Insert(copyAll, run);
                target.Insert(copyAll, new DockerInstruction { Kind = InstructionKind.Copy, Sources = manifest.Sources.Split(' ').ToList(), Destination = manifest.Destination });
            })));
    }

    private static void CheckFinalStage(DockerfileDocument document, List<LintResult> results)
    {
        var s = DockerfileModel.TargetStageIndex(document);
        var stage = document.Stages[s];
        var label = Label(document, s);
        var image = stage.BaseImage.Trim().ToLowerInvariant();
        var lastUser = stage.Instructions.LastOrDefault(x => x.Kind == InstructionKind.User);
        var nonRootBase = image.Contains(":nonroot", StringComparison.Ordinal) || image.Contains("-nonroot", StringComparison.Ordinal) || image.Contains("chiseled", StringComparison.Ordinal) || image.Contains("unprivileged", StringComparison.Ordinal);
        var inheritsStage = IsEarlierStage(document, s, stage.BaseImage.Trim());
        if (lastUser != null && !DockerfileModel.IsRootUser(lastUser.Value))
            results.Add(new("DF040", LintSeverity.Pass, "Runs as a non-root user", $"USER {lastUser.Value.Trim()} is set in the final stage {label}.", s, stage.Instructions.IndexOf(lastUser)));
        else if (nonRootBase && lastUser == null)
            results.Add(new("DF040", LintSeverity.Pass, "Base image runs as non-root", $"{stage.BaseImage.Trim()} defaults to a non-root user.", s));
        else if (!(inheritsStage && lastUser == null))
        {
            var user = image.StartsWith("node", StringComparison.Ordinal) || image.StartsWith("docker.io/library/node", StringComparison.Ordinal) ? "node" : image.StartsWith("nginx", StringComparison.Ordinal) && image.Contains("unprivileged", StringComparison.Ordinal) ? "nginx" : "10001";
            results.Add(new("DF040", LintSeverity.Warning, "Final stage runs as root",
                lastUser == null ? $"{label} has no USER instruction, so the container runs as root." : $"{label} switches to USER {lastUser.Value.Trim()}, which is root.",
                s, lastUser == null ? -1 : stage.Instructions.IndexOf(lastUser),
                new LintFix($"Add USER {user}", d => InsertBeforeStart(d.Stages[s], new DockerInstruction { Kind = InstructionKind.User, Value = user }))));
        }

        var health = stage.Instructions.FirstOrDefault(x => x.Kind == InstructionKind.Healthcheck);
        if (health != null)
        {
            if (!health.Disabled) results.Add(new("DF041", LintSeverity.Pass, "Health check defined", "The engine can report when the service is ready.", s, stage.Instructions.IndexOf(health)));
        }
        else if (!string.Equals(image, "scratch", StringComparison.Ordinal))
        {
            var port = stage.Instructions.FirstOrDefault(x => x.Kind == InstructionKind.Expose)?.Port.Trim();
            var template = DockerfileModel.Create(InstructionKind.Healthcheck);
            template.Command = $"wget -qO- http://localhost:{(string.IsNullOrEmpty(port) || port.Contains('-') ? "8080" : port)}/ || exit 1";
            results.Add(new("DF041", LintSeverity.Info, "No health check", "Add HEALTHCHECK so the engine can report when the service is ready. Adjust the template command to a tool and URL your image actually has.", s, -1,
                new LintFix("Add", d => InsertBeforeStart(d.Stages[s], template.Clone()))));
        }

        if (!stage.Instructions.Any(x => x.Kind == InstructionKind.Expose) && WebPort(document, stage) is { } webPort)
            results.Add(new("DF042", LintSeverity.Info, "No EXPOSE for a web service", $"This looks like a web service. EXPOSE {webPort} documents the listening port for people and tools (it does not publish it).", s, -1,
                new LintFix($"Add EXPOSE {webPort}", d => InsertBeforeStart(d.Stages[s], new DockerInstruction { Kind = InstructionKind.Expose, Port = webPort, Protocol = "tcp" }))));
    }

    /// <summary>Detects common web servers in the final stage and returns their usual port; null when nothing suggests a web service.</summary>
    public static string? WebPort(DockerfileDocument document, DockerfileStage stage)
    {
        var image = stage.BaseImage.Trim().ToLowerInvariant();
        var commands = string.Join(" ", stage.Instructions.Where(x => x.Kind is InstructionKind.Cmd or InstructionKind.Entrypoint).Select(x => x.ExecForm ? string.Join(" ", x.Arguments) : x.Command)).ToLowerInvariant();
        if (image.Contains("nginx", StringComparison.Ordinal) || image.Contains("httpd", StringComparison.Ordinal) || image.Contains("caddy", StringComparison.Ordinal)) return image.Contains("unprivileged", StringComparison.Ordinal) ? "8080" : "80";
        if (Regex.IsMatch(commands, @"\b(uvicorn|gunicorn|fastapi|flask)\b", RegexOptions.None, RegexTimeout)) return "8000";
        if (image.Contains("dotnet/aspnet", StringComparison.Ordinal)) return "8080";
        if (Regex.IsMatch(commands, @"\b(server|serve|next\s+start|npm\s+start|express)\b", RegexOptions.None, RegexTimeout)) return "8080";
        if (Regex.IsMatch(document.Origin, @"\b(Node\.js|Python|ASP\.NET|Static site)\b", RegexOptions.None, RegexTimeout) && stage.Instructions.Any(x => x.Kind is InstructionKind.Cmd or InstructionKind.Entrypoint)) return "8080";
        return null;
    }

    /// <summary>Inserts before the first CMD, ENTRYPOINT or HEALTHCHECK in the stage, or at the end.</summary>
    public static void InsertBeforeStart(DockerfileStage stage, DockerInstruction instruction)
    {
        var at = stage.Instructions.FindIndex(x => x.Kind is InstructionKind.Cmd or InstructionKind.Entrypoint || (instruction.Kind != InstructionKind.Healthcheck && x.Kind == InstructionKind.Healthcheck));
        if (instruction.Kind == InstructionKind.Expose)
        {
            var user = stage.Instructions.FindIndex(x => x.Kind == InstructionKind.User);
            if (user >= 0 && (at < 0 || user < at)) at = user;
        }
        if (at < 0) stage.Instructions.Add(instruction); else stage.Instructions.Insert(at, instruction);
    }

    private static void CheckSecretNames(DockerfileDocument document, List<LintResult> results)
    {
        void Inspect(int s, int i, DockerInstruction instruction)
        {
            var names = instruction.Kind switch
            {
                InstructionKind.Arg => [instruction.Name],
                InstructionKind.Env => instruction.Pairs.Select(p => p.Name).ToList(),
                _ => new List<string>()
            };
            foreach (var name in names.Where(LooksSecret))
                results.Add(new("DF060", LintSeverity.Warning, "Possible secret in " + DockerfileModel.Keyword(instruction.Kind),
                    $"{name} looks like a secret. ARG and ENV values are stored in the image history and metadata. Use RUN --mount=type=secret instead.", s, i));
        }
        for (var i = 0; i < document.Preamble.Count; i++) Inspect(-1, i, document.Preamble[i]);
        for (var s = 0; s < document.Stages.Count; s++)
            for (var i = 0; i < document.Stages[s].Instructions.Count; i++) Inspect(s, i, document.Stages[s].Instructions[i]);
    }

    public static bool LooksSecret(string name)
    {
        var parts = name.Trim().ToUpperInvariant().Split('_', StringSplitOptions.RemoveEmptyEntries);
        return parts.Any(p => SecretWords.Contains(p)) || parts.Any(p => p.EndsWith("PASSWORD", StringComparison.Ordinal) || p.EndsWith("TOKEN", StringComparison.Ordinal) || p.EndsWith("SECRET", StringComparison.Ordinal));
    }

    private static string Label(DockerfileDocument document, int s) => document.Stages[s].Name.Trim().Length > 0 ? document.Stages[s].Name.Trim() : "stage " + s.ToString(CultureInfo.InvariantCulture);
}
