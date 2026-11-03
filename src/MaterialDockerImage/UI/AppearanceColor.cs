using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>
/// A colour with channels in the 0 to 1 range (sRGB, straight alpha). Pure logic with no WPF dependency so the
/// conversions can be tested: HEX/HEX8, RGB, HSL, HSV, HWB, CMYK, OKLab and OKLCH, WCAG contrast and gamut checks.
/// </summary>
public readonly record struct AppearanceRgba(double R, double G, double B, double A = 1)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public byte R8 => ToByte(R);
    public byte G8 => ToByte(G);
    public byte B8 => ToByte(B);
    public byte A8 => ToByte(A);
    private static byte ToByte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
    private static double Clamp01(double v) => double.IsFinite(v) ? Math.Clamp(v, 0, 1) : 0;

    public static AppearanceRgba FromBytes(byte r, byte g, byte b, byte a = 255) => new(r / 255.0, g / 255.0, b / 255.0, a / 255.0);
    public AppearanceRgba Clamped() => new(Clamp01(R), Clamp01(G), Clamp01(B), Clamp01(A));
    public AppearanceRgba WithAlpha(double alpha) => this with { A = Clamp01(alpha) };
    /// <summary>The colour quantised to 8 bits per channel, as it is stored and displayed.</summary>
    public AppearanceRgba Quantized() => FromBytes(R8, G8, B8, A8);

    // ----- Hex -----

    /// <summary>#RRGGBB (alpha ignored).</summary>
    public string ToHex() => $"#{R8:X2}{G8:X2}{B8:X2}";
    /// <summary>CSS order #RRGGBBAA.</summary>
    public string ToHex8() => $"#{R8:X2}{G8:X2}{B8:X2}{A8:X2}";
    /// <summary>Windows order: #RRGGBB when opaque, otherwise #AARRGGBB. This is the form saved in settings.</summary>
    public string ToStorageHex() => A8 == 255 ? ToHex() : $"#{A8:X2}{R8:X2}{G8:X2}{B8:X2}";

    /// <summary>Parses CSS hex: #RGB, #RGBA, #RRGGBB or #RRGGBBAA (alpha last).</summary>
    public static bool TryParseHex(string? text, out AppearanceRgba color) => TryParseHexCore(text, false, out color);
    /// <summary>Parses the settings form: #RGB, #RRGGBB or #AARRGGBB (alpha first, as Windows writes it).</summary>
    public static bool TryParseStorageHex(string? text, out AppearanceRgba color) => TryParseHexCore(text, true, out color);

    private static bool TryParseHexCore(string? text, bool alphaFirst, out AppearanceRgba color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (s.StartsWith('#')) s = s[1..];
        if (s.Length is not (3 or 4 or 6 or 8) || !Regex.IsMatch(s, "^[0-9A-Fa-f]+$")) return false;
        if (s.Length is 3 or 4) s = string.Concat(s.Select(c => new string(c, 2)));
        byte P(int i) => byte.Parse(s.Substring(i, 2), NumberStyles.HexNumber, Inv);
        if (s.Length == 6) { color = FromBytes(P(0), P(2), P(4)); return true; }
        color = alphaFirst ? FromBytes(P(2), P(4), P(6), P(0)) : FromBytes(P(0), P(2), P(4), P(6));
        return true;
    }

    // ----- HSL / HSV / HWB -----

    public (double H, double S, double L) ToHsl()
    {
        double max = Math.Max(R, Math.Max(G, B)), min = Math.Min(R, Math.Min(G, B)), l = (max + min) / 2, d = max - min;
        if (d < 1e-12) return (0, 0, l);
        var s = l > .5 ? d / (2 - max - min) : d / (max + min);
        return (Hue(max, d), s, l);
    }

    public static AppearanceRgba FromHsl(double h, double s, double l, double a = 1)
    {
        h = Wrap(h) / 360; s = Clamp01(s); l = Clamp01(l);
        if (s == 0) return new(l, l, l, Clamp01(a));
        var q = l < .5 ? l * (1 + s) : l + s - l * s; var p = 2 * l - q;
        double C(double t) { t = t < 0 ? t + 1 : t > 1 ? t - 1 : t; return t < 1.0 / 6 ? p + (q - p) * 6 * t : t < .5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p; }
        return new(C(h + 1.0 / 3), C(h), C(h - 1.0 / 3), Clamp01(a));
    }

    public (double H, double S, double V) ToHsv()
    {
        double max = Math.Max(R, Math.Max(G, B)), min = Math.Min(R, Math.Min(G, B)), d = max - min;
        return (d < 1e-12 ? 0 : Hue(max, d), max <= 0 ? 0 : d / max, max);
    }

    public static AppearanceRgba FromHsv(double h, double s, double v, double a = 1)
    {
        h = Wrap(h); s = Clamp01(s); v = Clamp01(v);
        var c = v * s; var x = c * (1 - Math.Abs(h / 60 % 2 - 1)); var m = v - c;
        var (r, g, b) = h switch { < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x), < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x) };
        return new(r + m, g + m, b + m, Clamp01(a));
    }

    public (double H, double W, double B) ToHwb()
    {
        var (h, _, _) = ToHsv();
        return (h, Math.Min(R, Math.Min(G, B)), 1 - Math.Max(R, Math.Max(G, B)));
    }

    public static AppearanceRgba FromHwb(double h, double w, double b, double a = 1)
    {
        w = Clamp01(w); b = Clamp01(b);
        if (w + b >= 1) { var grey = w / (w + b); return new(grey, grey, grey, Clamp01(a)); }
        var v = 1 - b;
        return FromHsv(h, 1 - w / v, v, a);
    }

    private double Hue(double max, double d)
    {
        double h = max == R ? (G - B) / d + (G < B ? 6 : 0) : max == G ? (B - R) / d + 2 : (R - G) / d + 4;
        return Wrap(h * 60);
    }
    public static double Wrap(double h) => double.IsFinite(h) ? (h % 360 + 360) % 360 : 0;

    // ----- CMYK (naive device-independent conversion) -----

    public (double C, double M, double Y, double K) ToCmyk()
    {
        var k = 1 - Math.Max(R, Math.Max(G, B));
        if (k >= 1 - 1e-12) return (0, 0, 0, 1);
        return ((1 - R - k) / (1 - k), (1 - G - k) / (1 - k), (1 - B - k) / (1 - k), k);
    }
    public static AppearanceRgba FromCmyk(double c, double m, double y, double k, double a = 1)
    {
        c = Clamp01(c); m = Clamp01(m); y = Clamp01(y); k = Clamp01(k);
        return new((1 - c) * (1 - k), (1 - m) * (1 - k), (1 - y) * (1 - k), Clamp01(a));
    }

    // ----- OKLab / OKLCH -----

    private static double ToLinear(double x) => x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
    private static double FromLinear(double x) => x <= 0.0031308 ? 12.92 * x : 1.055 * Math.Pow(x, 1 / 2.4) - 0.055;

    public (double L, double A, double B) ToOklab()
    {
        double r = ToLinear(R), g = ToLinear(G), b = ToLinear(B);
        var l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        var m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        var s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        return (0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
                1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
                0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }

    /// <summary>Unclamped linear conversion; channels outside 0..1 mean the colour is outside sRGB.</summary>
    public static AppearanceRgba FromOklabUnclamped(double L, double a, double b, double alpha = 1)
    {
        var l = Math.Pow(L + 0.3963377774 * a + 0.2158037573 * b, 3);
        var m = Math.Pow(L - 0.1055613458 * a - 0.0638541728 * b, 3);
        var s = Math.Pow(L - 0.0894841775 * a - 1.2914855480 * b, 3);
        double r = 4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s;
        double g = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
        double bl = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s;
        static double F(double x) => x < 0 ? -FromLinear(-x) : FromLinear(x);
        return new(F(r), F(g), F(bl), alpha);
    }

    public bool InGamut(double tolerance = 0.0005) => R >= -tolerance && R <= 1 + tolerance && G >= -tolerance && G <= 1 + tolerance && B >= -tolerance && B <= 1 + tolerance;

    public (double L, double C, double H) ToOklch()
    {
        var (l, a, b) = ToOklab();
        var c = Math.Sqrt(a * a + b * b);
        return (l, c, c < 1e-7 ? 0 : Wrap(Math.Atan2(b, a) * 180 / Math.PI));
    }

    /// <summary>
    /// Converts OKLCH to sRGB. When the colour is outside sRGB, chroma is reduced (keeping lightness and hue) until it
    /// fits, and <paramref name="inGamut"/> reports false so the editor can warn.
    /// </summary>
    public static AppearanceRgba FromOklch(double L, double C, double H, out bool inGamut, double alpha = 1)
    {
        L = Clamp01(L); C = double.IsFinite(C) ? Math.Max(0, C) : 0; var rad = Wrap(H) * Math.PI / 180;
        var direct = FromOklabUnclamped(L, C * Math.Cos(rad), C * Math.Sin(rad), Clamp01(alpha));
        inGamut = direct.InGamut();
        if (inGamut) return direct.Clamped();
        double lo = 0, hi = C;
        for (var i = 0; i < 32; i++)
        {
            var mid = (lo + hi) / 2;
            if (FromOklabUnclamped(L, mid * Math.Cos(rad), mid * Math.Sin(rad)).InGamut()) lo = mid; else hi = mid;
        }
        return FromOklabUnclamped(L, lo * Math.Cos(rad), lo * Math.Sin(rad), Clamp01(alpha)).Clamped();
    }

    // ----- Contrast -----

    public double RelativeLuminance() => 0.2126 * ToLinear(Clamp01(R)) + 0.7152 * ToLinear(Clamp01(G)) + 0.0722 * ToLinear(Clamp01(B));

    /// <summary>WCAG 2 contrast ratio between two opaque colours (1 to 21).</summary>
    public static double ContrastRatio(AppearanceRgba a, AppearanceRgba b)
    {
        var (l1, l2) = (a.RelativeLuminance(), b.RelativeLuminance());
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    /// <summary>Composites this colour over an opaque backdrop (used before measuring contrast of translucent colours).</summary>
    public AppearanceRgba Over(AppearanceRgba backdrop) => new(R * A + backdrop.R * (1 - A), G * A + backdrop.G * (1 - A), B * A + backdrop.B * (1 - A), 1);

    public AppearanceRgba Mix(AppearanceRgba other, double amount)
    {
        amount = Clamp01(amount);
        return new(R + (other.R - R) * amount, G + (other.G - G) * amount, B + (other.B - B) * amount, A + (other.A - A) * amount);
    }

    // ----- Formatting and parsing of every format -----

    private static string N(double v, int decimals) => Math.Round(v, decimals).ToString("0." + new string('#', Math.Max(1, decimals)), Inv);
    private string AlphaSuffix(bool comma) => A8 == 255 ? "" : comma ? ", " + N(A, 3) : " / " + N(A, 3);

    public string Format(AppearanceColorFormat format)
    {
        switch (format)
        {
            case AppearanceColorFormat.Hex: return ToHex();
            case AppearanceColorFormat.Hex8: return ToHex8();
            case AppearanceColorFormat.Rgb: return A8 == 255 ? $"rgb({R8}, {G8}, {B8})" : $"rgba({R8}, {G8}, {B8}, {N(A, 3)})";
            case AppearanceColorFormat.Hsl: { var (h, s, l) = ToHsl(); return $"hsl({N(h, 1)}, {N(s * 100, 1)}%, {N(l * 100, 1)}%{AlphaSuffix(true)})"; }
            case AppearanceColorFormat.Hsv: { var (h, s, v) = ToHsv(); return $"hsv({N(h, 1)}, {N(s * 100, 1)}%, {N(v * 100, 1)}%{AlphaSuffix(true)})"; }
            case AppearanceColorFormat.Hwb: { var (h, w, b) = ToHwb(); return $"hwb({N(h, 1)} {N(w * 100, 1)}% {N(b * 100, 1)}%{AlphaSuffix(false)})"; }
            case AppearanceColorFormat.Oklch: { var (l, c, h) = ToOklch(); return $"oklch({N(l * 100, 2)}% {N(c, 4)} {N(h, 2)}{AlphaSuffix(false)})"; }
            case AppearanceColorFormat.Cmyk: { var (c, m, y, k) = ToCmyk(); return $"cmyk({N(c * 100, 1)}%, {N(m * 100, 1)}%, {N(y * 100, 1)}%, {N(k * 100, 1)}%)"; }
            default: return ToHex();
        }
    }

    private static readonly Regex Function = new(@"^\s*(rgba?|hsla?|hsva?|hwb|oklch|cmyk)\s*\(\s*([^)]*)\)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Number = new(@"^([+-]?(?:\d+\.?\d*|\.\d+)(?:e[+-]?\d+)?)(%|deg)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Parses any supported notation. <paramref name="warning"/> is set when the value was adjusted (for example an
    /// OKLCH colour outside sRGB). Hex uses CSS order (#RRGGBBAA).
    /// </summary>
    public static bool TryParse(string? text, out AppearanceRgba color, out string? warning)
    {
        color = default; warning = null;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 200) return false;
        if (TryParseHex(text, out color)) return true;
        var match = Function.Match(text);
        if (!match.Success) return false;
        var kind = match.Groups[1].Value.ToLowerInvariant().TrimEnd('a');
        var parts = Regex.Split(match.Groups[2].Value.Trim(), @"\s*[,/]\s*|\s+").Where(p => p.Length > 0).ToArray();
        var values = new List<(double Value, bool Percent)>();
        foreach (var part in parts)
        {
            var m = Number.Match(part);
            if (!m.Success || !double.TryParse(m.Groups[1].Value, NumberStyles.Float, Inv, out var v) || !double.IsFinite(v)) return false;
            values.Add((v, m.Groups[2].Value == "%"));
        }
        double Unit(int i) => values[i].Percent ? values[i].Value / 100 : values[i].Value;
        double Alpha(int i) => values.Count > i ? Unit(i) : 1;
        bool InRange(double v, double min, double max) => v >= min - 1e-9 && v <= max + 1e-9;
        switch (kind)
        {
            case "rgb":
                if (values.Count is not (3 or 4)) return false;
                double Ch(int i) => values[i].Percent ? values[i].Value / 100 : values[i].Value / 255;
                if (!Enumerable.Range(0, 3).All(i => InRange(Ch(i), 0, 1)) || !InRange(Alpha(3), 0, 1)) return false;
                color = new(Ch(0), Ch(1), Ch(2), Alpha(3)); return true;
            case "hsl":
            case "hsv":
                if (values.Count is not (3 or 4)) return false;
                double S1 = values[1].Percent ? values[1].Value / 100 : values[1].Value > 1 ? values[1].Value / 100 : values[1].Value;
                double S2 = values[2].Percent ? values[2].Value / 100 : values[2].Value > 1 ? values[2].Value / 100 : values[2].Value;
                if (!InRange(S1, 0, 1) || !InRange(S2, 0, 1) || !InRange(Alpha(3), 0, 1)) return false;
                color = kind == "hsl" ? FromHsl(values[0].Value, S1, S2, Alpha(3)) : FromHsv(values[0].Value, S1, S2, Alpha(3)); return true;
            case "hwb":
                if (values.Count is not (3 or 4)) return false;
                double W = values[1].Percent ? values[1].Value / 100 : values[1].Value > 1 ? values[1].Value / 100 : values[1].Value;
                double Bk = values[2].Percent ? values[2].Value / 100 : values[2].Value > 1 ? values[2].Value / 100 : values[2].Value;
                if (!InRange(W, 0, 1) || !InRange(Bk, 0, 1) || !InRange(Alpha(3), 0, 1)) return false;
                color = FromHwb(values[0].Value, W, Bk, Alpha(3)); return true;
            case "oklch":
                if (values.Count is not (3 or 4)) return false;
                var L = values[0].Percent ? values[0].Value / 100 : values[0].Value > 1 ? values[0].Value / 100 : values[0].Value;
                var C = values[1].Percent ? values[1].Value / 100 * 0.4 : values[1].Value;
                if (!InRange(L, 0, 1) || C < 0 || C > 0.5 || !InRange(Alpha(3), 0, 1)) return false;
                color = FromOklch(L, C, values[2].Value, out var fits, Alpha(3));
                if (!fits) warning = "This OKLCH colour is outside the sRGB range the screen can show. It was mapped to the nearest displayable colour by lowering chroma.";
                return true;
            case "cmyk":
                if (values.Count != 4) return false;
                double P(int i) => values[i].Percent ? values[i].Value / 100 : values[i].Value > 1 ? values[i].Value / 100 : values[i].Value;
                if (!Enumerable.Range(0, 4).All(i => InRange(P(i), 0, 1))) return false;
                color = FromCmyk(P(0), P(1), P(2), P(3)); return true;
        }
        return false;
    }

    /// <summary>Largest channel difference between two colours, including alpha.</summary>
    public static double Distance(AppearanceRgba a, AppearanceRgba b) => Math.Max(Math.Max(Math.Abs(a.R - b.R), Math.Abs(a.G - b.G)), Math.Max(Math.Abs(a.B - b.B), Math.Abs(a.A - b.A)));
}

public enum AppearanceColorFormat { Hex, Hex8, Rgb, Hsl, Hsv, Hwb, Oklch, Cmyk }
