using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Release history generated from git log at build time (see build/make-changelog.ps1): grouped by version, searchable with the
/// regex builder, filterable by typed ISO dates, exportable as Markdown. It lists only what the bundled file records.
/// </summary>
public sealed class ChangelogView
{
    private readonly ChangelogData? data;
    private readonly string problem = "";
    private readonly SearchFilter search = new("Search the changelog", 360);
    private readonly TextBox from = Ui.Input("", "Changelog from date");
    private readonly TextBox to = Ui.Input("", "Changelog to date");
    private readonly TextBlock rangeProblem = Ui.Text("", 12, null, "Error");
    private readonly TextBlock summary = Ui.Caption("");
    private readonly StackPanel list = new();
    private IReadOnlyList<ChangelogGroup> shown = [];

    public FrameworkElement Root { get; }

    public ChangelogView(FrameworkElement header)
    {
        try { data = ChangelogModel.Parse(ReadBundled()); }
        catch (Exception e) when (e is InvalidDataException or FileNotFoundException) { problem = e.Message; }

        from.Width = to.Width = 150;
        from.ToolTip = to.ToolTip = Loc.T("YYYY-MM-DD, or a date in your regional format. Leave empty for no limit.");
        search.Changed += Apply;
        from.TextChanged += (_, _) => Apply();
        to.TextChanged += (_, _) => Apply();
        var copy = Ui.Button("Copy as Markdown", Icons.ContentCopy, ButtonKind.Tonal, () => { if (data != null) PageKit.Copy(Markdown(), "the filtered changelog"); });
        var export = Ui.Button("Export Markdown…", Icons.FileExport, ButtonKind.Tonal, ExportMarkdown);
        var controls = Ui.Wrap(10, search, Ui.Field("From date", from, "Only changes committed on or after this day. Typed dates are checked as you type and your text is never discarded.", "2026-11-01"), Ui.Field("To date", to, "Only changes committed on or before this day.", "2026-11-30"), copy, export);
        var note = Ui.Caption(data?.Note ?? "");
        var body = Ui.VStack(14,
            header,
            Ui.Divider(),
            Ui.Title("Release history", 18),
            note,
            problem.Length > 0 ? Ui.Card(Ui.Text(Loc.T(problem), 14, null, "OnErrorContainer"), 14, "ErrorContainer", 12) : null,
            data != null ? controls : null,
            rangeProblem,
            summary,
            list);
        Root = Ui.Scroll(Ui.Card(body, 24, "SurfaceLow", 16).Margin(0, 0, 8, 16));
        if (data == null)
        {
            list.Children.Add(Ui.Caption("This build does not include a changelog, so no history is shown here. Builds made with build.bat include it."));
            return;
        }
        Apply();
    }

    private static string ReadBundled()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("changelog.json")
            ?? throw new FileNotFoundException("The changelog was not bundled with this build.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private string RangeText() => from.Text.Trim().Length == 0 && to.Text.Trim().Length == 0 ? "" : (from.Text.Trim().Length > 0 ? from.Text.Trim() : "start") + " to " + (to.Text.Trim().Length > 0 ? to.Text.Trim() : "today");

    private string Markdown() => ChangelogModel.ToMarkdown(data!, shown, RangeText(), search.Query);

    private void Apply()
    {
        if (data == null) return;
        var message = ChangelogModel.ValidateRange(from.Text, to.Text, out var start, out var end);
        rangeProblem.Text = Loc.T(message);
        // An invalid date leaves the previous bounds out of the query rather than silently hiding everything.
        shown = ChangelogModel.Filter(data, search.Matches, message.Length == 0 ? start : null, message.Length == 0 ? end : null);
        var count = shown.Sum(g => g.Entries.Count);
        summary.Text = Loc.T($"{count} of {data.EntryCount} changes shown in {shown.Count} versions") + (search.Status.Length > 0 ? " · " + search.Status : "");
        list.Children.Clear();
        if (shown.Count == 0) { list.Children.Add(Ui.EmptyState(Icons.FilterList, "No matches", "No change matches the search and date range. Clear them to see the full history.")); return; }
        foreach (var group in shown) list.Children.Add(GroupCard(group));
    }

    private FrameworkElement GroupCard(ChangelogGroup group)
    {
        var title = group.Date.Length > 0 ? group.Version + " · " + group.Date : group.Version;
        var stack = Ui.VStack(8, Ui.Text(title, 16, FontWeights.SemiBold, "OnSurface", true, false));
        foreach (var category in ChangelogModel.ByCategory(group))
        {
            stack.Children.Add(Ui.Overline(category.Key));
            foreach (var entry in category)
            {
                var sha = Ui.Button(ChangelogModel.ShortSha(entry.Sha), null, ButtonKind.Text, () => OpenCommit(entry.Sha), false);
                sha.ToolTip = Loc.T("Open this commit on the forge") + " · " + entry.Sha;
                System.Windows.Automation.AutomationProperties.SetName(sha, Loc.T("Commit") + " " + entry.Sha);
                var row = Ui.Columns(10, [1, 0, 0], Ui.Text(entry.Subject, 14, null, "OnSurface", true, false), Ui.Mono(entry.Date, 12, "OnSurfaceVariant"), sha);
                stack.Children.Add(row);
            }
        }
        return Ui.Card(stack, 16, "SurfaceContainer", 12);
    }

    private async void OpenCommit(string sha)
    {
        var url = ChangelogModel.CommitUrl(data!.Repository, sha);
        try
        {
            if (await AppHost.Current.Overlay.ConfirmAsync("Open in your browser?", Loc.T("This opens") + " " + url + " in your default browser.", "Open browser")) PageParts.OpenExternal(url);
        }
        catch (Exception e) when (e is not OutOfMemoryException) { AppHost.Current.Notify.Error(e.Message); }
    }

    private void ExportMarkdown()
    {
        if (data == null) return;
        var dialog = new SaveFileDialog { FileName = "changelog.md", Filter = "Markdown|*.md" };
        if (dialog.ShowDialog() != true) return;
        File.WriteAllText(dialog.FileName, Markdown(), new UTF8Encoding(false));
        AppHost.Current.Notify.Success($"Exported {shown.Sum(g => g.Entries.Count)} changes as Markdown (UTF-8). The range and search are written at the top of the file.");
    }
}
