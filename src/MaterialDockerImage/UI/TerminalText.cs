using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>
/// Plain text from command output that may contain terminal control sequences: colour and cursor codes (CSI), window
/// titles and links (OSC) and stray escape characters are removed, so the operation panel never shows "[33m".
/// </summary>
public static class TerminalText
{
    private static readonly Regex Sequences = new(@"\x1B\[[0-?]*[ -/]*[@-~]|\x1B\][^\x07\x1B]*(?:\x07|\x1B\\)|\x1B[@-Z\\-_]|\x9B[0-?]*[ -/]*[@-~]",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static string Plain(string text)
    {
        if (text.IndexOf('\x1B') < 0 && text.IndexOf('\x9B') < 0) return text;
        try { return Sequences.Replace(text, "").Replace("\x1B", ""); }
        catch (RegexMatchTimeoutException) { return text.Replace("\x1B", ""); }
    }

    /// <summary>Plain, non-empty lines of a chunk of streamed output, as shown by the log and text viewers.</summary>
    public static IEnumerable<string> Lines(string chunk) =>
        Plain(chunk).Replace("\r\n", "\n").Split('\n').Where(l => l.Length > 0);
}
