using System.Windows;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

public sealed class StoragePage : PageBase
{
    public override string Key => "Storage";
    public StoragePage()
    {
        Layout(Ui.Header("Storage", "Volumes and networks", out _), Ui.EmptyState(Icons.Database, "Storage", "This destination is being rebuilt."));
    }
}
