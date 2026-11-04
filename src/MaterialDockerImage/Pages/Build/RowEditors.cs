using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Editable NAME=value rows bound to a live list. Each edit updates the list and calls <c>changed</c>; invalid names
/// are flagged inline under the row.
/// </summary>
public sealed class BuildKeyValueRows : StackPanel
{
    private readonly List<NameValue> items;
    private readonly StackPanel rows = new();
    private readonly Func<string, string?> validateName;
    private readonly Action changed;
    private readonly string nameLabel, valueLabel;

    public IReadOnlyList<NameValue> Items => items;

    public BuildKeyValueRows(List<NameValue> items, string nameLabel, string valueLabel, string addLabel, Func<string, string?> validateName, Action changed)
    {
        this.items = items; this.nameLabel = nameLabel; this.valueLabel = valueLabel; this.validateName = validateName; this.changed = changed;
        Children.Add(rows);
        var add = Ui.Button(addLabel, Icons.Add, ButtonKind.Text, () => { items.Add(new NameValue()); Rebuild(); changed(); FocusLast(); });
        add.HorizontalAlignment = HorizontalAlignment.Left; add.Margin = new Thickness(0, 4, 0, 0);
        Children.Add(add);
        Rebuild();
    }

    public void Rebuild()
    {
        rows.Children.Clear();
        for (var i = 0; i < items.Count; i++) rows.Children.Add(Row(items[i]));
    }

    private void FocusLast()
    {
        if (rows.Children.Count == 0) return;
        if (rows.Children[^1] is StackPanel { Children.Count: > 0 } row && row.Children[0] is Grid g && g.Children.Count > 0 && g.Children[0] is TextBox t)
            t.Dispatcher.BeginInvoke(() => t.Focus(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private FrameworkElement Row(NameValue item)
    {
        var name = Ui.Input(item.Name, nameLabel, true);
        var value = Ui.Input(item.Value, valueLabel, true);
        var error = Ui.Text("", 12, null, "Error");
        error.Visibility = Visibility.Collapsed;
        void Validate()
        {
            var problem = item.Name.Length == 0 && item.Value.Length == 0 ? null : validateName(item.Name);
            error.Text = problem ?? ""; error.Visibility = problem == null ? Visibility.Collapsed : Visibility.Visible;
        }
        name.TextChanged += (_, _) => { item.Name = name.Text; Validate(); changed(); };
        value.TextChanged += (_, _) => { item.Value = value.Text; changed(); };
        var remove = Ui.IconButton(Icons.Delete, Loc.T("Remove") + " " + Loc.T(nameLabel).ToLowerInvariant(), () => { items.Remove(item); Rebuild(); changed(); });
        var equals = Ui.Mono("=", 14, "OnSurfaceVariant");
        equals.Margin = new Thickness(6, 0, 6, 0);
        var grid = Ui.Columns(0, [2, 0, 3, 0], name, equals, value, remove);
        remove.Margin = new Thickness(4, 0, 0, 0);
        Validate();
        var row = Ui.VStack(2, grid, error);
        row.Margin = new Thickness(0, 0, 0, 6);
        return row;
    }

    /// <summary>Non-empty rows in order, unvalidated (the caller validates, for example through BuildKitCommand).</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Pairs() =>
        items.Where(i => i.Name.Length > 0 || i.Value.Length > 0).Select(i => new KeyValuePair<string, string>(i.Name.Trim(), i.Value)).ToList();

    /// <summary>Replaces every row (used when a run or plan pre-fills values).</summary>
    public void SetPairs(IEnumerable<KeyValuePair<string, string>> pairs)
    {
        items.Clear();
        foreach (var (name, value) in pairs) items.Add(new NameValue { Name = name, Value = value });
        Rebuild();
        changed();
    }

    /// <summary>Non-empty rows as a dictionary; throws ArgumentException naming the first problem.</summary>
    public Dictionary<string, string> ToDictionary(string what)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item.Name.Length == 0 && item.Value.Length == 0) continue;
            var problem = validateName(item.Name);
            if (problem != null) throw new ArgumentException($"{what}: {problem}");
            StructuredInputs.Token(item.Value);
            if (!result.TryAdd(item.Name, item.Value)) throw new ArgumentException($"{what}: {item.Name} is listed twice.");
        }
        return result;
    }
}

/// <summary>Editable single-value rows bound to a live list of strings (paths, arguments, ids).</summary>
public sealed class ListRows : StackPanel
{
    private readonly List<string> items;
    private readonly StackPanel rows = new();
    private readonly string itemLabel;
    private readonly Action changed;
    private readonly bool mono;

    public ListRows(List<string> items, string itemLabel, string addLabel, Action changed, bool mono = true)
    {
        this.items = items; this.itemLabel = itemLabel; this.changed = changed; this.mono = mono;
        Children.Add(rows);
        var add = Ui.Button(addLabel, Icons.Add, ButtonKind.Text, () =>
        {
            items.Add(""); Rebuild(); changed();
            if (rows.Children.Count > 0 && rows.Children[^1] is Grid g && g.Children[0] is TextBox t) t.Dispatcher.BeginInvoke(() => t.Focus(), System.Windows.Threading.DispatcherPriority.Loaded);
        });
        add.HorizontalAlignment = HorizontalAlignment.Left; add.Margin = new Thickness(0, 2, 0, 0);
        Children.Add(add);
        Rebuild();
    }

    private void Rebuild()
    {
        rows.Children.Clear();
        for (var i = 0; i < items.Count; i++)
        {
            var index = i;
            var box = Ui.Input(items[i], $"{Loc.T(itemLabel)} {i + 1}", mono);
            box.TextChanged += (_, _) => { if (index < items.Count) { items[index] = box.Text; changed(); } };
            var up = Ui.IconButton(Icons.ExpandLess, "Move up", () => Move(index, -1));
            up.IsEnabled = index > 0;
            var remove = Ui.IconButton(Icons.Delete, Loc.T("Remove") + " " + Loc.T(itemLabel).ToLowerInvariant(), () => { items.RemoveAt(index); Rebuild(); changed(); });
            var grid = Ui.Columns(4, [1, 0, 0], box, up, remove);
            grid.Margin = new Thickness(0, 0, 0, 6);
            rows.Children.Add(grid);
        }
    }

    private void Move(int index, int delta)
    {
        var target = index + delta;
        if (target < 0 || target >= items.Count) return;
        (items[index], items[target]) = (items[target], items[index]);
        Rebuild(); changed();
    }
}
