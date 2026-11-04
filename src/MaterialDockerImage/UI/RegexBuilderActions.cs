using System.Text;
using System.Text.Json;

namespace MaterialDockerImage.UI;

/// <summary>
/// A search described in a file: the pattern and flags, the replacement, and the folder search it belongs to. An action file can be exported from the builder,
/// handed to another computer, and opened again with <c>--regex-action</c>. It describes a search only: it never contains a command to run, and opening it
/// shows the builder filled in without running the folder search or any replacement.
/// </summary>
public sealed record RegexSearchAction
{
    public const string Format = "material-docker-image/regex-action";
    public const int MaxDescriptionLength = 300;
    public const int MaxFieldLength = 2048;
    public const long MaxFileBytes = 256 * 1024;

    public string Description { get; init; } = "";
    public string Pattern { get; init; } = "";
    public RegexBuilderFlags Flags { get; init; } = new();
    public string Replacement { get; init; } = "";
    public string Folder { get; init; } = "";
    public string Globs { get; init; } = "*";
    public bool Recursive { get; init; } = true;
    public int ContextLines { get; init; }

    /// <summary>The folder-search options this action describes, with the engine limits left at their defaults.</summary>
    public RegexGrepOptions ToGrepOptions() => new() { Folder = Folder, Globs = Globs, Recursive = Recursive, ContextLines = ContextLines };

    /// <summary>Why the action cannot be used, or null. The pattern must compile with its flags and every text field must be within its bound.</summary>
    public string? Problem()
    {
        if (Pattern.Length == 0) return "The action has no pattern.";
        if (Pattern.Length > RegexBuilderEngine.MaxPatternLength) return $"The pattern is longer than {RegexBuilderEngine.MaxPatternLength} characters.";
        var check = RegexBuilderEngine.Validate(Pattern, Flags);
        if (!check.Valid) return "The pattern is not valid: " + check.Error;
        if (Description.Length > MaxDescriptionLength) return $"The description is longer than {MaxDescriptionLength} characters.";
        if (Replacement.Length > RegexBuilderEngine.MaxPatternLength) return "The replacement is too long.";
        if (Folder.Length > MaxFieldLength || Globs.Length > MaxFieldLength) return "The folder or file patterns are too long.";
        if (ContextLines is < 0 or > RegexGrep.MaxContextLines) return $"Context lines must be between 0 and {RegexGrep.MaxContextLines}.";
        return null;
    }

    public string Serialize()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("format", Format);
            w.WriteNumber("version", 1);
            if (Description.Length > 0) w.WriteString("description", Description);
            w.WriteString("pattern", Pattern);
            w.WriteStartObject("flags");
            foreach (var (name, _, _) in RegexBuilderFlags.All) w.WriteBoolean(name, Flags.Get(name));
            w.WriteEndObject();
            if (Replacement.Length > 0) w.WriteString("replacement", Replacement);
            if (Folder.Length > 0) w.WriteString("folder", Folder);
            w.WriteString("globs", Globs);
            w.WriteBoolean("recursive", Recursive);
            if (ContextLines > 0) w.WriteNumber("contextLines", ContextLines);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Reads and validates an action file. Throws InvalidDataException with the reason; a rejected file is never applied in part.</summary>
    public static RegexSearchAction Parse(string text)
    {
        if (text.Length > MaxFileBytes) throw new InvalidDataException("The action file is larger than 256 KiB and was not read.");
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var f) || f.GetString() != Format) throw new InvalidDataException("This is not a regular expression action file.");
            if (!root.TryGetProperty("version", out var v) || v.GetInt32() != 1) throw new InvalidDataException("The action file has an unsupported version.");
            string Text(string name, string fallback = "") => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? fallback : fallback;
            var flags = new RegexBuilderFlags { IgnoreCase = false };
            if (root.TryGetProperty("flags", out var fl))
                foreach (var (name, _, _) in RegexBuilderFlags.All)
                    if (fl.TryGetProperty(name, out var b)) flags = flags.With(name, b.GetBoolean());
            var action = new RegexSearchAction
            {
                Description = Text("description"), Pattern = Text("pattern"), Flags = flags, Replacement = Text("replacement"), Folder = Text("folder"), Globs = Text("globs", "*"),
                Recursive = !root.TryGetProperty("recursive", out var r) || r.ValueKind != JsonValueKind.False,
                ContextLines = root.TryGetProperty("contextLines", out var c) ? c.GetInt32() : 0,
            };
            var problem = action.Problem();
            if (problem != null) throw new InvalidDataException(problem);
            return action;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("The action file is not valid: " + e.Message, e);
        }
    }

    public static RegexSearchAction Load(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new InvalidDataException("The action file does not exist.");
        if (info.Length > MaxFileBytes) throw new InvalidDataException("The action file is larger than 256 KiB and was not read.");
        return Parse(File.ReadAllText(path, new UTF8Encoding(false, true)));
    }

    public void Save(string path)
    {
        var problem = Problem();
        if (problem != null) throw new InvalidOperationException(problem);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, Serialize(), new UTF8Encoding(false));
        File.Move(temp, path, true);
    }
}

/// <summary>
/// The command line that opens the builder filled in:
/// <c>--regex-pattern TEXT [--regex-flags LETTERS] [--regex-replacement TEXT] [--regex-folder PATH] [--regex-globs LIST]</c>, or <c>--regex-action FILE</c>.
/// Flag letters: i IgnoreCase, m Multiline, s Singleline, x IgnorePatternWhitespace, n ExplicitCapture, c CultureInvariant, r RightToLeft, b NonBacktracking.
/// Without <c>--regex-flags</c> no flag is set; the letters name exactly the flags that are on.
/// </summary>
public static class RegexCommandLine
{
    public static readonly IReadOnlyList<(char Letter, string Name)> FlagLetters =
        [('i', "IgnoreCase"), ('m', "Multiline"), ('s', "Singleline"), ('x', "IgnorePatternWhitespace"), ('n', "ExplicitCapture"), ('c', "CultureInvariant"), ('r', "RightToLeft"), ('b', "NonBacktracking")];

    public const string Usage = "--regex-pattern TEXT [--regex-flags imsxncrb] [--regex-replacement TEXT] [--regex-folder PATH] [--regex-globs LIST]   or   --regex-action FILE";

    private static readonly string[] known = ["--regex-pattern", "--regex-flags", "--regex-replacement", "--regex-folder", "--regex-globs", "--regex-action"];

    /// <summary>True when the arguments ask for the builder at all.</summary>
    public static bool Requested(IReadOnlyList<string> args) => args.Any(a => known.Contains(a, StringComparer.Ordinal));

    public static (RegexBuilderFlags? Flags, string? Problem) ParseFlags(string letters)
    {
        var flags = new RegexBuilderFlags { IgnoreCase = false };
        foreach (var ch in letters)
        {
            var name = FlagLetters.FirstOrDefault(f => f.Letter == char.ToLowerInvariant(ch)).Name;
            if (name == null) return (null, $"'{ch}' is not a flag letter. Use letters from {string.Concat(FlagLetters.Select(f => f.Letter))}.");
            flags = flags.With(name, true);
        }
        if (flags.NonBacktracking && flags.RightToLeft) return (null, "NonBacktracking and RightToLeft cannot be combined.");
        return (flags, null);
    }

    public static string FlagsToLetters(RegexBuilderFlags flags) => string.Concat(FlagLetters.Where(f => flags.Get(f.Name)).Select(f => f.Letter));

    /// <summary>Reads the arguments. Returns (null, null) when the builder was not requested, an action when it was, or a problem to show.</summary>
    public static (RegexSearchAction? Action, string? Problem) Parse(IReadOnlyList<string> args)
    {
        if (!Requested(args)) return (null, null);
        string? Value(string name, out bool present)
        {
            present = false;
            for (var i = 0; i < args.Count; i++)
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                {
                    present = true;
                    return i + 1 < args.Count && !args[i + 1].StartsWith("--regex-", StringComparison.Ordinal) ? args[i + 1] : null;
                }
            return null;
        }
        foreach (var name in known)
        {
            var value = Value(name, out var present);
            if (present && value == null) return (null, name + " needs a value. Usage: " + Usage);
        }
        var file = Value("--regex-action", out var hasFile);
        var pattern = Value("--regex-pattern", out var hasPattern);
        if (hasFile && hasPattern) return (null, "Use either --regex-action or --regex-pattern, not both.");
        if (hasFile)
        {
            try { return (RegexSearchAction.Load(file!), null); }
            catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or DecoderFallbackException) { return (null, e.Message); }
        }
        if (!hasPattern) return (null, "--regex-pattern is required when other --regex options are given. Usage: " + Usage);
        var letters = Value("--regex-flags", out _) ?? "";
        var (flags, flagProblem) = ParseFlags(letters);
        if (flags == null) return (null, flagProblem);
        var action = new RegexSearchAction
        {
            Pattern = pattern!, Flags = flags, Replacement = Value("--regex-replacement", out _) ?? "", Folder = Value("--regex-folder", out _) ?? "", Globs = Value("--regex-globs", out _) ?? "*",
        };
        var problem = action.Problem();
        return problem == null ? (action, null) : (null, problem);
    }

    /// <summary>
    /// Quotes one argument for a Windows command line as CommandLineToArgvW reads it: backslashes are literal unless they come right before a quote
    /// (or the closing quote), where each one is doubled, and a quote is written as a backslash and a quote.
    /// </summary>
    public static string Quote(string value)
    {
        var sb = new StringBuilder("\"");
        var slashes = 0;
        foreach (var ch in value)
        {
            if (ch == '\\') { slashes++; continue; }
            if (ch == '"') { sb.Append('\\', slashes * 2 + 1).Append('"'); }
            else { sb.Append('\\', slashes).Append(ch); }
            slashes = 0;
        }
        return sb.Append('\\', slashes * 2).Append('"').ToString();
    }

    /// <summary>The arguments that reopen an action in the builder, quoted for a Windows command line.</summary>
    public static string ToArguments(RegexSearchAction action)
    {
        var sb = new StringBuilder("--regex-pattern " + Quote(action.Pattern));
        var letters = FlagsToLetters(action.Flags);
        if (letters.Length > 0) sb.Append(" --regex-flags ").Append(letters);
        if (action.Replacement.Length > 0) sb.Append(" --regex-replacement ").Append(Quote(action.Replacement));
        if (action.Folder.Length > 0) sb.Append(" --regex-folder ").Append(Quote(action.Folder));
        if (action.Globs != "*") sb.Append(" --regex-globs ").Append(Quote(action.Globs));
        return sb.ToString();
    }
}
