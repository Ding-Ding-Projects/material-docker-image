using System.Windows;
using System.Windows.Controls.Primitives;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Small outlined action chips (radius 8, 13 px) used for the starters and the quick-add row of the Creator.</summary>
internal static class CreatorChips
{
    /// <summary>A chip that runs an action when clicked and returns to its resting state, like a button with the chip look.</summary>
    public static ToggleButton Action(string label, Action run, double height = 32, bool translate = true)
    {
        var chip = Ui.Chip(label, false, _ => { });
        if (!translate) chip.Content = label;
        chip.Height = height;
        System.Windows.Automation.AutomationProperties.SetName(chip, chip.Content as string ?? label);
        chip.Click += (_, _) => { chip.IsChecked = false; Ui.Guard(run); };
        return chip;
    }
}
