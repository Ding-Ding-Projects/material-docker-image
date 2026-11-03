using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>
/// Lists build context entries for path suggestions: names only (file contents are never read, apart from the
/// .dockerignore pattern file), breadth first, bounded, skipping secret-looking names, excluded entries and
/// symbolic links. Folders end with "/".
/// </summary>
public static class DockerfileCompletionPaths
{
    public const int DefaultLimit = 500;
    private static readonly Regex SecretRx = new(@"^(\.env(\..*)?|.*\.(pem|key|pfx|p12|kdbx|jks|keystore)|id_(rsa|dsa|ecdsa|ed25519)(\.pub)?|\.npmrc|\.pypirc|\.netrc|\.git-credentials|\.ssh|\.aws|\.gnupg|\.docker|credentials(\..*)?|secrets?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> SkipDescend = new(StringComparer.OrdinalIgnoreCase) { ".git", "node_modules", ".venv", "venv", "__pycache__", "bin", "obj", "target", ".gradle", ".idea", ".vs" };

    /// <summary>True for names that commonly hold credentials or keys. They are never listed.</summary>
    public static bool IsSecretName(string name) => SecretRx.IsMatch(name);

    public static IReadOnlyList<string> List(string root, int limit = DefaultLimit)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return result;
        IReadOnlyList<DockerignorePattern> ignore = Array.Empty<DockerignorePattern>();
        try
        {
            var ignoreFile = Path.Combine(root, ".dockerignore");
            if (File.Exists(ignoreFile) && new FileInfo(ignoreFile).Length < 256 * 1024)
                ignore = Dockerignore.Compile(Dockerignore.Lines(File.ReadAllText(ignoreFile)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        var queue = new Queue<(string Path, string Relative)>();
        queue.Enqueue((root, ""));
        while (queue.Count > 0 && result.Count < limit)
        {
            var (dir, relative) = queue.Dequeue();
            IEnumerable<FileSystemInfo> entries;
            try { entries = new DirectoryInfo(dir).EnumerateFileSystemInfos().OrderBy(e => e is FileInfo).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { continue; }
            foreach (var entry in entries)
            {
                if (result.Count >= limit) break;
                if (IsSecretName(entry.Name)) continue;
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                var rel = relative + entry.Name;
                if (ignore.Count > 0 && Dockerignore.IsExcluded(ignore, rel)) continue;
                if (entry is DirectoryInfo)
                {
                    result.Add(rel + "/");
                    if (!SkipDescend.Contains(entry.Name)) queue.Enqueue((entry.FullName, rel + "/"));
                }
                else result.Add(rel);
            }
        }
        return result;
    }
}
