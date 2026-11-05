using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Layers tab (design/workspace-redesign-2026-11/project/ImagesV2.dc.html): docker image history as a table with the
/// step number, the cleaned instruction in mono with a short note, a bar showing the layer's share relative to the
/// largest layer, and the size. Has its own search, a sort order and an option to hide metadata-only (empty) layers.
/// On narrow hosts such as the side inspector the bar moves under the instruction.
/// </summary>
internal sealed class ImageLayersTab : ImageDetailTab
{
    private enum Order { OldestFirst, NewestFirst, LargestFirst }

    private readonly DockPanel view = new() { LastChildFill = true };
    private readonly StackPanel rows = new();
    private readonly TextBlock heading = Ui.Text("", 15, FontWeights.SemiBold, "OnSurface", false, false);
    private readonly TextBlock caption = Ui.Text("", 12, null, "OnSurfaceVariant", false, false);
    private readonly SearchFilter search = new("Search layers", 200);
    private readonly Grid columnHeader;
    private readonly IReadOnlyList<ImageLayerEntry> layers;
    private readonly string problem = "";
    private readonly MenuItem hideItem;
    private readonly List<MenuItem> orderItems = [];
    private bool hideEmpty;
    private Order order = Order.OldestFirst;
    private double barWidth = 220;
    private Border? selected;

    public ImageLayersTab(ImageDetailContext context) : base(context)
    {
        var history = context.History;
        if (!history.Success) { layers = []; problem = Loc.T("History could not be read:") + " " + (history.StandardError.Trim() is { Length: > 0 } e ? e : $"docker exited with code {history.ExitCode}."); }
        else
        {
            try { layers = ImageLayerHistory.Parse(history.StandardOutput); }
            catch (JsonException e) { layers = []; problem = Loc.T("History output could not be parsed:") + " " + e.Message; }
        }

        heading.TextTrimming = TextTrimming.CharacterEllipsis;
        caption.TextTrimming = TextTrimming.CharacterEllipsis;
        caption.VerticalAlignment = VerticalAlignment.Center;
        var largest = layers.Count == 0 ? 0 : layers.Max(l => l.Bytes);
        caption.Text = largest > 0 ? Loc.T("Bars relative to the largest layer") + " (" + Ui.FormatBytes(largest) + ")" : "";

        var sort = Ui.IconButton(Icons.Sort, "Sort and filter layers");
        var menu = new ContextMenu { PlacementTarget = sort, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var (label, value) in new[] { ("Oldest first", Order.OldestFirst), ("Newest first", Order.NewestFirst), ("Largest first", Order.LargestFirst) })
        {
            var item = new MenuItem { Header = Loc.T(label), IsCheckable = true, IsChecked = value == order, Tag = value };
            item.Click += (_, _) => { order = value; foreach (var o in orderItems) o.IsChecked = (Order)o.Tag == order; Render(); };
            orderItems.Add(item);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        hideItem = new MenuItem { Header = Loc.T("Hide empty layers"), IsCheckable = true, ToolTip = Loc.T("Steps such as CMD, ENV, LABEL and EXPOSE only change image metadata and add 0 B. Hiding them leaves the steps that add files.") };
        hideItem.Click += (_, _) => { hideEmpty = hideItem.IsChecked; Render(); };
        menu.Items.Add(hideItem);
        sort.ContextMenu = menu;
        sort.Click += (_, _) => menu.IsOpen = true;

        search.VerticalAlignment = VerticalAlignment.Center;
        search.Changed += Render;
        var head = Ui.Columns(10, [1, 0, 0, 0], heading, caption, search, sort);
        head.Margin = new Thickness(4, 0, 0, 8);
        head.MinHeight = 44;

        columnHeader = new Grid { Margin = new Thickness(0, 0, 0, 2), Height = 36 };
        System.Windows.Automation.AutomationProperties.SetName(columnHeader, Loc.T("Layers"));

        view.Children.Add(head.Dock(System.Windows.Controls.Dock.Top));
        view.Children.Add(Ui.Divider().Dock(System.Windows.Controls.Dock.Top));
        view.Children.Add(columnHeader.Dock(System.Windows.Controls.Dock.Top));
        view.Children.Add(Ui.Scroll(rows));
        // The table adapts to its host: the full share column on wide hosts, a narrower one below 760 px and the
        // bar under the instruction below 520 px; the caption gives way to the search on small widths.
        view.SizeChanged += (_, e) =>
        {
            var width = e.NewSize.Width;
            caption.Visibility = width >= 760 ? Visibility.Visible : Visibility.Collapsed;
            var wanted = width >= 760 ? 220 : width >= 520 ? 120 : 0;
            if (wanted != barWidth) { barWidth = wanted; Render(); }
        };
        Render();
    }

    public override string Title => "Layers";
    public override FrameworkElement View => view;

    /// <summary>Number of history entries, shown as the tab's count.</summary>
    public int Count => layers.Count;

    private void Render()
    {
        rows.Children.Clear();
        selected = null;
        heading.Text = Loc.T(order switch { Order.NewestFirst => "Layers, newest first", Order.LargestFirst => "Layers, largest first", _ => "Layers, oldest first" });
        BuildColumnHeader();
        if (problem.Length > 0) { rows.Children.Add(Ui.Text(problem, 13, null, "Error", true, false).Margin(8)); return; }
        if (layers.Count == 0) { rows.Children.Add(Ui.EmptyState(Icons.Layers, "No history entries", "docker image history returned no steps for this image.")); return; }
        var largest = Math.Max(1, layers.Max(l => l.Bytes));
        IEnumerable<ImageLayerEntry> ordered = order switch
        {
            Order.NewestFirst => layers.OrderBy(l => l.Index),
            Order.LargestFirst => layers.OrderByDescending(l => l.Bytes).ThenByDescending(l => l.Index),
            _ => layers.OrderByDescending(l => l.Index)
        };
        var shown = 0;
        foreach (var layer in ordered)
        {
            if (hideEmpty && layer.Empty) continue;
            if (search.IsActive && !search.Matches(layer.Keyword + " " + layer.Instruction + " " + layer.SizeText + " " + layer.CreatedSince + " " + layer.Comment)) continue;
            rows.Children.Add(Row(layer, layers.Count - layer.Index, largest));
            shown++;
        }
        if (shown == 0) rows.Children.Add(Ui.EmptyState(Icons.FilterList, "No matches", search.Status.Length > 0 ? search.Status : "No layers match the search and filter."));
        var filtered = search.IsActive || hideEmpty;
        if (filtered) caption.Text = search.Status.Length > 0 ? search.Status : Loc.Line($"{shown} of {layers.Count} shown");
        else if (largest > 1) caption.Text = Loc.T("Bars relative to the largest layer") + " (" + Ui.FormatBytes(largest) + ")";
        if (filtered) caption.Visibility = Visibility.Visible;
    }

    private Grid Columns()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(barWidth > 0 ? barWidth + 12 : 0) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        return grid;
    }

    private void BuildColumnHeader()
    {
        columnHeader.Children.Clear();
        columnHeader.ColumnDefinitions.Clear();
        var template = Columns();
        foreach (var c in template.ColumnDefinitions.ToList()) { template.ColumnDefinitions.Remove(c); columnHeader.ColumnDefinitions.Add(c); }
        TextBlock H(string text) => Ui.Text(text, 12, FontWeights.SemiBold, "OnSurfaceVariant", false);
        var number = H("#"); number.VerticalAlignment = VerticalAlignment.Center;
        var created = H("Created by"); created.VerticalAlignment = VerticalAlignment.Center; created.Margin = new Thickness(12, 0, 12, 0);
        var share = H("Share of image"); share.VerticalAlignment = VerticalAlignment.Center;
        share.Visibility = barWidth > 0 ? Visibility.Visible : Visibility.Collapsed;
        var size = H("Size"); size.VerticalAlignment = VerticalAlignment.Center; size.HorizontalAlignment = HorizontalAlignment.Right;
        columnHeader.Margin = new Thickness(8, 0, 8, 2);
        columnHeader.Children.Add(number.At(0, 0));
        columnHeader.Children.Add(created.At(0, 1));
        columnHeader.Children.Add(share.At(0, 2));
        columnHeader.Children.Add(size.At(0, 3));
    }

    private Border Row(ImageLayerEntry layer, int step, long largest)
    {
        var grid = Columns();
        var number = Ui.Text(step.ToString(CultureInfo.CurrentCulture), 13, null, "OnSurfaceVariant", false, false);
        number.VerticalAlignment = VerticalAlignment.Center;
        var command = (layer.Keyword + " " + layer.Instruction).Trim();
        var instruction = Ui.Mono(command.Length > 0 ? command : Loc.T("(no instruction recorded)"), 12, "OnSurface");
        instruction.TextTrimming = TextTrimming.CharacterEllipsis;
        instruction.ToolTip = layer.CreatedBy;
        var note = Ui.Text(Note(layer), 12, null, "OnSurfaceVariant", false, false);
        note.TextTrimming = TextTrimming.CharacterEllipsis;
        var bar = SizeBar(layer.Bytes, largest, layer.Bytes == largest && largest > 1);
        var middle = Ui.VStack(2, instruction, note);
        middle.VerticalAlignment = VerticalAlignment.Center;
        middle.Margin = new Thickness(12, 0, 12, 0);
        if (barWidth <= 0) { bar.Margin = new Thickness(0, 4, 0, 0); middle.Children.Add(bar); }
        else { bar.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(bar.At(0, 2)); bar.Margin = new Thickness(0, 0, 12, 0); }
        var size = Ui.Text(layer.SizeText.Length > 0 ? layer.SizeText : "—", 13, null, layer.Empty ? "OnSurfaceVariant" : "OnSurface", false, false);
        size.HorizontalAlignment = HorizontalAlignment.Right;
        size.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(number.At(0, 0));
        grid.Children.Add(middle.At(0, 1));
        grid.Children.Add(size.At(0, 3));

        var row = new Border { Child = grid, CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 6, 8, 6), MinHeight = 52, Focusable = true, Background = System.Windows.Media.Brushes.Transparent, FocusVisualStyle = null };
        System.Windows.Automation.AutomationProperties.SetName(row, $"{Loc.T("Step")} {step}: {command}, {layer.SizeText}");
        void Paint()
        {
            if (row == selected) row.SetResourceReference(Border.BackgroundProperty, "SurfaceHigh");
            else if (row.IsMouseOver || row.IsKeyboardFocused) row.SetResourceReference(Border.BackgroundProperty, "HoverLayer");
            else row.Background = System.Windows.Media.Brushes.Transparent;
        }
        void Select()
        {
            var previous = selected;
            selected = row;
            if (previous != null && previous != row) (previous.Tag as Action)?.Invoke();
            Paint();
        }
        row.Tag = (Action)Paint;
        row.MouseEnter += (_, _) => Paint();
        row.MouseLeave += (_, _) => Paint();
        row.GotKeyboardFocus += (_, _) => Paint();
        row.LostKeyboardFocus += (_, _) => Paint();
        row.MouseLeftButtonDown += (_, _) => { row.Focus(); Select(); };
        row.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space) { Select(); e.Handled = true; }
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { PageKit.Copy(command, "the instruction"); e.Handled = true; }
            else if (e.Key is Key.Down or Key.Up)
            {
                var index = rows.Children.IndexOf(row) + (e.Key == Key.Down ? 1 : -1);
                if (index >= 0 && index < rows.Children.Count && rows.Children[index] is Border next) { next.Focus(); next.BringIntoView(); }
                e.Handled = true;
            }
        };
        row.ContextMenu = new ContextMenu();
        row.ContextMenu.Items.Add(Ui.MenuItem("Copy instruction", Icons.ContentCopy, () => PageKit.Copy(command, "the instruction"), "Ctrl+C"));
        row.ContextMenu.Items.Add(Ui.MenuItem("Copy recorded command", Icons.Terminal, () => PageKit.Copy(layer.CreatedBy, "the recorded command")));
        return row;
    }

    /// <summary>The second line under the instruction: what the history records about the step, nothing inferred.</summary>
    private static string Note(ImageLayerEntry layer)
    {
        var parts = new List<string>();
        if (layer.Empty) parts.Add(Loc.T("Metadata only"));
        if (layer.CreatedSince.Length > 0) parts.Add(layer.CreatedSince);
        if (layer.Comment.Length > 0) parts.Add(layer.Comment);
        return parts.Count > 0 ? string.Join(" · ", parts) : "—";
    }

    /// <summary>A rounded track whose filled part is the layer size relative to the largest layer; the largest is drawn in the primary colour.</summary>
    private static FrameworkElement SizeBar(long bytes, long largest, bool isLargest)
    {
        var ratio = Math.Clamp(bytes / (double)largest, 0, 1);
        var grid = new Grid { Height = 10 };
        var track = new Border { CornerRadius = new CornerRadius(5) };
        track.SetResourceReference(Border.BackgroundProperty, "SurfaceHighest");
        grid.Children.Add(track.At(0, 0, 1, 2));
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ratio, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - ratio, GridUnitType.Star) });
        if (bytes > 0)
        {
            var fill = new Border { CornerRadius = new CornerRadius(5), MinWidth = 3 };
            fill.SetResourceReference(Border.BackgroundProperty, isLargest ? "Primary" : "Outline");
            grid.Children.Add(fill.At(0, 0));
        }
        return grid;
    }
}
