using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MaterialDockerImage.UI;

/// <summary>One step of the path from the window down to an element: its type, its name when it has one, and its position among siblings of the same type.</summary>
public readonly record struct ElementKeySegment(string TypeName, string? Name, int IndexAmongSameType);

/// <summary>
/// Stable identity for an element. The key is the path of segments from the window to the element, for example
/// <c>Window/Grid/Border[2]/Button#btn.build</c>. Named elements keep their name, unnamed ones keep their position among siblings of
/// the same type, so the key survives restarts and page rebuilds as long as the layout around the element is the same.
/// </summary>
public static class ElementKey
{
    public const int MaxLength = 400;
    private const int MaxNameLength = 40;

    public static string Segment(ElementKeySegment s)
    {
        var type = Clean(s.TypeName, 40);
        if (type.Length == 0) type = "Element";
        var text = type;
        if (!string.IsNullOrWhiteSpace(s.Name)) text += "#" + Clean(s.Name!.Trim(), MaxNameLength);
        if (s.IndexAmongSameType > 0) text += "[" + s.IndexAmongSameType.ToString(CultureInfo.InvariantCulture) + "]";
        return text;
    }

    /// <summary>Builds the key from the root-to-element path. A very deep path keeps its last segments, marked with a leading "~".</summary>
    public static string Build(IReadOnlyList<ElementKeySegment> path)
    {
        var parts = path.Select(Segment).ToList();
        var key = string.Join("/", parts);
        if (key.Length <= MaxLength) return key;
        var kept = new List<string>();
        var length = 1;
        for (var i = parts.Count - 1; i >= 0; i--)
        {
            if (length + parts[i].Length + 1 > MaxLength) break;
            kept.Insert(0, parts[i]);
            length += parts[i].Length + 1;
        }
        return "~/" + string.Join("/", kept);
    }

    public static bool IsValid(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > MaxLength) return false;
        foreach (var c in key) if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-' or '/' or '#' or '[' or ']' or '~')) return false;
        return true;
    }

    /// <summary>The last segment, used as a short readable label for lists.</summary>
    public static string Leaf(string key)
    {
        var slash = key.LastIndexOf('/');
        return slash < 0 ? key : key[(slash + 1)..];
    }

    private static string Clean(string text, int max)
    {
        var sb = new StringBuilder();
        foreach (var c in text)
        {
            if (sb.Length >= max) break;
            sb.Append(char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-' ? c : '_');
        }
        return sb.ToString();
    }
}

/// <summary>The properties one element can override. A null value means "leave the theme's value alone".</summary>
public sealed class ElementStyle
{
    public const double MaxRadius = 48, MinFont = 8, MaxFont = 40, MaxPadding = 40;
    public static readonly int[] Weights = [300, 400, 500, 600, 700];

    public string? Background { get; set; }
    public string? Foreground { get; set; }
    public double? CornerRadius { get; set; }
    public double? FontSize { get; set; }
    public int? FontWeight { get; set; }
    public double? Padding { get; set; }
    public bool Hidden { get; set; }

    public bool IsEmpty => Background == null && Foreground == null && CornerRadius == null && FontSize == null && FontWeight == null && Padding == null && !Hidden;
    public ElementStyle Clone() => (ElementStyle)MemberwiseClone();

    /// <summary>Repairs values into their supported ranges. Returns a new style; a malformed colour is dropped.</summary>
    public ElementStyle Normalized()
    {
        var s = Clone();
        s.Background = NormalizeColor(s.Background);
        s.Foreground = NormalizeColor(s.Foreground);
        s.CornerRadius = Round(s.CornerRadius, 0, MaxRadius);
        s.FontSize = Round(s.FontSize, MinFont, MaxFont);
        s.Padding = Round(s.Padding, 0, MaxPadding);
        if (s.FontWeight is { } w) s.FontWeight = Weights.OrderBy(x => Math.Abs(x - w)).First();
        return s;
    }

    private static string? NormalizeColor(string? value) => AppearanceRgba.TryParseStorageHex(value, out var c) ? c.ToStorageHex() : null;
    private static double? Round(double? v, double min, double max) => v is { } d && double.IsFinite(d) ? Math.Round(Math.Clamp(d, min, max), 1) : null;

    /// <summary>Strict check used for imports: every present value must already be in range.</summary>
    public List<string> Validate()
    {
        var problems = new List<string>();
        if (Background != null && !AppearanceRgba.TryParseStorageHex(Background, out _)) problems.Add("Background must be a hex colour.");
        if (Foreground != null && !AppearanceRgba.TryParseStorageHex(Foreground, out _)) problems.Add("Foreground must be a hex colour.");
        void Range(double? v, double min, double max, string name) { if (v is { } d && (!double.IsFinite(d) || d < min || d > max)) problems.Add($"{name} must be between {min.ToString(CultureInfo.InvariantCulture)} and {max.ToString(CultureInfo.InvariantCulture)}."); }
        Range(CornerRadius, 0, MaxRadius, "Corner radius");
        Range(FontSize, MinFont, MaxFont, "Font size");
        Range(Padding, 0, MaxPadding, "Padding");
        if (FontWeight is { } w && !Weights.Contains(w)) problems.Add("Font weight must be 300, 400, 500, 600 or 700.");
        return problems;
    }

    /// <summary>A short readable summary such as "background #112233, corner radius 8".</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (Background != null) parts.Add("background " + Background);
        if (Foreground != null) parts.Add("text colour " + Foreground);
        if (CornerRadius is { } r) parts.Add("corner radius " + r.ToString("0.#", CultureInfo.InvariantCulture));
        if (FontSize is { } f) parts.Add("font size " + f.ToString("0.#", CultureInfo.InvariantCulture));
        if (FontWeight is { } w) parts.Add("weight " + w.ToString(CultureInfo.InvariantCulture));
        if (Padding is { } p) parts.Add("padding " + p.ToString("0.#", CultureInfo.InvariantCulture));
        if (Hidden) parts.Add("hidden");
        return parts.Count == 0 ? "no overrides" : string.Join(", ", parts);
    }

    /// <summary>The appearance lock id that guards each property, so a locked appearance row also locks the matching element edit.</summary>
    public static string? LockIdFor(string property) => property switch
    {
        nameof(Background) or nameof(Foreground) => "roles",
        nameof(CornerRadius) => "radius",
        nameof(FontSize) => "fontsize",
        nameof(Padding) => "density",
        _ => null
    };

    /// <summary>Warning text when both colours are set and read poorly together, otherwise null.</summary>
    public string? ContrastWarning()
    {
        if (!AppearanceRgba.TryParseStorageHex(Background, out var bg) || !AppearanceRgba.TryParseStorageHex(Foreground, out var fg)) return null;
        var ratio = AppearanceRgba.ContrastRatio(fg, bg);
        return ratio < 3 ? $"Text and background contrast is {ratio.ToString("0.0", CultureInfo.InvariantCulture)}:1, which is hard to read. Aim for at least 4.5:1." : null;
    }
}

/// <summary>The per-element overrides, keyed by stable element identity. Pure data and file handling.</summary>
public sealed class ElementAppearanceStore
{
    public const string FormatName = "material-docker-image.element-appearance";
    public const int MaxItems = 200;
    public const int MaxFileBytes = 256 * 1024;
    private readonly Dictionary<string, ElementStyle> items = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, ElementStyle> Items => items;
    public int Count => items.Count;
    public ElementStyle? Get(string key) => items.TryGetValue(key, out var s) ? s : null;

    /// <summary>Stores an element's style. An empty style removes the override. Returns false for an invalid key or a full store.</summary>
    public bool Set(string key, ElementStyle style)
    {
        if (!ElementKey.IsValid(key)) return false;
        var clean = style.Normalized();
        if (clean.IsEmpty) return items.Remove(key);
        if (!items.ContainsKey(key) && items.Count >= MaxItems) return false;
        items[key] = clean;
        return true;
    }

    public bool Reset(string key) => items.Remove(key);
    public int ResetAll() { var n = items.Count; items.Clear(); return n; }

    private sealed class FileModel
    {
        public string Format { get; set; } = FormatName;
        public int Version { get; set; } = 1;
        public Dictionary<string, ElementStyle> Elements { get; set; } = new(StringComparer.Ordinal);
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string ToJson() => JsonSerializer.Serialize(new FileModel { Elements = items.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal) }, Options);

    /// <summary>Parses and strictly validates a file. Throws <see cref="InvalidDataException"/> with a readable reason; nothing is partly applied.</summary>
    public static ElementAppearanceStore Parse(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaxFileBytes) throw new InvalidDataException("The element appearance file is larger than 256 KiB.");
        FileModel model;
        try { model = JsonSerializer.Deserialize<FileModel>(text, Options) ?? throw new InvalidDataException("The file is empty."); }
        catch (JsonException e) { throw new InvalidDataException("The element appearance file is not valid: " + e.Message); }
        if (model.Format != FormatName) throw new InvalidDataException($"The format must be \"{FormatName}\".");
        if (model.Version != 1) throw new InvalidDataException("Only element appearance version 1 is supported.");
        if (model.Elements == null || model.Elements.Count > MaxItems) throw new InvalidDataException("There are too many element overrides.");
        var store = new ElementAppearanceStore();
        foreach (var (key, style) in model.Elements)
        {
            if (!ElementKey.IsValid(key)) throw new InvalidDataException("An element key is not valid.");
            if (style == null) throw new InvalidDataException("An element override is empty.");
            var problems = style.Validate();
            if (problems.Count > 0) throw new InvalidDataException(string.Join(" ", problems.Take(3)));
            store.Set(key, style);
        }
        return store;
    }

    /// <summary>Loads the file. A missing file is an empty store; an unreadable one throws so nothing is overwritten silently.</summary>
    public static ElementAppearanceStore Load(string path)
    {
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > MaxFileBytes) throw new InvalidDataException("The element appearance file is larger than 256 KiB.");
        return Parse(File.ReadAllText(path));
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, ToJson());
        File.Move(temporary, path, true);
    }
}
