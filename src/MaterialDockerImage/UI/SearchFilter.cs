using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using MaterialDockerImage.Services;

namespace MaterialDockerImage.UI;

/// <summary>
/// Search bar with plain-text matching and an adjacent regular expression builder. Each instance keeps its own
/// isolated query, mode and builder state.
/// </summary>
public sealed class SearchFilter : Border
{
    private readonly TextBox query = new() { Style = (Style)Application.Current.FindResource("Field.Search"), MinWidth = 120 };
    private readonly ToggleButton regexToggle = new() { Content = ".*", Width = 36, Height = 28, Cursor = Cursors.Hand, FocusVisualStyle = null };
    private readonly TextBlock placeholder;
    private string status = "";
    public event Action? Changed;
    public string Placeholder { set => placeholder.Text = Loc.T(value); }
    public string Query { get => query.Text; set => query.Text = value; }
    public bool RegexMode { get => regexToggle.IsChecked == true; set => regexToggle.IsChecked = value; }
    public bool IgnoreCase { get; set; } = true;
    public bool IsActive => query.Text.Length > 0;
    public string Status => status;
    public TextBox Box => query;

    public SearchFilter(string placeholderText = "Search", double width = 320)
    {
        CornerRadius = new CornerRadius(20);
        Height = 40;
        Width = width;
        Padding = new Thickness(12, 0, 6, 0);
        SetResourceReference(BackgroundProperty, "SurfaceHigh");
        placeholder = Ui.Text(placeholderText, 14, null, "Outline", false);
        placeholder.IsHitTestVisible = false;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Ui.Icon(Icons.Search, 18).Margin(0, 0, 6, 0));
        var field = new Grid(); field.Children.Add(placeholder); field.Children.Add(query); Grid.SetColumn(field, 1); grid.Children.Add(field);
        var clear = Ui.IconButton(Icons.Close, "Clear search", () => { query.Clear(); query.Focus(); });
        clear.Width = clear.Height = clear.MinHeight = 28; clear.FontSize = 16; clear.Visibility = Visibility.Collapsed;
        Grid.SetColumn(clear, 2); grid.Children.Add(clear);
        regexToggle.Template = ToggleTemplate();
        regexToggle.SetResourceReference(Control.FontFamilyProperty, "MonoFont");
        regexToggle.ToolTip = Loc.T("Regular expression mode. Right-click or Alt+R for the builder.");
        AutomationProperties.SetName(regexToggle, Loc.T("Regular expression mode"));
        regexToggle.Checked += (_, _) => Changed?.Invoke(); regexToggle.Unchecked += (_, _) => Changed?.Invoke();
        regexToggle.MouseRightButtonUp += (_, e) => { OpenBuilder(); e.Handled = true; };
        Grid.SetColumn(regexToggle, 3); grid.Children.Add(regexToggle);
        Child = grid;
        AutomationProperties.SetName(query, Loc.T(placeholderText));
        query.TextChanged += (_, _) => { placeholder.Visibility = query.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; clear.Visibility = query.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible; status = ""; Changed?.Invoke(); };
        query.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && query.Text.Length > 0) { query.Clear(); e.Handled = true; }
            else if (e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Alt) { OpenBuilder(); e.Handled = true; }
        };
        GotKeyboardFocus += (_, _) => SetResourceReference(BorderBrushProperty, "Primary");
        LostKeyboardFocus += (_, _) => BorderBrush = null;
        BorderThickness = new Thickness(2);
    }

    private static ControlTemplate ToggleTemplate()
    {
        var template = new ControlTemplate(typeof(ToggleButton));
        var border = new FrameworkElementFactory(typeof(Border), "bg");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
        border.SetResourceReference(Border.BackgroundProperty, "SurfaceHighest");
        var text = new FrameworkElementFactory(typeof(ContentPresenter));
        text.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        text.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(text);
        template.VisualTree = border;
        var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        on.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Primary"), "bg"));
        on.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("OnPrimary")));
        template.Triggers.Add(on);
        var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "bg"));
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("OnSurface"), "bg"));
        template.Triggers.Add(focus);
        return template;
    }

    /// <summary>Anchored builder: pattern synchronized with this search, snippets, flags, live test and explanation.</summary>
    public void OpenBuilder()
    {
        var pattern = Ui.Input(query.Text, "Pattern", true);
        var sample = Ui.CodeBox("", 90);
        var result = Ui.Caption("");
        var ignore = Ui.Check("Ignore case", IgnoreCase, v => { IgnoreCase = v; Changed?.Invoke(); });
        var multiline = Ui.Check("Multiline anchors", false);
        void Test()
        {
            try
            {
                var options = (IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None) | (multiline.IsChecked == true ? RegexOptions.Multiline : RegexOptions.None);
                var matches = Regex.Matches(sample.Text, pattern.Text, options, TimeSpan.FromMilliseconds(100));
                result.Text = matches.Count == 0 ? Loc.T("No match in the sample.") : $"{matches.Count} match(es); first at index {matches[0].Index}: \"{Trim(matches[0].Value)}\"" + (matches[0].Groups.Count > 1 ? $", {matches[0].Groups.Count - 1} capture group(s)" : "");
            }
            catch (ArgumentException e) { result.Text = "Invalid pattern: " + e.Message; }
            catch (RegexMatchTimeoutException) { result.Text = "The pattern exceeded the 100 ms limit."; }
        }
        pattern.TextChanged += (_, _) => { if (query.Text != pattern.Text) query.Text = pattern.Text; RegexMode = true; Test(); };
        sample.TextChanged += (_, _) => Test();
        var snippets = new WrapPanel();
        foreach (var (snippet, meaning) in new[] { ("^", "Start of text"), ("$", "End of text"), (".*", "Anything"), ("\\d+", "Digits"), ("[A-Za-z]+", "Letters"), ("\\b", "Word boundary"), ("(?:)", "Group"), ("|", "Or"), ("(?i)", "Ignore case inline"), ("[0-9a-f]{12}", "Short image ID") })
        {
            var b = Ui.Button(snippet, null, ButtonKind.Outlined, () => { pattern.SelectedText = snippet; pattern.CaretIndex = pattern.SelectionStart + snippet.Length; pattern.Focus(); }, false);
            b.ToolTip = meaning; b.MinHeight = 32; b.Height = 32; b.Margin = new Thickness(0, 0, 6, 6); b.Padding = new Thickness(10, 0, 10, 0);
            b.SetResourceReference(Control.FontFamilyProperty, "MonoFont");
            snippets.Children.Add(b);
        }
        var body = Ui.VStack(10,
            Ui.Text("Regular expression builder", 16, FontWeights.SemiBold),
            Ui.Caption("Synchronized with this search only. Engine: .NET " + Environment.Version.ToString(2) + " regular expressions, 100 ms timeout per match."),
            Ui.Field("Pattern", pattern), snippets, Ui.HStack(12, ignore, multiline),
            Ui.Field("Test against a local sample", sample), result,
            Ui.Caption("Replacement, flavour conversion, benchmarking and file search are separate workbench topics that are not implemented here."));
        Ui.ShowPopup(regexToggle, body, 440);
        Test();
    }
    private static string Trim(string s) => s.Length > 40 ? s[..40] + "…" : s;

    public bool Matches(string text)
    {
        if (string.IsNullOrEmpty(query.Text)) return true;
        if (!RegexMode) return text.Contains(query.Text, IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        try { return Regex.IsMatch(text, query.Text, IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None, TimeSpan.FromMilliseconds(100)); }
        catch (ArgumentException) { status = "Invalid pattern. Correct the expression to filter results."; return false; }
        catch (RegexMatchTimeoutException) { status = "Pattern exceeded the 100 ms limit. Simplify the expression."; return false; }
    }

    /// <summary>True when the current pattern can run (used to refuse bulk actions on invalid patterns).</summary>
    public bool IsValid
    {
        get
        {
            if (!RegexMode || query.Text.Length == 0) return true;
            try { _ = new Regex(query.Text, RegexOptions.None, TimeSpan.FromMilliseconds(100)); return true; } catch (ArgumentException) { return false; }
        }
    }
}
