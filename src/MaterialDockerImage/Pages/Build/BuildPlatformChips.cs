using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Multi-select platform chips for the common targets plus a field for any other os/arch[/variant] values.
/// No selection means the builder's native platform.
/// </summary>
public sealed class BuildPlatformChips : StackPanel
{
    private readonly List<ToggleButton> chips = new();
    private readonly TextBox other = Ui.Input("", "Other platforms", true);
    private readonly TextBlock error = Ui.Text("", 12, null, "Error");
    private bool silent;
    public event Action? Changed;

    public BuildPlatformChips()
    {
        var wrap = new WrapPanel();
        foreach (var platform in BuildKitCommand.KnownPlatforms)
        {
            var chip = Ui.Chip(platform, false, _ => Raise());
            chip.Tag = platform;
            chip.Margin = new Thickness(0, 0, 6, 6);
            chip.SetResourceReference(Control.FontFamilyProperty, "MonoFont");
            chips.Add(chip);
            wrap.Children.Add(chip);
        }
        other.ToolTip = Loc.T("Other platforms, separated by commas (for example linux/ppc64le).");
        other.TextChanged += (_, _) => Raise();
        error.Visibility = Visibility.Collapsed;
        Children.Add(wrap);
        Children.Add(Ui.Columns(8, [0, 1], Ui.Caption("Other"), other).Margin(0, 2, 0, 0));
        Children.Add(error);
    }

    /// <summary>Selected platforms in chip order, then the typed ones. Does not validate.</summary>
    public IReadOnlyList<string> Platforms
    {
        get
        {
            var list = chips.Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToList();
            foreach (var p in (other.Text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (!list.Contains(p, StringComparer.OrdinalIgnoreCase)) list.Add(p);
            return list;
        }
    }

    /// <summary>Sets the selection from a comma-separated list (used when resuming or from a bake target).</summary>
    public void SetPlatforms(IEnumerable<string> values)
    {
        silent = true;
        try
        {
            var set = values.Select(v => v.Trim()).Where(v => v.Length > 0).ToList();
            foreach (var chip in chips) chip.IsChecked = set.Remove((string)chip.Tag);
            other.Text = string.Join(", ", set);
        }
        finally { silent = false; }
        Raise();
    }

    private void Raise()
    {
        if (silent) return;
        var problem = Platforms.Select(BuildKitCommand.ValidatePlatform).FirstOrDefault(p => p != null);
        error.Text = problem ?? "";
        error.Visibility = problem == null ? Visibility.Collapsed : Visibility.Visible;
        Changed?.Invoke();
    }
}
