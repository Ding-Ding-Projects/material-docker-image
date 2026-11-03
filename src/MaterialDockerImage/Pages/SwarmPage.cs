using System.Windows;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

public sealed class SwarmPage : PageBase
{
    public override string Key => "Swarm";
    public SwarmPage()
    {
        Layout(Ui.Header("Swarm", "Swarm services, stacks, nodes, secrets and configs", out _), Ui.EmptyState(Icons.Hub, "Swarm", "This destination is being built."));
    }
}
