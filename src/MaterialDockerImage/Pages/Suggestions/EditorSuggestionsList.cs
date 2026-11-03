using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// The Suggestions popup: a list of matches (icon, label with matched characters in bold, detail, version badge)
/// and a documentation pane for the selected item. Nothing in it is focusable, so the editor keeps keyboard focus;
/// the editor forwards Up, Down, Page Up, Page Down, Tab, Enter and Escape.
/// </summary>
public sealed class EditorSuggestionsList
{
    private const double RowHeight = 30;
    public Popup Popup { get; }
    private readonly StackPanel rows = new();
    private readonly ScrollViewer scroller;
    private readonly StackPanel doc = new() { Width = 280 };
    private readonly Border docPane;
    private readonly TextBlock header = Ui.Mono("", 12, "OnSurfaceVariant", true);
    private readonly List<Border> rowViews = new();
    private IReadOnlyList<SuggestionMatch> matches = Array.Empty<SuggestionMatch>();
    private DockerfileSyntaxVersion? syntax;
    private bool dockerfile;
    public int SelectedIndex { get; private set; }

    public event Action<SuggestionMatch>? Accepted;
    public event Action<SuggestionMatch>? SelectionChanged;

    public bool IsOpen => Popup.IsOpen;
    public SuggestionMatch? Selected => SelectedIndex >= 0 && SelectedIndex < matches.Count ? matches[SelectedIndex] : null;

    public EditorSuggestionsList()
    {
        scroller = new ScrollViewer { Content = rows, MaxHeight = RowHeight * 10, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Width = 360 };
        header.Margin = new Thickness(10, 6, 10, 6);
        header.MaxWidth = 340;
        var listColumn = new DockPanel();
        listColumn.Children.Add(header.Dock(Dock.Top));
        listColumn.Children.Add(scroller);
        docPane = new Border { Child = new ScrollViewer { Content = doc, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, MaxHeight = RowHeight * 10 + 30 }, Padding = new Thickness(12, 10, 12, 10), BorderThickness = new Thickness(1, 0, 0, 0) };
        docPane.SetResourceReference(Border.BorderBrushProperty, "OutlineVariant");
        docPane.SetResourceReference(Border.BackgroundProperty, "SurfaceContainer");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(listColumn);
        grid.Children.Add(docPane.At(0, 1));
        var card = new Border { Child = grid, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), ClipToBounds = true, Margin = new Thickness(0, 0, 14, 14), Effect = Ui.Elevation(16, 4, .3) };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceHigh");
        card.SetResourceReference(Border.BorderBrushProperty, "OutlineVariant");
        AutomationProperties.SetName(card, Loc.T("Suggestions"));
        Popup = new Popup { Child = card, AllowsTransparency = true, StaysOpen = true, Focusable = false, Placement = PlacementMode.Relative };
    }

    public void Show(IReadOnlyList<SuggestionMatch> items, string signature, DockerfileSyntaxVersion? declared, bool isDockerfile)
    {
        var previous = Selected?.Item;
        matches = items;
        syntax = declared;
        dockerfile = isDockerfile;
        header.Text = signature;
        header.Visibility = signature.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        rows.Children.Clear();
        rowViews.Clear();
        for (var i = 0; i < items.Count; i++)
        {
            var row = BuildRow(items[i], i);
            rowViews.Add(row);
            rows.Children.Add(row);
        }
        // Keep the selection on the same item while filtering; otherwise select the first non-picker item.
        var keep = previous == null ? -1 : items.ToList().FindIndex(m => m.Item == previous);
        var first = items.ToList().FindIndex(m => m.Item.Kind != SuggestionKind.Picker);
        Select(keep >= 0 ? keep : Math.Max(0, first), false);
        Popup.IsOpen = true;
    }

    public void Close() { Popup.IsOpen = false; matches = Array.Empty<SuggestionMatch>(); SelectedIndex = -1; }

    public void Move(int delta)
    {
        if (matches.Count == 0) return;
        var next = SelectedIndex + delta;
        if (Math.Abs(delta) == 1) next = (next + matches.Count) % matches.Count;
        else next = Math.Clamp(next, 0, matches.Count - 1);
        Select(next, true);
    }

    public int PageSize => Math.Max(1, (int)(scroller.MaxHeight / RowHeight) - 1);

    private void Select(int index, bool announce)
    {
        if (matches.Count == 0) { SelectedIndex = -1; return; }
        SelectedIndex = Math.Clamp(index, 0, matches.Count - 1);
        for (var i = 0; i < rowViews.Count; i++)
        {
            if (i == SelectedIndex) rowViews[i].SetResourceReference(Border.BackgroundProperty, "SecondaryContainer");
            else rowViews[i].Background = Brushes.Transparent;
        }
        // Scroll the selected row into view without moving focus.
        var top = SelectedIndex * RowHeight;
        if (top < scroller.VerticalOffset) scroller.ScrollToVerticalOffset(top);
        else if (top + RowHeight > scroller.VerticalOffset + scroller.MaxHeight) scroller.ScrollToVerticalOffset(top + RowHeight - scroller.MaxHeight);
        RenderDoc(matches[SelectedIndex].Item);
        if (announce) SelectionChanged?.Invoke(matches[SelectedIndex]);
    }

    private Border BuildRow(SuggestionMatch match, int index)
    {
        var item = match.Item;
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        label.SetResourceReference(TextBlock.FontFamilyProperty, item.Kind == SuggestionKind.Picker ? "TextFont" : "MonoFont");
        label.SetResourceReference(TextBlock.ForegroundProperty, "OnSurface");
        label.FontSize = 13;
        var bold = new HashSet<int>(match.Positions);
        var display = item.Label.Replace("\n", " ");
        var runStart = 0;
        for (var i = 1; i <= display.Length; i++)
        {
            if (i < display.Length && bold.Contains(i) == bold.Contains(runStart)) continue;
            label.Inlines.Add(new Run(display[runStart..i]) { FontWeight = bold.Contains(runStart) ? FontWeights.Bold : FontWeights.Normal });
            runStart = i;
        }
        var detail = Ui.Text(item.Detail, 12, null, "OnSurfaceVariant", false, false);
        detail.TextTrimming = TextTrimming.CharacterEllipsis;
        detail.MaxWidth = 130;
        detail.VerticalAlignment = VerticalAlignment.Center;
        detail.Margin = new Thickness(8, 0, 0, 0);
        var grid = new Grid { Height = RowHeight };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = Ui.Icon(IconFor(item.Kind), 16, false, item.Kind == SuggestionKind.Picker ? "Primary" : "OnSurfaceVariant");
        icon.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(icon);
        grid.Children.Add(label.At(0, 1));
        grid.Children.Add(detail.At(0, 2));
        if (item.Badge.Length > 0)
        {
            var badge = Ui.Badge(item.Badge, item.NeedsSyntaxUpgrade ? Tone.Warning : Tone.Neutral);
            badge.Margin = new Thickness(6, 0, 0, 0);
            badge.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(badge.At(0, 3));
        }
        var row = new Border { Child = grid, Padding = new Thickness(8, 0, 10, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand, Focusable = false };
        AutomationProperties.SetName(row, AccessibleName(item));
        row.MouseLeftButtonUp += (_, e) => { Select(index, false); Accepted?.Invoke(matches[index]); e.Handled = true; };
        return row;
    }

    /// <summary>The text announced for an item: label, kind and detail (never a value of a secret).</summary>
    public static string AccessibleName(SuggestionItem item) =>
        item.Label + ", " + KindName(item.Kind) + (item.Detail.Length > 0 ? ", " + item.Detail : "") + (item.NeedsSyntaxUpgrade ? ", " + Loc.T("needs a newer syntax") + " " + item.MinSyntax : "");

    public static string KindName(SuggestionKind kind) => kind switch
    {
        SuggestionKind.Instruction => Loc.T("instruction"), SuggestionKind.Directive => Loc.T("parser directive"), SuggestionKind.Flag => Loc.T("flag"),
        SuggestionKind.Value => Loc.T("value"), SuggestionKind.Stage => Loc.T("stage"), SuggestionKind.Variable => Loc.T("variable"),
        SuggestionKind.Image => Loc.T("image"), SuggestionKind.Path => Loc.T("path"), SuggestionKind.Snippet => Loc.T("snippet"),
        SuggestionKind.Key => Loc.T("key"), _ => Loc.T("picker"),
    };

    public static string IconFor(SuggestionKind kind) => kind switch
    {
        SuggestionKind.Instruction => Icons.Code, SuggestionKind.Directive => Icons.Settings, SuggestionKind.Flag => Icons.Tune,
        SuggestionKind.Value => Icons.Label, SuggestionKind.Stage => Icons.Layers, SuggestionKind.Variable => Icons.DataObject,
        SuggestionKind.Image => Icons.Inventory2, SuggestionKind.Path => Icons.FolderOpen, SuggestionKind.Snippet => Icons.TextFields,
        SuggestionKind.Key => Icons.Key, _ => Icons.Edit,
    };

    private void RenderDoc(SuggestionItem item)
    {
        doc.Children.Clear();
        doc.Children.Add(Ui.Mono(item.Label.Replace("\n", " "), 13, "OnSurface", true));
        if (item.Detail.Length > 0) doc.Children.Add(Ui.Text(item.Detail, 12, null, "OnSurfaceVariant").Margin(0, 2, 0, 0));
        if (item.Doc.Length > 0) doc.Children.Add(Ui.Text(item.Doc, 13).Margin(0, 8, 0, 0));
        if (item.Example.Length > 0)
            doc.Children.Add(Ui.Card(Ui.VStack(4, Ui.Overline("Example"), Ui.Mono(item.Example, 12, "OnSurface", true)), 8, "SurfaceHigh", 8).Margin(0, 10, 0, 0));
        if (item.MinSyntax != null && dockerfile)
        {
            var text = Loc.T("Needs") + " # syntax=docker/dockerfile:" + item.MinSyntax + " " + Loc.T("or newer") + (item.LabsMinSyntax != null ? " (" + item.LabsMinSyntax + "-labs " + Loc.T("before that") + ")" : "") + ".";
            if (item.NeedsSyntaxUpgrade)
                text += " " + (syntax == null ? Loc.T("This file has no # syntax line, so the engine's built-in frontend decides.") : Loc.T("This file declares") + " " + syntax.Raw + ".")
                    + " " + Loc.T("After inserting, press Ctrl+Enter to add or upgrade the header.");
            doc.Children.Add(Ui.Text(text, 12, null, item.NeedsSyntaxUpgrade ? "Warning" : "OnSurfaceVariant").Margin(0, 10, 0, 0));
        }
        if (item.Kind == SuggestionKind.Picker) doc.Children.Add(Ui.Caption("Press Enter or Tab to open it. Escape returns to the editor.").Margin(0, 10, 0, 0));
        docPane.Visibility = Visibility.Visible;
    }
}
