using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>A tab inside the server detail view. Tabs read the current server from the detail on every action.</summary>
internal abstract class MinecraftTab : DockPanel
{
    protected static AppHost Host => AppHost.Current;
    protected MinecraftServerDetail Detail { get; }
    protected MinecraftServer Server => Detail.Server;
    protected string? Context => Detail.Page.Context;
    protected MinecraftTab(MinecraftServerDetail detail) { Detail = detail; LastChildFill = true; }
    /// <summary>Called when the tab is shown or the server was reloaded while the tab is visible.</summary>
    public virtual void OnShown() { }
    /// <summary>Called when the tab is hidden or the detail closes; stop background streams here.</summary>
    public virtual void OnHidden() { }
    protected bool EnsureCurrent() => Detail.Page.EnsureCurrent();

    /// <summary>A banner explaining why the tab cannot act right now.</summary>
    protected static Border Notice(string text, Tone tone = Tone.Neutral)
    {
        var (bg, fg) = Ui.ToneKeys(tone);
        return Ui.Card(Ui.Columns(10, [0, 1], Ui.Icon(tone == Tone.Warning ? Icons.Warning : Icons.Info, 20, true, fg), Ui.Text(text, 13, null, fg, true, true)), 12, bg, 12);
    }
}

/// <summary>Full-page view of one server: identity header with lifecycle actions and tabs.</summary>
internal sealed class MinecraftServerDetail : DockPanel
{
    private static readonly string[] TabNames = ["Overview", "Console", "Players", "Properties", "Files", "Backups", "World"];
    public MinecraftPage Page { get; }
    public MinecraftServer Server { get; private set; }
    private readonly ContentControl headerHost = new();
    private readonly ContentControl body = new();
    private readonly Dictionary<string, MinecraftTab> tabs = new();
    private string tab;
    private bool gone;

    public MinecraftServerDetail(MinecraftPage page, MinecraftServer server, string initialTab)
    {
        Page = page; Server = server; tab = TabNames.Contains(initialTab) ? initialTab : "Overview";
        LastChildFill = true;
        Children.Add(headerHost.Dock(Dock.Top));
        var bar = new WrapPanel { Margin = new Thickness(0, 12, 0, 12) };
        var group = "mcdetail" + Guid.NewGuid().ToString("N");
        foreach (var name in TabNames)
        {
            var radio = new RadioButton { Style = (Style)Application.Current.FindResource("Tab.Item"), GroupName = group, Content = Loc.T(name), IsChecked = name == tab, Padding = new Thickness(14, 0, 14, 0) };
            System.Windows.Automation.AutomationProperties.SetName(radio, Loc.T(name));
            radio.Checked += (_, _) => Select(name);
            bar.Children.Add(radio);
        }
        System.Windows.Automation.AutomationProperties.SetName(bar, Loc.T("Server sections"));
        Children.Add(bar.Dock(Dock.Top));
        Children.Add(body);
        RenderHeader();
        Select(tab);
    }

    private MinecraftTab Create(string name) => name switch
    {
        "Console" => new MinecraftConsoleTab(this),
        "Players" => new MinecraftPlayersTab(this),
        "Properties" => new MinecraftPropertiesTab(this),
        "Files" => new MinecraftFilesTab(this),
        "Backups" => new MinecraftBackupsTab(this),
        "World" => new MinecraftWorldTab(this),
        _ => new MinecraftOverviewTab(this),
    };

    private void Select(string name)
    {
        if (tabs.TryGetValue(tab, out var old) && tab != name) old.OnHidden();
        tab = name;
        if (!tabs.TryGetValue(name, out var t)) tabs[name] = t = Create(name);
        body.Content = t;
        t.OnShown();
    }

    /// <summary>Re-renders after a page refresh. A null server means it no longer exists on this engine.</summary>
    public void Update(MinecraftServer? server)
    {
        if (server == null)
        {
            gone = true;
            headerHost.Content = Ui.VStack(12, Back(), Ui.Card(Ui.Text($"The server {Server.Name} is no longer on this engine. It may have been removed outside this page.", 14, null, "OnErrorContainer", true, true), 14, "ErrorContainer", 12));
            return;
        }
        gone = false;
        var stateChanged = server.State != Server.State;
        Server = server;
        RenderHeader();
        if (stateChanged && tabs.TryGetValue(tab, out var t)) t.OnShown();
    }

    /// <summary>Reloads the visible tab (for example after a backup elsewhere).</summary>
    public void ReloadTab() { if (tabs.TryGetValue(tab, out var t)) t.OnShown(); }

    public void Close() { foreach (var t in tabs.Values) t.OnHidden(); }

    public Task ReloadAsync() => Page.SafeRefresh();

    private Button Back() => Ui.Button("All servers", Icons.ArrowBack, ButtonKind.Text, Page.CloseDetail);

    private void RenderHeader()
    {
        if (gone) return;
        var s = Server;
        var chips = Ui.Wrap(8,
            Ui.Badge(s.State, s.StateTone),
            Ui.Badge(s.EditionText, s.IsJava ? Tone.Primary : Tone.Tertiary),
            Ui.Badge(s.FlavourText + " " + s.Spec.Version, Tone.Neutral),
            Ui.Badge(s.PortText, Tone.Neutral));
        var title = Ui.VStack(4, Ui.Title(s.Name, 20), chips);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        void Add(UIElement e) { if (e is FrameworkElement fe) fe.Margin = new Thickness(8, 4, 0, 4); actions.Children.Add(e); }
        if (s.IsRunning)
        {
            Add(Ui.AsyncButton("Stop", Icons.Stop, ButtonKind.Tonal, () => Page.StopAsync(Server)));
            Add(Ui.AsyncButton("Restart", Icons.RestartAlt, ButtonKind.Tonal, () => Page.RestartAsync(Server)));
        }
        else Add(Ui.AsyncButton("Start", Icons.PlayArrow, ButtonKind.Filled, () => Page.StartAsync(Server)));
        Add(Ui.AsyncButton("Backup", Icons.Backup, ButtonKind.Tonal, () => Page.BackupAsync(Server)));
        Add(Ui.Button("Logs", Icons.ReceiptLong, ButtonKind.Text, () => Page.ShowLogs(Server)));
        var remove = Ui.AsyncButton("Remove…", Icons.Delete, ButtonKind.Text, () => Page.RemoveAsync(Server));
        remove.SetResourceReference(Control.ForegroundProperty, "Error");
        Add(remove);
        Add(Ui.AsyncIconButton(Icons.Refresh, "Refresh", ReloadAsync));
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 220 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(title);
        row.Children.Add(actions.At(0, 1));
        headerHost.Content = Ui.VStack(4, Back(), row);
    }
}
