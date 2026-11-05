using System.Windows;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Registry sign-in and sign-out, Docker Hub search with pull, and buildx builder management.</summary>
public sealed class RegistryPage : PageBase
{
    public override string Key => "Registry";
    private readonly RegistryAccounts accounts;
    private readonly HubSearch hub;
    private readonly BuildersPanel builders;

    public RegistryPage()
    {
        accounts = new RegistryAccounts(() => IsCurrent, () => SnapshotContext);
        hub = new HubSearch(() => IsCurrent, () => SnapshotContext);
        builders = new BuildersPanel(() => IsCurrent, () => SnapshotContext);
        var header = Ui.Header("Registry", "Registry sign-in, Docker Hub search and buildx builders for the selected engine context", out _,
            Ui.AsyncButton("Refresh", Icons.Refresh, ButtonKind.Outlined, RefreshAsync));
        // Three columns as on the Registry board: accounts and sign-in, Hub search, then builders.
        accounts.SignedInCard.Margin = new Thickness(0, 0, 0, 12);
        var left = Ui.VStack(0, accounts.SignedInCard, accounts.SignInCard);
        var body = PageColumns.Fit(1180, 16, (left, 1), (hub.Card, 1.25), (builders.Card, 1.25));
        Layout(header, body);
    }

    public override async Task RefreshAsync()
    {
        accounts.Reload();
        await builders.RefreshAsync();
    }

    public override IEnumerable<AppCommand> PageCommands() =>
    [
        new AppCommand { Id = "registry.signin", Title = "Sign in to a registry…", Group = "Registry", Icon = Icons.Login, Keywords = "docker login credentials token", Run = () => Host.Navigate(Key) },
        new AppCommand { Id = "registry.search", Title = "Search Docker Hub…", Group = "Registry", Icon = Icons.Search, Keywords = "docker search hub find image", Run = () => Host.Navigate(Key) },
        new AppCommand { Id = "registry.builders", Title = "Manage buildx builders…", Group = "Registry", Icon = Icons.Construction, Keywords = "buildx builder buildkit cache prune", Run = () => Host.Navigate(Key) },
    ];
}
