using MaterialDockerImage.Services;

namespace MaterialDockerImage.UI;

/// <summary>Framework-free rules for dropping a personal wording file on the window, shared by the window and the checks.</summary>
public static class VocabularyDropRules
{
    /// <summary>The ordinary phrase whose personal value, when the imported file gives one, becomes the headline.</summary>
    public const string HeadlineKey = "private vocabulary";
    public const string FallbackHeadline = "Personal wording applied";
    public static readonly TimeSpan Hold = TimeSpan.FromSeconds(3);

    /// <summary>The single dropped path when the file-drop payload holds exactly one file, otherwise null.</summary>
    public static string? SinglePath(object? fileDropPayload) => fileDropPayload is string[] { Length: 1 } paths ? paths[0] : null;

    public static bool IsJson(string path) => string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase);

    /// <summary>The file's own value for the headline phrase, else the localized neutral headline.</summary>
    public static string Headline() =>
        Loc.TryPersonal(HeadlineKey, out var personal) && personal.Trim().Length > 0 ? personal.Trim() : Loc.T(FallbackHeadline);
}
