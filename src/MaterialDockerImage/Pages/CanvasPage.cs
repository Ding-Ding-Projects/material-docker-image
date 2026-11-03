using System.Windows;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

public sealed class CanvasPage : PageBase
{
    public override string Key => "Canvas";
    public CanvasPage()
    {
        Layout(Ui.Header("Canvas", "Dockerfile stage graph and runtime topology", out _), Ui.EmptyState(Icons.AccountTree, "Canvas", "This destination is being rebuilt."));
    }
}
