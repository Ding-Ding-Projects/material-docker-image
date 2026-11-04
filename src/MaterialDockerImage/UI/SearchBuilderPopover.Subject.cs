using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using MaterialDockerImage.Services;

namespace MaterialDockerImage.UI;

/// <summary>
/// The third wave of builder features, as view code only: the Unicode block picker, per-token help, the subject tools (scopes, hex view, line endings, files),
/// the pattern comparison and the growth estimate. The logic lives in RegexBuilderUnicode.cs, RegexBuilderHelp.cs, RegexBuilderSubject.cs and RegexBuilderCompare.cs.
/// </summary>
internal sealed partial class SearchBuilderPopover
{
    private readonly TextBlock tokenHelp = new() { TextWrapping = TextWrapping.Wrap };
    private ComboBox? scopeCombo, encodingCombo, endingCombo;
    private readonly TextBox scopeOutput = Ui.CodeBox("", 60);
    private readonly TextBlock scopeStatus = Ui.Caption("");
    private readonly TextBox hexOutput = Ui.CodeBox("", 60);
    private readonly TextBlock subjectStatus = Ui.Caption("");
    private TextBox? compareBox;
    private readonly TextBox compareOutput = Ui.CodeBox("", 60);
    private readonly TextBlock compareStatus = Ui.Caption("");
    private CancellationTokenSource? compareRun;
    private readonly TextBox saveDescription = Ui.Input("", "Description (optional)");

    private const string HelpHint = "Select a row of the explanation to read the full help for that token.";

    private void InitSubject()
    {
        tokenHelp.SetResourceReference(TextBlock.ForegroundProperty, "OnSurfaceVariant");
        tokenHelp.SetValue(AutomationProperties.LiveSettingProperty, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(tokenHelp, Loc.T("Help for the selected token"));
        ShowTokenHelp(null);
        explanation.SelectedItemChanged += (_, e) => ShowTokenHelp(e.NewValue as TreeViewItem);
        saveDescription.MaxLength = RegexSavedPattern.MaxDescriptionLength;
        AutomationProperties.SetName(saveDescription, Loc.T("Description (optional)"));
        foreach (var live in new[] { scopeStatus, subjectStatus, compareStatus })
            live.SetValue(AutomationProperties.LiveSettingProperty, AutomationLiveSetting.Polite);
        foreach (var box in new[] { scopeOutput, hexOutput, compareOutput }) box.IsReadOnly = true;
        AutomationProperties.SetName(scopeOutput, Loc.T("Matches by scope"));
        AutomationProperties.SetName(hexOutput, Loc.T("Hex view of the sample"));
        AutomationProperties.SetName(compareOutput, Loc.T("Pattern comparison"));
    }

    // ----- per-token help -----

    private void ShowTokenHelp(TreeViewItem? item)
    {
        tokenHelp.Text = item?.Tag is RegexExplainNode node ? RegexTokenHelp.Describe(node, Loc.T) : Loc.T(HelpHint);
    }

    // ----- Unicode blocks -----

    private FrameworkElement UnicodeBlockPicker()
    {
        var filter = new SearchFilter("Filter the blocks", 280) { Width = double.NaN };
        var combo = Ui.Combo(RegexUnicodeBlocks.All.Select(BlockLabel), BlockLabel(RegexUnicodeBlocks.All.First(b => b.Name == "Cyrillic")));
        var negate = Ui.Check("Not in this block", false);
        AutomationProperties.SetName(combo, Loc.T("Unicode block"));
        var status = Ui.Caption("");
        // The filter is a search field like every other and opens its own builder; this popup stays open while that one is in use.
        filter.Changed += () =>
        {
            var shown = RegexUnicodeBlocks.Search(filter.Predicate).Select(BlockLabel).ToList();
            combo.ItemsSource = shown;
            if (shown.Count > 0) combo.SelectedIndex = 0;
            status.Text = filter.Predicate.IsValid ? "" : Loc.T(filter.Predicate.Error ?? "Invalid pattern.");
        };
        RegisterNested(filter);
        return Ui.VStack(6,
            Ui.Field("Unicode block", Ui.VStack(6, filter, Ui.HStack(8, combo, negate)), "Matches a character of one named block such as Greek or Cyrillic. .NET knows the blocks of the Basic Multilingual Plane by name.", @"\p{IsCyrillic}"),
            Ui.Caption(RegexUnicodeBlocks.ScriptNotice),
            Small("Insert block", null, () =>
            {
                if (combo.SelectedItem is not string label) { status.Text = Loc.T("No block matches the filter."); return; }
                status.Text = "";
                Insert(RegexUnicodeBlocks.Pattern(label.Split(" · ")[1], negate.IsChecked == true), "");
            }),
            status);
    }

    private static string BlockLabel(RegexUnicodeBlock block) => block.Title + " · " + block.Name + " · " + block.Range;

    // ----- the subject: scopes, hex, line endings, files -----

    private FrameworkElement SubjectSection()
    {
        scopeCombo = Ui.Combo(new[] { "Whole text", "Each line", "Each page" }, "Whole text");
        encodingCombo = Ui.Combo(new[] { "Auto" }.Concat(RegexSubject.Encodings), "Auto");
        endingCombo = Ui.Combo(RegexSubject.LineEndingNames, "LF");
        AutomationProperties.SetName(scopeCombo, Loc.T("Scope"));
        AutomationProperties.SetName(encodingCombo, Loc.T("Encoding"));
        AutomationProperties.SetName(endingCombo, Loc.T("Line ending"));
        return Section("Subject tools", "Test the sample line by line or page by page, look at its bytes in an encoding, change its line endings, or load a text file into it and save it back. A file is read up to the sample limit.",
            Ui.Wrap(12,
                Ui.Field("Scope", scopeCombo, "Each line or each page is tested on its own, so ^ and $ anchor there and no match crosses the boundary. A page ends at a form feed.", "Each line"),
                Ui.Field("Encoding", encodingCombo, "Used by the hex view, by Load and by Save. Auto reads a byte order mark, then valid UTF-8, then Latin-1, and saves as UTF-8.", "UTF-8"),
                Ui.Field("Line ending", endingCombo, "The line ending that Convert and Save write.", "CRLF")),
            Ui.Wrap(8,
                Small("Test by scope", "Runs the pattern on every line or page of the sample and lists where it matches.", RunScopes),
                Small("Show hex view", "Shows the first 4 KiB of the sample as bytes in the chosen encoding.", ShowHex),
                Small("Convert line endings", "Rewrites every line break in the sample as the chosen line ending.", ConvertEndings),
                Small("Load file…", "Replaces the sample with the first 64 KiB of a text file.", LoadSubjectFile),
                Small("Save sample…", "Writes the sample to a file in the chosen encoding and line ending.", SaveSubjectFile)),
            scopeStatus, scopeOutput, subjectStatus, hexOutput);
    }

    private RegexScope ChosenScope() => (scopeCombo?.SelectedItem as string) switch { "Each line" => RegexScope.Line, "Each page" => RegexScope.Page, _ => RegexScope.Whole };

    private string ChosenEncoding(bool forWriting = false)
    {
        var name = encodingCombo?.SelectedItem as string ?? "Auto";
        return name == "Auto" && forWriting ? "UTF-8" : name;
    }

    private void RunScopes()
    {
        if (field.Query.Length == 0) { scopeStatus.Text = Loc.T("Enter a pattern to test."); scopeOutput.Text = ""; return; }
        var (text, flags) = Effective();
        var result = RegexSubject.RunScopes(text, flags, field.State.Sample, ChosenScope());
        scopeOutput.Text = result.Valid ? RegexSubject.Report(result, Loc.T) : "";
        scopeStatus.Text = result.Valid ? Loc.T("Matches by scope") + ": " + result.TotalMatches : result.Error ?? "";
    }

    private void ShowHex()
    {
        var name = ChosenEncoding();
        var view = RegexSubject.Hex(field.State.Sample, name == "Auto" ? "UTF-8" : name);
        hexOutput.Text = view.Text;
        subjectStatus.Text = RegexSubject.HexStatus(view, field.State.Sample, Loc.T);
    }

    private void ConvertEndings()
    {
        sample.Text = RegexSubject.NormalizeLineEndings(sample.Text, endingCombo?.SelectedItem as string ?? "LF");
        subjectStatus.Text = Loc.T(RegexSubject.CountLineEndings(sample.Text).Summary);
    }

    private void LoadSubjectFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Text|*.txt;*.log;*.csv;*.json;*.md;*.xml;*.cs;*.ps1|All files|*.*", Title = Loc.T("Load a file as the sample") };
        var encoding = ChosenEncoding();
        Close();
        if (dialog.ShowDialog() != true) return;
        var loaded = RegexSubject.LoadFile(dialog.FileName, encoding);
        if (!loaded.Success) { AppHost.Current.Notify.Error(loaded.Error ?? "The file could not be read."); return; }
        sample.Text = loaded.Text; // the text box keeps the field state in step through its change handler, open or closed
        AppHost.Current.Notify.Success($"Loaded {Path.GetFileName(dialog.FileName)} as the sample ({loaded.Encoding}" + (loaded.Truncated ? $", the first {RegexBuilderEngine.MaxSampleBytes / 1024} KiB of {loaded.FileBytes} bytes" : "") + "). Open the builder to test it.");
    }

    private void SaveSubjectFile()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Text|*.txt|All files|*.*", FileName = "sample.txt", Title = Loc.T("Save the sample") };
        var encoding = ChosenEncoding(true);
        var ending = endingCombo?.SelectedItem as string ?? "LF";
        var text = field.State.Sample;
        Close();
        if (dialog.ShowDialog() != true) return;
        var error = RegexSubject.SaveFile(dialog.FileName, text, encoding, ending);
        if (error != null) AppHost.Current.Notify.Error(error);
        else AppHost.Current.Notify.Success($"Sample saved to {dialog.FileName} ({encoding}, {ending}).");
    }

    // ----- comparing patterns -----

    private FrameworkElement CompareSection()
    {
        compareBox = Ui.CodeBox(field.State.ComparePatterns, 70);
        compareBox.TextChanged += (_, _) => field.State.ComparePatterns = compareBox.Text;
        AutomationProperties.SetName(compareBox, Loc.T("Patterns to compare, one per line"));
        return Section("Compare patterns", "Runs several patterns over the test cases above: each + line is a subject that must match and each - line a subject that must not. Every pattern is checked for the right answers and then timed.",
            Ui.Field("Patterns", compareBox, "One pattern per line, at most 8. They use the flags chosen above.", "\\d+ / [0-9]+"),
            Ui.Wrap(8,
                Small("Add the current pattern", "Adds the pattern of this field as a new line.", () =>
                {
                    if (field.Query.Length == 0) return;
                    compareBox.Text = (compareBox.Text.Length > 0 && !compareBox.Text.EndsWith('\n') ? compareBox.Text + "\n" : compareBox.Text) + Effective().Pattern;
                }),
                Small("Compare", "Checks and times every pattern on the test cases. The times are measurements on this computer and change from run to run.", RunCompare)),
            compareStatus, compareOutput);
    }

    private async void RunCompare()
    {
        compareRun?.Cancel();
        var cts = compareRun = new CancellationTokenSource();
        var patterns = RegexCompare.ParsePatterns(compareBox?.Text);
        var flags = Effective().Flags;
        var suite = field.State.TestCases;
        compareStatus.Text = Loc.T("Comparing…"); compareOutput.Text = "";
        try
        {
            var result = await Task.Run(() => RegexCompare.Run(patterns, flags, suite, cts.Token, Loc.T), cts.Token);
            if (cts.IsCancellationRequested || !popup.IsOpen) return;
            if (!result.Valid) { compareStatus.Text = Loc.T(result.Error ?? ""); return; }
            compareStatus.Text = result.Verdict;
            compareOutput.Text = string.Join("\n", result.Rows.Select(r => !r.Valid
                ? $"✗ {r.Pattern}   invalid: {r.Error}"
                : $"{(r.AllCorrect ? "✓" : "✗")} {r.Pattern}   must match {r.MatchingPassed}/{r.MatchingTotal}, must not match {r.NonMatchingPassed}/{r.NonMatchingTotal}   {r.MatchingMs:0.000} ms / {r.NonMatchingMs:0.000} ms   {(r.TimedOut ? "timed out" : r.Engine)}"));
        }
        catch (OperationCanceledException) { }
    }
}
