using System;
using System.Globalization;

namespace MaterialDockerImage.UI;

/// <summary>What the "From wallpaper" preset found when it looked at the Windows desktop background.</summary>
public enum WallpaperSource
{
    /// <summary>A picture was read and a seed colour was taken from it.</summary>
    Picture,
    /// <summary>No picture is set; the solid desktop background colour is the seed.</summary>
    SolidColour,
    /// <summary>No picture and no readable background colour.</summary>
    Nothing,
    /// <summary>A picture is set but the file is missing, too large or not an image this app can decode.</summary>
    Unreadable
}

/// <summary>The outcome of reading the wallpaper: where the colour came from, the seed (when there is one) and a short reason.</summary>
public sealed record WallpaperColourResult(WallpaperSource Source, string? Seed, string Detail);

/// <summary>
/// Finds a seed colour in wallpaper pixels without any imaging framework, so it can be checked on synthetic pixel arrays.
/// Pixels are grouped into 4,096 buckets (4 bits per channel); each bucket scores its pixel count weighted towards
/// colourful pixels, so a vivid area beats a large grey one, and the seed is the average colour of the winning bucket.
/// Mostly transparent pixels are ignored. Nothing here reads files or the registry.
/// </summary>
public static class AppearanceWallpaperColour
{
    public const int MaxPixels = 1 << 20;

    /// <summary>The seed in #RRGGBB form from 32-bit BGRA pixels, or null when there is no opaque pixel.</summary>
    public static string? SeedFromBgra(ReadOnlySpan<byte> bgra, int width, int height, int stride)
    {
        if (width <= 0 || height <= 0 || stride < width * 4 || (long)width * height > MaxPixels || bgra.Length < (long)stride * (height - 1) + width * 4) return null;
        var count = new int[4096];
        var sumR = new long[4096]; var sumG = new long[4096]; var sumB = new long[4096];
        var chroma = new double[4096];
        for (var y = 0; y < height; y++)
        {
            var row = bgra.Slice(y * stride, width * 4);
            for (var x = 0; x < width; x++)
            {
                byte b = row[x * 4], g = row[x * 4 + 1], r = row[x * 4 + 2], a = row[x * 4 + 3];
                if (a < 128) continue;
                var bucket = (r >> 4) << 8 | (g >> 4) << 4 | b >> 4;
                count[bucket]++; sumR[bucket] += r; sumG[bucket] += g; sumB[bucket] += b;
                chroma[bucket] += (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b))) / 255.0;
            }
        }
        var best = -1; var bestScore = 0.0;
        for (var i = 0; i < count.Length; i++)
        {
            if (count[i] == 0) continue;
            var score = count[i] * (0.15 + chroma[i] / count[i]);
            if (score > bestScore) { bestScore = score; best = i; }
        }
        if (best < 0) return null;
        var n = count[best];
        return Hex((int)Math.Round(sumR[best] / (double)n), (int)Math.Round(sumG[best] / (double)n), (int)Math.Round(sumB[best] / (double)n));
    }

    /// <summary>Parses the Windows background colour value ("R G B", each 0 to 255) into #RRGGBB.</summary>
    public static bool TryParseRegistryColour(string? text, out string hex)
    {
        hex = "";
        var parts = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) return false;
        var channels = new int[3];
        for (var i = 0; i < 3; i++)
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out channels[i]) || channels[i] > 255) return false;
        hex = Hex(channels[0], channels[1], channels[2]);
        return true;
    }

    /// <summary>
    /// Decides the result from what Windows reported, given an already decoded picture seed. A set picture that could not be
    /// decoded is Unreadable even when a background colour exists, so the person learns the picture was not used.
    /// </summary>
    public static WallpaperColourResult Decide(string? picturePath, bool pictureReadable, string? pictureSeed, string? backgroundColour)
    {
        var hasPicture = !string.IsNullOrWhiteSpace(picturePath);
        if (hasPicture && pictureReadable && pictureSeed != null) return new(WallpaperSource.Picture, pictureSeed, "Taken from the desktop picture.");
        if (hasPicture) return new(WallpaperSource.Unreadable, null, "The desktop picture could not be read, so no colour was taken from it.");
        if (TryParseRegistryColour(backgroundColour, out var solid)) return new(WallpaperSource.SolidColour, solid, "No desktop picture is set, so the solid background colour is used.");
        return new(WallpaperSource.Nothing, null, "No desktop picture or background colour was found.");
    }

    private static string Hex(int r, int g, int b) => $"#{Math.Clamp(r, 0, 255):X2}{Math.Clamp(g, 0, 255):X2}{Math.Clamp(b, 0, 255):X2}";
}
