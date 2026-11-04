using System.Text;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>A named parameter found in a pattern as {{name}} or {{name:default}}.</summary>
public sealed record RegexParameter(string Name, string? Default, int Uses);

/// <summary>The pattern with its parameters filled, or the reasons it could not be.</summary>
public sealed record RegexParameterFill(bool Success, string Pattern, IReadOnlyList<string> Problems);

/// <summary>
/// Parameterized patterns. A placeholder is {{name}} or {{name:default}}; the pattern with placeholders still compiles (it matches the
/// literal text), so library entries stay valid. Filling happens in one pass, so a value that itself contains {{x}} is never expanded again.
/// By default a value is inserted as literal text (escaped); "as pattern" inserts it unescaped for a deliberate regular expression fragment.
/// </summary>
public static class RegexParameters
{
    public const int MaxValueLength = 500;
    public const int MaxParameters = 32;

    private static readonly Regex placeholder = new(@"\{\{(?<name>[A-Za-z_][A-Za-z0-9_]{0,39})(?::(?<default>[^{}]{0,200}))?\}\}", RegexOptions.NonBacktracking);

    /// <summary>Distinct parameters in order of first use. A later default does not override the first one.</summary>
    public static IReadOnlyList<RegexParameter> Find(string? pattern)
    {
        var order = new List<string>();
        var defaults = new Dictionary<string, string?>(StringComparer.Ordinal);
        var uses = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match m in placeholder.Matches(pattern ?? ""))
        {
            var name = m.Groups["name"].Value;
            if (!defaults.ContainsKey(name)) { order.Add(name); defaults[name] = m.Groups["default"].Success ? m.Groups["default"].Value : null; }
            else if (defaults[name] == null && m.Groups["default"].Success) defaults[name] = m.Groups["default"].Value;
            uses[name] = uses.GetValueOrDefault(name) + 1;
        }
        return order.Take(MaxParameters).Select(n => new RegexParameter(n, defaults[n], uses[n])).ToList();
    }

    /// <summary>
    /// Fills the placeholders. A parameter without a value uses its default; one with neither is a problem. The result must also compile
    /// with the given flags, otherwise it is reported as a problem and no pattern is returned.
    /// </summary>
    public static RegexParameterFill Fill(string? pattern, IReadOnlyDictionary<string, string> values, RegexBuilderFlags flags, bool asPattern = false)
    {
        var text = pattern ?? "";
        var problems = new List<string>();
        var found = Find(text);
        if (found.Count == 0) return new(false, text, ["The pattern has no {{name}} placeholders."]);
        foreach (var p in found)
        {
            if (values.TryGetValue(p.Name, out var v) && v.Length > MaxValueLength) problems.Add($"The value of {p.Name} is longer than {MaxValueLength} characters.");
            else if ((!values.TryGetValue(p.Name, out var given) || given.Length == 0) && p.Default == null) problems.Add($"Give a value for {p.Name}.");
        }
        if (problems.Count > 0) return new(false, text, problems);
        var result = placeholder.Replace(text, m =>
        {
            var name = m.Groups["name"].Value;
            var value = values.TryGetValue(name, out var given) && given.Length > 0 ? given : m.Groups["default"].Success ? m.Groups["default"].Value : defaultOf(name);
            return asPattern ? value : Regex.Escape(value);
        });
        var check = RegexBuilderEngine.Validate(result, flags);
        if (!check.Valid) return new(false, text, [check.Error ?? "The filled pattern is not valid."]);
        return new(true, result, []);

        string defaultOf(string name) => found.First(p => p.Name == name).Default ?? "";
    }

    /// <summary>Marks a literal piece of the pattern as a parameter, for example turning "alpine" into "{{base:alpine}}".</summary>
    public static string MakeParameter(string name, string? defaultValue = null)
    {
        if (!Regex.IsMatch(name ?? "", @"^[A-Za-z_][A-Za-z0-9_]{0,39}$", RegexOptions.NonBacktracking)) throw new ArgumentException("A parameter name starts with a letter or _ and has up to 40 letters, digits or _.");
        if (defaultValue != null && (defaultValue.Length > 200 || defaultValue.IndexOfAny(['{', '}']) >= 0)) throw new ArgumentException("A default has at most 200 characters and no braces.");
        return "{{" + name + (defaultValue == null ? "" : ":" + defaultValue) + "}}";
    }
}

/// <summary>
/// Converts POSIX bracket classes such as [[:alpha:]] into .NET equivalents. .NET has no POSIX classes: typed directly, [[:alpha:]] is read as
/// a set holding "[:alph" followed by a stray bracket, which is why the builder converts instead of accepting them.
/// </summary>
public static class RegexPosixTranslator
{
    /// <summary>Members of the replacement set for each class name. Letters, digits and case follow Unicode; punctuation, graph, print and control follow ASCII.</summary>
    public static readonly IReadOnlyDictionary<string, string> Classes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["alpha"] = @"\p{L}", ["digit"] = "0-9", ["alnum"] = @"\p{L}\p{Nd}", ["upper"] = @"\p{Lu}", ["lower"] = @"\p{Ll}",
        ["space"] = @"\s", ["blank"] = @" \t", ["punct"] = @"!-/:-@\[-`{-~", ["cntrl"] = @"\x00-\x1F\x7F", ["graph"] = @"\x21-\x7E",
        ["print"] = @"\x20-\x7E", ["xdigit"] = "0-9A-Fa-f", ["word"] = @"\w",
    };

    public sealed record Translation(bool Success, string Pattern, int Replaced, string? Error);

    /// <summary>Rewrites every [:name:] inside a bracket expression. Anything else is copied unchanged. Unknown or negated ([:^name:]) classes are refused.</summary>
    public static Translation Translate(string? pattern)
    {
        var p = pattern ?? "";
        var sb = new StringBuilder();
        var replaced = 0;
        var i = 0;
        while (i < p.Length)
        {
            var c = p[i];
            if (c == '\\') { sb.Append(c); if (i + 1 < p.Length) sb.Append(p[i + 1]); i += 2; continue; }
            if (c != '[') { sb.Append(c); i++; continue; }
            // A bracket expression: copy to its end, translating classes.
            sb.Append('['); i++;
            if (i < p.Length && p[i] == '^') { sb.Append('^'); i++; }
            if (i < p.Length && p[i] == ']') { sb.Append("\\]"); i++; }
            var closed = false;
            while (i < p.Length)
            {
                var d = p[i];
                if (d == '\\') { sb.Append(d); if (i + 1 < p.Length) sb.Append(p[i + 1]); i += 2; continue; }
                if (d == ']') { sb.Append(']'); i++; closed = true; break; }
                if (d == '[' && i + 1 < p.Length && p[i + 1] == ':')
                {
                    var end = p.IndexOf(":]", i + 2, StringComparison.Ordinal);
                    if (end > 0)
                    {
                        var name = p[(i + 2)..end];
                        if (name.StartsWith('^')) return new(false, p, 0, $"[:{name}:] is a negated class; .NET cannot negate a class inside a set. Write the negated set yourself, for example [^[:{name[1..]}:]].");
                        if (!Classes.TryGetValue(name, out var members)) return new(false, p, 0, $"[:{name}:] is not a POSIX class this converter knows.");
                        sb.Append(members); replaced++; i = end + 2; continue;
                    }
                }
                if (d == '[') { sb.Append("\\["); i++; continue; }
                sb.Append(d); i++;
            }
            if (!closed) return new(false, p, 0, "A bracket expression is not closed.");
        }
        return new(true, sb.ToString(), replaced, null);
    }
}
