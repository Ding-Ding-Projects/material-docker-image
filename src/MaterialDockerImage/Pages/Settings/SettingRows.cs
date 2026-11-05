using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MaterialDockerImage.Services;
using MaterialDockerImage.Shell;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>One settings row: label, one-sentence explanation, optional info, control and a provenance line.</summary>
public sealed class SettingRow
{
    public required string Id { get; init; }
    public required string Section { get; init; }
    public required string Label { get; init; }
    public required string Explanation { get; init; }
    public string? Info { get; init; }
    public required FrameworkElement Element { get; init; }
    public TextBlock? Provenance { get; init; }
    public Func<UserSettings, object?>? Value { get; init; }
    public Func<object?, string>? Format { get; init; }
    public string SearchText => Section + " " + Label + " " + Loc.T(Label) + " " + Explanation + " " + Info;
}

/// <summary>Builds settings rows, keeps their provenance lines current and saves changes immediately.</summary>
internal sealed class SettingRows
{
    private static AppHost Host => AppHost.Current;
    private static readonly UserSettings Defaults = new();
    private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private string? pendingHistory;
    public List<SettingRow> All { get; } = new();
    public string Section { get; set; } = "";
    /// <summary>Wraps every save so the owner can ignore its own SettingsChanged echo.</summary>
    public Action<Action> Guard { get; init; } = save => save();

    public SettingRows()
    {
        debounce.Tick += (_, _) => { debounce.Stop(); if (pendingHistory != null) { var what = pendingHistory; pendingHistory = null; Guard(() => Host.SaveSettings(what)); } };
    }

    /// <summary>Applies runtime state that lives outside the settings object, then saves and records the change.</summary>
    public void Commit(string what, bool coalesce = false)
    {
        SyncRuntime();
        if (coalesce)
        {
            // Sliders save quietly while moving and record one history revision when they settle.
            Guard(() => Host.SaveSettings(null));
            pendingHistory = what;
            debounce.Stop(); debounce.Start();
        }
        else
        {
            if (pendingHistory != null) { pendingHistory = null; debounce.Stop(); }
            Guard(() => Host.SaveSettings(what));
        }
        UpdateProvenance();
    }

    public static void SyncRuntime()
    {
        var s = Host.Settings;
        Loc.Language = s.Language; Loc.FunnyEnglish = s.FunnyEnglish; Loc.FunnyCantonese = s.FunnyCantonese;
        Loc.DialogEmoji = s.DialogEmoji; Loc.SchoolMode = s.SchoolMode;
        Narrator.Voice = s.NarrationVoice; Narrator.Rate = s.NarrationRate;
    }

    public void UpdateProvenance()
    {
        foreach (var row in All) if (row.Provenance != null && row.Value != null) row.Provenance.Text = ProvenanceText(row.Value, row.Format);
    }

    public static string ProvenanceText(Func<UserSettings, object?> value, Func<object?, string>? format)
    {
        var fallback = value(Defaults);
        var shown = format?.Invoke(fallback) ?? Show(fallback);
        return Same(value(Host.Settings), fallback) ? $"Default: {shown} (built-in default)" : $"Saved in settings.json · default {shown}";
    }

    private static string FormatDefault(Func<UserSettings, object?> value, Func<object?, string>? format)
    {
        var fallback = value(Defaults);
        return format?.Invoke(fallback) ?? Show(fallback);
    }

    private static bool Same(object? a, object? b) => a is double x && b is double y ? Math.Abs(x - y) < 0.001 : Equals(a, b);

    private static string Show(object? value) => value switch
    {
        null => "none",
        bool b => b ? "On" : "Off",
        string { Length: 0 } => "not set",
        double d => d.ToString("0.#"),
        _ => value.ToString() ?? ""
    };

    /// <summary>Adds a row. Narrow controls sit to the right of the text; wide ones go below it.</summary>
    public SettingRow Add(string id, string label, string explanation, FrameworkElement control, Func<UserSettings, object?>? value, string? info = null, bool wide = false, Func<object?, string>? format = null, string? example = null)
    {
        var hidden = SchoolHiddenControls.HidesSetting(id, Host.Settings.SchoolMode);
        var provenance = value == null ? null : Ui.Text(ProvenanceText(value, format), 12, null, "Outline");
        // A wrap panel offers the label the row width, so a long (bilingual) label wraps instead of running out of the card.
        var title = new WrapPanel();
        title.Children.Add(Ui.Text(label, 14, FontWeights.SemiBold));
        // Every saved setting gets an info icon: hand-written guidance when the row has it, otherwise a generic default-and-undo note.
        info ??= value == null ? null : InfoGuidance.ForSavedSetting(explanation, FormatDefault(value, format));
        if (info != null) title.Children.Add(Ui.Info(label, info, example));
        System.Windows.Automation.AutomationProperties.SetName(control, Loc.T(label));
        System.Windows.Automation.AutomationProperties.SetHelpText(control, explanation);
        var lockNote = Ui.Caption("");
        lockNote.Visibility = Visibility.Collapsed;
        var text = Ui.VStack(2, title, Ui.Caption(explanation), provenance, lockNote);
        if (value != null) AddLock(id, label, title, lockNote, control);
        FrameworkElement layout;
        if (wide) layout = Ui.VStack(10, text, control);
        else
        {
            // Control beside the text when there is room, below it when the row is narrow.
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            control.VerticalAlignment = VerticalAlignment.Center;
            control.MaxWidth = 380;
            grid.Children.Add(text);
            grid.Children.Add(control);
            void Arrange()
            {
                var narrow = grid.ActualWidth > 0 && grid.ActualWidth < 600;
                control.At(narrow ? 1 : 0, narrow ? 0 : 1);
                control.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                control.Margin = narrow ? new Thickness(0, 10, 0, 0) : new Thickness(16, 0, 0, 0);
            }
            grid.SizeChanged += (_, _) => Arrange();
            Arrange();
            layout = grid;
        }
        var border = new Border { Child = layout, Padding = new Thickness(0, 12, 0, 12), BorderThickness = new Thickness(0, 0, 0, 1) };
        border.SetResourceReference(Border.BorderBrushProperty, "SurfaceHigh");
        if (value != null) LockUi.AttachMenu(border, id, label, Guard);
        var row = new SettingRow { Id = id, Section = Section, Label = label, Explanation = explanation, Info = info, Element = border, Provenance = provenance, Value = value, Format = format };
        // Under School mode a hidden control is absent from the page and from search, not shown disabled.
        if (!hidden) All.Add(row);
        return row;
    }

    /// <summary>
    /// Adds the lock button to a row's title. A locked row is read-only: its control is disabled and the note says how to unlock it.
    /// Unlocking asks for a confirmation. A lock is a speed bump for oneself, not security, and the confirmation says so.
    /// </summary>
    private void AddLock(string id, string label, Panel title, TextBlock note, FrameworkElement control)
    {
        Button? button = null;
        LockUi.SetClickTarget(control, id, label);
        void Apply()
        {
            var locked = ElementLocks.IsLocked(Host.Settings, id);
            control.IsEnabled = !locked;
            note.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
            note.Text = locked ? Loc.T("Locked. Unlock this setting to change it. " + ElementLocks.Disclosure) : "";
            if (button != null)
            {
                button.Content = locked ? Icons.Lock : Icons.LockOpen;
                System.Windows.Automation.AutomationProperties.SetName(button, Loc.T(locked ? "Unlock this setting: " : "Lock this setting: ") + Loc.T(label));
                button.ToolTip = Loc.T(locked ? "Locked. Select to unlock." : "Lock this setting against changes");
            }
        }
        button = Ui.IconButton(Icons.LockOpen, label, () => { _ = LockUi.ToggleAsync(id, label, Guard); });
        LockUi.Subscribe(id, button, Apply);
        button.Margin = new Thickness(4, -6, 0, -6);
        button.Width = button.Height = button.MinHeight = 28; button.FontSize = 16;
        title.Children.Add(button);
        Apply();
    }

    /// <summary>A switch with no label of its own (the row shows the label).</summary>
    public static CheckBox Toggle(bool value, Action<bool> changed)
    {
        var c = new CheckBox { Style = (Style)Application.Current.FindResource("Switch"), IsChecked = value, HorizontalAlignment = HorizontalAlignment.Right };
        c.Checked += (_, _) => changed(true); c.Unchecked += (_, _) => changed(false);
        return c;
    }

    public static Slider Slider(double min, double max, double value, Action<double> changed, double width = 220)
    {
        var s = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = 1, IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 1, Width = width };
        s.ValueChanged += (_, e) => { if (Math.Abs(e.NewValue - e.OldValue) > 0.0001) changed(s.Value); };
        return s;
    }
}
