using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Server cards for the Minecraft board: tile, kind line, state chip and Players, Memory and Port metrics.</summary>
internal static class MinecraftServerCards
{
    public static FrameworkElement Card(MinecraftServer s, bool selected, Action select)
    {
        var glyph = Ui.Icon(s.IsJava ? Icons.Public : Icons.Language, 22, true, "OnPrimaryContainer");
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        var tile = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(12), Child = glyph, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        tile.SetResourceReference(Border.BackgroundProperty, "PrimaryContainer");
        var kind = s.IsJava ? $"Java · {s.FlavourText} · {s.Spec.Version}" : "Bedrock · official server";
        var head = new DockPanel();
        head.Children.Add(tile.Dock(Dock.Left));
        head.Children.Add(Ui.Badge(s.State, s.StateTone).Dock(Dock.Right).Margin(8, 0, 0, 0));
        head.Children.Add(Ui.VStack(2, Ui.Text(s.Name, 16, FontWeights.SemiBold, "OnSurface", false, false), Ui.Text(kind, 12, null, "OnSurfaceVariant", false, false)));

        var metrics = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        for (var i = 0; i < 3; i++) metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void Metric(int column, string value, string label)
        {
            var m = Ui.VStack(0, Ui.Text(value, 17, FontWeights.SemiBold, "OnSurface", false, false), Ui.Caption(label));
            Grid.SetColumn(m, column);
            metrics.Children.Add(m);
        }
        Metric(0, s.IsRunning ? s.Players : "–", "Players");
        Metric(1, s.IsRunning ? (s.IsJava ? s.Spec.MemoryGb + " GB" : "auto") : "–", "Memory");
        Metric(2, PortShort(s), "Port");

        var card = Ui.Card(Ui.VStack(0, head, metrics), 14, "SurfaceLow", 16);
        card.BorderThickness = new Thickness(2);
        if (selected) card.SetResourceReference(Border.BorderBrushProperty, "Primary");
        else card.BorderBrush = System.Windows.Media.Brushes.Transparent;
        card.Margin = new Thickness(0, 0, 12, 12);
        card.Cursor = Cursors.Hand;
        card.Focusable = true;
        System.Windows.Automation.AutomationProperties.SetName(card, $"{s.Name}, {s.State}, {Loc.T("Players")} {(s.IsRunning ? s.Players : "–")}");
        card.MouseLeftButtonUp += (_, _) => select();
        card.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { e.Handled = true; select(); } };
        return card;
    }

    private static string PortShort(MinecraftServer s)
    {
        if (s.Binding == null) return "–";
        var port = MinecraftSpecRules.ParseBinding(s.Binding)?.Port ?? s.Spec.Port;
        return s.IsJava ? port.ToString() : port + "/udp";
    }
}

/// <summary>
/// The controls column beside the console on the Minecraft board: lifecycle buttons, how players connect, who is
/// online and a shortcut to the full Properties tab. Setting values are edited in Properties, never here.
/// </summary>
internal sealed class MinecraftControlsAside
{
    private readonly MinecraftPage page;
    private readonly MinecraftServerDetail detail;
    private readonly ContentControl body = new();
    private MinecraftServer server;
    public FrameworkElement Root { get; }

    public MinecraftControlsAside(MinecraftPage page, MinecraftServer server, MinecraftServerDetail detail)
    {
        this.page = page; this.server = server; this.detail = detail;
        Root = Ui.Scroll(Ui.Card(body, 16, "SurfaceLow", 16));
        Render();
    }

    public void Update(MinecraftServer next) { server = next; Render(); }

    private void Render()
    {
        var s = server;
        var buttons = new Grid();
        for (var i = 0; i < 3; i++) buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void Add(int column, Button b) { b.Margin = new Thickness(column == 0 ? 0 : 6, 0, column == 2 ? 0 : 6, 0); Grid.SetColumn(b, column); buttons.Children.Add(b); }
        if (s.IsRunning)
        {
            Add(0, Ui.AsyncButton("Restart", Icons.RestartAlt, ButtonKind.Tonal, () => page.RestartAsync(server)));
            Add(1, Ui.AsyncButton("Stop", Icons.Stop, ButtonKind.Outlined, () => page.StopAsync(server)));
        }
        else
        {
            Add(0, Ui.AsyncButton("Start", Icons.PlayArrow, ButtonKind.Filled, () => page.StartAsync(server)));
            Add(1, Ui.Button("Logs", Icons.ReceiptLong, ButtonKind.Outlined, () => page.ShowLogs(server)));
        }
        Add(2, Ui.AsyncButton("Backup", Icons.Backup, ButtonKind.Outlined, () => page.BackupAsync(server)));

        var online = s.OnlineNames.Count == 0
            ? (UIElement)Ui.Caption(s.IsRunning ? "Nobody is online, or the player list is not available." : "The server is not running.")
            : Ui.Wrap(6, s.OnlineNames.Select(n => (UIElement)Ui.Badge(n, Tone.Neutral, false)).ToArray());

        var facts = Ui.VStack(6,
            PageKit.Fact("Edition", s.EditionText),
            PageKit.Fact("Version", s.Spec.Version, true),
            PageKit.Fact("Address", s.Binding == null ? "not published" : s.PortText, true),
            PageKit.Fact("World volume", s.VolumeName, true),
            PageKit.Fact("Uptime", s.UptimeText));

        body.Content = Ui.VStack(14,
            buttons,
            Ui.Text("Players online", 14, FontWeights.SemiBold),
            online,
            Ui.Text("Server", 14, FontWeights.SemiBold),
            facts,
            Ui.Button("Edit server properties", Icons.Tune, ButtonKind.Outlined, () => detail.ShowTab("Properties")),
            Ui.Caption("Changes to properties are written to the container environment and need a restart. The world lives in the volume " + s.VolumeName + "; backups use the same volume archive flow as Storage."));
    }
}
