using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Edits the environment-based settings. The itzg images apply them when the container starts, so changes need a new
/// container: the diff is shown and Recreate replaces the container while keeping the world volume and RCON password.
/// </summary>
internal sealed class MinecraftPropertiesTab : MinecraftTab
{
    private readonly ContentControl form = new();
    private readonly StackPanel diffList = new();
    private readonly TextBlock problems = Ui.Text("", 13, null, "Error", true, false);
    private readonly Button recreate;
    private MinecraftServerSpec edited = new();
    private string? loadedFor;

    public MinecraftPropertiesTab(MinecraftServerDetail detail) : base(detail)
    {
        recreate = Ui.AsyncButton("Recreate container with new settings", Icons.Sync, ButtonKind.Filled, RecreateAsync);
        var reset = Ui.Button("Discard changes", Icons.Undo, ButtonKind.Text, () => Load(true));
        var side = Ui.Card(Ui.VStack(12,
            Ui.Text("Pending changes", 16, FontWeights.SemiBold),
            Ui.Caption("The image reads these settings when the container is created. Recreate stops the server, replaces the container and starts the new one with the same world volume."),
            diffList, problems, Ui.Wrap(8, recreate, reset)), 18, "SurfaceContainer", 16);
        var grid = Ui.SplitInspector(Ui.Scroll(form), Ui.Scroll(side), 1100, 360, 16);
        Children.Add(grid);
    }

    public override void OnShown() => Load(false);

    private void Load(bool force)
    {
        if (!force && loadedFor == Server.Id) { Changed(); return; }
        loadedFor = Server.Id;
        edited = Server.Spec.Clone();
        var sections = new List<UIElement>
        {
            MinecraftSpecEditor.VersionSection(edited, () => Context, Changed),
            MinecraftSpecEditor.ResourcesSection(edited, Changed),
            MinecraftSpecEditor.GameplaySection(edited, Changed),
            MinecraftSpecEditor.PlayersSection(edited, Changed),
        };
        if (!edited.EulaAccepted)
        {
            var eula = Ui.Check("I have read and accept the Minecraft End User License Agreement", false, v => { edited.EulaAccepted = v; Changed(); });
            var link = Ui.AsyncButton("Read the Minecraft EULA", Icons.OpenInNew, ButtonKind.Text, async () =>
            {
                if (await Host.Overlay.ConfirmAsync("Open the Minecraft EULA?", "This opens https://aka.ms/MinecraftEULA in your web browser.", "Open")) PageParts.OpenExternal("https://aka.ms/MinecraftEULA");
            });
            link.HorizontalAlignment = HorizontalAlignment.Left;
            sections.Insert(0, MinecraftControls.Section("Minecraft EULA", "This server's EULA is not accepted, so it cannot start.", link, eula));
        }
        form.Content = Ui.VStack(16, sections.ToArray());
        Changed();
    }

    private void Changed()
    {
        var diff = MinecraftSpecRules.Diff(Server.Spec, edited);
        diffList.Children.Clear();
        if (diff.Count == 0) diffList.Children.Add(Ui.Caption("No changes."));
        foreach (var line in diff)
        {
            var tone = line[0] == '+' ? Tone.Success : line[0] == '-' ? Tone.Error : Tone.Primary;
            diffList.Children.Add(Ui.Columns(8, [0, 1], Ui.Badge(line[..1], tone), Ui.Mono(line[2..], 12.5, "OnSurface", true)).Margin(0, 0, 0, 4));
        }
        var p = MinecraftSpecRules.Validate(edited);
        problems.Text = string.Join("\n", p);
        recreate.IsEnabled = diff.Count > 0 && p.Count == 0;
    }

    private async Task RecreateAsync()
    {
        if (!EnsureCurrent()) return;
        var diff = MinecraftSpecRules.Diff(Server.Spec, edited);
        if (diff.Count == 0) return;
        var message = "These settings change:\n" + string.Join("\n", diff) + "\n\n" + (Server.IsRunning ? "The server is saved and stopped, " : "") + $"the container {Server.ContainerName} is replaced and the world volume {Server.VolumeName} is kept. If the new container cannot be created, the previous one is restored.";
        if (edited.Version != Server.Spec.Version) message += "\n\nChanging the version upgrades the world on first start. Worlds cannot be downgraded; make a backup first.";
        if (!await Host.Overlay.ConfirmAsync(Loc.T("Recreate") + " " + Server.Name + "?", message, "Recreate")) return;
        if (await MinecraftOps.RecreateAsync(Server, edited.Clone(), Context)) loadedFor = null;
        await Detail.ReloadAsync();
    }
}
