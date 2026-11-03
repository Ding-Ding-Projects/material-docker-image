using System.Windows;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

public sealed class ToolsPage : PageBase
{
    public override string Key => "Tools";
    public ToolsPage()
    {
        Layout(Ui.Header("Tools", "Base64, documentation and changelog", out _), Ui.EmptyState(Icons.HomeRepairService, "Tools", "This destination is being rebuilt."));
    }
}
