using System.Windows;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

public sealed class ContainersPage : PageBase
{
    public override string Key => "Containers";
    public ContainersPage()
    {
        Layout(Ui.Header("Containers", "Container lifecycle, logs, stats, exec, files", out _), Ui.EmptyState(Icons.DeployedCode, "Containers", "This destination is being rebuilt."));
    }
}
