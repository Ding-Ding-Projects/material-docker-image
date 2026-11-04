using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>What a Home Assistant entity reported. Anything other than "on" or "off" (unavailable, unknown, a sensor value) is Indeterminate and applies nothing.</summary>
public enum HomeAssistantState { None, On, Off, Indeterminate }

/// <summary>The result of one network poll, reduced to what the pure rules need.</summary>
public sealed record SourceFetch(bool Succeeded, ScheduleSourceValues? Values, HomeAssistantState State, string Error);

/// <summary>What a network source currently holds: the last good data, when it was fetched, and the honest reason the latest attempt failed.</summary>
public sealed record SourceSnapshot(ScheduleSourceValues? Values, HomeAssistantState State, DateTime? LastGoodUtc, DateTime? LastAttemptUtc, string Error)
{
    public static SourceSnapshot Empty { get; } = new(null, HomeAssistantState.None, null, null, "");
    /// <summary>True when the latest attempt failed. Earlier good data, if any, is still in use.</summary>
    public bool Offline => Error.Length > 0;
}

/// <summary>Pure rules for the HTTPS and Home Assistant schedule sources: address validation, payload validation, polling cadence and offline behaviour.</summary>
public static class ScheduleSourceRules
{
    public const int MaxUrlLength = 500;
    public const int RequestTimeoutSeconds = 10;
    public const int MinHttpsPollMinutes = 5;
    public const int MinHomeAssistantPollMinutes = 1;
    public const int MaxPollMinutes = 1440;
    public const int DefaultHttpsPollMinutes = 15;
    public const int DefaultHomeAssistantPollMinutes = 5;
    public const int MaxHomeAssistantBytes = 65536;
    public const int MaxTokenLength = 512;

    private static readonly Regex EntityPattern = new("\\A[a-z0-9_]{1,40}\\.[a-z0-9_]{1,80}\\z", RegexOptions.Compiled);
    private static readonly string[] SecretWords = ["token", "secret", "password", "passwd", "apikey", "api_key", "api-key", "auth", "bearer", "credential"];
    private static readonly string[] SecretNames = ["key", "sig", "signature", "pwd", "pass"];

    public static int ClampPollMinutes(int minutes, int minimum) => Math.Clamp(minutes, minimum, MaxPollMinutes);

    /// <summary>
    /// Accepts only an absolute https address with a host, no user name or password, no fragment and no query parameter whose name looks like a secret:
    /// a secret typed into the address would be saved in plain text in the settings file.
    /// </summary>
    public static bool TryNormalizeHttpsUrl(string? text, out string url, out string error)
    {
        url = ""; error = "";
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0) { error = "Enter an https address."; return false; }
        if (trimmed.Length > MaxUrlLength) { error = "The address is longer than 500 characters."; return false; }
        if (trimmed.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))) { error = "The address cannot contain spaces or control characters."; return false; }
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) { error = "Only https addresses are accepted."; return false; }
        if (uri.Host.Length == 0) { error = "The address has no host."; return false; }
        if (uri.UserInfo.Length > 0) { error = "Remove the user name and password from the address. Credentials are never stored in plain text."; return false; }
        if (uri.Fragment.Length > 0) { error = "Remove the part after # from the address."; return false; }
        if (QueryLooksSecret(uri.Query)) { error = "The address has a query parameter that looks like a secret. Secrets are never stored in plain text."; return false; }
        url = uri.AbsoluteUri;
        return true;
    }

    private static bool QueryLooksSecret(string query)
    {
        if (query.Length <= 1) return false;
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = Uri.UnescapeDataString(pair.Split('=', 2)[0]).ToLowerInvariant();
            if (SecretWords.Any(name.Contains) || SecretNames.Contains(name)) return true;
        }
        return false;
    }

    /// <summary>True for loopback, private and link-local addresses and for names that only resolve inside a home network.</summary>
    public static bool IsLocalNetworkHost(string host)
    {
        host = host.Trim('[', ']').ToLowerInvariant();
        if (host.Length == 0) return false;
        if (IPAddress.TryParse(host, out var ip))
        {
            if (IPAddress.IsLoopback(ip)) return true;
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                var b = ip.GetAddressBytes();
                return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
            }
            if (ip.AddressFamily == AddressFamily.InterNetworkV6) return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || (ip.GetAddressBytes()[0] & 0xFE) == 0xFC;
            return false;
        }
        return host == "localhost" || !host.Contains('.') || host.EndsWith(".local", StringComparison.Ordinal) || host.EndsWith(".lan", StringComparison.Ordinal) || host.EndsWith(".home.arpa", StringComparison.Ordinal);
    }

    /// <summary>
    /// A Home Assistant base address such as http://homeassistant.local:8123. http is accepted only on the local network;
    /// anywhere else the long-lived access token would travel in clear text, so https is required.
    /// </summary>
    public static bool TryNormalizeHomeAssistantUrl(string? text, out string url, out string error)
    {
        url = ""; error = "";
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0) { error = "Enter the Home Assistant address."; return false; }
        if (trimmed.Length > MaxUrlLength) { error = "The address is longer than 500 characters."; return false; }
        if (trimmed.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))) { error = "The address cannot contain spaces or control characters."; return false; }
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) { error = "Use an http or https address."; return false; }
        if (uri.Host.Length == 0) { error = "The address has no host."; return false; }
        if (uri.UserInfo.Length > 0) { error = "Remove the user name and password from the address."; return false; }
        if (uri.Query.Length > 0 || uri.Fragment.Length > 0 || (uri.AbsolutePath != "/" && uri.AbsolutePath.Length > 0)) { error = "Use only the base address, for example http://homeassistant.local:8123."; return false; }
        if (uri.Scheme == Uri.UriSchemeHttp && !IsLocalNetworkHost(uri.Host)) { error = "Use https for a host outside your local network so the token is not sent in clear text."; return false; }
        url = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }

    public static bool IsValidEntityId(string? entity) => entity != null && EntityPattern.IsMatch(entity);

    /// <summary>The GET address of one entity's state, or null when either part is invalid.</summary>
    public static string? HomeAssistantStateUrl(string baseUrl, string entity) =>
        TryNormalizeHomeAssistantUrl(baseUrl, out var normalized, out _) && IsValidEntityId(entity) ? normalized + "/api/states/" + entity : null;

    public static bool IsValidToken(string? token) => token is { Length: > 0 and <= MaxTokenLength } && token.All(c => c > 32 && c < 127);

    /// <summary>
    /// Reads Home Assistant's state document for one entity: {"entity_id":"input_boolean.night","state":"on",...}.
    /// A document for a different entity, an oversized or malformed one, or a missing state is refused whole.
    /// Only the strings "on" and "off" decide anything; "unavailable", "unknown" and every other state are Indeterminate.
    /// </summary>
    public static (HomeAssistantState State, string Error) ParseHomeAssistantState(string? json, long byteLength, string expectedEntity)
    {
        if (string.IsNullOrWhiteSpace(json)) return (HomeAssistantState.None, "Home Assistant sent an empty answer.");
        if (byteLength > MaxHomeAssistantBytes) return (HomeAssistantState.None, "The Home Assistant answer is larger than 64 KiB.");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return (HomeAssistantState.None, "The Home Assistant answer is not one JSON object.");
            if (!document.RootElement.TryGetProperty("entity_id", out var id) || id.ValueKind != JsonValueKind.String || !string.Equals(id.GetString(), expectedEntity, StringComparison.Ordinal)) return (HomeAssistantState.None, "The Home Assistant answer is for a different entity.");
            if (!document.RootElement.TryGetProperty("state", out var state) || state.ValueKind != JsonValueKind.String) return (HomeAssistantState.None, "The Home Assistant answer has no state.");
            return (state.GetString()?.ToLowerInvariant() switch { "on" => HomeAssistantState.On, "off" => HomeAssistantState.Off, _ => HomeAssistantState.Indeterminate }, "");
        }
        catch (JsonException) { return (HomeAssistantState.None, "The Home Assistant answer is not valid JSON."); }
    }

    /// <summary>An automatic poll is due when nothing was tried yet or the interval has fully elapsed. The interval is clamped to its minimum.</summary>
    public static bool PollDue(DateTime? lastAttemptUtc, DateTime nowUtc, int intervalMinutes, int minimumMinutes)
    {
        if (lastAttemptUtc == null) return true;
        var interval = TimeSpan.FromMinutes(ClampPollMinutes(intervalMinutes, minimumMinutes));
        return nowUtc - lastAttemptUtc.Value >= interval || nowUtc < lastAttemptUtc.Value;
    }

    /// <summary>
    /// Folds one poll into the snapshot. A success replaces the data. A failure never discards earlier good data and never invents new data:
    /// the previous values stay in use and the snapshot reports the failure, so a network problem alone cannot change any setting.
    /// </summary>
    public static SourceSnapshot Merge(SourceSnapshot? previous, SourceFetch fetch, DateTime nowUtc)
    {
        previous ??= SourceSnapshot.Empty;
        if (fetch.Succeeded) return new SourceSnapshot(fetch.Values, fetch.State, nowUtc, nowUtc, "");
        return previous with { LastAttemptUtc = nowUtc, Error = fetch.Error.Length > 0 ? fetch.Error : "The source could not be reached." };
    }

    /// <summary>One line the settings page shows for a source. <paramref name="translate"/> looks up each fixed phrase (and the failure reason); {0} and {1} are filled afterwards.</summary>
    public static string Describe(SourceSnapshot snapshot, DateTime nowUtc, Func<string, string>? translate = null)
    {
        var t = translate ?? (x => x);
        if (snapshot.LastAttemptUtc == null) return t("Not checked yet.");
        if (!snapshot.Offline) return string.Format(t("Online. Last checked {0}."), Age(snapshot.LastGoodUtc, nowUtc, t));
        return snapshot.LastGoodUtc == null
            ? string.Format(t("Offline: {0} Nothing from this source is applied."), t(snapshot.Error))
            : string.Format(t("Offline: {0} The last good answer from {1} stays in use and nothing was changed."), t(snapshot.Error), Age(snapshot.LastGoodUtc, nowUtc, t));
    }

    private static string Age(DateTime? thenUtc, DateTime nowUtc, Func<string, string> t)
    {
        if (thenUtc == null) return t("never");
        var span = nowUtc - thenUtc.Value;
        if (span < TimeSpan.FromMinutes(1)) return t("less than a minute ago");
        if (span < TimeSpan.FromHours(1)) return string.Format(t("{0} minutes ago"), (int)span.TotalMinutes);
        if (span < TimeSpan.FromDays(1)) return string.Format(t("{0} hours ago"), (int)span.TotalHours);
        return string.Format(t("{0} days ago"), (int)span.TotalDays);
    }
}
