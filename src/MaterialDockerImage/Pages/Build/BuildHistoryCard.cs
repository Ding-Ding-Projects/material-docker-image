using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

public sealed record BuildHistoryRow(string Source, string Status, string Name, string Reference, double? Seconds, string Steps, DateTimeOffset? Started, string Digest);

/// <summary>
/// Recent builds: those started from this application in this session, plus the builder's own records from
/// <c>docker buildx history ls --format json</c> when the installed Buildx supports it.
/// </summary>
public sealed class BuildHistoryCard : DockPanel
{
    private static AppHost Host => AppHost.Current;
    private readonly DataTable<BuildHistoryRow> table = new("Recent builds", "Search builds");
    private readonly TextBlock builderState = Ui.Caption("");
    private IReadOnlyList<BuildHistoryRow> builderRows = [];
    private bool loading;

    public BuildHistoryCard()
    {
        table.ChipColumn("Status", r => Loc.T(r.Status), r => r.Status switch
        {
            "Completed" => Tone.Success,
            "Failed" or "Error" => Tone.Error,
            "Canceled" => Tone.Warning,
            "Running" => Tone.Primary,
            _ => Tone.Neutral
        }, 110)
            .Column("Build", r => r.Name, 240)
            .Column("Duration", r => r.Seconds is { } s ? BuildTimelineView.FormatSeconds(s) : "—", 90, true)
            .Column("Cached / steps", r => r.Steps, 110, true)
            .Column("Started", r => r.Started?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "—", 150)
            .ChipColumn("Source", r => Loc.T(r.Source), r => r.Source == "This app" ? Tone.Primary : Tone.Tertiary, 100)
            .Column("Reference", r => r.Reference, 260, true)
            .Filter("This app", r => r.Source == "This app")
            .Filter("Failed", r => r.Status is "Failed" or "Error")
            .RowAction("Copy reference", Icons.ContentCopy, r => { ActionSounds.Copy(r.Reference.Length > 0 ? r.Reference : r.Name); Host.Notify.Info("Copied."); return Task.CompletedTask; })
            .RowAction("Copy digest", Icons.Fingerprint, r => { if (r.Digest.Length == 0) Host.Notify.Info("This build reported no digest."); else { ActionSounds.Copy(r.Digest); Host.Notify.Info("Copied the digest."); } return Task.CompletedTask; });
        table.Key = r => r.Source + "|" + r.Reference + "|" + r.Started?.ToUnixTimeMilliseconds();
        table.EmptyTitle = Loc.T("No builds yet");
        table.EmptyMessage = Loc.T("Builds you start here appear immediately. Builder records appear when Buildx supports history.");
        var refresh = Ui.AsyncIconButton(Icons.Refresh, "Refresh builder history", RefreshAsync);
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        head.Children.Add(refresh.Dock(System.Windows.Controls.Dock.Right));
        head.Children.Add(Ui.VStack(2, Ui.Text("Recent builds", 16, FontWeights.SemiBold), builderState));
        Children.Add(head.Dock(System.Windows.Controls.Dock.Top));
        Children.Add(table);
        BuildSessionHistory.Changed += () => Dispatcher.BeginInvoke(Render);
        Render();
    }

    public async Task RefreshAsync()
    {
        if (loading) return;
        loading = true;
        builderState.Text = Loc.T("Reading builder history…");
        try
        {
            var result = await Host.Read(["buildx", "history", "ls", "--format", "json"]);
            if (!result.Success)
            {
                var error = result.StandardError.Trim();
                builderRows = [];
                builderState.Text = BuildKitHistoryRecord.IsUnsupported(error)
                    ? Loc.T("Builder history is not supported by this Buildx version. Builds from this session are still listed.")
                    : Loc.T("Builder history could not be read") + ": " + (error.Length > 0 ? error.Split('\n')[0] : "exit " + result.ExitCode);
            }
            else
            {
                builderRows = BuildKitHistoryRecord.ParseList(result.StandardOutput)
                    .Select(r => new BuildHistoryRow("Builder", r.Status.Length > 0 ? r.Status : "Unknown", r.Name.Length > 0 ? r.Name : r.Ref, r.Ref, r.Seconds,
                        r.TotalSteps is { } total ? $"{r.CachedSteps ?? 0} / {total}" : "—", r.Created, ""))
                    .ToList();
                builderState.Text = $"{builderRows.Count} {Loc.T("builder records")} · {Loc.T("context")} {Host.Context ?? Loc.T("current")}";
            }
        }
        catch (System.Text.Json.JsonException e) { builderRows = []; builderState.Text = Loc.T("Builder history returned JSON this application cannot read") + ": " + e.Message; }
        finally { loading = false; }
        Render();
    }

    private void Render()
    {
        var session = BuildSessionHistory.Entries.Select(e => new BuildHistoryRow("This app", e.Status, e.Label + (e.Reference.Length > 0 ? " · " + e.Reference : ""), e.Reference, e.Seconds,
            e.Instructions > 0 ? $"{e.Cached} / {e.Instructions}" : "—", e.Started, e.Digest));
        table.SetItems(session.Concat(builderRows).OrderByDescending(r => r.Started ?? DateTimeOffset.MinValue));
    }
}
