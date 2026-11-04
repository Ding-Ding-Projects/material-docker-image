using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace BuildCompanion;

public enum BuildAction { Build, BuildAndRun, BuildInstaller }

public enum RunState { Idle, Running, Succeeded, Failed, Cancelled, LaunchFailed }

/// <summary>One process the companion will start. Batch files always go through cmd.exe /c.</summary>
public sealed record PlanStep(string Label, string FileName, string Arguments, string WorkingDirectory, bool IsLaunch);

public sealed record BuildPlan(BuildAction Action, string RepositoryRoot, string? Version, IReadOnlyList<PlanStep> Steps)
{
    public const string BuildBat = "build.bat";
    public const string InstallerBat = "build-installer.bat";
    public static readonly string RelativeApplication = Path.Combine("out", "publish", "MaterialDockerImage.exe");

    /// <summary>Builds the exact command lines for an action, or throws ArgumentException for unsafe input.</summary>
    public static BuildPlan Create(BuildAction action, string repositoryRoot, string? version)
    {
        var root = ArgumentValidation.RequireSafeRoot(repositoryRoot);
        var normalized = ArgumentValidation.NormalizeVersion(version);
        if (action == BuildAction.BuildInstaller && normalized is null)
            throw new ArgumentException("A version such as 1.2.3 is required to build the installer.", nameof(version));
        var steps = new List<PlanStep>();
        if (action == BuildAction.BuildInstaller) steps.Add(BatchStep(InstallerBat, root, normalized));
        else steps.Add(BatchStep(BuildBat, root, normalized));
        if (action == BuildAction.BuildAndRun)
            steps.Add(new PlanStep("Launch", Path.Combine(root, RelativeApplication), "", root, true));
        return new BuildPlan(action, root, normalized, steps);
    }

    static PlanStep BatchStep(string bat, string root, string? version)
    {
        var script = Path.Combine(root, bat);
        // cmd /c strips the outer quote pair, so the quoted script path and arguments survive spaces in the path.
        var inner = "\"" + script + "\"" + (version is null ? "" : " -Version " + version);
        return new PlanStep(bat, "cmd.exe", "/d /c \"" + inner + "\"", root, false);
    }
}

public static class ArgumentValidation
{
    // ASCII-only digits and \z, so a trailing newline or a Unicode digit is refused.
    static readonly Regex VersionPattern = new(@"\A[0-9]+\.[0-9]+\.[0-9]+\z", RegexOptions.CultureInvariant);

    public static bool IsValidVersion(string? value) => value is { Length: > 0 and <= 32 } && VersionPattern.IsMatch(value);

    /// <summary>Returns null for an empty version, the trimmed version when valid, and throws otherwise.</summary>
    public static string? NormalizeVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (!IsValidVersion(trimmed)) throw new ArgumentException("Version must look like 1.2.3 (three numeric parts).", nameof(value));
        return trimmed;
    }

    /// <summary>A repository path is passed inside cmd quotes, so characters cmd would expand or terminate on are refused.</summary>
    public static bool IsSafeRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 240 || !Path.IsPathRooted(path)) return false;
        foreach (var c in path) if (char.IsControl(c) || c is '"' or '%' or '^' or '!' or '<' or '>' or '|') return false;
        return true;
    }

    public static string RequireSafeRoot(string? path)
    {
        if (!IsSafeRoot(path)) throw new ArgumentException("The repository path is empty, relative or contains characters that cmd.exe would interpret.", nameof(path));
        return Path.GetFullPath(path!).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}

public static class RunStates
{
    /// <summary>Success requires a real exit code of zero and no cancellation. Everything else is a failure state.</summary>
    public static RunState FromExit(int? exitCode, bool cancelled, bool launchFailed)
    {
        if (launchFailed) return RunState.LaunchFailed;
        if (cancelled) return RunState.Cancelled;
        return exitCode == 0 ? RunState.Succeeded : RunState.Failed;
    }
}
