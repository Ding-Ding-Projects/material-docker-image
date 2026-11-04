using System;
using System.Collections.Generic;

namespace MaterialDockerImage.UI;

/// <summary>How School mode is switched off: with a PIN or password, or with the platform passkey (Windows Hello user verification).</summary>
public enum SchoolOffMethod { Secret, Passkey }

/// <summary>What the platform reports about Windows Hello. Mirrors the platform's availability values without depending on the platform.</summary>
public enum HelloAvailability { Available, NoDevice, NotConfigured, DisabledByPolicy, Busy, Unsupported }

/// <summary>What the turn-off prompt should do for the current method and Windows Hello state.</summary>
public enum SchoolOffRoute
{
    /// <summary>Ask for the PIN or password.</summary>
    Secret,
    /// <summary>Ask Windows Hello to verify the user.</summary>
    Passkey,
    /// <summary>Nothing can verify the user, so the mode turns off freely (it is a self-set lock, not security).</summary>
    Free
}

/// <summary>The route, and when the passkey could not be used, the plain reason and what happens instead (two parts so each can be translated).</summary>
public sealed record SchoolOffPlan(SchoolOffRoute Route, string? Notice, string? Fallback = null);

/// <summary>
/// Pure rules for the passkey choice. Only the choice is stored, never anything secret; whether the passkey can be used is decided
/// from the live platform state each time, and an unavailable passkey is reported plainly and falls back instead of locking the user out.
/// </summary>
public static class SchoolPasskeyRules
{
    public static string Describe(HelloAvailability state) => state switch
    {
        HelloAvailability.Available => "Windows Hello is available on this computer.",
        HelloAvailability.NoDevice => "This computer has no device that Windows Hello can use, such as a fingerprint reader or camera.",
        HelloAvailability.NotConfigured => "Windows Hello is not set up for this Windows account. Set it up in Windows Settings first.",
        HelloAvailability.DisabledByPolicy => "Windows Hello is turned off by a policy on this computer.",
        HelloAvailability.Busy => "Windows Hello is busy right now. Try again in a moment.",
        _ => "Windows Hello cannot be used here."
    };

    /// <summary>Null when the passkey may be chosen; otherwise the honest reason it may not.</summary>
    public static string? ChoiceProblem(HelloAvailability state) => state == HelloAvailability.Available ? null : Describe(state);

    public const string FallbackToSecret = "Enter the PIN or password instead.";
    public const string FallbackFree = "No PIN or password is set either, so the mode turns off without a check.";

    /// <summary>Decides what the turn-off prompt does. A passkey that cannot be used falls back to the secret, and to a free switch-off only when there is no secret.</summary>
    public static SchoolOffPlan Plan(SchoolOffMethod method, HelloAvailability hello, bool hasSecret)
    {
        if (method == SchoolOffMethod.Secret) return new SchoolOffPlan(hasSecret ? SchoolOffRoute.Secret : SchoolOffRoute.Free, null);
        if (hello == HelloAvailability.Available) return new SchoolOffPlan(SchoolOffRoute.Passkey, null);
        var reason = Describe(hello);
        return hasSecret
            ? new SchoolOffPlan(SchoolOffRoute.Secret, reason, FallbackToSecret)
            : new SchoolOffPlan(SchoolOffRoute.Free, reason, FallbackFree);
    }

    public static string ToText(SchoolOffMethod method) => method == SchoolOffMethod.Passkey ? "passkey" : "secret";
    public static SchoolOffMethod FromText(string? text) => string.Equals(text, "passkey", StringComparison.Ordinal) ? SchoolOffMethod.Passkey : SchoolOffMethod.Secret;
}

/// <summary>The controls School mode hides, so no surface decides locally. Hidden means absent, not disabled with a message naming the hidden thing.</summary>
public static class SchoolHiddenControls
{
    /// <summary>Settings rows for Cantonese, bilingual, funny levels and personal wording.</summary>
    public static readonly IReadOnlySet<string> SettingIds = new HashSet<string>(StringComparer.Ordinal) { "language", "funnyen", "funnyyue", "vocabulary" };

    /// <summary>Command palette commands that change the language.</summary>
    public static readonly IReadOnlySet<string> CommandIds = new HashSet<string>(StringComparer.Ordinal) { "set.language" };

    public static bool HidesSetting(string id, bool schoolMode) => schoolMode && SettingIds.Contains(id);
    public static bool HidesCommand(string id, bool schoolMode) => schoolMode && CommandIds.Contains(id);
}
