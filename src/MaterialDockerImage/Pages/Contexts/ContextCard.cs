using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>One entry of <c>docker context ls</c>.</summary>
public sealed record ContextEntry(string Name, string Description, string Endpoint, bool CliCurrent, string Error)
{
    public string SearchText => $"{Name} {Description} {Endpoint} {ContextEndpoint.Transport(Endpoint)}";
}

/// <summary>Result of probing a context with <c>docker --context X version --format "{{json .Server}}"</c>.</summary>
public sealed record ContextProbe(bool Reachable, string Version, string ApiVersion, string Platform, string Message);

/// <summary>A context card: identity, endpoint, CLI/window markers, live reachability and actions.</summary>
public sealed class ContextCardView : Border
{
    private readonly System.Windows.Shapes.Ellipse dot = new() { Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock state = Ui.Text("", 12, null, "OnSurfaceVariant", false, false);
    private readonly Border versionBadge = Ui.Badge("", Tone.Neutral, false);
    private readonly TextBlock reachDetail = Ui.Text("", 12, null, "OnSurfaceVariant");
    public ContextEntry Entry { get; }

    public ContextCardView(ContextEntry entry, bool usedByWindow, ContextActions actions)
    {
        Entry = entry;
        // Design: a radio-style card that stretches with its row. The window's context has a 2 px primary border.
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Padding = new Thickness(usedByWindow ? 14 : 16);
        BorderThickness = new Thickness(usedByWindow ? 2 : 0);
        CornerRadius = new CornerRadius(16);
        SetResourceReference(BackgroundProperty, usedByWindow ? "SurfaceContainer" : "SurfaceLow");
        if (usedByWindow) SetResourceReference(BorderBrushProperty, "Primary");
        dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Outline");
        state.Text = Loc.T("Checking…");
        var name = Ui.Text(entry.Name, 16, FontWeights.SemiBold, "OnSurface", false, false);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var titleRow = new DockPanel();
        titleRow.Children.Add(dot.Dock(Dock.Left).Margin(0, 0, 8, 0));
        titleRow.Children.Add(state.Dock(Dock.Right).Margin(8, 0, 0, 0));
        titleRow.Children.Add(name);
        versionBadge.Visibility = Visibility.Collapsed;
        var chips = Ui.Wrap(6,
            versionBadge,
            Ui.Badge(ContextEndpoint.Transport(entry.Endpoint), Tone.Neutral),
            entry.CliCurrent ? Ui.Badge(Loc.T("Current in CLI"), Tone.Tertiary) : null,
            usedByWindow ? Ui.Badge(Loc.T("Used by this window"), Tone.Primary) : null,
            entry.Error.Length > 0 ? Ui.Badge(Loc.T("Configuration error"), Tone.Error) : null);
        var description = Ui.Text(entry.Description.Length > 0 ? entry.Description : Loc.T("No description"), 13, null, "OnSurfaceVariant", true, false);
        var endpoint = Ui.Mono(entry.Endpoint.Length > 0 ? entry.Endpoint : "(no docker endpoint)", 12, "OnSurfaceVariant", true);
        var use = Ui.Button(usedByWindow ? "In use here" : "Use in this window", usedByWindow ? Icons.Check : Icons.SwapHoriz, usedByWindow ? ButtonKind.Text : ButtonKind.Filled, () => actions.UseInWindow(entry));
        use.IsEnabled = !usedByWindow;
        use.ToolTip = Loc.T("Switches only this window. The docker CLI's current context is not changed.");
        var inspect = Ui.AsyncButton("Inspect", Icons.DataObject, ButtonKind.Text, () => actions.InspectAsync(entry));
        Button? more = null;
        more = Ui.IconButton(Icons.MoreVert, Loc.T("More actions for") + " " + entry.Name, () => actions.ShowMenu(more!, entry));
        var buttons = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        buttons.Children.Add(more.Dock(Dock.Right));
        buttons.Children.Add(Ui.HStack(4, use, inspect));
        var body = Ui.VStack(10, titleRow, endpoint, description, reachDetail, chips,
            entry.Error.Length > 0 ? Ui.Text(entry.Error, 12, null, "Error", true, false) : null,
            buttons);
        Child = body;
        System.Windows.Automation.AutomationProperties.SetName(this, Loc.T("Context") + " " + entry.Name);
        ContextMenu = actions.BuildMenu(entry);
    }

    public void SetProbe(ContextProbe probe)
    {
        dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, probe.Reachable ? "Success" : "Error");
        state.Text = probe.Reachable ? Loc.T("Reachable") : Loc.T("Unreachable");
        state.SetResourceReference(TextBlock.ForegroundProperty, probe.Reachable ? "OnSurfaceVariant" : "Error");
        state.ToolTip = probe.Reachable ? null : probe.Message;
        versionBadge.Visibility = probe.Reachable && probe.Version.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (versionBadge.Child is TextBlock t) t.Text = Loc.T("Engine") + " " + probe.Version;
        reachDetail.Text = probe.Reachable
            ? string.Join(" · ", new[] { probe.Platform, probe.ApiVersion.Length > 0 ? "API " + probe.ApiVersion : "" }.Where(s => s.Length > 0))
            : probe.Message;
    }

    /// <summary>Parses the server object; an empty or null object means the CLI could not reach the engine.</summary>
    public static ContextProbe ParseProbe(Core.CommandResult result)
    {
        var text = result.StandardOutput.Trim();
        if (text.Length > 0 && text != "null")
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                var s = doc.RootElement;
                if (s.ValueKind == JsonValueKind.Object && CliValues.Str(s, "Version").Length > 0)
                {
                    var platform = CliValues.Str(s, "Os") + "/" + CliValues.Str(s, "Arch");
                    var name = CliValues.Str(s, "Platform", "Name");
                    return new ContextProbe(true, CliValues.Str(s, "Version"), CliValues.Str(s, "ApiVersion"), name.Length > 0 ? name + " · " + platform : platform, "");
                }
            }
            catch (JsonException) { }
        }
        var error = result.StandardError.Trim().Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
        if (result.Cancelled) error = "No answer within the probe time limit.";
        return new ContextProbe(false, "", "", "", error.Length > 0 ? error : $"docker version exited with code {result.ExitCode}.");
    }
}
