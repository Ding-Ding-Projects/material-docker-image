using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Searchable picker over the fonts installed on this computer. Each name is drawn in its own font, a live sample shows
/// the choice, and "Use default" returns to the built-in font.
/// </summary>
public sealed class AppearanceFontPicker : StackPanel
{
    private static IReadOnlyList<string>? installed;
    /// <summary>Installed font family names, read once.</summary>
    public static IReadOnlyList<string> Installed => installed ??= Fonts.SystemFontFamilies.Select(f => f.Source).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();

    public event Action<string>? Picked;
    private readonly SearchFilter search;
    private readonly ListBox list = new() { Height = 200 };
    private readonly TextBlock sample;
    private readonly TextBlock state = Ui.Caption("");
    private readonly string fallback;
    private bool filling;

    public AppearanceFontPicker(string current, string fallback, string sampleText, bool mono)
    {
        this.fallback = fallback;
        search = new SearchFilter(mono ? "Search monospace and code fonts" : "Search installed fonts", 260);
        sample = new TextBlock { Text = sampleText, FontSize = 16, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        sample.SetResourceReference(TextBlock.ForegroundProperty, "OnSurface");
        AutomationProperties.SetName(list, Loc.T(mono ? "Monospace font" : "Interface font"));
        var template = new DataTemplate();
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding("."));
        text.SetBinding(TextBlock.FontFamilyProperty, new Binding("."));
        text.SetValue(TextBlock.FontSizeProperty, 15.0);
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        template.VisualTree = text;
        list.ItemTemplate = template;
        VirtualizingPanel.SetIsVirtualizing(list, true);
        VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
        list.SelectionChanged += (_, _) =>
        {
            if (filling || list.SelectedItem is not string name) return;
            Show(name);
            Picked?.Invoke(name);
        };
        search.Changed += Fill;
        var reset = Ui.Button("Use default", Icons.RestartAlt, ButtonKind.Text, () => { list.SelectedItem = null; Show(""); Picked?.Invoke(""); });
        reset.ToolTip = Loc.T("Default: ") + fallback;
        var preview = Ui.Card(Ui.VStack(4, Ui.Overline("Live sample"), sample), 12, "SurfaceContainer", 12);
        Children.Add(Ui.Columns(12, [1, 0], search, reset));
        Children.Add(list.Margin(0, 8, 0, 0));
        Children.Add(state.Margin(0, 4, 0, 0));
        Children.Add(preview.Margin(0, 8, 0, 0));
        Fill();
        Show(current);
        if (current.Length > 0) { filling = true; list.SelectedItem = Installed.FirstOrDefault(n => n.Equals(current, StringComparison.OrdinalIgnoreCase)); filling = false; if (list.SelectedItem != null) list.ScrollIntoView(list.SelectedItem); }
    }

    private void Fill()
    {
        filling = true;
        var selected = list.SelectedItem as string;
        var matches = Installed.Where(n => search.Matches(n)).ToList();
        list.ItemsSource = matches;
        if (selected != null && matches.Contains(selected)) list.SelectedItem = selected;
        filling = false;
        state.Text = search.IsActive ? (search.Status.Length > 0 ? search.Status : matches.Count == 0 ? Loc.T("No installed fonts match.") : $"{matches.Count} of {Installed.Count} fonts") : $"{Installed.Count} {Loc.T("installed fonts")}";
    }

    private void Show(string name)
    {
        sample.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(name) ? fallback : name + ", " + fallback);
        sample.ToolTip = string.IsNullOrWhiteSpace(name) ? Loc.T("Built-in default font") : name;
    }
}
