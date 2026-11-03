using System.Windows;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

public sealed class MonitorPage : PageBase
{
    public override string Key => "Monitor";
    public MonitorPage()
    {
        Layout(Ui.Header("Monitor", "Live CPU, memory, network and disk charts", out _), Ui.EmptyState(Icons.Monitoring, "Monitor", "This destination is being rebuilt."));
    }
}
