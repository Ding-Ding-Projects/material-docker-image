using System.Windows;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

public sealed class BakePage : PageBase
{
    public override string Key => "Bake";
    public BakePage()
    {
        Layout(Ui.Header("Bake", "Bake files: list, inspect and build targets", out _), Ui.EmptyState(Icons.Workspaces, "Bake", "This destination is being built."));
    }
}
