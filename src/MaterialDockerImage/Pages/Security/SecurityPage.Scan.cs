using System.Text;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.UI;
using MaterialDockerImage.Services;

namespace MaterialDockerImage.Pages;

/// <summary>Sequential, cancellable Docker Scout scanning with per-image progress, and findings export.</summary>
public sealed partial class SecurityPage
{
    private readonly Button scanAll;
    private readonly Button scanSelected;
    private readonly Border progressCard = new() { CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 12), Visibility = Visibility.Collapsed };
    private readonly TextBlock progressTitle = Ui.Text("", 14, FontWeights.SemiBold, "OnSecondaryContainer", true, false);
    private readonly TextBlock progressDetail = Ui.Text("", 12.5, null, "OnSecondaryContainer", true, false);
    private readonly ProgressBar progressBar = new() { Height = 6, Minimum = 0, Margin = new Thickness(0, 8, 0, 0) };
    private CancellationTokenSource? scanCancellation;

    private void BuildProgressCard()
    {
        progressCard.SetResourceReference(Border.BackgroundProperty, "SecondaryContainer");
        var cancel = Ui.Button("Cancel scan", Icons.Cancel, ButtonKind.Outlined, CancelScan);
        var text = Ui.VStack(2, progressTitle, progressDetail);
        progressCard.Child = Ui.VStack(0, Ui.Columns(12, [0, 1, 0], Ui.Icon(Icons.ManageSearch, 22, true, "OnSecondaryContainer"), text, cancel), progressBar);
        System.Windows.Automation.AutomationProperties.SetName(progressCard, Services.Loc.T("Scan progress"));
    }

    private void CancelScan()
    {
        if (scanCancellation == null) return;
        scanCancellation.Cancel();
        progressDetail.Text = Services.Loc.T("Canceling after the current command stops…");
    }

    private async Task EnsureLoadedAsync()
    {
        if (!loaded) await RefreshAsync();
    }

    private async Task ScanAllAsync()
    {
        await EnsureLoadedAsync();
        if (baseRows.Count == 0) { Host.Notify.Info("There are no tagged local images to scan. Pull or build an image first."); return; }
        await ScanAsync(baseRows);
    }

    private async Task ScanSelectedAsync()
    {
        await EnsureLoadedAsync();
        IReadOnlyList<SecurityImageRow> selected = table.Selected.Count > 0 ? table.Selected : table.Focused is { } f ? [f] : [];
        if (selected.Count == 0) { Host.Notify.Info("Select one or more images in the Security list first."); return; }
        await ScanAsync(selected);
    }

    /// <summary>Analyzes each image in turn with docker scout cves. Stops early when Scout is missing or sign-in is required.</summary>
    internal async Task ScanAsync(IReadOnlyList<SecurityImageRow> targets)
    {
        if (scanCancellation != null) { Host.Notify.Info("A scan is already running. Cancel it or wait for it to finish."); return; }
        if (!EnsureCurrentForAction()) return;
        var list = targets.GroupBy(t => t.Id, StringComparer.Ordinal).Select(g => g.First()).ToList();
        if (list.Count == 0) return;
        var context = SnapshotContext;
        var availability = await ScoutRunner.CheckAsync(context, true);
        ShowAvailability(availability);
        if (!availability.Available) { Host.Notify.Warning("Docker Scout is not available. The banner shows the exact output of docker scout version."); return; }

        scanCancellation = new CancellationTokenSource();
        var token = scanCancellation.Token;
        scanAll.IsEnabled = scanSelected.IsEnabled = false;
        progressCard.Visibility = Visibility.Visible;
        progressBar.Maximum = list.Count;
        progressBar.Value = 0;
        int succeeded = 0, failed = 0;
        var cancelled = false;
        string? stopReason = null;
        var started = DateTime.Now;
        try
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (token.IsCancellationRequested) { cancelled = true; break; }
                if (!IsCurrent) { stopReason = PageKit.StaleMessage(context); break; }
                var target = list[i];
                progressTitle.Text = $"Analyzing {i + 1} of {list.Count}: {target.Reference}";
                progressDetail.Text = $"{succeeded} analyzed · {failed} failed · started {started:HH:mm:ss} · docker scout cves --format sarif";
                var result = await ScoutRunner.ScanBackgroundAsync(target.Reference, target.Id, context, token);
                if (result.State == ScoutState.Cancelled || token.IsCancellationRequested) { cancelled = true; break; }
                ScoutSessionCache.Put(result);
                progressBar.Value = i + 1;
                if (result.Succeeded) succeeded++;
                else
                {
                    failed++;
                    if (result.State is ScoutState.NotSignedIn or ScoutState.PluginMissing)
                    {
                        stopReason = ScoutStatus.Explain(result.State);
                        banner.Content = ScoutSeverityUi.StateCard(result.State, result.Output, "docker scout cves --format sarif --output <temporary file> " + result.Reference,
                            result.State == ScoutState.NotSignedIn ? Ui.Button("Open Registry sign-in", Icons.Login, ButtonKind.Text, () => Host.Navigate("Registry")) : ScoutSeverityUi.InstallGuideButton());
                        break;
                    }
                }
            }
        }
        finally
        {
            scanCancellation.Dispose();
            scanCancellation = null;
            progressCard.Visibility = Visibility.Collapsed;
            scanAll.IsEnabled = scanSelected.IsEnabled = true;
        }

        var summary = $"{succeeded} of {list.Count} analyzed" + (failed > 0 ? $", {failed} failed" : "");
        Host.History.Record("security scan", $"Docker Scout scan · {summary}{(cancelled ? " · canceled" : "")} · context {context ?? "default"}");
        if (cancelled) Host.Notify.Warning($"Scan canceled. {summary}. Results already finished are kept for this session.");
        else if (stopReason != null) Host.Notify.Warning($"Scan stopped: {stopReason} {summary}.");
        else if (failed > 0) Host.Notify.Warning($"Scan finished: {summary}. Select a failed image to see the exact output.");
        else Host.Notify.Success($"Scan finished: {summary}.");
    }

    /// <summary>Exports every finding of the images analyzed on this engine context in this session, as JSON or CSV.</summary>
    private void ExportFindings()
    {
        var context = SnapshotContext;
        var results = ScoutSessionCache.ForContext(context).Where(r => r.Succeeded).ToList();
        if (results.Count == 0) { Host.Notify.Info("No images have been analyzed in this session yet. Scan first, then export."); return; }
        var rows = results.SelectMany(r => r.Findings.Select(f => new ScoutExportRow(r.Reference, r.ImageId, r.AnalysedUtc, f))).ToList();
        var path = PageKit.PickSavePath($"security-findings-{DateTime.Now:yyyyMMdd-HHmm}.json", "JSON|*.json|CSV|*.csv");
        if (path == null) return;
        var csv = Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase);
        try
        {
            File.WriteAllText(path, csv ? ScoutReport.ToCsv(rows) : ScoutReport.ToJson(rows), new UTF8Encoding(false));
            Host.Notify.Success($"Exported {rows.Count} findings from {results.Count} {(results.Count == 1 ? "image" : "images")} to {path}.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Host.Notify.Error(Loc.T("The findings could not be saved:") + " " + e.Message); }
    }
}
