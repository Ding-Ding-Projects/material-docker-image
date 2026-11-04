using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MaterialDockerImage.Services;

namespace MaterialDockerImage.UI;

/// <summary>
/// The last wave of builder features, as view code only: panel layout and preferences, the keyboard map, editing through the explanation tree, editable code
/// templates, the folder search extras (date window, context lines, saved result lists, opening a result in an editor), action files and the engine version
/// pickers. The logic lives in RegexBuilderTreeEdit.cs, RegexBuilderTemplates.cs, RegexBuilderPreferences.cs, RegexBuilderGrepResults.cs, RegexBuilderActions.cs
/// and RegexBuilderFlavorVersions.cs; none of this view code has been exercised in a running window.
/// </summary>
internal sealed partial class SearchBuilderPopover
{
    // ----- nested search fields -----

    private readonly List<SearchFilter> nestedFilters = new();

    /// <summary>A filter inside this builder carries its own builder, which opens as a second popup. While one is open this popup must not close when the pointer moves to it.</summary>
    private void RegisterNested(SearchFilter nested)
    {
        nestedFilters.Add(nested);
        nested.BuilderStateChanged += () => popup.StaysOpen = nestedFilters.Any(f => f.BuilderOpen);
    }

    // ----- panels, layout and keyboard -----

    private readonly Dictionary<string, FrameworkElement> panelElements = new();
    private StackPanel? contentStack;
    private FrameworkElement? headerElement, actionsElement, noteElement;
    private RegexBuilderPreferences prefs = RegexBuilderPreferences.Current;
    private readonly TextBlock prefsStatus = Ui.Caption("");
    private readonly StackPanel layoutList = new();
    private readonly TextBox keyboardHelp = Ui.CodeBox("", 60);

    private void RegisterPanel(string id, FrameworkElement element)
    {
        AutomationProperties.SetName(element, Loc.T(RegexPanelLayout.Panel(id)?.Title ?? id));
        panelElements[id] = element;
    }

    /// <summary>Rebuilds the content in the saved panel order, leaving out the panels that are hidden. Required panels always stay.</summary>
    private void ApplyLayout()
    {
        if (contentStack == null) return;
        contentStack.Children.Clear();
        contentStack.Children.Add(headerElement ??= Header());
        contentStack.Children.Add(modeSwitch);
        foreach (var id in prefs.Layout.Visible()) if (panelElements.TryGetValue(id, out var element)) contentStack.Children.Add(element);
        contentStack.Children.Add(actionsElement ??= ActionsRow());
        contentStack.Children.Add(noteElement ??= DialectNote());
        RenderLayoutList();
        keyboardHelp.Text = LocalKeyboardHelp(prefs.Layout.Visible());
    }

    private static string LocalKeyboardHelp(IReadOnlyList<string> shown)
    {
        var lines = RegexKeyboard.Map.Select(m => m.Gesture + ": " + Loc.T(m.Action)).ToList();
        lines.Add("");
        lines.Add(Loc.T("Panels shown, in order:"));
        for (var i = 0; i < shown.Count; i++) lines.Add("  " + (RegexKeyboard.DirectGesture(i) ?? "F6") + "  " + Loc.T(RegexPanelLayout.Panel(shown[i])?.Title ?? shown[i]));
        return string.Join(Environment.NewLine, lines);
    }

    private string? CurrentPanel()
    {
        DependencyObject? at = Keyboard.FocusedElement as DependencyObject;
        while (at != null)
        {
            foreach (var (id, element) in panelElements) if (ReferenceEquals(element, at)) return id;
            at = at is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at);
        }
        return null;
    }

    private void JumpToPanel(string id)
    {
        if (!panelElements.TryGetValue(id, out var element)) return;
        element.BringIntoView();
        if (id == "pattern") { pattern.Focus(); Keyboard.Focus(pattern); return; }
        element.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private static string? GestureOf(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        if (key == Key.F6) return modifiers == ModifierKeys.Shift ? "Shift+F6" : modifiers == ModifierKeys.None ? "F6" : null;
        if (modifiers == ModifierKeys.Control && key is >= Key.D0 and <= Key.D9) return "Ctrl+" + (key - Key.D0);
        if (modifiers == ModifierKeys.Control && key is >= Key.NumPad0 and <= Key.NumPad9) return "Ctrl+" + (key - Key.NumPad0);
        if (modifiers == ModifierKeys.Alt && key == Key.Home) return "Alt+Home";
        return null;
    }

    private void InitFinish()
    {
        pattern.FontSize = prefs.PatternFontSize;
        keyboardHelp.IsReadOnly = true;
        AutomationProperties.SetName(keyboardHelp, Loc.T("Keyboard shortcuts of the builder"));
        prefsStatus.SetValue(AutomationProperties.LiveSettingProperty, AutomationLiveSetting.Polite);
        treeEditStatus.SetValue(AutomationProperties.LiveSettingProperty, AutomationLiveSetting.Polite);
        templateStatus.SetValue(AutomationProperties.LiveSettingProperty, AutomationLiveSetting.Polite);
        grepExtraStatus.SetValue(AutomationProperties.LiveSettingProperty, AutomationLiveSetting.Polite);
        actionStatus.SetValue(AutomationProperties.LiveSettingProperty, AutomationLiveSetting.Polite);
        card.PreviewKeyDown += (_, e) =>
        {
            var gesture = GestureOf(e);
            if (gesture == null) return;
            var target = RegexKeyboard.Target(gesture, prefs.Layout.Visible(), CurrentPanel());
            if (target == null) return;
            JumpToPanel(target);
            e.Handled = true;
        };
        if (grepRecursive != null) grepRecursive.IsChecked = field.State.GrepRecursive;
        if (grepContextCombo != null) grepContextCombo.SelectedItem = field.State.GrepContext.ToString();
        if (field.State.LoadedResults is { } loaded)
        {
            grepMatches = loaded.Matches;
            grepGrid.ItemsSource = loaded.Matches.Select(m => new RegexGrepRow(SafeRelative(loaded.Folder, m.File), m.Line, m.Column, m.Text, m.LineText)).ToList();
            grepGrid.Visibility = loaded.Matches.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            grepStatus.Text = $"{Loc.T("Saved result list:")} {loaded.Matches.Count} · {loaded.Pattern} · {(loaded.Saved == DateTimeOffset.MinValue ? Loc.T("save time unknown") : loaded.Saved.ToLocalTime().ToString("yyyy-MM-dd HH:mm"))}";
        }
        grepGrid.SelectionChanged += (_, _) => ShowGrepContext();
        grepGrid.MouseDoubleClick += (_, _) => OpenSelectedResult();
        grepGrid.KeyDown += (_, e) => { if (e.Key == Key.Enter) { OpenSelectedResult(); e.Handled = true; } };
    }

    // ----- preferences and layout panel -----

    private FrameworkElement PreferencesSection()
    {
        var flagBoxes = new WrapPanel();
        foreach (var (name, _, description) in RegexBuilderFlags.All)
        {
            var flagName = name;
            var box = Ui.Check(name, prefs.DefaultFlags.Get(name), v =>
            {
                var next = prefs.DefaultFlags.With(flagName, v);
                if (next.NonBacktracking && next.RightToLeft) { prefsStatus.Text = Loc.T("NonBacktracking and RightToLeft cannot both be default flags."); RenderPrefsAfterRefusal(); return; }
                prefs = prefs with { DefaultFlags = next };
            });
            box.ToolTip = Loc.T(description);
            box.Margin = new Thickness(0, 0, 12, 6);
            flagBoxes.Children.Add(box);
        }
        var timeout = Ui.Input(prefs.MatchTimeoutMs.ToString(System.Globalization.CultureInfo.InvariantCulture), "Time limit per match (ms)", true);
        var size = Ui.Combo(Enumerable.Range((int)RegexBuilderPreferences.MinFontSize, (int)(RegexBuilderPreferences.MaxFontSize - RegexBuilderPreferences.MinFontSize) + 1).Select(i => i.ToString()), ((int)prefs.PatternFontSize).ToString());
        var detail = Ui.Combo(RegexBuilderPreferences.Details, prefs.ExplanationDetail);
        AutomationProperties.SetName(size, Loc.T("Font size of the pattern box"));
        AutomationProperties.SetName(detail, Loc.T("Default explanation detail"));
        var save = Small("Save preferences", "Remembers these preferences for every search field, from the next search field that is created. The match time limit applies to patterns compiled from now on.", () =>
        {
            if (!int.TryParse(timeout.Text, out var ms) || ms < RegexBuilderEngine.MinTimeoutMs || ms > RegexBuilderEngine.MaxTimeoutMs)
            {
                prefsStatus.Text = string.Format(Loc.T("The time limit must be a whole number between {0} and {1}."), RegexBuilderEngine.MinTimeoutMs, RegexBuilderEngine.MaxTimeoutMs);
                return;
            }
            prefs = prefs with { MatchTimeoutMs = ms, PatternFontSize = double.Parse(size.SelectedItem as string ?? "13"), ExplanationDetail = detail.SelectedItem as string ?? "Full" };
            PersistPreferences("Preferences saved.");
            pattern.FontSize = prefs.PatternFontSize;
        });
        var reset = Small("Reset preferences", "Goes back to the built-in defaults, including the panel layout, and removes the saved preferences.", () =>
        {
            prefs = new RegexBuilderPreferences();
            RegexBuilderPreferences.Apply(prefs);
            try { if (File.Exists(RegexBuilderPreferences.DefaultPath)) File.Delete(RegexBuilderPreferences.DefaultPath); prefsStatus.Text = Loc.T("Preferences were reset."); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { prefsStatus.Text = Loc.T("The saved preferences could not be removed:") + " " + e.Message; }
            pattern.FontSize = prefs.PatternFontSize;
            timeout.Text = prefs.MatchTimeoutMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
            size.SelectedItem = ((int)prefs.PatternFontSize).ToString();
            detail.SelectedItem = prefs.ExplanationDetail;
            foreach (var cb in flagBoxes.Children.OfType<CheckBox>()) cb.IsChecked = prefs.DefaultFlags.Get(RegexBuilderFlags.All[flagBoxes.Children.IndexOf(cb)].Name);
            ApplyLayout();
        });
        if (RegexBuilderPreferences.LoadProblem != null) prefsStatus.Text = Loc.T("Saved preferences could not be read, so the defaults are used:") + " " + RegexBuilderPreferences.LoadProblem;
        return Section("Preferences and layout", "Choose which panels are shown and in what order, and the defaults for new search fields. Everything is remembered for this user on this computer. The pattern and these two panels cannot be hidden.",
            Ui.Overline("Default flags for new search fields"), flagBoxes,
            Ui.Field("Time limit per match (ms)", timeout, $"Between {RegexBuilderEngine.MinTimeoutMs} and {RegexBuilderEngine.MaxTimeoutMs}. A match that takes longer is stopped and reported.", "250"),
            Ui.Field("Font size of the pattern box", size, "Applies to the pattern box of this builder.", "13"),
            Ui.Field("Default explanation detail", detail, "Used by the explanation of a builder opened from now on.", "Full"),
            Ui.Wrap(8, save, reset), prefsStatus,
            Ui.Overline("Panels"), layoutList,
            Ui.Overline("Keyboard shortcuts"), keyboardHelp);
    }

    private void RenderPrefsAfterRefusal() { }

    private void PersistPreferences(string message)
    {
        try { RegexBuilderPreferences.Apply(prefs); prefs.Save(RegexBuilderPreferences.DefaultPath); prefsStatus.Text = Loc.T(message); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { prefsStatus.Text = Loc.T("The preferences could not be saved:") + " " + e.Message; }
    }

    private void RenderLayoutList()
    {
        layoutList.Children.Clear();
        var order = prefs.Layout.Normalize();
        foreach (var id in order)
        {
            var panel = RegexPanelLayout.Panel(id)!;
            var panelId = id;
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
            var down = Ui.IconButton(Icons.ExpandMore, "Move " + panel.Title + " down", () => ChangeLayout(prefs.Layout.Move(panelId, 1)));
            var up = Ui.IconButton(Icons.ExpandLess, "Move " + panel.Title + " up", () => ChangeLayout(prefs.Layout.Move(panelId, -1)));
            down.Dock(Dock.Right); up.Dock(Dock.Right);
            row.Children.Add(down); row.Children.Add(up);
            var shown = Ui.Check(panel.Title, !prefs.Layout.IsHidden(id), v =>
            {
                var (layout, problem) = prefs.Layout.SetHidden(panelId, !v);
                if (problem != null) { prefsStatus.Text = RegexText.Sentences(problem, Loc.T); RenderLayoutList(); return; }
                ChangeLayout(layout);
            });
            shown.IsEnabled = !panel.Required;
            AutomationProperties.SetName(shown, Loc.T(panel.Title) + (panel.Required ? ", " + Loc.T("always shown") : ""));
            row.Children.Add(shown);
            layoutList.Children.Add(row);
        }
    }

    private void ChangeLayout(RegexPanelLayout layout)
    {
        prefs = prefs with { Layout = layout };
        ApplyLayout();
        PersistPreferences("Layout saved.");
    }

    // ----- editing through the explanation tree -----

    private readonly TextBlock treeEditStatus = Ui.Caption("");
    private readonly TextBox quantifierBox = Ui.Input("", "Quantifier", true);
    private ComboBox? explanationEditCombo;

    // Created on first use: the explanation panel is built before InitFinish runs, and a null child made WPF throw
    // "Value cannot be null (Parameter 'element')", so the builder could not open at all.
    private ComboBox ExplanationEditCombo()
    {
        if (explanationEditCombo != null) return explanationEditCombo;
        explanationEditCombo = Ui.Combo(["Capturing group", "Non-capturing group", "Atomic group", "Lookahead", "Negative lookahead", "Lookbehind", "Negative lookbehind"], "Non-capturing group");
        AutomationProperties.SetName(explanationEditCombo, Loc.T("Wrap in"));
        return explanationEditCombo;
    }

    private FrameworkElement TreeEditControls()
    {
        AutomationProperties.SetName(quantifierBox, Loc.T("Quantifier for the selected row"));
        return Ui.VStack(6,
            Ui.Overline("Edit through the tree"),
            Ui.Caption("Select a row, then change the pattern from here. Each edit rewrites the pattern text, checks that it still compiles, and keeps your selection."),
            Ui.Field("Quantifier for the selected row", quantifierBox, "One of ? * + {n} {n,} {n,m}, optionally followed by ? for lazy. Leave it empty and press Set to remove the quantifier.", "{2,5}?"),
            Ui.Wrap(8,
                Small("?", "Optional (zero or one)", () => ApplyQuantifier("?"), true), Small("*", "Zero or more", () => ApplyQuantifier("*"), true), Small("+", "One or more", () => ApplyQuantifier("+"), true),
                Small("Set quantifier", "Applies the quantifier typed above to the selected row.", () => ApplyQuantifier(quantifierBox.Text)),
                Small("Remove quantifier", "Removes the quantifier of the selected row.", () => ApplyQuantifier(""))),
            Ui.Wrap(8, Ui.Field("Wrap in", ExplanationEditCombo(), "Wraps the selected row, with its quantifier, in a group or lookaround.", "Non-capturing group"),
                Small("Wrap selected row", "Wraps the selected row in the chosen kind of group.", WrapSelected),
                Small("Unwrap group", "Removes the selected group and keeps what is inside it.", () => TreeEdit((p, f, n) => RegexTreeEditor.Unwrap(p, f, n.Start, n.Length))),
                Small("Delete row", "Deletes the selected row from the pattern.", () => TreeEdit((p, f, n) => RegexTreeEditor.Delete(p, f, n.Start, n.Length)))),
            treeEditStatus);
    }

    private void ApplyQuantifier(string quantifier) => TreeEdit((p, f, n) => RegexTreeEditor.SetQuantifier(p, f, n.Start, n.Length, quantifier));

    private void WrapSelected()
    {
        var kind = (explanationEditCombo?.SelectedItem as string) switch
        {
            "Capturing group" => RegexTreeWrap.Capturing, "Atomic group" => RegexTreeWrap.Atomic, "Lookahead" => RegexTreeWrap.PositiveLookahead,
            "Negative lookahead" => RegexTreeWrap.NegativeLookahead, "Lookbehind" => RegexTreeWrap.PositiveLookbehind, "Negative lookbehind" => RegexTreeWrap.NegativeLookbehind,
            _ => RegexTreeWrap.NonCapturing,
        };
        TreeEdit((p, f, n) => RegexTreeEditor.Wrap(p, f, n.Start, n.Length, kind));
    }

    private void TreeEdit(Func<string, RegexBuilderFlags, RegexExplainNode, RegexTreeEditResult> edit)
    {
        if (!field.RegexMode) { treeEditStatus.Text = Loc.T("Turn on regular expression mode to edit the pattern through the tree."); return; }
        if (explanation.SelectedItem is not TreeViewItem { Tag: RegexExplainNode node }) { treeEditStatus.Text = Loc.T("Select a row of the explanation first."); return; }
        var result = edit(field.Query, field.Flags, node);
        treeEditStatus.Text = RegexText.Sentences(result.Message, Loc.T);
        if (!result.Success) return;
        pattern.Text = result.Pattern;
        pattern.Focus();
        pattern.Select(Math.Clamp(result.SelectStart, 0, pattern.Text.Length), Math.Clamp(result.SelectLength, 0, pattern.Text.Length - Math.Clamp(result.SelectStart, 0, pattern.Text.Length)));
    }

    // ----- code templates -----

    private Dictionary<string, string>? templates;
    private ComboBox? templateLanguage, templateKind;
    private readonly TextBox templateBox = Ui.CodeBox("", 90);
    private readonly TextBox templatePreview = Ui.CodeBox("", 60);
    private readonly TextBlock templateStatus = Ui.Caption("");

    private Dictionary<string, string> Templates()
    {
        if (templates != null) return templates;
        try { templates = RegexCodeTemplates.Load(RegexCodeTemplates.DefaultPath); }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            templates = new();
            templateStatus.Text = Loc.T("Saved templates could not be read, so the built-in ones are used:") + " " + e.Message;
        }
        return templates;
    }

    private static RegexCodeKind KindOf(string? label) => label switch { "Test for a match" => RegexCodeKind.IsMatch, "Replace" => RegexCodeKind.Replace, "Split" => RegexCodeKind.Split, _ => RegexCodeKind.Matches };

    private FrameworkElement TemplatesSection()
    {
        templateLanguage = Ui.Combo(RegexCodeTemplates.Languages, RegexCodeTemplates.Languages[0]);
        templateKind = Ui.Combo(new[] { "Find all matches", "Test for a match", "Replace", "Split" }, "Find all matches");
        AutomationProperties.SetName(templateLanguage, Loc.T("Language"));
        AutomationProperties.SetName(templateKind, Loc.T("Code to generate"));
        AutomationProperties.SetName(templateBox, Loc.T("Template text"));
        AutomationProperties.SetName(templatePreview, Loc.T("Preview of the template with the current pattern"));
        templatePreview.IsReadOnly = true;
        templateLanguage.SelectionChanged += (_, _) => LoadTemplateText();
        templateKind.SelectionChanged += (_, _) => LoadTemplateText();
        templateBox.TextChanged += (_, _) => RenderTemplatePreview();
        LoadTemplateText();
        var placeholders = Ui.VStack(2, RegexCodeTemplates.Placeholders.Select(p => (UIElement)Ui.Mono("{{" + p.Name + "}}  " + Loc.T(p.Meaning), 12, "OnSurfaceVariant", true)).ToArray());
        return Section("Code templates", "The shape of the code the Convert section generates for C#, PowerShell, JavaScript, Python, Java, Go and PHP. Your template replaces the built-in one for that language and kind; the pattern is still quoted for the language, and a pattern the language cannot express is still refused.",
            Ui.Wrap(8, Ui.Field("Language", templateLanguage), Ui.Field("Code to generate", templateKind)),
            Ui.Field("Template text", templateBox, "Plain text with placeholders. At least one placeholder is needed, usually {{pattern}}.", "const ok = {{pattern}}.test(input);"),
            placeholders,
            Ui.Wrap(8,
                Small("Save template", "Stores this template for the language and kind, replacing the built-in one.", SaveTemplate),
                Small("Reset to built-in", "Removes your template for the language and kind.", ResetTemplate),
                Small("Copy preview", null, () => { if (templatePreview.Text.Length > 0) Copy(templatePreview.Text, "Template preview copied."); })),
            templateStatus, Ui.Overline("Preview with the current pattern"), templatePreview);
    }

    private (string Language, RegexCodeKind Kind) TemplateTarget() => (templateLanguage?.SelectedItem as string ?? RegexCodeTemplates.Languages[0], KindOf(templateKind?.SelectedItem as string));

    private void LoadTemplateText()
    {
        var (language, kind) = TemplateTarget();
        templateBox.Text = Templates().TryGetValue(RegexCodeTemplates.Key(language, kind), out var own) ? own : RegexCodeTemplates.Default(language, kind) ?? "";
        RenderTemplatePreview();
    }

    private void RenderTemplatePreview()
    {
        var (language, kind) = TemplateTarget();
        var problem = RegexCodeTemplates.Validate(templateBox.Text);
        if (field.Query.Length == 0) { templatePreview.Text = ""; if (problem != null) templateStatus.Text = RegexText.Sentences(problem, Loc.T); return; }
        templateStatus.Text = problem == null ? "" : RegexText.Sentences(problem, Loc.T);
        if (problem != null) { templatePreview.Text = ""; return; }
        var (text, flags) = Effective();
        var preview = new Dictionary<string, string> { [RegexCodeTemplates.Key(language, kind)] = templateBox.Text };
        var result = RegexCodeTemplates.Render(language, text, flags, kind, field.State.Replacement, preview);
        templatePreview.Text = result.Text;
        if (!result.Converted) templateStatus.Text = string.Join(" ", result.Notes.Select(n => RegexConverter.TranslateNote(n, Loc.T)));
    }

    private void SaveTemplate()
    {
        var (language, kind) = TemplateTarget();
        try
        {
            var next = RegexCodeTemplates.With(Templates(), language, kind, templateBox.Text);
            RegexCodeTemplates.Save(RegexCodeTemplates.DefaultPath, next);
            templates = next;
            templateStatus.Text = Loc.T("Template saved.");
            RenderConversion();
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException) { templateStatus.Text = RegexText.Sentences(e.Message, Loc.T); }
    }

    private void ResetTemplate()
    {
        var (language, kind) = TemplateTarget();
        try
        {
            var next = RegexCodeTemplates.Without(Templates(), language, kind);
            RegexCodeTemplates.Save(RegexCodeTemplates.DefaultPath, next);
            templates = next;
            LoadTemplateText();
            templateStatus.Text = Loc.T("The built-in template is used again.");
            RenderConversion();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { templateStatus.Text = Loc.T(e.Message); }
    }

    // ----- folder search extras -----

    private readonly TextBox grepAfter = Ui.Input("", "Modified after (optional)", true);
    private readonly TextBox grepBefore = Ui.Input("", "Modified before (optional)", true);
    private ComboBox? grepContextCombo;
    private CheckBox? grepHidden, grepReadOnly, grepSystem;
    private ComboBox? grepEncodingCombo;
    private static readonly (string Label, RegexGrepEncoding Value)[] EncodingChoices =
    [
        ("Auto (UTF-8)", RegexGrepEncoding.Auto), ("UTF-16 little endian", RegexGrepEncoding.Utf16Le), ("UTF-16 big endian", RegexGrepEncoding.Utf16Be),
        ("Windows-1252", RegexGrepEncoding.Windows1252), ("Latin-1 (ISO 8859-1)", RegexGrepEncoding.Latin1),
    ];
    private readonly TextBlock grepExtraStatus = Ui.Caption("");
    private readonly TextBlock grepContext = new() { TextWrapping = TextWrapping.Wrap };
    private IReadOnlyList<RegexGrepMatch> grepMatches = [];
    private RegexGrepResult? lastGrepResult;
    private RegexGrepOptions? lastGrepOptions;

    private FrameworkElement GrepExtras()
    {
        grepContextCombo = Ui.Combo(Enumerable.Range(0, RegexGrep.MaxContextLines + 1).Select(i => i.ToString()), "0");
        AutomationProperties.SetName(grepContextCombo, Loc.T("Context lines"));
        grepHidden = Ui.Check("Include hidden files and folders", field.State.GrepIncludeHidden);
        grepReadOnly = Ui.Check("Include read-only files", field.State.GrepIncludeReadOnly);
        grepSystem = Ui.Check("Include system files and folders", field.State.GrepIncludeSystem);
        grepEncodingCombo = Ui.Combo(EncodingChoices.Select(c => c.Label), EncodingChoices.First(c => c.Value == field.State.GrepEncoding).Label);
        AutomationProperties.SetName(grepEncodingCombo, Loc.T("Encoding for files without a byte order mark"));
        grepContext.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        grepContext.SetResourceReference(TextBlock.ForegroundProperty, "OnSurfaceVariant");
        return Ui.VStack(6,
            Ui.Wrap(8,
                Ui.Field("Modified after", grepAfter, "Only files last written at or after this local time are searched. Write 2026-11-04 or 2026-11-04 13:30. Empty means no limit.", "2026-11-01"),
                Ui.Field("Modified before", grepBefore, "Only files last written at or before this local time. A date without a time covers that whole day.", "2026-11-04")),
            Ui.Wrap(8, grepHidden, grepReadOnly, grepSystem),
            Ui.Field("Encoding for files without a byte order mark", grepEncodingCombo, "How a file with no byte order mark is read and written back. A byte order mark (UTF-8, UTF-16 or UTF-32) always wins. Auto means UTF-8. A replacement writes each file back in the encoding it was read in. A read-only file is never edited, whatever this says.", "Auto (UTF-8)"),
            Ui.Field("Context lines", grepContextCombo, "Lines shown before and after each match when you select a result, and written to saved result lists. At most 5.", "2"),
            Ui.Wrap(8,
                Small("Save results…", "Saves the result list as JSON that can be loaded again, as text with context lines, or as CSV.", SaveResults),
                Small("Load results…", "Shows a result list that was saved earlier. The files are not read again.", LoadResults),
                Small("Open selected result", "Opens the selected result in the external editor from Settings, or in Visual Studio Code when none is set, at its line when the editor is known. Double-click or Enter on a result does the same.", OpenSelectedResult)),
            grepExtraStatus, grepContext);
    }

    private (RegexGrepOptions? Options, string? Problem) ExtraOptions()
    {
        var (after, p1) = RegexGrepResults.ParseDate(grepAfter.Text, false);
        var (before, p2) = RegexGrepResults.ParseDate(grepBefore.Text, true);
        if (p1 != null) return (null, Loc.T("Modified after:") + " " + RegexText.Sentences(p1, Loc.T));
        if (p2 != null) return (null, Loc.T("Modified before:") + " " + RegexText.Sentences(p2, Loc.T));
        var context = int.TryParse(grepContextCombo?.SelectedItem as string, out var c) ? c : 0;
        var encoding = EncodingChoices.FirstOrDefault(c => c.Label == grepEncodingCombo?.SelectedItem as string).Value;
        return (new RegexGrepOptions
        {
            ModifiedAfterUtc = after, ModifiedBeforeUtc = before, ContextLines = context, BomlessEncoding = encoding,
            IncludeHidden = grepHidden?.IsChecked != false, IncludeReadOnly = grepReadOnly?.IsChecked != false, IncludeSystem = grepSystem?.IsChecked != false,
        }, null);
    }

    private void ShowGrepContext()
    {
        var index = grepGrid.SelectedIndex;
        if (index < 0 || index >= grepMatches.Count) { grepContext.Text = ""; return; }
        var m = grepMatches[index];
        var lines = new List<string>();
        var first = m.Line - m.ContextBefore.Count;
        for (var i = 0; i < m.ContextBefore.Count; i++) lines.Add($"{first + i,5}  {m.ContextBefore[i]}");
        lines.Add($"{m.Line,5}: {m.LineText}");
        for (var i = 0; i < m.ContextAfter.Count; i++) lines.Add($"{m.Line + 1 + i,5}  {m.ContextAfter[i]}");
        grepContext.Text = string.Join("\n", lines);
    }

    private async void OpenSelectedResult()
    {
        var index = grepGrid.SelectedIndex;
        if (index < 0 || index >= grepMatches.Count) { grepExtraStatus.Text = Loc.T("Select a result first."); return; }
        var m = grepMatches[index];
        grepExtraStatus.Text = Loc.T("Opening the editor…");
        var problem = await Pages.ExternalEditor.OpenAsync(m.File, AppHost.Current.Settings.ExternalEditor, m.Line, m.Column);
        grepExtraStatus.Text = problem == null ? Loc.T("The editor was started.") : Loc.T(problem);
    }

    private void SaveResults()
    {
        if (lastGrepResult == null || lastGrepResult.Matches.Count == 0) { grepExtraStatus.Text = Loc.T("There are no results to save. Run Search files first."); return; }
        var format = RegexGrepResults.Formats[0];
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "JSON (can be loaded again)|*.json|Text with context|*.txt|CSV|*.csv", FileName = "folder-search-results.json", Title = Loc.T("Save results") };
        var result = lastGrepResult; var options = lastGrepOptions ?? new RegexGrepOptions(); var (text, flags) = Effective();
        Close();
        if (dialog.ShowDialog() != true) return;
        format = dialog.FilterIndex switch { 2 => RegexGrepResults.Formats[1], 3 => RegexGrepResults.Formats[2], _ => format };
        try { File.WriteAllText(dialog.FileName, RegexGrepResults.Export(format, text, flags, options, result), new UTF8Encoding(false)); AppHost.Current.Notify.Success("Results saved to " + dialog.FileName + "."); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { AppHost.Current.Notify.Error("The results could not be saved: " + e.Message); }
    }

    private void LoadResults()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "JSON|*.json", Title = Loc.T("Load results") };
        Close();
        if (dialog.ShowDialog() != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 16 * 1024 * 1024) { AppHost.Current.Notify.Error("The file is larger than 16 MiB and was not read."); return; }
            var list = RegexGrepResults.Parse(File.ReadAllText(dialog.FileName, new UTF8Encoding(false, true)));
            field.State.LoadedResults = list;
            AppHost.Current.Notify.Success($"Loaded {list.Matches.Count} saved result{(list.Matches.Count == 1 ? "" : "s")} for {list.Pattern}.");
            field.Dispatcher.BeginInvoke(() => field.OpenBuilder());
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or DecoderFallbackException) { AppHost.Current.Notify.Error("The result list could not be loaded: " + e.Message); }
    }

    // ----- action files and command line -----

    private readonly TextBlock actionStatus = Ui.Caption("");

    private RegexSearchAction CurrentAction()
    {
        var (text, flags) = Effective();
        var (extra, _) = ExtraOptions();
        return new RegexSearchAction { Pattern = text, Flags = flags, Replacement = field.State.Replacement, Folder = field.State.GrepFolder, Globs = field.State.GrepGlobs, Recursive = grepRecursive?.IsChecked != false, ContextLines = extra?.ContextLines ?? 0, IncludeHidden = extra?.IncludeHidden ?? true, IncludeReadOnly = extra?.IncludeReadOnly ?? true, IncludeSystem = extra?.IncludeSystem ?? true, BomlessEncoding = extra?.BomlessEncoding ?? RegexGrepEncoding.Auto };
    }

    private FrameworkElement ActionFileControls() => Ui.VStack(6,
        Ui.Overline("Action file and command line"),
        Ui.Caption("An action file describes this search (pattern, flags, replacement, folder and file patterns) so it can be kept, shared and reopened. Opening one only fills in the builder: nothing is searched or replaced until you press the buttons."),
        Ui.Wrap(8,
            Small("Save action file…", "Saves this search as a small JSON file.", SaveAction),
            Small("Open action file…", "Fills in the builder from an action file.", OpenAction),
            Small("Copy command line", "Copies the arguments that start the application with this search already in the builder.", () =>
            {
                var action = CurrentAction();
                var problem = action.Problem();
                if (problem != null) { actionStatus.Text = RegexText.Sentences(problem, Loc.T); return; }
                Copy("MaterialDockerImage.exe " + RegexCommandLine.ToArguments(action), "Command line copied.");
            })),
        actionStatus);

    private void SaveAction()
    {
        var action = CurrentAction();
        var problem = action.Problem();
        if (problem != null) { actionStatus.Text = RegexText.Sentences(problem, Loc.T); return; }
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "JSON|*.json", FileName = "regex-search.json", Title = Loc.T("Save action file") };
        Close();
        if (dialog.ShowDialog() != true) return;
        try { action.Save(dialog.FileName); AppHost.Current.Notify.Success("Action file saved to " + dialog.FileName + "."); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { AppHost.Current.Notify.Error("The action file could not be saved: " + e.Message); }
    }

    private void OpenAction()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "JSON|*.json", Title = Loc.T("Open action file") };
        Close();
        if (dialog.ShowDialog() != true) return;
        try
        {
            var action = RegexSearchAction.Load(dialog.FileName);
            ApplyActionToField(field, action);
            AppHost.Current.Notify.Info("Opened " + Path.GetFileName(dialog.FileName) + ". Nothing has been searched or replaced.");
            field.Dispatcher.BeginInvoke(() => field.OpenBuilder());
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or DecoderFallbackException) { AppHost.Current.Notify.Error("The action file could not be opened: " + e.Message); }
    }

    /// <summary>
    /// Fills in a search field from an action (an action file or the command line): regular expression mode, flags, pattern, replacement, folder, file patterns,
    /// subfolders and context lines. A builder opened afterwards reads them from the field's state. It never runs the search or a replacement.
    /// </summary>
    internal static void ApplyActionToField(SearchFilter field, RegexSearchAction action)
    {
        field.RegexMode = true;
        field.Flags = action.Flags;
        field.Query = action.Pattern;
        field.State.Replacement = action.Replacement;
        field.State.GrepFolder = action.Folder;
        field.State.GrepGlobs = action.Globs;
        field.State.GrepRecursive = action.Recursive;
        field.State.GrepContext = action.ContextLines;
        field.State.GrepIncludeHidden = action.IncludeHidden;
        field.State.GrepIncludeReadOnly = action.IncludeReadOnly;
        field.State.GrepIncludeSystem = action.IncludeSystem;
        field.State.GrepEncoding = action.BomlessEncoding;
    }

    // ----- engine versions -----

    private readonly Dictionary<string, string> chosenVersions = new(StringComparer.Ordinal);

    private FrameworkElement FlavorVersionPickers()
    {
        var wrap = new WrapPanel();
        foreach (var f in RegexFlavors.Flavors.Where(f => RegexFlavorVersions.HasChoice(f.Id)))
        {
            var id = f.Id;
            var versions = RegexFlavorVersions.Versions(id);
            var combo = Ui.Combo(versions.Select(v => v.Label), RegexFlavorVersions.Latest(id).Label);
            AutomationProperties.SetName(combo, f.Name + " " + Loc.T("version"));
            combo.SelectionChanged += (_, _) =>
            {
                var picked = versions.FirstOrDefault(v => v.Label == combo.SelectedItem as string);
                if (picked == null) return;
                chosenVersions[id] = picked.Id;
                RenderFlavors();
            };
            var dependent = string.Join("; ", RegexFlavorVersions.Dependent(id));
            wrap.Children.Add(Ui.Field(f.Name + " version", combo, "Changes the verdict for: " + dependent + ". The newest version is what the table describes; older versions come from the language's documentation, not from running an old engine.", versions[^1].Label).Margin(0, 0, 12, 6));
        }
        return wrap;
    }
}
