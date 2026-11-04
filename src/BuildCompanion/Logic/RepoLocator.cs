using System;
using System.IO;

namespace BuildCompanion;

public sealed record RepoLookup(string? Path, string Source, string? Error);

public static class RepoLocator
{
    /// <summary>--repo path wins, then a walk up from the executable directory, otherwise the caller asks the user.</summary>
    public static RepoLookup Find(string[] args, string baseDirectory, Func<string, bool>? hasBuildBat = null)
    {
        hasBuildBat ??= dir => File.Exists(System.IO.Path.Combine(dir, BuildPlan.BuildBat));
        var requested = ReadRepoArgument(args);
        if (requested is not null)
        {
            if (!ArgumentValidation.IsSafeRoot(requested)) return new RepoLookup(null, "argument", "The --repo path is not an absolute, shell-safe path.");
            var full = System.IO.Path.GetFullPath(requested).TrimEnd('\\', '/');
            return hasBuildBat(full) ? new RepoLookup(full, "argument", null) : new RepoLookup(null, "argument", "The --repo folder does not contain build.bat.");
        }
        var current = string.IsNullOrWhiteSpace(baseDirectory) ? null : new DirectoryInfo(baseDirectory);
        for (var depth = 0; current is not null && depth < 12; depth++, current = current.Parent)
            if (hasBuildBat(current.FullName)) return new RepoLookup(current.FullName.TrimEnd('\\', '/'), "walk", null);
        return new RepoLookup(null, "none", null);
    }

    public static string? ReadRepoArgument(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--repo=", StringComparison.Ordinal)) return args[i][7..];
            if (args[i] == "--repo") return i + 1 < args.Length ? args[i + 1] : "";
        }
        return null;
    }
}
