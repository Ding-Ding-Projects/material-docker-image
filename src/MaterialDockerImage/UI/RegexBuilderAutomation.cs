using System.Text;

namespace MaterialDockerImage.UI;

/// <summary>The arguments of a headless run: where the action comes from (a path, or "-" for standard input), where the results go, and whether to write the replacement.</summary>
public sealed record RegexAutomationRequest(string ActionSource, string OutputPath, bool Apply)
{
    public bool FromStandardInput => ActionSource == "-";
}

/// <summary>
/// Headless automation of the folder search, for scripts:
/// <c>--regex-run ACTIONFILE --regex-output RESULTS.json [--regex-apply]</c>. No window is ever created. The action file (or standard input when the name is <c>-</c>) is
/// validated in full, the folder search runs, and the result list is written in the format of <b>Save results…</b> so <b>Load results…</b> reads it back. The exit code is 0
/// when something matched, 1 when nothing matched, and 2 for an invalid action, a bad argument or any error. Without <c>--regex-apply</c> nothing is written except the output file.
/// With it, and only when the action has a replacement, the replacement goes through the same dry-run plan, SHA-256 check and byte-exact undo snapshot as the window, and the
/// snapshot id is printed. Framework-free, so the checks can drive it with temporary folders.
/// </summary>
public static class RegexBuilderAutomation
{
    public const int ExitMatches = 0, ExitNoMatches = 1, ExitError = 2;
    public const string Usage = "--regex-run ACTIONFILE|- --regex-output RESULTS.json [--regex-apply]";

    private static readonly string[] known = ["--regex-run", "--regex-output", "--regex-apply"];

    /// <summary>True when the arguments ask for a headless run at all.</summary>
    public static bool Requested(IReadOnlyList<string> args) => args.Any(a => string.Equals(a, "--regex-run", StringComparison.Ordinal));

    /// <summary>Reads the arguments of a headless run. Returns a problem for anything missing, repeated or stray.</summary>
    public static (RegexAutomationRequest? Request, string? Problem) Parse(IReadOnlyList<string> args)
    {
        string? source = null, output = null;
        var apply = false;
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (string.Equals(a, "--regex-apply", StringComparison.Ordinal))
            {
                if (apply) return (null, "--regex-apply was given twice. Usage: " + Usage);
                apply = true;
            }
            else if (string.Equals(a, "--regex-run", StringComparison.Ordinal) || string.Equals(a, "--regex-output", StringComparison.Ordinal))
            {
                var isRun = a == "--regex-run";
                if ((isRun ? source : output) != null) return (null, a + " was given twice. Usage: " + Usage);
                if (i + 1 >= args.Count || (args[i + 1].StartsWith("--", StringComparison.Ordinal) && args[i + 1] != "-")) return (null, a + " needs a value. Usage: " + Usage);
                var value = args[++i];
                if (value.Length == 0) return (null, a + " needs a value. Usage: " + Usage);
                if (isRun) source = value; else output = value;
            }
            else if (a.StartsWith("--regex-", StringComparison.Ordinal) && !known.Contains(a, StringComparer.Ordinal))
            {
                // The builder's own start-up options cannot be mixed with a headless run, because no builder opens.
                return (null, a + " cannot be combined with --regex-run. Usage: " + Usage);
            }
        }
        if (source == null) return (null, "--regex-run is required. Usage: " + Usage);
        if (output == null) return (null, "--regex-output is required. Usage: " + Usage);
        string fullOutput;
        try { fullOutput = Path.GetFullPath(output); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return (null, "The output path is not valid."); }
        if (Directory.Exists(fullOutput)) return (null, "The output path is a folder. Name a file.");
        return (new RegexAutomationRequest(source, fullOutput, apply), null);
    }

    /// <summary>Reads at most the action-file limit plus one character from standard input, so an endless stream cannot be buffered.</summary>
    private static string ReadBounded(TextReader reader)
    {
        var sb = new StringBuilder();
        var buffer = new char[8192];
        while (sb.Length <= RegexSearchAction.MaxFileBytes)
        {
            var n = reader.Read(buffer, 0, buffer.Length);
            if (n <= 0) break;
            sb.Append(buffer, 0, n);
        }
        return sb.ToString();
    }

    private static bool IsInside(string folder, string file)
    {
        var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(file).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Writes a file atomically: a temporary neighbour is written whole and then moved over the target.</summary>
    public static void WriteAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        try
        {
            File.WriteAllText(temp, content, new UTF8Encoding(false));
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) { try { File.Delete(temp); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } } }
    }

    /// <summary>
    /// Runs the request and returns the exit code. <paramref name="undoRoot"/> is where a replacement keeps its undo snapshot (the window's own folder in the application).
    /// Messages go to <paramref name="stdout"/> and problems to <paramref name="stderr"/>; nothing here shows a window or asks a question.
    /// </summary>
    public static int Run(RegexAutomationRequest request, TextReader stdin, TextWriter stdout, TextWriter stderr, string undoRoot, CancellationToken cancellation = default)
    {
        RegexSearchAction action;
        try
        {
            action = request.FromStandardInput ? RegexSearchAction.Parse(ReadBounded(stdin)) : RegexSearchAction.Load(request.ActionSource);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or DecoderFallbackException)
        {
            stderr.WriteLine("Invalid action: " + e.Message);
            return ExitError;
        }

        var options = action.ToGrepOptions();
        var willApply = request.Apply && action.Replacement.Length > 0;
        if (willApply && options.Folder.Length > 0)
        {
            try
            {
                if (IsInside(options.Folder, request.OutputPath)) { stderr.WriteLine("Refusing to apply: the output file is inside the folder that would be changed."); return ExitError; }
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { stderr.WriteLine("The folder is not valid."); return ExitError; }
        }

        var result = RegexGrep.Search(action.Pattern, action.Flags, options, cancellation);
        if (!result.Valid) { stderr.WriteLine("Search failed: " + (result.Error ?? "invalid search.")); return ExitError; }
        try { WriteAtomic(request.OutputPath, RegexGrepResults.ToJson(action.Pattern, action.Flags, options, result, DateTimeOffset.UtcNow)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            stderr.WriteLine("The results could not be written: " + e.Message);
            return ExitError;
        }
        stdout.WriteLine(result.Summary());
        stdout.WriteLine("Results: " + request.OutputPath);

        if (request.Apply && !willApply) stdout.WriteLine("The action has no replacement, so nothing was changed.");
        if (willApply)
        {
            var plan = RegexGrep.PlanReplace(action.Pattern, action.Flags, action.Replacement, options, cancellation);
            if (!plan.Valid) { stderr.WriteLine("Replacement not applied: " + (plan.Error ?? "the preview failed.")); return ExitError; }
            if (plan.Truncated || plan.TimedOut || plan.Cancelled) { stderr.WriteLine("Replacement not applied: the preview did not cover every file (a limit was reached)."); return ExitError; }
            if (plan.Files.Count == 0) stdout.WriteLine("Nothing to replace in the searched files.");
            else
            {
                var applied = RegexGrep.Apply(plan, undoRoot, cancellation);
                if (!applied.Success)
                {
                    stderr.WriteLine("Replacement failed: " + (applied.Error ?? "the replacement failed.") + (applied.RolledBack ? " The files already written were restored." : ""));
                    return ExitError;
                }
                stdout.WriteLine($"{applied.Replacements} replacement{(applied.Replacements == 1 ? "" : "s")} written in {applied.FilesChanged} file{(applied.FilesChanged == 1 ? "" : "s")}.");
                stdout.WriteLine("Snapshot: " + applied.SnapshotId);
                if (applied.Skipped.Count > 0) stdout.WriteLine("Skipped: " + applied.Skipped.Count);
            }
        }
        return result.Matches.Count > 0 ? ExitMatches : ExitNoMatches;
    }
}
