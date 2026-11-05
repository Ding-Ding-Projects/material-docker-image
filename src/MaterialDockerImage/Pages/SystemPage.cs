using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Disk usage, engine information and versions, and an itemised cleanup with super confirmation.</summary>
public sealed class SystemPage : PageBase
{
    public override string Key => "System";
    private readonly TextBlock subtitle;
    private readonly WrapPanel usageCards = new();
    private readonly TextBlock usageState = Ui.Caption("");
    private readonly EngineInfoCard engine = new();
    private readonly CleanupPanel cleanup;
    private string serverVersion = "";
    private readonly Grid columns;

    public SystemPage()
    {
        cleanup = new CleanupPanel(() => SnapshotContext, () => IsCurrent, RefreshAsync);
        var header = Ui.Header("System", "Disk usage, cleanup and engine information", out subtitle,
            Ui.AsyncButton("Measure again", Icons.Refresh, ButtonKind.Outlined, RefreshAsync));
        var usageBlock = Ui.VStack(8, usageCards, usageState);
        columns = Ui.Columns(16, [3, 2], cleanup, engine);
        columns.VerticalAlignment = VerticalAlignment.Top;
        var body = Ui.Scroll(Ui.VStack(12, usageBlock, columns).Margin(0, 0, 8, 16));
        Layout(header, body);
        SizeChanged += (_, _) => ResizeCards();
    }

    private void ResizeCards()
    {
        var width = ActualWidth - 24;
        if (width <= 0) return;
        var perRow = width >= 1100 ? 4 : width >= 640 ? 2 : 1;
        foreach (FrameworkElement card in usageCards.Children) card.Width = Math.Max(200, width / perRow - 12);
        // Cleanup and engine stack vertically below 1100 px so neither column gets cramped.
        var stacked = width < 1100;
        if (stacked == (columns.RowDefinitions.Count > 0)) return;
        columns.ColumnDefinitions.Clear(); columns.RowDefinitions.Clear();
        if (stacked)
        {
            columns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            columns.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
            columns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            cleanup.At(0, 0); engine.At(2, 0);
        }
        else
        {
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            cleanup.At(0, 0); engine.At(0, 2);
        }
    }

    public override async Task RefreshAsync()
    {
        var context = SnapshotContext;
        usageState.Text = Loc.T("Measuring…");
        var dfTask = Host.ReadRows(["system", "df", "--format", "{{json .}}"], context);
        var infoTask = Host.Read(["info", "--format", "{{json .}}"], context);
        var versionTask = Host.Read(["version", "--format", "{{json .}}"], context);
        try
        {
            var rows = await dfTask;
            RenderUsage(rows);
        }
        catch (Exception e) when (e is InvalidOperationException or JsonException)
        {
            usageCards.Children.Clear();
            usageState.Text = "docker system df failed: " + e.Message;
            cleanup.SetUsage(new(StringComparer.OrdinalIgnoreCase));
        }
        var info = await infoTask;
        if (TryParse(info, out var infoJson)) { engine.SetInfo(infoJson); serverVersion = CliValues.Str(infoJson, "ServerVersion"); }
        else engine.SetInfoError(Error(info));
        var version = await versionTask;
        // docker version exits non-zero when the server is unreachable but still prints the client half.
        if (TryParse(version, out var versionJson, allowFailure: true)) engine.SetVersion(versionJson);
        else engine.SetVersionError(Error(version));
        subtitle.Text = Loc.Line($"Engine {(serverVersion.Length > 0 ? serverVersion : "unknown")} on {context ?? "the current context"}" + (usageTotal is { } t ? $" · {Ui.FormatBytes(t)} used" : "") + (reclaimTotal is { } r ? $" · {Ui.FormatBytes(r)} reclaimable" : "") + $" · measured {DateTime.Now:HH:mm:ss}");
    }

    private static string Error(CommandResult r) => r.StandardError.Trim().Length > 0 ? r.StandardError.Trim().Split('\n')[0] : $"exit code {r.ExitCode}";

    private static bool TryParse(CommandResult r, out JsonElement json, bool allowFailure = false)
    {
        json = default;
        if ((!r.Success && !allowFailure) || r.StandardOutput.Trim().Length == 0) return false;
        try
        {
            var rows = DockerJson.ParseRows(r.StandardOutput.Trim().Split('\n')[0]);
            if (rows.Count == 0) return false;
            json = rows[0];
            return true;
        }
        catch (JsonException) { return false; }
    }

    private double? usageTotal, reclaimTotal;

    private void RenderUsage(IReadOnlyList<JsonElement> rows)
    {
        usageCards.Children.Clear();
        var values = new Dictionary<string, (string Size, string Reclaimable)>(StringComparer.OrdinalIgnoreCase);
        double total = 0, reclaim = 0; var parsedTotal = false;
        foreach (var row in rows)
        {
            var type = CliValues.Str(row, "Type");
            var size = CliValues.Str(row, "Size");
            var reclaimable = CliValues.Str(row, "Reclaimable");
            var reclaimSize = reclaimable.Split(' ')[0];
            values[type] = (size, reclaimSize);
            if (CliValues.ParseSize(size) is { } s) { total += s; parsedTotal = true; }
            if (CliValues.ParseSize(reclaimSize) is { } rc) reclaim += rc;
            var icon = type switch { "Images" => Icons.Layers, "Containers" => Icons.DeployedCode, "Local Volumes" => Icons.Database, "Build Cache" => Icons.Construction, _ => Icons.Storage };
            var count = CliValues.Str(row, "TotalCount"); var active = CliValues.Str(row, "Active");
            var label = type switch { "Local Volumes" => "Volumes", "Build Cache" => "Build cache", _ => type };
            var unit = type switch { "Images" => "images", "Containers" => "containers", "Local Volumes" => "volumes", _ => "entries" };
            var share = CliValues.ParseSize(size) is { } used && CliValues.ParseSize(reclaimSize) is { } free && used > 0 ? Math.Clamp(free / used, 0, 1) : 0;
            var track = new Border { Height = 6, CornerRadius = new CornerRadius(3) };
            track.SetResourceReference(Border.BackgroundProperty, "SurfaceHighest");
            var fill = new Border { Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
            fill.SetResourceReference(Border.BackgroundProperty, "Warning");
            var bar = new Grid { Height = 6 };
            bar.Children.Add(track); bar.Children.Add(fill);
            bar.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * share;
            var header = Ui.Columns(8, [0, 1, 0], Ui.Icon(icon, 20, false, "Primary"), Ui.Text(label, 14, FontWeights.SemiBold, "OnSurface", false, false), Ui.Text($"{count} {unit}", 12, null, "OnSurfaceVariant", false, false));
            var card = Ui.Card(Ui.VStack(8,
                header,
                Ui.Text(size.Length > 0 ? size : "—", 28, FontWeights.SemiBold, "OnSurface", false, false),
                bar,
                Ui.Text(reclaimable.Length > 0 ? $"{reclaimable} reclaimable · {active} active" : "Reclaimable not reported", 12, null, "OnSurfaceVariant", true, false)), 16, "SurfaceLow");
            card.Margin = new Thickness(0, 0, 12, 12);
            System.Windows.Automation.AutomationProperties.SetName(card, $"{label}: {size}, {count} total, {active} active, reclaimable {reclaimable}");
            usageCards.Children.Add(card);
        }
        usageTotal = parsedTotal ? total : null;
        reclaimTotal = parsedTotal ? reclaim : null;
        usageState.Text = rows.Count == 0 ? Loc.T("The engine returned no disk usage rows.") : Loc.T("Sizes and reclaimable estimates come from docker system df. Shared image layers are counted once.");
        cleanup.SetUsage(values);
        ResizeCards();
    }

    public override IEnumerable<AppCommand> PageCommands() =>
    [
        new AppCommand { Id = "system.measure", Title = "Measure disk usage", Group = "System", Icon = Icons.DataUsage, Keywords = "docker system df", Run = () => { Host.Navigate("System"); _ = SafeRefresh(); } },
        new AppCommand { Id = "system.cleanup", Title = "Clean up unused data…", Group = "System", Icon = Icons.CleaningServices, Keywords = "prune cache images containers", Run = () => Host.Navigate("System") },
    ];
}
