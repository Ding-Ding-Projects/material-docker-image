using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>A ready-made pattern with a worked example: <see cref="Example"/> contains <see cref="ExpectedMatch"/>.</summary>
public sealed record RegexSnippet(string Id, string Name, string Pattern, string Description, string Example, string ExpectedMatch, string NonMatch, RegexBuilderFlags? Flags = null);

/// <summary>A guided insertion: <see cref="Prefix"/> and <see cref="Suffix"/> wrap the selection in the pattern.</summary>
public sealed record RegexToken(string Family, string Label, string Prefix, string Suffix, string Description);

/// <summary>Built-in snippets, token palette and composers for the builder. Pure logic.</summary>
public static class RegexBuilderLibrary
{
    private const string Hex4 = "[0-9A-Fa-f]{1,4}";

    public static readonly IReadOnlyList<RegexSnippet> Snippets =
    [
        new("image-reference", "Image reference", @"(?:[a-z0-9.-]+(?::[0-9]{1,5})?/)?[a-z0-9]+(?:[._-][a-z0-9]+)*(?:/[a-z0-9]+(?:[._-][a-z0-9]+)*)*(?::[\w][\w.-]{0,127})?(?:@sha256:[a-f0-9]{64})?",
            "Optional registry host and port, repository path, optional tag and optional sha256 digest. Broad: combine with ^ and $ or Multiline to match whole values.", "ghcr.io/owner/app:1.2.3", "ghcr.io/owner/app:1.2.3", "!!!"),
        new("image-tag", "Image tag", @":(?<tag>[\w][\w.-]{0,127})\b", "A tag after a colon: starts with a letter, digit or _, then up to 127 letters, digits, _, . or -.", "alpine:3.20", ":3.20", "alpine"),
        new("digest", "Digest (sha256)", @"sha256:[a-f0-9]{64}", "A content digest: sha256: followed by 64 lowercase hexadecimal digits.", "image@sha256:" + new string('a', 64), "sha256:" + new string('a', 64), "sha256:abc"),
        new("semver", "Semantic version", @"\bv?(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:-(?<pre>[0-9A-Za-z.-]+))?(?:\+(?<build>[0-9A-Za-z.-]+))?\b",
            "major.minor.patch with optional v prefix, pre-release and build metadata.", "release v1.2.3-rc.1+build.5 ok", "v1.2.3-rc.1+build.5", "1.2"),
        new("ipv4", "IPv4 address", @"\b(?:(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\.){3}(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\b", "Four numbers from 0 to 255 separated by dots.", "bind 192.168.1.20:80", "192.168.1.20", "999.1.1.1"),
        new("ipv6", "IPv6 address",
            $"(?:(?:{Hex4}:){{7}}{Hex4}|(?:{Hex4}:){{1,6}}:{Hex4}|(?:{Hex4}:){{1,5}}(?::{Hex4}){{1,2}}|(?:{Hex4}:){{1,4}}(?::{Hex4}){{1,3}}|(?:{Hex4}:){{1,3}}(?::{Hex4}){{1,4}}|(?:{Hex4}:){{1,2}}(?::{Hex4}){{1,5}}|{Hex4}:(?::{Hex4}){{1,6}}|(?:{Hex4}:){{1,7}}:|:(?:(?::{Hex4}){{1,7}}|:))",
            "Full or compressed (::) IPv6 address without a zone or embedded IPv4 part.", "listen [2001:db8::1]:443", "2001:db8::1", "12345"),
        new("port", "Port number", @":(?<port>6553[0-5]|655[0-2]\d|65[0-4]\d{2}|6[0-4]\d{3}|[1-5]\d{4}|[1-9]\d{0,3})\b", "A port from 1 to 65535 after a colon.", "0.0.0.0:8080->80/tcp", ":8080", ":70000"),
        new("container-id", "Container or image ID", @"\b(?:[0-9a-f]{64}|[0-9a-f]{12})\b", "A full 64-digit or short 12-digit hexadecimal ID.", "abc123def456 nginx", "abc123def456", "abc123"),
        new("iso-date", "ISO 8601 date and time", @"\b\d{4}-(?:0[1-9]|1[0-2])-(?:0[1-9]|[12]\d|3[01])(?:[T ](?:[01]\d|2[0-3]):[0-5]\d(?::[0-5]\d(?:\.\d+)?)?(?:Z|[+-](?:[01]\d|2[0-3]):?[0-5]\d)?)?",
            "A date such as 2026-11-03 with an optional time and time zone.", "created 2026-11-03T17:24:14Z by", "2026-11-03T17:24:14Z", "2026-13-01"),
        new("email", "Email address", @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b", "A common email address shape (not a full validation).", "maintainer admin@example.com here", "admin@example.com", "admin@localhost"),
        new("url", "URL", @"\bhttps?://[^\s/$.?#][^\s""'<>]*", "An http or https address up to the next space or quote.", "see https://example.com/docs?a=1 now", "https://example.com/docs?a=1", "ftp://example.com"),
        new("windows-path", "Windows path", @"\b[A-Za-z]:\\(?:[^\\/:*?""<>|\r\n]+\\)*[^\\/:*?""<>|\r\n]*", "A drive-letter path such as C:\\Projects\\app\\Dockerfile.", @"C:\Projects\app\Dockerfile", @"C:\Projects\app\Dockerfile", "/usr/bin/env"),
        new("env-var", "Environment variable NAME=value", @"^(?<name>[A-Za-z_][A-Za-z0-9_]*)=(?<value>.*)$", "A NAME=value line; turn on Multiline to match every line of a block.", "PATH=/usr/local/bin", "PATH=/usr/local/bin", "1BAD=x", new RegexBuilderFlags { Multiline = true }),
        new("log-level", "Log level word", @"\b(?:ERROR|WARN(?:ING)?|INFO|DEBUG|TRACE|FATAL)\b", "A common log level keyword.", "2026-11-03 WARN disk low", "WARN", "warned"),
    ];

    public static readonly IReadOnlyList<RegexToken> Tokens =
    [
        new("Character classes", @"\d", @"\d", "", "A digit."),
        new("Character classes", @"\D", @"\D", "", "Not a digit."),
        new("Character classes", @"\w", @"\w", "", "A word character: letter, digit or _."),
        new("Character classes", @"\W", @"\W", "", "Not a word character."),
        new("Character classes", @"\s", @"\s", "", "White space."),
        new("Character classes", @"\S", @"\S", "", "Not white space."),
        new("Character classes", ".", ".", "", "Any character except newline (Singleline includes newline)."),
        new("Anchors", "^", "^", "", "Start of text (or line with Multiline)."),
        new("Anchors", "$", "$", "", "End of text (or line with Multiline)."),
        new("Anchors", @"\b", @"\b", "", "Word boundary."),
        new("Anchors", @"\B", @"\B", "", "Not a word boundary."),
        new("Anchors", @"\A", @"\A", "", "Start of text only."),
        new("Anchors", @"\z", @"\z", "", "End of text only."),
        new("Groups", "( )", "(", ")", "Capturing group around the selection."),
        new("Groups", "(?: )", "(?:", ")", "Non-capturing group around the selection."),
        new("Groups", "(?= )", "(?=", ")", "Positive lookahead (uses the backtracking engine)."),
        new("Groups", "(?! )", "(?!", ")", "Negative lookahead (uses the backtracking engine)."),
        new("Groups", "(?<= )", "(?<=", ")", "Positive lookbehind (uses the backtracking engine)."),
        new("Groups", "(?<! )", "(?<!", ")", "Negative lookbehind (uses the backtracking engine)."),
        new("Groups", "(?> )", "(?>", ")", "Atomic group (uses the backtracking engine)."),
        new("Alternation", "|", "|", "", "Or: either the left or the right side."),
        new("Quantifiers", "?", "?", "", "Optional: zero or one time."),
        new("Quantifiers", "*", "*", "", "Zero or more times, greedy."),
        new("Quantifiers", "+", "+", "", "One or more times, greedy."),
        new("Quantifiers", "??", "??", "", "Zero or one time, lazy."),
        new("Quantifiers", "*?", "*?", "", "Zero or more times, lazy."),
        new("Quantifiers", "+?", "+?", "", "One or more times, lazy."),
    ];

    /// <summary>Plain text as an escaped pattern (spaces and # are escaped too, so it also works with IgnorePatternWhitespace).</summary>
    public static string Literal(string text) => Regex.Escape(text ?? "");

    /// <summary>Inverse of <see cref="Literal"/>.</summary>
    public static string Unescape(string pattern) => Regex.Unescape(pattern);

    /// <summary>
    /// Character set from members. Each member is a single character, a range "a-z", or a class escape
    /// (\d \w \s \D \W \S or \p{..}). Characters that are special inside [] are escaped.
    /// </summary>
    public static string CharacterSet(IEnumerable<string> members, bool negated)
    {
        var sb = new StringBuilder(negated ? "[^" : "[");
        var any = false;
        foreach (var raw in members)
        {
            if (string.IsNullOrEmpty(raw)) continue;
            any = true;
            if (raw.Length == 2 && raw[0] == '\\' && "dwsDWS".Contains(raw[1])) { sb.Append(raw); continue; }
            if (raw.StartsWith(@"\p{", StringComparison.Ordinal) || raw.StartsWith(@"\P{", StringComparison.Ordinal))
            {
                var name = raw[3..^1];
                if (!raw.EndsWith('}') || !RegexExplainer.UnicodeCategories.ContainsKey(name)) throw new ArgumentException($"Unknown Unicode category {raw}.");
                sb.Append(raw); continue;
            }
            if (raw.Length == 3 && raw[1] == '-')
            {
                if (raw[2] < raw[0]) throw new ArgumentException($"The range {raw} is in reverse order.");
                sb.Append(SetEscape(raw[0])).Append('-').Append(SetEscape(raw[2])); continue;
            }
            foreach (var ch in raw) sb.Append(SetEscape(ch));
        }
        if (!any) throw new ArgumentException("A character set needs at least one member.");
        return sb.Append(']').ToString();
    }

    private static string SetEscape(char c) => c switch
    {
        '\\' or ']' or '[' or '^' or '-' => "\\" + c,
        '\n' => @"\n", '\r' => @"\r", '\t' => @"\t",
        _ => c.ToString(),
    };

    /// <summary>Quantifier text: ?, *, +, {n}, {n,} or {n,m}, with a trailing ? when lazy.</summary>
    public static string Quantifier(int min, int? max, bool lazy)
    {
        if (min < 0 || min > 100_000) throw new ArgumentException("The minimum must be between 0 and 100000.");
        if (max is { } m && (m < min || m > 100_000)) throw new ArgumentException("The maximum must be at least the minimum and at most 100000.");
        var q = (min, max) switch
        {
            (0, 1) => "?",
            (0, null) => "*",
            (1, null) => "+",
            (_, null) => $"{{{min},}}",
            var (a, b) when a == b => $"{{{min}}}",
            _ => $"{{{min},{max}}}",
        };
        return lazy ? q + "?" : q;
    }

    public static bool IsValidGroupName(string name) => name.Length is > 0 and <= 64 && (char.IsLetter(name[0]) || name[0] == '_') && name.All(c => char.IsLetterOrDigit(c) || c == '_');

    public static string NamedGroup(string name, string inner)
    {
        if (!IsValidGroupName(name)) throw new ArgumentException("A group name uses letters, digits and _ and does not start with a digit.");
        return $"(?<{name}>{inner})";
    }

    public static string Backreference(int number) => number is >= 1 and <= 99 ? @"\" + number : throw new ArgumentException("Group numbers start at 1.");

    public static string Backreference(string name) => IsValidGroupName(name) ? $@"\k<{name}>" : throw new ArgumentException("Not a valid group name.");

    /// <summary>Non-capturing alternation of literal options, each escaped.</summary>
    public static string Alternation(IEnumerable<string> options)
    {
        var list = options.Where(o => o.Length > 0).Select(Literal).ToList();
        if (list.Count == 0) throw new ArgumentException("Enter at least one alternative.");
        return "(?:" + string.Join("|", list) + ")";
    }

    public static string UnicodeCategory(string code, bool negated)
    {
        if (!RegexExplainer.UnicodeCategories.ContainsKey(code)) throw new ArgumentException($"Unknown Unicode category {code}.");
        return (negated ? @"\P{" : @"\p{") + code + "}";
    }
}

/// <summary>A pattern the user explicitly saved with "Save to my patterns".</summary>
public sealed record RegexSavedPattern(string Name, string Pattern, RegexBuilderFlags Flags, DateTimeOffset Saved);

/// <summary>
/// The only persisted builder data: the user's saved patterns, stored as JSON in the app data folder, bounded to
/// <see cref="MaxEntries"/> entries.
/// </summary>
public static partial class RegexPatternStore
{
    public const int MaxEntries = 200;
    public const int MaxNameLength = 80;
    public const string FileName = "regex-patterns.json";
    private const string Format = "material-docker-image.regex-patterns";

    public static string DefaultPath => Path.Combine(UserSettings.DataDirectory, FileName);

    /// <summary>Loads saved patterns. A missing file is an empty list; a corrupt file throws InvalidDataException.</summary>
    public static List<RegexSavedPattern> Load(string path)
    {
        if (!File.Exists(path)) return new();
        var info = new FileInfo(path);
        if (info.Length > 4 * 1024 * 1024) throw new InvalidDataException("The saved patterns file is larger than 4 MiB and was not read.");
        return Parse(File.ReadAllText(path));
    }

    /// <summary>Parses the library format. Throws InvalidDataException for anything that is not a valid library.</summary>
    public static List<RegexSavedPattern> Parse(string text)
    {
        if (text.Length > 4 * 1024 * 1024) throw new InvalidDataException("The saved patterns text is larger than 4 MiB and was not read.");
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var f) || f.GetString() != Format) throw new InvalidDataException("The saved patterns file has an unknown format.");
            if (!root.TryGetProperty("version", out var v) || v.GetInt32() != 1) throw new InvalidDataException("The saved patterns file has an unsupported version.");
            var list = new List<RegexSavedPattern>();
            foreach (var e in root.GetProperty("patterns").EnumerateArray())
            {
                var name = e.GetProperty("name").GetString() ?? "";
                var pattern = e.GetProperty("pattern").GetString() ?? "";
                if (name.Length is 0 or > MaxNameLength || pattern.Length is 0 or > RegexBuilderEngine.MaxPatternLength) throw new InvalidDataException("A saved pattern has an empty or too long name or pattern.");
                var flags = new RegexBuilderFlags();
                if (e.TryGetProperty("flags", out var fl))
                    foreach (var (flag, _, _) in RegexBuilderFlags.All)
                        if (fl.TryGetProperty(flag, out var b)) flags = flags.With(flag, b.GetBoolean());
                var saved = e.TryGetProperty("saved", out var s) && s.TryGetDateTimeOffset(out var d) ? d : DateTimeOffset.MinValue;
                list.Add(new(name, pattern, flags, saved));
                if (list.Count > MaxEntries) throw new InvalidDataException($"The saved patterns file has more than {MaxEntries} entries.");
            }
            return list;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("The saved patterns file is not valid: " + e.Message, e);
        }
    }

    /// <summary>Writes atomically (temporary file, then replace).</summary>
    public static void Save(string path, IReadOnlyList<RegexSavedPattern> patterns)
    {
        if (patterns.Count > MaxEntries) throw new InvalidOperationException($"At most {MaxEntries} patterns can be saved.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, SerializeBytes(patterns));
        File.Move(temp, path, true);
    }

    /// <summary>The library as JSON text, for export.</summary>
    public static string Serialize(IReadOnlyList<RegexSavedPattern> patterns) => System.Text.Encoding.UTF8.GetString(SerializeBytes(patterns));

    private static byte[] SerializeBytes(IReadOnlyList<RegexSavedPattern> patterns)
    {
        if (patterns.Count > MaxEntries) throw new InvalidOperationException($"At most {MaxEntries} patterns can be saved.");
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("format", Format);
            w.WriteNumber("version", 1);
            w.WriteStartArray("patterns");
            foreach (var p in patterns)
            {
                w.WriteStartObject();
                w.WriteString("name", p.Name);
                w.WriteString("pattern", p.Pattern);
                w.WriteStartObject("flags");
                foreach (var (flag, _, _) in RegexBuilderFlags.All) w.WriteBoolean(flag, p.Flags.Get(flag));
                w.WriteEndObject();
                w.WriteString("saved", p.Saved);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return stream.ToArray();
    }

    /// <summary>Adds or replaces (same name) a pattern, newest first. Refuses invalid patterns and a full library.</summary>
    public static List<RegexSavedPattern> Add(IReadOnlyList<RegexSavedPattern> existing, RegexSavedPattern entry)
    {
        var name = entry.Name.Trim();
        if (name.Length is 0 or > MaxNameLength) throw new ArgumentException($"Give the pattern a name of 1 to {MaxNameLength} characters.");
        var check = RegexBuilderEngine.Validate(entry.Pattern, entry.Flags);
        if (!check.Valid) throw new ArgumentException(check.Error);
        var list = existing.Where(p => !string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (list.Count >= MaxEntries) throw new InvalidOperationException($"My patterns already holds {MaxEntries} entries. Remove one first.");
        list.Insert(0, entry with { Name = name });
        return list;
    }

    public static List<RegexSavedPattern> Remove(IReadOnlyList<RegexSavedPattern> existing, string name) =>
        existing.Where(p => !string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
}
