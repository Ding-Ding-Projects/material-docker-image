using System.Windows;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

public sealed class SetupPage : PageBase
{
    public override string Key => "Setup";
    public SetupPage()
    {
        Layout(Ui.Header("Setup", "First-run setup wizard", out _), Ui.EmptyState(Icons.RocketLaunch, "Setup", "This destination is being rebuilt."));
    }
}
