namespace MaterialDockerImage.UI;

/// <summary>
/// Framework-free rules for DataTable two-line cells (a primary line with a secondary caption under it): what a screen
/// reader hears, what search matches and how exports split the two lines into columns. Verified by tests/UiState.
/// </summary>
public static class TableCells
{
    /// <summary>The accessible name and tooltip of a cell: both lines, the secondary after a comma, empty lines left out.</summary>
    public static string Label(string? primary, string? secondary)
    {
        primary ??= ""; secondary ??= "";
        if (secondary.Length == 0) return primary;
        return primary.Length == 0 ? secondary : primary + ", " + secondary;
    }

    /// <summary>Search fields in reading order: each cell, followed by its secondary line when it has one.</summary>
    public static string[] SearchFields(IReadOnlyList<string> cells, IReadOnlyList<string> secondaries)
    {
        var fields = new List<string>(cells.Count + secondaries.Count);
        for (var i = 0; i < cells.Count; i++)
        {
            fields.Add(cells[i] ?? "");
            if (i < secondaries.Count && !string.IsNullOrEmpty(secondaries[i])) fields.Add(secondaries[i]);
        }
        return fields.ToArray();
    }

    /// <summary>The header for the exported secondary column when the table does not name one.</summary>
    public static string DefaultSecondaryHeader(string header) => header + " detail";

    /// <summary>Export headers: a two-line column becomes two columns, so a spreadsheet keeps the lines apart.</summary>
    public static List<string> ExportHeaders(IReadOnlyList<(string Header, string? SecondaryHeader)> columns)
    {
        var headers = new List<string>();
        foreach (var (header, secondary) in columns)
        {
            headers.Add(header);
            if (secondary != null) headers.Add(secondary);
        }
        return headers;
    }

    /// <summary>One export row matching <see cref="ExportHeaders"/>: a two-line column contributes its primary then its secondary.</summary>
    public static string[] ExportRow(IReadOnlyList<string> cells, IReadOnlyList<string> secondaries, IReadOnlyList<bool> twoLine)
    {
        var row = new List<string>();
        for (var i = 0; i < cells.Count; i++)
        {
            row.Add(cells[i] ?? "");
            if (i < twoLine.Count && twoLine[i]) row.Add(i < secondaries.Count ? secondaries[i] ?? "" : "");
        }
        return row.ToArray();
    }
}
