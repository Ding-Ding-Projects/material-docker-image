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
        foreach (var p in shown) list.Children.Add(Row(p).Margin(0, 0, 0, 8));
    }

    private static Border Row(ContextPluginEntry p)
    {
        var icon = Ui.Icon(p.Failed ? Icons.ErrorOutline : Icons.Extension, 22, true, p.Failed ? "Error" : "Primary");
        var title = Ui.HStack(8, Ui.Text(p.Name.Length > 0 ? p.Name : "(unnamed)", 14, FontWeights.SemiBold, "OnSurface", false, false),
            p.Version.Length > 0 ? Ui.Badge(p.Version, Tone.Neutral) : null,
            p.Failed ? Ui.Badge(Loc.T("Failed to load"), Tone.Error) : null);
        var body = Ui.VStack(4, title,
            p.Description.Length > 0 || p.Vendor.Length > 0 ? Ui.Caption(string.Join(" · ", new[] { p.Description, p.Vendor.Length > 0 ? Loc.T("Vendor") + ": " + p.Vendor : "" }.Where(s => s.Length > 0))) : null,
            p.Path.Length > 0 ? Ui.Mono(PageParts.Sanitize(p.Path), 12, "OnSurfaceVariant", true) : null,
            p.Failed ? Ui.Text(p.Error, 12, null, "Error", true, false) : null);
        var row = new DockPanel();
        row.Children.Add(icon.Dock(Dock.Left).Margin(0, 2, 12, 0));
        icon.VerticalAlignment = VerticalAlignment.Top;
        row.Children.Add(body);
        return Ui.Card(row, 12, "SurfaceContainer", 12);
    }
}
