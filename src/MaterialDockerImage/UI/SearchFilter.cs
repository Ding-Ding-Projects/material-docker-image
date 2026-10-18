using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace MaterialDockerImage.UI;

public sealed class SearchFilter : StackPanel
{
    private readonly TextBox query = new() { MinWidth = 190, Width = 260, ToolTip = "Filter this collection" };
    private readonly CheckBox regex = new() { Content = "Regex mode", Margin = new Thickness(8) };
    private readonly CheckBox ignoreCase = new() { Content = "Ignore case", IsChecked = true };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };
    public event Action? Changed;
    public SearchFilter()
    {
        Orientation = Orientation.Horizontal;
        var builder = new Button { Content = "Regex builder", ToolTip = "Open the independent regular expression builder for this filter" };
        var popup = new Popup { PlacementTarget = builder, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        var panel = new StackPanel { Width = 440, Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = "Regular expression builder", FontSize = 20, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = "Pattern is synchronized with this filter. Engine: .NET; timeout: 100 ms per match.", TextWrapping = TextWrapping.Wrap });
        var pattern = new TextBox();
        pattern.TextChanged += (_, _) => { if (query.Text != pattern.Text) query.Text = pattern.Text; };
        query.TextChanged += (_, _) => { if (pattern.Text != query.Text) pattern.Text = query.Text; status.Text = ""; Changed?.Invoke(); };
        panel.Children.Add(pattern); panel.Children.Add(regex); panel.Children.Add(ignoreCase);
        var snippets = new WrapPanel();
        foreach (var snippet in new[] { "^", "$", ".*", "[0-9]+", "[A-Za-z]+", "(?:)", "|", "\\b" })
        {
            var button = new Button { Content = snippet };
            button.Click += (_, _) => { pattern.SelectedText = snippet; pattern.Focus(); };
            snippets.Children.Add(button);
        }
        panel.Children.Add(snippets);
        var sample = new TextBox { AcceptsReturn = true, Height = 90, ToolTip = "Local sample text" };
        panel.Children.Add(new TextBlock { Text = "Test against local sample" }); panel.Children.Add(sample);
        var test = new Button { Content = "Test pattern" };
        test.Click += (_, _) => status.Text = Matches(sample.Text) ? "Sample matches." : status.Text.Length > 0 ? status.Text : "Sample does not match.";
        panel.Children.Add(test); panel.Children.Add(status);
        panel.Children.Add(new TextBlock { Text = "This bounded builder supports .NET patterns, flags, snippets and sample testing. Flavor conversion, debugging, benchmarking, collections and file GREP are not implemented.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), Opacity = .7 });
        popup.Child = new Border { Child = panel, Background = System.Windows.Media.Brushes.WhiteSmoke, BorderBrush = System.Windows.Media.Brushes.Gray, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16) };
        panel.SetValue(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.Black);
        builder.Click += (_, _) => { popup.IsOpen = true; pattern.Focus(); };
        regex.Checked += (_, _) => Changed?.Invoke(); regex.Unchecked += (_, _) => Changed?.Invoke();
        ignoreCase.Checked += (_, _) => Changed?.Invoke(); ignoreCase.Unchecked += (_, _) => Changed?.Invoke();
        Children.Add(query); Children.Add(builder); Children.Add(popup);
        System.Windows.Automation.AutomationProperties.SetName(query, "Filter this collection");
    }
    public bool Matches(string text)
    {
        if (string.IsNullOrEmpty(query.Text)) return true;
        if (regex.IsChecked != true) return text.Contains(query.Text, StringComparison.OrdinalIgnoreCase);
        try { return Regex.IsMatch(text, query.Text, ignoreCase.IsChecked == true ? RegexOptions.IgnoreCase : RegexOptions.None, TimeSpan.FromMilliseconds(100)); }
        catch (ArgumentException) { status.Text = "Invalid pattern. Correct the expression to filter results."; return false; }
        catch (RegexMatchTimeoutException) { status.Text = "Pattern exceeded the 100 ms limit. Simplify the expression."; return false; }
    }
}
