using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage.Pages;

/// <summary>
/// The general authenticator: accounts for anything the user registers, with live codes, a countdown, the next code, copy, rename,
/// reorder and delete. Secrets are kept in one file protected for the current Windows account, are never written to settings, history,
/// logs or exports in readable form, and backups are encrypted with a passphrase. Capture mode shows labelled sample accounts only.
/// </summary>
internal sealed class AuthenticatorAccountsPanel
{
    private static AppHost Host => AppHost.Current;
    public FrameworkElement Root { get; }

    private sealed record Row(OtpAccount Account, TextBlock Code, TextBlock Seconds, ProgressBar Bar, TextBlock Next);
    private readonly AuthenticatorVault? vault;
    private readonly bool sample;
    private readonly StackPanel list = new();
    private readonly SearchFilter search = new("Search accounts", 360) { Width = double.NaN };
    private readonly List<Row> rows = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TextBlock problem = Ui.Text("", 13, null, "Error");
    private readonly TextBlock addError = Ui.Text("", 13, null, "Error");
    private readonly TextBox link = Ui.Input("", "otpauth:// link", true);
    private readonly TextBox issuer = Ui.Input("", "Issuer");
    private readonly TextBox account = Ui.Input("", "Account name");
    private readonly TextBox secret = Ui.Input("", "Base32 secret", true);
    private readonly ComboBox algorithm = Ui.Combo(OtpAccountRules.Algorithms, "SHA1");
    private readonly ComboBox digits = Ui.Combo(["6", "7", "8"], "6");
    private readonly TextBox period = Ui.Input("30", "Period in seconds");
    private readonly ContentControl addForm = new() { Focusable = false };

    public AuthenticatorAccountsPanel()
    {
        sample = Host.IsCapture;
        if (!sample)
        {
            vault = new AuthenticatorVault(new DpapiProtector(), Path.Combine(UserSettings.DataDirectory, "authenticator.bin"));
            if (DpapiProtector.IsAvailable) vault.Load(); else problem.Text = Loc.T("The authenticator needs Windows secret protection, which is not available here.");
            if (vault.Problem != null) problem.Text = Loc.T(vault.Problem) + " " + Loc.T("Nothing is changed while the file cannot be read.");
        }
        search.Changed += Render;
        link.AcceptsReturn = false;
        System.Windows.Controls.SpellCheck.SetIsEnabled(link, false);
        var mode = Ui.Segmented(["Paste a link", "Enter manually"], "Paste a link", m => { addForm.Content = m == "Paste a link" ? PasteForm() : ManualForm(); addError.Text = ""; });
        addForm.Content = PasteForm();
        var add = Ui.Card(Ui.VStack(10, Ui.Title("Add an account", 16), mode, addForm, addError), 16, "SurfaceLow", 16);
        var export = Ui.AsyncButton("Export encrypted backup…", Icons.Download, ButtonKind.Tonal, ExportAsync);
        var import = Ui.AsyncButton("Import encrypted backup…", Icons.Upload, ButtonKind.Tonal, ImportAsync);
        var header = Ui.VStack(6,
            Ui.Title("Authenticator", 18),
            Ui.Caption("Keep the time-based one-time codes for your own accounts here. Everything stays on this computer: secrets are protected for your Windows account and are never sent anywhere."),
            sample ? Ui.Badge("Sample accounts, not your real ones", Tone.Warning) : null,
            problem);
        Root = Ui.Scroll(Ui.VStack(18, header, add, Ui.VStack(10, search, list), Ui.Card(Ui.VStack(8, Ui.Title("Backup", 16),
            Ui.Caption("A backup is encrypted with a passphrase you choose. Without that passphrase it cannot be opened, so keep it safe. Importing skips accounts you already have."),
            Ui.Wrap(8, export, import)), 16, "SurfaceLow", 16)));
        if (sample) { add.IsEnabled = false; export.IsEnabled = false; import.IsEnabled = false; }
        Render();
        timer.Tick += (_, _) => Tick();
        Root.Loaded += (_, _) => { timer.Start(); Tick(); };
        Root.Unloaded += (_, _) => timer.Stop();
    }

    private IReadOnlyList<OtpAccount> Accounts => sample ? AuthenticatorSample.Accounts : vault!.Accounts;

    // ----- Adding -----

    private FrameworkElement PasteForm()
    {
        var button = Ui.Button("Add account", Icons.Add, ButtonKind.Filled, AddFromLink);
        var fromImage = Ui.Button("From an image file…", Icons.FolderOpen, ButtonKind.Tonal, AddFromImageFile);
        var fromClipboard = Ui.Button("From the clipboard", Icons.ContentCopy, ButtonKind.Tonal, AddFromClipboard);
        var fromCamera = Ui.Button("From the camera…", Icons.Visibility, ButtonKind.Tonal, AddFromCamera);
        return Ui.VStack(8, Ui.Field("otpauth:// link", link, "Paste the link a service shows next to its QR code. Its issuer, algorithm, digits and period are used as given.", "otpauth://totp/Example:me@example.com?secret=…"), button,
            Ui.Caption("Or read the QR code from a picture or the camera. It is read on this computer and nothing is kept."), Ui.Wrap(8, fromImage, fromClipboard, fromCamera));
    }

    private FrameworkElement ManualForm()
    {
        var button = Ui.Button("Add account", Icons.Add, ButtonKind.Filled, AddManual);
        return Ui.VStack(8,
            Ui.Columns(10, [1, 1], Ui.Field("Issuer", issuer, "The service the codes are for.", "Example Mail"), Ui.Field("Account name", account, "Your account or e-mail at that service.", "me@example.com")),
            Ui.Field("Base32 secret", secret, "The setup key the service shows. Spaces and lower case are fine.", "JBSW Y3DP EHPK 3PXP"),
            Ui.Columns(10, [1, 1, 1], Ui.Field("Algorithm", algorithm, "Most services use SHA1."), Ui.Field("Digits", digits, "Most services use 6."), Ui.Field("Period (seconds)", period, "Most services use 30.")),
            button);
    }

    private void AddFromLink()
    {
        if (!OtpauthUri.TryParse(link.Text, out var parsed, out var error)) { addError.Text = Loc.T(error); return; }
        Commit(parsed!, () => link.Clear());
    }

    // ----- From a QR picture -----

    private void AddFromImageFile()
    {
        var dialog = new OpenFileDialog { Title = Loc.T("Choose a picture of the QR code"), Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*" };
        if (dialog.ShowDialog() != true) return;
        ImportPictureFile(dialog.FileName);
    }

    private void ImportPictureFile(string path)
    {
        try
        {
            if (OtpQrImport.FileProblem(path, new FileInfo(path).Length) is { } problemText) { addError.Text = Loc.T(problemText); return; }
            var bitmap = new BitmapImage();
            bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(path); bitmap.EndInit(); bitmap.Freeze();
            ImportPicture(bitmap);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or FileFormatException or UriFormatException)
        {
            addError.Text = Loc.T("That picture could not be opened.");
        }
    }

    private void AddFromCamera()
    {
        addError.Text = "";
        AuthenticatorCameraDialog.Open(parsed => Commit(parsed, () => { }));
    }

    private void AddFromClipboard()
    {
        try
        {
            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } image) { ImportPicture(image); return; }
            if (Clipboard.ContainsFileDropList() && Clipboard.GetFileDropList().Cast<string>().FirstOrDefault() is { } file) { ImportPictureFile(file); return; }
            if (Clipboard.ContainsText() && Clipboard.GetText().Trim().StartsWith("otpauth:", StringComparison.OrdinalIgnoreCase)) { link.Text = Clipboard.GetText().Trim(); AddFromLink(); return; }
            addError.Text = Loc.T("The clipboard has no picture of a QR code.");
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            addError.Text = Loc.T("The clipboard could not be read.");
        }
    }

    private void ImportPicture(BitmapSource source)
    {
        var width = source.PixelWidth; var height = source.PixelHeight;
        if ((long)width * height > OtpQrImport.MaxPixels) { addError.Text = Loc.T("That picture is too large to read."); return; }
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[width * height * 4];
        converted.CopyPixels(pixels, width * 4, 0);
        if (!OtpQrImport.TryFromPixels(pixels, width, height, out var parsed, out var error)) { addError.Text = Loc.T(error); return; }
        Commit(parsed!, () => { });
    }

    private void AddManual()
    {
        if (!int.TryParse(period.Text.Trim(), out var seconds)) { addError.Text = Loc.T("The period must be a whole number of seconds."); return; }
        if (!OtpAccountRules.TryCreate(issuer.Text, account.Text, secret.Text, (algorithm.SelectedItem as string) ?? "SHA1", int.Parse((digits.SelectedItem as string) ?? "6"), seconds, out var created, out var error)) { addError.Text = Loc.T(error); return; }
        Commit(created!, () => { issuer.Clear(); account.Clear(); secret.Clear(); });
    }

    private void Commit(OtpAccount created, Action clearInputs)
    {
        var failure = vault!.Add(created);
        if (failure != null) { addError.Text = Loc.T(failure); return; }
        addError.Text = ""; clearInputs();
        Host.Notify.Success("Account added: " + created.Label);
        Render();
    }

    // ----- Rendering -----

    private void Render()
    {
        list.Children.Clear(); rows.Clear();
        var all = Accounts;
        if (all.Count == 0) { list.Children.Add(Ui.EmptyState(Icons.ShieldLock, "No accounts yet", "Add an account above with its otpauth:// link or its setup key, and its code appears here.")); return; }
        var shown = all.Where(a => search.Predicate.Matches(a.Label)).ToList();
        if (shown.Count == 0) { list.Children.Add(Ui.Caption("No account matches this search.")); return; }
        foreach (var a in shown) list.Children.Add(BuildRow(a));
        Tick();
    }

    private FrameworkElement BuildRow(OtpAccount a)
    {
        var code = Ui.Mono("", 28, "Primary");
        AutomationProperties.SetName(code, Loc.T("Current code for") + " " + a.Label);
        var seconds = Ui.Text("", 13, FontWeights.SemiBold, "OnSurface", false, false);
        var bar = new ProgressBar { Minimum = 0, Maximum = a.Period, Height = 4, MinWidth = 120 };
        AutomationProperties.SetName(bar, Loc.T("Time left for") + " " + a.Label);
        var next = Ui.Mono("", 12, "OnSurfaceVariant");
        rows.Add(new Row(a, code, seconds, bar, next));
        var copy = Ui.IconButton(Icons.ContentCopy, Loc.T("Copy code for") + " " + a.Label, () => CopyCode(a));
        var buttons = Ui.HStack(0, copy);
        if (!sample)
        {
            buttons.Children.Add(Ui.IconButton(Icons.Edit, Loc.T("Rename") + " " + a.Label, () => _ = RenameAsync(a)));
            buttons.Children.Add(Ui.IconButton(Icons.ArrowUpward, Loc.T("Move up") + ": " + a.Label, () => { if (vault!.Move(a.Id, -1)) Render(); }));
            buttons.Children.Add(Ui.IconButton(Icons.ArrowDownward, Loc.T("Move down") + ": " + a.Label, () => { if (vault!.Move(a.Id, 1)) Render(); }));
            buttons.Children.Add(Ui.IconButton(Icons.Delete, Loc.T("Delete") + " " + a.Label, () => _ = DeleteAsync(a)));
        }
        var meta = Ui.Caption($"{a.Algorithm} · {a.Digits} " + Loc.T("digits") + $" · {a.Period} s");
        var left = Ui.VStack(4, Ui.Text(a.Label, 14, FontWeights.SemiBold, "OnSurface", false, false), code, Ui.HStack(12, seconds, bar, Ui.Caption("Next:"), next), meta);
        return Ui.Card(Ui.Columns(8, [1, 0], left, buttons), 14, "SurfaceLow", 16).Margin(0, 0, 0, 2);
    }

    private void Tick()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var row in rows)
        {
            try
            {
                var reading = OtpClock.Read(row.Account, now);
                row.Code.Text = reading.Grouped; row.Next.Text = reading.NextGrouped;
                row.Seconds.Text = reading.SecondsLeft + " s";
                row.Bar.Value = reading.SecondsLeft;
                row.Seconds.SetResourceReference(TextBlock.ForegroundProperty, reading.Expiring ? "Warning" : "OnSurface");
            }
            catch (InvalidOperationException) { row.Code.Text = Loc.T("Unreadable secret"); }
        }
    }

    private void CopyCode(OtpAccount a)
    {
        try { ActionSounds.Copy(OtpClock.Read(a, DateTimeOffset.UtcNow).Code); }
        catch (InvalidOperationException) { return; }
        Host.Notify.Info("Code copied. It stays on the clipboard until you copy something else.");
    }

    // ----- Rename and delete -----

    private Task RenameAsync(OtpAccount a)
    {
        var done = new TaskCompletionSource();
        Action? close = null;
        var issuerBox = Ui.Input(a.Issuer, "Issuer"); var nameBox = Ui.Input(a.Name, "Account name");
        var error = Ui.Text("", 13, null, "Error");
        void Apply()
        {
            var failure = vault!.Rename(a.Id, issuerBox.Text, nameBox.Text);
            if (failure != null) { error.Text = Loc.T(failure); return; }
            close!(); done.TrySetResult(); Render();
        }
        var actions = Ui.HStack(8, Ui.Button("Cancel", null, ButtonKind.Text, () => { close!(); done.TrySetResult(); }), Ui.Button("Save", null, ButtonKind.Filled, Apply));
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        close = Host.Overlay.Show(Ui.VStack(14, Ui.Title("Rename account", 20), Ui.Field("Issuer", issuerBox), Ui.Field("Account name", nameBox), error, actions), 480, () => done.TrySetResult());
        return done.Task;
    }

    private async Task DeleteAsync(OtpAccount a)
    {
        if (!await Host.Overlay.ConfirmAsync("Delete this account?", a.Label + "\n\n" + Loc.T("Its secret is removed from this computer. If you have not kept its setup key or another authenticator, you may lose access to the account it protects."), "Delete", true)) return;
        if (vault!.Delete(a.Id)) { Host.Notify.Info("Account deleted."); Render(); }
    }

    // ----- Backup -----

    private Task<string?> AskPassphraseAsync(string title, string help, bool confirm)
    {
        var done = new TaskCompletionSource<string?>();
        Action? close = null;
        var first = new PasswordBox { MaxLength = 256 }; var second = new PasswordBox { MaxLength = 256 };
        AutomationProperties.SetName(first, Loc.T("Passphrase")); AutomationProperties.SetName(second, Loc.T("Repeat the passphrase"));
        var error = Ui.Text("", 13, null, "Error");
        void Apply()
        {
            if (confirm)
            {
                if (AuthenticatorBackup.PassphraseProblem(first.Password) is { } weak) { error.Text = Loc.T(weak); return; }
                if (first.Password != second.Password) { error.Text = Loc.T("The two passphrases are different."); return; }
            }
            else if (first.Password.Length == 0) { error.Text = Loc.T("Enter the passphrase."); return; }
            var value = first.Password;
            close!(); done.TrySetResult(value);
        }
        first.KeyDown += (_, e) => { if (e.Key == Key.Enter && !confirm) Apply(); };
        var actions = Ui.HStack(8, Ui.Button("Cancel", null, ButtonKind.Text, () => { close!(); done.TrySetResult(null); }), Ui.Button("Continue", null, ButtonKind.Filled, Apply));
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        close = Host.Overlay.Show(Ui.VStack(14, Ui.Title(title, 20), Ui.Caption(help), Ui.Field("Passphrase", first), confirm ? Ui.Field("Repeat the passphrase", second) : null, error, actions), 520, () => done.TrySetResult(null));
        first.Loaded += (_, _) => first.Focus();
        return done.Task;
    }

    private async Task ExportAsync()
    {
        if (Accounts.Count == 0) { Host.Notify.Info("There are no accounts to back up."); return; }
        var passphrase = await AskPassphraseAsync("Encrypt the backup", "Choose a passphrase of at least 8 characters. It is not stored anywhere, and a backup cannot be opened without it.", true);
        if (passphrase == null) return;
        var dialog = new SaveFileDialog { FileName = $"authenticator-backup-{DateTime.Now:yyyyMMdd}.json", Filter = "Authenticator backup|*.json", Title = Loc.T("Save the encrypted backup"), OverwritePrompt = true };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var text = await Task.Run(() => AuthenticatorBackup.Export(Accounts, passphrase));
            await File.WriteAllTextAsync(dialog.FileName, text);
            Host.Notify.Success("Encrypted backup saved.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Host.Notify.Error("The backup could not be saved: " + e.Message); }
    }

    private async Task ImportAsync()
    {
        var dialog = new OpenFileDialog { Title = Loc.T("Open an encrypted backup"), Filter = "Authenticator backup|*.json|All files|*.*" };
        if (dialog.ShowDialog() != true) return;
        string text;
        try
        {
            if (new FileInfo(dialog.FileName).Length > AuthenticatorBackup.MaxFileBytes) { Host.Notify.Error("That file is too large to be an authenticator backup."); return; }
            text = await File.ReadAllTextAsync(dialog.FileName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Host.Notify.Error("The file could not be read: " + e.Message); return; }
        var passphrase = await AskPassphraseAsync("Open the backup", "Enter the passphrase this backup was encrypted with.", false);
        if (passphrase == null) return;
        try
        {
            var accounts = await Task.Run(() => AuthenticatorBackup.Import(text, passphrase));
            var (added, skipped, error) = vault!.AddMany(accounts);
            if (error != null) { Host.Notify.Error(error); return; }
            Host.Notify.Success($"Imported {added} account(s). {skipped} already present or skipped.");
            Render();
        }
        catch (InvalidDataException e) { Host.Notify.Error(e.Message); }
    }
}
