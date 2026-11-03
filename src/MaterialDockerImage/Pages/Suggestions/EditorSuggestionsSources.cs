using System.Text.Json;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Real data for Suggestions: local images (docker image ls --digests) and published host ports (docker container
/// ls), read once per engine context in the background and cached; and build context entry names, listed in the
/// background per folder and refreshed after 30 seconds. Nothing secret is read.
/// </summary>
public static class EditorSuggestionsSources
{
    private static AppHost Host => AppHost.Current;
    private static string? loadedContext;
    private static long loadedGeneration = -1;
    private static Task? loading;
    private static IReadOnlyList<SuggestionLocalImage> images = Array.Empty<SuggestionLocalImage>();
    private static HashSet<(int Port, string Protocol)> published = new();
    private static readonly Dictionary<string, (DateTime At, IReadOnlyList<string> Entries)> contextCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> listing = new(StringComparer.OrdinalIgnoreCase);
    private static bool hooked;

    /// <summary>Raised on the UI thread when images, ports or a context listing finished loading.</summary>
    public static event Action? Updated;
    /// <summary>The reason the last image read failed, or empty.</summary>
    public static string ImageError { get; private set; } = "";
    public static bool ImagesLoading => loading is { IsCompleted: false };
    /// <summary>The engine context the cached images belong to (for picker wording).</summary>
    public static string ImagesContext => loadedContext is { Length: > 0 } c ? c : "the CLI default context";

    public static IReadOnlyList<SuggestionLocalImage> Images { get { EnsureImages(); return images; } }

    public static bool IsPublished(int port, string protocol) { EnsureImages(); return published.Contains((port, protocol)); }

    public static SuggestionSources For(string? contextFolder) => new(Images, ContextEntries(contextFolder));

    public static void EnsureImages(bool force = false)
    {
        if (Host == null) return;
        if (!hooked) { hooked = true; Host.ContextChanged += () => { loadedContext = null; loadedGeneration = -1; images = Array.Empty<SuggestionLocalImage>(); published = new(); }; }
        if (!force && loadedGeneration == Host.Generation && loadedContext == Host.Context) return;
        if (ImagesLoading) return;
        loadedContext = Host.Context;
        loadedGeneration = Host.Generation;
        loading = LoadAsync(Host.Context, Host.Generation);
    }

    private static async Task LoadAsync(string? context, long generation)
    {
        var found = new List<SuggestionLocalImage>();
        var ports = new HashSet<(int, string)>();
        try
        {
            foreach (var row in await Host.ReadRows(["image", "ls", "--digests", "--format", "{{json .}}"], context))
                found.Add(new SuggestionLocalImage(Text(row, "Repository"), Text(row, "Tag"), Text(row, "Digest"), Text(row, "ID"), Text(row, "Size")));
            ImageError = "";
        }
        catch (Exception e) when (e is InvalidOperationException or JsonException) { ImageError = e.Message; }
        try
        {
            foreach (var row in await Host.ReadRows(["container", "ls", "--format", "{{json .}}"], context))
                foreach (var p in EditorValueFormats.PublishedHostPorts(Text(row, "Ports"))) ports.Add(p);
        }
        catch (Exception e) when (e is InvalidOperationException or JsonException) { }
        // Drop results for a context the window has since left.
        if (Host.Context != context || Host.Generation != generation) return;
        images = found;
        published = ports;
        Updated?.Invoke();
    }

    private static string Text(JsonElement row, string name) => row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>Cached entry names of the folder (empty until the background listing finishes).</summary>
    public static IReadOnlyList<string> ContextEntries(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return Array.Empty<string>();
        string full;
        try { full = Path.GetFullPath(folder); } catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return Array.Empty<string>(); }
        contextCache.TryGetValue(full, out var cached);
        if (DateTime.UtcNow - cached.At > TimeSpan.FromSeconds(30) && listing.Add(full))
        {
            _ = Task.Run(() => DockerfileCompletionPaths.List(full)).ContinueWith(t =>
            {
                listing.Remove(full);
                if (t.IsCompletedSuccessfully) { contextCache[full] = (DateTime.UtcNow, t.Result); Updated?.Invoke(); }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        return cached.Entries ?? Array.Empty<string>();
    }
}
