using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace MaterialDockerImage.Services;

/// <summary>Applies Material 3 colour roles to the application resources.</summary>
public static class Theme
{
    private static readonly Dictionary<string, string> Dark = new()
    {
        ["Surface"] = "#111318", ["SurfaceLowest"] = "#0C0E13", ["SurfaceLow"] = "#191C20", ["SurfaceContainer"] = "#1D2024",
        ["SurfaceHigh"] = "#282A2F", ["SurfaceHighest"] = "#33353A", ["OnSurface"] = "#E2E2E9", ["OnSurfaceVariant"] = "#C4C6D0",
        ["Outline"] = "#8E9099", ["OutlineVariant"] = "#44474F", ["Primary"] = "#ADC6FF", ["OnPrimary"] = "#002E69",
        ["PrimaryContainer"] = "#004494", ["OnPrimaryContainer"] = "#D8E2FF", ["SecondaryContainer"] = "#3E4759", ["OnSecondaryContainer"] = "#DAE2F9",
        ["Tertiary"] = "#82D3E0", ["TertiaryContainer"] = "#004F58", ["OnTertiaryContainer"] = "#A2EEFB",
        ["Success"] = "#7FD99A", ["SuccessContainer"] = "#005227", ["OnSuccessContainer"] = "#9CF6B4",
        ["Warning"] = "#F9BD5C", ["WarningContainer"] = "#5C3F00", ["OnWarningContainer"] = "#FFDDB3",
        ["Error"] = "#FFB4AB", ["OnError"] = "#690005", ["ErrorContainer"] = "#93000A", ["OnErrorContainer"] = "#FFDAD6",
        ["Scrim"] = "#99000000", ["HoverLayer"] = "#14E2E2E9", ["PressLayer"] = "#24E2E2E9"
    };
    private static readonly Dictionary<string, string> Light = new()
    {
        ["Surface"] = "#F9F9FF", ["SurfaceLowest"] = "#FFFFFF", ["SurfaceLow"] = "#F3F3FA", ["SurfaceContainer"] = "#EDEDF4",
        ["SurfaceHigh"] = "#E8E7EF", ["SurfaceHighest"] = "#E2E2E9", ["OnSurface"] = "#1A1B20", ["OnSurfaceVariant"] = "#44474F",
        ["Outline"] = "#74777F", ["OutlineVariant"] = "#C4C6D0", ["Primary"] = "#2B5BB5", ["OnPrimary"] = "#FFFFFF",
        ["PrimaryContainer"] = "#D8E2FF", ["OnPrimaryContainer"] = "#001A41", ["SecondaryContainer"] = "#DAE2F9", ["OnSecondaryContainer"] = "#131C2B",
        ["Tertiary"] = "#00687A", ["TertiaryContainer"] = "#A2EEFB", ["OnTertiaryContainer"] = "#001F25",
        ["Success"] = "#1B6D3A", ["SuccessContainer"] = "#A4F4B6", ["OnSuccessContainer"] = "#00210B",
        ["Warning"] = "#7C5800", ["WarningContainer"] = "#FFDEA9", ["OnWarningContainer"] = "#271900",
        ["Error"] = "#BA1A1A", ["OnError"] = "#FFFFFF", ["ErrorContainer"] = "#FFDAD6", ["OnErrorContainer"] = "#410002",
        ["Scrim"] = "#66000000", ["HoverLayer"] = "#141A1B20", ["PressLayer"] = "#241A1B20"
    };
    private static readonly string[] SeedFollowers = ["Primary", "OnPrimary", "PrimaryContainer", "OnPrimaryContainer", "SecondaryContainer", "OnSecondaryContainer"];
    public static bool IsDark { get; private set; } = true;

    public static bool SystemPrefersDark()
    {
        try { return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int value && value == 0; }
        catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException) { return true; }
    }

    public static void Apply(ResourceDictionary resources, string mode, string seed, bool highContrast)
    {
        IsDark = mode switch { "Light" => false, "System" => SystemPrefersDark(), _ => true };
        var palette = IsDark ? Dark : Light;
        double? seedHue = TryParse(seed, out var seedColor) ? Hsl(seedColor).H : null;
        foreach (var pair in palette)
        {
            var color = (Color)ColorConverter.ConvertFromString(pair.Value);
            if (seedHue is { } hue && Array.IndexOf(SeedFollowers, pair.Key) >= 0) color = WithHue(color, hue);
            if (highContrast) color = Contrast(pair.Key, color);
            var brush = new SolidColorBrush(color); brush.Freeze();
            resources[pair.Key] = brush;
        }
    }

    private static Color Contrast(string role, Color color) => role switch
    {
        "OnSurfaceVariant" or "Outline" => IsDark ? Colors.White : Colors.Black,
        "OutlineVariant" => IsDark ? Color.FromRgb(0xC4, 0xC6, 0xD0) : Color.FromRgb(0x44, 0x47, 0x4F),
        "Surface" or "SurfaceLow" or "SurfaceLowest" => IsDark ? Colors.Black : Colors.White,
        _ => color
    };

    public static bool TryParse(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        try { color = (Color)ColorConverter.ConvertFromString(text.Trim()); return true; }
        catch (FormatException) { return false; }
    }

    public static (double H, double S, double L) Hsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2, d = max - min;
        if (d == 0) return (0, 0, l);
        var s = l > .5 ? d / (2 - max - min) : d / (max + min);
        double h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60, s, l);
    }

    public static Color FromHsl(double h, double s, double l, byte alpha = 255)
    {
        h = ((h % 360) + 360) % 360 / 360;
        double Channel(double t) { t = t < 0 ? t + 1 : t > 1 ? t - 1 : t; var q = l < .5 ? l * (1 + s) : l + s - l * s; var p = 2 * l - q; return t < 1.0 / 6 ? p + (q - p) * 6 * t : t < .5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p; }
        if (s == 0) { var v = (byte)Math.Round(l * 255); return Color.FromArgb(alpha, v, v, v); }
        return Color.FromArgb(alpha, (byte)Math.Round(Channel(h + 1.0 / 3) * 255), (byte)Math.Round(Channel(h) * 255), (byte)Math.Round(Channel(h - 1.0 / 3) * 255));
    }

    private static Color WithHue(Color c, double hue) { var (_, s, l) = Hsl(c); return FromHsl(hue, s, l, c.A); }

    public static string Hex(Color c) => c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>WCAG relative-luminance contrast ratio.</summary>
    public static double ContrastRatio(Color a, Color b)
    {
        static double Lum(Color c) { double F(byte v) { var x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); } return 0.2126 * F(c.R) + 0.7152 * F(c.G) + 0.0722 * F(c.B); }
        var (l1, l2) = (Lum(a), Lum(b)); return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }
}
