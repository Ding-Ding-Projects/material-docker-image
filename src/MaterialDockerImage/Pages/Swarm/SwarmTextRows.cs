using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Editable single-value rows (published ports, placement constraints) with add and remove buttons.</summary>
public sealed class SwarmTextRows : StackPanel
{
    private readonly StackPanel rows = new();
    private readonly string itemName;
    public event Action? Changed;

    public SwarmTextRows(string itemName, string addLabel)
    {
        this.itemName = itemName;
        Children.Add(rows);
        var add = Ui.Button(addLabel, Icons.Add, ButtonKind.Text, () => AddRow(""));
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Margin = new Thickness(0, 6, 0, 0);
        Children.Add(add);
    }

    public void AddRow(string value)
    {
        var box = Ui.Input(value, itemName, true);
        Grid? row = null;
        var remove = Ui.IconButton(Icons.Close, "Remove " + itemName.ToLowerInvariant(), () => { rows.Children.Remove(row); Changed?.Invoke(); });
        row = Ui.Columns(8, [1, 0], box, remove);
        row.Margin = new Thickness(0, rows.Children.Count == 0 ? 0 : 6, 0, 0);
        box.TextChanged += (_, _) => Changed?.Invoke();
        rows.Children.Add(row);
        Changed?.Invoke();
    }

    public IReadOnlyList<string> Values => rows.Children.OfType<Grid>().Select(g => g.Children.OfType<TextBox>().First().Text.Trim()).Where(v => v.Length > 0).ToList();
}
