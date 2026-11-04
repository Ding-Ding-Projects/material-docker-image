using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

public sealed record HubResult(string Name, string Description, int Stars, bool Official, bool Automated)
{
    public string StarsText => Stars >= 1000 ? (Stars / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "K" : Stars.ToString(CultureInfo.InvariantCulture);
    public static HubResult From(JsonElement row)
    {
        int.TryParse(DockerJson.Text(row, "StarCount"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var stars);
        static bool Flag(string v) => v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "[OK]";
        return new(DockerJson.Text(row, "Name"), DockerJson.Text(row, "Description"), stars, Flag(DockerJson.Text(row, "IsOfficial")), Flag(DockerJson.Text(row, "IsAutomated")));
    }
}

/// <summary>Docker Hub search through docker search, shown in a DataTable with a Pull action that asks for the tag.</summary>
internal sealed class HubSearch
{
    private static AppHost Host => AppHost.Current;
    private readonly Func<bool> isCurrent;
    private readonly Func<string?> snapshot;
    private readonly TextBox term = Ui.Input("", "Search Docker Hub");
    private readonly CheckBox officialOnly;
    private readonly TextBlock state = Ui.Caption("docker search asks the engine to query Docker Hub. Results are not cached.");
    public DataTable<HubResult> Table { get; }
    public Border Card { get; }

    public HubSearch(Func<bool> isCurrent, Func<string?> snapshot)
    {
        this.isCurrent = isCurrent; this.snapshot = snapshot;
        officialOnly = Ui.Switch("Official images only", false, _ => { });
        Table = new DataTable<HubResult>("Docker Hub results", "Filter these results")
        {
            EmptyTitle = Loc.T("No search yet"),
            EmptyMessage = Loc.T("Type a term and press Search. Results come from docker search; nothing is shown until the engine answers."),
            Key = r => r.Name
        };
        Table.Column("Name", r => r.Name, 220, true)
            .ChipColumn("Official", r => r.Official ? "Official" : "Community", r => r.Official ? Tone.Success : Tone.Neutral, 110)
            .Column("Stars", r => r.StarsText, 70)
            .Column("Description", r => r.Description, 380)
            .Filter("Official", r => r.Official)
            .RowAction("Pull…", Icons.CloudDownload, PullAsync, "Enter")
            .RowAction("Copy name", Icons.ContentCopy, r => { ActionSounds.Copy(r.Name); return Task.CompletedTask; })
            .RowAction("Open on Docker Hub", Icons.OpenInNew, r => { OpenHub(r); return Task.CompletedTask; });
        Table.Activated += r => _ = PullAsync(r);
        Table.MinHeight = 320; Table.MaxHeight = 560;
        term.KeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) await SearchAsync(); };
        var go = Ui.AsyncButton("Search", Icons.Search, ButtonKind.Filled, SearchAsync);
        var bar = new DockPanel();
        bar.Children.Add(go.Dock(Dock.Right).Margin(8, 0, 0, 0));
        bar.Children.Add(officialOnly.Dock(Dock.Right).Margin(12, 0, 0, 0));
        bar.Children.Add(term);
        Card = PageParts.Section("Search Docker Hub", "docker search --limit 25", Ui.VStack(12, Ui.Field("Search term", bar, "A word to search for in Docker Hub repository names and descriptions. docker search returns at most 25 results here. Turning on Official images only adds --filter is-official=true.", "postgres"), state, Table));
    }

    public async Task SearchAsync()
    {
        var text = term.Text.Trim();
        if (text.Length == 0) { state.Text = Loc.T("Enter a search term."); return; }
        try { DockerService.ValidateOperand(text, "term"); }
        catch (ArgumentException) { state.Text = Loc.T("The term cannot start with a hyphen or contain line breaks."); return; }
        var args = new List<string> { "search", "--format", "{{json .}}", "--limit", "25" };
        if (officialOnly.IsChecked == true) args.AddRange(["--filter", "is-official=true"]);
        args.Add(text);
        state.Text = Loc.T("Searching…");
        try
        {
            var rows = await Host.ReadRows(args, snapshot());
            var results = rows.Select(HubResult.From).Where(r => r.Name.Length > 0).ToList();
            Table.SetItems(results);
            Table.EmptyTitle = Loc.T("No results");
            Table.EmptyMessage = $"docker search returned no repositories for \"{text}\".";
            Table.Apply();
            state.Text = $"{results.Count} result(s) for \"{text}\"" + (officialOnly.IsChecked == true ? " · official only" : "") + $" · {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception e) when (e is InvalidOperationException or JsonException)
        {
            Table.SetItems([]);
            state.Text = Loc.T("Search failed: ") + e.Message;
        }
    }

    private async Task PullAsync(HubResult result)
    {
        if (!isCurrent()) { Host.Notify.Warning("The engine context changed after these results loaded. Refresh before pulling."); return; }
        var tag = await Host.Overlay.PromptAsync(Loc.T("Pull") + " " + result.Name, "Tag", "latest", "The image is pulled into the engine of the current context. Use a specific version tag for repeatable builds.",
            v => v.Any(c => char.IsWhiteSpace(c) || c is ':' or '/' or '@') ? "A tag cannot contain spaces, colons, slashes or @." : v.Length > 128 ? "A tag is at most 128 characters." : null);
        if (tag == null) return;
        var reference = result.Name + ":" + tag;
        await Host.Execute("Pull " + reference, ["image", "pull", reference], true, snapshot());
    }

    private static void OpenHub(HubResult result)
    {
        var path = result.Name.Contains('/') ? "r/" + result.Name : "_/" + result.Name;
        PageParts.OpenExternal("https://hub.docker.com/" + path);
    }
}
