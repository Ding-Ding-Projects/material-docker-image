using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Shell;

/// <summary>
/// A slim, non-blocking strip above the workspace that appears only when an update has been staged. It names the build as unsigned,
/// never restarts anything by itself, and can be dismissed for this session.
/// </summary>
public sealed class UpdateBanner
{
    private readonly ContentControl slot;
    private readonly AppHost host;
    private bool dismissed;

    public UpdateBanner(ContentControl slot, AppHost host)
    {
        this.slot = slot; this.host = host;
        host.Updates.Changed += () => slot.Dispatcher.BeginInvoke(Render);
    }

    private void Render()
    {
        if (dismissed || host.Updates.State != UpdateState.Ready) { slot.Content = null; return; }
        var version = host.Updates.ReadyVersion;
        var restart = Ui.Button("Restart to update", Icons.RestartAlt, ButtonKind.Filled, async () =>
        {
            if (!await host.Overlay.ConfirmAsync("Restart to finish updating?", Loc.T("This closes the app and opens version") + " " + version + ". " + Loc.T("Save or finish any work first. Nothing is restarted unless you confirm."), "Restart now")) return;
            if (host.Updates.RestartIntoUpdate()) Application.Current.Shutdown();
            else host.Notify.Error("The updated version could not be started. The current version keeps working.");
        });
        var later = Ui.Button("Later", null, ButtonKind.Text, () => { dismissed = true; Render(); });
        var text = Ui.Text(Loc.T("Version") + " " + version + " " + Loc.T("is ready.") + " " + Loc.T("It will start the next time you open the app, or you can restart now. This build is unsigned, so Windows may show an unknown publisher warning."), 13, null, "OnPrimaryContainer", true, false);
        var row = Ui.Columns(12, [0, 1, 0, 0], Ui.Icon(Icons.Info, 20, true, "OnPrimaryContainer"), text, restart, later);
        var card = Ui.Card(row, 10, "PrimaryContainer", 12);
        card.Margin = new Thickness(0, 0, 0, 10);
        System.Windows.Automation.AutomationProperties.SetName(card, Loc.T("Update ready") + ": " + version);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(card, System.Windows.Automation.AutomationLiveSetting.Polite);
        slot.Content = card;
    }
}
