using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MaterialDockerImage.Pages;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;
using System.IO;

namespace MaterialDockerImage.Shell;

/// <summary>
/// School mode's views: the PIN or password prompt that turns the mode off, the dialogs that set it and rename the mode, and the live follow of the
/// shared record so that a change made by another app shows here without a restart. The rules and storage are in <see cref="SchoolModeStore"/>.
/// </summary>
public static class SchoolModeUi
{
    private static AppHost Host => AppHost.Current;
    private static SchoolModeStore Store => GameServices.School;
    private static DispatcherTimer? watcher;

    /// <summary>The name every surface uses. Once the user renames the mode the shipped name no longer appears anywhere.</summary>
    public static string Name => Store.Name;
    public static bool Renamed => Name != SchoolModeRules.DefaultName;

    /// <summary>Puts the chosen name into one of this app's English phrases before it is translated. An unrenamed mode keeps the shipped, translated wording.</summary>
    public static string Phrase(string english) => Renamed ? english.Replace("School mode", Name, StringComparison.Ordinal) : english;

    /// <summary>Adopts the shared record at startup and then follows it, so the mode switches live when another app changes it.</summary>
    public static void Attach(AppHost host)
    {
        if (host.IsCapture) return;
        var store = Store;
        store.AdoptLegacy(host.Settings.SchoolMode);
        if (store.Exists && store.Enabled != host.Settings.SchoolMode) Apply(host, store.Enabled, false);
        watcher = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        watcher.Tick += (_, _) =>
        {
            try { if (store.ReloadIfChanged() | store.Enabled != host.Settings.SchoolMode) Apply(host, store.Enabled, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        };
        watcher.Start();
    }

    private static void Apply(AppHost host, bool on, bool announce)
    {
        host.Settings.SchoolMode = on;
        SettingRows.SyncRuntime();
        try { host.Settings.Save(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        host.NotifySettingsChanged();
        if (announce) host.Notify.Info(Name + ": " + Loc.T(on ? "on" : "off"));
    }

    /// <summary>Asks for what the change needs. Turning the mode on needs nothing; turning it off needs the PIN or password when one is set. Returns whether the change may go ahead.</summary>
    public static async Task<bool> RequestAsync(bool turnOn)
    {
        var store = Store;
        if (turnOn) { store.Enable(); return true; }
        if (!store.Enabled) return true;
        var plan = SchoolPasskeyRules.Plan(store.Method, store.Method == SchoolOffMethod.Passkey ? await WindowsHello.CheckAsync() : HelloAvailability.Available, store.HasSecret);
        if (plan.Notice != null) Host.Notify.Warning(Loc.T(plan.Notice) + " " + Loc.T(plan.Fallback ?? ""));
        if (plan.Route == SchoolOffRoute.Passkey)
        {
            var verified = await WindowsHello.VerifyAsync(Loc.T("Turn off") + ": " + Name);
            if (store.TurnOffAfterPasskey(verified) == SchoolOffOutcome.TurnedOff) return true;
            Host.Notify.Warning(Loc.T("Windows Hello did not verify you, so the mode stays on."));
            return false;
        }
        string? message = null;
        for (var tries = 0; tries < 30; tries++)
        {
            string? secret = null;
            if (store.HasSecret)
            {
                if (store.WaitUntil is { } wait) { Host.Notify.Warning(Loc.T("Too many wrong answers. Try again at") + " " + wait.ToLocalTime().ToString("HH:mm:ss") + "."); return false; }
                secret = await PromptSecretAsync(Name, message);
                if (secret == null) return false;
            }
            switch (store.TryTurnOff(secret))
            {
                case SchoolOffOutcome.TurnedOff or SchoolOffOutcome.AlreadyOff: return true;
                case SchoolOffOutcome.Waiting: Host.Notify.Warning("Too many wrong answers. Wait, then try again."); return false;
                default: message = "That did not match."; break;
            }
        }
        return false;
    }

    private static Task<string?> PromptSecretAsync(string title, string? message, string action = "Turn off")
    {
        var done = new TaskCompletionSource<string?>();
        Action? close = null;
        var box = new PasswordBox { MaxLength = SchoolModeRules.MaxPasswordLength };
        var error = Ui.Text(message ?? "", 13, null, "Error");
        void Submit() { var value = box.Password; box.Clear(); done.TrySetResult(value); close!(); }
        box.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Submit(); };
        var actions = Ui.HStack(8, Ui.Button("Cancel", null, ButtonKind.Text, () => { done.TrySetResult(null); close!(); }), Ui.Button(action, Icons.LockOpen, ButtonKind.Filled, Submit));
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        var body = Ui.VStack(14, Ui.Title(Loc.T(action) + ": " + title, 20), Ui.Caption("Enter the PIN or password you set for this mode."), Ui.Field("PIN or password", box), error, Ui.Caption(SchoolModeRules.Disclosure), actions);
        close = Host.Overlay.Show(body, 480, () => done.TrySetResult(null));
        box.Loaded += (_, _) => box.Focus();
        return done.Task;
    }

    /// <summary>Chooses how the mode is turned off. Choosing the passkey asks Windows Hello to verify first and is refused, with the reason, when it is not available.</summary>
    public static async Task<bool> ChooseMethodAsync(SchoolOffMethod method)
    {
        var store = Store;
        if (method == store.Method) return true;
        var hello = await WindowsHello.CheckAsync();
        if (method == SchoolOffMethod.Passkey && SchoolPasskeyRules.ChoiceProblem(hello) is { } problem) { Host.Notify.Warning(Loc.T(problem)); return false; }
        // While the mode is on, changing how it is turned off needs the same proof as turning it off, so the change is not a way round the lock.
        if (store.Enabled && store.Method == SchoolOffMethod.Secret && store.HasSecret)
        {
            var typed = await PromptSecretAsync(Name, null, "Change how to turn off");
            if (typed == null || !store.SecretMatches(typed)) { Host.Notify.Warning(Loc.T("That did not match.")); return false; }
        }
        if (store.Enabled && store.Method == SchoolOffMethod.Passkey && hello == HelloAvailability.Available
            && !await WindowsHello.VerifyAsync(Loc.T("Change how to turn off") + ": " + Name)) { Host.Notify.Warning(Loc.T("Windows Hello did not verify you, so the choice was not changed.")); return false; }
        if (method == SchoolOffMethod.Passkey && !await WindowsHello.VerifyAsync(Loc.T("Use Windows Hello to turn off") + ": " + Name)) { Host.Notify.Warning(Loc.T("Windows Hello did not verify you, so the choice was not changed.")); return false; }
        try { store.SetMethod(method); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Host.Notify.Error(Loc.T("It could not be saved: ") + e.Message); return false; }
        Host.Notify.Success(method == SchoolOffMethod.Passkey ? "Windows Hello will now turn this mode off." : "The PIN or password will now turn this mode off.");
        return true;
    }

    /// <summary>Sets or replaces the secret. Replacing it asks for the current one first.</summary>
    public static Task SetSecretAsync()
    {
        var done = new TaskCompletionSource();
        Action? close = null;
        var store = Store;
        var current = new PasswordBox { MaxLength = SchoolModeRules.MaxPasswordLength };
        var first = new PasswordBox { MaxLength = SchoolModeRules.MaxPasswordLength };
        var second = new PasswordBox { MaxLength = SchoolModeRules.MaxPasswordLength };
        var error = Ui.Text("", 13, null, "Error");
        void Save()
        {
            if (SchoolModeRules.SecretProblem(first.Password) is { } problem) { error.Text = Loc.T(problem); return; }
            if (first.Password != second.Password) { error.Text = Loc.T("The two entries do not match."); return; }
            try
            {
                if (!store.SetSecret(first.Password, store.HasSecret ? current.Password : null)) { error.Text = Loc.T("The current PIN or password did not match."); return; }
            }
            catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException) { error.Text = Loc.T("It could not be saved: ") + e.Message; return; }
            current.Clear(); first.Clear(); second.Clear();
            Host.Notify.Success("The PIN or password was saved. It is stored only as a salted hash.");
            done.TrySetResult(); close!();
        }
        var actions = Ui.HStack(8, Ui.Button("Cancel", null, ButtonKind.Text, () => { done.TrySetResult(); close!(); }), Ui.Button("Save", Icons.Lock, ButtonKind.Filled, Save));
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        var body = Ui.VStack(14, Ui.Title(Loc.T("PIN or password to turn off") + ": " + store.Name, 20),
            Ui.Caption("A PIN is 4 to 12 digits. A password is 6 to 64 characters. It is asked for only when turning the mode off, in every app that shares this record."),
            store.HasSecret ? Ui.Field("Current PIN or password", current) : null, Ui.Field("New PIN or password", first), Ui.Field("Repeat it", second), error, Ui.Caption(SchoolModeRules.Disclosure), actions);
        close = Host.Overlay.Show(body, 520, () => done.TrySetResult());
        return done.Task;
    }

    public static async Task RenameAsync()
    {
        var answer = await Host.Overlay.PromptAsync("Rename this mode", "Name", Store.Name, "Every screen of every app uses this name instead of the shipped one.", v => SchoolModeRules.NameProblem(v));
        if (answer == null) return;
        try { Store.Rename(answer); Host.Notify.Success("Renamed."); Host.NotifySettingsChanged(); }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException) { Host.Notify.Error(Loc.T("It could not be saved: ") + e.Message); }
    }

    /// <summary>A switch for live controls such as the command palette. It asks for the secret when switching off and puts itself back when refused.</summary>
    public static CheckBox PaletteSwitch()
    {
        CheckBox? box = null; var reverting = false;
        box = Ui.Switch(Name, Host.Settings.SchoolMode, v =>
        {
            if (reverting) return;
            _ = Change(v);
        });
        return box;

        async Task Change(bool v)
        {
            if (!await RequestAsync(v)) { reverting = true; box!.IsChecked = !v; reverting = false; return; }
            Host.Settings.SchoolMode = v; SettingRows.SyncRuntime(); Host.SaveSettings(Name + (v ? " on" : " off"));
        }
    }
}
