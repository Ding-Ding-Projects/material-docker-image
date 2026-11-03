using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>One compiled .dockerignore pattern.</summary>
public sealed record DockerignorePattern(string Source, string Cleaned, bool Exception, Regex? Regex, int Depth, string? Error);

/// <summary>One build context entry with its result.</summary>
public sealed record DockerignoreEntry(string Path, bool IsDirectory, bool Excluded, long Size);

/// <summary>The result of checking a context folder: entries visited (bounded), totals and whether the walk stopped early.</summary>
public sealed record DockerignoreCheck(IReadOnlyList<DockerignoreEntry> Entries, int Included, int Excluded, bool Truncated, IReadOnlyList<string> Problems);

/// <summary>
/// Pure .dockerignore handling that follows Docker's rules (moby/patternmatcher): lines are trimmed, # starts a
/// comment, a leading ! makes an exception, paths are cleaned (so a trailing / and a leading / are ignored and
/// patterns are relative to the context root), * matches within one path segment, ? matches one character, **
/// matches any number of directories, and the last matching pattern decides. A pattern that matches a directory
/// also excludes everything inside it.
/// </summary>
public static class Dockerignore
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    public const int DefaultLimit = 2000;

    /// <summary>Preset pattern groups per language. "Common" applies to every project.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Presets = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["Common"] = [".git", ".gitignore", "**/.DS_Store", "**/*.log", ".env", ".env.*", "**/*.swp", ".vscode", ".idea", "Dockerfile*", ".dockerignore", "compose*.yaml", "docker-compose*.yml", "README.md"],
        ["Node.js"] = ["node_modules", "npm-debug.log*", "yarn-error.log", ".npm", ".next/cache", "dist", "coverage", ".npmrc"],
        ["Python"] = ["**/__pycache__", "**/*.py[cod]", ".venv", "venv", ".pytest_cache", ".mypy_cache", ".ruff_cache", "*.egg-info", "build", "dist"],
        ["Go"] = ["bin", "vendor", "*.test", "coverage.out"],
        [".NET"] = ["**/bin", "**/obj", "**/*.user", "**/TestResults", ".vs"],
        ["Rust"] = ["target"],
        ["Java"] = ["target", "build", ".gradle", "out", "*.class"],
        ["Static site"] = ["node_modules", ".cache"],
    };

    /// <summary>Parses .dockerignore text into entries (pattern or comment lines, blank lines removed).</summary>
    public static List<string> Lines(string text) => text.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    public static string Render(IEnumerable<string> lines) => string.Join("\n", lines.Select(l => l.Trim()).Where(l => l.Length > 0)) + "\n";

    /// <summary>Compiles the non-comment lines. Invalid patterns are returned with <see cref="DockerignorePattern.Error"/> set.</summary>
    public static List<DockerignorePattern> Compile(IEnumerable<string> lines)
    {
        var result = new List<DockerignorePattern>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var exception = line.StartsWith('!');
            var pattern = exception ? line[1..].Trim() : line;
            if (pattern.Length == 0) { result.Add(new(raw, "", true, null, 0, "\"!\" needs a pattern after it.")); continue; }
            var cleaned = Clean(pattern);
            if (cleaned.Length > 1 && cleaned.StartsWith('/')) cleaned = cleaned[1..];
            if (cleaned is "." or "") { result.Add(new(raw, cleaned, exception, null, 0, "This pattern refers to the whole context.")); continue; }
            try
            {
                var regex = new Regex(ToRegex(cleaned), RegexOptions.CultureInvariant, RegexTimeout);
                result.Add(new(raw, cleaned, exception, regex, cleaned.Split('/').Length, null));
            }
            catch (ArgumentException e) { result.Add(new(raw, cleaned, exception, null, 0, "Invalid pattern: " + e.Message)); }
        }
        return result;
    }

    /// <summary>Lexical path cleaning like Go's filepath.Clean on slash-separated paths.</summary>
    public static string Clean(string path)
    {
        path = path.Replace('\\', '/');
        var rooted = path.StartsWith('/');
        var parts = new List<string>();
        foreach (var part in path.Split('/'))
        {
            if (part.Length == 0 || part == ".") continue;
            if (part == "..")
            {
                if (parts.Count > 0 && parts[^1] != "..") parts.RemoveAt(parts.Count - 1);
                else if (!rooted) parts.Add("..");
                continue;
            }
            parts.Add(part);
        }
        var joined = string.Join("/", parts);
        return rooted ? "/" + joined : joined.Length == 0 ? "." : joined;
    }

    /// <summary>Translates one cleaned pattern to an anchored regular expression.</summary>
    public static string ToRegex(string pattern)
    {
        var sb = new StringBuilder("^");
        for (var k = 0; k < pattern.Length; k++)
        {
            var c = pattern[k];
            if (c == '*')
            {
                if (k + 1 < pattern.Length && pattern[k + 1] == '*')
                {
                    k++;
                    if (k + 1 < pattern.Length && pattern[k + 1] == '/') k++;
                    sb.Append(k + 1 >= pattern.Length ? ".*" : "(.*/)?");
                }
                else sb.Append("[^/]*");
            }
            else if (c == '?') sb.Append("[^/]");
            else if (c == '\\' && k + 1 < pattern.Length) sb.Append(Regex.Escape(pattern[++k].ToString()));
            else if (c == '[')
            {
                var close = pattern.IndexOf(']', k + 1);
                if (close < 0) throw new ArgumentException("unterminated character class");
                var body = pattern[(k + 1)..close];
                if (body.StartsWith('!')) body = "^" + body[1..];
                sb.Append('[').Append(body.Replace("\\", "\\\\")).Append(']');
                k = close;
            }
            else sb.Append(Regex.Escape(c.ToString()));
        }
        return sb.Append('$').ToString();
    }

    /// <summary>True when the relative path (slash separated) is excluded from the build context.</summary>
    public static bool IsExcluded(IReadOnlyList<DockerignorePattern> patterns, string relativePath)
    {
        var file = Clean(relativePath).TrimStart('/');
        var slash = file.LastIndexOf('/');
        var parentDirs = slash < 0 ? Array.Empty<string>() : file[..slash].Split('/');
        var matched = false;
        foreach (var pattern in patterns)
        {
            if (pattern.Regex == null) continue;
            // An inclusion only matters while not excluded; an exception only while excluded.
            if (pattern.Exception != matched) continue;
            var match = Matches(pattern.Regex, file);
            if (!match)
                for (var i = 0; i < parentDirs.Length && !match; i++)
                    match = Matches(pattern.Regex, string.Join("/", parentDirs.Take(i + 1)));
            if (match) matched = !pattern.Exception;
        }
        return matched;
    }

    private static bool Matches(Regex regex, string path)
    {
        try { return regex.IsMatch(path); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    /// <summary>
    /// Walks the context folder breadth first (sorted, symbolic links and junctions not followed) and reports each
    /// entry up to <paramref name="limit"/>. Directories excluded without any exception pattern are not descended,
    /// as Docker does.
    /// </summary>
    public static DockerignoreCheck Evaluate(string root, IEnumerable<string> lines, int limit = DefaultLimit)
    {
        var patterns = Compile(lines);
        var problems = patterns.Where(p => p.Error != null).Select(p => $"{p.Source}: {p.Error}").ToList();
        var hasExceptions = patterns.Any(p => p.Exception && p.Regex != null);
        var entries = new List<DockerignoreEntry>();
        var truncated = false;
        var queue = new Queue<string>();
        queue.Enqueue("");
        while (queue.Count > 0 && !truncated)
        {
            var relative = queue.Dequeue();
            var directory = relative.Length == 0 ? root : Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            IEnumerable<FileSystemInfo> children;
            try { children = new DirectoryInfo(directory).EnumerateFileSystemInfos().OrderBy(f => f is FileInfo).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { problems.Add((relative.Length == 0 ? "." : relative) + ": " + e.Message); continue; }
            foreach (var child in children)
            {
                if (entries.Count >= limit) { truncated = true; break; }
                var path = relative.Length == 0 ? child.Name : relative + "/" + child.Name;
                var isDirectory = child is DirectoryInfo;
                var excluded = IsExcluded(patterns, path);
                long size = child is FileInfo fi ? SafeLength(fi) : 0;
                entries.Add(new DockerignoreEntry(path, isDirectory, excluded, size));
                var link = child.Attributes.HasFlag(FileAttributes.ReparsePoint);
                if (isDirectory && !link && (!excluded || hasExceptions)) queue.Enqueue(path);
            }
        }
        return new DockerignoreCheck(entries, entries.Count(e => !e.Excluded), entries.Count(e => e.Excluded), truncated, problems);
    }

    private static long SafeLength(FileInfo file)
    {
        try { return file.Length; }
        catch (IOException) { return 0; }
    }
}
