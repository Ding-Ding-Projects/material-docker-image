using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Shell;

/// <summary>
/// Shows the title-bar logo chosen in Settings: the default glyph, a bundled Material Symbol, or a validated local image.
/// Decoding is bounded (pixel limit checked from metadata before any pixels are read) and never leaves the machine.
/// </summary>
public static class AppLogo
{
    private static (string Path, DateTime Written, BitmapSource? Image)? cache;

    /// <summary>Decodes a stored logo with a pixel bound. Returns null and an error when the file is not a usable image.</summary>
    public static BitmapSource? TryLoad(string path, out string error)
    {
        error = "";
        try
        {
            var written = File.GetLastWriteTimeUtc(path);
            if (cache is { } hit && hit.Path == path && hit.Written == written && hit.Image != null) return hit.Image;
            // Metadata-only probe first: a small file can still declare enormous dimensions.
            BitmapFrame probe;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                probe = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None).Frames[0];
            if (probe.PixelWidth <= 0 || probe.PixelHeight <= 0 || probe.PixelWidth > AppIdentity.MaxLogoPixels || probe.PixelHeight > AppIdentity.MaxLogoPixels)
            {
                error = "The image dimensions are outside the supported range.";
                return null;
            }
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.DecodePixelWidth = Math.Min(probe.PixelWidth, 128);
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            cache = (path, written, image);
            return image;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or ArgumentException or FileFormatException or System.Runtime.InteropServices.COMException)
        {
            error = "The image could not be decoded.";
            return null;
        }
    }

    /// <summary>Applies the saved choice to a glyph block and an image element. Returns a notice when the default had to be shown instead.</summary>
    public static string Apply(TextBlock glyph, Image image, UserSettings settings, double size)
    {
        var resolved = AppIdentity.Resolve(settings.LogoKind, settings.LogoSymbol, settings.LogoImageFile, UserSettings.DataDirectory);
        var notice = resolved.Notice;
        BitmapSource? bitmap = null;
        if (resolved.ImagePath != null)
        {
            bitmap = TryLoad(resolved.ImagePath, out _);
            if (bitmap == null) { resolved = AppIdentity.Resolve(LogoKinds.Default, "", "", UserSettings.DataDirectory); notice = "The custom logo file is missing or invalid, so the default logo is shown."; }
        }
        glyph.Text = resolved.Glyph;
        glyph.FontSize = size;
        glyph.Visibility = bitmap == null ? Visibility.Visible : Visibility.Collapsed;
        image.Source = bitmap;
        image.Width = image.Height = size;
        image.Visibility = bitmap == null ? Visibility.Collapsed : Visibility.Visible;
        return notice;
    }

    /// <summary>A small self-contained preview of the saved choice, for the Settings page.</summary>
    public static FrameworkElement CreatePreview(UserSettings settings, double size = 32)
    {
        var glyph = new TextBlock { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFontFilled");
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "Primary");
        var image = new Image { Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        Apply(glyph, image, settings, size);
        var grid = new Grid { Width = size + 8, Height = size + 8 };
        grid.Children.Add(glyph);
        grid.Children.Add(image);
        return grid;
    }
}
