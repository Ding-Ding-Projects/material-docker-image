using System.Text.RegularExpressions;

namespace MaterialDockerImage.Pages;

/// <summary>Finds the HCL <c>target "name" { ... }</c> block of a bake target in the files bake reads.</summary>
public static class BakeHclSource
{
    private const long MaxBytes = 2 * 1024 * 1024;

    /// <summary>The block text prefixed with its file name, or an empty string when no HCL file declares the target.</summary>
    public static string Read(IEnumerable<string> files, string target)
    {
        foreach (var file in files)
        {
            if (!file.EndsWith(".hcl", StringComparison.OrdinalIgnoreCase)) continue;
            string text;
            try
            {
                if (!File.Exists(file) || new FileInfo(file).Length > MaxBytes) continue;
                text = File.ReadAllText(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            var block = ExtractBlock(text, "target", target);
            if (block != null) return "# " + Path.GetFileName(file) + Environment.NewLine + block;
        }
        return "";
    }

    /// <summary>Returns the balanced block that starts at <c>kind "name" {</c>, skipping braces inside strings and comments.</summary>
    public static string? ExtractBlock(string text, string kind, string name)
    {
        Match m;
        try { m = Regex.Match(text, @"(?m)^[ \t]*" + Regex.Escape(kind) + @"[ \t]+""" + Regex.Escape(name) + @"""[ \t]*\{", RegexOptions.None, TimeSpan.FromSeconds(2)); }
        catch (RegexMatchTimeoutException) { return null; }
        if (!m.Success) return null;
        var depth = 0;
        var inString = false;
        for (var i = m.Index + m.Length - 1; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\\') i++;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') inString = true;
            else if (c == '#' || (c == '/' && i + 1 < text.Length && text[i + 1] == '/'))
            {
                while (i < text.Length && text[i] != '\n') i++;
            }
            else if (c == '{') depth++;
            else if (c == '}' && --depth == 0) return text.Substring(m.Index, i - m.Index + 1).TrimEnd();
        }
        return null;
    }
}
