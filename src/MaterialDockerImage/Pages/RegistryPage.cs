using System.Windows;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

public sealed class RegistryPage : PageBase
{
    public override string Key => "Registry";
    public RegistryPage()
    {
        Layout(Ui.Header("Registry", "Registry sign-in, Docker Hub search and builders", out _), Ui.EmptyState(Icons.Cloud, "Registry", "This destination is being rebuilt."));
    }
}
