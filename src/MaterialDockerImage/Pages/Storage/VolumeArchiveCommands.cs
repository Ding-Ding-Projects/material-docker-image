using System.Text.RegularExpressions;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Argument builders for volume backup, restore and clone with a short-lived helper container. No shell receives
/// user text: file names reach the helper as separate arguments, and the only shell scripts are constants.
/// </summary>
public static class VolumeArchiveCommands
{
    public const string DefaultHelperImage = "busybox:1.36";
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);

    public static string? ValidateVolumeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Enter a volume name.";
        return Regex.IsMatch(name, "^[A-Za-z0-9][A-Za-z0-9_.-]*$", RegexOptions.None, Timeout) ? null : "Volume names start with a letter or digit and use letters, digits, '_', '.' or '-'.";
    }

    public static string? ValidateArchiveName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Enter an archive file name.";
        if (name.StartsWith('-') || name.StartsWith('.')) return "The archive name cannot start with '-' or '.'.";
        if (!Regex.IsMatch(name, @"^[A-Za-z0-9_.+-]+$", RegexOptions.None, Timeout)) return "Use letters, digits, '_', '.', '+' or '-' in the archive name.";
        if (!(name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))) return "The archive name ends with .tar.gz, .tgz or .tar.";
        return null;
    }

    /// <summary>Folders are passed in a --mount value, which is comma-separated, so commas and quotes are refused.</summary>
    public static string? ValidateHostFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return "Choose a folder.";
        if (folder.Contains(',') || folder.Contains('"') || folder.Any(char.IsControl)) return "The folder path cannot contain commas, quotes or control characters. Choose a simpler path.";
        return null;
    }

    public static string? ValidateImage(string image)
    {
        if (string.IsNullOrWhiteSpace(image) || image.StartsWith('-') || image.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) return "Enter a helper image such as busybox:1.36. It needs sh, tar and find.";
        return null;
    }

    private static List<string> RunPrefix(string image, params string[] mounts)
    {
        var args = new List<string> { "run", "--rm", "--network", "none" };
        foreach (var m in mounts) args.AddRange(["--mount", m]);
        args.Add(image);
        return args;
    }

    /// <summary>tar czf /backup/ARCHIVE -C /source . with the volume mounted read-only.</summary>
    public static List<string> Backup(string volume, string folder, string archive, string image)
    {
        var args = RunPrefix(image, $"type=volume,source={volume},target=/source,readonly", $"type=bind,source={folder},target=/backup");
        var compress = archive.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) ? "cf" : "czf";
        args.AddRange(["tar", compress, "/backup/" + archive, "-C", "/source", "."]);
        return args;
    }

    /// <summary>Extracts ARCHIVE into the volume. With replace, the volume is emptied first by a constant script.</summary>
    public static List<string> Restore(string volume, string folder, string archive, string image, bool replace)
    {
        var args = RunPrefix(image, $"type=volume,source={volume},target=/target", $"type=bind,source={folder},target=/backup,readonly");
        var extract = archive.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) ? "xf" : "xzf";
        if (replace) args.AddRange(["sh", "-c", $"find /target -mindepth 1 -delete && tar {extract} \"$1\" -C /target", "restore", "/backup/" + archive]);
        else args.AddRange(["tar", extract, "/backup/" + archive, "-C", "/target"]);
        return args;
    }

    /// <summary>Copies every file of SOURCE into TARGET through a tar stream inside one helper container.</summary>
    public static List<string> Clone(string source, string target, string image)
    {
        var args = RunPrefix(image, $"type=volume,source={source},target=/from,readonly", $"type=volume,source={target},target=/to");
        args.AddRange(["sh", "-c", "cd /from && tar cf - . | tar xf - -C /to"]);
        return args;
    }

    public static string SuggestedArchiveName(string volume) => $"{(volume.Length > 40 ? volume[..12] : volume)}-{DateTime.Now:yyyyMMdd-HHmmss}.tar.gz";
}
