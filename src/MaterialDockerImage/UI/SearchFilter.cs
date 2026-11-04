using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using MaterialDockerImage.Services;

namespace MaterialDockerImage.UI;

/// <summary>
/// Search bar used by every search and filter field in the app: plain-text matching by default, an explicit
/// regular expression opt-in (the .* toggle) and its own anchored builder popover (Builder button, right-click on
/// .* or Alt+R). Each instance keeps its own query, mode, flags, sample text and recent patterns in memory.
/// Register every new instance in <see cref="SearchSurfaceCatalogue"/>.
/// </summary>
public sealed class SearchFilter : Border
{
    private readonly TextBox query = new() { Style = (Style)Application.Current.FindResource("Field.Search"), MinWidth = 96 };
    private readonly ToggleButton regexToggle = new() { Content = ".*", Width = 36, Height = 28, Cursor = Cursors.Hand, FocusVisualStyle = null };
    private readonly Button builderButton;
    private readonly TextBlock placeholder;
    private string placeholderText;
    private RegexSearchQuery? predicate;
    private SearchBuilderPopover? builder;
    private string status = "";
    public event Action? Changed;
    /// <summary>Raised when this field's builder opens or closes, so a host that is itself a popup can stay open while the builder is in use.</summary>
    public event Action? BuilderStateChanged;

    /// <summary>In-memory builder state for this field only (flags, sample, replacement, recent patterns).</summary>
    public RegexFieldState State { get; } = new();
    public string Placeholder { get => placeholderText; set { placeholderText = value; placeholder.Text = Loc.T(value); AutomationProperties.SetName(query, Loc.T(value)); } }
    public string Query { get => query.Text; set => query.Text = value; }
    public bool RegexMode { get => regexToggle.IsChecked == true; set => regexToggle.IsChecked = value; }
    public RegexBuilderFlags Flags
    {
        get => State.Flags;
        set { if (State.Flags == value) return; State.Flags = value; predicate = null; Changed?.Invoke(); }
    }
    public bool IgnoreCase { get => Flags.IgnoreCase; set => Flags = Flags with { IgnoreCase = value }; }
    public bool IsActive => query.Text.Length > 0;
    /// <summary>Message about the last filtering pass (invalid pattern or timeout); empty when fine.</summary>
    public string Status => status;
    public TextBox Box => query;
    public bool BuilderOpen => builder != null;

    /// <summary>The current predicate, rebuilt only when the query, mode or flags change.</summary>
    public RegexSearchQuery Predicate => predicate ??= new RegexSearchQuery(query.Text, RegexMode, Flags);

    public SearchFilter(string placeholderText = "Search", double width = 320)
    {
        this.placeholderText = placeholderText;
        CornerRadius = new CornerRadius(20);
        Height = 40;
        Width = width;
        Padding = new Thickness(12, 0, 6, 0);
        SetResourceReference(BackgroundProperty, "SurfaceHigh");
        placeholder = Ui.Text(placeholderText, 14, null, "Outline", false);
        placeholder.IsHitTestVisible = false;
        placeholder.TextTrimming = TextTrimming.CharacterEllipsis;
        var grid = new Grid();
        foreach (var w in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        grid.Children.Add(Ui.Icon(Icons.Search, 18).Margin(0, 0, 6, 0));
        var field = new Grid(); field.Children.Add(placeholder); field.Children.Add(query); Grid.SetColumn(field, 1); grid.Children.Add(field);
        var clear = Ui.IconButton(Icons.Close, "Clear search", () => { query.Clear(); query.Focus(); });
        clear.Width = clear.Height = clear.MinHeight = 28; clear.FontSize = 16; clear.Visibility = Visibility.Collapsed;
        Grid.SetColumn(clear, 2); grid.Children.Add(clear);
        regexToggle.Template = ToggleTemplate();
        regexToggle.SetResourceReference(Control.FontFamilyProperty, "MonoFont");
        regexToggle.ToolTip = Loc.T("Regular expression mode (off: plain text). Right-click or Alt+R opens the builder.");
        AutomationProperties.SetName(regexToggle, Loc.T("Regular expression mode"));
        AutomationProperties.SetHelpText(regexToggle, Loc.T("Off matches the text as typed. On treats it as a .NET regular expression."));
        regexToggle.Checked += (_, _) => { predicate = null; Changed?.Invoke(); };
        regexToggle.Unchecked += (_, _) => { predicate = null; Changed?.Invoke(); };
        regexToggle.MouseRightButtonUp += (_, e) => { OpenBuilder(); e.Handled = true; };
        Grid.SetColumn(regexToggle, 3); grid.Children.Add(regexToggle);
        builderButton = Ui.IconButton(Icons.Tune, "Regular expression builder (Alt+R)", OpenBuilder);
        builderButton.Width = builderButton.Height = builderButton.MinHeight = 28; builderButton.FontSize = 16; builderButton.Margin = new Thickness(2, 0, 0, 0);
        builderButton.ToolTip = Loc.T("Regular expression builder for this search (Alt+R)");
        AutomationProperties.SetHelpText(builderButton, Loc.T("Opens a builder beside this field. Esc closes it and returns to the field."));
        Grid.SetColumn(builderButton, 4); grid.Children.Add(builderButton);
        Child = grid;
        AutomationProperties.SetName(query, Loc.T(placeholderText));
        AutomationProperties.SetHelpText(query, Loc.T("Plain text search. Alt+R opens the regular expression builder."));
        query.TextChanged += (_, _) =>
        {
            placeholder.Visibility = query.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            clear.Visibility = query.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            status = ""; predicate = null;
            Changed?.Invoke();
        };
        query.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && query.Text.Length > 0) { query.Clear(); e.Handled = true; } };
        // Alt+R arrives as Key.System with SystemKey R; handled for the field, the toggle and the builder button.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.System && e.SystemKey == Key.R && Keyboard.Modifiers == ModifierKeys.Alt) { OpenBuilder(); e.Handled = true; }
        };
        GotKeyboardFocus += (_, _) => SetResourceReference(BorderBrushProperty, "Primary");
        LostKeyboardFocus += (_, _) => BorderBrush = null;
        BorderThickness = new Thickness(2);
        Unloaded += (_, _) => builder?.Close();
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

    /// <summary>Opens (or focuses) this field's anchored builder popover.</summary>
    public void OpenBuilder()
    {
        if (builder != null) { builder.Focus(); return; }
        builder = new SearchBuilderPopover(this);
        builder.Closed += returnFocus =>
        {
            builder = null;
            BuilderStateChanged?.Invoke();
            if (returnFocus) Dispatcher.BeginInvoke(() => { query.Focus(); Keyboard.Focus(query); }, System.Windows.Threading.DispatcherPriority.Input);
        };
        builder.Open();
        BuilderStateChanged?.Invoke();
    }

    /// <summary>Closes this field's builder, if it is open, without moving focus.</summary>
    public void CloseBuilder() => builder?.Close();

    /// <summary>True when the text matches. Plain text is a contains test; regex mode uses the bounded engine.</summary>
    public bool Matches(string text)
    {
        var p = Predicate;
        var result = p.Matches(text);
        status = !p.IsValid ? (p.Error ?? "Invalid pattern.") + " Correct the expression to filter results."
            : p.TimedOut ? $"The pattern exceeded the {RegexBuilderEngine.MatchTimeout.TotalMilliseconds:0} ms limit on some items, which are hidden. Simplify the expression."
            : "";
        return result;
    }

    /// <summary>Row matching: any single field or the fields joined (see RegexSearchQuery.MatchesFields).</summary>
    public bool MatchesFields(IReadOnlyList<string> fields)
    {
        var p = Predicate;
        var result = p.MatchesFields(fields);
        status = !p.IsValid ? (p.Error ?? "Invalid pattern.") + " Correct the expression to filter results."
            : p.TimedOut ? $"The pattern exceeded the {RegexBuilderEngine.MatchTimeout.TotalMilliseconds:0} ms limit on some items, which are hidden. Simplify the expression."
            : "";
        return result;
    }

    /// <summary>True when the current pattern can run (used to refuse bulk actions on invalid patterns).</summary>
    public bool IsValid => Predicate.IsValid;

    /// <summary>
    /// A compiled regex for find-in-text consumers, honouring this field's flags (plus <paramref name="extra"/>
    /// Multiline when requested). Null when regex mode is off, the query is empty, or the pattern is invalid.
    /// </summary>
    public Regex? CreateRegex(RegexOptions extra = RegexOptions.None)
    {
        if (!RegexMode || query.Text.Length == 0) return null;
        var flags = Flags;
        if (extra.HasFlag(RegexOptions.Multiline)) flags = flags with { Multiline = true };
        return RegexBuilderEngine.Compile(query.Text, flags).Regex;
    }
}
