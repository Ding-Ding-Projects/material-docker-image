using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Core;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Stat tiles (state, players, memory, CPU, uptime) from docker stats and RCON, plus connection details and facts.</summary>
internal sealed class MinecraftOverviewTab : MinecraftTab
{
    private readonly ContentControl content = new();
    private int version;

    public MinecraftOverviewTab(MinecraftServerDetail detail) : base(detail)
    {
        Children.Add(Ui.Scroll(content));
    }

    public override void OnShown() => _ = LoadAsync(++version);

    private static Border Tile(string label, string value, string? caption, string icon)
    {
        var stack = Ui.VStack(4, Ui.HStack(8, Ui.Icon(icon, 18, true, "Primary"), Ui.Overline(label)), Ui.Text(value, 26, FontWeights.SemiBold, "OnSurface", false, false), caption == null ? null : Ui.Caption(caption));
        var tile = Ui.Card(stack, 16, "SurfaceContainer", 16);
        tile.Width = 210; tile.Margin = new Thickness(0, 0, 12, 12);
        return tile;
    }

    private async Task LoadAsync(int mine)
    {
        var s = Server;
        string memory = "—", memoryCaption = "not running", cpu = "—";
        if (s.IsRunning)
        {
            var stats = await Host.Read(["container", "stats", "--no-stream", "--format", "{{json .}}", s.Id], Context);
            if (stats.Success && DockerJson.ParseRows(stats.StandardOutput).FirstOrDefault() is { ValueKind: JsonValueKind.Object } row)
            {
                var usage = DockerJson.Text(row, "MemUsage");
                memory = usage.Split('/')[0].Trim();
                memoryCaption = "of " + (usage.Contains('/') ? usage.Split('/')[1].Trim() : "unknown limit") + " · " + DockerJson.Text(row, "MemPerc");
                cpu = DockerJson.Text(row, "CPUPerc");
            }
            else memoryCaption = "docker stats failed: " + MinecraftOps.Trim(stats);
        }
        if (mine != version) return;
        var players = s.Players;
        var connect = ConnectText(s);
        var tiles = new WrapPanel();
        tiles.Children.Add(Tile("State", s.State, s.Status, Icons.Speed));
        tiles.Children.Add(Tile("Players", players, s.OnlineNames.Count > 0 ? string.Join(", ", s.OnlineNames) : s.IsRunning ? "From rcon-cli list" : null, Icons.Person));
        tiles.Children.Add(Tile("Memory", memory, memoryCaption, Icons.Memory));
        tiles.Children.Add(Tile("CPU", cpu, s.IsRunning ? "docker stats, one sample" : "not running", Icons.DeveloperBoard));
        tiles.Children.Add(Tile("Uptime", s.UptimeText, s.StartedAt is { } t ? "since " + t.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : null, Icons.Timer));

        var copy = Ui.IconButton(Icons.ContentCopy, "Copy address", () => PageKit.Copy(connect.Address, "the server address"));
        var connectCard = MinecraftControls.Section("Connect", connect.Explanation, Ui.Columns(8, [1, 0], Ui.Mono(connect.Address, 18, "OnSurface", true), copy));
        var facts = MinecraftControls.Section("Details", null, Ui.VStack(6,
            PageKit.Fact("Edition", s.EditionText),
            PageKit.Fact("Version", s.Spec.Version, true),
            PageKit.Fact("Server type", s.FlavourText),
            PageKit.Fact("Image", s.Image, true),
            PageKit.Fact("Container", s.ContainerName + " · " + (s.Id.Length > 12 ? s.Id[..12] : s.Id), true),
            PageKit.Fact("World volume", s.VolumeName, true),
            PageKit.Fact("Memory", s.MemoryText),
            PageKit.Fact("RCON", s.IsJava ? (s.Spec.RconEnabled ? "on (inside the container only)" : "off") : "not available for Bedrock"),
            PageKit.Fact("EULA", s.Spec.EulaAccepted ? "accepted" : "not accepted: the server will not start"),
            PageKit.Fact("Created", s.CreatedText),
            PageKit.Fact("Engine context", Context ?? "default", true)));
        content.Content = Ui.VStack(4, tiles, PageParts.Responsive(connectCard, facts, 1000));
    }

    private (string Address, string Explanation) ConnectText(MinecraftServer s)
    {
        if (s.Binding == null) return ("not published", "The container has no published port.");
        var parsed = MinecraftSpecRules.ParseBinding(s.Binding);
        var port = parsed?.Port ?? s.Spec.Port;
        var defaultPort = s.IsJava ? 25565 : 19132;
        var remote = !string.IsNullOrEmpty(Context) && Context != "default" && Context != "desktop-linux";
        var host = parsed?.Address switch
        {
            "127.0.0.1" => "localhost",
            "0.0.0.0" => "<this computer's IP address>",
            { } a => a,
            _ => "localhost",
        };
        var address = port == defaultPort && s.IsJava ? host : host + ":" + port;
        var why = parsed?.Address switch
        {
            "127.0.0.1" => "Published on 127.0.0.1 only: players on the engine host can join; others cannot.",
            "0.0.0.0" => "Published on every interface: players on your network join with the engine host's IP address. Use port forwarding for the internet at your own risk.",
            _ => "Published on a specific host address.",
        };
        if (remote) why += " With engine context " + Context + " the engine host may be another machine; use that host's address.";
        if (!s.IsJava) why += " Bedrock uses UDP; enter the address and port separately in the client.";
        return (address, why);
    }
}
