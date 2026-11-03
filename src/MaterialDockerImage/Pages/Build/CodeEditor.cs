using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Monospace source box (Field.Code) with a line-number gutter that scrolls with the text. The gutter is a
/// TextBlock using the same font and size as the TextBox, so line heights match.
/// </summary>
public sealed class CodeEditor : Border
{
    public TextBox Box { get; }
    private readonly TextBlock numbers;
    private readonly TranslateTransform scroll = new();
    private int lineCount = -1;
    /// <summary>Raised with the 1-based line and column of the caret.</summary>
    public event Action<int, int>? CaretMoved;

    public CodeEditor(bool readOnly = false, double minHeight = 200)
    {
        CornerRadius = new CornerRadius(12);
        BorderThickness = new Thickness(1);
        ClipToBounds = true;
        SetResourceReference(BackgroundProperty, readOnly ? "SurfaceContainer" : "SurfaceLowest");
        SetResourceReference(BorderBrushProperty, "OutlineVariant");

        Box = Ui.CodeBox("", minHeight);
        Box.IsReadOnly = readOnly;
        // A constant 2px transparent border: the focus trigger only recolours it, so text never shifts against the gutter.
        Box.BorderThickness = new Thickness(2);
        Box.BorderBrush = Brushes.Transparent;
        Box.Background = Brushes.Transparent;
        Box.Padding = new Thickness(8, 10, 12, 10);
        Box.IsInactiveSelectionHighlightEnabled = true;

        numbers = new TextBlock { TextAlignment = TextAlignment.Right, Width = 40, IsHitTestVisible = false, RenderTransform = scroll };
        numbers.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        numbers.SetResourceReference(TextBlock.ForegroundProperty, "Outline");
        numbers.FontSize = Box.FontSize > 0 ? Box.FontSize : 13;
        Box.FontSize = numbers.FontSize;
        // Text starts below the 2px border and the 10px top padding.
        Canvas.SetTop(numbers, 12);
        var gutter = new Canvas { Width = 48, ClipToBounds = true, Focusable = false };
        gutter.SetResourceReference(Panel.BackgroundProperty, "SurfaceContainer");
        gutter.Children.Add(numbers);
        System.Windows.Automation.AutomationProperties.SetName(gutter, "Line numbers");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(gutter);
        grid.Children.Add(Box.At(0, 1));
        Child = grid;

        Box.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) => scroll.Y = -e.VerticalOffset));
        Box.TextChanged += (_, _) => UpdateNumbers();
        Box.SelectionChanged += (_, _) => RaiseCaret();
        UpdateNumbers();
    }

    public string Text { get => Box.Text; set => Box.Text = value; }

    /// <summary>Suggestions attached with <see cref="EnableSuggestions"/>, or null.</summary>
    public EditorSuggestions? Suggestions { get; private set; }

    /// <summary>Attaches Suggestions (completion, hover, signature hint and value pickers) to this editor.</summary>
    public EditorSuggestions EnableSuggestions(EditorSuggestionLanguage language, Func<string?> contextFolder) =>
        Suggestions ??= EditorSuggestions.Attach(Box, language, contextFolder);

    private void UpdateNumbers()
    {
        var text = Box.Text;
        var count = 1;
        foreach (var c in text) if (c == '\n') count++;
        if (count == lineCount) return;
        lineCount = count;
        var sb = new System.Text.StringBuilder(count * 4);
        for (var i = 1; i <= count; i++) { if (i > 1) sb.Append('\n'); sb.Append(i); }
        numbers.Text = sb.ToString();
        numbers.Width = Math.Max(32, count.ToString().Length * 9 + 8);
        ((Canvas)numbers.Parent).Width = numbers.Width + 8;
    }

    private void RaiseCaret()
    {
        var caret = Box.CaretIndex;
        var line = Box.GetLineIndexFromCharacterIndex(caret);
        if (line < 0) { CaretMoved?.Invoke(1, 1); return; }
        var start = Box.GetCharacterIndexFromLineIndex(line);
        CaretMoved?.Invoke(line + 1, caret - start + 1);
    }

    /// <summary>Selects a zero-based line and scrolls it into view. Safe to call before the box is visible.</summary>
    public void GoToLine(int line, bool focus = true)
    {
        void Apply()
        {
            if (Box.LineCount <= 0) return;
            line = Math.Clamp(line, 0, Box.LineCount - 1);
            var start = Box.GetCharacterIndexFromLineIndex(line);
            if (start < 0) return;
            var length = Box.GetLineLength(line);
            var text = Box.Text;
            while (length > 0 && start + length <= text.Length && text[start + length - 1] is '\n' or '\r') length--;
            if (focus) Box.Focus();
            Box.Select(start, length);
            Box.ScrollToLine(line);
        }
        if (Box.IsLoaded && Box.LineCount > 0) Apply();
        else Dispatcher.BeginInvoke(Apply, System.Windows.Threading.DispatcherPriority.Loaded);
    }
}
