using System.Globalization;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Finds a Dockerfile this application knows for an image and relates its history rows to the Dockerfile lines. Two
/// sources are tried, in order: a build from the Build page in this session whose result digest is this image, and the
/// Dockerfile currently in the Build editor. <see cref="DockerfileLayerSource"/> only returns notes when every
/// instruction is accounted for, so the current editor text never labels an image it did not build.
/// </summary>
internal sealed class ImageLayerSources
{
    private static AppHost Host => AppHost.Current;
    private readonly IReadOnlyDictionary<int, LayerSourceNote> notes;

    /// <summary>Where the matched Dockerfile came from: its path, or a description of an unsaved draft.</summary>
    public string Origin { get; } = "";
    /// <summary>True when the matched text is exactly what the Build editor holds now, so its lines can be opened there.</summary>
    public bool InEditor { get; }

    private ImageLayerSources(IReadOnlyDictionary<int, LayerSourceNote> notes, string origin, bool inEditor)
    {
        this.notes = notes;
        Origin = origin;
        InEditor = inEditor;
    }

    public static ImageLayerSources Empty { get; } = new(new Dictionary<int, LayerSourceNote>(), "", false);

    public static ImageLayerSources Find(string imageId, IReadOnlyList<ImageLayerEntry> layers)
    {
        if (layers.Count == 0) return Empty;
        var history = layers.OrderBy(l => l.Index).Select(l => l.CreatedBy).ToList();
        var editorText = Host.HandOff.TryGetValue("Build.CurrentSource", out var source) && source is Func<string> getSource ? getSource() : "";
        var editorPath = Host.HandOff.TryGetValue("Build.CurrentPath", out var path) && path is Func<string> getPath ? getPath() : "";
        var candidates = BuildSessionHistory.Entries
            .Where(e => e.Status == "Completed" && e.DockerfileSource.Length > 0 && string.Equals(e.Digest, imageId, StringComparison.Ordinal))
            .Select(e => (Text: e.DockerfileSource, Path: e.DockerfilePath))
            .ToList();
        if (editorText.Length > 0) candidates.Add((editorText, editorPath));
        foreach (var (text, origin) in candidates)
        {
            var matched = DockerfileLayerSource.Match(text, history);
            if (matched.Count > 0) return new ImageLayerSources(matched, origin.Length > 0 ? origin : Loc.T("Unsaved Dockerfile draft in Build"), text == editorText);
        }
        return Empty;
    }

    public LayerSourceNote? For(ImageLayerEntry layer) => notes.TryGetValue(layer.Index, out var note) ? note : null;

    /// <summary>"Your Dockerfile, line 12 · from stage deps" or "Base: alpine:3.20".</summary>
    public static string Describe(LayerSourceNote note)
    {
        if (note.IsBase) return Loc.T("Base:") + " " + note.BaseImage;
        var text = string.Format(CultureInfo.CurrentCulture, Loc.T("Your Dockerfile, line {0}"), note.Line);
        if (note.FromStage.Length > 0) text += " · " + string.Format(CultureInfo.CurrentCulture, Loc.T("from stage {0}"), note.FromStage);
        else if (note.FromImage.Length > 0) text += " · " + string.Format(CultureInfo.CurrentCulture, Loc.T("from image {0}"), note.FromImage);
        return text;
    }
}
