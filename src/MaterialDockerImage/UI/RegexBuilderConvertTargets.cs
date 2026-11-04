using System.Text;

namespace MaterialDockerImage.UI;

/// <summary>
/// Further conversion targets: command lines for grep -P, ripgrep and Select-String, and source code for JavaScript, Python, Java, Go and PHP.
/// Quoting is exact. Whether the construct means the same thing in the target comes from the hand-written flavor table: constructs the target
/// does not support make the conversion fail, constructs it reads differently are listed and mark the result as lossy.
/// </summary>
public static partial class RegexConverter
{
    private static (List<string> Refusals, List<string> Differences) Compatibility(string pattern, RegexBuilderFlags flags, string flavorId)
    {
        var refusals = new List<string>();
        var differences = new List<string>();
        var verdict = RegexFlavors.Compare(pattern, flags, [flavorId]).First();
        var name = verdict.Flavor.Name;
        foreach (var (construct, support, note) in verdict.Findings)
        {
            if (support == FlavorSupport.No) refusals.Add($"{construct.Label} is not supported by {name}" + (note.Length > 0 ? " (" + note + ")" : "") + ".");
            else if (support == FlavorSupport.Different && note.Length > 0) differences.Add($"{construct.Label}: {note}");
        }
        return (refusals, differences);
    }

    private static RegexConversion Result(string target, string text, List<string> differences, List<string> notes)
    {
        var all = new List<string>(notes);
        all.AddRange(differences);
        return new(target, text, true, differences.Count > 0, all);
    }

    // ----- shell quoting for the command lines -----

    private static string ShellQuote(string text) => "'" + text.Replace("'", "'\\''") + "'";

    private static List<string> GlobArguments(string? globs, string includeFlag, string excludeFlag, Func<string, string> quote, List<string> notes)
    {
        var args = new List<string>();
        foreach (var raw in (globs ?? "").Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var exclude = raw.StartsWith('!');
            var glob = (exclude ? raw[1..] : raw).Trim();
            if (glob.Length == 0 || glob == "*") continue;
            args.Add((exclude ? excludeFlag : includeFlag) + quote(glob));
        }
        return args;
    }

    private static string FolderText(string? folder, Func<string, string> quote) => string.IsNullOrWhiteSpace(folder) ? "." : quote(folder.Trim());

    // ----- grep -P -----

    private static RegexConversion GrepPcre(string pattern, RegexBuilderFlags flags, string? folder, string? globs)
    {
        const string target = "grep -P";
        if (flags.Singleline) return Refuse(target, "Singleline has no meaning in grep, which matches one line at a time.");
        if (flags.RightToLeft) return Refuse(target, "grep has no right-to-left matching.");
        var (refusals, differences) = Compatibility(pattern, flags, "pcre2");
        if (refusals.Count > 0) return new(target, "", false, false, ["Not convertible: " + string.Join(" ", refusals)]);
        var notes = new List<string> { "grep -P is a GNU grep option and needs a grep built with PCRE2. Quoting is for a POSIX shell." };
        var prefix = (flags.IgnorePatternWhitespace ? "x" : "") + (flags.ExplicitCapture ? "n" : "");
        if (flags.Multiline) notes.Add("Multiline is implied: grep matches ^ and $ at the start and end of every line.");
        if (flags.CultureInvariant) notes.Add("CultureInvariant has no equivalent; grep uses the locale.");
        if (flags.NonBacktracking) notes.Add("NonBacktracking is an engine choice and is dropped.");
        var text = (prefix.Length > 0 ? "(?" + prefix + ")" : "") + pattern;
        var files = GlobArguments(globs, "--include=", "--exclude=", ShellQuote, notes);
        var recursive = !string.IsNullOrWhiteSpace(folder);
        var sb = new StringBuilder("grep -P");
        if (flags.IgnoreCase) sb.Append(" -i");
        if (recursive) sb.Append(" -rn");
        foreach (var f in files) sb.Append(' ').Append(f);
        sb.Append(" -e ").Append(ShellQuote(text)).Append(' ').Append(FolderText(folder, ShellQuote));
        return Result(target, sb.ToString(), differences, notes);
    }

    // ----- ripgrep -----

    private static RegexConversion Ripgrep(string pattern, RegexBuilderFlags flags, string? folder, string? globs)
    {
        const string target = "ripgrep";
        if (flags.RightToLeft) return Refuse(target, "ripgrep has no right-to-left matching.");
        var (refusals, differences) = Compatibility(pattern, flags, "rust");
        var usePcre = false;
        var notes = new List<string>();
        if (refusals.Count > 0)
        {
            // The default engine cannot read it. ripgrep -P switches to PCRE2, which may.
            var (pcreRefusals, pcreDifferences) = Compatibility(pattern, flags, "pcre2");
            if (pcreRefusals.Count > 0) return new(target, "", false, false, ["Not convertible: " + string.Join(" ", refusals) + " With -P (PCRE2): " + string.Join(" ", pcreRefusals)]);
            usePcre = true; differences = pcreDifferences;
            notes.Add("ripgrep's default engine cannot read this pattern (" + string.Join(" ", refusals) + ") so -P is added, which needs a ripgrep built with PCRE2.");
        }
        var sb = new StringBuilder("rg");
        if (usePcre) sb.Append(" -P");
        sb.Append(flags.IgnoreCase ? " -i" : " -s");
        if (flags.Multiline) { notes.Add("Multiline is implied: ripgrep matches ^ and $ at the start and end of every line, unless --multiline is used."); }
        if (flags.Singleline) { sb.Append(" -U --multiline-dotall"); notes.Add("Singleline needs --multiline, so the whole file is searched as one text and a match may span lines."); }
        var inline = (flags.IgnorePatternWhitespace ? "x" : "") + (flags.ExplicitCapture && usePcre ? "n" : "");
        if (flags.ExplicitCapture && !usePcre) notes.Add("ExplicitCapture is dropped: ripgrep groups always capture, which only matters for replacements.");
        if (flags.CultureInvariant) notes.Add("CultureInvariant has no equivalent.");
        if (flags.NonBacktracking) notes.Add("NonBacktracking is an engine choice and is dropped.");
        if (!flags.IgnoreCase) notes.Add("-s makes the search case sensitive, which is what the IgnoreCase flag being off means here (ripgrep's default is smart case).");
        var text = (inline.Length > 0 ? "(?" + inline + ")" : "") + pattern;
        foreach (var g in GlobArguments(globs, "-g ", "-g !", ShellQuote, notes).Select(a => a.Replace("-g !'", "-g '!"))) sb.Append(' ').Append(g);
        sb.Append(" -e ").Append(ShellQuote(text)).Append(' ').Append(FolderText(folder, ShellQuote));
        notes.Add("Quoting is for a POSIX shell. In PowerShell, use single quotes and double each single quote inside the pattern.");
        return Result(target, sb.ToString(), differences, notes);
    }

    // ----- Select-String -----

    private static RegexConversion SelectString(string pattern, RegexBuilderFlags flags, string? folder, string? globs)
    {
        const string target = "Select-String";
        if (flags.Singleline) return Refuse(target, "Select-String tests one line at a time, so Singleline cannot apply.");
        if (flags.RightToLeft) return Refuse(target, "Select-String has no right-to-left switch.");
        var notes = new List<string> { "Select-String uses .NET regular expressions, so the pattern means the same thing." };
        if (flags.Multiline) notes.Add("Multiline is implied: Select-String tests one line at a time, so ^ and $ match at the start and end of every line.");
        if (flags.CultureInvariant) notes.Add("CultureInvariant is dropped; Select-String uses the current culture for case folding.");
        if (flags.NonBacktracking) notes.Add("NonBacktracking is dropped; Select-String cannot set regular expression options.");
        var prefix = (flags.IgnorePatternWhitespace ? "x" : "") + (flags.ExplicitCapture ? "n" : "");
        var text = (prefix.Length > 0 ? "(?" + prefix + ")" : "") + pattern;
        var sb = new StringBuilder();
        string Quote(string s) => "'" + s.Replace("'", "''") + "'";
        var path = string.IsNullOrWhiteSpace(folder) ? "." : folder.Trim();
        var include = (globs ?? "").Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(g => !g.StartsWith('!') && g != "*").ToList();
        var exclude = (globs ?? "").Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(g => g.StartsWith('!')).Select(g => g[1..]).ToList();
        if (!string.IsNullOrWhiteSpace(folder))
        {
            sb.Append("Get-ChildItem -LiteralPath ").Append(Quote(path)).Append(" -Recurse -File");
            if (include.Count > 0) sb.Append(" -Include ").Append(string.Join(",", include.Select(Quote)));
            if (exclude.Count > 0) sb.Append(" -Exclude ").Append(string.Join(",", exclude.Select(Quote)));
            sb.Append(" | Select-String -Pattern ").Append(Quote(text));
            notes.Add("Get-ChildItem -Include and -Exclude match file names only.");
        }
        else sb.Append("Select-String -Path ").Append(Quote(".\\*")).Append(" -Pattern ").Append(Quote(text));
        if (!flags.IgnoreCase) sb.Append(" -CaseSensitive");
        else notes.Add("Select-String ignores case by default, which matches the IgnoreCase flag.");
        return new(target, sb.ToString(), true, false, notes);
    }

    // ----- source code in other languages -----

    /// <summary>A JavaScript regular expression literal body: slashes escaped, line breaks escaped.</summary>
    public static string JavaScriptLiteral(string pattern, string flagLetters)
    {
        var sb = new StringBuilder("/");
        var inSet = false;
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '\\' && i + 1 < pattern.Length) { sb.Append(c).Append(pattern[++i] switch { '\n' => "n", '\r' => "r", var x => x.ToString() }); continue; }
            if (c == '[') inSet = true; else if (c == ']') inSet = false;
            if (c == '/' && !inSet) sb.Append("\\/");
            else if (c == '\n') sb.Append("\\n");
            else if (c == '\r') sb.Append("\\r");
            else if (c == (char)0x2028) sb.Append("\\u2028");
            else if (c == (char)0x2029) sb.Append("\\u2029");
            else sb.Append(c);
        }
        return sb.Append('/').Append(flagLetters).ToString();
    }

    private static RegexConversion JavaScript(string pattern, RegexBuilderFlags flags, RegexCodeKind kind, string replacement)
    {
        const string target = "JavaScript";
        if (flags.RightToLeft) return Refuse(target, "JavaScript has no right-to-left matching.");
        var (refusals, differences) = Compatibility(pattern, flags, "js");
        if (refusals.Count > 0) return new(target, "", false, false, ["Not convertible: " + string.Join(" ", refusals)]);
        var notes = new List<string>();
        var letters = (flags.IgnoreCase ? "i" : "") + (flags.Multiline ? "m" : "") + (flags.Singleline ? "s" : "") + "u";
        notes.Add("The u flag is added so that Unicode escapes and \\p{...} work; it also makes unknown escapes an error.");
        if (flags.CultureInvariant) notes.Add("CultureInvariant has no equivalent.");
        if (flags.NonBacktracking) notes.Add("NonBacktracking is an engine choice and is dropped.");
        if (flags.ExplicitCapture) notes.Add("ExplicitCapture is dropped: JavaScript groups always capture.");
        var global = kind is RegexCodeKind.Matches or RegexCodeKind.Replace ? "g" : "";
        var literal = JavaScriptLiteral(pattern, letters + global);
        var text = kind switch
        {
            RegexCodeKind.IsMatch => $"const found = {literal}.test(input);",
            RegexCodeKind.Matches => $"for (const match of input.matchAll({literal})) {{\n  console.log(match.index, match[0]);\n}}",
            RegexCodeKind.Replace => $"const output = input.replace({literal}, {JsonQuote(replacement)});",
            _ => $"const pieces = input.split({literal});",
        };
        if (kind == RegexCodeKind.Replace) notes.Add("JavaScript replacements use $1, $<name>, $&, $` and $'; ${name} is not understood, write $<name> instead.");
        return Result(target, text, differences, notes);
    }

    private static string JsonQuote(string s) => System.Text.Json.JsonSerializer.Serialize(s, new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    /// <summary>A Python string literal for the pattern: a raw string when that is exact, otherwise an escaped string.</summary>
    public static string PythonString(string text)
    {
        var rawSafe = text.Length > 0 && !text.Contains('\'') && !text.Contains('\n') && !text.Contains('\r') && !text.EndsWith('\\') && text.All(c => c >= ' ' && c != '\u007f');
        if (rawSafe) return "r'" + text + "'";
        var sb = new StringBuilder("'");
        foreach (var c in text)
            sb.Append(c switch { '\\' => @"\\", '\'' => @"\'", '\n' => @"\n", '\r' => @"\r", '\t' => @"\t", < ' ' or '\u007f' => "\\x" + ((int)c).ToString("x2"), _ => c.ToString() });
        return sb.Append('\'').ToString();
    }

    private static RegexConversion Python(string pattern, RegexBuilderFlags flags, RegexCodeKind kind, string replacement)
    {
        const string target = "Python";
        if (flags.RightToLeft) return Refuse(target, "Python's re module has no right-to-left matching.");
        var (refusals, differences) = Compatibility(pattern, flags, "python");
        if (refusals.Count > 0) return new(target, "", false, false, ["Not convertible: " + string.Join(" ", refusals)]);
        var notes = new List<string> { "Python 3.14 was the version the table was checked against." };
        var options = new List<string>();
        if (flags.IgnoreCase) options.Add("re.IGNORECASE");
        if (flags.Multiline) options.Add("re.MULTILINE");
        if (flags.Singleline) options.Add("re.DOTALL");
        if (flags.IgnorePatternWhitespace) options.Add("re.VERBOSE");
        if (flags.CultureInvariant) notes.Add("CultureInvariant has no equivalent.");
        if (flags.NonBacktracking) notes.Add("NonBacktracking is an engine choice and is dropped.");
        if (flags.ExplicitCapture) notes.Add("ExplicitCapture is dropped: Python groups always capture.");
        var compile = $"pattern = re.compile({PythonString(pattern)}" + (options.Count > 0 ? ", " + string.Join(" | ", options) : "") + ")";
        var text = "import re\n" + compile + "\n" + kind switch
        {
            RegexCodeKind.IsMatch => "found = pattern.search(text) is not None",
            RegexCodeKind.Matches => "for match in pattern.finditer(text):\n    print(match.start(), match.group(0))",
            RegexCodeKind.Replace => $"output = pattern.sub({PythonString(replacement)}, text)",
            _ => "pieces = pattern.split(text)",
        };
        if (kind == RegexCodeKind.Replace) notes.Add("Python replacements use \\1 and \\g<name>; $1 and ${name} are not understood. Rewrite the replacement before use.");
        return Result(target, text, differences, notes);
    }

    /// <summary>A Java string literal with \\, \", control characters and non-ASCII characters escaped.</summary>
    public static string JavaString(string text)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in text)
            sb.Append(c switch { '\\' => @"\\", '"' => "\\\"", '\n' => @"\n", '\r' => @"\r", '\t' => @"\t", '\f' => @"\f", '\b' => @"\b", < ' ' or '\u007f' => "\\u" + ((int)c).ToString("x4"), _ => c.ToString() });
        return sb.Append('"').ToString();
    }

    private static RegexConversion Java(string pattern, RegexBuilderFlags flags, RegexCodeKind kind, string replacement)
    {
        const string target = "Java";
        if (flags.RightToLeft) return Refuse(target, "java.util.regex has no right-to-left matching.");
        // Java's engine is close to PCRE2 for the constructs in the table; the table has no Java column, so only PCRE2-unsupported ones are refused.
        var (refusals, differences) = Compatibility(pattern, flags, "pcre2");
        if (refusals.Count > 0) return new(target, "", false, false, ["Not convertible: " + string.Join(" ", refusals) + " (checked against PCRE2, the closest column; Java has no column of its own)."]);
        var notes = new List<string> { "The comparison table has no Java column. PCRE2 was used as the nearest engine, so differences are not fully known. Test the pattern in Java." };
        var options = new List<string>();
        if (flags.IgnoreCase) options.Add("Pattern.CASE_INSENSITIVE");
        if (flags.Multiline) options.Add("Pattern.MULTILINE");
        if (flags.Singleline) options.Add("Pattern.DOTALL");
        if (flags.IgnorePatternWhitespace) options.Add("Pattern.COMMENTS");
        if (flags.CultureInvariant) notes.Add("CultureInvariant has no equivalent.");
        if (flags.NonBacktracking) notes.Add("NonBacktracking is an engine choice and is dropped.");
        if (flags.ExplicitCapture) notes.Add("ExplicitCapture is dropped: Java groups always capture.");
        var compile = $"Pattern pattern = Pattern.compile({JavaString(pattern)}" + (options.Count > 0 ? ", " + string.Join(" | ", options) : "") + ");";
        var text = compile + "\n" + kind switch
        {
            RegexCodeKind.IsMatch => "boolean found = pattern.matcher(input).find();",
            RegexCodeKind.Matches => "Matcher matcher = pattern.matcher(input);\nwhile (matcher.find()) {\n    System.out.println(matcher.start() + \": \" + matcher.group());\n}",
            RegexCodeKind.Replace => $"String output = pattern.matcher(input).replaceAll({JavaString(replacement)});",
            _ => "String[] pieces = pattern.split(input);",
        };
        if (kind == RegexCodeKind.Replace) notes.Add("Java replacements use $1 and ${name}; \\ and $ must be escaped with a backslash.");
        return Result(target, text, differences, notes);
    }

    /// <summary>A Go string literal: a raw (backtick) string when the text has no backtick, otherwise an escaped one.</summary>
    public static string GoString(string text)
    {
        if (!text.Contains('`') && text.All(c => c is '\t' or '\n' || (c >= ' ' && c != '\u007f')) && !text.Contains('\r')) return "`" + text + "`";
        var sb = new StringBuilder("\"");
        foreach (var c in text)
            sb.Append(c switch { '\\' => @"\\", '"' => "\\\"", '\n' => @"\n", '\r' => @"\r", '\t' => @"\t", < ' ' or '\u007f' => "\\x" + ((int)c).ToString("x2"), _ => c.ToString() });
        return sb.Append('"').ToString();
    }

    private static RegexConversion GoLang(string pattern, RegexBuilderFlags flags, RegexCodeKind kind, string replacement)
    {
        const string target = "Go";
        if (flags.RightToLeft) return Refuse(target, "Go's regexp package has no right-to-left matching.");
        if (flags.IgnorePatternWhitespace) return Refuse(target, "Go's regexp package has no option to ignore white space and # comments.");
        var (refusals, differences) = Compatibility(pattern, flags, "go");
        if (refusals.Count > 0) return new(target, "", false, false, ["Not convertible: " + string.Join(" ", refusals)]);
        var notes = new List<string> { "Go's regexp package runs in time linear in the text, like the non-backtracking engine." };
        var inline = (flags.IgnoreCase ? "i" : "") + (flags.Multiline ? "m" : "") + (flags.Singleline ? "s" : "");
        var text = (inline.Length > 0 ? "(?" + inline + ")" : "") + pattern;
        if (flags.CultureInvariant) notes.Add("CultureInvariant has no equivalent.");
        if (flags.NonBacktracking) notes.Add("NonBacktracking is already how Go matches.");
        if (flags.ExplicitCapture) notes.Add("ExplicitCapture is dropped: Go groups always capture.");
        var compile = $"re := regexp.MustCompile({GoString(text)})";
        var body = kind switch
        {
            RegexCodeKind.IsMatch => "found := re.MatchString(input)",
            RegexCodeKind.Matches => "for _, loc := range re.FindAllStringIndex(input, -1) {\n    fmt.Println(loc[0], input[loc[0]:loc[1]])\n}",
            RegexCodeKind.Replace => $"output := re.ReplaceAllString(input, {GoString(replacement)})",
            _ => "pieces := re.Split(input, -1)",
        };
        if (kind == RegexCodeKind.Replace) notes.Add("Go replacements use $1 and ${name}; a name followed by a letter must be written ${name}.");
        return Result(target, compile + "\n" + body, differences, notes);
    }

    /// <summary>A PHP preg_* argument: slash-delimited, with flags after the closing slash, as a single-quoted string.</summary>
    public static string PhpPattern(string pattern, string flagLetters)
    {
        var delimited = "/" + pattern.Replace("/", "\\/") + "/" + flagLetters;
        return "'" + delimited.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
    }

    private static RegexConversion Php(string pattern, RegexBuilderFlags flags, RegexCodeKind kind, string replacement)
    {
        const string target = "PHP";
        if (flags.RightToLeft) return Refuse(target, "PHP's PCRE functions have no right-to-left matching.");
        var (refusals, differences) = Compatibility(pattern, flags, "pcre2");
        if (refusals.Count > 0) return new(target, "", false, false, ["Not convertible: " + string.Join(" ", refusals)]);
        var notes = new List<string> { "PHP uses PCRE2. The u modifier is added so that the pattern and text are read as UTF-8." };
        var letters = (flags.IgnoreCase ? "i" : "") + (flags.Multiline ? "m" : "") + (flags.Singleline ? "s" : "") + (flags.IgnorePatternWhitespace ? "x" : "") + "u";
        if (flags.CultureInvariant) notes.Add("CultureInvariant has no equivalent.");
        if (flags.NonBacktracking) notes.Add("NonBacktracking is an engine choice and is dropped.");
        var body = flags.ExplicitCapture ? "(?n)" + pattern : pattern;
        var arg = PhpPattern(body, letters);
        var phpString = (string s) => "'" + s.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
        var text = kind switch
        {
            RegexCodeKind.IsMatch => $"$found = preg_match({arg}, $input) === 1;",
            RegexCodeKind.Matches => $"preg_match_all({arg}, $input, $matches, PREG_OFFSET_CAPTURE);",
            RegexCodeKind.Replace => $"$output = preg_replace({arg}, {phpString(replacement)}, $input);",
            _ => $"$pieces = preg_split({arg}, $input);",
        };
        if (kind == RegexCodeKind.Replace) notes.Add("PHP replacements use $1, ${1} and \\1; named references are not available, use numbers.");
        return Result(target, text, differences, notes);
    }
}
