using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Small typed controls used by the Minecraft forms: validated numbers, list rows and section cards.</summary>
internal static class MinecraftControls
{
    /// <summary>A whole-number field with bounds; invalid text shows an inline message and leaves the model unchanged.</summary>
    public static FrameworkElement Number(string label, int value, int min, int max, Action<int> set, string info, string? example = null, string unit = "")
    {
        var box = Ui.Input(value.ToString(CultureInfo.InvariantCulture), label);
        box.Width = 120; box.HorizontalAlignment = HorizontalAlignment.Left;
        var error = Ui.Text("", 12, null, "Error", true, false);
        var hint = Ui.Caption($"{min}–{max}{(unit.Length > 0 ? " " + unit : "")}");
        box.TextChanged += (_, _) =>
        {
            if (int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v >= min && v <= max) { error.Text = ""; set(v); }
            else error.Text = $"Enter a whole number from {min} to {max}.";
        };
        var row = Ui.HStack(10, box, hint);
        return Ui.VStack(4, Ui.Field(label, row, info, example), error);
    }

    /// <summary>A one-line text field bound to a setter.</summary>
    public static FrameworkElement Text(string label, string value, Action<string> set, string info, string? example = null, bool mono = false)
    {
        var box = Ui.Input(value, label, mono);
        box.TextChanged += (_, _) => set(box.Text);
        return Ui.Field(label, box, info, example);
    }

    /// <summary>Titled tonal group used to split long forms.</summary>
    public static Border Section(string title, string? subtitle, params UIElement?[] children)
    {
        var stack = Ui.VStack(14, Ui.VStack(2, Ui.Text(title, 16, FontWeights.SemiBold), subtitle == null ? null : Ui.Caption(subtitle)));
        foreach (var c in children) Ui.Add(stack, c, 14);
        return Ui.Card(stack, 18, "SurfaceLow", 16);
    }

    /// <summary>Two-column grid of fields that falls back to one column below 760 pixels.</summary>
    public static FrameworkElement Grid2(params FrameworkElement[] fields)
    {
        var wrap = new UniformGridLike(fields);
        return wrap;
    }

    private sealed class UniformGridLike : Grid
    {
        private readonly FrameworkElement[] items;
        private int columns = -1;
        public UniformGridLike(FrameworkElement[] items)
        {
            this.items = items;
            foreach (var i in items) Children.Add(i);
            SizeChanged += (_, _) => Arrange();
            Arrange();
        }
        private void Arrange()
        {
            var now = ActualWidth <= 0 || ActualWidth >= 760 ? 2 : 1;
            if (now == columns) return;
            columns = now;
            ColumnDefinitions.Clear(); RowDefinitions.Clear();
            for (var c = 0; c < now; c++)
            {
                if (c > 0) ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
                ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }
            var rows = (items.Length + now - 1) / now;
            for (var r = 0; r < rows; r++) RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var i = 0; i < items.Length; i++)
            {
                Grid.SetRow(items[i], i / now); Grid.SetColumn(items[i], (i % now) * 2);
                items[i].Margin = new Thickness(0, i / now == 0 ? 0 : 14, 0, 0);
            }
        }
    }
}

/// <summary>
/// Editable list of single values (player names, project slugs, resource ids) with per-row validation, add and remove.
/// The bound list is replaced with the valid, trimmed, de-duplicated values on every change.
/// </summary>
internal sealed class MinecraftListRows : StackPanel
{
    private readonly StackPanel rows = new();
    private readonly Func<string, string?> validate;
    private readonly Action<List<string>> changed;
    private readonly string itemName;
    private readonly TextBlock error = Ui.Text("", 12, null, "Error", true, false);

    public MinecraftListRows(IEnumerable<string> values, string itemName, string addLabel, Func<string, string?> validate, Action<List<string>> changed)
    {
        this.validate = validate; this.changed = changed; this.itemName = itemName;
        Children.Add(rows);
        var add = Ui.Button(addLabel, Icons.Add, ButtonKind.Text, () => AddRow("", true));
        add.HorizontalAlignment = HorizontalAlignment.Left;
        Children.Add(add.Margin(0, 6, 0, 0));
        Children.Add(error);
        foreach (var v in values) AddRow(v, false);
        if (rows.Children.Count == 0) Children.Insert(0, Empty());
    }

    private TextBlock? emptyText;
    private TextBlock Empty() => emptyText = Ui.Caption("None yet.");

    private void AddRow(string value, bool focus)
    {
        if (emptyText != null) { Children.Remove(emptyText); emptyText = null; }
        var box = Ui.Input(value, itemName, true);
        var remove = Ui.IconButton(Icons.Close, "Remove " + itemName);
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        row.Children.Add(remove.Dock(Dock.Right));
        box.Margin = new Thickness(0, 0, 8, 0);
        row.Children.Add(box);
        remove.Click += (_, _) => { rows.Children.Remove(row); Update(); };
        box.TextChanged += (_, _) => Update();
        rows.Children.Add(row);
        if (focus) box.Loaded += (_, _) => box.Focus();
        Update();
    }

    private void Update()
    {
        var values = new List<string>();
        var problems = new List<string>();
        foreach (var box in rows.Children.OfType<DockPanel>().Select(r => r.Children.OfType<TextBox>().First()))
        {
            var v = box.Text.Trim();
            if (v.Length == 0) continue;
            if (validate(v) is { } problem) { problems.Add($"\"{v}\": {problem}"); continue; }
            if (!values.Contains(v, StringComparer.OrdinalIgnoreCase)) values.Add(v);
        }
        error.Text = string.Join("\n", problems);
        changed(values);
    }
}
