using System.Windows;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

public sealed class SecurityPage : PageBase
{
    public override string Key => "Security";
    public SecurityPage()
    {
        Layout(Ui.Header("Security", "Vulnerabilities, SBOMs and provenance for local images", out _), Ui.EmptyState(Icons.Security, "Security", "This destination is being built."));
    }
}
