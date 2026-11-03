using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Editable key/value rows (driver options, labels) with add and remove buttons and validation.</summary>
public sealed class KeyValueRows : StackPanel
{
    private readonly StackPanel rows = new();
    private readonly string keyName;
    private readonly string valueName;
    public event Action? Changed;

    public KeyValueRows(string keyName = "Key", string valueName = "Value")
    {
        this.keyName = keyName; this.valueName = valueName;
        Children.Add(rows);
        var add = Ui.Button("Add row", Icons.Add, ButtonKind.Text, () => AddRow("", ""));
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Margin = new Thickness(0, 6, 0, 0);
        Children.Add(add);
    }

    public void AddRow(string key, string value)
    {
        var k = Ui.Input(key, keyName, true);
        var v = Ui.Input(value, valueName, true);
        Grid? row = null;
        var remove = Ui.IconButton(Icons.Close, "Remove row", () => { rows.Children.Remove(row); Changed?.Invoke(); });
        row = Ui.Columns(8, [1, 1, 0], k, v, remove);
        row.Margin = new Thickness(0, rows.Children.Count == 0 ? 0 : 6, 0, 0);
        k.TextChanged += (_, _) => Changed?.Invoke();
        v.TextChanged += (_, _) => Changed?.Invoke();
        rows.Children.Add(row);
        Changed?.Invoke();
    }

    /// <summary>Returns the non-empty rows. Throws ArgumentException with a clear message on invalid keys or duplicates.</summary>
    public List<KeyValuePair<string, string>> Values()
    {
        var result = new List<KeyValuePair<string, string>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Grid row in rows.Children)
        {
            var boxes = row.Children.OfType<TextBox>().ToList();
            var key = boxes[0].Text.Trim(); var value = boxes[1].Text;
            if (key.Length == 0 && value.Length == 0) continue;
            if (key.Length == 0) throw new ArgumentException($"A {keyName.ToLowerInvariant()} is missing for value \"{value}\".");
            if (key.StartsWith('-') || key.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == '=')) throw new ArgumentException($"{keyName} \"{key}\" cannot start with a hyphen or contain spaces, control characters or '='.");
            if (value.Any(char.IsControl)) throw new ArgumentException($"The value for \"{key}\" contains control characters.");
            if (!seen.Add(key)) throw new ArgumentException($"{keyName} \"{key}\" appears twice.");
            result.Add(new(key, value));
        }
        return result;
    }

    /// <summary>Same as Values but returns null and an error message instead of throwing.</summary>
    public (List<KeyValuePair<string, string>>? Values, string? Error) TryValues()
    {
        try { return (Values(), null); } catch (ArgumentException e) { return (null, e.Message); }
    }
}
