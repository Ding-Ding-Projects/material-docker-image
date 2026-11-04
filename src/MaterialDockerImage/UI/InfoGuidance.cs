namespace MaterialDockerImage.UI;

/// <summary>Builds the fallback text behind a settings row's info icon when the row supplies no hand-written guidance.</summary>
public static class InfoGuidance
{
    /// <summary>Explanation, the built-in default, and how the choice is stored and undone. Only states what every saved setting really does.</summary>
    public static string ForSavedSetting(string explanation, string defaultText)
    {
        var sentence = explanation.TrimEnd();
        if (sentence.Length > 0 && sentence[^1] is not ('.' or '!' or '?')) sentence += ".";
        return $"{sentence} The built-in default is {defaultText}. A change is saved to settings.json on this computer straight away, and Reset all settings in Data and privacy restores the default.";
    }
}
