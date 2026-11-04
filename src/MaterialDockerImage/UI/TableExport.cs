using System.Net;
using System.Text;
using System.Text.Json;

namespace MaterialDockerImage.UI;

/// <summary>
/// Framework-free table serialisation shared by every DataTable export and "copy as" action.
/// Cells are display text, so every format says so; escaping is verified by tests/UiState.
/// </summary>
public static class TableExport
{
    public const string SchemaName = "material-docker-image/table-export";
    public const int SchemaVersion = 1;
    public static readonly IReadOnlyList<string> Formats = ["CSV", "TSV", "JSON", "Markdown", "HTML"];

    public static string Extension(string format) => format switch { "CSV" => "csv", "TSV" => "tsv", "JSON" => "json", "HTML" => "html", _ => "md" };

    public static string Render(string format, string table, IReadOnlyList<string> headers, IReadOnlyList<string[]> rows, DateTimeOffset exported)
    {
        var names = UniqueHeaders(headers);
        return format switch
        {
            "CSV" => Delimited(names, rows, ','),
            "TSV" => Delimited(names, rows, '\t'),
            "JSON" => Json(table, names, rows, exported),
            "HTML" => Html(table, names, rows, exported),
            "Markdown" => Markdown(names, rows),
            _ => throw new ArgumentException("Unknown export format: " + format, nameof(format)),
        };
    }

    /// <summary>Equal headers would collide as JSON keys; later ones get a numeric suffix so no column is lost.</summary>
    public static IReadOnlyList<string> UniqueHeaders(IReadOnlyList<string> headers)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<string>(headers.Count);
        foreach (var header in headers)
        {
            var key = header ?? "";
            seen[key] = seen.TryGetValue(key, out var n) ? n + 1 : 1;
            result.Add(seen[key] == 1 ? key : key + " (" + seen[key] + ")");
        }
        return result;
    }

    private static string Cell(string[] row, int index) => index < row.Length ? row[index] ?? "" : "";

    private static string Quote(string value, char separator) =>
        value.IndexOfAny([separator, '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

    private static string Delimited(IReadOnlyList<string> headers, IReadOnlyList<string[]> rows, char separator)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(separator, headers.Select(h => Quote(h, separator)))).Append("\r\n");
        foreach (var row in rows) sb.Append(string.Join(separator, headers.Select((_, i) => Quote(Cell(row, i), separator)))).Append("\r\n");
        return sb.ToString();
    }

    private static string Json(string table, IReadOnlyList<string> headers, IReadOnlyList<string[]> rows, DateTimeOffset exported)
    {
        var objects = rows.Select(row => headers.Select((h, i) => (h, v: Cell(row, i))).ToDictionary(p => p.h, p => p.v)).ToList();
        return JsonSerializer.Serialize(new
        {
            schema = SchemaName,
            version = SchemaVersion,
            table,
            exported = exported.ToString("o"),
            columns = headers,
            count = objects.Count,
            note = "Cell values are the display text shown in the table.",
            rows = objects,
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>GitHub-flavoured Markdown: pipes and backslashes are escaped and line breaks become &lt;br&gt;, so a cell never breaks the table.</summary>
    public static string MarkdownCell(string value) =>
        value.Replace("\\", "\\\\").Replace("|", "\\|").Replace("\r\n", "<br>").Replace("\n", "<br>").Replace("\r", "<br>");

    private static string Markdown(IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.Append("| ").Append(string.Join(" | ", headers.Select(MarkdownCell))).Append(" |\n");
        sb.Append('|').Append(string.Concat(headers.Select(_ => " --- |"))).Append('\n');
        foreach (var row in rows) sb.Append("| ").Append(string.Join(" | ", headers.Select((_, i) => MarkdownCell(Cell(row, i))))).Append(" |\n");
        return sb.ToString();
    }

    /// <summary>Escapes text for HTML element content and attribute values alike.</summary>
    public static string HtmlEscape(string value) => WebUtility.HtmlEncode(value).Replace("'", "&#39;");

    private static string Html(string table, IReadOnlyList<string> headers, IReadOnlyList<string[]> rows, DateTimeOffset exported)
    {
        var title = HtmlEscape(table);
        var sb = new StringBuilder();
        sb.Append("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n<title>").Append(title).Append("</title>\n");
        sb.Append("<style>body{font:14px system-ui,sans-serif;margin:24px}table{border-collapse:collapse}th,td{border:1px solid #888;padding:4px 10px;text-align:left;vertical-align:top}th{background:#eee}</style>\n</head>\n<body>\n");
        sb.Append("<h1>").Append(title).Append("</h1>\n<p>Exported ").Append(HtmlEscape(exported.ToString("o"))).Append(" · ").Append(rows.Count).Append(" rows · cells are display text.</p>\n");
        sb.Append("<table>\n<thead><tr>").Append(string.Concat(headers.Select(h => "<th scope=\"col\">" + HtmlEscape(h) + "</th>"))).Append("</tr></thead>\n<tbody>\n");
        foreach (var row in rows) sb.Append("<tr>").Append(string.Concat(headers.Select((_, i) => "<td>" + HtmlEscape(Cell(row, i)).Replace("\r\n", "<br>").Replace("\n", "<br>") + "</td>"))).Append("</tr>\n");
        sb.Append("</tbody>\n</table>\n</body>\n</html>\n");
        return sb.ToString();
    }
}
