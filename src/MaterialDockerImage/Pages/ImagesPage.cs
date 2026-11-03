using System.Windows;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

public sealed class ImagesPage : PageBase
{
    public override string Key => "Images";
    public ImagesPage()
    {
        Layout(Ui.Header("Images", "Local image inventory, pull, tag, push, export", out _), Ui.EmptyState(Icons.Layers, "Images", "This destination is being rebuilt."));
    }
}
