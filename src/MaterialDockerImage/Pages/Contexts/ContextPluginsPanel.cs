using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>One docker CLI plugin from <c>docker info</c>'s ClientInfo.Plugins.</summary>
public sealed record ContextPluginEntry(string Name, string Version, string Vendor, string Path, string Description, string Error)
{
    public bool Failed => Error.Length > 0;
    public string SearchText => $"{Name} {Version} {Vendor} {Path} {Description} {Error}";
}

/// <summary>CLI plugins (buildx, compose, scout and others) with search, failures and CLI warnings.</summary>
public sealed class ContextPluginsPanel : Border
{
    private static AppHost Host => AppHost.Current;
    private readonly SearchFilter search = new("Search plugins", 280);
    private readonly StackPanel list = new();
    private readonly StackPanel warnings = new();
    private readonly TextBlock summary = Ui.Caption("Not loaded yet.");
    private List<ContextPluginEntry> plugins = new();

    public ContextPluginsPanel()
    {
        search.Changed += Render;
        var head = new DockPanel();
        head.Children.Add(search.Dock(Dock.Right));
        head.Children.Add(Ui.VStack(2, Ui.Text("CLI plugins", 16, FontWeights.SemiBold), summary));
        Child = Ui.VStack(14, head, warnings, list);
        Padding = new Thickness(20);
        CornerRadius = new CornerRadius(16);
        SetResourceReference(BackgroundProperty, "SurfaceLow");
    }

    public async Task LoadAsync()
    {
        // Plugins are a property of this computer's docker CLI, not of an engine, so no --context is passed.
        var r = await ContextActions.ReadLocal(["info", "--format", "{{json .ClientInfo}}"]);
        plugins = new();
        warnings.Children.Clear();
        var text = r.StandardOutput.Trim().Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith('{'));
        if (text == null)
        {
            summary.Text = "docker info did not return client information: " + (r.StandardError.Trim().Split('\n').FirstOrDefault() ?? $"exit code {r.ExitCode}");
            Render();
            return;
        }
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            foreach (var p in CliValues.Arr(root, "Plugins"))
                plugins.Add(new ContextPluginEntry(CliValues.Str(p, "Name"), CliValues.Str(p, "Version"), CliValues.Str(p, "Vendor"), CliValues.Str(p, "Path"), CliValues.Str(p, "ShortDescription"), CliValues.Str(p, "Err")));
            foreach (var w in CliValues.Arr(root, "Warnings"))
                warnings.Children.Add(Ui.Card(Ui.HStack(10, Ui.Icon(Icons.WarningAmber, 18, true, "OnWarningContainer"), Ui.Text(w.ToString(), 13, null, "OnWarningContainer", true, false)), 10, "WarningContainer", 12).Margin(0, 0, 0, 6));
            var failed = plugins.Count(p => p.Failed);
            summary.Text = Loc.Line($"{plugins.Count} plugin{(plugins.Count == 1 ? "" : "s")} found by the docker CLI" + (failed > 0 ? $" · {failed} failed to load" : " · all loaded") + (r.Success ? "" : " · engine unreachable, client details still shown"));
        }
        catch (JsonException e) { summary.Text = Loc.T("The client information could not be parsed:") + " " + e.Message; }
        Render();
    }

    private void Render()
    {
        list.Children.Clear();
        var shown = plugins.Where(p => search.Matches(p.SearchText)).OrderBy(p => p.Failed ? 0 : 1).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (plugins.Count == 0) { list.Children.Add(Ui.Caption("No plugins reported. Docker Desktop installs buildx and compose as CLI plugins; standalone installations may not have any.")); return; }
        if (shown.Count == 0) { list.Children.Add(Ui.Caption(search.Status.Length > 0 ? search.Status : "No plugins match the search.")); return; }
        list.Children.Add(HeaderRow());
        foreach (var p in shown) list.Children.Add(Row(p));
    }

    private static readonly double[] Columns = [140, 110, 0, 120];

    private static Grid TableGrid(params UIElement[] cells)
    {
        var g = new Grid();
        for (var i = 0; i < Columns.Length; i++)
        {
            if (i > 0) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = Columns[i] == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(Columns[i]) });
        }
        for (var i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i * 2); g.Children.Add(cells[i]); }
        return g;
    }

    private static FrameworkElement HeaderRow()
    {
        var row = TableGrid(Ui.Overline("Plugin"), Ui.Overline("Version"), Ui.Overline("Provides"), Ui.Overline("Status"));
        row.Height = 36; row.Margin = new Thickness(8, 0, 8, 0);
        foreach (UIElement c in row.Children) if (c is FrameworkElement f) f.VerticalAlignment = VerticalAlignment.Center;
        return row;
    }

    private static FrameworkElement Row(ContextPluginEntry p)
    {
        var name = Ui.Mono(p.Name.Length > 0 ? p.Name : "(unnamed)", 13, "OnSurface");
        name.FontWeight = FontWeights.SemiBold;
        var version = Ui.Mono(p.Version, 12, "OnSurfaceVariant");
        var detail = string.Join(" · ", new[] { p.Description, p.Vendor.Length > 0 ? Loc.T("Vendor") + ": " + p.Vendor : "" }.Where(s => s.Length > 0));
        var provides = Ui.VStack(2,
            Ui.Text(detail.Length > 0 ? detail : "-", 14, null, "OnSurfaceVariant", true, false),
            p.Path.Length > 0 ? Ui.Mono(PageParts.Sanitize(p.Path), 11, "OnSurfaceVariant", true) : null,
            p.Failed ? Ui.Text(p.Error, 12, null, "Error", true, false) : null);
        var chip = Ui.Badge(p.Failed ? Loc.T("Failed to load") : Loc.T("Enabled"), p.Failed ? Tone.Error : Tone.Success, false);
        chip.HorizontalAlignment = HorizontalAlignment.Left;
        var cells = new UIElement[] { name, version, provides, chip };
        foreach (var c in cells) if (c is FrameworkElement f) f.VerticalAlignment = VerticalAlignment.Center;
        var row = TableGrid(cells);
        var host = new Border { Child = row, MinHeight = 52, Padding = new Thickness(8, 6, 8, 6), BorderThickness = new Thickness(0, 1, 0, 0) };
        host.SetResourceReference(Border.BorderBrushProperty, "SurfaceHighest");
        System.Windows.Automation.AutomationProperties.SetName(host, p.Name + (p.Version.Length > 0 ? " " + p.Version : "") + ", " + (p.Failed ? "failed to load" : "enabled"));
        return host;
    }
}
