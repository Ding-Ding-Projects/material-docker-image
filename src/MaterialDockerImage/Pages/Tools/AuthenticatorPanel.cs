using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.Shell;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// The app's own authenticator for its own locks: pair an authenticator app with a lock, check a code, review the lock list
/// and run the RFC 6238 self-test. It works offline and never sends a secret anywhere.
/// </summary>
internal sealed class AuthenticatorPanel
{
    private static AppHost Host => AppHost.Current;
    /// <summary>The Authenticator tab: general accounts first, with the lock pairing tools beside them.</summary>
    public FrameworkElement Root { get; }
    private readonly FrameworkElement lockPane;
    private readonly ComboBox lockId;
    private readonly StackPanel list = new();
    private readonly TextBox check = Ui.Input("", "Code to check", true);
    private readonly TextBlock result = Ui.Text("", 13, null, "OnSurfaceVariant");
    private readonly TextBlock selfTest = Ui.Mono("", 12, "OnSurfaceVariant", true);

    public AuthenticatorPanel()
    {
        var ids = ElementLocks.Properties.Keys.Concat(Host.Locks.Records.Select(r => r.Id)).Concat(Host.Settings.LockedSettings).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        lockId = Ui.Combo(ids, ids.FirstOrDefault(), true);
        System.Windows.Automation.AutomationProperties.SetName(lockId, Loc.T("Lock to pair"));
        check.MaxLength = 7;
        var setup = Ui.Button("Set up authenticator for this lock", Icons.ShieldLock, ButtonKind.Filled, () => _ = SetUpAsync());
        var checkButton = Ui.Button("Check code", Icons.CheckCircle, ButtonKind.Tonal, Check);
        var test = Ui.Button("Run RFC 6238 self-test", Icons.CheckCircle, ButtonKind.Outlined, RunSelfTest);
        lockPane = Ui.Scroll(Ui.VStack(18,
            Ui.Title("Authenticator for this app's locks", 18),
            Ui.Caption("Pair an authenticator app with one of this app's locks. The secret is made here, shown once as a QR code and text, and stored protected for your Windows account. Nothing is sent anywhere."),
            Ui.Caption("This only unlocks controls inside this app. A lock is a speed bump you set for yourself, and an authenticator for a lock on this same computer is mostly for fun."),
            Ui.Card(Ui.VStack(12,
                Ui.Field("Lock to pair", lockId, "Pick a settings row or type the id of any lock, for example language or btn.build.build-image. The lock is not turned on by this; lock the control afterwards and it will ask for the code."),
                setup), 16, "SurfaceLow", 16),
            Ui.Card(Ui.VStack(10,
                Ui.Title("Check a code", 16),
                Ui.Caption("Checks a code for the lock above without using it up and without counting as an unlock attempt."),
                Ui.Field("Code to check", check), Ui.HStack(8, checkButton), result), 16, "SurfaceLow", 16),
            Ui.Card(Ui.VStack(10, Ui.Title("Locks with a policy", 16), list), 16, "SurfaceLow", 16),
            Ui.Card(Ui.VStack(10, Ui.Title("Standards check", 16),
                Ui.Caption("Computes the published RFC 6238 test vectors for SHA-1, SHA-256 and SHA-512 with this build's own code."), Ui.HStack(8, test), selfTest), 16, "SurfaceLow", 16)));
        var accounts = new AuthenticatorAccountsPanel().Root;
        var holder = new ContentControl { Focusable = false, Content = accounts };
        var sections = Ui.Segmented(["My accounts", "App locks"], "My accounts", name => holder.Content = name == "App locks" ? lockPane : accounts);
        sections.HorizontalAlignment = HorizontalAlignment.Left; sections.MinWidth = 320;
        Root = Ui.Rows(12, [0, 1], sections, holder);
        Render();
    }

    private string Id => (lockId.Text ?? "").Trim();

    private async Task SetUpAsync()
    {
        var id = Id;
        if (!ElementLocks.IsValidId(id)) { Host.Notify.Warning("A lock id uses lower-case letters, digits, dots, hyphens and underscores, up to 40 characters."); return; }
        if (Host.IsLocked(id)) { Host.Notify.Warning("Unlock this control before pairing a new authenticator."); return; }
        if (!DpapiProtector.IsAvailable) { Host.Notify.Error("Authenticator locks need Windows secret protection, which is not available here."); return; }
        if (Host.Locks.PolicyOf(id) != LockPolicy.None && !await Host.Overlay.ConfirmAsync("Replace this lock's policy?", id + "\n\n" + Loc.T("Its current PIN or authenticator and its recovery codes will stop working."), "Replace", true)) return;
        var setup = new LockDialogs.TotpSetup(id);
        var done = new TaskCompletionSource<bool>();
        Action? close = null;
        var error = Ui.Text("", 13, null, "Error");
        var actions = Ui.HStack(8,
            Ui.Button("Cancel", null, ButtonKind.Text, () => { done.TrySetResult(false); close!(); }),
            Ui.Button("Arm authenticator", Icons.Lock, ButtonKind.Filled, () =>
            {
                if (!setup.CodeMatches()) { error.Text = Loc.T("That code did not match. Check the app and try the current code."); return; }
                IReadOnlyList<string>? codes;
                try { codes = Host.Locks.Configure(id, LockPolicy.Authenticator, null, setup.Secret); }
                catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException) { error.Text = e.Message; return; }
                close!(); done.TrySetResult(true);
                if (codes != null) _ = LockDialogs.ShowRecoveryCodesAsync(id, codes);
            }));
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        close = Host.Overlay.Show(Ui.VStack(14, Ui.Title(Loc.T("Pair an authenticator") + ": " + id, 20), setup.Root, error, LockDialogs.RecoveryFooter(), actions), 560, () => done.TrySetResult(false));
        if (await done.Task) { Host.Notify.Success("Authenticator armed for this lock."); Render(); }
    }

    private void Check()
    {
        var id = Id;
        if (!LockPolicies.Factors(Host.Locks.PolicyOf(id)).HasFlag(LockFactors.Totp)) { result.Text = Loc.T("This lock has no authenticator yet."); return; }
        result.Text = Host.Locks.CheckCode(id, check.Text) ? Loc.T("That code is valid right now.") : Loc.T("That code does not match. It may be old, or this computer's clock may be off.");
        check.Clear();
    }

    private void RunSelfTest()
    {
        var (passed, total, failures) = Rfc6238Vectors.Run();
        selfTest.Text = failures.Count == 0 ? Loc.T($"All {total} RFC 6238 vectors match.") : Loc.T($"{passed} of {total} vectors match.") + "\n" + string.Join("\n", failures);
    }

    private void Render()
    {
        list.Children.Clear();
        var records = Host.Locks.Records.OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
        if (records.Count == 0) { list.Children.Add(Ui.Caption("No lock has a PIN, password or authenticator yet. Lock a control from its right-click menu to choose one.")); return; }
        foreach (var record in records)
        {
            var id = record.Id;
            var policy = LockPolicies.Label(record.Policy);
            var line = Ui.HStack(10, Ui.Mono(id, 13, "OnSurface", false), Ui.Badge(policy, Tone.Neutral), Ui.Badge(Host.IsLocked(id) ? "Locked" : "Unlocked", Host.IsLocked(id) ? Tone.Warning : Tone.Success),
                Ui.Caption(Loc.T("Recovery codes left:") + " " + record.RecoveryHashes.Count));
            var remove = Ui.Button("Remove policy", Icons.Delete, ButtonKind.Text, () => _ = RemoveAsync(id));
            list.Children.Add(Ui.Wrap(8, line, remove));
        }
    }

    private async Task RemoveAsync(string id)
    {
        await LockUi.RemovePolicyAsync(id, id);
        Render();
    }
}
