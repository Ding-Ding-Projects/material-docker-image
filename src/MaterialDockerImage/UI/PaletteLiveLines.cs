using System.Globalization;
using MaterialDockerImage.Services;

namespace MaterialDockerImage.UI;

/// <summary>
/// Live second lines for command palette rows, built from state a page has already loaded. Each method formats real
/// counts and names; none of them runs docker. Framework-free so tests/UiState can check the wording and the
/// Cantonese coverage. Phrases are joined with " · " and every phrase is a translated template.
/// </summary>
public static class PaletteLiveLines
{
    /// <summary>Every template used here, for the Cantonese coverage check.</summary>
    public static readonly string[] Templates =
    [
        "{0} running", "{0} total", "{0} image", "{0} images", "{0} project", "{0} projects", "{0} recent file", "{0} recent files",
        "Current context: {0}", "Context list unavailable", "Sampling every {0} s", "Sampling paused", "{0} container sampled", "{0} containers sampled",
        "Last sample {0}", "{0} of {1} images analyzed", "Swarm active", "Swarm inactive", "Worker node", "{0} node", "{0} nodes",
        "{0} stopped container", "{0} stopped containers", "Nothing stopped to remove", "Selected container", "Selected image",
        "Swarm state unavailable",
    ];

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string F(string template, params string[] values)
    {
        var text = Loc.T(template);
        for (var i = 0; i < values.Length; i++) text = text.Replace("{" + i + "}", values[i]);
        return text;
    }
    private static string Join(params string[] parts) => string.Join(" · ", parts.Where(p => p.Length > 0));

    /// <summary>The second line to show: the live text when there is one, otherwise the description, otherwise the group.</summary>
    public static string Choose(string? live, string? description, string group) =>
        !string.IsNullOrWhiteSpace(live) ? live : CommandDescriptions.SubLine(description, group);

    /// <summary>The text a row is searched by: the static title and description text plus the live line, so either matches.</summary>
    public static string SearchText(string staticText, string? live) => string.IsNullOrWhiteSpace(live) ? staticText : staticText + " " + live;

    public static string Containers(int running, int total) => Join(F("{0} running", N(running)), F("{0} total", N(total)));

    public static string Images(int count, string totalSize) => Join(F(count == 1 ? "{0} image" : "{0} images", N(count)), totalSize);

    public static string Compose(int projects, int running, int recentFiles) =>
        Join(F(projects == 1 ? "{0} project" : "{0} projects", N(projects)), F("{0} running", N(running)),
            recentFiles > 0 ? F(recentFiles == 1 ? "{0} recent file" : "{0} recent files", N(recentFiles)) : "");

    public static string Contexts(string? current, bool listed) =>
        !listed ? Loc.T("Context list unavailable") : F("Current context: {0}", string.IsNullOrWhiteSpace(current) ? "default" : current);

    /// <summary>Monitor: whether sampling runs, its interval and how many containers the last sample covered.</summary>
    public static string Monitor(bool sampling, int intervalSeconds, int containers, DateTimeOffset? lastSample)
    {
        var count = F(containers == 1 ? "{0} container sampled" : "{0} containers sampled", N(containers));
        return sampling
            ? Join(F("Sampling every {0} s", N(intervalSeconds)), count)
            : Join(Loc.T("Sampling paused"), lastSample is { } t ? F("Last sample {0}", t.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)) : "", count);
    }

    public static string Security(int analyzed, int images) => F("{0} of {1} images analyzed", N(analyzed), N(images));

    /// <summary>Swarm membership: null state means the read failed.</summary>
    public static string Swarm(bool? active, bool manager, int nodes) => active switch
    {
        null => Loc.T("Swarm state unavailable"),
        false => Loc.T("Swarm inactive"),
        true when !manager => Join(Loc.T("Swarm active"), Loc.T("Worker node")),
        _ => Join(Loc.T("Swarm active"), F(nodes == 1 ? "{0} node" : "{0} nodes", N(nodes))),
    };

    public static string StoppedContainers(int stopped) =>
        stopped == 0 ? Loc.T("Nothing stopped to remove") : F(stopped == 1 ? "{0} stopped container" : "{0} stopped containers", N(stopped));

    /// <summary>The focused container: its name and the engine's status text (for example "Up 4 minutes"), kept literal.</summary>
    public static string SelectedContainer(string name, string status) => Join(Loc.T("Selected container"), name, status);

    /// <summary>The focused image: its reference and size.</summary>
    public static string SelectedImage(string reference, string size) => Join(Loc.T("Selected image"), reference, size);
}
