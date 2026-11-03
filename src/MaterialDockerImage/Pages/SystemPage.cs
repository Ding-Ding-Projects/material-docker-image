using System.Windows;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

public sealed class SystemPage : PageBase
{
    public override string Key => "System";
    public SystemPage()
    {
        Layout(Ui.Header("System", "Disk usage, cleanup and engine information", out _), Ui.EmptyState(Icons.Dns, "System", "This destination is being rebuilt."));
    }
}
