using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MaterialDockerImage.UI;

/// <summary>Hashes PINs and recovery codes with PBKDF2-SHA256. The stored string carries its own parameters so they can be raised later.</summary>
public static class CredentialHasher
{
    public const int Iterations = 150_000, MinIterations = 10_000, MaxIterations = 2_000_000, HashBytes = 32, SaltBytes = 16;
    public const string Scheme = "pbkdf2-sha256";

    /// <summary>The install salt is mixed in front of the per-hash random salt, so a hash copied to another install does not verify there.</summary>
    public static string Hash(string secret, byte[] installSalt, int iterations = Iterations)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(secret, installSalt, salt, iterations, HashBytes);
        return $"{Scheme}${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string secret, string? stored, byte[] installSalt)
    {
        if (stored == null) return false;
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Scheme || !int.TryParse(parts[1], out var iterations) || iterations < MinIterations || iterations > MaxIterations) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]); var expected = Convert.FromBase64String(parts[3]);
            if (salt.Length != SaltBytes || expected.Length != HashBytes) return false;
            return CryptographicOperations.FixedTimeEquals(Derive(secret, installSalt, salt, iterations, expected.Length), expected);
        }
        catch (FormatException) { return false; }
    }

    private static byte[] Derive(string secret, byte[] installSalt, byte[] salt, int iterations, int length) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(secret), installSalt.Concat(salt).ToArray(), iterations, HashAlgorithmName.SHA256, length);
}

public static class PinRules
{
    public const int MinLength = 4, MaxLength = 12;
    public static bool IsValid(string? pin) => pin != null && pin.Length is >= MinLength and <= MaxLength && pin.All(char.IsAsciiDigit);
    public static string? Problem(string? pin) => IsValid(pin) ? null : "A PIN is 4 to 12 digits, numbers only.";
}

/// <summary>Single-use recovery codes generated at setup: 10 characters from an unambiguous alphabet, written XXXXX-XXXXX.</summary>
public static class RecoveryCodes
{
    public const int Count = 8;
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static IReadOnlyList<string> Generate(int count = Count)
    {
        var codes = new List<string>();
        while (codes.Count < count)
        {
            var raw = new char[10];
            for (var i = 0; i < raw.Length; i++) raw[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
            var code = new string(raw, 0, 5) + "-" + new string(raw, 5, 5);
            if (!codes.Contains(code)) codes.Add(code);
        }
        return codes;
    }

    /// <summary>Normalises what a person typed: upper case, no spaces or hyphens, and the look-alike letters I, L and O read as 1, 1 and 0.</summary>
    public static string Normalize(string? text)
    {
        var sb = new StringBuilder();
        foreach (var c in (text ?? "").ToUpperInvariant())
        {
            if (c is ' ' or '-') continue;
            sb.Append(c switch { 'I' or 'L' => '1', 'O' => '0', _ => c });
        }
        return sb.ToString();
    }

    public static bool LooksValid(string? text) { var n = Normalize(text); return n.Length == 10 && n.All(Alphabet.Contains); }
}

/// <summary>
/// Attempt limiting with exponential backoff. The first <see cref="FreeAttempts"/> wrong answers cost nothing; each further one
/// doubles the wait (30 s, 60 s, 120 s ...) up to an hour. Attempts made during a wait are refused without being checked or counted.
/// </summary>
public static class AttemptLimiter
{
    public const int FreeAttempts = 2;
    public static readonly TimeSpan BaseWait = TimeSpan.FromSeconds(30), MaxWait = TimeSpan.FromHours(1);

    public static TimeSpan WaitAfter(int consecutiveFailures)
    {
        if (consecutiveFailures <= FreeAttempts) return TimeSpan.Zero;
        var exponent = Math.Min(consecutiveFailures - FreeAttempts - 1, 12);
        var wait = TimeSpan.FromTicks(BaseWait.Ticks * (1L << exponent));
        return wait > MaxWait ? MaxWait : wait;
    }

    public static int FreeAttemptsLeft(int consecutiveFailures) => Math.Max(0, FreeAttempts - consecutiveFailures);
}
