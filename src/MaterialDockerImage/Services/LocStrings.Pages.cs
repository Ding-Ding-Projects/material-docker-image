namespace MaterialDockerImage.Services;

/// <summary>Hong Kong written Cantonese for destination page copy. Keys containing {0}, {1}… are sentence templates.</summary>
public static partial class Loc
{
    private static IEnumerable<KeyValuePair<string, string>> PageCantonese()
    {
        foreach (var pair in PageCantonesePart1()) yield return pair;
        foreach (var pair in PageCantonesePart2()) yield return pair;
        foreach (var pair in PageCantonesePart3()) yield return pair;
        foreach (var pair in PageCantonesePart4()) yield return pair;
        foreach (var pair in PageCantoneseSummaries()) yield return pair;
        foreach (var pair in PageCantoneseContainers()) yield return pair;
        foreach (var pair in SettingsSafetyCantonese()) yield return pair;
        foreach (var pair in PageCantoneseRegex()) yield return pair;
    }
}
