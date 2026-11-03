using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Find bar for a source TextBox: the shared search field (with its regex builder), a match counter and
/// previous/next buttons. Matches are selected in the target without moving keyboard focus out of the search.
/// </summary>
public sealed class TextFindBar : Grid
{
    private readonly TextBox target;
    private readonly SearchFilter search;
    private readonly TextBlock count = Ui.Caption("");
    private readonly DispatcherTimer recount = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private List<(int Index, int Length)> matches = new();
    private int current = -1;

    public SearchFilter Search => search;

    public TextFindBar(TextBox target, string placeholder = "Find in source", double width = 260)
    {
        this.target = target;
        target.IsInactiveSelectionHighlightEnabled = true;
        search = new SearchFilter(placeholder, width) { HorizontalAlignment = HorizontalAlignment.Stretch };
        search.Width = double.NaN; search.MinWidth = 160;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Children.Add(search);
        count.Margin = new Thickness(10, 0, 4, 0); count.MinWidth = 70;
        Children.Add(count.At(0, 1));
        Children.Add(Ui.IconButton(Icons.ExpandLess, "Previous match (Shift+F3)", FindPrevious).At(0, 2));
        Children.Add(Ui.IconButton(Icons.ExpandMore, "Next match (F3)", FindNext).At(0, 3));
        search.Changed += () => { Recount(); if (matches.Count > 0) SelectNearest(); };
        search.Box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { if (Keyboard.Modifiers == ModifierKeys.Shift) FindPrevious(); else FindNext(); e.Handled = true; }
        };
        recount.Tick += (_, _) => { recount.Stop(); Recount(); };
        target.TextChanged += (_, _) => { if (search.IsActive) { recount.Stop(); recount.Start(); } };
    }

    public void FocusSearch()
    {
        if (target.SelectionLength is > 0 and < 200 && !target.SelectedText.Contains('\n')) search.Query = target.SelectedText;
        search.Box.Focus(); search.Box.SelectAll();
    }

    private void Recount()
    {
        matches = new();
        current = -1;
        var query = search.Query;
        if (query.Length == 0) { count.Text = ""; return; }
        var text = target.Text;
        try
        {
            if (search.RegexMode)
            {
                // The field's flags and engine (non-backtracking when possible); ^ and $ always work per line here.
                var regex = search.CreateRegex(RegexOptions.Multiline) ?? throw new ArgumentException(search.Predicate.Error);
                foreach (Match m in regex.Matches(text))
                {
                    if (m.Length == 0) continue;
                    matches.Add((m.Index, m.Length));
                    if (matches.Count >= 10000) break;
                }
            }
            else
            {
                var comparison = search.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                var at = 0;
                while (matches.Count < 10000 && (at = text.IndexOf(query, at, comparison)) >= 0) { matches.Add((at, query.Length)); at += query.Length; }
            }
            count.Text = matches.Count == 0 ? Loc.T("No matches") : matches.Count >= 10000 ? "10000+" : $"{matches.Count} {Loc.T(matches.Count == 1 ? "match" : "matches")}";
        }
        catch (ArgumentException) { count.Text = Loc.T("Invalid pattern"); }
        catch (RegexMatchTimeoutException) { matches.Clear(); count.Text = Loc.T("Pattern too slow"); }
    }

    private void SelectNearest()
    {
        var caret = target.SelectionStart;
        current = matches.FindIndex(m => m.Index >= caret);
        if (current < 0) current = 0;
        Show();
    }

    public void FindNext()
    {
        if (matches.Count == 0) Recount();
        if (matches.Count == 0) return;
        var after = target.SelectionStart + Math.Max(1, target.SelectionLength);
        var next = matches.FindIndex(m => m.Index >= after);
        current = next < 0 ? 0 : next;
        Show();
    }

    public void FindPrevious()
    {
        if (matches.Count == 0) Recount();
        if (matches.Count == 0) return;
        var before = target.SelectionStart;
        var previous = matches.FindLastIndex(m => m.Index < before);
        current = previous < 0 ? matches.Count - 1 : previous;
        Show();
    }

    private void Show()
    {
        if (current < 0 || current >= matches.Count) return;
        var (index, length) = matches[current];
        if (index + length > target.Text.Length) { Recount(); return; }
        target.Select(index, length);
        var line = target.GetLineIndexFromCharacterIndex(index);
        if (line >= 0) target.ScrollToLine(line);
        count.Text = $"{current + 1} / {matches.Count}";
    }
}
