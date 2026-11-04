using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>Where and how a folder search looks. Every limit has a hard ceiling that the caller cannot raise.</summary>
public sealed record RegexGrepOptions
{
    public const long HardMaxFileBytes = 16L * 1024 * 1024;
    public const int HardMaxFiles = 20000;
    public const int HardMaxResults = 5000;

    public string Folder { get; init; } = "";
    /// <summary>Glob list separated by ; or , (for example "*.log;*.txt;!*.min.js"). A leading ! excludes. Empty means every file.</summary>
    public string Globs { get; init; } = "*";
    public bool Recursive { get; init; } = true;
    public long MaxFileBytes { get; init; } = 2L * 1024 * 1024;
    public int MaxFiles { get; init; } = 5000;
    public int MaxResults { get; init; } = 1000;
    public int MaxMatchesPerFile { get; init; } = 200;
    public long MaxTotalBytes { get; init; } = 128L * 1024 * 1024;
    public TimeSpan Budget { get; init; } = TimeSpan.FromSeconds(15);
    public IReadOnlyList<string> ExcludedDirectories { get; init; } = [".git", "node_modules", ".vs"];
    /// <summary>Only files last written at or after this time (UTC) are searched. Null means no lower bound.</summary>
    public DateTime? ModifiedAfterUtc { get; init; }
    /// <summary>Only files last written at or before this time (UTC) are searched. Null means no upper bound.</summary>
    public DateTime? ModifiedBeforeUtc { get; init; }
    /// <summary>Lines of context listed before and after each match in a search (0 to <see cref="RegexGrep.MaxContextLines"/>).</summary>
    public int ContextLines { get; init; }
    /// <summary>Whether hidden files and folders are searched. On by default, which is how folder searches always behaved.</summary>
    public bool IncludeHidden { get; init; } = true;
    /// <summary>Whether read-only files are searched. A replacement never edits a read-only file whatever this says.</summary>
    public bool IncludeReadOnly { get; init; } = true;
    /// <summary>Whether files and folders with the system attribute are searched.</summary>
    public bool IncludeSystem { get; init; } = true;
    /// <summary>How a file without a byte order mark is read and written back. Auto means UTF-8. A byte order mark always wins.</summary>
    public RegexGrepEncoding BomlessEncoding { get; init; } = RegexGrepEncoding.Auto;
}

/// <summary>The encoding assumed for a file that has no byte order mark.</summary>
public enum RegexGrepEncoding { Auto, Utf16Le, Utf16Be, Windows1252, Latin1 }

/// <summary>One match in one file. <see cref="Column"/> counts UTF-16 units from 1.</summary>
public sealed record RegexGrepMatch(string File, int Line, int Column, int Length, string Text, string LineText)
{
    /// <summary>The lines just before the match line, oldest first. Empty unless context lines were requested.</summary>
    public IReadOnlyList<string> ContextBefore { get; init; } = [];
    /// <summary>The lines just after the match line.</summary>
    public IReadOnlyList<string> ContextAfter { get; init; } = [];
}

/// <summary>A file that was left out, with the reason.</summary>
public sealed record RegexGrepSkip(string File, string Reason);

/// <summary>Outcome of a folder search.</summary>
public sealed record RegexGrepResult(
    bool Valid, string? Error, IReadOnlyList<RegexGrepMatch> Matches, int FilesSearched, int FilesMatched,
    IReadOnlyList<RegexGrepSkip> Skipped, bool ResultsTruncated, bool FileLimitReached, bool ByteBudgetReached, bool TimedOut, bool Cancelled, double ElapsedMilliseconds)
{
    public string Summary()
    {
        if (!Valid) return Error ?? "Invalid search.";
        var text = Matches.Count == 0 ? $"No match in {FilesSearched} file{(FilesSearched == 1 ? "" : "s")}" : $"{Matches.Count}{(ResultsTruncated ? "+" : "")} match{(Matches.Count == 1 ? "" : "es")} in {FilesMatched} of {FilesSearched} file{(FilesSearched == 1 ? "" : "s")}";
        if (ResultsTruncated) text += "; the result list was cut at its limit";
        if (FileLimitReached) text += "; the file limit was reached, so later files were not searched";
        if (ByteBudgetReached) text += "; the total size limit was reached, so later files were not searched";
        if (TimedOut) text += "; the time budget ended the search";
        if (Cancelled) text += "; cancelled";
        if (Skipped.Count > 0) text += $"; {Skipped.Count} skipped";
        return text + ".";
    }
}

/// <summary>One shown change of a replacement plan.</summary>
public sealed record RegexGrepChange(int Line, int Column, string Before, string After);

/// <summary>What would happen to one file.</summary>
public sealed record RegexGrepFilePlan(string File, int Replacements, long OriginalLength, string OriginalSha256, IReadOnlyList<RegexGrepChange> Changes);

/// <summary>The dry run of a replacement in files. Nothing is written until <see cref="RegexGrep.Apply"/>.</summary>
public sealed record RegexGrepPlan(
    bool Valid, string? Error, string Pattern, RegexBuilderFlags Flags, string Replacement, RegexGrepOptions Options,
    IReadOnlyList<RegexGrepFilePlan> Files, IReadOnlyList<RegexGrepSkip> Skipped, int FilesSearched, bool Truncated, bool TimedOut, bool Cancelled)
{
    public int TotalReplacements => Files.Sum(f => f.Replacements);
    public long TotalBytes => Files.Sum(f => f.OriginalLength);
}

/// <summary>Outcome of applying a plan.</summary>
public sealed record RegexGrepApplyResult(bool Success, string? Error, string? SnapshotId, int FilesChanged, int Replacements, IReadOnlyList<RegexGrepSkip> Skipped, bool RolledBack);

/// <summary>Outcome of undoing an applied plan.</summary>
public sealed record RegexGrepUndoResult(bool Success, string? Error, int FilesRestored, IReadOnlyList<RegexGrepSkip> Skipped);

/// <summary>An undo snapshot as listed from disk.</summary>
public sealed record RegexGrepSnapshotInfo(string Id, DateTimeOffset Created, string State, int Files, int Replacements, string Pattern, string Replacement);

/// <summary>
/// Search and replace across the files of a folder. Pure logic with hard bounds: file size, file count, total bytes, result count and
/// time. Binary files are skipped, reparse points are never followed, the dry run writes nothing, and applying writes a byte-exact undo
/// snapshot first and rolls back if any write fails.
/// </summary>
public static class RegexGrep
{
    public const int MaxChangesShownPerFile = 5;
    public const int MaxReplacementsPerFile = 10000;
    public const int MaxApplyFiles = 500;
    public const long MaxApplyBytes = 256L * 1024 * 1024;
    public const int MaxSnapshots = 10;
    public const int MaxLineChars = 240;
    public const int MaxSkipsListed = 200;
    public const int MaxContextLines = 5;
    private const string SnapshotFormat = "material-docker-image.regex-grep-undo";

    // ----- file text -----

    private enum TextKind { Utf8, Utf8Bom, Utf16Le, Utf16Be, Utf32Le, Utf32Be, Utf16LeNoBom, Utf16BeNoBom, Windows1252, Latin1 }

    private sealed record FileText(string Text, TextKind Kind, bool RoundTrips, byte[] Bytes);

    private static readonly Lazy<Encoding?> Cp1252 = new(() =>
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException) { return null; }
    });

    private static Encoding EncodingOf(TextKind kind) => kind switch
    {
        TextKind.Utf16Le or TextKind.Utf16LeNoBom => new UnicodeEncoding(false, false, true),
        TextKind.Utf16Be or TextKind.Utf16BeNoBom => new UnicodeEncoding(true, false, true),
        TextKind.Utf32Le => new UTF32Encoding(false, false, true),
        TextKind.Utf32Be => new UTF32Encoding(true, false, true),
        TextKind.Windows1252 => Cp1252.Value ?? Encoding.Latin1,
        TextKind.Latin1 => Encoding.GetEncoding("iso-8859-1", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
        _ => new UTF8Encoding(false, true),
    };

    private static byte[] Preamble(TextKind kind) => kind switch
    {
        TextKind.Utf8Bom => [0xEF, 0xBB, 0xBF],
        TextKind.Utf16Le => [0xFF, 0xFE],
        TextKind.Utf16Be => [0xFE, 0xFF],
        TextKind.Utf32Le => [0xFF, 0xFE, 0x00, 0x00],
        TextKind.Utf32Be => [0x00, 0x00, 0xFE, 0xFF],
        _ => [],
    };

    private static bool IsWide(TextKind kind) => kind is TextKind.Utf16Le or TextKind.Utf16Be or TextKind.Utf32Le or TextKind.Utf32Be or TextKind.Utf16LeNoBom or TextKind.Utf16BeNoBom;

    private static byte[] Encode(string text, TextKind kind)
    {
        var body = EncodingOf(kind).GetBytes(text);
        var pre = Preamble(kind);
        if (pre.Length == 0) return body;
        var all = new byte[pre.Length + body.Length];
        pre.CopyTo(all, 0); body.CopyTo(all, pre.Length);
        return all;
    }

    /// <summary>Decodes a text file, or returns null with the reason when it is binary. A file that does not round-trip is searchable but not editable.</summary>
    private static FileText? Decode(byte[] bytes, RegexGrepEncoding bomless, out string? skip)
    {
        skip = null;
        TextKind kind;
        int offset;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) { kind = TextKind.Utf8Bom; offset = 3; }
        else if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0) { kind = TextKind.Utf32Le; offset = 4; }
        else if (bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xFE && bytes[3] == 0xFF) { kind = TextKind.Utf32Be; offset = 4; }
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) { kind = TextKind.Utf16Le; offset = 2; }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) { kind = TextKind.Utf16Be; offset = 2; }
        else
        {
            kind = bomless switch
            {
                RegexGrepEncoding.Utf16Le => TextKind.Utf16LeNoBom,
                RegexGrepEncoding.Utf16Be => TextKind.Utf16BeNoBom,
                RegexGrepEncoding.Windows1252 => TextKind.Windows1252,
                RegexGrepEncoding.Latin1 => TextKind.Latin1,
                _ => TextKind.Utf8,
            };
            offset = 0;
            // A NUL byte means binary, except in an encoding where NUL is part of every ordinary character.
            if (!IsWide(kind))
            {
                var probe = Math.Min(bytes.Length, 8192);
                for (var i = 0; i < probe; i++) if (bytes[i] == 0) { skip = "binary file (contains a NUL byte)"; return null; }
            }
        }
        string text;
        try { text = EncodingOf(kind).GetString(bytes, offset, bytes.Length - offset); }
        catch (Exception e) when (e is DecoderFallbackException or ArgumentException)
        {
            // Not valid in its own encoding: still searchable as lossy UTF-8, never editable.
            var lossy = Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
            return new FileText(lossy, kind, false, bytes);
        }
        return new FileText(text, kind, Encode(text, kind).AsSpan().SequenceEqual(bytes), bytes);
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    // ----- globs -----

    /// <summary>Compiled include and exclude globs. Matches the file name, or the relative path when the glob contains a slash.</summary>
    public sealed class GlobSet
    {
        private readonly List<(Regex Regex, bool PathMode)> includes = new(), excludes = new();
        public string? Error { get; }
        public bool Any => includes.Count > 0;

        public GlobSet(string? text)
        {
            var parts = (text ?? "").Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 32) { Error = "Use at most 32 file patterns."; return; }
            foreach (var raw in parts)
            {
                var exclude = raw.StartsWith('!');
                var glob = (exclude ? raw[1..] : raw).Trim().Replace('\\', '/');
                if (glob.Length == 0) continue;
                if (glob.Length > 200) { Error = "A file pattern is longer than 200 characters."; return; }
                var pathMode = glob.Contains('/');
                var sb = new StringBuilder("^");
                for (var i = 0; i < glob.Length; i++)
                {
                    var c = glob[i];
                    if (c == '*') { if (i + 1 < glob.Length && glob[i + 1] == '*') { sb.Append(".*"); i++; } else sb.Append(pathMode ? "[^/]*" : ".*"); }
                    else if (c == '?') sb.Append(pathMode ? "[^/]" : ".");
                    else sb.Append(Regex.Escape(c.ToString()));
                }
                sb.Append('$');
                (exclude ? excludes : includes).Add((new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking), pathMode));
            }
            if (includes.Count == 0 && Error == null) includes.Add((new Regex("^.*$", RegexOptions.NonBacktracking), false));
        }

        public bool IsMatch(string name, string relativePath)
        {
            var rel = relativePath.Replace('\\', '/');
            bool Hit((Regex Regex, bool PathMode) g) => g.Regex.IsMatch(g.PathMode ? rel : name);
            return includes.Any(Hit) && !excludes.Any(Hit);
        }
    }

    // ----- enumeration -----

    private static IEnumerable<(string Full, string Relative)> Files(RegexGrepOptions o, List<RegexGrepSkip> skipped, Func<bool> stop)
    {
        var root = Path.GetFullPath(o.Folder);
        var stack = new Stack<string>();
        stack.Push(root);
        var excluded = new HashSet<string>(o.ExcludedDirectories, StringComparer.OrdinalIgnoreCase);
        while (stack.Count > 0 && !stop())
        {
            var dir = stack.Pop();
            FileSystemInfo[] entries;
            try { entries = new DirectoryInfo(dir).GetFileSystemInfos().OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToArray(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Skip(skipped, dir, "folder could not be read"); continue; }
            var subdirectories = new List<string>();
            foreach (var entry in entries)
            {
                if (stop()) yield break;
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) { Skip(skipped, entry.FullName, "link or junction (not followed)"); continue; }
                if (!o.IncludeHidden && (entry.Attributes & FileAttributes.Hidden) != 0) { Skip(skipped, entry.FullName, "hidden (hidden items are not searched)"); continue; }
                if (!o.IncludeSystem && (entry.Attributes & FileAttributes.System) != 0) { Skip(skipped, entry.FullName, "system item (system items are not searched)"); continue; }
                if (!o.IncludeReadOnly && (entry.Attributes & FileAttributes.Directory) == 0 && (entry.Attributes & FileAttributes.ReadOnly) != 0) { Skip(skipped, entry.FullName, "read-only (read-only files are not searched)"); continue; }
                if ((entry.Attributes & FileAttributes.Directory) != 0)
                {
                    if (o.Recursive && !excluded.Contains(entry.Name)) subdirectories.Add(entry.FullName);
                    continue;
                }
                yield return (entry.FullName, Path.GetRelativePath(root, entry.FullName));
            }
            for (var i = subdirectories.Count - 1; i >= 0; i--) stack.Push(subdirectories[i]);
        }
    }

    /// <summary>True when the file's last write time is inside the optional date window.</summary>
    private static bool InDateWindow(RegexGrepOptions o, string path)
    {
        if (o.ModifiedAfterUtc == null && o.ModifiedBeforeUtc == null) return true;
        try
        {
            var written = File.GetLastWriteTimeUtc(path);
            return (o.ModifiedAfterUtc == null || written >= o.ModifiedAfterUtc) && (o.ModifiedBeforeUtc == null || written <= o.ModifiedBeforeUtc);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    private static void Skip(List<RegexGrepSkip> skipped, string file, string reason)
    {
        if (skipped.Count < MaxSkipsListed) skipped.Add(new(file, reason));
    }

    private static (RegexGrepOptions Options, string? Error) Normalize(RegexGrepOptions o)
    {
        if (string.IsNullOrWhiteSpace(o.Folder)) return (o, "Choose a folder to search.");
        string full;
        try { full = Path.GetFullPath(o.Folder.Trim().Trim('"')); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return (o, "The folder path is not valid."); }
        if (!Directory.Exists(full)) return (o, "The folder does not exist.");
        var root = Path.GetPathRoot(full);
        if (string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), (root ?? "").TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            return (o, "Choose a folder, not a whole drive.");
        return (o with
        {
            Folder = full,
            MaxFileBytes = Math.Clamp(o.MaxFileBytes, 1, RegexGrepOptions.HardMaxFileBytes),
            MaxFiles = Math.Clamp(o.MaxFiles, 1, RegexGrepOptions.HardMaxFiles),
            MaxResults = Math.Clamp(o.MaxResults, 1, RegexGrepOptions.HardMaxResults),
            MaxMatchesPerFile = Math.Clamp(o.MaxMatchesPerFile, 1, 10000),
            MaxTotalBytes = Math.Clamp(o.MaxTotalBytes, 1, 1024L * 1024 * 1024),
            ContextLines = Math.Clamp(o.ContextLines, 0, MaxContextLines),
            BomlessEncoding = Enum.IsDefined(o.BomlessEncoding) ? o.BomlessEncoding : RegexGrepEncoding.Auto,
        }, o.ModifiedAfterUtc is { } a && o.ModifiedBeforeUtc is { } b && a > b ? "The earliest modified time is later than the latest one, so no file can match." : null);
    }

    // ----- search -----

    private static int[] LineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++) if (text[i] == '\n') starts.Add(i + 1);
        return starts.ToArray();
    }

    private static string LineAt(string text, int[] starts, int line)
    {
        var from = starts[line];
        var to = line + 1 < starts.Length ? starts[line + 1] - 1 : text.Length;
        if (to > from && text[to - 1] == '\r') to--;
        var s = text[from..Math.Max(from, to)];
        return s.Length > MaxLineChars ? s[..MaxLineChars] + "…" : s;
    }

    private static string Bound(string s) => s.Length > MaxLineChars ? s[..MaxLineChars] + "…" : s;

    /// <summary>Searches the files of a folder. Never throws; every limit ends the search with a flag instead.</summary>
    public static RegexGrepResult Search(string pattern, RegexBuilderFlags flags, RegexGrepOptions options, CancellationToken cancellation = default)
    {
        var (o, problem) = Normalize(options);
        if (problem != null) return Fail(problem);
        var compiled = RegexBuilderEngine.Compile(pattern, flags);
        if (!compiled.Valid) return Fail(compiled.Error ?? "Invalid pattern.");
        if (pattern.Length == 0) return Fail("Enter a pattern to search for.");
        var globs = new GlobSet(o.Globs);
        if (globs.Error != null) return Fail(globs.Error);
        var regex = compiled.Regex!;
        var clock = Stopwatch.StartNew();
        var matches = new List<RegexGrepMatch>();
        var skipped = new List<RegexGrepSkip>();
        int searched = 0, matchedFiles = 0;
        bool truncated = false, fileLimit = false, byteBudget = false, timedOut = false, cancelled = false;
        long totalBytes = 0;
        Func<bool> stop = () => cancellation.IsCancellationRequested || clock.Elapsed > o.Budget || truncated;
        foreach (var (full, relative) in Files(o, skipped, stop))
        {
            if (cancellation.IsCancellationRequested) { cancelled = true; break; }
            if (clock.Elapsed > o.Budget) { timedOut = true; break; }
            if (!globs.IsMatch(Path.GetFileName(full), relative)) continue;
            if (!InDateWindow(o, full)) continue;
            if (searched >= o.MaxFiles) { fileLimit = true; break; }
            var bytes = ReadFile(full, o.MaxFileBytes, skipped);
            if (bytes == null) continue;
            if (totalBytes + bytes.Length > o.MaxTotalBytes) { byteBudget = true; break; }
            totalBytes += bytes.Length;
            var file = Decode(bytes, o.BomlessEncoding, out var skipReason);
            if (file == null) { Skip(skipped, full, skipReason!); continue; }
            searched++;
            var hits = 0;
            int[]? starts = null;
            try
            {
                var m = regex.Match(file.Text);
                while (m.Success)
                {
                    if (cancellation.IsCancellationRequested) { cancelled = true; break; }
                    if (clock.Elapsed > o.Budget) { timedOut = true; break; }
                    if (matches.Count >= o.MaxResults) { truncated = true; break; }
                    if (hits >= o.MaxMatchesPerFile) { Skip(skipped, full, $"only the first {o.MaxMatchesPerFile} matches of this file are listed"); break; }
                    starts ??= LineStarts(file.Text);
                    var line = Array.BinarySearch(starts, m.Index);
                    if (line < 0) line = ~line - 1;
                    var hit = new RegexGrepMatch(full, line + 1, m.Index - starts[line] + 1, m.Length, Bound(m.Value), LineAt(file.Text, starts, line));
                    if (o.ContextLines > 0)
                        hit = hit with
                        {
                            ContextBefore = Enumerable.Range(Math.Max(0, line - o.ContextLines), line - Math.Max(0, line - o.ContextLines)).Select(i => LineAt(file.Text, starts, i)).ToList(),
                            ContextAfter = Enumerable.Range(line + 1, Math.Max(0, Math.Min(starts.Length - 1, line + o.ContextLines) - line)).Select(i => LineAt(file.Text, starts, i)).ToList(),
                        };
                    matches.Add(hit);
                    hits++;
                    m = m.NextMatch();
                }
            }
            catch (RegexMatchTimeoutException) { Skip(skipped, full, $"the pattern exceeded the {RegexBuilderEngine.MatchTimeout.TotalMilliseconds:0} ms limit in this file"); }
            if (hits > 0) matchedFiles++;
            if (cancelled || timedOut) break;
        }
        if (cancellation.IsCancellationRequested) cancelled = true;
        if (clock.Elapsed > o.Budget && !cancelled) timedOut = true;
        return new(true, null, matches, searched, matchedFiles, skipped, truncated, fileLimit, byteBudget, timedOut, cancelled, clock.Elapsed.TotalMilliseconds);

        static RegexGrepResult Fail(string error) => new(false, error, [], 0, 0, [], false, false, false, false, false, 0);
    }

    private static byte[]? ReadFile(string path, long maxBytes, List<RegexGrepSkip> skipped)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > maxBytes) { Skip(skipped, path, $"larger than {maxBytes / 1024} KiB"); return null; }
            var bytes = new byte[stream.Length];
            var read = 0;
            while (read < bytes.Length) { var n = stream.Read(bytes, read, bytes.Length - read); if (n == 0) break; read += n; }
            return read == bytes.Length ? bytes : bytes[..read];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Skip(skipped, path, "could not be read"); return null; }
    }

    // ----- replace in files: dry run -----

    /// <summary>The dry run: what a replacement would change, file by file. Writes nothing and needs no backup.</summary>
    public static RegexGrepPlan PlanReplace(string pattern, RegexBuilderFlags flags, string replacement, RegexGrepOptions options, CancellationToken cancellation = default)
    {
        replacement ??= "";
        var (o, problem) = Normalize(options);
        if (problem != null) return Bad(problem);
        var compiled = RegexBuilderEngine.Compile(pattern, flags);
        if (!compiled.Valid) return Bad(compiled.Error ?? "Invalid pattern.");
        if (pattern.Length == 0) return Bad("Enter a pattern to search for.");
        var globs = new GlobSet(o.Globs);
        if (globs.Error != null) return Bad(globs.Error);
        var regex = compiled.Regex!;
        var clock = Stopwatch.StartNew();
        var plans = new List<RegexGrepFilePlan>();
        var skipped = new List<RegexGrepSkip>();
        int searched = 0;
        long total = 0;
        bool truncated = false, timedOut = false, cancelled = false;
        Func<bool> stop = () => cancellation.IsCancellationRequested || clock.Elapsed > o.Budget || truncated;
        foreach (var (full, relative) in Files(o, skipped, stop))
        {
            if (cancellation.IsCancellationRequested) { cancelled = true; break; }
            if (clock.Elapsed > o.Budget) { timedOut = true; break; }
            if (!globs.IsMatch(Path.GetFileName(full), relative)) continue;
            if (!InDateWindow(o, full)) continue;
            if (searched >= o.MaxFiles) { truncated = true; Skip(skipped, full, "the file limit was reached"); break; }
            var bytes = ReadFile(full, o.MaxFileBytes, skipped);
            if (bytes == null) continue;
            var file = Decode(bytes, o.BomlessEncoding, out var reason);
            if (file == null) { Skip(skipped, full, reason!); continue; }
            searched++;
            if (!file.RoundTrips) { if (regex.IsMatch(file.Text)) Skip(skipped, full, "not valid in its own encoding, so it is shown in searches but never edited"); continue; }
            var edit = ReplaceInText(regex, file.Text, replacement, withChanges: true, cancellation, out var timeout);
            if (timeout) { Skip(skipped, full, $"the pattern exceeded the {RegexBuilderEngine.MatchTimeout.TotalMilliseconds:0} ms limit in this file"); continue; }
            if (edit == null || edit.Count == 0) continue;
            if (IsReadOnly(full)) { Skip(skipped, full, "read-only file"); continue; }
            if (plans.Count >= MaxApplyFiles) { truncated = true; Skip(skipped, full, $"a replacement plan covers at most {MaxApplyFiles} files"); break; }
            if (total + bytes.Length > MaxApplyBytes) { truncated = true; Skip(skipped, full, "the plan reached its total size limit"); break; }
            total += bytes.Length;
            plans.Add(new(full, edit.Count, bytes.Length, Sha(bytes), edit.Changes));
        }
        if (cancellation.IsCancellationRequested) cancelled = true;
        if (clock.Elapsed > o.Budget && !cancelled) timedOut = true;
        return new(true, null, pattern, flags, replacement, o, plans, skipped, searched, truncated, timedOut, cancelled);

        RegexGrepPlan Bad(string error) => new(false, error, pattern, flags, replacement, options, [], [], 0, false, false, false);
    }

    private static bool IsReadOnly(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return true; }
    }

    private sealed record TextEdit(string Text, int Count, IReadOnlyList<RegexGrepChange> Changes);

    private static TextEdit? ReplaceInText(Regex regex, string text, string replacement, bool withChanges, CancellationToken cancellation, out bool timedOut)
    {
        timedOut = false;
        var count = 0;
        var changes = new List<RegexGrepChange>();
        int[]? starts = null;
        try
        {
            var output = regex.Replace(text, m =>
            {
                cancellation.ThrowIfCancellationRequested();
                count++;
                var after = m.Result(replacement);
                if (withChanges && changes.Count < MaxChangesShownPerFile)
                {
                    starts ??= LineStarts(text);
                    var line = Array.BinarySearch(starts, m.Index);
                    if (line < 0) line = ~line - 1;
                    changes.Add(new(line + 1, m.Index - starts[line] + 1, Bound(m.Value), Bound(after)));
                }
                return after;
            }, MaxReplacementsPerFile);
            return new(output, count, changes);
        }
        catch (RegexMatchTimeoutException) { timedOut = true; return null; }
        catch (OperationCanceledException) { return null; }
    }

    // ----- replace in files: apply and undo -----

    /// <summary>
    /// Applies a plan. Every file is re-read and must still have the planned content (SHA-256), otherwise it is skipped. A byte-exact
    /// snapshot of every file to change is written before the first write; if any write fails, files already written are restored.
    /// </summary>
    public static RegexGrepApplyResult Apply(RegexGrepPlan plan, string snapshotRoot, CancellationToken cancellation = default)
    {
        if (!plan.Valid) return new(false, plan.Error, null, 0, 0, [], false);
        if (plan.Files.Count == 0) return new(false, "The plan changes no file.", null, 0, 0, [], false);
        if (plan.Files.Count > MaxApplyFiles || plan.TotalBytes > MaxApplyBytes) return new(false, "The plan is larger than an undoable replacement may be.", null, 0, 0, [], false);
        var compiled = RegexBuilderEngine.Compile(plan.Pattern, plan.Flags);
        if (!compiled.Valid) return new(false, compiled.Error, null, 0, 0, [], false);
        var skipped = new List<RegexGrepSkip>();
        var work = new List<(RegexGrepFilePlan Plan, byte[] Original, byte[] Updated, int Count)>();
        foreach (var file in plan.Files)
        {
            if (cancellation.IsCancellationRequested) return new(false, "Cancelled before anything was written.", null, 0, 0, skipped, false);
            var bytes = ReadFile(file.File, plan.Options.MaxFileBytes, skipped);
            if (bytes == null) continue;
            if (Sha(bytes) != file.OriginalSha256) { Skip(skipped, file.File, "changed since the preview, so it was not touched"); continue; }
            var text = Decode(bytes, plan.Options.BomlessEncoding, out _);
            if (text is not { RoundTrips: true }) { Skip(skipped, file.File, "not editable"); continue; }
            var edit = ReplaceInText(compiled.Regex!, text.Text, plan.Replacement, withChanges: false, cancellation, out var timeout);
            if (edit == null || timeout || edit.Count == 0) { Skip(skipped, file.File, timeout ? "the pattern timed out" : "nothing to replace any more"); continue; }
            var updated = Encode(edit.Text, text.Kind);
            if (updated.Length > RegexGrepOptions.HardMaxFileBytes) { Skip(skipped, file.File, "the result would be too large"); continue; }
            work.Add((file, bytes, updated, edit.Count));
        }
        if (work.Count == 0) return new(false, "No file could be changed.", null, 0, 0, skipped, false);

        string id;
        string dir;
        try
        {
            id = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
            dir = Path.Combine(snapshotRoot, id);
            Directory.CreateDirectory(Path.Combine(dir, "files"));
            for (var i = 0; i < work.Count; i++) File.WriteAllBytes(Path.Combine(dir, "files", i + ".bin"), work[i].Original);
            WriteManifest(dir, id, "pending", plan, work.Select(w => (w.Plan.File, w.Plan.OriginalSha256, "", w.Count)).ToList());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new(false, "The undo snapshot could not be written, so nothing was changed: " + e.Message, null, 0, 0, skipped, false);
        }

        var written = new List<int>();
        try
        {
            for (var i = 0; i < work.Count; i++)
            {
                if (cancellation.IsCancellationRequested) throw new OperationCanceledException();
                WriteReplacing(work[i].Plan.File, work[i].Updated);
                written.Add(i);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            foreach (var i in written)
            {
                try { WriteReplacing(work[i].Plan.File, work[i].Original); } catch (Exception inner) when (inner is IOException or UnauthorizedAccessException) { Skip(skipped, work[i].Plan.File, "could not be restored: " + inner.Message); }
            }
            try { WriteManifest(dir, id, "rolled-back", plan, work.Select(w => (w.Plan.File, w.Plan.OriginalSha256, "", w.Count)).ToList()); } catch (Exception m) when (m is IOException or UnauthorizedAccessException) { }
            return new(false, (e is OperationCanceledException ? "Cancelled; " : "A file could not be written; ") + "the files already written were restored.", id, 0, 0, skipped, true);
        }
        WriteManifest(dir, id, "applied", plan, work.Select(w => (w.Plan.File, w.Plan.OriginalSha256, Sha(w.Updated), w.Count)).ToList());
        Prune(snapshotRoot);
        return new(true, null, id, work.Count, work.Sum(w => w.Count), skipped, false);
    }

    private static void WriteReplacing(string path, byte[] content)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".mditmp";
        try
        {
            File.WriteAllBytes(temp, content);
            File.Replace(temp, path, null);
        }
        finally { if (File.Exists(temp)) { try { File.Delete(temp); } catch (IOException) { } } }
    }

    private static void WriteManifest(string dir, string id, string state, RegexGrepPlan plan, List<(string File, string Before, string After, int Count)> files)
    {
        using var stream = new MemoryStream();
        using (var w = new System.Text.Json.Utf8JsonWriter(stream, new System.Text.Json.JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("format", SnapshotFormat);
            w.WriteNumber("version", 1);
            w.WriteString("id", id);
            w.WriteString("created", DateTimeOffset.UtcNow);
            w.WriteString("state", state);
            w.WriteString("pattern", plan.Pattern);
            w.WriteString("replacement", plan.Replacement);
            w.WriteStartArray("files");
            for (var i = 0; i < files.Count; i++)
            {
                w.WriteStartObject();
                w.WriteString("path", files[i].File);
                w.WriteString("snapshot", "files/" + i + ".bin");
                w.WriteString("sha256Before", files[i].Before);
                w.WriteString("sha256After", files[i].After);
                w.WriteNumber("replacements", files[i].Count);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        var temp = Path.Combine(dir, "manifest.json.tmp");
        File.WriteAllBytes(temp, stream.ToArray());
        File.Move(temp, Path.Combine(dir, "manifest.json"), true);
    }

    private static void Prune(string root)
    {
        try
        {
            var dirs = Directory.GetDirectories(root).OrderByDescending(Path.GetFileName, StringComparer.Ordinal).ToList();
            foreach (var old in dirs.Skip(MaxSnapshots)) Directory.Delete(old, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Lists snapshots, newest first. A damaged snapshot folder is left out.</summary>
    public static IReadOnlyList<RegexGrepSnapshotInfo> ListSnapshots(string snapshotRoot)
    {
        var list = new List<RegexGrepSnapshotInfo>();
        if (!Directory.Exists(snapshotRoot)) return list;
        foreach (var dir in Directory.GetDirectories(snapshotRoot).OrderByDescending(Path.GetFileName, StringComparer.Ordinal))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir, "manifest.json")));
                var r = doc.RootElement;
                if (r.GetProperty("format").GetString() != SnapshotFormat) continue;
                var files = r.GetProperty("files");
                list.Add(new(r.GetProperty("id").GetString() ?? "", r.GetProperty("created").GetDateTimeOffset(), r.GetProperty("state").GetString() ?? "", files.GetArrayLength(),
                    files.EnumerateArray().Sum(f => f.GetProperty("replacements").GetInt32()), r.GetProperty("pattern").GetString() ?? "", r.GetProperty("replacement").GetString() ?? ""));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { }
        }
        return list;
    }

    /// <summary>Restores the original bytes of every file the snapshot changed. A file edited after the apply is left alone and reported.</summary>
    public static RegexGrepUndoResult Undo(string snapshotRoot, string id)
    {
        if (id.Length == 0 || id.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-'))) return new(false, "Not a snapshot id.", 0, []);
        var dir = Path.Combine(snapshotRoot, id);
        var skipped = new List<RegexGrepSkip>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir, "manifest.json")));
            var r = doc.RootElement;
            if (r.GetProperty("format").GetString() != SnapshotFormat) return new(false, "Not an undo snapshot.", 0, []);
            var state = r.GetProperty("state").GetString();
            if (state != "applied") return new(false, state == "undone" ? "This snapshot was already undone." : "This snapshot was not applied.", 0, []);
            var restored = 0;
            foreach (var f in r.GetProperty("files").EnumerateArray())
            {
                var path = f.GetProperty("path").GetString() ?? "";
                var snapshot = f.GetProperty("snapshot").GetString() ?? "";
                if (snapshot.Contains("..") || Path.IsPathRooted(snapshot)) { Skip(skipped, path, "invalid snapshot entry"); continue; }
                if (!File.Exists(path)) { Skip(skipped, path, "the file no longer exists"); continue; }
                var current = File.ReadAllBytes(path);
                if (Sha(current) != f.GetProperty("sha256After").GetString()) { Skip(skipped, path, "changed after the replacement, so it was not restored"); continue; }
                var original = File.ReadAllBytes(Path.Combine(dir, snapshot.Replace('/', Path.DirectorySeparatorChar)));
                if (Sha(original) != f.GetProperty("sha256Before").GetString()) { Skip(skipped, path, "the snapshot copy is damaged"); continue; }
                WriteReplacing(path, original);
                restored++;
            }
            if (restored > 0 || skipped.Count == 0)
            {
                var text = File.ReadAllText(Path.Combine(dir, "manifest.json"));
                File.WriteAllText(Path.Combine(dir, "manifest.json"), text.Replace("\"state\": \"applied\"", "\"state\": \"undone\""));
            }
            return new(true, null, restored, skipped);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new(false, "The snapshot could not be read: " + e.Message, 0, skipped);
        }
    }
}
