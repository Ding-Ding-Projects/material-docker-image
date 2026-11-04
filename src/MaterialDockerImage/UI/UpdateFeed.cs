using System.Globalization;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>A parsed Squirrel version: numeric parts plus an optional pre-release label. Pre-releases sort below the plain release.</summary>
public sealed record FeedVersion(int[] Parts, string Pre) : IComparable<FeedVersion>
{
    public int CompareTo(FeedVersion? other)
    {
        if (other is null) return 1;
        var length = Math.Max(Parts.Length, other.Parts.Length);
        for (var i = 0; i < length; i++)
        {
            var a = i < Parts.Length ? Parts[i] : 0;
            var b = i < other.Parts.Length ? other.Parts[i] : 0;
            if (a != b) return a.CompareTo(b);
        }
        if (Pre.Length == 0 && other.Pre.Length == 0) return 0;
        if (Pre.Length == 0) return 1;
        if (other.Pre.Length == 0) return -1;
        return string.CompareOrdinal(Pre, other.Pre) switch { < 0 => -1, > 0 => 1, _ => 0 };
    }
    public override string ToString() => string.Join('.', Parts) + (Pre.Length > 0 ? "-" + Pre : "");
}

/// <summary>One line of a Squirrel RELEASES file.</summary>
public sealed record FeedRelease(string Sha1, string FileName, string PackageId, FeedVersion Version, bool IsDelta, long Size, int? RolloutPercent);

/// <summary>Result of comparing the running version with a feed.</summary>
public sealed record FeedDecision(bool UpdateAvailable, string NewestVersion, string Message);

/// <summary>
/// Framework-free update-feed logic: version comparison and RELEASES parsing. It never touches the network or the disk;
/// UpdateService (WPF side) fetches the file and runs Update.exe, and these functions decide what the bytes mean.
/// </summary>
public static class UpdateFeed
{
    public const int MaxFeedChars = 256 * 1024;
    private static readonly Regex VersionPattern = new(@"^(?<nums>\d+(?:\.\d+){0,3})(?:-(?<pre>[0-9A-Za-z.\-]+))?(?:\+[0-9A-Za-z.\-]+)?$", RegexOptions.CultureInvariant);
    private static readonly Regex LinePattern = new(@"^(?<sha>[0-9A-Fa-f]{40})\s+(?<file>\S+\.nupkg)\s+(?<size>\d{1,12})(?:\s*#\s*(?<pct>\d{1,3})%)?\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex FilePattern = new(@"^(?<id>[A-Za-z0-9_.]+?)-(?<ver>\d+(?:\.\d+){0,3}(?:-[0-9A-Za-z.]+?)?)-(?<kind>full|delta)\.nupkg$", RegexOptions.CultureInvariant);

    public static bool TryParseVersion(string text, out FeedVersion version)
    {
        version = new FeedVersion([], "");
        if (string.IsNullOrWhiteSpace(text)) return false;
        var m = VersionPattern.Match(text.Trim().TrimStart('v', 'V'));
        if (!m.Success) return false;
        var parts = new List<int>();
        foreach (var piece in m.Groups["nums"].Value.Split('.'))
        {
            if (!int.TryParse(piece, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return false;
            parts.Add(n);
        }
        version = new FeedVersion(parts.ToArray(), m.Groups["pre"].Value);
        return true;
    }

    /// <summary>Parses a RELEASES file. Blank lines are skipped; any other line that does not match the format is an error, not silently ignored.</summary>
    public static IReadOnlyList<FeedRelease> ParseReleases(string text)
    {
        if (text.Length > MaxFeedChars) throw new InvalidDataException("The update feed is larger than the 256 KB limit.");
        var releases = new List<FeedRelease>();
        foreach (var raw in text.TrimStart('﻿').Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var m = LinePattern.Match(line);
            if (!m.Success) throw new InvalidDataException("The update feed has a line that is not in RELEASES format.");
            var file = FilePattern.Match(m.Groups["file"].Value);
            if (!file.Success || !TryParseVersion(file.Groups["ver"].Value, out var version)) throw new InvalidDataException("The update feed names a package whose version cannot be read.");
            var percent = m.Groups["pct"].Success ? int.Parse(m.Groups["pct"].Value, CultureInfo.InvariantCulture) : (int?)null;
            if (percent is > 100) throw new InvalidDataException("The update feed has a rollout percentage above 100.");
            releases.Add(new FeedRelease(m.Groups["sha"].Value.ToUpperInvariant(), file.Groups["file"].Value, file.Groups["id"].Value, version, file.Groups["kind"].Value == "delta", long.Parse(m.Groups["size"].Value, CultureInfo.InvariantCulture), percent));
        }
        return releases;
    }

    /// <summary>Decides whether the feed offers a full package for the expected id that is newer than the running version.</summary>
    public static FeedDecision Decide(string runningVersion, string expectedPackageId, IReadOnlyList<FeedRelease> releases)
    {
        if (!TryParseVersion(runningVersion, out var current)) return new FeedDecision(false, "", "The running version is not a recognised version number, so no comparison was made.");
        var full = releases.Where(r => !r.IsDelta && string.Equals(r.PackageId, expectedPackageId, StringComparison.Ordinal)).ToList();
        if (full.Count == 0) return new FeedDecision(false, "", "The feed lists no full package for this app.");
        var newest = full.OrderBy(r => r.Version).Last();
        return newest.Version.CompareTo(current) > 0
            ? new FeedDecision(true, newest.Version.ToString(), "Version " + newest.Version + " is available.")
            : new FeedDecision(false, newest.Version.ToString(), "This build is up to date.");
    }

    /// <summary>The running version from assembly metadata ("0.51.1+abc123" or "0.51.1.0") reduced to its comparable part.</summary>
    public static string CleanRunningVersion(string informational) => informational.Split('+')[0].Trim();
}
