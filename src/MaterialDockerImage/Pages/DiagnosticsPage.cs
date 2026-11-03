using System.Windows;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

public sealed class DiagnosticsPage : PageBase
{
    public override string Key => "Diagnostics";
    public DiagnosticsPage()
    {
        Layout(Ui.Header("Diagnostics", "Engine checks, events and sanitized reports", out _), Ui.EmptyState(Icons.Stethoscope, "Diagnostics", "This destination is being rebuilt."));
    }
}
