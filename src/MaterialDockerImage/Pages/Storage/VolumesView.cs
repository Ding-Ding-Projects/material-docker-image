using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

public sealed record VolumeRow(string Name, string Driver, string Scope, string Mountpoint, string Size, double? SizeBytes, IReadOnlyList<string> UsedBy, string Labels, string Created = "")
{
    public bool Anonymous => Regex.IsMatch(Name, "^[0-9a-f]{64}$");
    public bool InUse => UsedBy.Count > 0;
    public string UsedByText => UsedBy.Count == 0 ? "Not used" : string.Join(", ", UsedBy);
}

/// <summary>Volumes list with usage, sizes when the engine reports them, create, inspect, remove and prune.</summary>
public sealed class VolumesView : Grid
{
    private static AppHost Host => AppHost.Current;
    private readonly DataTable<VolumeRow> table = new("Volumes", "Search volumes");
    private readonly Func<string?> context;
    private readonly Func<bool> isCurrent;
    private readonly Action<string> setSummary;
    public Func<Task>? Reload { get; set; }
    /// <summary>Raised after each load with the volume count, total reported size (null when unknown) and unused count.</summary>
    public event Action<int, double?, int>? StatsChanged;

    public VolumesView(Func<string?> boundContext, Func<bool> current, Action<string> summary)
    {
        context = boundContext; isCurrent = current; setSummary = summary;
        table.Key = v => v.Name;
        table.EmptyTitle = Loc.T("No volumes");
        table.EmptyMessage = Loc.T("This engine has no volumes. Create one to keep data outside a container's writable layer.");
        table.Column("Name", v => v.Name, 300)
            .Column("Driver", v => v.Driver, 90)
            .Column("Size", v => v.Size, 100)
            .ChipColumn("Used by", v => v.InUse ? v.UsedByText : "Not used", v => v.InUse ? Tone.Success : Tone.Neutral, 220)
            .Column("Created", v => v.Created, 130)
            .Column("Labels", v => v.Labels, 200, true)
            .Filter("In use", v => v.InUse)
            .Filter("Unused", v => !v.InUse)
            .Filter("Anonymous", v => v.Anonymous)
            .RowAction("Inspect", Icons.DataObject, InspectAsync, "Enter")
            .RowAction("Back up…", Icons.Backup, v => Stale() ? Task.CompletedTask : VolumeBackupDialog.ShowAsync(v, context, isCurrent))
            .RowAction("Restore into…", Icons.Restore, v => { if (!Stale()) VolumeRestoreDialog.Show(v.Name, table.Items.Select(i => i.Name).ToList(), context, isCurrent, ReloadOrNothing); return Task.CompletedTask; })
            .RowAction("Clone…", Icons.FilterNone, v => VolumeRestoreDialog.CloneAsync(v, table.Items.Select(i => i.Name).ToList(), context, isCurrent, ReloadOrNothing))
            .RowAction("Copy name", Icons.ContentCopy, v => { ActionSounds.Copy(v.Name); return Task.CompletedTask; })
            .RowAction("Remove…", Icons.Delete, RemoveAsync, "Del", true)
            .BulkAction("Remove selected…", Icons.Delete, BulkRemoveAsync, true);
        table.Activated += v => _ = Guarded(() => InspectAsync(v));
        var create = Ui.Button("Create volume", Icons.Add, ButtonKind.Filled, () => VolumeCreateForm.Show(context, isCurrent, () => Reload?.Invoke() ?? Task.CompletedTask));
        var prune = Ui.AsyncButton("Prune…", Icons.CleaningServices, ButtonKind.Outlined, PruneAsync);
        var backup = Ui.AsyncButton("Back up…", Icons.Backup, ButtonKind.Tonal, () => table.Focused is { } v ? (Stale() ? Task.CompletedTask : VolumeBackupDialog.ShowAsync(v, context, isCurrent)) : SelectFirst());
        var restore = Ui.Button("Restore from archive…", Icons.Restore, ButtonKind.Tonal, () => { if (!Stale()) VolumeRestoreDialog.Show(table.Focused?.Name, table.Items.Select(i => i.Name).ToList(), context, isCurrent, ReloadOrNothing); });
        var toolbar = Ui.Wrap(8, create, backup, restore, prune);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Children.Add(toolbar);
        Children.Add(table.At(1));
    }

    private Task ReloadOrNothing() => Reload?.Invoke() ?? Task.CompletedTask;
    private static Task SelectFirst() { Host.Notify.Info("Select a volume to back up first."); return Task.CompletedTask; }

    private static async Task Guarded(Func<Task> action)
    {
        try { await action(); } catch (Exception e) when (e is not OutOfMemoryException) { Host.Notify.Error(e.Message); }
    }

    private bool Stale()
    {
        if (isCurrent()) return false;
        Host.Notify.Error("This list belongs to an earlier engine context. Refresh before acting.");
        return true;
    }

    public async Task LoadAsync()
    {
        var ctx = context();
        var rows = await Host.ReadRows(["volume", "ls", "--format", "{{json .}}"], ctx);
        // Usage: which containers mount which volume (one inspect call for all containers).
        var usedBy = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        try
        {
            var ids = (await Host.ReadRows(["container", "ls", "--all", "--format", "{{json .}}"], ctx)).Select(r => CliValues.Str(r, "ID")).Where(i => i.Length > 0).ToList();
            if (ids.Count > 0)
            {
                var inspect = await Host.Read(["container", "inspect", .. ids], ctx);
                if (inspect.Success)
                {
                    using var doc = JsonDocument.Parse(inspect.StandardOutput);
                    foreach (var c in doc.RootElement.EnumerateArray())
                    {
                        var name = CliValues.Str(c, "Name").TrimStart('/');
                        foreach (var m in CliValues.Arr(c, "Mounts"))
                        {
                            if (CliValues.Str(m, "Type") != "volume") continue;
                            var v = CliValues.Str(m, "Name");
                            if (!usedBy.TryGetValue(v, out var list)) usedBy[v] = list = new();
                            if (!list.Contains(name)) list.Add(name);
                        }
                    }
                }
            }
        }
        catch (Exception e) when (e is InvalidOperationException or JsonException) { Host.Notify.Warning(Loc.T("Volume usage could not be read:") + " " + e.Message); }
        // Sizes: docker system df -v reports per-volume sizes on engines that support it.
        var sizes = new Dictionary<string, string>(StringComparer.Ordinal);
        var df = await Host.Read(["system", "df", "-v", "--format", "{{json .}}"], ctx);
        if (df.Success)
        {
            try
            {
                foreach (var row in Core.DockerJson.ParseRows(df.StandardOutput))
                    foreach (var v in CliValues.Arr(row, "Volumes"))
                        sizes[CliValues.Str(v, "Name")] = CliValues.Str(v, "Size");
            }
            catch (JsonException) { }
        }
        // Creation times come from one inspect call for all volumes; a failure leaves the column empty.
        var created = new Dictionary<string, string>(StringComparer.Ordinal);
        var names = rows.Select(r => CliValues.Str(r, "Name")).Where(n => n.Length > 0).ToList();
        if (names.Count > 0)
        {
            var inspected = await Host.Read(["volume", "inspect", .. names], ctx);
            if (inspected.Success)
            {
                try
                {
                    using var doc = JsonDocument.Parse(inspected.StandardOutput);
                    foreach (var v in doc.RootElement.EnumerateArray())
                        if (DateTimeOffset.TryParse(CliValues.Str(v, "CreatedAt"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var at))
                            created[CliValues.Str(v, "Name")] = AgoText(at);
                }
                catch (JsonException) { }
            }
        }
        var items = rows.Select(r =>
        {
            var name = CliValues.Str(r, "Name");
            var size = sizes.TryGetValue(name, out var s) && s.Length > 0 ? s : "N/A";
            return new VolumeRow(name, CliValues.Str(r, "Driver"), CliValues.Str(r, "Scope"), CliValues.Str(r, "Mountpoint"), size, CliValues.ParseSize(size),
                usedBy.TryGetValue(name, out var u) ? u : [], CliValues.Str(r, "Labels"), created.TryGetValue(name, out var c) ? c : "");
        }).ToList();
        table.SetItems(items);
        var total = items.Where(i => i.SizeBytes.HasValue).Sum(i => i.SizeBytes!.Value);
        var unused = items.Count(i => !i.InUse);
        StatsChanged?.Invoke(items.Count, sizes.Count > 0 ? total : null, unused);
        setSummary(Loc.Line($"{items.Count} volume{(items.Count == 1 ? "" : "s")}" + (sizes.Count > 0 ? $" · {Ui.FormatBytes(total)}" : " · sizes not reported by this engine") + $" · {unused} not used by any container"));
    }

    private static string AgoText(DateTimeOffset at)
    {
        var span = DateTimeOffset.Now - at;
        string Plural(double n, string unit) => $"{(int)n} {unit}{((int)n == 1 ? "" : "s")} ago";
        return span.TotalMinutes < 1 ? Loc.T("just now")
            : span.TotalHours < 1 ? Plural(span.TotalMinutes, "minute")
            : span.TotalDays < 1 ? Plural(span.TotalHours, "hour")
            : span.TotalDays < 7 ? Plural(span.TotalDays, "day")
            : span.TotalDays < 30 ? Plural(span.TotalDays / 7, "week")
            : span.TotalDays < 365 ? Plural(span.TotalDays / 30, "month")
            : Plural(span.TotalDays / 365, "year");
    }

    private async Task InspectAsync(VolumeRow v)
    {
        var r = await Host.Read(["volume", "inspect", v.Name], context());
        if (!r.Success) { Host.Notify.Error($"Inspect {v.Name} failed: {r.StandardError.Trim()}"); return; }
        ResourceTextViewer.Show("Volume " + v.Name, r.StandardOutput, $"docker volume inspect · used by: {v.UsedByText}");
    }

    private async Task RemoveAsync(VolumeRow v)
    {
        if (Stale()) return;
        var warning = v.InUse ? $" It is used by {v.UsedByText}; the engine refuses to remove a volume a container still references." : "";
        if (!await Host.Overlay.SuperConfirmAsync("Remove volume", "Removing a volume deletes the data stored in it. This cannot be undone." + warning, [v.Name + (v.Size != "N/A" ? $" ({v.Size})" : "")])) return;
        if (Stale()) return;
        await Host.Execute("Remove volume " + v.Name, ["volume", "rm", v.Name], boundContext: context());
        if (Reload != null) await Reload();
    }

    private async Task BulkRemoveAsync(IReadOnlyList<VolumeRow> items)
    {
        if (Stale() || items.Count == 0) return;
        if (!table.Search.IsValid) { Host.Notify.Error("The search pattern is invalid. Fix it before acting on a selection."); return; }
        if (!await Host.Overlay.SuperConfirmAsync($"Remove {items.Count} volumes", "Every listed volume and the data stored in it is deleted. Volumes still used by a container are refused by the engine.", items.Select(v => v.Name + (v.InUse ? $" (used by {v.UsedByText})" : "")).ToList())) return;
        if (Stale()) return;
        await Host.Execute($"Remove {items.Count} volumes", ["volume", "rm", .. items.Select(v => v.Name)], boundContext: context());
        if (Reload != null) await Reload();
    }

    private async Task PruneAsync()
    {
        if (Stale()) return;
        var all = Ui.Check("All unused named volumes too (--all)", false);
        var choice = new TaskCompletionSource<bool?>();
        Action? close = null;
        var body = Ui.VStack(14,
            Ui.Title("Prune unused volumes", 20),
            Ui.Caption("By default only anonymous volumes that no container uses are removed. Turn on the option below to also remove every unused named volume."),
            Ui.Field("Scope", all, "docker volume prune -f removes unused anonymous volumes. Adding --all also removes unused named volumes, which often hold databases.", "docker volume prune -f --all"),
            Ui.Columns(8, [1, 0, 0], new Border(), Ui.Button("Cancel", null, ButtonKind.Text, () => { choice.TrySetResult(null); close!(); }), Ui.Button("Review…", null, ButtonKind.Filled, () => { choice.TrySetResult(all.IsChecked == true); close!(); })));
        close = Host.Overlay.Show(body, 520, () => choice.TrySetResult(null));
        if (await choice.Task is not { } includeNamed) return;
        var candidates = table.Items.Where(v => !v.InUse && (includeNamed || v.Anonymous)).Select(v => v.Name + (v.Anonymous ? " (anonymous)" : " (named)") + (v.Size != "N/A" ? $" {v.Size}" : "")).ToList();
        if (candidates.Count == 0) candidates.Add("No unused volumes in the last refresh. The engine decides the final set when the prune runs.");
        if (!await Host.Overlay.SuperConfirmAsync(includeNamed ? "Prune all unused volumes" : "Prune unused anonymous volumes", "The data in every removed volume is deleted. The list is from the last refresh; the engine decides the final set.", candidates)) return;
        if (Stale()) return;
        var result = await Host.Execute("Prune volumes", includeNamed ? ["volume", "prune", "-f", "--all"] : ["volume", "prune", "-f"], boundContext: context());
        if (result?.Success == true && CliValues.ReclaimedSpace(result.StandardOutput) is { } reclaimed) Host.Notify.Info("Volume prune reclaimed " + reclaimed + ".");
        if (Reload != null) await Reload();
    }
}
