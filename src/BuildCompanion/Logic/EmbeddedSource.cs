using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace BuildCompanion;

/// <summary>The source manifest packed beside the files: the commit the files came from and the SHA-256 of every file.</summary>
public sealed record EmbeddedSourceManifest(string Source, IReadOnlyDictionary<string, string> Files);

public sealed record EmbeddedSourceResult(string? Path, string? Source, string? Error);

/// <summary>
/// The companion carries its own complete source as a zip inside the executable, so it can build with no checkout nearby. The zip is
/// extracted once per source commit under the user's local application data and reused while it is intact. Every entry is checked
/// before anything is written: no rooted path, no "..", no drive or stream colon, only files listed in the manifest, every hash equal
/// to the manifest, and bounded sizes. A half-written extraction never becomes the build folder: files go to a temporary sibling that
/// is renamed into place only after every check passed.
/// </summary>
public static class EmbeddedSource
{
    public const string ResourceName = "source.zip";
    public const string ManifestName = "EMBEDDED-SOURCE.json";
    public const string CompleteMarker = ".embedded-source-complete";
    public const long MaxTotalBytes = 512L * 1024 * 1024;
    public const long MaxFileBytes = 128L * 1024 * 1024;
    public const int MaxFiles = 20000;

    /// <summary>The default extraction root: %LOCALAPPDATA%\DingDingProjects\MaterialDockerImageBuild\source.</summary>
    public static string DefaultRoot() =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DingDingProjects", "MaterialDockerImageBuild", "source");

    /// <summary>The zip packed into this executable at publish time, or null in a build made without it.</summary>
    public static Stream? OpenPacked() => typeof(EmbeddedSource).Assembly.GetManifestResourceStream(ResourceName);

    /// <summary>Unpacks the packed source to the default root, or returns null when this executable carries none.</summary>
    public static EmbeddedSourceResult? UnpackPacked()
    {
        using var zip = OpenPacked();
        return zip is null ? null : Extract(zip, DefaultRoot());
    }

    /// <summary>The commit of the packed source, read from its manifest without unpacking anything; null when none is packed.</summary>
    public static string? PackedCommit()
    {
        using var zip = OpenPacked();
        if (zip is null) return null;
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
        using var stream = (archive.GetEntry(ManifestName) ?? throw new InvalidDataException("The embedded source has no manifest.")).Open();
        return ReadManifest(stream).Source;
    }

    public static EmbeddedSourceManifest ReadManifest(Stream manifest)
    {
        using var doc = JsonDocument.Parse(manifest);
        var root = doc.RootElement;
        var source = root.GetProperty("source").GetString() ?? "";
        if (!IsCommit(source)) throw new InvalidDataException("The embedded source manifest has no valid commit.");
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in root.GetProperty("files").EnumerateArray())
        {
            var path = entry.GetProperty("path").GetString() ?? "";
            var sha = (entry.GetProperty("sha256").GetString() ?? "").ToLowerInvariant();
            if (!IsSafeEntry(path)) throw new InvalidDataException("Unsafe path in the embedded source manifest: " + path);
            if (sha.Length != 64 || !sha.All(Uri.IsHexDigit)) throw new InvalidDataException("Bad hash in the embedded source manifest: " + path);
            if (!files.TryAdd(path, sha)) throw new InvalidDataException("Duplicate path in the embedded source manifest: " + path);
        }
        if (files.Count == 0 || files.Count > MaxFiles) throw new InvalidDataException("The embedded source manifest lists no files or too many.");
        return new EmbeddedSourceManifest(source, files);
    }

    public static bool IsCommit(string value) => value.Length == 40 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    /// <summary>A relative path with forward slashes and no way to climb out of the target or name a drive or stream.</summary>
    public static bool IsSafeEntry(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 400 || path.StartsWith('/') || path.Contains('\\') || path.Contains(':') || path.Contains('\0')) return false;
        return path.Split('/').All(part => part.Length > 0 && part != "." && part != ".." && part.TrimEnd(' ', '.') == part);
    }

    /// <summary>Extracts the zip under <paramref name="root"/> (one folder per commit) or reuses an intact earlier extraction.</summary>
    public static EmbeddedSourceResult Extract(Stream zip, string root)
    {
        try
        {
            using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
            var manifestEntry = archive.GetEntry(ManifestName) ?? throw new InvalidDataException("The embedded source has no manifest.");
            EmbeddedSourceManifest manifest;
            using (var stream = manifestEntry.Open()) manifest = ReadManifest(stream);
            var target = System.IO.Path.Combine(root, manifest.Source);
            if (IsIntact(target, manifest)) return new EmbeddedSourceResult(target, manifest.Source, null);

            var entries = archive.Entries.Where(e => e.FullName != ManifestName && !e.FullName.EndsWith('/')).ToList();
            var listed = new HashSet<string>(manifest.Files.Keys, StringComparer.Ordinal);
            if (entries.Count != listed.Count || entries.Any(e => !listed.Contains(e.FullName))) throw new InvalidDataException("The embedded source files do not match its manifest.");
            if (entries.Any(e => e.Length > MaxFileBytes) || entries.Sum(e => e.Length) > MaxTotalBytes) throw new InvalidDataException("The embedded source is larger than allowed.");

            Directory.CreateDirectory(root);
            var staging = System.IO.Path.Combine(root, manifest.Source + ".partial-" + Guid.NewGuid().ToString("N"));
            try
            {
                foreach (var entry in entries)
                {
                    var destination = System.IO.Path.GetFullPath(System.IO.Path.Combine(staging, entry.FullName.Replace('/', System.IO.Path.DirectorySeparatorChar)));
                    if (!destination.StartsWith(System.IO.Path.GetFullPath(staging) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Unsafe path in the embedded source: " + entry.FullName);
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
                    using (var input = entry.Open())
                    using (var output = File.Create(destination)) input.CopyTo(output);
                    if (Sha256(destination) != manifest.Files[entry.FullName]) throw new InvalidDataException("A file of the embedded source does not match its hash: " + entry.FullName);
                }
                using (var input = manifestEntry.Open())
                using (var output = File.Create(System.IO.Path.Combine(staging, ManifestName))) input.CopyTo(output);
                File.WriteAllText(System.IO.Path.Combine(staging, CompleteMarker), manifest.Source);
                if (Directory.Exists(target)) Directory.Delete(target, recursive: true); // an earlier, damaged extraction of the same commit
                Directory.Move(staging, target);
            }
            finally
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            }
            return new EmbeddedSourceResult(target, manifest.Source, null);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new EmbeddedSourceResult(null, null, "The embedded source could not be unpacked: " + e.Message);
        }
    }

    /// <summary>An earlier extraction is reused only when its marker names the commit and every listed file still has its hash.</summary>
    public static bool IsIntact(string target, EmbeddedSourceManifest manifest)
    {
        var marker = System.IO.Path.Combine(target, CompleteMarker);
        if (!File.Exists(marker) || File.ReadAllText(marker).Trim() != manifest.Source) return false;
        foreach (var (path, sha) in manifest.Files)
        {
            var file = System.IO.Path.Combine(target, path.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (!File.Exists(file) || Sha256(file) != sha) return false;
        }
        return true;
    }

    static string Sha256(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
