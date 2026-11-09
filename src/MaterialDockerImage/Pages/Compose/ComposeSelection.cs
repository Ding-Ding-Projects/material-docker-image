namespace MaterialDockerImage.Pages;

/// <summary>Keys for the Compose project list: an engine project by name, otherwise a file by its path.</summary>
public static class ComposeSelection
{
    public static string ListedKey(string name) => "ls:" + name;
    public static string FileKey(string path) => "file:" + path;

    /// <summary>The list key that shows a Compose file: the engine project that runs it, otherwise the file itself.</summary>
    public static string ForFile(IEnumerable<(string Name, bool Listed, IReadOnlyList<string> Files)> entries, string fullPath) =>
        entries.FirstOrDefault(e => e.Listed && e.Files.Contains(fullPath, StringComparer.OrdinalIgnoreCase)) is { Name: not null } hit
            ? ListedKey(hit.Name) : FileKey(fullPath);
}
