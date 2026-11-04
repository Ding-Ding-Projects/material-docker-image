using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>
/// Editable source templates for the generated code snippets. A template is plain text with <c>{{placeholders}}</c>; the builder fills in the pattern and
/// replacement already quoted for the language, so editing a template can change the surrounding code but never the quoting. The default templates are the
/// built-in snippet shapes (the checks compare their output with <see cref="RegexConverter"/> for every language and kind), and a user template replaces one
/// default at a time. Whether the pattern can be expressed in the language at all is still decided by the converter, so a refused conversion stays refused.
/// </summary>
public static class RegexCodeTemplates
{
    public const string Format = "material-docker-image/regex-templates";
    public const int MaxTemplateLength = 4096;
    public const int MaxTemplates = 28;
    public const string FileName = "regex-templates.json";

    /// <summary>The languages that have editable templates, in display order.</summary>
    public static readonly IReadOnlyList<string> Languages = ["C# code", "PowerShell", "JavaScript", "Python", "Java", "Go", "PHP"];

    /// <summary>Placeholders and what each one holds.</summary>
    public static readonly IReadOnlyList<(string Name, string Meaning)> Placeholders =
    [
        ("pattern", "The pattern as a quoted literal for the language (for Go and PHP, with the options folded in as that language writes them)."),
        ("options", "The options expression for the language, for example RegexOptions.IgnoreCase or re.IGNORECASE | re.MULTILINE."),
        ("options_arg", "The options as a trailing argument including its comma, or nothing when there are none."),
        ("replacement", "The replacement text as a quoted literal for the language."),
        ("raw_pattern", "The pattern exactly as typed, not quoted."),
        ("raw_replacement", "The replacement exactly as typed, not quoted."),
        ("kind", "The kind of code: IsMatch, Matches, Replace or Split."),
        ("language", "The name of the language."),
    ];

    public static string DefaultPath => Path.Combine(UserSettings.DataDirectory, FileName);

    public static string Key(string language, RegexCodeKind kind) => language + "|" + kind;

    /// <summary>The built-in template for a language and kind, or null when the language has none.</summary>
    public static string? Default(string language, RegexCodeKind kind) => (language, kind) switch
    {
        ("C# code", RegexCodeKind.IsMatch) => "var regex = new Regex({{pattern}}, {{options}}, TimeSpan.FromMilliseconds(250));\nbool found = regex.IsMatch(input);",
        ("C# code", RegexCodeKind.Matches) => "var regex = new Regex({{pattern}}, {{options}}, TimeSpan.FromMilliseconds(250));\nforeach (Match match in regex.Matches(input))\n{\n    Console.WriteLine($\"{match.Index}: {match.Value}\");\n    foreach (Group group in match.Groups)\n        Console.WriteLine($\"  {group.Name}: {group.Value}\");\n}",
        ("C# code", RegexCodeKind.Replace) => "var regex = new Regex({{pattern}}, {{options}}, TimeSpan.FromMilliseconds(250));\nstring output = regex.Replace(input, {{replacement}});",
        ("C# code", RegexCodeKind.Split) => "var regex = new Regex({{pattern}}, {{options}}, TimeSpan.FromMilliseconds(250));\nstring[] pieces = regex.Split(input);",
        ("PowerShell", RegexCodeKind.IsMatch) => "[regex]::new({{pattern}}, '{{options}}').IsMatch($text)",
        ("PowerShell", RegexCodeKind.Matches) => "[regex]::new({{pattern}}, '{{options}}').Matches($text) | ForEach-Object { $_.Value }",
        ("PowerShell", RegexCodeKind.Replace) => "[regex]::new({{pattern}}, '{{options}}').Replace($text, {{replacement}})",
        ("PowerShell", RegexCodeKind.Split) => "[regex]::new({{pattern}}, '{{options}}').Split($text)",
        ("JavaScript", RegexCodeKind.IsMatch) => "const found = {{pattern}}.test(input);",
        ("JavaScript", RegexCodeKind.Matches) => "for (const match of input.matchAll({{pattern}})) {\n  console.log(match.index, match[0]);\n}",
        ("JavaScript", RegexCodeKind.Replace) => "const output = input.replace({{pattern}}, {{replacement}});",
        ("JavaScript", RegexCodeKind.Split) => "const pieces = input.split({{pattern}});",
        ("Python", RegexCodeKind.IsMatch) => "import re\npattern = re.compile({{pattern}}{{options_arg}})\nfound = pattern.search(text) is not None",
        ("Python", RegexCodeKind.Matches) => "import re\npattern = re.compile({{pattern}}{{options_arg}})\nfor match in pattern.finditer(text):\n    print(match.start(), match.group(0))",
        ("Python", RegexCodeKind.Replace) => "import re\npattern = re.compile({{pattern}}{{options_arg}})\noutput = pattern.sub({{replacement}}, text)",
        ("Python", RegexCodeKind.Split) => "import re\npattern = re.compile({{pattern}}{{options_arg}})\npieces = pattern.split(text)",
        ("Java", RegexCodeKind.IsMatch) => "Pattern pattern = Pattern.compile({{pattern}}{{options_arg}});\nboolean found = pattern.matcher(input).find();",
        ("Java", RegexCodeKind.Matches) => "Pattern pattern = Pattern.compile({{pattern}}{{options_arg}});\nMatcher matcher = pattern.matcher(input);\nwhile (matcher.find()) {\n    System.out.println(matcher.start() + \": \" + matcher.group());\n}",
        ("Java", RegexCodeKind.Replace) => "Pattern pattern = Pattern.compile({{pattern}}{{options_arg}});\nString output = pattern.matcher(input).replaceAll({{replacement}});",
        ("Java", RegexCodeKind.Split) => "Pattern pattern = Pattern.compile({{pattern}}{{options_arg}});\nString[] pieces = pattern.split(input);",
        ("Go", RegexCodeKind.IsMatch) => "re := regexp.MustCompile({{pattern}})\nfound := re.MatchString(input)",
        ("Go", RegexCodeKind.Matches) => "re := regexp.MustCompile({{pattern}})\nfor _, loc := range re.FindAllStringIndex(input, -1) {\n    fmt.Println(loc[0], input[loc[0]:loc[1]])\n}",
        ("Go", RegexCodeKind.Replace) => "re := regexp.MustCompile({{pattern}})\noutput := re.ReplaceAllString(input, {{replacement}})",
        ("Go", RegexCodeKind.Split) => "re := regexp.MustCompile({{pattern}})\npieces := re.Split(input, -1)",
        ("PHP", RegexCodeKind.IsMatch) => "$found = preg_match({{pattern}}, $input) === 1;",
        ("PHP", RegexCodeKind.Matches) => "preg_match_all({{pattern}}, $input, $matches, PREG_OFFSET_CAPTURE);",
        ("PHP", RegexCodeKind.Replace) => "$output = preg_replace({{pattern}}, {{replacement}}, $input);",
        ("PHP", RegexCodeKind.Split) => "$pieces = preg_split({{pattern}}, $input);",
        _ => null,
    };

    private static readonly Regex placeholder = new(@"\{\{\s*([A-Za-z_]+)\s*\}\}", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    /// <summary>The placeholder names a template uses that this builder does not know, in order of first appearance.</summary>
    public static IReadOnlyList<string> UnknownPlaceholders(string template) =>
        placeholder.Matches(template ?? "").Select(m => m.Groups[1].Value).Where(n => !Placeholders.Any(p => p.Name == n)).Distinct().ToList();

    /// <summary>Why a template cannot be saved, or null when it can.</summary>
    public static string? Validate(string? template)
    {
        if (string.IsNullOrWhiteSpace(template)) return "A template cannot be empty. Use Reset to go back to the built-in one.";
        if (template.Length > MaxTemplateLength) return $"A template can be at most {MaxTemplateLength} characters.";
        var unknown = UnknownPlaceholders(template);
        if (unknown.Count > 0) return "Unknown placeholder " + string.Join(", ", unknown.Select(u => "{{" + u + "}}")) + ". Known placeholders: " + string.Join(", ", Placeholders.Select(p => "{{" + p.Name + "}}")) + ".";
        if (!placeholder.IsMatch(template)) return "A template needs at least one placeholder, usually {{pattern}}; otherwise the generated code would not contain the pattern.";
        return null;
    }

    /// <summary>Fills a template. Every placeholder in <paramref name="values"/> is replaced; unknown ones are left as written (Validate refuses them).</summary>
    public static string Fill(string template, IReadOnlyDictionary<string, string> values) =>
        placeholder.Replace(template, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

    /// <summary>The values for one language, quoted exactly as the converter quotes them. Null when the language has no templates.</summary>
    public static Dictionary<string, string>? Values(string language, string pattern, RegexBuilderFlags flags, RegexCodeKind kind, string replacement)
    {
        string options, optionsArg, pat, rep;
        switch (language)
        {
            case "C# code":
                options = RegexConverter.CSharpOptions(flags); optionsArg = ", " + options;
                pat = RegexBuilderEngine.ToCSharpVerbatim(pattern); rep = RegexConverter.CSharpString(replacement); break;
            case "PowerShell":
                {
                    var names = RegexBuilderFlags.All.Where(f => flags.Get(f.Name)).Select(f => f.Name).ToList();
                    options = names.Count == 0 ? "None" : string.Join(", ", names); optionsArg = "";
                    pat = "'" + pattern.Replace("'", "''") + "'"; rep = "'" + replacement.Replace("'", "''") + "'"; break;
                }
            case "JavaScript":
                {
                    var letters = (flags.IgnoreCase ? "i" : "") + (flags.Multiline ? "m" : "") + (flags.Singleline ? "s" : "") + "u";
                    options = letters; optionsArg = "";
                    pat = RegexConverter.JavaScriptLiteral(pattern, letters + (kind is RegexCodeKind.Matches or RegexCodeKind.Replace ? "g" : ""));
                    rep = System.Text.Json.JsonSerializer.Serialize(replacement, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }); break;
                }
            case "Python":
                {
                    var list = new List<string>();
                    if (flags.IgnoreCase) list.Add("re.IGNORECASE");
                    if (flags.Multiline) list.Add("re.MULTILINE");
                    if (flags.Singleline) list.Add("re.DOTALL");
                    if (flags.IgnorePatternWhitespace) list.Add("re.VERBOSE");
                    options = list.Count == 0 ? "0" : string.Join(" | ", list); optionsArg = list.Count == 0 ? "" : ", " + options;
                    pat = RegexConverter.PythonString(pattern); rep = RegexConverter.PythonString(replacement); break;
                }
            case "Java":
                {
                    var list = new List<string>();
                    if (flags.IgnoreCase) list.Add("Pattern.CASE_INSENSITIVE");
                    if (flags.Multiline) list.Add("Pattern.MULTILINE");
                    if (flags.Singleline) list.Add("Pattern.DOTALL");
                    if (flags.IgnorePatternWhitespace) list.Add("Pattern.COMMENTS");
                    options = list.Count == 0 ? "0" : string.Join(" | ", list); optionsArg = list.Count == 0 ? "" : ", " + options;
                    pat = RegexConverter.JavaString(pattern); rep = RegexConverter.JavaString(replacement); break;
                }
            case "Go":
                {
                    var inline = (flags.IgnoreCase ? "i" : "") + (flags.Multiline ? "m" : "") + (flags.Singleline ? "s" : "");
                    options = inline; optionsArg = "";
                    pat = RegexConverter.GoString((inline.Length > 0 ? "(?" + inline + ")" : "") + pattern); rep = RegexConverter.GoString(replacement); break;
                }
            case "PHP":
                {
                    var letters = (flags.IgnoreCase ? "i" : "") + (flags.Multiline ? "m" : "") + (flags.Singleline ? "s" : "") + (flags.IgnorePatternWhitespace ? "x" : "") + "u";
                    options = letters; optionsArg = "";
                    pat = RegexConverter.PhpPattern(flags.ExplicitCapture ? "(?n)" + pattern : pattern, letters);
                    rep = "'" + replacement.Replace("\\", "\\\\").Replace("'", "\\'") + "'"; break;
                }
            default: return null;
        }
        return new Dictionary<string, string>
        {
            ["pattern"] = pat, ["options"] = options, ["options_arg"] = optionsArg, ["replacement"] = rep,
            ["raw_pattern"] = pattern, ["raw_replacement"] = replacement, ["kind"] = kind.ToString(), ["language"] = language,
        };
    }

    /// <summary>
    /// Renders the code for a language and kind. The converter decides first whether the pattern can be expressed; a refused conversion is returned as it is.
    /// The template used is the user's when <paramref name="custom"/> has one for this language and kind, otherwise the built-in one.
    /// </summary>
    public static RegexConversion Render(string language, string pattern, RegexBuilderFlags flags, RegexCodeKind kind, string replacement, IReadOnlyDictionary<string, string>? custom)
    {
        var baseline = RegexConverter.Convert(language, pattern, flags, kind, replacement);
        var template = custom != null && custom.TryGetValue(Key(language, kind), out var own) ? own : Default(language, kind);
        if (!baseline.Converted || template == null) return baseline;
        var values = Values(language, pattern, flags, kind, replacement)!;
        var text = Fill(template, values);
        var notes = baseline.Notes.ToList();
        if (custom != null && custom.ContainsKey(Key(language, kind))) notes.Insert(0, "Rendered with your template for " + language + " " + kind + "; the notes below describe the built-in snippet.");
        return baseline with { Text = text, Notes = notes };
    }

    // ----- persistence -----

    /// <summary>Loads user templates. A missing file is an empty set; a corrupt or oversize one throws InvalidDataException.</summary>
    public static Dictionary<string, string> Load(string path)
    {
        if (!File.Exists(path)) return new();
        var info = new FileInfo(path);
        if (info.Length > 512 * 1024) throw new InvalidDataException("The templates file is larger than 512 KiB and was not read.");
        return Parse(File.ReadAllText(path, new UTF8Encoding(false, true)));
    }

    public static Dictionary<string, string> Parse(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var f) || f.GetString() != Format) throw new InvalidDataException("The templates file has an unknown format.");
            if (!root.TryGetProperty("version", out var v) || v.GetInt32() != 1) throw new InvalidDataException("The templates file has an unsupported version.");
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in root.GetProperty("templates").EnumerateObject())
            {
                if (!IsKnownKey(entry.Name)) throw new InvalidDataException("The templates file names a template that does not exist: " + entry.Name + ".");
                var body = entry.Value.GetString() ?? "";
                var problem = Validate(body);
                if (problem != null) throw new InvalidDataException($"The template {entry.Name} is not valid: {problem}");
                result[entry.Name] = body;
            }
            return result;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("The templates file is not valid: " + e.Message, e);
        }
    }

    private static bool IsKnownKey(string key) => Languages.Any(l => Enum.GetValues<RegexCodeKind>().Any(k => Key(l, k) == key));

    public static string Serialize(IReadOnlyDictionary<string, string> templates)
    {
        if (templates.Count > MaxTemplates) throw new InvalidOperationException($"At most {MaxTemplates} templates can be saved.");
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("format", Format);
            w.WriteNumber("version", 1);
            w.WriteStartObject("templates");
            foreach (var (key, body) in templates.OrderBy(t => t.Key, StringComparer.Ordinal)) w.WriteString(key, body);
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Returns a copy of the set with one template changed. Refuses an unknown key or an invalid template.</summary>
    public static Dictionary<string, string> With(IReadOnlyDictionary<string, string> templates, string language, RegexCodeKind kind, string body)
    {
        if (Default(language, kind) == null) throw new ArgumentException($"{language} has no editable template.");
        var problem = Validate(body);
        if (problem != null) throw new ArgumentException(problem);
        var copy = new Dictionary<string, string>(templates, StringComparer.Ordinal) { [Key(language, kind)] = body };
        return copy;
    }

    /// <summary>Returns a copy without the user's template for a language and kind, so the built-in one applies again.</summary>
    public static Dictionary<string, string> Without(IReadOnlyDictionary<string, string> templates, string language, RegexCodeKind kind)
    {
        var copy = new Dictionary<string, string>(templates, StringComparer.Ordinal);
        copy.Remove(Key(language, kind));
        return copy;
    }

    /// <summary>Writes atomically (temporary file, then replace).</summary>
    public static void Save(string path, IReadOnlyDictionary<string, string> templates)
    {
        var text = Serialize(templates);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, text, new UTF8Encoding(false));
        File.Move(temp, path, true);
    }
}
