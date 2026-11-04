using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

public sealed record EngineEvent(DateTimeOffset When, string Type, string Action, string Target, string Raw)
{
    public string WhenText => When.ToLocalTime().ToString("HH:mm:ss");
    public static EngineEvent? Parse(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var type = DockerJson.Text(root, "Type");
            var action = DockerJson.Text(root, "Action");
            if (action.Length == 0) action = DockerJson.Text(root, "status");
            var when = long.TryParse(DockerJson.Text(root, "timeNano"), out var nano) && nano > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(nano / 1_000_000)
                : long.TryParse(DockerJson.Text(root, "time"), out var sec) ? DateTimeOffset.FromUnixTimeSeconds(sec) : DateTimeOffset.Now;
            var target = "";
            if (root.TryGetProperty("Actor", out var actor) && actor.ValueKind == JsonValueKind.Object)
            {
                var id = DockerJson.Text(actor, "ID");
                var name = actor.TryGetProperty("Attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Object ? DockerJson.Text(attrs, "name") : "";
                var image = actor.TryGetProperty("Attributes", out var a2) && a2.ValueKind == JsonValueKind.Object ? DockerJson.Text(a2, "image") : "";
                target = name.Length > 0 ? name : id.Length > 24 ? id[..12] : id;
                if (image.Length > 0 && type == "container") target += " · " + image;
            }
            return new(when, type, action, target, line);
        }
        catch (JsonException) { return null; }
    }
}

/// <summary>
/// Live docker events stream with its own cancellation, type filter chips, search, a 1,000-row bound,
/// and automatic stop when the page is hidden. It runs in the background and never blocks foreground operations.
/// </summary>
internal sealed class EventsPanel
{
    private const int Limit = 1000;
    private static AppHost Host => AppHost.Current;
    private readonly Func<string?> snapshot;
    private readonly List<EngineEvent> all = new();
    private readonly ObservableCollection<EngineEvent> shown = new();
    private readonly ListView list = new() { MinHeight = 260, MaxHeight = 520 };
    private readonly SearchFilter search = new("Search events", 240);
    private readonly Dictionary<string, ToggleButton> chips = new();
    private readonly TextBlock status = Ui.Caption("Not streaming. Start to follow engine events as they happen.");
    private readonly ToggleButton toggle;
    private readonly StringBuilder partial = new();
    private CancellationTokenSource? stream;
    private DateTimeOffset? since;
    private int received;
    private string state = "Not streaming";
    public Border Card { get; }
    public bool Running => stream != null;

    public EventsPanel(Func<string?> snapshot)
    {
        this.snapshot = snapshot;
        toggle = new ToggleButton { Style = (Style)Application.Current.FindResource("Chip"), Content = Loc.T("Stream events") };
        System.Windows.Automation.AutomationProperties.SetName(toggle, Loc.T("Stream events"));
        toggle.Checked += (_, _) => Start();
        toggle.Unchecked += (_, _) => Stop("Stopped");
        var filters = new WrapPanel();
        foreach (var type in new[] { "container", "image", "network", "volume" })
        {
            var chip = Ui.Chip(char.ToUpperInvariant(type[0]) + type[1..] + "s", false, _ => Apply());
            chip.Margin = new Thickness(0, 0, 8, 6);
            chips[type] = chip;
            filters.Children.Add(chip);
        }
        search.Changed += Apply;
        var view = new GridView();
        view.Columns.Add(Column("Time", nameof(EngineEvent.WhenText), 80));
        view.Columns.Add(Column("Type", nameof(EngineEvent.Type), 100));
        view.Columns.Add(Column("Action", nameof(EngineEvent.Action), 140));
        view.Columns.Add(Column("Target", nameof(EngineEvent.Target), 420));
        list.View = view;
        list.ItemsSource = shown;
        System.Windows.Automation.AutomationProperties.SetName(list, Loc.T("Engine events"));
        var menu = new ContextMenu();
        menu.Items.Add(Ui.MenuItem("Copy event JSON", Icons.ContentCopy, () => { if (list.SelectedItem is EngineEvent e) Clipboard.SetText(e.Raw); }));
        menu.Items.Add(Ui.MenuItem("Clear list", Icons.Delete, () => { all.Clear(); Apply(); }));
        list.ContextMenu = menu;
        var bar = new WrapPanel();
        bar.Children.Add(search.Margin(0, 0, 12, 6));
        bar.Children.Add(filters);
        Card = PageParts.Section("Events", "docker events · keeps the last 1,000 · stops when you leave this page", Ui.VStack(10, bar, list, status), toggle);
    }

    private static GridViewColumn Column(string header, string path, double width)
    {
        var factory = new FrameworkElementFactory(typeof(TextBlock));
        factory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(path));
        factory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        factory.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        factory.SetValue(TextBlock.FontSizeProperty, 12.5);
        return new GridViewColumn { Header = Loc.T(header), Width = width, CellTemplate = new DataTemplate { VisualTree = factory } };
    }

    private bool Visible(EngineEvent e)
    {
        var active = chips.Where(c => c.Value.IsChecked == true).Select(c => c.Key).ToList();
        if (active.Count > 0 && !active.Contains(e.Type)) return false;
        return search.Matches($"{e.WhenText} {e.Type} {e.Action} {e.Target}");
    }

    private void Apply()
    {
        shown.Clear();
        foreach (var e in all.Where(Visible)) shown.Add(e);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var head = stream != null ? $"Streaming since {since?.ToLocalTime():HH:mm:ss} on {snapshot() ?? "the current context"}" : state;
        status.Text = $"{head} · {received} received · {all.Count} kept · {shown.Count} shown" + (search.Status.Length > 0 ? " · " + search.Status : "");
    }

    private void Add(EngineEvent e)
    {
        all.Insert(0, e);
        if (all.Count > Limit) { var dropped = all[^1]; all.RemoveAt(all.Count - 1); shown.Remove(dropped); }
        if (Visible(e)) shown.Insert(0, e);
        UpdateStatus();
    }

    public void Start()
    {
        if (stream != null) return;
        stream = new CancellationTokenSource();
        since = DateTimeOffset.Now; received = 0; partial.Clear();
        toggle.IsChecked = true;
        UpdateStatus();
        _ = RunAsync(stream);
    }

    private async Task RunAsync(CancellationTokenSource source)
    {
        // Progress created on the UI thread reports back on the UI thread.
        var progress = new Progress<DockerOutput>(chunk =>
        {
            if (source.IsCancellationRequested || chunk.IsError) return;
            partial.Append(chunk.Text);
            var text = partial.ToString();
            var last = text.LastIndexOf('\n');
            if (last < 0) return;
            partial.Clear().Append(text[(last + 1)..]);
            foreach (var line in text[..last].Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (EngineEvent.Parse(line) is { } e) { received++; Add(e); }
        });
        string end;
        try
        {
            var result = await Host.Docker.ExecuteAsync(["events", "--format", "{{json .}}"], snapshot(), progress, source.Token);
            end = result.Cancelled ? "Stopped" : $"Stream ended with exit code {result.ExitCode}" + (result.StandardError.Trim().Length > 0 ? ": " + PageParts.Shorten(result.StandardError.Trim(), 200) : "");
        }
        catch (OperationCanceledException) { end = "Stopped"; }
        catch (System.ComponentModel.Win32Exception) { end = "Stream ended: the docker command was not found"; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException) { end = Loc.T("Stream ended:") + " " + e.Message; }
        if (stream == source) { stream = null; toggle.IsChecked = false; }
        source.Dispose();
        state = end;
        UpdateStatus();
    }

    public void Stop(string reason)
    {
        var s = stream;
        if (s == null) return;
        stream = null;
        try { s.Cancel(); } catch (ObjectDisposedException) { }
        toggle.IsChecked = false;
        state = reason;
        UpdateStatus();
    }
}
