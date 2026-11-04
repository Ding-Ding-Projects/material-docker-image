using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>Helpers for showing the builder's generated English wording in the chosen language.</summary>
public static class RegexText
{
    private static readonly Regex sentenceBreak = new(@"(?<=\.) (?=[A-Z\\])", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Translates a message that may be one sentence or several. When every sentence has its own translation they are joined with a space; otherwise the whole
    /// message is tried as one unit, which is how a sentence that ends in an error from the framework is matched (the error stays as the framework wrote it).
    /// </summary>
    public static string Sentences(string text, Func<string, string> translate)
    {
        if (string.IsNullOrEmpty(text)) return text;
        try
        {
            var parts = sentenceBreak.Split(text);
            if (parts.Length >= 2)
            {
                var each = parts.Select(translate).ToList();
                if (each.Zip(parts, (a, b) => a != b).All(changed => changed)) return string.Join(" ", each);
            }
        }
        catch (RegexMatchTimeoutException) { }
        return translate(text);
    }
}
