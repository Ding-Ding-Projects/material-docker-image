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
    private readonly Border reach;
    private readonly TextBlock reachDetail = Ui.Caption("");
    public ContextEntry Entry { get; }

    public ContextCardView(ContextEntry entry, bool usedByWindow, ContextActions actions)
    {
        Entry = entry;
        Width = 372;
        Padding = new Thickness(18);
        CornerRadius = new CornerRadius(20);
        SetResourceReference(BackgroundProperty, usedByWindow ? "SecondaryContainer" : "SurfaceLow");
        var icon = Ui.Icon(ContextEndpoint.IsLocal(entry.Endpoint) ? Icons.DeveloperBoard : Icons.Cloud, 22, true, usedByWindow ? "OnSecondaryContainer" : "Primary");
        var name = Ui.Mono(entry.Name, 16, "OnSurface");
        name.FontWeight = FontWeights.SemiBold;
        var titleRow = new DockPanel();
        titleRow.Children.Add(icon.Dock(Dock.Left).Margin(0, 0, 10, 0));
        titleRow.Children.Add(Ui.Badge(ContextEndpoint.Transport(entry.Endpoint), Tone.Neutral).Dock(Dock.Right));
        titleRow.Children.Add(name);
        var chips = Ui.Wrap(6,
            entry.CliCurrent ? Ui.Badge(Loc.T("Current in CLI"), Tone.Tertiary) : null,
            usedByWindow ? Ui.Badge(Loc.T("Used by this window"), Tone.Primary) : null,
            entry.Error.Length > 0 ? Ui.Badge(Loc.T("Configuration error"), Tone.Error) : null);
        reach = Ui.Badge(Loc.T("Checking…"), Tone.Neutral);
        var description = Ui.Text(entry.Description.Length > 0 ? entry.Description : Loc.T("No description"), 13, null, "OnSurfaceVariant", true, false);
        var endpoint = Ui.Mono(entry.Endpoint.Length > 0 ? entry.Endpoint : "(no docker endpoint)", 12, "OnSurface", true);
        var endpointCard = Ui.Card(Ui.VStack(2, Ui.Overline("Docker endpoint"), endpoint), 10, usedByWindow ? "SurfaceLow" : "SurfaceContainer", 12);
        var use = Ui.Button(usedByWindow ? "In use here" : "Use in this window", usedByWindow ? Icons.Check : Icons.SwapHoriz, usedByWindow ? ButtonKind.Text : ButtonKind.Filled, () => actions.UseInWindow(entry));
        use.IsEnabled = !usedByWindow;
        use.ToolTip = Loc.T("Switches only this window. The docker CLI's current context is not changed.");
        var inspect = Ui.AsyncButton("Inspect", Icons.DataObject, ButtonKind.Text, () => actions.InspectAsync(entry));
        Button? more = null;
        more = Ui.IconButton(Icons.MoreVert, Loc.T("More actions for") + " " + entry.Name, () => actions.ShowMenu(more!, entry));
        var buttons = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        buttons.Children.Add(more.Dock(Dock.Right));
        buttons.Children.Add(Ui.HStack(4, use, inspect));
        var body = Ui.VStack(10, titleRow, chips.Children.Count > 0 ? chips : null, description, endpointCard,
            Ui.VStack(4, Ui.HStack(8, Ui.Overline("Engine"), reach), reachDetail),
            entry.Error.Length > 0 ? Ui.Text(entry.Error, 12, null, "Error", true, false) : null,
            buttons);
        Child = body;
        System.Windows.Automation.AutomationProperties.SetName(this, Loc.T("Context") + " " + entry.Name);
        ContextMenu = actions.BuildMenu(entry);
    }

    public void SetProbe(ContextProbe probe)
    {
        var (bg, fg) = Ui.ToneKeys(probe.Reachable ? Tone.Success : Tone.Error);
        reach.SetResourceReference(BackgroundProperty, bg);
        if (reach.Child is TextBlock t)
        {
            t.Text = probe.Reachable ? Loc.T("Reachable") + (probe.Version.Length > 0 ? " · " + probe.Version : "") : Loc.T("Unreachable");
            t.SetResourceReference(TextBlock.ForegroundProperty, fg);
        }
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
