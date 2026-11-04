using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Services;

/// <summary>
/// The two optional network schedule sources: one https JSON address and one Home Assistant entity. Every request is a bounded GET
/// (10 seconds, 64 KiB), is made only when an enabled rule uses the source, and never carries anything but its own token.
/// A failed request keeps the last good answer and reports the failure (see ScheduleSourceRules.Merge), so a network problem alone never changes a setting.
/// Error text is fixed wording: addresses, tokens and server text are never copied into it or into any log.
/// </summary>
public sealed class ScheduleNetwork
{
    private static readonly HttpClient Client = Create();
    private bool httpsBusy, haBusy;
    private string httpsKey = "", haKey = "";
    public SourceSnapshot Https { get; private set; } = SourceSnapshot.Empty;
    public SourceSnapshot HomeAssistant { get; private set; } = SourceSnapshot.Empty;
    /// <summary>Raised on the UI thread after a poll finishes, whether it worked or not.</summary>
    public event Action? Changed;

    private static HttpClient Create()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 3, UseCookies = false };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(ScheduleSourceRules.RequestTimeoutSeconds) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MaterialDockerImage-Schedules/1");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public static bool Uses(IEnumerable<ScheduleRule> rules, string source) => rules.Any(r => r.Enabled && r.Source == source);

    /// <summary>Polls whichever sources are due (or all configured ones when forced) and are used by an enabled rule.</summary>
    public async Task PollAsync(UserSettings settings, bool force)
    {
        var tasks = new List<Task>();
        var now = DateTime.UtcNow;
        if (Uses(settings.ScheduleRules, ScheduleSources.Https) && ScheduleSourceRules.TryNormalizeHttpsUrl(settings.ScheduleHttpsUrl, out var httpsUrl, out _))
        {
            if (httpsKey != httpsUrl) { httpsKey = httpsUrl; Https = SourceSnapshot.Empty; force = true; }
            if (!httpsBusy && (force || ScheduleSourceRules.PollDue(Https.LastAttemptUtc, now, settings.ScheduleHttpsPollMinutes, ScheduleSourceRules.MinHttpsPollMinutes))) tasks.Add(PollHttpsAsync(httpsUrl));
        }
        else if (httpsKey.Length > 0) { httpsKey = ""; Https = SourceSnapshot.Empty; Changed?.Invoke(); }
        if (Uses(settings.ScheduleRules, ScheduleSources.HomeAssistant) && ScheduleSourceRules.TryNormalizeHomeAssistantUrl(settings.ScheduleHaBaseUrl, out var baseUrl, out _) && ScheduleSourceRules.IsValidEntityId(settings.ScheduleHaEntity))
        {
            var key = baseUrl + "|" + settings.ScheduleHaEntity;
            if (haKey != key) { haKey = key; HomeAssistant = SourceSnapshot.Empty; force = true; }
            if (!haBusy && (force || ScheduleSourceRules.PollDue(HomeAssistant.LastAttemptUtc, now, settings.ScheduleHaPollMinutes, ScheduleSourceRules.MinHomeAssistantPollMinutes))) tasks.Add(PollHomeAssistantAsync(baseUrl, settings.ScheduleHaEntity));
        }
        else if (haKey.Length > 0) { haKey = ""; HomeAssistant = SourceSnapshot.Empty; Changed?.Invoke(); }
        if (tasks.Count > 0) await Task.WhenAll(tasks);
    }

    /// <summary>Forgets what the sources held (after an address, entity or token changed) so old data cannot outlive its configuration.</summary>
    public void Forget()
    {
        httpsKey = haKey = "";
        Https = SourceSnapshot.Empty; HomeAssistant = SourceSnapshot.Empty;
    }

    private async Task PollHttpsAsync(string url)
    {
        httpsBusy = true;
        try
        {
            var result = await FetchAsync(new HttpRequestMessage(HttpMethod.Get, url), ScheduleEvaluator.MaxHttpsBytes);
            SourceFetch fetch;
            if (result.Body == null) fetch = new(false, null, HomeAssistantState.None, result.Error);
            else
            {
                var parsed = ScheduleEvaluator.ParseSource(result.Body, result.Length, ScheduleEvaluator.MaxHttpsBytes, "response");
                fetch = parsed.Error.Length > 0 ? new(false, null, HomeAssistantState.None, parsed.Error) : new(true, parsed, HomeAssistantState.None, "");
            }
            if (httpsKey == url) Https = ScheduleSourceRules.Merge(Https, fetch, DateTime.UtcNow);
        }
        finally { httpsBusy = false; }
        Changed?.Invoke();
    }

    private async Task PollHomeAssistantAsync(string baseUrl, string entity)
    {
        haBusy = true;
        try
        {
            var token = HomeAssistantToken.Load();
            SourceFetch fetch;
            var address = ScheduleSourceRules.HomeAssistantStateUrl(baseUrl, entity);
            if (token == null) fetch = new(false, null, HomeAssistantState.None, HomeAssistantToken.Exists ? "The stored Home Assistant token could not be read. Enter it again." : "No Home Assistant token is stored. Enter one to use this source.");
            else if (address == null) fetch = new(false, null, HomeAssistantState.None, "The Home Assistant address or entity is not valid.");
            else
            {
                var request = new HttpRequestMessage(HttpMethod.Get, address);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var result = await FetchAsync(request, ScheduleSourceRules.MaxHomeAssistantBytes);
                if (result.Body == null) fetch = new(false, null, HomeAssistantState.None, result.Error);
                else
                {
                    var (state, error) = ScheduleSourceRules.ParseHomeAssistantState(result.Body, result.Length, entity);
                    fetch = error.Length > 0 ? new(false, null, HomeAssistantState.None, error) : new(true, null, state, "");
                }
            }
            if (haKey == baseUrl + "|" + entity) HomeAssistant = ScheduleSourceRules.Merge(HomeAssistant, fetch, DateTime.UtcNow);
        }
        finally { haBusy = false; }
        Changed?.Invoke();
    }

    private sealed record Fetched(string? Body, long Length, string Error);

    private static async Task<Fetched> FetchAsync(HttpRequestMessage request, int maxBytes)
    {
        try
        {
            using (request)
            {
                using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden) return new(null, 0, $"The server refused the request (HTTP {(int)response.StatusCode}).");
                if (!response.IsSuccessStatusCode) return new(null, 0, $"The server answered HTTP {(int)response.StatusCode}.");
                if (response.Content.Headers.ContentLength is { } declared && declared > maxBytes) return new(null, 0, $"The answer is larger than {maxBytes / 1024} KiB.");
                await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                var buffer = new byte[maxBytes + 1];
                var total = 0;
                while (total < buffer.Length)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(total)).ConfigureAwait(false);
                    if (read == 0) break;
                    total += read;
                }
                if (total > maxBytes) return new(null, 0, $"The answer is larger than {maxBytes / 1024} KiB.");
                return new(System.Text.Encoding.UTF8.GetString(buffer, 0, total), total, "");
            }
        }
        catch (TaskCanceledException) { return new(null, 0, $"The request timed out after {ScheduleSourceRules.RequestTimeoutSeconds} seconds."); }
        catch (HttpRequestException) { return new(null, 0, "The server could not be reached."); }
        catch (Exception e) when (e is IOException or InvalidOperationException or NotSupportedException) { return new(null, 0, "The answer could not be read."); }
    }
}
