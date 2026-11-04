using System.Text;
using System.Text.Json;

namespace MaterialDockerImage.UI;

/// <summary>How much of the explanation tree is shown or exported.</summary>
public enum RegexExplainPrecision { Overview = 1, Standard = 3, Full = int.MaxValue }

/// <summary>Exports the explanation tree as indented text, Markdown or JSON, at a chosen precision, and finds the token under a position.</summary>
public static class RegexTreeExport
{
    public const int MaxNodes = 2000;
    public static readonly IReadOnlyList<string> Formats = ["Text", "Markdown", "JSON"];

    public static IEnumerable<(RegexExplainNode Node, int Depth)> Visible(string pattern, RegexBuilderFlags flags, RegexExplainPrecision precision)
    {
        var limit = (int)precision;
        var count = 0;
        foreach (var item in RegexExplainer.Flatten(RegexExplainer.Explain(pattern, flags)))
        {
            if (item.Depth >= limit) continue;
            if (++count > MaxNodes) yield break;
            yield return item;
        }
    }

    /// <summary>One line per token: indentation shows nesting, then the token, then its description.</summary>
    public static string ToText(string pattern, RegexBuilderFlags flags, RegexExplainPrecision precision = RegexExplainPrecision.Full)
    {
        var sb = new StringBuilder();
        sb.Append("Pattern: ").AppendLine(pattern).Append("Options: ").AppendLine(flags.Describe());
        foreach (var (n, depth) in Visible(pattern, flags, precision))
            sb.Append(' ', depth * 2).Append(n.Text.Length > 0 ? OneLine(n.Text) + "  " : "").Append(": ").AppendLine(n.Description);
        return sb.ToString();
    }

    /// <summary>A nested Markdown list with each token in a code span that cannot be broken by backticks inside the token.</summary>
    public static string ToMarkdown(string pattern, RegexBuilderFlags flags, RegexExplainPrecision precision = RegexExplainPrecision.Full)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Regular expression explanation").AppendLine();
        sb.Append("- Pattern: ").AppendLine(CodeSpan(pattern));
        sb.Append("- Options: ").AppendLine(flags.Describe());
        sb.AppendLine().AppendLine("## Tokens").AppendLine();
        foreach (var (n, depth) in Visible(pattern, flags, precision))
        {
            sb.Append(' ', depth * 2).Append("- ");
            if (n.Text.Length > 0) sb.Append(CodeSpan(OneLine(n.Text))).Append(": ");
            sb.AppendLine(EscapeMarkdown(n.Description));
        }
        return sb.ToString();
    }

    /// <summary>Nested JSON with family, position, length, token text and description.</summary>
    public static string ToJson(string pattern, RegexBuilderFlags flags, RegexExplainPrecision precision = RegexExplainPrecision.Full)
    {
        var compiled = RegexBuilderEngine.Compile(pattern, flags);
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("format", "material-docker-image.regex-explanation");
            w.WriteNumber("version", 1);
            w.WriteString("dialect", ".NET regular expressions");
            w.WriteString("pattern", pattern);
            w.WriteString("options", flags.Describe());
            w.WriteBoolean("valid", compiled.Valid);
            if (!compiled.Valid) w.WriteString("error", compiled.Error);
            w.WriteString("precision", precision.ToString());
            w.WriteStartArray("tokens");
            var budget = MaxNodes;
            foreach (var n in RegexExplainer.Explain(pattern, flags)) Node(w, n, 1, (int)precision, ref budget);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Node(Utf8JsonWriter w, RegexExplainNode n, int depth, int limit, ref int budget)
    {
        if (depth > limit || budget <= 0) return;
        budget--;
        w.WriteStartObject();
        w.WriteString("family", n.Family.ToString());
        w.WriteNumber("start", n.Start);
        w.WriteNumber("length", n.Length);
        w.WriteString("text", n.Text);
        w.WriteString("description", n.Description);
        w.WriteStartArray("children");
        foreach (var c in n.Children) Node(w, c, depth + 1, limit, ref budget);
        w.WriteEndArray();
        w.WriteEndObject();
    }

    public static string Export(string format, string pattern, RegexBuilderFlags flags, RegexExplainPrecision precision) => format switch
    {
        "Text" => ToText(pattern, flags, precision),
        "Markdown" => ToMarkdown(pattern, flags, precision),
        "JSON" => ToJson(pattern, flags, precision),
        _ => throw new ArgumentException("Unknown export format " + format, nameof(format)),
    };

    private static string OneLine(string s) => s.Replace("\r", "\\r").Replace("\n", "\\n");

    private static string CodeSpan(string s)
    {
        s = OneLine(s);
        var longest = 0; var run = 0;
        foreach (var c in s) { run = c == '`' ? run + 1 : 0; longest = Math.Max(longest, run); }
        var fence = new string('`', longest + 1);
        return fence + (s.StartsWith('`') || s.EndsWith('`') ? " " + s + " " : s) + fence;
    }

    private static string EscapeMarkdown(string s) => s.Replace("\\", "\\\\").Replace("*", "\\*").Replace("_", "\\_").Replace("<", "&lt;").Replace("[", "\\[");

    /// <summary>
    /// The deepest token whose span holds <paramref name="position"/>. A caret at the end of a token belongs to that token only when
    /// no later token starts at the same place. Null when the position is outside every token.
    /// </summary>
    public static RegexExplainNode? NodeAt(IEnumerable<RegexExplainNode> nodes, int position)
    {
        RegexExplainNode? atEnd = null;
        foreach (var n in nodes)
        {
            if (position < n.Start) break;
            if (position < n.Start + n.Length) return NodeAt(n.Children, position) ?? n;
            if (position == n.Start + n.Length && n.Length > 0) atEnd = NodeAt(n.Children, position) ?? n;
        }
        return atEnd;
    }
}
