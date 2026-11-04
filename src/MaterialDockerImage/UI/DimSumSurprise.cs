using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace MaterialDockerImage.UI;

/// <summary>One dish from the public dim sum catalogue. Names are the catalogue's own; the picture is a public release asset, never a file in this project.</summary>
public sealed record DimSumDish(string Id, string English, string Cantonese, string ImageFile, string AltEnglish, string AltCantonese);

/// <summary>What is true at startup that decides whether the surprise may appear. Each flag is something the surprise must stay out of the way of.</summary>
public sealed record SurpriseContext(bool SetupComplete, bool SchoolMode, bool Busy, bool FirstRun, bool ErrorShown, bool UpdatePending, bool QuietHours, bool Capture, bool AlreadyShownThisLaunch);

/// <summary>The rules of the startup dim sum surprise. It cannot be switched off: it stays polite by staying out of every flow where it could interrupt.</summary>
public static class DimSumRules
{
    /// <summary>Ten in a hundred, from one fresh random draw per launch.</summary>
    public const double Chance = 0.10;
    public const string CatalogUrl = "https://raw.githubusercontent.com/Ding-Ding-Projects/dim-sum-photos/main/catalog/index.json";
    public const string ReleaseBase = "https://github.com/Ding-Ding-Projects/dim-sum-photos/releases/download/";
    public static readonly string[] ReleaseTags = ["catalog-v1", "catalog-v1-part-002", "catalog-v1-part-003"];
    public const int ToastSeconds = 9, MaxImageBytes = 12 * 1024 * 1024;

    /// <summary>The surprise needs the user to be idle in a settled app: never during first-run setup, an error, an update, a busy operation, quiet hours or School mode.</summary>
    public static bool MayAppear(SurpriseContext c) =>
        c.SetupComplete && !c.SchoolMode && !c.Busy && !c.FirstRun && !c.ErrorShown && !c.UpdatePending && !c.QuietHours && !c.Capture && !c.AlreadyShownThisLaunch;

    /// <summary>True when this launch's single draw wins. Pass a fresh <c>NextDouble</c> value.</summary>
    public static bool Wins(double draw) => draw >= 0 && draw < Chance;

    /// <summary>The dish name for the active language mode. The dish's own names stay exact at every funny level.</summary>
    public static string Caption(DimSumDish dish, string language) => language switch
    {
        "Cantonese" => dish.Cantonese,
        "Bilingual" => dish.English + " · " + dish.Cantonese,
        _ => dish.English
    };

    public static string Alt(DimSumDish dish, string language) => language switch
    {
        "Cantonese" => dish.AltCantonese.Length > 0 ? dish.AltCantonese : dish.Cantonese,
        _ => dish.AltEnglish.Length > 0 ? dish.AltEnglish : dish.English
    };

    /// <summary>Candidate public asset URLs for a dish's picture, in the order they should be tried.</summary>
    public static IReadOnlyList<string> ImageUrls(DimSumDish dish) =>
        dish.ImageFile.Length == 0 || dish.ImageFile.Any(ch => ch is '/' or '\\' or ':' or '?' or '#' || char.IsControl(ch))
            ? Array.Empty<string>()
            : ReleaseTags.Select(t => ReleaseBase + t + "/" + Uri.EscapeDataString(dish.ImageFile)).ToList();

    public static bool LooksLikePng(ReadOnlySpan<byte> bytes) =>
        bytes.Length > 24 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;

    /// <summary>Reads the public catalogue into dishes. A record without both names or without an image is left out, as the rules say never to fill a gap locally.</summary>
    public static IReadOnlyList<DimSumDish> ParseCatalog(string json)
    {
        var list = new List<DimSumDish>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("dishes", out var dishes) || dishes.ValueKind != JsonValueKind.Array) return list;
            foreach (var d in dishes.EnumerateArray())
            {
                if (d.ValueKind != JsonValueKind.Object) continue;
                var id = Text(d, "id"); var name = d.TryGetProperty("name", out var n) ? n : default;
                var en = name.ValueKind == JsonValueKind.Object ? Text(name, "en") : ""; var zh = name.ValueKind == JsonValueKind.Object ? Text(name, "zhHant") : "";
                var img = d.TryGetProperty("image", out var i) && i.ValueKind == JsonValueKind.Object ? i : default;
                var path = img.ValueKind == JsonValueKind.Object ? Text(img, "path") : "";
                var file = path.Length > 0 ? path[(path.LastIndexOfAny(new[] { '/', '\\' }) + 1)..] : "";
                if (id.Length == 0 || en.Length == 0 || zh.Length == 0 || !file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                var alt = img.TryGetProperty("alt", out var a) && a.ValueKind == JsonValueKind.Object ? a : default;
                list.Add(new DimSumDish(id, en, zh, file, alt.ValueKind == JsonValueKind.Object ? Text(alt, "en") : "", alt.ValueKind == JsonValueKind.Object ? Text(alt, "yue") : ""));
                if (list.Count >= 20_000) break;
            }
        }
        catch (JsonException) { list.Clear(); }
        return list;
    }

    private static string Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

    /// <summary>The small local cache of the catalogue. It records the source URL and the revision it came from, and is never an authority of its own.</summary>
    public static string ToCache(IReadOnlyList<DimSumDish> dishes, string revision, DateTimeOffset fetched) =>
        JsonSerializer.Serialize(new CacheShape { Source = CatalogUrl, Revision = revision, Fetched = fetched, Dishes = dishes.ToList() });

    public static (IReadOnlyList<DimSumDish> Dishes, string Revision, DateTimeOffset Fetched)? FromCache(string json)
    {
        try
        {
            var shape = JsonSerializer.Deserialize<CacheShape>(json);
            if (shape == null || shape.Source != CatalogUrl || shape.Dishes.Count == 0) return null;
            return (shape.Dishes.Take(20_000).ToList(), shape.Revision ?? "", shape.Fetched);
        }
        catch (JsonException) { return null; }
    }

    private sealed class CacheShape { public string Source { get; set; } = ""; public string? Revision { get; set; } public DateTimeOffset Fetched { get; set; } public List<DimSumDish> Dishes { get; set; } = new(); }

    public static DimSumDish Pick(IReadOnlyList<DimSumDish> dishes, Random random) => dishes[random.Next(dishes.Count)];

    public static IReadOnlyList<DishName> AsNames(IReadOnlyList<DimSumDish> dishes) => dishes.Select(d => new DishName(d.Id, d.English, d.Cantonese)).ToList();

    /// <summary>A few well-known dishes used only for the offline name question. They carry no picture, so nothing here pretends to be a photo.</summary>
    public static readonly IReadOnlyList<DishName> OfflineNames =
    [
        new("offline-1", "Shrimp dumpling", "蝦餃"), new("offline-2", "Pork and shrimp dumpling", "燒賣"), new("offline-3", "Barbecue pork bun", "叉燒包"),
        new("offline-4", "Rice noodle roll", "腸粉"), new("offline-5", "Sticky rice in lotus leaf", "糯米雞"), new("offline-6", "Egg tart", "蛋撻"),
        new("offline-7", "Turnip cake", "蘿蔔糕"), new("offline-8", "Phoenix claws", "鳳爪")
    ];
}
