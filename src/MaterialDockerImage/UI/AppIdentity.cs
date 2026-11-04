using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MaterialDockerImage.UI;

/// <summary>The three ways the title-bar logo can be chosen.</summary>
public static class LogoKinds
{
    public const string Default = "Default";
    public const string Symbol = "Symbol";
    public const string Image = "Image";
    public static readonly IReadOnlyList<string> All = new[] { Default, Symbol, Image };
}

/// <summary>Result of validating a user-chosen image: the detected format, the safe stored file name and an error when refused.</summary>
public sealed record LogoImageCheck(bool Ok, string Format, string StoredName, string Error);

/// <summary>
/// Framework-free rules for the user-visible identity of the app: the display name and the title-bar logo.
/// The installed identity (package id, executable name, data folder, update feed) is never touched by either.
/// </summary>
public static class AppIdentity
{
    public const string ShippedName = "Material Docker Image";
    public const int MaxNameLength = 80;
    /// <summary>Largest custom logo file accepted. A title-bar mark needs far less; the limit also bounds decode cost.</summary>
    public const long MaxLogoBytes = 1024 * 1024;
    public const int MaxLogoPixels = 2048;
    public const string LogoFolderName = "logo";
    public const string DefaultSymbolName = "DeployedCode";
    private static readonly string[] StoredNames = { "logo.png", "logo.ico", "logo.jpg" };

    // ----- Display name -----

    /// <summary>Trims and validates a display name. Control characters and over-long or empty names are refused.</summary>
    public static bool TryNormalizeName(string? text, out string name, out string error)
    {
        name = "";
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxNameLength) { error = "Use 1 to 80 characters."; return false; }
        if (trimmed.Any(char.IsControl)) { error = "The name cannot contain control characters."; return false; }
        name = trimmed;
        error = "";
        return true;
    }

    /// <summary>Returns a safe name for loading from settings: a valid saved name, or the shipped name.</summary>
    public static string NameOrShipped(string? text) => TryNormalizeName(text, out var name, out _) ? name : ShippedName;

    /// <summary>
    /// Applies a display name to the settings. Returns the history summary for exactly one revision when the name really changed,
    /// or null when the input was refused (error set) or equal to the current name (nothing to record).
    /// </summary>
    public static string? ApplyDisplayName(UserSettings settings, string? input, out string error)
    {
        if (!TryNormalizeName(input, out var name, out error)) return null;
        if (!IsRename(settings.DisplayName, name)) return null;
        settings.DisplayName = name;
        return name == ShippedName ? "Display name reset to " + ShippedName : "Display name set to " + name;
    }

    /// <summary>True when the next name differs from the current one, so a rename deserves exactly one history revision.</summary>
    public static bool IsRename(string current, string next) => !string.Equals(current, next, StringComparison.Ordinal);

    // ----- Symbols -----

    /// <summary>The Material Symbols bundled with the app: public name to glyph, taken from the generated icon class.</summary>
    public static IReadOnlyDictionary<string, string> BundledSymbols { get; } = typeof(Icons)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!, StringComparer.Ordinal);

    private static string Fold(string text)
    {
        var builder = new StringBuilder();
        foreach (var c in text) if (char.IsLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
        return builder.ToString();
    }

    /// <summary>Finds a bundled symbol by name, ignoring case, spaces and underscores ("deployed_code" matches DeployedCode).</summary>
    public static bool TryResolveSymbol(string? name, IReadOnlyDictionary<string, string> catalogue, out string canonicalName, out string glyph)
    {
        canonicalName = ""; glyph = "";
        if (name == null || name.Length > 60) return false;
        var folded = Fold(name);
        if (folded.Length == 0) return false;
        foreach (var pair in catalogue)
        {
            if (Fold(pair.Key) != folded) continue;
            canonicalName = pair.Key; glyph = pair.Value;
            return true;
        }
        return false;
    }

    public static bool TryResolveSymbol(string? name, out string canonicalName, out string glyph) => TryResolveSymbol(name, BundledSymbols, out canonicalName, out glyph);

    /// <summary>Bundled symbol names containing the filter text, for the picker's suggestions.</summary>
    public static IReadOnlyList<string> SuggestSymbols(string? filter, IReadOnlyDictionary<string, string> catalogue, int limit = 24)
    {
        var folded = Fold(filter ?? "");
        return catalogue.Keys.Where(k => folded.Length == 0 || Fold(k).Contains(folded, StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal).Take(limit).ToList();
    }

    // ----- Custom image -----

    /// <summary>Identifies PNG, ICO or JPEG from the actual bytes, never from the extension alone.</summary>
    public static string DetectFormat(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A) return "png";
        if (header.Length >= 4 && header[0] == 0 && header[1] == 0 && header[2] == 1 && header[3] == 0) return "ico";
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF) return "jpg";
        return "";
    }

    /// <summary>Checks a candidate logo file: it exists, is within the size limit, has an allowed extension and signature, and the two agree.</summary>
    public static LogoImageCheck CheckImageFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return new(false, "", "", "Choose an image file.");
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return new(false, "", "", "That file does not exist.");
            if (info.Length == 0) return new(false, "", "", "The file is empty.");
            if (info.Length > MaxLogoBytes) return new(false, "", "", "The image is larger than 1 MiB.");
            var extension = info.Extension.TrimStart('.').ToLowerInvariant();
            if (extension == "jpeg") extension = "jpg";
            if (extension is not ("png" or "ico" or "jpg")) return new(false, "", "", "Use a PNG, ICO or JPG file.");
            var header = new byte[16];
            int read;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) read = stream.Read(header, 0, header.Length);
            var format = DetectFormat(header.AsSpan(0, read));
            if (format.Length == 0) return new(false, "", "", "The file content is not a PNG, ICO or JPG image.");
            if (format != extension) return new(false, format, "", "The file extension does not match its content.");
            return new(true, format, "logo." + format, "");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new(false, "", "", "The file could not be read.");
        }
    }

    /// <summary>True when a stored logo file name is exactly one of the names this app writes, so a tampered settings file cannot point elsewhere.</summary>
    public static bool IsStoredLogoName(string? name) => name != null && Array.IndexOf(StoredNames, name) >= 0;

    /// <summary>Full path of the stored logo, or null when the name is not a legal stored name.</summary>
    public static string? StoredLogoPath(string dataDirectory, string? storedName)
        => IsStoredLogoName(storedName) ? Path.Combine(dataDirectory, LogoFolderName, storedName!) : null;

    /// <summary>
    /// Copies a validated image into the app's private logo folder, replacing any previous logo. The copy is written to a temporary
    /// file first so a failure leaves the previous logo in place. Nothing is uploaded.
    /// </summary>
    public static LogoImageCheck StoreImage(string sourcePath, string dataDirectory)
    {
        var check = CheckImageFile(sourcePath);
        if (!check.Ok) return check;
        try
        {
            var folder = Path.Combine(dataDirectory, LogoFolderName);
            Directory.CreateDirectory(folder);
            var temporary = Path.Combine(folder, "incoming." + Guid.NewGuid().ToString("N") + ".tmp");
            File.Copy(sourcePath, temporary, true);
            File.Move(temporary, Path.Combine(folder, check.StoredName), true);
            foreach (var old in StoredNames)
                if (old != check.StoredName) TryDelete(Path.Combine(folder, old));
            return check;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new(false, check.Format, "", "The image could not be copied into the app data folder.");
        }
    }

    /// <summary>Removes every stored custom logo file. Used by reset.</summary>
    public static void ClearStoredImages(string dataDirectory)
    {
        var folder = Path.Combine(dataDirectory, LogoFolderName);
        foreach (var name in StoredNames) TryDelete(Path.Combine(folder, name));
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Decides what the title bar shows for the saved choice. Anything unusable (unknown symbol, missing or tampered image) falls back
    /// to the default mark and says why, so the notice can be shown instead of failing silently.
    /// </summary>
    public static (string Kind, string Glyph, string? ImagePath, string Notice) Resolve(string? kind, string? symbol, string? imageFile, string dataDirectory)
    {
        var defaultGlyph = BundledSymbols[DefaultSymbolName];
        if (kind == LogoKinds.Symbol)
        {
            if (TryResolveSymbol(symbol, out _, out var glyph)) return (LogoKinds.Symbol, glyph, null, "");
            return (LogoKinds.Default, defaultGlyph, null, "The chosen logo symbol is not available, so the default logo is shown.");
        }
        if (kind == LogoKinds.Image)
        {
            var path = StoredLogoPath(dataDirectory, imageFile);
            if (path != null && File.Exists(path) && CheckImageFile(path).Ok) return (LogoKinds.Image, defaultGlyph, path, "");
            return (LogoKinds.Default, defaultGlyph, null, "The custom logo file is missing or invalid, so the default logo is shown.");
        }
        return (LogoKinds.Default, defaultGlyph, null, "");
    }
}
