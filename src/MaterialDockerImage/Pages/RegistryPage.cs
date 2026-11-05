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
        var top = PageParts.Responsive(accounts.SignedInCard, accounts.SignInCard, 1000);
        var body = Ui.Scroll(Ui.VStack(12, top, hub.Card, builders.Card).Margin(0, 0, 8, 16));
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
