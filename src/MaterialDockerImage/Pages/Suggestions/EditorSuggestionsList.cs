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
/// The Suggestions popup: a signature note with the required parts in bold, then a two-pane card with the matches
/// (icon, label with matched characters in bold, a short tag) and the documentation of the selected item. Nothing in it
/// is focusable, so the editor keeps keyboard focus; the editor forwards Up, Down, Page Up, Page Down, Tab, Enter and Escape.
/// </summary>
public sealed class EditorSuggestionsList
{
    private const double RowHeight = 34;
    private const double CardHeight = 260;
    private const double ListPadding = 6;
    public Popup Popup { get; }
    private readonly StackPanel rows = new() { Margin = new Thickness(0, ListPadding, 0, ListPadding) };
    private readonly ScrollViewer scroller;
    private readonly StackPanel doc = new();
    private readonly Border docPane;
    private readonly TextBlock header = Ui.Mono("", 12, "OnSurfaceVariant", false);
    private readonly Border signature;
    private readonly List<Border> rowViews = new();
    private readonly List<(TextBlock Icon, TextBlock Label)> rowInk = new();
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
        // Two panes of fixed size, as on the design board: the matches (340) and the documentation of the selected one (420).
        scroller = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        var listColumn = new Border { Child = scroller, Width = 340, BorderThickness = new Thickness(0, 0, 1, 0) };
        listColumn.SetResourceReference(Border.BorderBrushProperty, "SurfaceHighest");
        docPane = new Border { Child = new ScrollViewer { Content = doc, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false }, Padding = new Thickness(16, 14, 16, 14), Width = 420 };
        var grid = new Grid { Height = CardHeight };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(listColumn);
        grid.Children.Add(docPane.At(0, 1));
        var card = new Border { Child = grid, CornerRadius = new CornerRadius(12), ClipToBounds = true, Effect = Ui.Elevation(16, 4, .3) };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceHigh");
        AutomationProperties.SetName(card, Loc.T("Suggestions"));
        // The signature of the instruction or option sits above the list as its own note.
        signature = SignatureNote(header);
        signature.HorizontalAlignment = HorizontalAlignment.Left;
        signature.Margin = new Thickness(0, 0, 0, 8);
        signature.MaxWidth = 760;
        var stack = new StackPanel { Margin = new Thickness(0, 0, 14, 14) };
        // A popup inherits from the editor it is anchored to, which uses the monospace font; prose here uses the body font
        // and the code parts (labels, signature, example) set the monospace font themselves.
        stack.SetResourceReference(TextElement.FontFamilyProperty, "TextFont");
        stack.Children.Add(signature);
        stack.Children.Add(card);
        Popup = new Popup { Child = stack, AllowsTransparency = true, StaysOpen = true, Focusable = false, Placement = PlacementMode.Relative };
    }

    /// <summary>The 30 px signature note: surface-highest, radius 8, monospace 12 (also used by the editor's hint popup).</summary>
    public static Border SignatureNote(UIElement content)
    {
        var note = new Border { Child = content, MinHeight = 30, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 4, 12, 4), Effect = Ui.Elevation(12, 4, .4) };
        note.SetResourceReference(Border.BackgroundProperty, "SurfaceHighest");
        if (content is FrameworkElement element) element.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(note, Loc.T("Suggestion hint"));
        return note;
    }

    /// <summary>Fills a text block with a signature; required placeholders (outside square brackets) are bold.</summary>
    public static void FillSignature(TextBlock target, string text)
    {
        target.Inlines.Clear();
        foreach (var part in SuggestionPresentation.Signature(text))
        {
            var run = new Run(part.Text);
            if (part.Required) { run.FontWeight = FontWeights.Bold; run.SetResourceReference(TextElement.ForegroundProperty, "OnSurface"); }
            target.Inlines.Add(run);
        }
    }

    public void Show(IReadOnlyList<SuggestionMatch> items, string signatureText, DockerfileSyntaxVersion? declared, bool isDockerfile)
    {
        var previous = Selected?.Item;
        matches = items;
        syntax = declared;
        dockerfile = isDockerfile;
        FillSignature(header, signatureText);
        signature.Visibility = signatureText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        rows.Children.Clear();
        rowViews.Clear();
        rowInk.Clear();
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

    /// <summary>Selects the row whose label is <paramref name="label"/> (used by capture mode); false when it is not listed.</summary>
    internal bool SelectLabel(string label)
    {
        var index = matches.ToList().FindIndex(m => m.Item.Label == label);
        if (index < 0) return false;
        Select(index, false);
        return true;
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

    private static double VisibleHeight => CardHeight - 2 * ListPadding;
    public int PageSize => Math.Max(1, (int)(VisibleHeight / RowHeight) - 1);

    private void Select(int index, bool announce)
    {
        if (matches.Count == 0) { SelectedIndex = -1; return; }
        SelectedIndex = Math.Clamp(index, 0, matches.Count - 1);
        for (var i = 0; i < rowViews.Count; i++)
        {
            var on = i == SelectedIndex;
            if (on) rowViews[i].SetResourceReference(Border.BackgroundProperty, "SecondaryContainer");
            else rowViews[i].Background = Brushes.Transparent;
            var picker = matches[i].Item.Kind == SuggestionKind.Picker;
            rowInk[i].Icon.SetResourceReference(TextBlock.ForegroundProperty, on ? "OnSecondaryContainer" : picker ? "Primary" : "Tertiary");
            rowInk[i].Label.SetResourceReference(TextBlock.ForegroundProperty, on ? "OnSecondaryContainer" : "OnSurface");
        }
        // Scroll the selected row into view without moving focus.
        var top = SelectedIndex * RowHeight;
        if (top < scroller.VerticalOffset) scroller.ScrollToVerticalOffset(top);
        else if (top + RowHeight > scroller.VerticalOffset + VisibleHeight) scroller.ScrollToVerticalOffset(top + RowHeight - VisibleHeight);
        RenderDoc(matches[SelectedIndex].Item);
        if (announce) SelectionChanged?.Invoke(matches[SelectedIndex]);
    }

    private Border BuildRow(SuggestionMatch match, int index)
    {
        var item = match.Item;
        var picker = item.Kind == SuggestionKind.Picker;
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 13 };
        label.SetResourceReference(TextBlock.FontFamilyProperty, picker ? "TextFont" : "MonoFont");
        // Matched characters are bold in the primary colour.
        var bold = new HashSet<int>(match.Positions);
        var display = item.Label.Replace("\n", " ");
        var runStart = 0;
        for (var i = 1; i <= display.Length; i++)
        {
            if (i < display.Length && bold.Contains(i) == bold.Contains(runStart)) continue;
            var run = new Run(display[runStart..i]);
            if (bold.Contains(runStart)) { run.FontWeight = FontWeights.Bold; run.SetResourceReference(TextElement.ForegroundProperty, "Primary"); }
            label.Inlines.Add(run);
            runStart = i;
        }
        // A picker row says what it opens; its detail joins the label because its tag is the Enter key.
        if (picker && item.Detail.Length > 0) label.Inlines.Add(new Run(" · " + item.Detail));
        var grid = new Grid { Height = RowHeight };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = Ui.Icon(SuggestionPresentation.Icon(item), 18, false, picker ? "Primary" : "Tertiary");
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        grid.Children.Add(icon);
        grid.Children.Add(label.At(0, 1));
        var tagText = SuggestionPresentation.RowTag(item);
        if (tagText.Length > 0)
        {
            var warns = SuggestionPresentation.RowTagWarns(item);
            var tagLabel = Ui.Text(picker ? Loc.T(tagText) : tagText, 11, null, warns ? "OnError" : "OnSurfaceVariant", false, false);
            tagLabel.MaxWidth = 130;
            var tag = new Border { Child = tagLabel, CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            tag.SetResourceReference(Border.BackgroundProperty, warns ? "Error" : "SurfaceHighest");
            tag.ToolTip = tagText;
            grid.Children.Add(tag.At(0, 2));
        }
        rowInk.Add((icon, label));
        var row = new Border { Child = grid, Padding = new Thickness(12, 0, 12, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand, Focusable = false };
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

    /// <summary>The row icon of a suggestion kind (pickers and paths are refined by <see cref="SuggestionPresentation.Icon"/>).</summary>
    public static string IconFor(SuggestionKind kind) => SuggestionPresentation.Icon(new SuggestionItem("", "", kind));

    private void RenderDoc(SuggestionItem item)
    {
        doc.Children.Clear();
        var title = Ui.Mono(item.Label.Replace("\n", " "), 15, "OnSurface", true);
        title.FontWeight = FontWeights.SemiBold;
        title.Margin = new Thickness(0, 0, 8, 4);
        var chipText = Ui.Text(SuggestionPresentation.DocChip(item, KindName), 11, FontWeights.SemiBold, "OnSecondaryContainer", false, false);
        chipText.MaxWidth = 360;
        var chip = new Border { Child = chipText, MinHeight = 22, CornerRadius = new CornerRadius(11), Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 0, 4), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        chip.SetResourceReference(Border.BackgroundProperty, "SecondaryContainer");
        var head = new WrapPanel();
        head.Children.Add(title);
        head.Children.Add(chip);
        doc.Children.Add(head);
        if (item.Doc.Length > 0) doc.Children.Add(Ui.Text(item.Doc, 13, null, "OnSurfaceVariant").Margin(0, 6, 0, 0));
        if (item.Example.Length > 0)
        {
            var example = new Border { Child = Ui.Mono(item.Example, 12, "OnSurface", true), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 4, 0, 0) };
            example.SetResourceReference(Border.BackgroundProperty, "SurfaceLow");
            doc.Children.Add(Ui.Overline("Example").Margin(0, 10, 0, 0));
            doc.Children.Add(example);
        }
        var valuesLine = SuggestionPresentation.ValuesLine(item, Loc.T);
        if (valuesLine.Length > 0)
        {
            var values = Ui.Text(valuesLine, 12, null, "OnSurfaceVariant", true, false).Margin(0, 10, 0, 0);
            System.Windows.Automation.AutomationProperties.SetName(values, valuesLine);
            doc.Children.Add(values);
        }
        if (item.MinSyntax != null && dockerfile)
        {
            var text = Loc.T("Needs") + " # syntax=docker/dockerfile:" + item.MinSyntax + " " + Loc.T("or newer") + (item.LabsMinSyntax != null ? " (" + item.LabsMinSyntax + "-labs " + Loc.T("before that") + ")" : "") + ".";
            if (item.NeedsSyntaxUpgrade)
                text += " " + (syntax == null ? Loc.T("This file has no # syntax line, so the engine's built-in frontend decides.") : Loc.T("This file declares") + " " + syntax.Raw + ".")
                    + " " + Loc.T("After inserting, press Ctrl+Enter to add or upgrade the header.");
            doc.Children.Add(Ui.Text(text, 12, null, item.NeedsSyntaxUpgrade ? "Warning" : "OnSurfaceVariant").Margin(0, 10, 0, 0));
        }
        if (item.Kind == SuggestionKind.Picker) doc.Children.Add(Ui.Text("Press Enter or Tab to open it. Escape returns to the editor.", 12, null, "OnSurfaceVariant").Margin(0, 10, 0, 0));
        docPane.Visibility = Visibility.Visible;
    }
}
