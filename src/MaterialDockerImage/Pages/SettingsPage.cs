using System.Windows;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

public sealed class SettingsPage : PageBase
{
    public override string Key => "Settings";
    public SettingsPage()
    {
        Layout(Ui.Header("Settings", "Appearance, language, accessibility and more", out _), Ui.EmptyState(Icons.Settings, "Settings", "This destination is being rebuilt."));
    }
}
