using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MaterialDockerImage.UI;

public enum RegexCodeKind { IsMatch, Matches, Replace, Split }

/// <summary>The result of converting a pattern for another language or tool. <see cref="Text"/> is empty when it cannot be converted.</summary>
public sealed record RegexConversion(string Target, string Text, bool Converted, bool Lossy, IReadOnlyList<string> Notes);

/// <summary>
/// Copy-as conversions: C#, PowerShell, a JSON string, and grep -E. Quoting is exact; semantic differences between
/// .NET and the target are listed in <see cref="RegexConversion.Notes"/> and anything that cannot be expressed is
/// reported as not convertible instead of being guessed.
/// </summary>
public static class RegexConverter
{
    public static readonly IReadOnlyList<string> Targets = ["C# verbatim string", "C# regular string", "C# code", "PowerShell", "JSON string", "grep -E"];

    public static RegexConversion Convert(string target, string pattern, RegexBuilderFlags flags, RegexCodeKind kind = RegexCodeKind.Matches, string replacement = "") => target switch
    {
        "C# verbatim string" => new(target, RegexBuilderEngine.ToCSharpVerbatim(pattern), true, false, []),
        "C# regular string" => new(target, CSharpString(pattern), true, false, []),
        "C# code" => CSharpCode(pattern, flags, kind, replacement),
        "PowerShell" => PowerShell(pattern, flags, kind, replacement),
        "JSON string" => JsonString(pattern),
        "grep -E" => Grep(pattern, flags),
        _ => throw new ArgumentException("Unknown target " + target, nameof(target)),
    };

    /// <summary>A regular C# string literal with \\, \", and control characters escaped.</summary>
    public static string CSharpString(string text)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in text)
            sb.Append(c switch
            {
                '\\' => @"\\", '"' => "\\\"", '\n' => @"\n", '\r' => @"\r", '\t' => @"\t", '\0' => @"\0", '\f' => @"\f", '\v' => @"\v", '\a' => @"\a", '\b' => @"\b",
                < ' ' or '\u007f' => "\\u" + ((int)c).ToString("X4"),
                _ => c.ToString(),
            });
        return sb.Append('"').ToString();
    }

    /// <summary>The RegexOptions expression for the flags, for example RegexOptions.IgnoreCase | RegexOptions.Multiline.</summary>
    public static string CSharpOptions(RegexBuilderFlags flags)
    {
        var parts = RegexBuilderFlags.All.Where(f => flags.Get(f.Name)).Select(f => "RegexOptions." + f.Name).ToList();
        return parts.Count == 0 ? "RegexOptions.None" : string.Join(" | ", parts);
    }

    private static RegexConversion CSharpCode(string pattern, RegexBuilderFlags flags, RegexCodeKind kind, string replacement)
    {
        var notes = new List<string> { "Add using System.Text.RegularExpressions;. The timeout protects against catastrophic backtracking." };
        if (flags.NonBacktracking) notes.Add("RegexOptions.NonBacktracking needs .NET 7 or later.");
        var sb = new StringBuilder();
        sb.AppendLine($"var regex = new Regex({RegexBuilderEngine.ToCSharpVerbatim(pattern)}, {CSharpOptions(flags)}, TimeSpan.FromMilliseconds(250));");
        switch (kind)
        {
            case RegexCodeKind.IsMatch: sb.Append("bool found = regex.IsMatch(input);"); break;
            case RegexCodeKind.Matches:
                sb.AppendLine("foreach (Match match in regex.Matches(input))").AppendLine("{").AppendLine("    Console.WriteLine($\"{match.Index}: {match.Value}\");")
                  .AppendLine("    foreach (Group group in match.Groups)").AppendLine("        Console.WriteLine($\"  {group.Name}: {group.Value}\");").Append('}');
                break;
            case RegexCodeKind.Replace: sb.Append($"string output = regex.Replace(input, {CSharpString(replacement)});"); break;
            case RegexCodeKind.Split: sb.Append("string[] pieces = regex.Split(input);"); break;
        }
        return new("C# code", sb.ToString(), true, false, notes);
    }

    private static RegexConversion PowerShell(string pattern, RegexBuilderFlags flags, RegexCodeKind kind, string replacement)
    {
        var notes = new List<string>();
        var quoted = "'" + pattern.Replace("'", "''") + "'";
        if (pattern.Contains('\n') || pattern.Contains('\r')) notes.Add("The pattern contains a line break. A single-quoted PowerShell string may span lines, but check it survives your editor.");
        var options = RegexBuilderFlags.All.Where(f => flags.Get(f.Name)).Select(f => f.Name).ToList();
        var optionText = options.Count == 0 ? "None" : string.Join(", ", options);
        var rx = $"[regex]::new({quoted}, '{optionText}')";
        var text = kind switch
        {
            RegexCodeKind.IsMatch => $"{rx}.IsMatch($text)",
            RegexCodeKind.Replace => $"{rx}.Replace($text, '{replacement.Replace("'", "''")}')",
            RegexCodeKind.Split => $"{rx}.Split($text)",
            _ => $"{rx}.Matches($text) | ForEach-Object {{ $_.Value }}",
        };
        notes.Add("PowerShell converts the option names in the string to [System.Text.RegularExpressions.RegexOptions]. The -match operator is case-insensitive unless you use -cmatch, so the [regex] form is used to keep the flags exact.");
        if (kind == RegexCodeKind.Replace) notes.Add("In the replacement, $1 and ${name} are expanded by .NET because the replacement is in single quotes.");
        return new("PowerShell", text, true, false, notes);
    }

    private static RegexConversion JsonString(string pattern)
    {
        var json = JsonSerializer.Serialize(pattern, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        return new("JSON string", json, true, false, ["This only escapes the text for a JSON string. The pattern keeps .NET syntax: other engines may not support every construct (for example \\A, \\z, atomic groups or conditionals)."]);
    }

    // ----- grep -E -----

    /// <summary>
    /// Converts to a POSIX extended expression for <c>grep -E</c>. Constructs grep cannot express, such as lazy
    /// quantifiers, lookarounds, named groups' names, inline options and Unicode escapes, make the conversion fail
    /// instead of producing a pattern that means something else.
    /// </summary>
    public static RegexConversion Grep(string pattern, RegexBuilderFlags flags)
    {
        const string target = "grep -E";
        var notes = new List<string>();
        var lossy = false;

        if (flags.Singleline) return Refuse(target, "Singleline has no meaning in grep, which matches one line at a time.");
        if (flags.RightToLeft) return Refuse(target, "grep has no right-to-left matching.");
        if (flags.IgnorePatternWhitespace) return Refuse(target, "grep does not ignore white space or support # comments in a pattern.");
        if (flags.ExplicitCapture) notes.Add("ExplicitCapture is dropped: grep -E groups always capture, which only matters for backreferences.");
        if (flags.Multiline) notes.Add("Multiline is implied: grep matches ^ and $ at the start and end of every line.");
        if (flags.CultureInvariant) notes.Add("CultureInvariant has no equivalent; grep uses the locale.");
        if (flags.NonBacktracking) notes.Add("NonBacktracking is an engine choice and is dropped.");

        var sb = new StringBuilder();
        var i = 0;
        var p = pattern;
        while (i < p.Length)
        {
            var c = p[i];
            if (c == '\\')
            {
                if (i + 1 >= p.Length) return Refuse(target, "The pattern ends with a lone backslash.");
                var e = p[i + 1];
                i += 2;
                switch (e)
                {
                    case 'd': sb.Append("[0-9]"); Lossy("\\d matches only ASCII digits in grep, but Unicode digits in .NET."); break;
                    case 'D': sb.Append("[^0-9]"); Lossy("\\D is converted to [^0-9] (ASCII digits only)."); break;
                    case 'w': sb.Append("[[:alnum:]_]"); Lossy("\\w becomes [[:alnum:]_], which depends on the locale."); break;
                    case 'W': sb.Append("[^[:alnum:]_]"); Lossy("\\W becomes [^[:alnum:]_], which depends on the locale."); break;
                    case 's': sb.Append("[[:space:]]"); break;
                    case 'S': sb.Append("[^[:space:]]"); break;
                    case 'b': sb.Append("\\b"); notes.Add("\\b is a GNU grep extension (not POSIX)."); break;
                    case 'B': sb.Append("\\B"); notes.Add("\\B is a GNU grep extension (not POSIX)."); break;
                    case 'A': sb.Append('^'); Lossy("\\A becomes ^, which matches at the start of every line in grep."); break;
                    case 'z' or 'Z': sb.Append('$'); Lossy("\\z becomes $, which matches at the end of every line in grep."); break;
                    case >= '1' and <= '9': sb.Append('\\').Append(e); notes.Add("Backreferences in ERE are a GNU grep extension."); break;
                    case 't' or 'n' or 'r' or 'f' or 'v' or 'e' or 'a' or '0' or 'x' or 'u' or 'c' or 'p' or 'P' or 'k' or 'G':
                        return Refuse(target, $"\\{e} has no equivalent in grep -E (grep matches one line at a time and has no escape sequences for characters).");
                    default:
                        if (char.IsLetterOrDigit(e)) return Refuse(target, $"\\{e} has no equivalent in grep -E.");
                        sb.Append('\\').Append(e); break; // escaped punctuation
                }
                continue;
            }
            if (c == '[')
            {
                var close = SetEnd(p, i);
                if (close < 0) return Refuse(target, "Unterminated character set.");
                var set = ConvertSet(p, i, close, out var problem, notes);
                if (set == null) return Refuse(target, problem!);
                sb.Append(set); i = close + 1; continue;
            }
            if (c == '(')
            {
                if (i + 1 < p.Length && p[i + 1] == '?')
                {
                    if (p.AsSpan(i).StartsWith("(?:")) { sb.Append('('); i += 3; notes.Add("(?: ) becomes ( ): grep -E has no non-capturing group, so group numbers for backreferences can shift."); lossy = true; continue; }
                    if ((p.AsSpan(i).StartsWith("(?<") && i + 3 < p.Length && p[i + 3] is not ('=' or '!')) || p.AsSpan(i).StartsWith("(?'"))
                    {
                        var closeName = p.IndexOfAny(['>', '\''], i + 3);
                        if (closeName < 0) return Refuse(target, "Unterminated group name.");
                        sb.Append('('); i = closeName + 1; notes.Add("Group names are dropped; refer to the group by number."); lossy = true; continue;
                    }
                    return Refuse(target, "Lookarounds, atomic groups, conditionals and inline options have no equivalent in grep -E. GNU grep -P (PCRE) supports some of them, but with different semantics.");
                }
                sb.Append('('); i++; continue;
            }
            if (c is '*' or '+' or '?' or '}')
            {
                sb.Append(c); i++;
                if (i < p.Length && p[i] == '?' ) return Refuse(target, "Lazy quantifiers have no equivalent in grep -E (it always takes the longest match).");
                if (i < p.Length && p[i] == '+' && c != '+') return Refuse(target, "Possessive quantifiers have no equivalent in grep -E.");
                continue;
            }
            if (c == '{')
            {
                var close = p.IndexOf('}', i);
                var body = close > 0 ? p[(i + 1)..close] : "";
                if (close < 0 || body.Length == 0 || !body.All(ch => char.IsAsciiDigit(ch) || ch == ',')) { sb.Append("\\{"); i++; continue; }
                sb.Append(p, i, close - i + 1); i = close + 1;
                if (i < p.Length && p[i] == '?') return Refuse(target, "Lazy quantifiers have no equivalent in grep -E (it always takes the longest match).");
                continue;
            }
            if (c is '\n' or '\r') return Refuse(target, "The pattern contains a line break; grep patterns match within one line.");
            sb.Append(c); i++;
        }
        if (flags.IgnoreCase) notes.Insert(0, "IgnoreCase is expressed with the -i option.");
        var quoted = "'" + sb.ToString().Replace("'", "'\\''") + "'";
        return new(target, $"grep -E{(flags.IgnoreCase ? " -i" : "")} {quoted}", true, lossy, notes);

        void Lossy(string note) { notes.Add(note); lossy = true; }
    }

    private static RegexConversion Refuse(string target, string reason) => new(target, "", false, false, ["Not convertible: " + reason]);

    private static int SetEnd(string p, int open)
    {
        var i = open + 1;
        if (i < p.Length && p[i] == '^') i++;
        if (i < p.Length && p[i] == ']') i++; // a leading ] is a member
        for (; i < p.Length; i++)
        {
            if (p[i] == '\\') { i++; continue; }
            if (p[i] == '[' && i + 1 < p.Length && p[i + 1] is ':' or '.' or '=') { var end = p.IndexOf(p[i + 1] + "]", i + 2, StringComparison.Ordinal); if (end > 0) { i = end + 1; continue; } }
            if (p[i] == ']') return i;
        }
        return -1;
    }

    private static string? ConvertSet(string p, int open, int close, out string? problem, List<string> notes)
    {
        problem = null;
        var sb = new StringBuilder("[");
        var i = open + 1;
        if (p[i] == '^') { sb.Append('^'); i++; }
        var members = new StringBuilder();
        var hasBracket = false;
        for (; i < close; i++)
        {
            var c = p[i];
            if (c == '\\' && i + 1 < close)
            {
                var e = p[++i];
                switch (e)
                {
                    case 'd': members.Append("0-9"); notes.Add("\\d inside a set becomes 0-9 (ASCII only)."); break;
                    case 'w': members.Append("[:alnum:]_"); notes.Add("\\w inside a set becomes [:alnum:]_."); break;
                    case 's': members.Append("[:space:]"); break;
                    case '\\': members.Append('\\'); break;
                    case ']': hasBracket = true; break;
                    case '-': members.Append('-'); break;
                    case '^': members.Append('^'); break;
                    case '[': members.Append('['); break;
                    default:
                        problem = $"\\{e} inside a character set has no equivalent in grep -E, where a backslash in a set is literal.";
                        return null;
                }
                continue;
            }
            if (c == '[' && i + 1 < close && p[i + 1] == ':') { var end = p.IndexOf(":]", i + 2, StringComparison.Ordinal); if (end > 0 && end < close + 1) { members.Append(p, i, end + 2 - i); i = end + 1; continue; } }
            if (c == '-' && i + 1 < close && p[i + 1] == '[') { problem = "Character class subtraction is .NET-specific and has no equivalent in grep -E."; return null; }
            members.Append(c);
        }
        if (hasBracket) sb.Append(']');
        // A literal - must be last and ^ must not be first for the POSIX parser; leave other order as written.
        sb.Append(members).Append(']');
        return sb.ToString();
    }
}
