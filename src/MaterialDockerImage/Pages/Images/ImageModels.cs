using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MaterialDockerImage.Core;

namespace MaterialDockerImage.Pages;

/// <summary>One row of docker image ls --all, with container usage computed from docker container ls --all.</summary>
internal sealed record ImageRow(string Repository, string Tag, string Id, string Size, string CreatedSince, string CreatedAt, string Digest)
{
    public int Running { get; init; }
    public int Total { get; init; }
    public bool Tagged => Repository != "<none>" && Tag != "<none>";
    public bool Dangling => Repository == "<none>" && Tag == "<none>";
    public string Name => Tagged ? Repository + ":" + Tag : Repository != "<none>" ? Repository + " (untagged)" : "<none>";
    /// <summary>The reference used for actions: repository:tag when tagged, otherwise the image ID.</summary>
    public string Reference => Tagged ? Repository + ":" + Tag : Id;
    public string ShortId => (Id.StartsWith("sha256:", StringComparison.Ordinal) ? Id[7..] : Id) is var s && s.Length > 12 ? s[..12] : Id.Replace("sha256:", "", StringComparison.Ordinal);
    public string Usage => Total == 0 ? "None" : Running > 0 ? $"{Running} running" + (Total > Running ? $", {Total - Running} stopped" : "") : $"{Total} stopped";
    public string Key => Reference + "|" + Id;

    public static ImageRow From(JsonElement row) => new(
        DockerJson.Text(row, "Repository"), DockerJson.Text(row, "Tag"), DockerJson.Text(row, "ID"), DockerJson.Text(row, "Size"),
        DockerJson.Text(row, "CreatedSince"), DockerJson.Text(row, "CreatedAt"), DockerJson.Text(row, "Digest"));

    /// <summary>True when a container's Image field (as shown by docker container ls) refers to this image.</summary>
    public bool UsedBy(string containerImage)
    {
        if (containerImage.Length == 0) return false;
        var bare = containerImage.StartsWith("sha256:", StringComparison.Ordinal) ? containerImage[7..] : containerImage;
        var id = Id.StartsWith("sha256:", StringComparison.Ordinal) ? Id[7..] : Id;
        if (bare.Length >= 12 && Regex.IsMatch(bare, "^[0-9a-f]+$", RegexOptions.None, TimeSpan.FromMilliseconds(100)) && (id.StartsWith(bare, StringComparison.Ordinal) || bare.StartsWith(id, StringComparison.Ordinal))) return true;
        if (!Tagged) return false;
        if (containerImage == Repository + ":" + Tag) return true;
        return Tag == "latest" && containerImage == Repository;
    }

    /// <summary>Parses docker's human size text (for example 182MB, 1.2GB, 512kB) into bytes; 0 when unknown.</summary>
    public static double ParseSize(string text)
    {
        var m = Regex.Match(text.Trim(), @"^([0-9.]+)\s*([kKMGT]?i?B)$", RegexOptions.None, TimeSpan.FromMilliseconds(100));
        if (!m.Success || !double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return 0;
        var unit = m.Groups[2].Value.ToUpperInvariant();
        var power = unit[0] switch { 'K' => 1, 'M' => 2, 'G' => 3, 'T' => 4, _ => 0 };
        return value * Math.Pow(unit.Contains('I') ? 1024 : 1000, power);
    }

    /// <summary>Registry host a reference would be pushed to.</summary>
    public static string RegistryOf(string reference)
    {
        var first = reference.Split('/')[0];
        return reference.Contains('/') && (first.Contains('.') || first.Contains(':') || first == "localhost") ? first : "docker.io (Docker Hub)";
    }
}
