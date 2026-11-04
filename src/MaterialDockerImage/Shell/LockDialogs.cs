using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Shell;

/// <summary>The dialogs behind element locks: choosing a policy, pairing an authenticator, showing recovery codes and climbing the unlock ladder.</summary>
public static class LockDialogs
{
    private static AppHost Host => AppHost.Current;
    public const string Issuer = "MaterialDockerImage";

    /// <summary>The QR drawn from vector squares with a four-module quiet zone and true black on white, in both themes.</summary>
    public static FrameworkElement QrView(string text, string description)
    {
        var modules = QrCode.Encode(text);
        var n = modules.GetLength(0); var total = n + 2 * QrCode.QuietZone;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
            for (var r = 0; r < n; r++)
                for (var c = 0; c < n; c++)
                {
                    if (!modules[r, c]) continue;
                    double x = c + QrCode.QuietZone, y = r + QrCode.QuietZone;
                    context.BeginFigure(new Point(x, y), true, true);
                    context.LineTo(new Point(x + 1, y), false, false); context.LineTo(new Point(x + 1, y + 1), false, false); context.LineTo(new Point(x, y + 1), false, false);
                }
        geometry.Freeze();
        var path = new System.Windows.Shapes.Path { Data = geometry, Fill = Brushes.Black, SnapsToDevicePixels = false };
        var canvas = new Canvas { Width = total, Height = total, Background = Brushes.White };
        canvas.Children.Add(path);
        var box = new Viewbox { Child = canvas, Width = 224, Height = 224, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
        System.Windows.Automation.AutomationProperties.SetName(box, Loc.T(description));
        return box;
    }

    /// <summary>
    /// The pairing block shared by the lock wizard and the Authenticator tool: the secret is made here, shown only after an explicit
    /// action (as a QR, the otpauth text and grouped Base32), and the factor arms only when one current code is typed back.
    /// </summary>
    public sealed class TotpSetup
    {
        public byte[] Secret { get; } = Totp.NewSecret();
        public FrameworkElement Root { get; }
        public TextBox Code { get; } = Ui.Input("", "Code from your authenticator", true);
        public Func<bool> CodeMatches => () => Totp.Verify(Secret, Code.Text, DateTimeOffset.UtcNow) >= 0;

        public TotpSetup(string account)
        {
            var uri = Totp.Uri(Issuer, account, Secret);
            var reveal = Ui.VStack(10);
            var shown = false;
            var show = Ui.Button("Show secret and QR code", Icons.Lock, ButtonKind.Tonal);
            show.Click += (_, _) =>
            {
                if (shown) return; shown = true; show.Visibility = Visibility.Collapsed;
                reveal.Children.Add(QrView(uri, "QR code that pairs an authenticator app with the lock for " + account + ". Scan it, or type the text secret beside it."));
                reveal.Children.Add(Ui.Caption("Type this instead if you cannot scan. SHA-1, 6 digits, 30 seconds."));
                var spaced = Ui.Mono(Base32.Group(Base32.Encode(Secret)), 15, "OnSurface", true);
                spaced.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, Loc.T("Authenticator secret"));
                reveal.Children.Add(spaced);
                reveal.Children.Add(Ui.HStack(8,
                    Ui.Button("Copy secret", Icons.ContentCopy, ButtonKind.Text, () => Copy(Base32.Encode(Secret))),
                    Ui.Button("Copy otpauth link", Icons.ContentCopy, ButtonKind.Text, () => Copy(uri))));
                reveal.Children.Add(Ui.Caption("This secret is shown only now. It is stored protected for your Windows account and never written to a log, export or history."));
            };
            Code.MaxLength = 7;
            Root = Ui.VStack(10, Ui.Caption("Scan the QR code with an authenticator app, then type the 6-digit code it shows to arm the lock."), show, reveal, Ui.Field("Code from your authenticator", Code));
        }
    }

    public static void Copy(string text)
    {
        try { Clipboard.SetText(text); Host.Notify.Success("Copied to the clipboard."); }
        catch (System.Runtime.InteropServices.ExternalException) { Host.Notify.Warning("The clipboard is busy. Try again."); }
    }

    private static string Folder => UserSettings.DataDirectory;

    /// <summary>The recovery line every lock surface shows: the actual folder, a copy button and the honest nature of the lock.</summary>
    public static FrameworkElement RecoveryFooter()
    {
        var copy = Ui.Button("Copy folder path", Icons.ContentCopy, ButtonKind.Text, () => Copy(Folder));
        var open = Ui.Button("Open folder", Icons.FolderOpen, ButtonKind.Text, () => Pages.PageParts.OpenFolder(Folder));
        var tickets = Ui.Button("Support Tickets…", Icons.Help, ButtonKind.Text, () => SupportDeskUi.Show());
        return Ui.VStack(6,
            Ui.Caption(ElementLocks.Disclosure),
            Ui.Caption("Locked out completely? Close the app and delete the lock file locks.json in the data folder (or the whole folder) to clear every lock:"),
            Ui.Mono(Pages.PageParts.Sanitize(Folder), 12, "OnSurfaceVariant", true),
            Ui.Wrap(4, copy, open, tickets));
    }

    // ---- Choose a policy -------------------------------------------------------------------------------------------------

    /// <summary>
    /// The "Lock this control" wizard: pick one of the policies, set each credential it needs in the order it will be asked, and pick how long an unlock lasts.
    /// Returns true when a policy was stored (the caller then locks the element).
    /// </summary>
    public static Task<bool> ConfigureAsync(string id, string label)
    {
        var done = new TaskCompletionSource<bool>();
        Action? close = null;
        var policy = LockPolicy.None;
        var duration = UnlockDuration.UntilRestart;
        var panel = new ContentControl();
        var pin = new PasswordBox { MaxLength = PinRules.MaxLength }; var pinAgain = new PasswordBox { MaxLength = PinRules.MaxLength };
        var password = new PasswordBox { MaxLength = PasswordRules.MaxLength }; var passwordAgain = new PasswordBox { MaxLength = PasswordRules.MaxLength };
        TotpSetup? totp = null;
        var error = Ui.Text("", 13, null, "Error");
        var policies = LockPolicies.Choosable;
        var policyBox = Ui.Combo(policies.Select(p => Loc.T(LockPolicies.Label(p))), Loc.T(LockPolicies.Label(LockPolicy.None)));
        void Show()
        {
            error.Text = "";
            var factors = LockPolicies.Factors(policy);
            if (factors == LockFactors.None) { panel.Content = Ui.Caption("Unlocking will only ask you to confirm. Pick a PIN, a password or an authenticator app for something harder to click through."); return; }
            var steps = Ui.VStack(12, Ui.Caption("Set up the factors in order."));
            if (factors.HasFlag(LockFactors.Pin))
                steps.Children.Add(Ui.VStack(10, Ui.Field("PIN (4 to 12 digits)", pin), Ui.Field("Repeat the PIN", pinAgain), Ui.Caption("The PIN is stored as a salted PBKDF2 hash, never as typed.")));
            if (factors.HasFlag(LockFactors.Password))
                steps.Children.Add(Ui.VStack(10, Ui.Field("Password (8 to 128 characters)", password), Ui.Field("Repeat the password", passwordAgain), Ui.Caption("The password is stored as a salted PBKDF2 hash, never as typed.")));
            if (factors.HasFlag(LockFactors.Totp)) { totp ??= new TotpSetup(Account(id)); steps.Children.Add(totp.Root); }
            panel.Content = steps;
        }
        policyBox.SelectionChanged += (_, _) => { if (policyBox.SelectedIndex >= 0) { policy = policies[policyBox.SelectedIndex]; Show(); } };
        var durationLabels = UnlockDurations.Choosable.Select(UnlockDurations.Label).ToList();
        var durationBar = Ui.Segmented(durationLabels, UnlockDurations.Label(duration), choice => duration = UnlockDurations.Choosable[durationLabels.IndexOf(choice)]);
        void Save()
        {
            IReadOnlyList<string>? codes;
            var factors = LockPolicies.Factors(policy);
            try
            {
                if (factors.HasFlag(LockFactors.Pin))
                {
                    if (PinRules.Problem(pin.Password) is { } problem) { error.Text = Loc.T(problem); return; }
                    if (pin.Password != pinAgain.Password) { error.Text = Loc.T("The two PINs do not match."); return; }
                }
                if (factors.HasFlag(LockFactors.Password))
                {
                    if (PasswordRules.Problem(password.Password) is { } problem) { error.Text = Loc.T(problem); return; }
                    if (password.Password != passwordAgain.Password) { error.Text = Loc.T("The two passwords do not match."); return; }
                }
                if (factors.HasFlag(LockFactors.Totp))
                {
                    if (!DpapiProtector.IsAvailable) { error.Text = Loc.T("Authenticator locks need Windows secret protection, which is not available here."); return; }
                    if (totp == null || !totp.CodeMatches()) { error.Text = Loc.T("That code did not match. Check the app and try the current code."); return; }
                }
                codes = Host.Locks.Configure(id, policy, factors.HasFlag(LockFactors.Pin) ? pin.Password : null, factors.HasFlag(LockFactors.Totp) ? totp!.Secret : null, factors.HasFlag(LockFactors.Password) ? password.Password : null, duration);
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
            { error.Text = Loc.T("The lock could not be saved: ") + e.Message; return; }
            pin.Clear(); pinAgain.Clear(); password.Clear(); passwordAgain.Clear();
            close!();
            if (codes == null) { done.TrySetResult(true); return; }
            _ = ShowCodesThenFinish(label, codes, done);
        }
        var actions = Ui.HStack(8, Ui.Button("Cancel", null, ButtonKind.Text, () => { done.TrySetResult(false); close!(); }), Ui.Button("Lock", Icons.Lock, ButtonKind.Filled, Save));
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        durationBar.HorizontalAlignment = HorizontalAlignment.Left;
        var body = Ui.VStack(14, Ui.Title(Loc.T("Lock this control") + ": " + Loc.T(label), 20), Ui.Caption("Choose what unlocking should ask for."), Ui.Field("Lock policy", policyBox), panel,
            Ui.Field("How long should unlocking last?", durationBar), error, RecoveryFooter(), actions);
        Show();
        close = Host.Overlay.Show(body, 560, () => done.TrySetResult(false));
        return done.Task;
    }

    private static string Account(string id) => id;

    private static async Task ShowCodesThenFinish(string label, IReadOnlyList<string> codes, TaskCompletionSource<bool> done)
    {
        try { await ShowRecoveryCodesAsync(label, codes); }
        finally { done.TrySetResult(true); }
    }

    // ---- Recovery codes --------------------------------------------------------------------------------------------------

    public static Task ShowRecoveryCodesAsync(string label, IReadOnlyList<string> codes)
    {
        var done = new TaskCompletionSource();
        Action? close = null;
        var text = string.Join(Environment.NewLine, codes);
        var grid = new UniformGrid2(codes);
        var saved = Ui.Check("I have saved these recovery codes", false);
        var finish = Ui.Button("Done", null, ButtonKind.Filled, () => { done.TrySetResult(); close!(); });
        finish.IsEnabled = false;
        saved.Checked += (_, _) => finish.IsEnabled = true; saved.Unchecked += (_, _) => finish.IsEnabled = false;
        var save = Ui.Button("Save to file…", Icons.Save, ButtonKind.Tonal, () =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "recovery-codes.txt", Filter = "Text file|*.txt", Title = Loc.T("Save recovery codes"), OverwritePrompt = true };
            if (dialog.ShowDialog() != true) return;
            try { File.WriteAllText(dialog.FileName, "Recovery codes for " + label + "\r\nEach code works once. Keep this file somewhere only you can open.\r\n\r\n" + text + "\r\n"); Host.Notify.Success("Recovery codes saved."); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Host.Notify.Error("The file could not be saved: " + e.Message); }
        });
        var actions = Ui.HStack(8, Ui.Button("Copy all", Icons.ContentCopy, ButtonKind.Tonal, () => Copy(text)), save, finish);
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        var body = Ui.VStack(14, Ui.Title(Loc.T("Recovery codes") + ": " + Loc.T(label), 20),
            Ui.Caption("Each code clears this lock once if you lose the PIN or authenticator. They are shown only now and stored only as hashes, so nobody can read them back."),
            grid, saved, actions);
        close = Host.Overlay.Show(body, 520, () => done.TrySetResult());
        return done.Task;
    }

    private sealed class UniformGrid2 : Grid
    {
        public UniformGrid2(IReadOnlyList<string> codes)
        {
            ColumnDefinitions.Add(new ColumnDefinition()); ColumnDefinitions.Add(new ColumnDefinition());
            for (var i = 0; i < codes.Count; i++)
            {
                if (i % 2 == 0) RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var cell = Ui.Mono(codes[i], 16, "OnSurface", true).At(i / 2, i % 2).Margin(0, 3, 12, 3);
                Children.Add(cell);
            }
        }
    }

    // ---- The unlock ladder -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Confirm, then the credential steps in their fixed order (PIN, password, authenticator code), then the honest wait with a live countdown, with the recovery code reachable from the credential and wait rungs.
    /// Returns true when the person got through. Nothing here changes the lock itself; the caller does that on true.
    /// </summary>
    public static Task<bool> LadderAsync(string id, string label)
    {
        var store = Host.Locks;
        var done = new TaskCompletionSource<bool>();
        Action? close = null;
        var recovery = false;
        var content = new ContentControl();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        var attempt = store.Begin(id);
        string? message = null;
        Action? tick = null;
        timer.Tick += (_, _) => tick?.Invoke();
        void Finish(bool ok) { timer.Stop(); done.TrySetResult(ok); close?.Invoke(); }
        void Render()
        {
            timer.Stop(); tick = null;
            var stage = recovery ? LadderStage.Recovery : store.Stage(id);
            var error = Ui.Text(message ?? "", 13, null, "Error");
            var cancel = Ui.Button("Cancel", null, ButtonKind.Text, () => Finish(false));
            var rows = Ui.VStack(12);
            var factor = attempt?.Next ?? LockFactors.None;
            rows.Children.Add(Ui.Caption(stage switch
            {
                LadderStage.Confirm => "Confirm that you want to unlock this control.",
                LadderStage.Credential => factor == LockFactors.Pin ? "Enter your PIN." : factor == LockFactors.Password ? "Enter your password." : "Enter the 6-digit code from your authenticator app.",
                LadderStage.Wait => "Too many wrong answers. Wait it out, or use a recovery code.",
                _ => "Enter one of your recovery codes. Each works once."
            }));
            if (stage == LadderStage.Credential && attempt != null && attempt.Steps.Count > 1)
                rows.Children.Add(Ui.Text(Loc.T($"Step {attempt.Index + 1} of {attempt.Steps.Count}") + ": " + Loc.T(LockPolicies.FactorLabel(factor)), 14, FontWeights.SemiBold, "Primary", true, false));
            switch (stage)
            {
                case LadderStage.Confirm:
                {
                    rows.Children.Add(Ui.HStack(8, cancel, Ui.Button("Unlock", Icons.LockOpen, ButtonKind.Filled, () => Finish(true))));
                    break;
                }
                case LadderStage.Credential:
                case LadderStage.Recovery:
                {
                    FrameworkElement entry; Func<string> read; Action clear;
                    if (stage == LadderStage.Credential && factor is LockFactors.Pin or LockFactors.Password) { var box = new PasswordBox { MaxLength = factor == LockFactors.Pin ? PinRules.MaxLength : PasswordRules.MaxLength }; entry = box; read = () => box.Password; clear = box.Clear; }
                    else { var box = Ui.Input("", stage == LadderStage.Recovery ? "Recovery code" : "Code from your authenticator", true); box.MaxLength = stage == LadderStage.Recovery ? 16 : 7; entry = box; read = () => box.Text; clear = () => box.Clear(); }
                    void Submit()
                    {
                        var result = stage == LadderStage.Recovery ? store.UseRecoveryCode(id, read()) : attempt!.Submit(read());
                        clear();
                        switch (result.Outcome)
                        {
                            case UnlockOutcome.Unlocked: Finish(true); return;
                            case UnlockOutcome.NextFactor: message = null; break;
                            case UnlockOutcome.Unavailable: message = Loc.T("The stored authenticator secret could not be read on this account. Use a recovery code."); break;
                            case UnlockOutcome.Waiting: message = ""; break;
                            default: message = stage == LadderStage.Recovery ? Loc.T("That recovery code did not match, or it was already used.") : result.FreeAttemptsLeft > 0 ? Loc.T("That did not match.") + " " + Loc.T(result.FreeAttemptsLeft == 1 ? "1 more try before a wait starts." : result.FreeAttemptsLeft + " more tries before a wait starts.") : Loc.T("That did not match."); break;
                        }
                        Render();
                    }
                    entry.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Submit(); };
                    rows.Children.Add(Ui.Field(stage == LadderStage.Recovery ? "Recovery code" : LockPolicies.FactorLabel(factor), entry));
                    rows.Children.Add(error);
                    var toggle = stage == LadderStage.Recovery
                        ? Ui.Button("Back", null, ButtonKind.Text, () => { recovery = false; message = null; Render(); })
                        : Ui.Button("Use a recovery code", Icons.ShieldLock, ButtonKind.Text, () => { recovery = true; message = null; Render(); });
                    rows.Children.Add(Ui.Caption(stage == LadderStage.Recovery ? Loc.T("Recovery codes left:") + " " + store.RecoveryLeft(id) : ""));
                    rows.Children.Add(Ui.HStack(8, cancel, toggle, Ui.Button("Unlock", Icons.LockOpen, ButtonKind.Filled, Submit)));
                    entry.Loaded += (_, _) => entry.Focus();
                    break;
                }
                default:
                {
                    var until = store.WaitUntil(id);
                    var countdown = Ui.Text("", 28, FontWeights.SemiBold, "OnSurface", true, false);
                    var detail = Ui.Caption("");
                    void Tick()
                    {
                        var left = store.WaitUntil(id);
                        if (left == null) { timer.Stop(); message = null; Render(); return; }
                        var span = left.Value - DateTimeOffset.UtcNow; if (span < TimeSpan.Zero) span = TimeSpan.Zero;
                        countdown.Text = $"{(int)span.TotalMinutes:00}:{span.Seconds:00}";
                        detail.Text = Loc.T("You can try again in") + " " + countdown.Text + " " + Loc.T("at") + " " + left.Value.ToLocalTime().ToString("HH:mm:ss") + ". " + Loc.T("Each wrong answer after that doubles the wait, up to one hour.");
                    }
                    tick = Tick;
                    Tick();
                    rows.Children.Add(countdown);
                    rows.Children.Add(detail);
                    rows.Children.Add(Ui.Caption("Waiting does not unlock anything: when the time is up you still need your PIN or code. A recovery code works at any time."));
                    var offer = LadderGameDialogs.Offer(id);
                    rows.Children.Add(Ui.Caption(LadderGameDialogs.Describe(offer)));
                    var play = Ui.AsyncButton("Play to shorten the wait", Icons.Extension, ButtonKind.Tonal, async () => { await LadderGameDialogs.PlayAsync(id); Render(); });
                    play.IsEnabled = offer.Available;
                    rows.Children.Add(Ui.HStack(8, cancel, play, Ui.Button("Use a recovery code", Icons.ShieldLock, ButtonKind.Tonal, () => { recovery = true; message = null; Render(); })));
                    if (until != null) timer.Start();
                    break;
                }
            }
            if (stage != LadderStage.Confirm) rows.Children.Add(Ui.Button("Forgotten your password?", Icons.Help, ButtonKind.Text, () => SupportDeskUi.Show()));
            content.Content = rows;
        }
        var body = Ui.VStack(14, Ui.Title(Loc.T("Unlock this control") + ": " + Loc.T(label), 20), content, RecoveryFooter());
        Render();
        close = Host.Overlay.Show(body, 560, () => { timer.Stop(); done.TrySetResult(false); });
        return done.Task;
    }
}
