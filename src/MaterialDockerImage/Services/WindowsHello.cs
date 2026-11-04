using MaterialDockerImage.UI;
using Windows.Security.Credentials.UI;

namespace MaterialDockerImage.Services;

/// <summary>
/// Windows Hello user verification through the platform's own consent dialog. Nothing is stored here: the platform answers yes or no, and
/// asking is always a deliberate user action (turning School mode off, or choosing the passkey), never a background check.
/// </summary>
internal static class WindowsHello
{
    /// <summary>Reads only the availability value; this shows no prompt.</summary>
    public static async Task<HelloAvailability> CheckAsync()
    {
        try
        {
            return await UserConsentVerifier.CheckAvailabilityAsync() switch
            {
                UserConsentVerifierAvailability.Available => HelloAvailability.Available,
                UserConsentVerifierAvailability.DeviceNotPresent => HelloAvailability.NoDevice,
                UserConsentVerifierAvailability.NotConfiguredForUser => HelloAvailability.NotConfigured,
                UserConsentVerifierAvailability.DisabledByPolicy => HelloAvailability.DisabledByPolicy,
                UserConsentVerifierAvailability.DeviceBusy => HelloAvailability.Busy,
                _ => HelloAvailability.Unsupported
            };
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or PlatformNotSupportedException or TypeLoadException)
        {
            return HelloAvailability.Unsupported;
        }
    }

    /// <summary>Shows the Windows Hello dialog with the given message. True only when the platform verified the user.</summary>
    public static async Task<bool> VerifyAsync(string message)
    {
        try { return await UserConsentVerifier.RequestVerificationAsync(message) == UserConsentVerificationResult.Verified; }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or PlatformNotSupportedException or TypeLoadException) { return false; }
    }
}
