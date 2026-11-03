using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Engine facts from <c>docker info</c> and client/server versions from <c>docker version</c>.</summary>
public sealed class EngineInfoCard : Border
{
    private readonly StackPanel info = new();
    private readonly StackPanel versions = new();
    private readonly SearchFilter search = new("Search engine facts", 260);
    private readonly List<(FrameworkElement Row, string Text)> rows = new();

    public EngineInfoCard()
    {
        CornerRadius = new CornerRadius(16);
        Padding = new Thickness(16);
        SetResourceReference(BackgroundProperty, "SurfaceLow");
        search.Width = double.NaN;
        search.Changed += Filter;
        Child = Ui.VStack(12,
            Ui.Text("Engine", 18, FontWeights.SemiBold),
            search,
            info,
            Ui.Divider(),
            Ui.Text("Engine and client versions", 16, FontWeights.SemiBold),
            versions);
        ShowMessage(info, "Loading engine information…");
        ShowMessage(versions, "Loading versions…");
    }

    private static void ShowMessage(Panel panel, string text) { panel.Children.Clear(); panel.Children.Add(Ui.Caption(text)); }

    private FrameworkElement Row(string label, string value, bool mono = true)
    {
        var row = Ui.Columns(12, [-120, 1], Ui.Caption(label), mono ? Ui.Mono(value.Length == 0 ? "—" : value, 13, "OnSurface", true) : Ui.Text(value, 13, null, "OnSurface", true, false));
        rows.Add((row, label + " " + value));
        return row;
    }

    private void Filter()
    {
        foreach (var (row, text) in rows) row.Visibility = search.Matches(text) ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetInfoError(string message) { rows.RemoveAll(r => info.Children.Contains(r.Row)); ShowMessage(info, "docker info failed: " + message); }
    public void SetVersionError(string message) { rows.RemoveAll(r => versions.Children.Contains(r.Row)); ShowMessage(versions, "docker version failed: " + message); }

    public void SetInfo(JsonElement x)
    {
        rows.RemoveAll(r => info.Children.Contains(r.Row));
        info.Children.Clear();
        var memory = CliValues.Long(x, "MemTotal");
        void Add(string label, string value) => Ui.Add(info, Row(label, value), 6);
        Add("Server version", CliValues.Str(x, "ServerVersion"));
        Add("Name", CliValues.Str(x, "Name"));
        Add("Operating system", CliValues.Str(x, "OperatingSystem"));
        Add("OS type / arch", $"{CliValues.Str(x, "OSType")} / {CliValues.Str(x, "Architecture")}");
        Add("Kernel", CliValues.Str(x, "KernelVersion"));
        Add("CPUs", CliValues.Str(x, "NCPU"));
        Add("Memory", memory > 0 ? Ui.FormatBytes(memory) : "");
        Add("Storage driver", CliValues.Str(x, "Driver"));
        Add("Root directory", CliValues.Str(x, "DockerRootDir"));
        Add("Containers", $"{CliValues.Long(x, "Containers")} total · {CliValues.Long(x, "ContainersRunning")} running · {CliValues.Long(x, "ContainersPaused")} paused · {CliValues.Long(x, "ContainersStopped")} stopped");
        Add("Images", CliValues.Str(x, "Images"));
        Add("Cgroup", $"{CliValues.Str(x, "CgroupDriver")} {CliValues.Str(x, "CgroupVersion")}".Trim());
        var serverErrors = CliValues.Arr(x, "ServerErrors").Select(e => e.ToString()).ToList();
        var warnings = CliValues.Arr(x, "Warnings").Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.ToString()).Where(w => w.Length > 0).ToList();
        if (serverErrors.Count > 0)
            Ui.Add(info, Ui.Card(Ui.VStack(4, new UIElement[] { Ui.Text("Server errors", 13, FontWeights.SemiBold, "OnErrorContainer") }.Concat(serverErrors.Select(e => (UIElement)Ui.Text(e, 13, null, "OnErrorContainer", true, false))).ToArray()), 12, "ErrorContainer", 12), 10);
        var warningBody = warnings.Count == 0
            ? (UIElement)Ui.Caption("The engine reported no warnings.")
            : Ui.Card(Ui.VStack(4, new UIElement[] { Ui.Text($"{warnings.Count} engine warning{(warnings.Count == 1 ? "" : "s")}", 13, FontWeights.SemiBold, "OnWarningContainer") }.Concat(warnings.Select(w => (UIElement)Ui.Text(w, 13, null, "OnWarningContainer", true, false))).ToArray()), 12, "WarningContainer", 12);
        Ui.Add(info, warningBody, 10);
        Filter();
    }

    public void SetVersion(JsonElement x)
    {
        rows.RemoveAll(r => versions.Children.Contains(r.Row));
        versions.Children.Clear();
        void Add(string label, string value) => Ui.Add(versions, Row(label, value), 6);
        Add("Client", $"{CliValues.Str(x, "Client", "Version")} · API {CliValues.Str(x, "Client", "ApiVersion")} · {CliValues.Str(x, "Client", "Os")}/{CliValues.Str(x, "Client", "Arch")}");
        if (CliValues.Get(x, "Server") is { ValueKind: JsonValueKind.Object })
        {
            Add("Server", $"{CliValues.Str(x, "Server", "Version")} · API {CliValues.Str(x, "Server", "ApiVersion")} (minimum {CliValues.Str(x, "Server", "MinAPIVersion")}) · {CliValues.Str(x, "Server", "Os")}/{CliValues.Str(x, "Server", "Arch")}");
            foreach (var component in CliValues.Arr(x, "Server", "Components"))
            {
                var name = CliValues.Str(component, "Name");
                if (name.Length == 0 || name == "Engine") continue;
                Add(name, CliValues.Str(component, "Version"));
            }
            if (CliValues.Str(x, "Server", "Platform", "Name") is { Length: > 0 } platform) Add("Platform", platform);
        }
        else Add("Server", "Not reported (the engine did not answer the version request)");
        Filter();
    }
}
