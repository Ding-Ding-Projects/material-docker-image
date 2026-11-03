using System.Windows;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

public sealed class BuildPage : PageBase
{
    public override string Key => "Build";
    public BuildPage()
    {
        Layout(Ui.Header("Build", "Build images from a Dockerfile and context folder", out _), Ui.EmptyState(Icons.Construction, "Build", "This destination is being rebuilt."));
    }
}
