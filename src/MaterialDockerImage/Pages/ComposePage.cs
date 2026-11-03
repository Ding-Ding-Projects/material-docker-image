using System.Windows;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

public sealed class ComposePage : PageBase
{
    public override string Key => "Compose";
    public ComposePage()
    {
        Layout(Ui.Header("Compose", "Compose projects, services and source", out _), Ui.EmptyState(Icons.Stacks, "Compose", "This destination is being rebuilt."));
    }
}
