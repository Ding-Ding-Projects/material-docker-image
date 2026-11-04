using System;
using System.IO;
using ZXing;
using ZXing.Common;

namespace MaterialDockerImage.UI;

/// <summary>
/// Turns a picture of a QR code into an authenticator account. The picture is decoded in this process, the text goes through the same
/// <see cref="OtpauthUri"/> parser as a pasted link, and neither the picture nor the secret is written anywhere.
/// </summary>
public static class OtpQrImport
{
    public const long MaxFileBytes = 20L * 1024 * 1024;
    public const long MaxPixels = 40_000_000;
    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff"];

    /// <summary>Null when the file may be opened as a picture; otherwise why not.</summary>
    public static string? FileProblem(string? path, long length)
    {
        if (string.IsNullOrWhiteSpace(path)) return "Choose a picture file.";
        if (Array.IndexOf(Extensions, Path.GetExtension(path).ToLowerInvariant()) < 0) return "Choose a PNG, JPEG, BMP, GIF or TIFF picture.";
        if (length > MaxFileBytes) return "That picture is too large. Use one under 20 MB.";
        if (length <= 0) return "That picture is empty.";
        return null;
    }

    /// <summary>Reads the QR code in a BGRA pixel buffer, also trying inverted (light-on-dark) codes.</summary>
    public static bool TryDecode(byte[] bgra, int width, int height, out string text, out string error)
    {
        text = ""; error = "";
        if (width <= 0 || height <= 0 || (long)width * height > MaxPixels || bgra.Length < (long)width * height * 4) { error = "That picture cannot be read."; return false; }
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions { TryHarder = true, TryInverted = true, PossibleFormats = [BarcodeFormat.QR_CODE] }
        };
        var result = reader.Decode(new RGBLuminanceSource(bgra, width, height, RGBLuminanceSource.BitmapFormat.BGRA32));
        if (result == null || string.IsNullOrEmpty(result.Text)) { error = "No QR code was found in that picture."; return false; }
        text = result.Text;
        return true;
    }

    /// <summary>Checks that the text a QR code carried is an authenticator link and parses it with the shared parser.</summary>
    public static bool TryAccount(string text, out OtpAccount? account, out string error)
    {
        account = null;
        var trimmed = text.Trim();
        if (trimmed.StartsWith("otpauth-migration:", StringComparison.OrdinalIgnoreCase))
        {
            error = "This is an export code for several accounts, which this authenticator cannot read. Show one account's own QR code instead.";
            return false;
        }
        if (!trimmed.StartsWith("otpauth:", StringComparison.OrdinalIgnoreCase)) { error = "That QR code is not an authenticator link."; return false; }
        return OtpauthUri.TryParse(trimmed, out account, out error);
    }

    /// <summary>The whole route for a pixel buffer: find the QR code, then build the account.</summary>
    public static bool TryFromPixels(byte[] bgra, int width, int height, out OtpAccount? account, out string error)
    {
        account = null;
        return TryDecode(bgra, width, height, out var text, out error) && TryAccount(text, out account, out error);
    }
}
