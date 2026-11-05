using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

public enum EditorSuggestionLanguage { Dockerfile, Compose }

/// <summary>
/// Suggestions for a source TextBox (Dockerfile or Compose): a completion popup opened automatically while typing
/// (when the setting is on) or with Ctrl+Space, a signature hint line, hover documentation, a one-keypress offer to
/// add or upgrade the # syntax header, and value pickers (Ctrl+Shift+Space). The editor keeps keyboard focus while
/// the list is open; edits go through the TextBox selection so Ctrl+Z undoes them.
/// </summary>
public sealed class EditorSuggestions
{
    private static AppHost Host => AppHost.Current;
    private static readonly char[] Triggers = ['-', '=', '$', '{', ':', ' ', '/', ',', '#'];
    private static WeakReference<EditorSuggestions>? lastFocused;
    private static bool commandsRegistered;

    public TextBox Box { get; }
    public EditorSuggestionLanguage Language { get; }
    /// <summary>The build context folder (Dockerfile) or project folder (Compose) used for paths.</summary>
    public Func<string?> ContextFolder { get; }

    private readonly EditorSuggestionsList list = new();
    /// <summary>Popups currently open (list, signature hint, hover), so capture mode can composite them into a screenshot.</summary>
    internal IEnumerable<Popup> OpenPopups => new[] { list.Popup, hint, hover }.Where(p => p.IsOpen && p.Child != null);
    private readonly Popup hint;
    private readonly TextBlock hintText = Ui.Mono("", 12, "OnSurfaceVariant", true);
    private readonly Button hintAction;
    private readonly Popup hover;
    private readonly StackPanel hoverBody = new() { MaxWidth = 380 };
    private readonly DispatcherTimer typingTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private readonly DispatcherTimer hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private SuggestionResult? current;
    private bool explicitOpen, applying, upgradeOffered;
    private int hintLine = -1;
    private Point hoverPoint;
    internal bool PickerOpen { get; set; }

    private EditorSuggestions(TextBox box, EditorSuggestionLanguage language, Func<string?> contextFolder)
    {
        Box = box; Language = language; ContextFolder = contextFolder;

        hintAction = Ui.Button("Add # syntax header (Ctrl+Enter)", Icons.Bolt, ButtonKind.Text, ApplySyntaxUpgrade);
        hintAction.Focusable = false;
        hintAction.Visibility = Visibility.Collapsed;
        // The signature note of the design board: surface-highest, radius 8, monospace 12, required parts in bold.
        hintText.MaxWidth = 720;
        var hintCard = EditorSuggestionsList.SignatureNote(Ui.HStack(8, hintText, hintAction));
        hintCard.Margin = new Thickness(0, 0, 10, 10);
        AutomationProperties.SetName(hintCard, Loc.T("Suggestion hint"));
        AutomationProperties.SetLiveSetting(hintText, AutomationLiveSetting.Polite);
        hint = new Popup { Child = hintCard, AllowsTransparency = true, StaysOpen = true, Focusable = false, Placement = PlacementMode.Relative, PlacementTarget = box };

        var hoverCard = new Border { Child = hoverBody, CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 12, 10), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 12, 12), Effect = Ui.Elevation(12, 3, .3), IsHitTestVisible = false };
        hoverCard.SetResourceReference(Border.BackgroundProperty, "SurfaceHigh");
        hoverCard.SetResourceReference(Border.BorderBrushProperty, "OutlineVariant");
        hover = new Popup { Child = hoverCard, AllowsTransparency = true, StaysOpen = true, Focusable = false, Placement = PlacementMode.Relative, PlacementTarget = box };

        list.Popup.PlacementTarget = box;
        list.Accepted += Accept;
        list.SelectionChanged += m => Announce(EditorSuggestionsList.AccessibleName(m.Item));

        box.PreviewKeyDown += OnPreviewKeyDown;
        box.TextChanged += OnTextChanged;
        box.SelectionChanged += (_, _) => OnCaretMoved();
        box.GotKeyboardFocus += (_, _) => { lastFocused = new WeakReference<EditorSuggestions>(this); EditorSuggestionsSources.EnsureImages(); _ = EditorSuggestionsSources.ContextEntries(ContextFolder()); };
        box.LostKeyboardFocus += (_, _) => { if (!PickerOpen) CloseAll(); };
        box.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => { if (list.IsOpen) Position(list.Popup, current?.Start ?? box.CaretIndex, true); HideHint(); HideHover(); }));
        box.MouseMove += OnMouseMove;
        box.MouseLeave += (_, _) => { hoverTimer.Stop(); HideHover(); };
        box.Unloaded += (_, _) => CloseAll();
        box.Loaded += (_, _) =>
        {
            if (Window.GetWindow(box) is { } window)
            {
                window.LocationChanged -= OnWindowMoved; window.LocationChanged += OnWindowMoved;
                window.Deactivated -= OnWindowMoved; window.Deactivated += OnWindowMoved;
            }
        };
        typingTimer.Tick += (_, _) => { typingTimer.Stop(); if (Box.IsKeyboardFocused) Open(false); };
        hoverTimer.Tick += (_, _) => { hoverTimer.Stop(); ShowHover(); };
        EditorSuggestionsSources.Updated += () => { if (list.IsOpen) Refresh(); };
    }

    /// <summary>Attaches Suggestions to a source box. Safe to call once per box.</summary>
    public static EditorSuggestions Attach(TextBox box, EditorSuggestionLanguage language, Func<string?> contextFolder)
    {
        RegisterCommands();
        return new EditorSuggestions(box, language, contextFolder);
    }

    private static bool WhileTyping => Host?.Settings.SuggestionsWhileTyping != false;

    private void OnWindowMoved(object? sender, EventArgs e) { if (!PickerOpen) CloseAll(); }

    // ------------------------------------------------------------------ keyboard

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        if (key == Key.Space && mods == ModifierKeys.Control) { Open(true); e.Handled = true; return; }
        if (key == Key.Space && mods == (ModifierKeys.Control | ModifierKeys.Shift)) { OpenPickerAtCaret(); e.Handled = true; return; }
        if (upgradeOffered && key == Key.Enter && mods == ModifierKeys.Control) { ApplySyntaxUpgrade(); e.Handled = true; return; }
        if (list.IsOpen)
        {
            switch (key)
            {
                case Key.Down when mods == ModifierKeys.None: list.Move(1); e.Handled = true; return;
                case Key.Up when mods == ModifierKeys.None: list.Move(-1); e.Handled = true; return;
                case Key.PageDown: list.Move(list.PageSize); e.Handled = true; return;
                case Key.PageUp: list.Move(-list.PageSize); e.Handled = true; return;
                case Key.Enter when mods == ModifierKeys.None && list.Selected is { } typedAlready && current != null
                    && typedAlready.Item.Kind != SuggestionKind.Picker && typedAlready.Item.Insert.TrimEnd() == current.Query:
                    // The text already matches the selection: close and let Enter insert the line break.
                    CloseList();
                    return;
                case Key.Tab when mods == ModifierKeys.None:
                case Key.Enter when mods == ModifierKeys.None:
                    if (list.Selected is { } m) { Accept(m); e.Handled = true; }
                    return;
                case Key.Escape: CloseList(); e.Handled = true; return;
            }
        }
        if (key == Key.Escape && (hint.IsOpen || hover.IsOpen)) { HideHint(); HideHover(); upgradeOffered = false; e.Handled = true; }
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        HideHover();
        if (applying || Box.IsReadOnly) return;
        var typed = '\0';
        var deleted = false;
        foreach (var change in e.Changes)
        {
            if (change.AddedLength == 1 && change.Offset < Box.Text.Length) typed = Box.Text[change.Offset];
            else if (change.AddedLength == 0 && change.RemovedLength > 0) deleted = true;
            else typed = '\0';
        }
        if (list.IsOpen) { Dispatcher.BeginInvoke(Refresh, DispatcherPriority.Background); return; }
        if (deleted || typed == '\0' || typed == '\n' || typed == '\r') { typingTimer.Stop(); return; }
        if (!WhileTyping) return;
        if (Array.IndexOf(Triggers, typed) >= 0) { typingTimer.Stop(); Dispatcher.BeginInvoke(() => { if (Box.IsKeyboardFocused) Open(false); }, DispatcherPriority.Background); }
        else if (char.IsLetterOrDigit(typed) || typed is '_' or '.') { typingTimer.Stop(); typingTimer.Start(); }
    }

    private Dispatcher Dispatcher => Box.Dispatcher;

    private void OnCaretMoved()
    {
        if (applying) return;
        var caret = Box.CaretIndex;
        if (list.IsOpen && current != null)
        {
            var text = Box.Text;
            if (Box.SelectionLength > 0 || caret < current.Start || caret > SuggestionText.LineEnd(text, current.Start)) CloseList();
        }
        if (hint.IsOpen && LineOf(caret) != hintLine) { HideHint(); upgradeOffered = false; }
    }

    private int LineOf(int index) => Box.Text.Take(Math.Min(index, Box.Text.Length)).Count(c => c == '\n');

    // ------------------------------------------------------------------ completion

    private SuggestionResult Complete(string text, int caret)
    {
        var sources = EditorSuggestionsSources.For(ContextFolder());
        return Language == EditorSuggestionLanguage.Dockerfile ? DockerfileCompletion.Complete(text, caret, sources) : ComposeCompletion.Complete(text, caret, sources);
    }

    /// <summary>Opens the list at the caret. Explicit opens (Ctrl+Space, palette) also show "no suggestions".</summary>
    public void Open(bool explicitly)
    {
        if (Box.IsReadOnly || !Box.IsEnabled) return;
        explicitOpen = explicitly;
        var result = Complete(Box.Text, Box.CaretIndex);
        var matches = SuggestionFuzzy.Filter(result.Items, result.Query);
        if (!explicitly && (!result.AutoTrigger || !matches.Any(m => m.Item.Kind != SuggestionKind.Picker)))
        {
            CloseList();
            ShowSignature();
            return;
        }
        if (matches.Count == 0)
        {
            CloseList();
            ShowHint(Loc.T("No suggestions at this position."), false);
            return;
        }
        current = result;
        HideHint();
        HideHover();
        Position(list.Popup, result.Start, true);
        list.Show(matches, result.Signature, result.Syntax, Language == EditorSuggestionLanguage.Dockerfile);
        if (list.Selected is { } selected) Announce(Loc.T("Suggestions") + ": " + matches.Count + ". " + EditorSuggestionsList.AccessibleName(selected.Item));
    }

    private void Refresh()
    {
        if (!list.IsOpen) return;
        var result = Complete(Box.Text, Box.CaretIndex);
        var matches = SuggestionFuzzy.Filter(result.Items, result.Query);
        if (!matches.Any(m => m.Item.Kind != SuggestionKind.Picker) && !(explicitOpen && matches.Count > 0)) { CloseList(); return; }
        var moved = current == null || current.Start != result.Start;
        current = result;
        list.Show(matches, result.Signature, result.Syntax, Language == EditorSuggestionLanguage.Dockerfile);
        if (moved) Position(list.Popup, result.Start, true);
    }

    private void Accept(SuggestionMatch match)
    {
        var result = current;
        if (result == null) return;
        var item = match.Item;
        CloseList();
        if (item.Kind == SuggestionKind.Picker)
        {
            if (result.Value != null) OpenPicker(result.Value, item.PickerVariant);
            return;
        }
        var edit = SuggestionText.Apply(Box.Text, result, item);
        Replace(edit.Start, edit.Length, edit.Insert, edit.CaretAfter);
        Announce(Loc.T("Inserted") + " " + item.Label);
        if (item.NeedsSyntaxUpgrade && Language == EditorSuggestionLanguage.Dockerfile && DockerfileCompletion.SyntaxUpgradeEdit(Box.Text) != null)
        {
            upgradeOffered = true;
            ShowHint(item.Label.Trim() + " " + Loc.T("needs") + " docker/dockerfile:" + item.MinSyntax + " " + Loc.T("or newer."), true);
        }
        else if (item.Retrigger) Dispatcher.BeginInvoke(() => { if (Box.IsKeyboardFocused) Open(false); }, DispatcherPriority.Background);
    }

    /// <summary>Replaces a range through the TextBox selection (one undo step) and places the caret.</summary>
    internal void Replace(int start, int length, string insert, int caretAfter)
    {
        applying = true;
        try
        {
            start = Math.Clamp(start, 0, Box.Text.Length);
            length = Math.Clamp(length, 0, Box.Text.Length - start);
            Box.Select(start, length);
            Box.SelectedText = insert;
            Box.Select(Math.Clamp(caretAfter, 0, Box.Text.Length), 0);
        }
        finally { applying = false; }
        Box.Focus();
    }

    private void ApplySyntaxUpgrade()
    {
        upgradeOffered = false;
        HideHint();
        if (DockerfileCompletion.SyntaxUpgradeEdit(Box.Text) is not { } edit) return;
        var caret = Box.CaretIndex;
        var shifted = caret >= edit.Start ? caret + edit.Insert.Length - edit.Length : caret;
        Replace(edit.Start, edit.Length, edit.Insert, shifted);
        Announce(Loc.T("Added") + " # syntax=" + DockerfileCompletionData.RecommendedSyntax);
        Host?.Notify.Info(Loc.T("The Dockerfile now declares # syntax=docker/dockerfile:1, the latest stable 1.x frontend. Ctrl+Z undoes it."));
    }

    // ------------------------------------------------------------------ pickers

    /// <summary>Opens the value picker for the value at the caret (Ctrl+Shift+Space), or explains that there is none.</summary>
    public void OpenPickerAtCaret()
    {
        if (Box.IsReadOnly || !Box.IsEnabled) return;
        CloseList();
        var result = Complete(Box.Text, Box.CaretIndex);
        if (result.Value == null) { ShowHint(Loc.T("No value with a picker at the caret."), false); return; }
        var variant = result.Value.Kind == EditorValueKind.Path && !result.Value.FolderOnly ? "file" : result.Value.Kind == EditorValueKind.Path ? "folder" : "";
        OpenPicker(result.Value, variant);
    }

    private void OpenPicker(EditorValueSpan value, string variant)
    {
        HideHint();
        HideHover();
        EditorValuePickers.Open(this, value, variant);
    }

    /// <summary>The caret rectangle (relative to the box) for anchoring pickers.</summary>
    internal Rect AnchorFor(int index)
    {
        Box.UpdateLayout();
        var rect = Box.GetRectFromCharacterIndex(Math.Clamp(index, 0, Box.Text.Length));
        return rect.IsEmpty ? new Rect(8, 8, 1, 16) : rect;
    }

    // ------------------------------------------------------------------ hint, hover, positioning

    private void ShowSignature()
    {
        if (Language != EditorSuggestionLanguage.Dockerfile) return;
        var signature = DockerfileCompletion.SignatureAt(Box.Text, Box.CaretIndex);
        if (signature.Length == 0) { if (!upgradeOffered) HideHint(); return; }
        if (upgradeOffered) return;
        ShowHint(signature, false);
    }

    private void ShowHint(string text, bool offerUpgrade)
    {
        EditorSuggestionsList.FillSignature(hintText, text);
        hintAction.Visibility = offerUpgrade ? Visibility.Visible : Visibility.Collapsed;
        hintLine = LineOf(Box.CaretIndex);
        Position(hint, SuggestionText.LineStart(Box.Text, Box.CaretIndex), false, Box.CaretIndex);
        hint.IsOpen = true;
        if (offerUpgrade) Announce(text + " " + Loc.T("Press Control Enter to add or upgrade the syntax header."));
    }

    private void HideHint() { hint.IsOpen = false; hintLine = -1; }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (Language != EditorSuggestionLanguage.Dockerfile && Language != EditorSuggestionLanguage.Compose) return;
        var p = e.GetPosition(Box);
        if (hover.IsOpen && (Math.Abs(p.X - hoverPoint.X) > 6 || Math.Abs(p.Y - hoverPoint.Y) > 6)) HideHover();
        if (list.IsOpen || PickerOpen || e.LeftButton == MouseButtonState.Pressed) return;
        hoverPoint = p;
        hoverTimer.Stop();
        hoverTimer.Start();
    }

    private void ShowHover()
    {
        if (list.IsOpen || PickerOpen || !Box.IsMouseOver) return;
        var index = Box.GetCharacterIndexFromPoint(hoverPoint, false);
        if (index < 0) return;
        var item = Language == EditorSuggestionLanguage.Dockerfile ? DockerfileCompletion.Hover(Box.Text, index) : ComposeCompletion.Hover(Box.Text, index);
        if (item == null) return;
        hoverBody.Children.Clear();
        hoverBody.Children.Add(Ui.Mono(item.Label, 13, "OnSurface", true));
        if (item.Detail.Length > 0) hoverBody.Children.Add(Ui.Mono(item.Detail, 12, "OnSurfaceVariant", true).Margin(0, 2, 0, 0));
        if (item.Doc.Length > 0) hoverBody.Children.Add(Ui.Text(item.Doc, 13).Margin(0, 6, 0, 0));
        if (item.Example.Length > 0) hoverBody.Children.Add(Ui.Mono(item.Example, 12, "OnSurfaceVariant", true).Margin(0, 6, 0, 0));
        if (item.MinSyntax != null) hoverBody.Children.Add(Ui.Text(Loc.T("Needs") + " # syntax=docker/dockerfile:" + item.MinSyntax + " " + Loc.T("or newer."), 12, null, "OnSurfaceVariant").Margin(0, 6, 0, 0));
        hover.HorizontalOffset = hoverPoint.X + 4;
        hover.VerticalOffset = hoverPoint.Y + 18;
        hover.IsOpen = true;
    }

    private void HideHover() { hoverTimer.Stop(); hover.IsOpen = false; }

    /// <summary>Places a popup under the character at <paramref name="index"/> (or under the caret line for hints).</summary>
    private void Position(Popup popup, int index, bool alignIcon, int? lineOf = null)
    {
        Box.UpdateLayout();
        var text = Box.Text;
        var rect = Box.GetRectFromCharacterIndex(Math.Clamp(index, 0, text.Length));
        if (lineOf is { } caret)
        {
            var caretRect = Box.GetRectFromCharacterIndex(Math.Clamp(caret, 0, text.Length));
            if (!caretRect.IsEmpty) rect = new Rect(rect.IsEmpty ? caretRect.Left : rect.Left, caretRect.Top, 1, caretRect.Height);
        }
        if (rect.IsEmpty) rect = new Rect(8, 8, 1, 16);
        popup.PlacementTarget = Box;
        popup.Placement = PlacementMode.Relative;
        var x = Math.Max(0, rect.Left - (alignIcon ? 36 : 0));
        var y = rect.Bottom + 2;
        // Keep the popup inside the window: shift it left when it would run past the right edge, and open it above the
        // caret line when there is not enough room below.
        if (Window.GetWindow(Box) is { } window && popup.Child is FrameworkElement child && window.Content is UIElement content)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = child.DesiredSize;
            var origin = Box.TranslatePoint(new Point(0, 0), content);
            var room = content.RenderSize;
            if (origin.X + x + size.Width > room.Width - 8) x = Math.Max(-origin.X + 8, room.Width - 8 - size.Width - origin.X);
            if (origin.Y + y + size.Height > room.Height - 8 && origin.Y + rect.Top - 2 - size.Height >= 8) y = rect.Top - 2 - size.Height;
        }
        popup.HorizontalOffset = x;
        popup.VerticalOffset = y;
    }

    private void CloseList() { list.Close(); current = null; explicitOpen = false; }

    private void CloseAll() { typingTimer.Stop(); CloseList(); HideHint(); HideHover(); upgradeOffered = false; }

    private void Announce(string text)
    {
        try
        {
            var peer = UIElementAutomationPeer.FromElement(Box) ?? UIElementAutomationPeer.CreatePeerForElement(Box);
            peer?.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantMostRecent, text, "EditorSuggestions");
        }
        catch (InvalidOperationException) { }
    }

    // ------------------------------------------------------------------ palette

    private static void RegisterCommands()
    {
        if (commandsRegistered || Host == null) return;
        commandsRegistered = true;
        Host.Commands.Register(new AppCommand
        {
            Id = "editor.suggest", Title = "Trigger suggestions", Group = "Editor", Icon = Icons.Code,
            Keywords = "suggestions completion autocomplete intellisense ctrl+space dockerfile compose editor",
            Run = () => RunOnLastFocused(s => s.Open(true)),
        });
        Host.Commands.Register(new AppCommand
        {
            Id = "editor.pick", Title = "Pick value at caret", Group = "Editor", Icon = Icons.Edit,
            Keywords = "picker colour color port duration permissions chmod user platform image digest ctrl+shift+space",
            Run = () => RunOnLastFocused(s => s.OpenPickerAtCaret()),
        });
        Host.Commands.Register(new AppCommand
        {
            Id = "editor.suggest.typing", Title = "Show suggestions while typing", Group = "Editor", Icon = Icons.ToggleOn,
            Keywords = "suggestions automatic completion setting",
            LiveControl = () => Ui.Switch("Show suggestions while typing", Host.Settings.SuggestionsWhileTyping, v => { Host.Settings.SuggestionsWhileTyping = v; Host.SaveSettings("Suggestions while typing " + (v ? "on" : "off")); }),
        });
    }

    private static void RunOnLastFocused(Action<EditorSuggestions> action)
    {
        if (lastFocused == null || !lastFocused.TryGetTarget(out var target) || !target.Box.IsVisible || target.Box.IsReadOnly)
        {
            Host.Notify.Info(Loc.T("Click into a Dockerfile or Compose editor first, then run this again."));
            return;
        }
        // Run after the palette has closed and focus has settled.
        target.Box.Dispatcher.BeginInvoke(() => { target.Box.Focus(); action(target); }, DispatcherPriority.ApplicationIdle);
    }
}
