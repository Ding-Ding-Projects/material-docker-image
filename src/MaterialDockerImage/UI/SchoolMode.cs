using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace MaterialDockerImage.UI;

public enum SchoolSecretKind { None, Pin, Password }

public enum SchoolOffOutcome { TurnedOff, Wrong, Waiting, AlreadyOff }

/// <summary>Rules for the School mode name and for the secret that turns the mode off.</summary>
public static class SchoolModeRules
{
    public const string DefaultName = "School mode";
    public const int MaxNameLength = 40, MinPasswordLength = 6, MaxPasswordLength = 64;

    public static string? NameProblem(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Enter a name.";
        if (name.Trim().Length > MaxNameLength) return "The name can be at most 40 characters.";
        if (name.Any(char.IsControl)) return "The name cannot contain control characters.";
        return null;
    }

    public static SchoolSecretKind KindOf(string? secret)
    {
        if (PinRules.IsValid(secret)) return SchoolSecretKind.Pin;
        return secret != null && secret.Length is >= MinPasswordLength and <= MaxPasswordLength && !secret.Any(char.IsControl) ? SchoolSecretKind.Password : SchoolSecretKind.None;
    }

    public static string? SecretProblem(string? secret) => KindOf(secret) == SchoolSecretKind.None ? "Use a PIN of 4 to 12 digits, or a password of 6 to 64 characters." : null;

    /// <summary>The name every surface shows. Once renamed it never falls back to the shipped name.</summary>
    public static string DisplayName(string? chosen) => string.IsNullOrWhiteSpace(chosen) ? DefaultName : chosen.Trim();

    public const string Disclosure = "This mode is a lock you set for yourself, not security. Anyone who can delete the shared application-data record can switch it off.";
}

/// <summary>
/// The one shared School mode record: on or off, the name the user gave it, and the salted hash of the PIN or password that turns it off.
/// It lives in the shared local application-data folder used by all the apps, so one switch serves every app, and a running app can notice a change
/// with <see cref="ReloadIfChanged"/>. The secret is never stored or logged; deleting the record resets the mode, which the disclosure says plainly.
/// </summary>
public sealed class SchoolModeStore
{
    private sealed class Shape
    {
        public int Version { get; set; } = 1;
        public bool Enabled { get; set; }
        public string Name { get; set; } = SchoolModeRules.DefaultName;
        public string? Salt { get; set; }
        public string? Hash { get; set; }
        public int Failures { get; set; }
        public long WaitUntilUnix { get; set; }
    }

    private readonly string? path;
    private readonly Func<DateTimeOffset> clock;
    private readonly int iterations;
    private Shape data = new();
    private DateTime stamp;

    /// <summary>A null path keeps the record in memory (capture mode and checks).</summary>
    public SchoolModeStore(string? path, Func<DateTimeOffset>? clock = null, int iterations = CredentialHasher.Iterations)
    {
        this.path = path; this.clock = clock ?? (() => DateTimeOffset.UtcNow); this.iterations = iterations;
        Load();
    }

    /// <summary>Shared by every app: the parent of the per-app data folder.</summary>
    public static string SharedPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DingDingProjects", "shared", "school-mode.json");

    public bool Enabled => data.Enabled;
    public string Name => SchoolModeRules.DisplayName(data.Name);
    public bool HasSecret => data.Hash != null;
    public bool Exists => path == null || File.Exists(path);
    public string? LoadError { get; private set; }

    private void Load()
    {
        if (path == null) return;
        if (!File.Exists(path)) { data = new Shape(); stamp = default; return; }
        try
        {
            stamp = File.GetLastWriteTimeUtc(path);
            var shape = JsonSerializer.Deserialize<Shape>(File.ReadAllText(path));
            if (shape == null) return;
            if (SchoolModeRules.NameProblem(shape.Name) != null) shape.Name = SchoolModeRules.DefaultName;
            if (shape.Hash != null && shape.Salt == null) shape.Hash = null;
            data = shape; LoadError = null;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { LoadError = "The School mode record could not be read, so the mode is treated as off: " + e.Message; data = new Shape(); }
    }

    /// <summary>Re-reads the record when another app (or a deletion) changed it. Returns true when the visible state changed, so a running app switches live.</summary>
    public bool ReloadIfChanged()
    {
        if (path == null) return false;
        var now = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default;
        if (now == stamp) return false;
        var before = (data.Enabled, Name, HasSecret);
        Load();
        return before != (data.Enabled, Name, HasSecret);
    }

    private void Save()
    {
        if (path == null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
        stamp = File.GetLastWriteTimeUtc(path);
    }

    /// <summary>Turns the mode on. Turning it on needs no secret; turning it off does when one is set.</summary>
    public void Enable() { if (data.Enabled) return; data.Enabled = true; Save(); }

    /// <summary>Adopts a mode that an older build kept only in settings.json, so the shared record starts agreeing with it.</summary>
    public void AdoptLegacy(bool enabled) { if (!enabled || path != null && File.Exists(path)) return; data.Enabled = true; Save(); }

    public void Rename(string name)
    {
        if (SchoolModeRules.NameProblem(name) is { } problem) throw new ArgumentException(problem, nameof(name));
        data.Name = name.Trim(); Save();
    }

    /// <summary>Sets or replaces the secret that turns the mode off. Replacing it needs the old one when one exists.</summary>
    public bool SetSecret(string secret, string? current = null)
    {
        if (SchoolModeRules.SecretProblem(secret) is { } problem) throw new ArgumentException(problem, nameof(secret));
        if (HasSecret && !CheckSecret(current)) return false;
        data.Salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        data.Hash = CredentialHasher.Hash(secret, Convert.FromBase64String(data.Salt), iterations);
        data.Failures = 0; data.WaitUntilUnix = 0;
        Save();
        return true;
    }

    private bool CheckSecret(string? secret) => secret != null && data.Hash != null && data.Salt != null && CredentialHasher.Verify(secret, data.Hash, Convert.FromBase64String(data.Salt));

    public DateTimeOffset? WaitUntil
    {
        get { if (data.WaitUntilUnix <= 0) return null; var until = DateTimeOffset.FromUnixTimeSeconds(data.WaitUntilUnix); return until > clock() ? until : null; }
    }

    /// <summary>Turns the mode off. With a secret set, a wrong answer is counted and lengthens a wait exactly as the lock ladder's credential rung does.</summary>
    public SchoolOffOutcome TryTurnOff(string? secret)
    {
        if (!data.Enabled) return SchoolOffOutcome.AlreadyOff;
        if (HasSecret)
        {
            if (WaitUntil != null) return SchoolOffOutcome.Waiting;
            if (!CheckSecret(secret))
            {
                data.Failures++;
                var wait = AttemptLimiter.WaitAfter(data.Failures);
                if (wait > TimeSpan.Zero) data.WaitUntilUnix = (clock() + wait).ToUnixTimeSeconds();
                Save();
                return SchoolOffOutcome.Wrong;
            }
        }
        data.Enabled = false; data.Failures = 0; data.WaitUntilUnix = 0;
        Save();
        return SchoolOffOutcome.TurnedOff;
    }

    /// <summary>Removes the record, which is the documented way to reset the mode, its name and its secret.</summary>
    public void Reset()
    {
        data = new Shape();
        if (path != null && File.Exists(path)) File.Delete(path);
        stamp = default;
    }
}
