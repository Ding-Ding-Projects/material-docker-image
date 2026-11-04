using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.Shell;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>The School mode rows of Data and privacy: the switch, its name, and the PIN or password that turns it off.</summary>
public sealed partial class SettingsPage
{
    private void SchoolRows()
    {
        var name = SchoolModeUi.Name;
        CheckBox? toggle = null; var reverting = false;
        async Task Change(bool v)
        {
            if (!await SchoolModeUi.RequestAsync(v)) { reverting = true; toggle!.IsChecked = !v; reverting = false; return; }
            S.SchoolMode = v; Commit(name + (v ? " on" : " off"));
        }
        toggle = SettingRows.Toggle(S.SchoolMode, v => { if (!reverting) _ = Change(v); });
        rows.Add("school", name, SchoolModeUi.Phrase("Uses English only and hides Cantonese wording, funny lead-ins and the dim sum surprise."), toggle, s => s.SchoolMode,
            SchoolModeUi.Phrase("Your language and funny-level choices are kept and come back when School mode is turned off. Turning it off asks for the PIN or password when one is set. One shared record serves every app on this computer."));
        rows.Add("schoolname", "Name of this mode", "Rename the mode. Every screen then uses your name and never the shipped one.",
            Ui.Button("Rename…", Icons.Edit, ButtonKind.Tonal, () => _ = SchoolModeUi.RenameAsync()), null,
            "The name is kept in the shared School mode record, so every app shows the same one.");
        var store = GameServices.School;
        var secretNote = Ui.Caption("");
        Button? secretButton = null;
        // The note and the button label follow the record after the dialog closes; they used to keep showing
        // "No PIN or password is set" right after one was saved.
        void ShowSecretState()
        {
            secretNote.Text = Loc.T(store.HasSecret ? "A PIN or password is set." : "No PIN or password is set, so the mode can be turned off freely.");
            if (secretButton?.Content is Panel { Children.Count: > 0 } content && content.Children[^1] is TextBlock label) label.Text = Loc.T(store.HasSecret ? "Change…" : "Set…");
        }
        secretButton = Ui.AsyncButton(store.HasSecret ? "Change…" : "Set…", Icons.Lock, ButtonKind.Tonal, async () => { await SchoolModeUi.SetSecretAsync(); ShowSecretState(); });
        ShowSecretState();
        rows.Add("schoolsecret", "PIN or password to turn it off", SchoolModeRules.Disclosure,
            Ui.VStack(6, secretButton, secretNote), null,
            "Kept only as a salted PBKDF2 hash in the shared record at " + PageParts.Sanitize(SchoolModeStore.SharedPath) + ". Deleting that file resets the mode, its name and its PIN.", true);
        // How the mode is turned off: the PIN or password above, or the Windows Hello passkey. Only the choice is stored.
        var methodNote = Ui.Caption("");
        Button? methodButton = null;
        void ShowMethodState()
        {
            var passkey = store.Method == SchoolOffMethod.Passkey;
            methodNote.Text = Loc.T(passkey ? "Windows Hello turns this mode off. If it is not available, the PIN or password is asked for instead." : "The PIN or password turns this mode off.");
            if (methodButton?.Content is Panel { Children.Count: > 0 } content && content.Children[^1] is TextBlock label) label.Text = Loc.T(passkey ? "Use PIN or password" : "Use Windows Hello");
        }
        methodButton = Ui.AsyncButton(store.Method == SchoolOffMethod.Passkey ? "Use PIN or password" : "Use Windows Hello", Icons.Lock, ButtonKind.Tonal,
            async () => { await SchoolModeUi.ChooseMethodAsync(store.Method == SchoolOffMethod.Passkey ? SchoolOffMethod.Secret : SchoolOffMethod.Passkey); ShowMethodState(); });
        ShowMethodState();
        rows.Add("schoolmethod", "Windows Hello passkey to turn it off", "Choose whether Windows Hello or the PIN or password turns the mode off. Windows shows its own verification dialog; nothing it checks is stored here.",
            Ui.VStack(6, methodButton, methodNote), null,
            "Only the choice is saved in the shared record. If Windows Hello is not set up or not available when the mode is turned off, that is said plainly and the PIN or password is asked for instead; with neither, the mode turns off without a check.", true);
    }
}
