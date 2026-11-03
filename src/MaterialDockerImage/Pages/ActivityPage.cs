using System.Windows;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

public sealed class ActivityPage : PageBase
{
    public override string Key => "Activity";
    public ActivityPage()
    {
        Layout(Ui.Header("Activity", "Local version history and notifications", out _), Ui.EmptyState(Icons.History, "Activity", "This destination is being rebuilt."));
    }
}
