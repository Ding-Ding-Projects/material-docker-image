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
        foreach (var pair in LocksCantonese()) yield return pair;
        foreach (var pair in ElementAppearanceCantonese()) yield return pair;
        foreach (var pair in AuthenticatorCantonese()) yield return pair;
        foreach (var pair in LadderCantonese()) yield return pair;
        foreach (var pair in SchoolPasskeyCantonese()) yield return pair;
        foreach (var pair in LockCatalogueCantonese()) yield return pair;
        foreach (var pair in LockExtraCantonese()) yield return pair;
        foreach (var pair in SchedulesExtendedCantonese()) yield return pair;
        foreach (var pair in PageCantoneseRegex()) yield return pair;
        foreach (var pair in PageCantoneseRegexTools()) yield return pair;
        foreach (var pair in PageCantoneseRegexSubject()) yield return pair;
        foreach (var pair in PageCantoneseRegexFinish()) yield return pair;
        foreach (var pair in PageCantoneseRegexRest()) yield return pair;
        foreach (var pair in PageCantoneseParity()) yield return pair;
        foreach (var pair in PageCantoneseRegexPanes()) yield return pair;
        foreach (var pair in PageCantoneseImagesV2()) yield return pair;
        foreach (var pair in PageCantonesePaletteDescriptions()) yield return pair;
        foreach (var pair in PageCantoneseCanvasMap()) yield return pair;
        foreach (var pair in PageCantoneseAppearanceMotion()) yield return pair;
        foreach (var pair in PageCantoneseCreatorV2()) yield return pair;
        foreach (var pair in PageCantoneseAppearanceStudio()) yield return pair;
        foreach (var pair in PageCantoneseRecords()) yield return pair;
        foreach (var pair in CameraQrCantonese()) yield return pair;
    }
}
