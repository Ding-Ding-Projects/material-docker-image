using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage.Services;

/// <summary>
/// Reads the current Windows desktop background for the "From wallpaper" preset. Read-only: it asks Windows for the
/// wallpaper path (SystemParametersInfo, then the HKCU\Control Panel\Desktop\WallPaper value), decodes the picture at a
/// small size and hands the pixels to <see cref="AppearanceWallpaperColour"/>. With no picture it reads the solid
/// background colour (HKCU\Control Panel\Colors\Background). It never writes to the registry or to the file.
/// </summary>
public static class WallpaperColourReader
{
    private const uint SpiGetDeskWallpaper = 0x0073;
    private const long MaxFileBytes = 64L * 1024 * 1024;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, StringBuilder value, uint winIni);

    /// <summary>Reads the wallpaper off the calling thread and returns what was found. Never throws.</summary>
    public static Task<WallpaperColourResult> ReadAsync() => Task.Run(Read);

    public static WallpaperColourResult Read()
    {
        var path = PicturePath();
        string? seed = null;
        var readable = false;
        if (!string.IsNullOrWhiteSpace(path))
        {
            try { seed = SeedFromFile(path); readable = seed != null; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException or FileFormatException or COMException) { readable = false; }
        }
        return AppearanceWallpaperColour.Decide(path, readable, seed, BackgroundColour());
    }

    private static string? PicturePath()
    {
        try
        {
            var buffer = new StringBuilder(1024);
            if (SystemParametersInfo(SpiGetDeskWallpaper, (uint)buffer.Capacity, buffer, 0) && buffer.Length > 0) return buffer.ToString();
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException) { }
        return ReadRegistry(@"HKEY_CURRENT_USER\Control Panel\Desktop", "WallPaper");
    }

    private static string? BackgroundColour() => ReadRegistry(@"HKEY_CURRENT_USER\Control Panel\Colors", "Background");

    private static string? ReadRegistry(string key, string name)
    {
        try { return Registry.GetValue(key, name, null) as string; }
        catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Decodes the picture to at most 96 pixels wide and returns its seed, or null when it has no opaque pixel.</summary>
    private static string? SeedFromFile(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length == 0 || info.Length > MaxFileBytes) return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.DecodePixelWidth = 96;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var width = bgra.PixelWidth; var height = bgra.PixelHeight;
        if (width <= 0 || height <= 0 || (long)width * height > AppearanceWallpaperColour.MaxPixels) return null;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        bgra.CopyPixels(pixels, stride, 0);
        return AppearanceWallpaperColour.SeedFromBgra(pixels, width, height, stride);
    }
}
