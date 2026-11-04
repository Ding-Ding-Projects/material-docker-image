using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Chip input for image tags. Enter, comma or Tab commits the typed text; Backspace in an empty field removes the
/// last chip. Typed text that has not been committed is still included by <see cref="Tags"/>.
/// </summary>
public sealed class TagInput : Border
{
    private static readonly Regex Reference = new(@"^(?:[A-Za-z0-9.-]+(?::[0-9]+)?/)?[a-z0-9]+(?:(?:[._]|__|-+)[a-z0-9]+)*(?:/[a-z0-9]+(?:(?:[._]|__|-+)[a-z0-9]+)*)*(?::[A-Za-z0-9_][A-Za-z0-9_.-]{0,127})?$", RegexOptions.None, TimeSpan.FromMilliseconds(100));
    private readonly WrapPanel panel = new();
    private readonly TextBox entry = new() { Style = (Style)Application.Current.FindResource("Field.Search"), MinWidth = 140, MinHeight = 32 };
    private readonly TextBlock error = Ui.Text("", 12, null, "Error");
    private readonly List<string> tags = new();
    public event Action? Changed;
    public TextBlock ErrorText => error;

    public TagInput()
    {
        CornerRadius = new CornerRadius(8); BorderThickness = new Thickness(1); Padding = new Thickness(6, 4, 6, 4); MinHeight = 40;
        SetResourceReference(BackgroundProperty, "SurfaceLowest");
        SetResourceReference(BorderBrushProperty, "OutlineVariant");
        entry.SetResourceReference(Control.FontFamilyProperty, "MonoFont");
        System.Windows.Automation.AutomationProperties.SetName(entry, Loc.T("Add tag"));
        entry.ToolTip = Loc.T("Type a tag such as my-app:1.0 and press Enter or comma.");
        panel.Children.Add(entry);
        Child = panel;
        error.Visibility = Visibility.Collapsed;
        entry.PreviewKeyDown += (_, e) =>
        {
            if ((e.Key is Key.Enter || (e.Key == Key.Tab && entry.Text.Trim().Length > 0)) && Keyboard.Modifiers == ModifierKeys.None) { Commit(); e.Handled = e.Key == Key.Enter || error.Visibility == Visibility.Visible; }
            else if (e.Key == Key.Back && entry.Text.Length == 0 && tags.Count > 0) { Remove(tags[^1]); e.Handled = true; }
        };
        entry.TextChanged += (_, _) =>
        {
            if (entry.Text.Contains(',') || entry.Text.Contains('\n')) Commit();
            else Changed?.Invoke();
        };
        entry.LostKeyboardFocus += (_, _) => { if (entry.Text.Trim().Length > 0) Commit(); };
        GotKeyboardFocus += (_, _) => SetResourceReference(BorderBrushProperty, "Primary");
        LostKeyboardFocus += (_, _) => SetResourceReference(BorderBrushProperty, "OutlineVariant");
        MouseLeftButtonUp += (_, _) => entry.Focus();
    }

    public static string? Validate(string tag)
    {
        if (tag.StartsWith('-')) return "Tags cannot begin with a hyphen.";
        if (tag.Length > 255 || !Reference.IsMatch(tag)) return $"\"{tag}\" is not a valid image reference. Use lowercase name[:tag], for example my-app:1.0 or registry.example.com/team/app:2.1.";
        return null;
    }

    public IReadOnlyList<string> Tags
    {
        get
        {
            var all = new List<string>(tags);
            foreach (var pending in Split(entry.Text)) if (!all.Contains(pending)) all.Add(pending);
            return all;
        }
    }

    public void SetTags(IEnumerable<string> values)
    {
        tags.Clear();
        foreach (var chip in panel.Children.OfType<Border>().ToList()) panel.Children.Remove(chip);
        foreach (var v in values) Add(v, false);
        entry.Clear();
        Changed?.Invoke();
    }

    public static IEnumerable<string> Split(string text) => text.Split([',', '\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private void Commit()
    {
        var problems = new List<string>();
        var rejected = new List<string>();
        foreach (var value in Split(entry.Text))
        {
            var problem = Validate(value);
            if (problem != null) { problems.Add(problem); rejected.Add(value); continue; }
            Add(value, false);
        }
        entry.Text = string.Join(", ", rejected);
        entry.CaretIndex = entry.Text.Length;
        error.Text = string.Join(" ", problems);
        error.Visibility = problems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Changed?.Invoke();
    }

    private void Add(string tag, bool raise)
    {
        if (tags.Contains(tag)) return;
        tags.Add(tag);
        var text = Ui.Mono(tag, 13, "OnSecondaryContainer");
        var remove = Ui.IconButton(Icons.Close, Loc.T("Remove tag") + " " + tag, () => Remove(tag), "OnSecondaryContainer");
        remove.Width = remove.Height = remove.MinHeight = remove.MinWidth = 24; remove.FontSize = 14; remove.Margin = new Thickness(4, 0, -4, 0);
        var chip = new Border { Child = Ui.HStack(0, text, remove), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 2, 6, 2), Margin = new Thickness(0, 2, 6, 2), Tag = tag };
        chip.SetResourceReference(BackgroundProperty, "SecondaryContainer");
        panel.Children.Insert(panel.Children.Count - 1, chip);
        if (raise) Changed?.Invoke();
    }

    private void Remove(string tag)
    {
        tags.Remove(tag);
        foreach (var chip in panel.Children.OfType<Border>().Where(b => Equals(b.Tag, tag)).ToList()) panel.Children.Remove(chip);
        entry.Focus();
        Changed?.Invoke();
    }
}
