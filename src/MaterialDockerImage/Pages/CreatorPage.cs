using System.Windows;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

public sealed class CreatorPage : PageBase
{
    public override string Key => "Creator";
    public CreatorPage()
    {
        Layout(Ui.Header("Creator", "Create a Dockerfile step by step with live checks", out _), Ui.EmptyState(Icons.Code, "Creator", "This destination is being rebuilt."));
    }
}
