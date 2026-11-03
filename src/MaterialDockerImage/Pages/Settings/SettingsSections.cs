using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage.Pages;

/// <summary>Appearance, Language, Accessibility, Focus modes and Sounds sections.</summary>
public sealed partial class SettingsPage
{
    private static IReadOnlyList<string>? voices;
    private UserSettings S => Host.Settings;

    /// <summary>The Appearance section is built by the appearance editor (Pages/Settings/AppearanceEditor.cs).</summary>
    private void AppearanceSection() => AppearanceEditorSection();

    private void LanguageSection()
    {
        var preview = Ui.Text("", 14, null, "OnSurface", true, false);
        void Preview()
        {
            SettingRows.SyncRuntime();
            preview.Text = Loc.Voice("Image built in 18 s.", "Done! Nice and quick.", "搞掂，好快手！") + "  " + Loc.Emoji("success");
        }
        rows.Add("language", "Language", "Shows labels in English, Cantonese, or both side by side.",
            Ui.Segmented(["English", "Cantonese", "Bilingual"], S.Language, v => { S.Language = v; Commit("Language set to " + v); Preview(); Host.Notify.Info("Language saved. Open pages refresh their labels when reopened."); }), s => s.Language,
            "Only labels with a Cantonese translation change; everything else stays in English. School mode forces English regardless of this setting.");
        string Level(double v) => $"{v:0} of 5" + (v <= 1 ? " · plain" : v >= 5 ? " · most playful" : "");
        var enText = Ui.Text(Level(S.FunnyEnglish), 13, null, "OnSurfaceVariant", false, false);
        var en = SettingRows.Slider(1, 5, S.FunnyEnglish, v => { S.FunnyEnglish = (int)v; enText.Text = Level(v); Preview(); Commit("English funny level set to " + v, true); }, 180);
        rows.Add("funnyen", "Funny level, English", "Changes how playful English messages sound. Facts in messages never change.", Ui.HStack(12, en, enText), s => s.FunnyEnglish,
            "Levels 1 to 3 state the fact only. Levels 4 and 5 add a short friendly lead-in before the fact.");
        var yueText = Ui.Text(Level(S.FunnyCantonese), 13, null, "OnSurfaceVariant", false, false);
        var yue = SettingRows.Slider(1, 5, S.FunnyCantonese, v => { S.FunnyCantonese = (int)v; yueText.Text = Level(v); Preview(); Commit("Cantonese funny level set to " + v, true); }, 180);
        rows.Add("funnyyue", "Funny level, Cantonese", "Changes how playful messages sound when the language is Cantonese.", Ui.HStack(12, yue, yueText), s => s.FunnyCantonese,
            "Used only when Language is Cantonese. The fact in each message is always kept.");
        rows.Add("emoji", "Show emojis in dialogs", "Adds a small emoji to dialog titles and the message preview.",
            SettingRows.Toggle(S.DialogEmoji, v => { S.DialogEmoji = v; Commit("Dialog emojis " + (v ? "on" : "off")); Preview(); }), s => s.DialogEmoji);
        Preview();
        var previewCard = Ui.Card(Ui.VStack(6, Ui.Overline("Preview (example message, nothing was built)"), preview), 14, "SurfaceContainer", 12);
        previewCard.Margin = new Thickness(0, 12, 0, 4);
        Extra(previewCard);
        if (S.SchoolMode) Extra(Ui.Caption("School mode is on, so English is used and funny levels have no effect until it is turned off in Data and privacy.").Margin(0, 4, 0, 4));
        rows.Add("vocabulary", "Personal wording", "Replaces chosen labels with your own words from a local JSON file. The file stays on this computer.",
            VocabularyControl(), s => s.PersonalVocabularyPath,
            "Format: {\"version\": 1, \"entries\": {\"Build image\": \"Cook it\"}}. Keys are the English labels. The whole file is checked before anything is applied; a rejected file changes nothing. The wording is never written to logs, history or exports, only the file path is saved.", true,
            v => v is string { Length: > 0 } ? "a file" : "no file", "{\"version\": 1, \"entries\": {\"Build image\": \"Cook it\"}}");
    }

    private FrameworkElement VocabularyControl()
    {
        var state = Ui.Caption("");
        void Show(string? problem = null)
        {
            if (problem != null) { state.Text = "Invalid file: " + problem; state.SetResourceReference(TextBlock.ForegroundProperty, "Error"); return; }
            state.SetResourceReference(TextBlock.ForegroundProperty, "OnSurfaceVariant");
            state.Text = S.PersonalVocabularyPath.Length == 0 ? Loc.T("No file chosen.") : Loc.OverrideCount > 0 ? $"Loaded {Loc.OverrideCount} entries from {Path.GetFileName(S.PersonalVocabularyPath)}." : $"{Path.GetFileName(S.PersonalVocabularyPath)} is saved but not loaded (the file may have moved). Choose it again.";
        }
        var choose = Ui.Button("Choose file…", Icons.FolderOpen, ButtonKind.Tonal, () =>
        {
            var dialog = new OpenFileDialog { Filter = "JSON|*.json", Title = Loc.T("Choose a personal wording file") };
            if (dialog.ShowDialog() != true) return;
            var result = Loc.LoadVocabulary(dialog.FileName);
            if (!result.Success) { Show(result.Message); return; }
            S.PersonalVocabularyPath = dialog.FileName;
            Commit("Personal wording file chosen");
            Show();
            Host.Notify.Success($"Loaded {result.Count} personal wording entries. Open pages show them when reopened.");
        });
        var clear = Ui.Button("Clear", Icons.Close, ButtonKind.Text, () =>
        {
            Loc.ClearVocabulary();
            S.PersonalVocabularyPath = "";
            Commit("Personal wording cleared");
            Show();
        });
        Show();
        return Ui.VStack(6, Ui.HStack(8, choose, clear), state);
    }

    private void AccessibilitySection()
    {
        rows.Add("accessmenu", "Accessibility menu", "Shows the accessibility button in the title bar for quick text size, contrast, motion and narration changes.",
            SettingRows.Toggle(S.AccessibilityMenuVisible, v => { S.AccessibilityMenuVisible = v; Commit("Accessibility menu " + (v ? "shown" : "hidden")); }), s => s.AccessibilityMenuVisible);
        var shortcuts = new (string Keys, string Action)[]
        {
            ("Ctrl+Shift+F", "Command palette"), ("Ctrl+1 … Ctrl+9", "Go to destinations in rail order"), ("Ctrl+T", "New tab"), ("Ctrl+W", "Close tab"),
            ("Ctrl+Tab", "Next tab"), ("Ctrl+J", "Show or hide the output panel"), ("Alt+R", "Regular expression builder in a search box"),
            ("Enter", "Open or run the focused row's main action"), ("Delete", "Remove the focused row (where removal exists)"), ("Shift+F10 or Menu key", "Row context menu"), ("Escape", "Close dialogs and clear searches")
        };
        var filter = new SearchFilter("Search shortcuts", 240);
        var list = new StackPanel();
        void Render()
        {
            list.Children.Clear();
            foreach (var (keys, action) in shortcuts.Where(s => filter.Matches(s.Keys + " " + s.Action)))
                list.Children.Add(Ui.Columns(12, [-190, 1], Ui.Mono(keys, 13), Ui.Text(action, 13)).Margin(0, 3, 0, 3));
            if (list.Children.Count == 0) list.Children.Add(Ui.Caption("No shortcuts match."));
        }
        filter.Changed += Render;
        Render();
        var card = Ui.Card(Ui.VStack(8, Ui.Text("Keyboard shortcuts", 14, FontWeights.SemiBold), filter, list), 14, "SurfaceContainer", 12);
        card.Margin = new Thickness(0, 12, 0, 0);
        Extra(card);
        Extra(Ui.Caption("Text size, fonts, high contrast, motion, reduced motion and larger targets are under Appearance. Ctrl+Shift+E edits the appearance of the focused element.").Margin(0, 10, 0, 0));
    }

    private void FocusSection()
    {
        rows.Add("focusmode", "Focus", "Narrows the navigation rail and hides its labels, leaving more room for the page.",
            SettingRows.Toggle(S.FocusMode, v => { S.FocusMode = v; Commit("Focus " + (v ? "on" : "off")); }), s => s.FocusMode);
        rows.Add("lowstim", "Low stimulation", "Turns off action sounds and every animation (the interface snaps to each final state).",
            SettingRows.Toggle(S.LowStimulation, v => { S.LowStimulation = v; Commit("Low stimulation " + (v ? "on" : "off")); }), s => s.LowStimulation);
        rows.Add("time", "Time awareness", "Shows a clock in the title bar.",
            SettingRows.Toggle(S.TimeAwareness, v => { S.TimeAwareness = v; Commit("Time awareness " + (v ? "on" : "off")); }), s => s.TimeAwareness);
        rows.Add("onething", "One thing at a time", "Opening another destination reuses the current tab instead of adding a new one.",
            SettingRows.Toggle(S.OneThingAtATime, v => { S.OneThingAtATime = v; Commit("One thing at a time " + (v ? "on" : "off")); }), s => s.OneThingAtATime);
        rows.Add("momentum", "Momentum", "Saved preference for next-step suggestions after an action. This version does not show them yet.",
            SettingRows.Toggle(S.Momentum, v => { S.Momentum = v; Commit("Momentum " + (v ? "on" : "off")); }), s => s.Momentum);
    }

    private void SoundsSection()
    {
        rows.Add("sounds", "Action sounds", "Plays the Windows system sound for success, warning and error notifications.",
            SettingRows.Toggle(S.Sounds, v => { S.Sounds = v; Commit("Action sounds " + (v ? "on" : "off")); }), s => s.Sounds,
            "Low stimulation turns sounds off even when this is on.");
        rows.Add("narration", "Narration", "Reads notifications aloud with a Windows speech voice.",
            SettingRows.Toggle(S.Narration, v => { S.Narration = v; Commit("Narration " + (v ? "on" : "off")); }), s => s.Narration,
            "One message is spoken at a time, and the same message is not repeated within three seconds. Screen readers announce notifications independently of this setting.");
        FrameworkElement voiceControl;
        if (voices == null) voiceControl = Ui.Caption("Loading installed voices…");
        else if (voices.Count == 0) voiceControl = Ui.Caption("No speech voices are installed or speech is unavailable" + (Narrator.Unavailable != null ? ": " + Narrator.Unavailable : ".") + " Add voices in Windows Settings, Time and language, Speech.");
        else
        {
            var options = new List<string> { Loc.T("Choose automatically") };
            options.AddRange(voices);
            var combo = Ui.Combo(options, S.NarrationVoice.Length > 0 && voices.Contains(S.NarrationVoice) ? S.NarrationVoice : options[0]);
            combo.MinWidth = 240;
            combo.SelectionChanged += (_, _) => { S.NarrationVoice = combo.SelectedIndex <= 0 ? "" : combo.SelectedItem as string ?? ""; Commit("Narration voice set to " + (S.NarrationVoice.Length > 0 ? S.NarrationVoice : "automatic")); };
            voiceControl = combo;
        }
        rows.Add("voice", "Voice", "The speech voice used for narration.", voiceControl, s => s.NarrationVoice, null, false, v => v is string { Length: > 0 } t ? t : "chosen automatically");
        var rateText = Ui.Text(S.NarrationRate.ToString("+0;-0;0"), 13, null, "OnSurfaceVariant", false, false);
        var rate = SettingRows.Slider(-5, 5, S.NarrationRate, v => { S.NarrationRate = (int)v; rateText.Text = ((int)v).ToString("+0;-0;0"); Commit("Narration rate set to " + v, true); }, 200);
        rows.Add("rate", "Speaking rate", "How fast narration speaks, from -5 (slower) to +5 (faster).", Ui.HStack(12, rate, rateText), s => s.NarrationRate);
        var test = Ui.Button("Test narration", Icons.RecordVoiceOver, ButtonKind.Text, () =>
        {
            if (!S.Narration) { Host.Notify.Info("Turn on Narration to hear a test."); return; }
            Narrator.Speak(Loc.T("Narration test. This is how notifications will sound."));
        });
        test.HorizontalAlignment = HorizontalAlignment.Left;
        Extra(test.Margin(0, 8, 0, 0));
    }

    public override async Task RefreshAsync()
    {
        if (voices != null) return;
        voices = await Task.Run(Narrator.Voices);
        Rebuild();
    }
}
