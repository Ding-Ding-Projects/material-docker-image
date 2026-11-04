using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MaterialDockerImage.Pages;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

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

    private static Task<string?> PromptSecretAsync(string title, string? message)
    {
        var done = new TaskCompletionSource<string?>();
        Action? close = null;
        var box = new PasswordBox { MaxLength = SchoolModeRules.MaxPasswordLength };
        var error = Ui.Text(message ?? "", 13, null, "Error");
        void Submit() { var value = box.Password; box.Clear(); done.TrySetResult(value); close!(); }
        box.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Submit(); };
        var actions = Ui.HStack(8, Ui.Button("Cancel", null, ButtonKind.Text, () => { done.TrySetResult(null); close!(); }), Ui.Button("Turn off", Icons.LockOpen, ButtonKind.Filled, Submit));
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        var body = Ui.VStack(14, Ui.Title(Loc.T("Turn off") + ": " + title, 20), Ui.Caption("Enter the PIN or password you set for this mode."), Ui.Field("PIN or password", box), error, Ui.Caption(SchoolModeRules.Disclosure), actions);
        close = Host.Overlay.Show(body, 480, () => done.TrySetResult(null));
        box.Loaded += (_, _) => box.Focus();
        return done.Task;
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
