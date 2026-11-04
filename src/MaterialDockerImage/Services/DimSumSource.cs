using System.Net.Http;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Services;

/// <summary>
/// Reads the public dim sum catalogue and its published pictures. The only network use is two public reads of the project's catalogue:
/// no personal data is sent, nothing is tracked, no picture is stored in this project, and every failure falls back to a plain name instead of a made-up picture.
/// The small cache in the data folder records the catalogue URL and revision it came from.
/// </summary>
public sealed class DimSumSource
{
    private static readonly HttpClient Http = Create();
    private static readonly TimeSpan CacheLife = TimeSpan.FromDays(7);
    private const int MaxCachedPictures = 24;
    private readonly string folder;
    private readonly bool offline;
    private IReadOnlyList<DimSumDish>? loaded;
    private Task<IReadOnlyList<DimSumDish>>? loading;

    /// <summary>Capture mode never touches the network or the disk.</summary>
    public DimSumSource(bool offline) { this.offline = offline; folder = Path.Combine(UserSettings.DataDirectory, "dimsum"); }

    private static HttpClient Create()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 4, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MaterialDockerImage-dim-sum/1");
        return client;
    }

    private string CachePath => Path.Combine(folder, "catalog-cache.json");

    /// <summary>The dishes, or an empty list when the catalogue cannot be read. Safe to call repeatedly; one load serves every caller.</summary>
    public Task<IReadOnlyList<DimSumDish>> LoadAsync()
    {
        if (loaded != null) return Task.FromResult(loaded);
        return loading ??= Task.Run(async () =>
        {
            var found = await LoadCoreAsync();
            loaded = found;
            return found;
        });
    }

    public IReadOnlyList<DimSumDish>? Cached => loaded;

    private async Task<IReadOnlyList<DimSumDish>> LoadCoreAsync()
    {
        if (offline) return Array.Empty<DimSumDish>();
        (IReadOnlyList<DimSumDish> Dishes, string Revision, DateTimeOffset Fetched)? cache = null;
        try { if (File.Exists(CachePath)) cache = DimSumRules.FromCache(await File.ReadAllTextAsync(CachePath)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        if (cache is { } fresh && DateTimeOffset.UtcNow - fresh.Fetched < CacheLife) return fresh.Dishes;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, DimSumRules.CatalogUrl);
            if (cache is { Revision.Length: > 0 } old) request.Headers.TryAddWithoutValidation("If-None-Match", old.Revision);
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (response.StatusCode == System.Net.HttpStatusCode.NotModified && cache is { } same) { Store(same.Dishes, same.Revision); return same.Dishes; }
            if (!response.IsSuccessStatusCode) return cache?.Dishes ?? Array.Empty<DimSumDish>();
            var json = await response.Content.ReadAsStringAsync();
            var dishes = DimSumRules.ParseCatalog(json);
            if (dishes.Count == 0) return cache?.Dishes ?? Array.Empty<DimSumDish>();
            var revision = response.Headers.ETag?.Tag ?? response.Content.Headers.LastModified?.ToString("o") ?? "";
            Store(dishes, revision);
            return dishes;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException) { return cache?.Dishes ?? Array.Empty<DimSumDish>(); }
    }

    private void Store(IReadOnlyList<DimSumDish> dishes, string revision)
    {
        try { Directory.CreateDirectory(folder); File.WriteAllText(CachePath, DimSumRules.ToCache(dishes, revision, DateTimeOffset.UtcNow)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>A local path to the dish's published picture, downloading it once into the cache. Null when it cannot be had; callers then show the name only.</summary>
    public async Task<string?> PictureAsync(DimSumDish dish)
    {
        if (offline) return null;
        var safe = new string(dish.Id.Where(c => char.IsAsciiLetterOrDigit(c) || c == '-').ToArray());
        if (safe.Length == 0) return null;
        var target = Path.Combine(folder, safe + ".png");
        try
        {
            if (File.Exists(target)) { File.SetLastWriteTimeUtc(target, DateTime.UtcNow); return target; }
            foreach (var url in DimSumRules.ImageUrls(dish))
            {
                using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode) continue;
                if (response.Content.Headers.ContentLength is > DimSumRules.MaxImageBytes) continue;
                var bytes = await response.Content.ReadAsByteArrayAsync();
                if (bytes.Length > DimSumRules.MaxImageBytes || !DimSumRules.LooksLikePng(bytes)) continue;
                Directory.CreateDirectory(folder);
                var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllBytesAsync(temporary, bytes);
                File.Move(temporary, target, true);
                Trim();
                return target;
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException) { }
        return null;
    }

    private void Trim()
    {
        try
        {
            foreach (var old in new DirectoryInfo(folder).GetFiles("*.png").OrderByDescending(f => f.LastWriteTimeUtc).Skip(MaxCachedPictures)) old.Delete();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>The long-lived game services of one app run, built on first use. Capture mode keeps everything in memory.</summary>
public static class GameServices
{
    private static UnlockLadder? ladder;
    private static SchoolModeStore? school;
    private static DimSumSource? dimSum;

    private static bool Capture => AppHost.Current?.IsCapture ?? true;

    public static UnlockLadder Ladder => ladder ??= new UnlockLadder(new LadderLedger(Capture ? null : LadderLedger.DefaultPath));
    public static SchoolModeStore School => school ??= new SchoolModeStore(Capture ? null : SchoolModeStore.SharedPath);
    public static DimSumSource DimSum => dimSum ??= new DimSumSource(Capture);
}
