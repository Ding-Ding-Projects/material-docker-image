using System;
using System.IO;
namespace MaterialDockerImage.UI;
public static class SourceSafety
{
    public static string RequireLoadedPath(string loadedPath, string requestedPath)
    {
        if (string.IsNullOrWhiteSpace(loadedPath) || string.IsNullOrWhiteSpace(requestedPath)) throw new InvalidOperationException("Load a source file before saving or executing it.");
        var actual = Path.GetFullPath(requestedPath);
        if (!string.Equals(Path.GetFullPath(loadedPath), actual, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The path changed. Load that file explicitly, or use Save As to write a reviewed destination.");
        return actual;
    }
    public static bool IsCurrentSnapshot(string? ownerContext, long ownerGeneration, string? activeContext, long activeGeneration) => ownerGeneration == activeGeneration && string.Equals(ownerContext,activeContext,StringComparison.Ordinal);
}
