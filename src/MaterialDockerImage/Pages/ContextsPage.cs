using System.Windows;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

public sealed class ContextsPage : PageBase
{
    public override string Key => "Contexts";
    public ContextsPage()
    {
        Layout(Ui.Header("Contexts", "Engine contexts and plugins", out _), Ui.EmptyState(Icons.DeviceHub, "Contexts", "This destination is being built."));
    }
}
