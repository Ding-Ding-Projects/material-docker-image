using System.IO;
using System.Security.Cryptography;
using System.Text;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Services;

/// <summary>
/// Keeps the Home Assistant long-lived access token encrypted with Windows DPAPI for the current user, in its own file in the data folder.
/// The token is never written to settings.json, never exported with settings, never logged and never shown again after it is stored.
/// A copy of the file on another computer or user account cannot be decrypted.
/// </summary>
public static class HomeAssistantToken
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MaterialDockerImage.HomeAssistant.Token.v1");
    private static string FilePath => Path.Combine(UserSettings.DataDirectory, "ha-token.bin");

    public static bool Exists => File.Exists(FilePath);

    /// <summary>Encrypts and stores the token. Returns false with a short reason when it is not a plausible token or cannot be written.</summary>
    public static bool Save(string? token, out string error)
    {
        error = "";
        token = token?.Trim();
        if (!ScheduleSourceRules.IsValidToken(token)) { error = "That does not look like a Home Assistant token. It must be printable ASCII without spaces, up to 512 characters."; return false; }
        try
        {
            Directory.CreateDirectory(UserSettings.DataDirectory);
            var protectedBytes = ProtectedData.Protect(Encoding.ASCII.GetBytes(token!), Entropy, DataProtectionScope.CurrentUser);
            var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporary, protectedBytes);
            File.Move(temporary, FilePath, true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or PlatformNotSupportedException)
        {
            error = "The token could not be stored securely on this computer.";
            return false;
        }
    }

    /// <summary>The decrypted token, or null when none is stored or it cannot be decrypted by this user.</summary>
    public static string? Load()
    {
        try
        {
            if (!File.Exists(FilePath) || new FileInfo(FilePath).Length > 4096) return null;
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser);
            var token = Encoding.ASCII.GetString(plain);
            return ScheduleSourceRules.IsValidToken(token) ? token : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or PlatformNotSupportedException) { return null; }
    }

    public static void Clear()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
