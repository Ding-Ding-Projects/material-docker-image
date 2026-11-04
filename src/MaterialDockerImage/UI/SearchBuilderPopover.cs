using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MaterialDockerImage.Services;

namespace MaterialDockerImage.UI;

/// <summary>
/// The anchored regular expression builder of one <see cref="SearchFilter"/>. It edits that field's query,
/// mode and flags directly (two-way) and keeps sample and replacement text in the field's in-memory state.
/// Pure logic lives in RegexBuilder*.cs; this class is only the view.
/// </summary>
internal sealed class SearchBuilderPopover
{
    private readonly SearchFilter field;
    private readonly Popup popup;
    private readonly Border card;
    private readonly ScrollViewer scroll;
    private readonly TextBox pattern = Ui.Input("", "Pattern", true);
    private readonly TextBlock errorView = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly TextBlock validation = Ui.Caption("");
    private readonly TextBlock engine = Ui.Caption("");
    private readonly CheckBox modeSwitch;
    private readonly List<ToggleButton> flagChips = new();
    private readonly TreeView explanation = new() { MaxHeight = 220, MinHeight = 60 };
    private readonly TextBox sample;
    private readonly TextBlock highlighted = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock testStatus = Ui.Caption("");
    private readonly DataGrid groups = new() { IsReadOnly = true, AutoGenerateColumns = false, HeadersVisibility = DataGridHeadersVisibility.Column, CanUserAddRows = false, MaxHeight = 200, MinHeight = 0 };
    private readonly TextBox replacement;
    private readonly TextBox replaceOutput = Ui.CodeBox("", 60);
    private readonly TextBlock replaceStatus = Ui.Caption("");
    private readonly StackPanel savedList = new();
    private readonly StackPanel recentList = new();
    private readonly TextBlock mineStatus = Ui.Caption("");
    private readonly TextBox saveName = Ui.Input("", "Pattern name");
    private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private CancellationTokenSource? evaluation;
    private List<RegexSavedPattern>? saved;
    private bool syncing;
    private bool returnFocus;
    private ComboBox? categoryCombo;
    private readonly TextChangedEventHandler fieldTextChanged;
    private readonly Action fieldChanged;

    /// <summary>Raised once when the popover closes; true when focus should return to the field (Esc, Done, close).</summary>
    public event Action<bool>? Closed;

    public SearchBuilderPopover(SearchFilter field)
    {
        this.field = field;
        pattern.Text = field.Query;
        pattern.IsInactiveSelectionHighlightEnabled = true;
        AutomationProperties.SetName(pattern, Loc.T("Pattern"));
        errorView.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        validation.SetValue(AutomationProperties.LiveSettingProperty, AutomationLiveSetting.Polite);
        testStatus.SetValue(AutomationProperties.LiveSettingProperty, AutomationLiveSetting.Polite);
        modeSwitch = Ui.Switch("Use as a regular expression", field.RegexMode, v => { field.RegexMode = v; Refresh(); },
            "Off: the field matches the text exactly as typed (default). On: the text is a .NET regular expression.");
        sample = Ui.CodeBox(field.State.Sample, 80);
        sample.MaxLength = RegexBuilderEngine.MaxSampleBytes;
        AutomationProperties.SetName(sample, Loc.T("Sample text to test the pattern against"));
        replacement = Ui.Input(field.State.Replacement, "Replacement", true);
        replaceOutput.IsReadOnly = true;
        AutomationProperties.SetName(replaceOutput, Loc.T("Replace preview"));
        highlighted.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        highlighted.SetResourceReference(TextBlock.ForegroundProperty, "OnSurface");
        AutomationProperties.SetName(highlighted, Loc.T("Sample with matches highlighted"));
        AutomationProperties.SetName(explanation, Loc.T("Pattern explanation"));
        AutomationProperties.SetName(groups, Loc.T("Capture groups"));
        foreach (var (header, path) in new[] { ("Match", "Match"), ("Group", "Group"), ("Name", "Name"), ("Value", "Value"), ("Position", "Position") })
            groups.Columns.Add(new DataGridTextColumn { Header = Loc.T(header), Binding = new Binding(path), Width = header == "Value" ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto });

        var content = Ui.VStack(14,
            Header(),
            modeSwitch,
            PatternSection(),
            FlagsSection(),
            InsertSection(),
            Section("Explanation", "Each token of the pattern in plain English. Select a row to highlight that token in the pattern.", explanation),
            TesterSection(),
            ReplaceSection(),
            ActionsRow(),
            DialectNote());
        scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 4, 0) };
        card = Ui.Card(scroll, 16, "SurfaceHigh", 16);
        card.Effect = Ui.Elevation(18, 4, .35);
        card.Margin = new Thickness(0, 4, 12, 12);
        card.Focusable = false;
        KeyboardNavigation.SetTabNavigation(card, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetControlTabNavigation(card, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetDirectionalNavigation(card, KeyboardNavigationMode.Contained);
        AutomationProperties.SetName(card, Loc.T("Regular expression builder for") + " " + Loc.T(field.Placeholder));
        card.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && categoryCombo?.IsDropDownOpen != true) { Close(); e.Handled = true; }
        };
        popup = new Popup
        {
            PlacementTarget = field, Placement = PlacementMode.Custom, CustomPopupPlacementCallback = Place,
            StaysOpen = false, AllowsTransparency = true, Child = card, Focusable = false,
        };
        popup.Closed += (_, _) => OnClosed();

        pattern.TextChanged += (_, _) =>
        {
            if (syncing) return;
            syncing = true; field.Query = pattern.Text; syncing = false;
            Refresh();
        };
        sample.TextChanged += (_, _) => { field.State.Sample = sample.Text; Schedule(); };
        replacement.TextChanged += (_, _) => { field.State.Replacement = replacement.Text; Schedule(); };
        explanation.SelectedItemChanged += (_, e) =>
        {
            if (e.NewValue is TreeViewItem { Tag: RegexExplainNode n } && n.Start + n.Length <= pattern.Text.Length) pattern.Select(n.Start, n.Length);
        };
        debounce.Tick += (_, _) => { debounce.Stop(); _ = EvaluateAsync(); };
        fieldTextChanged = (_, _) =>
        {
            if (syncing || pattern.Text == field.Query) return;
            syncing = true; pattern.Text = field.Query; syncing = false;
            Refresh();
        };
        fieldChanged = () =>
        {
            if (modeSwitch.IsChecked != field.RegexMode) { modeSwitch.IsChecked = field.RegexMode; Refresh(); }
        };
        field.Box.TextChanged += fieldTextChanged;
        field.Changed += fieldChanged;
    }

    public void Open()
    {
        var window = Window.GetWindow(field);
        var width = Math.Min(600, Math.Max(320, (window?.ActualWidth ?? 960) - 48));
        card.Width = width;
        scroll.MaxHeight = Math.Max(260, ((window?.ActualHeight ?? 720) - 140) * 0.9);
        popup.IsOpen = true;
        RenderMine();
        Refresh();
        Focus();
    }

    public void Focus() => card.Dispatcher.BeginInvoke(() => { pattern.Focus(); Keyboard.Focus(pattern); pattern.CaretIndex = pattern.Text.Length; }, DispatcherPriority.Input);

    /// <summary>Closes the popover and returns focus to the field. Clicking elsewhere closes it without moving focus.</summary>
    public void Close() { returnFocus = true; if (popup.IsOpen) popup.IsOpen = false; }

    private void OnClosed()
    {
        debounce.Stop();
        evaluation?.Cancel();
        field.Box.TextChanged -= fieldTextChanged;
        field.Changed -= fieldChanged;
        if (field.RegexMode) field.State.Recent.Add(field.Query, field.Flags);
        Closed?.Invoke(returnFocus);
    }

    /// <summary>Below the field when it fits, otherwise above it (WPF picks the first placement that stays on screen).</summary>
    private CustomPopupPlacement[] Place(Size popupSize, Size targetSize, Point offset) =>
    [
        new(new Point(0, targetSize.Height + 4), PopupPrimaryAxis.Horizontal),
        new(new Point(0, -popupSize.Height - 4), PopupPrimaryAxis.Horizontal),
        new(new Point(targetSize.Width - popupSize.Width, targetSize.Height + 4), PopupPrimaryAxis.Horizontal),
    ];

    // ----- Layout -----

    private FrameworkElement Header()
    {
        var close = Ui.IconButton(Icons.Close, "Close builder (Esc)", Close);
        var title = Ui.VStack(2, Ui.Text("Regular expression builder", 16, FontWeights.SemiBold), Ui.Caption(Loc.T("For this search only:") + " " + Loc.T(field.Placeholder)));
        var bar = new DockPanel();
        bar.Children.Add(close.Dock(Dock.Right));
        bar.Children.Add(title);
        return bar;
    }

    private static FrameworkElement Section(string title, string? caption, params UIElement[] children)
    {
        var stack = Ui.VStack(6, Ui.Overline(title));
        if (caption != null) stack.Children.Add(Ui.Caption(caption));
        foreach (var c in children) stack.Children.Add(c);
        return stack;
    }

    private FrameworkElement PatternSection()
    {
        var errorBox = Ui.Card(errorView, 8, "ErrorContainer", 8);
        errorBox.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(UIElement.Visibility)) { Source = errorView });
        return Section("Pattern", "Edits here change the search field, and typing in the field changes this pattern.",
            Ui.Field("Pattern", pattern, "The raw .NET regular expression. Typing here updates the search field immediately; nothing is saved.", @"\b\d{3}\b"),
            errorBox, validation, engine);
    }

    private FrameworkElement FlagsSection()
    {
        var wrap = new WrapPanel();
        foreach (var (name, inline, description) in RegexBuilderFlags.All)
        {
            var flag = name;
            var chip = Ui.Chip(name + (inline.Length > 0 ? $" ({inline})" : ""), field.Flags.Get(flag), v => { field.Flags = field.Flags.With(flag, v); Refresh(); });
            chip.ToolTip = Loc.T(description);
            AutomationProperties.SetHelpText(chip, Loc.T(description));
            chip.Margin = new Thickness(0, 0, 6, 6);
            flagChips.Add(chip);
            wrap.Children.Add(chip);
        }
        return Section("Flags", "IgnoreCase also applies to plain-text mode. The other flags only affect regular expressions.", wrap);
    }

    private FrameworkElement InsertSection()
    {
        var tabs = new TabControl { MinHeight = 120 };
        AutomationProperties.SetName(tabs, Loc.T("Insert into the pattern"));
        tabs.Items.Add(Tab("Text", TextTab()));
        tabs.Items.Add(Tab("Classes", ClassesTab()));
        tabs.Items.Add(Tab("Anchors", TokenWrap("Anchors")));
        tabs.Items.Add(Tab("Groups", GroupsTab()));
        tabs.Items.Add(Tab("Repeat", RepeatTab()));
        tabs.Items.Add(Tab("Library", LibraryTab()));
        tabs.Items.Add(Tab("My patterns", MineTab()));
        return Section("Insert", "Guided insertion at the caret. Group buttons wrap the selected part of the pattern. Inserting turns regular expression mode on.", tabs);
    }

    private static TabItem Tab(string header, UIElement content) => new() { Header = Loc.T(header), Content = new Border { Padding = new Thickness(0, 10, 0, 0), Child = content } };

    private Button Small(string label, string? tooltip, Action action, bool mono = false)
    {
        var b = Ui.Button(label, null, ButtonKind.Outlined, action, !mono);
        b.MinHeight = 32; b.Height = 32; b.Margin = new Thickness(0, 0, 6, 6); b.Padding = new Thickness(10, 0, 10, 0);
        if (mono) b.SetResourceReference(Control.FontFamilyProperty, "MonoFont");
        if (tooltip != null) { b.ToolTip = Loc.T(tooltip); AutomationProperties.SetHelpText(b, Loc.T(tooltip)); }
        if (mono) AutomationProperties.SetName(b, label + " " + Loc.T(tooltip ?? ""));
        return b;
    }

    private WrapPanel TokenWrap(string family)
    {
        var wrap = new WrapPanel();
        foreach (var t in RegexBuilderLibrary.Tokens.Where(t => t.Family == family))
            wrap.Children.Add(Small(t.Label, t.Description, () => Insert(t.Prefix, t.Suffix), true));
        return wrap;
    }

    private FrameworkElement TextTab()
    {
        var literal = Ui.Input("", "Literal text");
        var preview = Ui.Mono("", 12, "OnSurfaceVariant", true);
        literal.TextChanged += (_, _) => preview.Text = literal.Text.Length == 0 ? "" : RegexBuilderLibrary.Literal(literal.Text);
        var options = Ui.CodeBox("", 60);
        AutomationProperties.SetName(options, Loc.T("Alternatives, one per line"));
        var optionsError = Ui.Caption("");
        return Ui.VStack(8,
            Ui.Field("Text to match exactly", literal, "Special characters are escaped automatically, so the text matches as typed.", "v1.2 (beta)"),
            preview,
            Small("Insert escaped text", null, () => { if (literal.Text.Length > 0) Insert(RegexBuilderLibrary.Literal(literal.Text), ""); }),
            Ui.Field("Alternatives (one per line)", options, "Inserts (?:a|b|c): any one of the lines, each escaped.", "error\nwarning"),
            optionsError,
            Small("Insert alternatives", null, () =>
            {
                try { Insert(RegexBuilderLibrary.Alternation(options.Text.Replace("\r", "").Split('\n')), ""); optionsError.Text = ""; }
                catch (ArgumentException e) { optionsError.Text = e.Message; }
            }));
    }

    private FrameworkElement ClassesTab()
    {
        var categories = categoryCombo = Ui.Combo(RegexExplainer.UnicodeCategories.Select(c => c.Key + " · " + c.Value), "L · letter");
        AutomationProperties.SetName(categories, Loc.T("Unicode category"));
        var negateCategory = Ui.Check("Not in this category", false);
        var members = Ui.Input("", "Set members", true);
        var digits = Ui.Check(@"Digits \d", false);
        var word = Ui.Check(@"Word characters \w", false);
        var space = Ui.Check(@"White space \s", false);
        var negated = Ui.Check("Negated set (any character except these)", false);
        var preview = Ui.Mono("", 12, "OnSurfaceVariant", true);
        string? Build(out string? error)
        {
            error = null;
            var items = members.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (digits.IsChecked == true) items.Add(@"\d");
            if (word.IsChecked == true) items.Add(@"\w");
            if (space.IsChecked == true) items.Add(@"\s");
            if (items.Count == 0) return null;
            try { return RegexBuilderLibrary.CharacterSet(items, negated.IsChecked == true); }
            catch (ArgumentException e) { error = e.Message; return null; }
        }
        void Update() { var set = Build(out var error); preview.Text = error ?? set ?? ""; }
        members.TextChanged += (_, _) => Update();
        foreach (var c in new[] { digits, word, space, negated }) { c.Checked += (_, _) => Update(); c.Unchecked += (_, _) => Update(); }
        return Ui.VStack(8,
            TokenWrap("Character classes"),
            Ui.Field("Unicode category", Ui.HStack(8, categories, negateCategory), "Matches any character in a Unicode category, for example letters in every script.", @"\p{L}"),
            Small("Insert category", null, () =>
            {
                var code = (categories.SelectedItem as string ?? "L").Split(' ')[0];
                Insert(RegexBuilderLibrary.UnicodeCategory(code, negateCategory.IsChecked == true), "");
            }),
            Ui.Field("Custom set", members, "Characters and ranges separated by spaces. A range is three characters such as a-z. ] \\ ^ and - are escaped for you.", "a-f 0-9 _ ."),
            Ui.Wrap(8, digits, word, space, negated),
            preview,
            Small("Insert set", null, () => { var set = Build(out var error); if (set != null) Insert(set, ""); else preview.Text = error ?? Loc.T("Add at least one member."); }));
    }

    private FrameworkElement GroupsTab()
    {
        var name = Ui.Input("", "Group name", true);
        var nameError = Ui.Caption("");
        var reference = Ui.Input("", "Group number or name", true);
        return Ui.VStack(8,
            TokenWrap("Groups"),
            TokenWrap("Alternation"),
            Ui.Field("Named group", name, "Wraps the selection in (?<name>...). The capture appears under this name in the groups table and in replacements as ${name}.", "version"),
            nameError,
            Small("Wrap selection in named group", null, () =>
            {
                if (!RegexBuilderLibrary.IsValidGroupName(name.Text)) { nameError.Text = Loc.T("Use letters, digits and _; do not start with a digit."); return; }
                nameError.Text = ""; Insert("(?<" + name.Text + ">", ")");
            }),
            Ui.Field("Backreference", reference, "Matches the same text a group already captured: \\1 for group 1, \\k<name> for a named group. Uses the backtracking engine.", "1"),
            Small("Insert backreference", null, () =>
            {
                try { Insert(int.TryParse(reference.Text, out var n) ? RegexBuilderLibrary.Backreference(n) : RegexBuilderLibrary.Backreference(reference.Text.Trim()), ""); nameError.Text = ""; }
                catch (ArgumentException e) { nameError.Text = e.Message; }
            }));
    }

    private FrameworkElement RepeatTab()
    {
        var min = Ui.Input("1", "Minimum", true); min.Width = 80;
        var max = Ui.Input("", "Maximum (empty for no limit)", true); max.Width = 80;
        var lazy = Ui.Check("Lazy (as few as possible)", false);
        var error = Ui.Caption("");
        return Ui.VStack(8,
            TokenWrap("Quantifiers"),
            Ui.Field("Repeat count", Ui.HStack(8, min, Ui.Caption("to"), max, lazy), "Inserts {n}, {n,} or {n,m} after the previous item. Leave the maximum empty for no upper limit.", "2 to 4 gives {2,4}"),
            error,
            Small("Insert repeat count", null, () =>
            {
                try
                {
                    if (!int.TryParse(min.Text, out var lo)) throw new ArgumentException(Loc.T("Enter a whole number for the minimum."));
                    int? hi = max.Text.Trim().Length == 0 ? null : int.TryParse(max.Text, out var h) ? h : throw new ArgumentException(Loc.T("Enter a whole number for the maximum, or leave it empty."));
                    Insert(RegexBuilderLibrary.Quantifier(lo, hi, lazy.IsChecked == true), ""); error.Text = "";
                }
                catch (ArgumentException e) { error.Text = e.Message; }
            }));
    }

    private FrameworkElement LibraryTab()
    {
        var list = new StackPanel();
        foreach (var s in RegexBuilderLibrary.Snippets)
        {
            var snippet = s;
            var use = Small("Use", "Replace the pattern with this snippet" + (snippet.Flags != null ? " and its flags" : ""), () => Use(snippet.Pattern, snippet.Flags));
            var insert = Small("Insert", "Insert at the caret", () => Insert(snippet.Pattern, ""));
            var text = Ui.VStack(2,
                Ui.Text(snippet.Name, 14, FontWeights.SemiBold),
                Ui.Caption(snippet.Description),
                Ui.Mono(snippet.Pattern.Length > 120 ? snippet.Pattern[..120] + "…" : snippet.Pattern, 12, "OnSurfaceVariant", true),
                Ui.Caption(Loc.T("Example:") + " " + snippet.Example));
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            row.Children.Add(Ui.VStack(0, use, insert).Dock(Dock.Right));
            row.Children.Add(text);
            list.Children.Add(row);
        }
        return new ScrollViewer { Content = list, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private FrameworkElement MineTab()
    {
        var save = Small("Save to my patterns", "Stores the pattern and flags in regex-patterns.json in the app data folder (at most 200).", SaveCurrent);
        return Ui.VStack(8,
            Ui.Field("Name", saveName, "Saving is the only way the builder stores anything. Sample and replacement text are never saved.", "Short image IDs"),
            save, mineStatus,
            Ui.Overline("Saved patterns"), new ScrollViewer { Content = savedList, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            Ui.Overline("Recent in this field (this session only)"), recentList);
    }

    private FrameworkElement TesterSection()
    {
        var view = new Border { Child = new ScrollViewer { Content = highlighted, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, Padding = new Thickness(10), CornerRadius = new CornerRadius(8) };
        view.SetResourceReference(Border.BackgroundProperty, "SurfaceLowest");
        return Section("Test", $"Paste up to {RegexBuilderEngine.MaxSampleBytes / 1024} KiB of sample text. It stays in memory for this field only.",
            Ui.Field("Sample text", sample, "Matches are highlighted below. Zero-width matches (such as \\b) show as a thin marker.", "nginx:1.27 alpine:3.20"),
            testStatus, view, groups);
    }

    private FrameworkElement ReplaceSection() => Section("Replace preview", "Shows the sample after replacement. Nothing in the app is changed.",
        Ui.Field("Replacement", replacement, "Use $1 or ${name} for a group, $0 or $& for the whole match and $$ for a dollar sign.", "${name}:latest"),
        replaceStatus, replaceOutput);

    private FrameworkElement ActionsRow()
    {
        var wrap = new WrapPanel();
        foreach (var b in new[]
        {
            Ui.Button("Copy pattern", Icons.ContentCopy, ButtonKind.Tonal, () => Copy(field.Query, "Pattern copied.")),
            Ui.Button("Copy as C# string", Icons.Code, ButtonKind.Tonal, () => Copy(RegexBuilderEngine.ToCSharpVerbatim(field.Query), "C# verbatim string copied.")),
            Ui.Button("Export JSON…", Icons.FileExport, ButtonKind.Tonal, Export),
            Ui.Button("Done", null, ButtonKind.Filled, Close),
        })
        { b.Margin = new Thickness(0, 0, 8, 8); wrap.Children.Add(b); }
        return wrap;
    }

    private static FrameworkElement DialectNote() => Ui.Card(Ui.VStack(4,
        Ui.Overline("Dialect: .NET regular expressions"),
        Ui.Caption("Syntax of System.Text.RegularExpressions. To match one of \\ * + ? | { [ ( ) ^ $ . # or a space literally, put a backslash before it, or use Insert escaped text. Inside [ ] escape \\ ] [ ^ and -. Plain-text mode needs no escaping."),
        Ui.Caption($"Limits: patterns up to {RegexBuilderEngine.MaxPatternLength} characters, samples up to {RegexBuilderEngine.MaxSampleBytes / 1024} KiB, {RegexBuilderEngine.MaxHighlightedMatches} highlighted matches. The non-backtracking engine is used whenever the pattern allows it; otherwise each match is limited to {RegexBuilderEngine.MatchTimeout.TotalMilliseconds:0} ms. Tests run off the interface thread and are cancelled when you keep typing.")),
        12, "SurfaceContainer", 12);

    // ----- Behaviour -----

    private void Insert(string prefix, string suffix)
    {
        var start = pattern.SelectionStart;
        var selected = pattern.SelectedText;
        pattern.SelectedText = prefix + selected + suffix;
        pattern.Focus();
        pattern.Select(selected.Length == 0 ? start + prefix.Length : start + prefix.Length + selected.Length + suffix.Length, 0);
        if (!field.RegexMode) { field.RegexMode = true; modeSwitch.IsChecked = true; }
        Refresh();
    }

    private void Use(string text, RegexBuilderFlags? flags)
    {
        pattern.Text = text;
        if (flags != null) field.Flags = flags with { IgnoreCase = field.Flags.IgnoreCase };
        if (!field.RegexMode) { field.RegexMode = true; modeSwitch.IsChecked = true; }
        pattern.Focus(); pattern.CaretIndex = pattern.Text.Length;
        Refresh();
    }

    /// <summary>Synchronous part: validation, error underline, explanation, chips and lists. Then schedules the test.</summary>
    private void Refresh()
    {
        var text = field.Query;
        var flags = field.Flags;
        for (var i = 0; i < flagChips.Count; i++) { var on = flags.Get(RegexBuilderFlags.All[i].Name); if (flagChips[i].IsChecked != on) flagChips[i].IsChecked = on; }
        if (modeSwitch.IsChecked != field.RegexMode) modeSwitch.IsChecked = field.RegexMode;
        var compiled = text.Length == 0 ? null : RegexBuilderEngine.Compile(text, flags);
        errorView.Inlines.Clear();
        if (compiled is { Valid: false })
        {
            var at = Math.Clamp(compiled.ErrorIndex < 0 ? text.Length : compiled.ErrorIndex, 0, text.Length);
            var before = at > 0 ? text[..Math.Max(0, at - 1)] : "";
            var mark = at > 0 ? text.Substring(at - 1, 1) : text.Length > 0 ? text[..1] : " ";
            var after = at > 0 ? text[at..] : text.Length > 1 ? text[1..] : "";
            errorView.Inlines.Add(new Run(before));
            var bad = new Run(mark) { TextDecorations = TextDecorations.Underline, FontWeight = FontWeights.Bold };
            bad.SetResourceReference(TextElement.ForegroundProperty, "Error");
            errorView.Inlines.Add(bad);
            errorView.Inlines.Add(new Run(after));
            errorView.SetResourceReference(TextBlock.ForegroundProperty, "OnErrorContainer");
            errorView.Visibility = Visibility.Visible;
            validation.Text = compiled.Error ?? "";
            validation.SetResourceReference(TextBlock.ForegroundProperty, "Error");
            engine.Text = "";
        }
        else
        {
            errorView.Visibility = Visibility.Collapsed;
            validation.SetResourceReference(TextBlock.ForegroundProperty, "OnSurfaceVariant");
            validation.Text = text.Length == 0 ? Loc.T("Empty: the field shows everything.")
                : field.RegexMode ? Loc.T("Valid pattern.")
                : Loc.T("Regular expression mode is off: the field matches this text exactly as typed. The test below shows the same behaviour.");
            engine.Text = compiled == null || !field.RegexMode ? Loc.T("Engine: plain-text comparison.") : Loc.T("Engine:") + " " + RegexBuilderEngine.DescribeEngine(compiled.Engine);
        }
        RenderExplanation(text, flags);
        Schedule();
    }

    private void RenderExplanation(string text, RegexBuilderFlags flags)
    {
        explanation.Items.Clear();
        if (text.Length == 0) { explanation.Items.Add(new TreeViewItem { Header = Loc.T("Type or insert a pattern to see its explanation."), Focusable = false }); return; }
        var budget = 300;
        foreach (var node in RegexExplainer.Explain(text, flags)) { if (budget <= 0) break; explanation.Items.Add(Item(node, ref budget)); }
        if (budget <= 0) explanation.Items.Add(new TreeViewItem { Header = Loc.T("Explanation shortened after 300 tokens."), Focusable = false });
    }

    private static TreeViewItem Item(RegexExplainNode node, ref int budget)
    {
        budget--;
        var header = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 480 };
        if (node.Text.Length > 0)
        {
            var token = new Run(node.Text.Length > 60 ? node.Text[..60] + "…" : node.Text) { FontWeight = FontWeights.SemiBold };
            token.SetResourceReference(TextElement.FontFamilyProperty, "MonoFont");
            token.SetResourceReference(TextElement.ForegroundProperty, node.Family == RegexTokenFamily.Error ? "Error" : "Primary");
            header.Inlines.Add(token);
            header.Inlines.Add(new Run("  "));
        }
        var description = new Run(node.Description);
        description.SetResourceReference(TextElement.ForegroundProperty, node.Family == RegexTokenFamily.Error ? "Error" : "OnSurface");
        header.Inlines.Add(description);
        var item = new TreeViewItem { Header = header, Tag = node, IsExpanded = true };
        AutomationProperties.SetName(item, node.Text + ": " + node.Description);
        foreach (var child in node.Children) { if (budget <= 0) break; item.Items.Add(Item(child, ref budget)); }
        return item;
    }

    private void Schedule() { evaluation?.Cancel(); debounce.Stop(); debounce.Start(); }

    private async Task EvaluateAsync()
    {
        evaluation?.Cancel();
        var cts = evaluation = new CancellationTokenSource();
        var token = cts.Token;
        var regexMode = field.RegexMode;
        var text = regexMode ? field.Query : RegexBuilderLibrary.Literal(field.Query);
        var flags = regexMode ? field.Flags : new RegexBuilderFlags { IgnoreCase = field.Flags.IgnoreCase };
        var sampleText = field.State.Sample;
        var replaceText = field.State.Replacement;
        if (field.Query.Length == 0 || sampleText.Length == 0)
        {
            highlighted.Inlines.Clear(); highlighted.Text = sampleText.Length == 0 ? "" : RegexBuilderEngine.BoundSample(sampleText, out _);
            testStatus.Text = field.Query.Length == 0 ? Loc.T("Enter a pattern to test.") : Loc.T("Paste sample text to test the pattern.");
            groups.ItemsSource = null; groups.Visibility = Visibility.Collapsed;
            replaceOutput.Text = ""; replaceStatus.Text = "";
            return;
        }
        testStatus.Text = Loc.T("Testing…");
        try
        {
            var (result, preview) = await Task.Run(() =>
            {
                var r = RegexBuilderEngine.Evaluate(text, flags, sampleText, token);
                var p = replaceText.Length > 0 && r.Valid ? RegexBuilderEngine.Replace(text, flags, sampleText, replaceText, token) : null;
                return (r, p);
            }, token);
            if (token.IsCancellationRequested || !popup.IsOpen) return;
            RenderTest(result);
            RenderReplace(preview, replaceText.Length > 0);
        }
        catch (OperationCanceledException) { }
    }

    private void RenderTest(RegexEvaluation result)
    {
        testStatus.Text = result.Summary() + (result.Valid && !result.Cancelled ? $" {result.ElapsedMilliseconds:0} ms." : "");
        highlighted.Inlines.Clear();
        if (!result.Valid) { highlighted.Text = result.Sample; groups.Visibility = Visibility.Collapsed; return; }
        var text = result.Sample;
        var at = 0;
        foreach (var m in result.Matches)
        {
            if (m.Index < at) continue;
            if (m.Index > at) highlighted.Inlines.Add(new Run(text[at..m.Index]));
            if (m.ZeroWidth)
            {
                var marker = new Run("│");
                marker.SetResourceReference(TextElement.ForegroundProperty, "Tertiary");
                highlighted.Inlines.Add(marker);
            }
            else
            {
                var end = Math.Min(text.Length, m.Index + m.Length);
                var run = new Run(text[m.Index..end]);
                run.SetResourceReference(TextElement.BackgroundProperty, "PrimaryContainer");
                run.SetResourceReference(TextElement.ForegroundProperty, "OnPrimaryContainer");
                highlighted.Inlines.Add(run);
                at = end;
            }
        }
        if (at < text.Length) highlighted.Inlines.Add(new Run(text[at..]));
        var rows = new List<RegexBuilderGroupRow>();
        for (var i = 0; i < result.Matches.Count && rows.Count < 500; i++)
        {
            var m = result.Matches[i];
            rows.Add(new(i + 1, 0, "(whole match)", m.Value, m.Index));
            foreach (var g in m.Groups) { if (rows.Count >= 500) break; rows.Add(new(i + 1, g.Number, g.Name, g.Success ? g.Value : "(did not participate)", g.Success ? g.Index : -1)); }
        }
        groups.ItemsSource = rows;
        groups.Visibility = rows.Count > 0 && result.Matches.Any(m => m.Groups.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderReplace(RegexReplacePreview? preview, bool requested)
    {
        if (!requested || preview == null) { replaceOutput.Text = ""; replaceStatus.Text = requested ? Loc.T("Correct the pattern to preview replacements.") : Loc.T("Type a replacement to preview it."); return; }
        replaceOutput.Text = preview.Output;
        var status = preview.Error ?? $"{preview.Replacements} replacement{(preview.Replacements == 1 ? "" : "s")}" + (preview.OutputTruncated ? $"; output cut to {RegexBuilderEngine.MaxReplaceOutputChars / 1024} KiB" : "") + ".";
        if (preview.Warnings.Count > 0) status += " " + string.Join(" ", preview.Warnings);
        replaceStatus.Text = status;
    }

    private void RenderMine()
    {
        recentList.Children.Clear();
        if (field.State.Recent.Items.Count == 0) recentList.Children.Add(Ui.Caption("Valid patterns appear here when you close the builder in regular expression mode."));
        foreach (var r in field.State.Recent.Items)
        {
            var value = r;
            recentList.Children.Add(Row(value, null, () => Use(value, null), null));
        }
        savedList.Children.Clear();
        if (saved == null)
        {
            try { saved = RegexPatternStore.Load(RegexPatternStore.DefaultPath); }
            catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException) { saved = new(); mineStatus.Text = Loc.T("Saved patterns could not be read:") + " " + e.Message; }
        }
        if (saved.Count == 0) savedList.Children.Add(Ui.Caption("No saved patterns yet."));
        foreach (var s in saved)
        {
            var entry = s;
            savedList.Children.Add(Row(entry.Pattern, entry.Name, () => Use(entry.Pattern, entry.Flags), () => Persist(RegexPatternStore.Remove(saved, entry.Name), "Removed " + entry.Name + ".")));
        }
    }

    private FrameworkElement Row(string value, string? name, Action use, Action? remove)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        if (remove != null) row.Children.Add(Ui.IconButton(Icons.Delete, "Remove " + name, remove).Dock(Dock.Right));
        row.Children.Add(Small("Use", "Replace the pattern with this one", use).Dock(Dock.Right));
        row.Children.Add(Ui.VStack(0, name == null ? null : Ui.Text(name, 13, FontWeights.SemiBold, "OnSurface", true, false), Ui.Mono(value.Length > 100 ? value[..100] + "…" : value, 12, "OnSurfaceVariant", true)));
        return row;
    }

    private void SaveCurrent()
    {
        var nameBox = saveName;
        try
        {
            var entry = new RegexSavedPattern(nameBox.Text, field.Query, field.Flags, DateTimeOffset.Now);
            Persist(RegexPatternStore.Add(saved ?? new(), entry), "Saved " + nameBox.Text.Trim() + " to my patterns.");
            nameBox.Clear();
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { mineStatus.Text = e.Message; }
    }

    private void Persist(List<RegexSavedPattern> next, string message)
    {
        try { RegexPatternStore.Save(RegexPatternStore.DefaultPath, next); saved = next; mineStatus.Text = Loc.T(message); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { mineStatus.Text = Loc.T("Could not save my patterns:") + " " + e.Message; }
        RenderMine();
    }

    private void Copy(string text, string message)
    {
        if (text.Length == 0) { testStatus.Text = Loc.T("There is no pattern to copy."); return; }
        try { ActionSounds.Copy(text); testStatus.Text = Loc.T(message); }
        catch (ExternalException) { testStatus.Text = Loc.T("The clipboard is busy. Try again."); }
    }

    private void Export()
    {
        var json = RegexBuilderEngine.ExportJson(field.Query, field.Flags, field.State.Sample, field.State.Replacement.Length > 0 ? field.State.Replacement : null);
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "JSON|*.json", FileName = "pattern.json", Title = Loc.T("Export pattern") };
        Close(); // a modal dialog would dismiss the popover anyway; state stays in the field
        if (dialog.ShowDialog() != true) return;
        try { File.WriteAllText(dialog.FileName, json); AppHost.Current.Notify.Success("Pattern exported to " + dialog.FileName + "."); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { AppHost.Current.Notify.Error("The pattern could not be exported: " + e.Message); }
    }
}

/// <summary>Row of the capture groups table.</summary>
public sealed record RegexBuilderGroupRow(int Match, int Group, string Name, string Value, int Position);
