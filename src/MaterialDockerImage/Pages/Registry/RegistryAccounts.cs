using System.Windows.Automation;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Registry sign-in and the list of registries named in the Docker CLI configuration file. Only registry names are
/// read; passwords travel to the docker process on standard input and are never logged, stored or shown.
/// </summary>
internal sealed class RegistryAccounts
{
    private static AppHost Host => AppHost.Current;
    private readonly Func<bool> isCurrent;
    private readonly Func<string?> snapshot;
    private readonly TextBox registry = Ui.Input("docker.io", "Registry", true);
    private readonly TextBox user = Ui.Input("", "User name");
    private readonly PasswordBox password = new();
    private readonly TextBlock formError = Ui.Text("", 13, null, "Error");
    private readonly StackPanel signedIn = new();
    private readonly TextBlock fileNote = Ui.Caption("");
    private readonly SearchFilter search = new("Search signed-in registries", 260);
    private RegistryAuthFile? file;
    public Border SignedInCard { get; }
    public Border SignInCard { get; }

    public RegistryAccounts(Func<bool> isCurrent, Func<string?> snapshot)
    {
        this.isCurrent = isCurrent; this.snapshot = snapshot;
        System.Windows.Automation.AutomationProperties.SetName(password, Loc.T("Password or access token"));
        password.KeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) await SignInAsync(); };
        search.Changed += Render;
        SignedInCard = PageParts.Section("Signed in", "Registry names from the Docker CLI configuration file. Credentials are never read.",
            Ui.VStack(10, search, signedIn, fileNote),
            Ui.AsyncIconButton(Icons.Refresh, "Reload signed-in registries", () => { Reload(); return Task.CompletedTask; }));
        var signIn = Ui.AsyncButton("Sign in", Icons.Login, ButtonKind.Filled, SignInAsync);
        var form = Ui.VStack(12,
            Ui.Field("Registry", registry, "The registry host to sign in to. Use docker.io for Docker Hub, or a host such as ghcr.io or registry.example.com:5000.", "ghcr.io"),
            Ui.Field("User name", user, "Your account name on that registry. For tokens, some registries accept any user name; check the registry's documentation."),
            Ui.Field("Password or access token", password, "Prefer a personal access token with the narrowest scope. It is sent to docker login on standard input (--password-stdin), never placed on the command line, never written to history or logs, and the field is cleared afterwards."),
            // A horizontal stack gives the caption unlimited width, so it never wrapped and was cut off at the card edge.
            new DockPanel { Children = { Ui.Icon(Icons.Lock, 16).Margin(0, 0, 8, 0).Dock(Dock.Left), Ui.Caption("Sent to the engine on standard input, never stored or logged by this app. Docker itself stores the credential in its configured credential store.") } },
            formError,
            signIn);
        SignInCard = PageParts.Section("Sign in", null, form);
    }

    public void Reload()
    {
        file = PageParts.ReadAuthFile();
        Render();
    }

    private void Render()
    {
        signedIn.Children.Clear();
        if (file == null) return;
        var path = PageParts.DisplayPath(file);
        if (file.Error != null)
        {
            signedIn.Children.Add(Ui.EmptyState(Icons.Warning, "Configuration unreadable", $"{path}: {file.Error}"));
            fileNote.Text = "";
            return;
        }
        if (!file.Exists || file.Entries.Count == 0)
        {
            signedIn.Children.Add(Ui.EmptyState(Icons.Cloud, "No registries listed", file.Exists ? $"{path} names no registries. Sign in to add one." : $"{path} does not exist yet. Signing in creates it."));
            fileNote.Text = "";
            return;
        }
        var shown = file.Entries.Where(e => search.Matches(e.Registry + " " + e.Source)).ToList();
        if (shown.Count == 0) signedIn.Children.Add(Ui.Caption(search.Status.Length > 0 ? search.Status : "No registries match the search."));
        foreach (var entry in shown)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            row.Children.Add(Ui.Icon(Icons.Cloud, 20, false, "Primary").Margin(0, 0, 12, 0).Dock(Dock.Left));
            var e = entry;
            var signOut = Ui.AsyncButton("Sign out", null, ButtonKind.Text, () => SignOutAsync(e.Registry));
            // Every row has the same visible label, so the accessible name carries the registry.
            AutomationProperties.SetName(signOut, Loc.T("Sign out of") + " " + e.Registry);
            row.Children.Add(signOut.Dock(Dock.Right));
            row.Children.Add(Ui.VStack(2, Ui.Text(entry.Registry, 14, FontWeights.SemiBold, "OnSurface", false, false), Ui.Caption(Loc.T("Listed under") + " " + entry.Source)));
            signedIn.Children.Add(row);
        }
        fileNote.Text = $"Source: {path}" + (file.CredentialStore != null ? $" · credential store: {file.CredentialStore}" : "") + ". Docker Desktop may list registries here that the credential store no longer holds.";
    }

    private async Task SignInAsync()
    {
        formError.Text = "";
        var host = registry.Text.Trim();
        var name = user.Text.Trim();
        if (host.Length == 0) { formError.Text = Loc.T("Enter a registry host, for example docker.io."); return; }
        if (name.Length == 0) { formError.Text = Loc.T("Enter a user name."); return; }
        if (password.SecurePassword.Length == 0) { formError.Text = Loc.T("Enter a password or access token."); return; }
        try { DockerService.ValidateOperand(host, "registry"); DockerService.ValidateOperand(name, "user"); }
        catch (ArgumentException) { formError.Text = Loc.T("Registry and user name cannot be empty, start with a hyphen or contain line breaks."); return; }
        if (!isCurrent()) { formError.Text = Loc.T("The engine context changed after this page loaded. Refresh the page before signing in."); return; }
        CommandResult? result;
        try
        {
            // The secret is passed on standard input only; it never enters arguments, labels, history or output.
            result = await Host.Execute("Sign in to " + host, ["login", host, "--username", name, "--password-stdin"], true, snapshot(), password.Password + "\n");
        }
        finally { password.Clear(); }
        if (result?.Success == true) Reload();
        else if (result != null) formError.Text = Loc.T("Sign-in failed. The output panel shows docker's message.");
    }

    private async Task SignOutAsync(string host)
    {
        if (!await Host.Overlay.ConfirmAsync(Loc.T("Sign out of") + " " + host + "?", $"Runs docker logout {host}. Docker removes the stored credential for this registry. Pulls and pushes that need it will fail until you sign in again.", "Sign out", true)) return;
        var result = await Host.Execute("Sign out of " + host, ["logout", host], true, snapshot());
        if (result != null) Reload();
    }
}
