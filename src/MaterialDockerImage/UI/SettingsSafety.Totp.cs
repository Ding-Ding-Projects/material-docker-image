using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MaterialDockerImage.UI;

/// <summary>RFC 4648 Base32 (no padding on output, padding and spaces tolerated on input), as authenticator apps use it.</summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b; bits += 8;
            while (bits >= 5) { sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]); bits -= 5; }
            buffer &= (1 << bits) - 1;
        }
        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    /// <summary>Decodes Base32; returns null for any character outside the alphabet, so a typo is never silently accepted.</summary>
    public static byte[]? Decode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var clean = text.Replace(" ", "").Replace("-", "").TrimEnd('=').ToUpperInvariant();
        if (clean.Length == 0) return null;
        var output = new byte[clean.Length * 5 / 8];
        int buffer = 0, bits = 0, index = 0;
        foreach (var c in clean)
        {
            var value = Alphabet.IndexOf(c);
            if (value < 0) return null;
            buffer = (buffer << 5) | value; bits += 5;
            if (bits >= 8) { bits -= 8; if (index < output.Length) output[index++] = (byte)((buffer >> bits) & 0xFF); buffer &= (1 << bits) - 1; }
        }
        return output;
    }

    /// <summary>Groups the text in fours for reading aloud and typing: "ABCD EFGH ...".</summary>
    public static string Group(string text)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < text.Length; i++) { if (i > 0 && i % 4 == 0) sb.Append(' '); sb.Append(text[i]); }
        return sb.ToString();
    }
}

/// <summary>RFC 4226 HOTP and RFC 6238 TOTP. SHA-1, SHA-256 and SHA-512, 6 to 8 digits, any period.</summary>
public static class Totp
{
    public const string DefaultAlgorithm = "SHA1";
    public const int DefaultDigits = 6, DefaultPeriod = 30;
    /// <summary>Accepted clock skew in whole steps either side of the current one.</summary>
    public const int SkewSteps = 1;

    public static string Hotp(byte[] secret, long counter, int digits = DefaultDigits, string algorithm = DefaultAlgorithm)
    {
        if (digits is < 6 or > 8) throw new ArgumentOutOfRangeException(nameof(digits));
        var message = new byte[8];
        for (var i = 7; i >= 0; i--) { message[i] = (byte)(counter & 0xFF); counter >>= 8; }
        byte[] hash = algorithm.ToUpperInvariant() switch
        {
            "SHA1" => HMACSHA1.HashData(secret, message),
            "SHA256" => HMACSHA256.HashData(secret, message),
            "SHA512" => HMACSHA512.HashData(secret, message),
            _ => throw new ArgumentException("Unsupported algorithm.", nameof(algorithm))
        };
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        long modulus = 1; for (var i = 0; i < digits; i++) modulus *= 10;
        return (binary % modulus).ToString().PadLeft(digits, '0');
    }

    public static long Step(DateTimeOffset now, int period = DefaultPeriod) => now.ToUnixTimeSeconds() / period;
    public static string Code(byte[] secret, DateTimeOffset now, int digits = DefaultDigits, int period = DefaultPeriod, string algorithm = DefaultAlgorithm) => Hotp(secret, Step(now, period), digits, algorithm);
    public static int SecondsLeft(DateTimeOffset now, int period = DefaultPeriod) => period - (int)(now.ToUnixTimeSeconds() % period);

    /// <summary>
    /// Checks a typed code against the current step and one step either side. Returns the matched step so the caller can
    /// refuse that step (and earlier ones) afterwards: a code that was accepted once cannot be replayed. Returns -1 on no match.
    /// </summary>
    public static long Verify(byte[] secret, string? code, DateTimeOffset now, long lastAcceptedStep = -1, int digits = DefaultDigits, int period = DefaultPeriod, string algorithm = DefaultAlgorithm)
    {
        if (code == null) return -1;
        var typed = code.Replace(" ", "");
        if (typed.Length != digits || !typed.All(char.IsAsciiDigit)) return -1;
        var current = Step(now, period);
        long matched = -1;
        // Every candidate is compared so the time taken does not reveal which step matched.
        for (var delta = -SkewSteps; delta <= SkewSteps; delta++)
        {
            var step = current + delta;
            if (step < 0) continue;
            var expected = Hotp(secret, step, digits, algorithm);
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(typed)) && step > lastAcceptedStep && matched < 0) matched = step;
        }
        return matched;
    }

    public static byte[] NewSecret(int bytes = 20) => RandomNumberGenerator.GetBytes(bytes);

    /// <summary>Builds the standard otpauth://totp/ URI shown once as text and as a QR code.</summary>
    public static string Uri(string issuer, string account, byte[] secret, int digits = DefaultDigits, int period = DefaultPeriod, string algorithm = DefaultAlgorithm)
    {
        var label = System.Uri.EscapeDataString(issuer) + ":" + System.Uri.EscapeDataString(account);
        return $"otpauth://totp/{label}?secret={Base32.Encode(secret)}&issuer={System.Uri.EscapeDataString(issuer)}&algorithm={algorithm.ToUpperInvariant()}&digits={digits}&period={period}";
    }
}

/// <summary>The published RFC 6238 Appendix B test vectors (8 digits), so the app and its checks can prove the implementation against the standard.</summary>
public static class Rfc6238Vectors
{
    public static readonly (long Time, string Algorithm, string Code)[] All =
    {
        (59, "SHA1", "94287082"), (59, "SHA256", "46119246"), (59, "SHA512", "90693936"),
        (1111111109, "SHA1", "07081804"), (1111111109, "SHA256", "68084774"), (1111111109, "SHA512", "25091201"),
        (1111111111, "SHA1", "14050471"), (1111111111, "SHA256", "67062674"), (1111111111, "SHA512", "99943326"),
        (1234567890, "SHA1", "89005924"), (1234567890, "SHA256", "91819424"), (1234567890, "SHA512", "93441116"),
        (2000000000, "SHA1", "69279037"), (2000000000, "SHA256", "90698825"), (2000000000, "SHA512", "38618901"),
        (20000000000, "SHA1", "65353130"), (20000000000, "SHA256", "77737706"), (20000000000, "SHA512", "47863826")
    };

    public static byte[] SecretFor(string algorithm) => System.Text.Encoding.ASCII.GetBytes(algorithm switch
    {
        "SHA1" => "12345678901234567890",
        "SHA256" => "12345678901234567890123456789012",
        _ => "1234567890123456789012345678901234567890123456789012345678901234"
    });

    /// <summary>Computes each vector with the live implementation. A tampered expected code in <paramref name="vectors"/> is reported as a failure.</summary>
    public static (int Passed, int Total, IReadOnlyList<string> Failures) Run((long Time, string Algorithm, string Code)[]? vectors = null)
    {
        vectors ??= All;
        var failures = new List<string>(); var passed = 0;
        foreach (var (time, algorithm, expected) in vectors)
        {
            var actual = Totp.Code(SecretFor(algorithm), DateTimeOffset.FromUnixTimeSeconds(time), 8, 30, algorithm);
            if (actual == expected) passed++; else failures.Add($"{algorithm} at {time}: expected {expected}, got {actual}");
        }
        return (passed, vectors.Length, failures);
    }
}
