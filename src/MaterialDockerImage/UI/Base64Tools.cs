using System.Security.Cryptography;
using System.Text;

namespace MaterialDockerImage.UI;

public enum Base64Variant { Standard, UrlSafe, Mime }
public enum TextEncodingKind { Utf8, Utf16LE, Latin1, Ascii }

/// <summary>Outcome of a decode. On failure, <see cref="ErrorOffset"/> is the zero-based character index of the problem.</summary>
public sealed record Base64DecodeResult(bool Success, byte[] Bytes, string? Error, int ErrorOffset)
{
    public static Base64DecodeResult Fail(string error, int offset) => new(false, [], error, offset);
}

/// <summary>Outcome of converting text to bytes. On failure, <see cref="ErrorOffset"/> is the character index that cannot be represented.</summary>
public sealed record TextBytesResult(bool Success, byte[] Bytes, string? Error, int ErrorOffset);

public sealed record DataUrl(string MimeType, byte[] Bytes, bool WasBase64);

/// <summary>
/// Pure Base64 helpers (no WPF): standard, URL-safe and MIME variants, optional padding, strict or lenient decoding
/// with exact error offsets, strict text encodings, data: URLs, streamed file encoding and SHA-256.
/// </summary>
public static class Base64Tools
{
    public const int MimeLineLength = 76;
    private const string StandardAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    private const string UrlAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

    public static string EncodingName(TextEncodingKind kind) => kind switch
    {
        TextEncodingKind.Utf16LE => "UTF-16LE",
        TextEncodingKind.Latin1 => "Latin-1",
        TextEncodingKind.Ascii => "ASCII",
        _ => "UTF-8"
    };

    public static string VariantName(Base64Variant variant) => variant switch
    {
        Base64Variant.UrlSafe => "URL-safe",
        Base64Variant.Mime => "MIME",
        _ => "Standard"
    };

    /// <summary>Strict encoding that throws on characters or bytes the encoding cannot represent.</summary>
    public static Encoding StrictEncoding(TextEncodingKind kind) => kind switch
    {
        TextEncodingKind.Utf16LE => new UnicodeEncoding(false, false, true),
        TextEncodingKind.Latin1 => Encoding.GetEncoding("iso-8859-1", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
        TextEncodingKind.Ascii => Encoding.GetEncoding("us-ascii", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
        _ => new UTF8Encoding(false, true)
    };

    /// <summary>Converts text to bytes, reporting the first character the encoding cannot represent.</summary>
    public static TextBytesResult GetBytes(string text, TextEncodingKind kind)
    {
        try { return new(true, StrictEncoding(kind).GetBytes(text), null, -1); }
        catch (EncoderFallbackException e)
        {
            var offset = Math.Max(0, e.Index);
            var shown = e.CharUnknown != '\0' ? $"'{e.CharUnknown}' (U+{(int)e.CharUnknown:X4})" : $"U+{char.ConvertToUtf32(e.CharUnknownHigh, e.CharUnknownLow):X4}";
            return new(false, [], $"Character {shown} at offset {offset} cannot be represented in {EncodingName(kind)}.", offset);
        }
    }

    /// <summary>Converts bytes to text, returning null with the failing byte offset when they are not valid in the encoding.</summary>
    public static string? TryGetString(byte[] bytes, TextEncodingKind kind, out string? error)
    {
        error = null;
        if (kind == TextEncodingKind.Utf16LE && bytes.Length % 2 != 0) { error = "UTF-16LE text needs an even number of bytes."; return null; }
        try { return StrictEncoding(kind).GetString(bytes); }
        catch (DecoderFallbackException e) { error = $"Byte offset {Math.Max(0, e.Index)} is not valid {EncodingName(kind)}."; return null; }
    }

    public static string Encode(ReadOnlySpan<byte> data, Base64Variant variant, bool padding = true)
    {
        var text = Convert.ToBase64String(data);
        return Finish(text, variant, padding, 0);
    }

    /// <summary>Applies alphabet, padding and MIME line breaks to standard padded Base64 text.</summary>
    private static string Finish(string standard, Base64Variant variant, bool padding, int lineStart)
    {
        if (variant == Base64Variant.UrlSafe) standard = standard.Replace('+', '-').Replace('/', '_');
        if (!padding) standard = standard.TrimEnd('=');
        return variant == Base64Variant.Mime ? WrapLines(standard, lineStart) : standard;
    }

    /// <summary>Inserts CRLF every 76 characters (MIME). <paramref name="column"/> is the current line length.</summary>
    public static string WrapLines(string text, int column = 0)
    {
        var builder = new StringBuilder(text.Length + text.Length / MimeLineLength * 2 + 2);
        foreach (var c in text)
        {
            if (column == MimeLineLength) { builder.Append("\r\n"); column = 0; }
            builder.Append(c); column++;
        }
        return builder.ToString();
    }

    /// <summary>
    /// Decodes Base64 text. Strict mode accepts only the variant's alphabet, exact padding (when padding is on),
    /// canonical trailing bits and, for MIME, CRLF line breaks with lines of at most 76 characters. Lenient mode
    /// accepts either alphabet, ignores whitespace and tolerates missing padding.
    /// </summary>
    public static Base64DecodeResult Decode(string text, Base64Variant variant, bool strict, bool padding = true, Action<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        var output = new List<byte>(text.Length * 3 / 4 + 3);
        int buffer = 0, bits = 0, sextets = 0, pads = 0, padStart = -1, lastData = -1, lineLength = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if ((i & 0xFFFF) == 0 && i > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Invoke((double)i / text.Length);
            }
            var c = text[i];
            if (c is '\r' or '\n' or ' ' or '\t' or '\f' or '\v')
            {
                if (!strict) continue;
                if (variant == Base64Variant.Mime && c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') { i++; lineLength = 0; continue; }
                if (variant == Base64Variant.Mime && c == '\n') return Base64DecodeResult.Fail("MIME line breaks must be CRLF in strict mode.", i);
                return Base64DecodeResult.Fail($"Whitespace ({Describe(c)}) is not allowed in strict {VariantName(variant)} Base64.", i);
            }
            if (strict && variant == Base64Variant.Mime && ++lineLength > MimeLineLength)
                return Base64DecodeResult.Fail($"MIME lines must be at most {MimeLineLength} characters.", i);
            if (c == '=')
            {
                if (strict && !padding) return Base64DecodeResult.Fail("Padding is switched off, but '=' was found.", i);
                if (padStart < 0) padStart = i;
                pads++;
                if (pads > 2) return Base64DecodeResult.Fail("At most two '=' padding characters are allowed.", i);
                continue;
            }
            var value = Value(c, variant, strict);
            if (value < 0) return Base64DecodeResult.Fail($"Character {Describe(c)} is not in the {(strict ? VariantName(variant) + " " : "")}Base64 alphabet.", i);
            if (padStart >= 0) return Base64DecodeResult.Fail("Data continues after '=' padding.", i);
            buffer = (buffer << 6) | value; bits += 6; sextets++; lastData = i;
            if (bits >= 8) { bits -= 8; output.Add((byte)(buffer >> bits)); buffer &= (1 << bits) - 1; }
        }
        var remainder = sextets % 4;
        if (remainder == 1) return Base64DecodeResult.Fail("The text ends with a single character after the last full group, which cannot encode a byte.", Math.Max(0, lastData));
        var expectedPads = remainder == 0 ? 0 : 4 - remainder;
        if (pads > 0 && pads != expectedPads) return Base64DecodeResult.Fail($"Expected {expectedPads} padding character(s) but found {pads}.", padStart);
        if (strict)
        {
            if (padding && expectedPads > 0 && pads == 0) return Base64DecodeResult.Fail($"Missing padding: {expectedPads} '=' expected at the end.", text.Length);
            if (bits > 0 && buffer != 0) return Base64DecodeResult.Fail("Non-canonical encoding: unused bits in the last character are not zero.", lastData);
        }
        progress?.Invoke(1);
        return new(true, output.ToArray(), null, -1);
    }

    private static int Value(char c, Base64Variant variant, bool strict)
    {
        var alphabet = variant == Base64Variant.UrlSafe ? UrlAlphabet : StandardAlphabet;
        var index = alphabet.IndexOf(c);
        if (index >= 0) return index;
        if (strict) return -1;
        return c switch { '+' or '-' => 62, '/' or '_' => 63, _ => -1 };
    }

    private static string Describe(char c) => c switch
    {
        '\r' => "carriage return",
        '\n' => "line feed",
        '\t' => "tab",
        ' ' => "space",
        _ => char.IsControl(c) || char.IsWhiteSpace(c) ? $"U+{(int)c:X4}" : $"'{c}' (U+{(int)c:X4})"
    };

    /// <summary>Builds an RFC 2397 data: URL with standard Base64.</summary>
    public static string BuildDataUrl(string mimeType, ReadOnlySpan<byte> data)
    {
        var mime = string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType.Trim();
        if (mime.IndexOfAny([',', '\r', '\n', ' ']) >= 0) throw new ArgumentException("The MIME type cannot contain commas, spaces or line breaks.", nameof(mimeType));
        return "data:" + mime + ";base64," + Convert.ToBase64String(data);
    }

    /// <summary>Parses a data: URL (Base64 or percent-encoded). Returns false with a reason when it is not valid.</summary>
    public static bool TryParseDataUrl(string text, out DataUrl? result, out string? error)
    {
        result = null; error = null;
        var value = text.Trim();
        if (!value.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) { error = "A data: URL must start with \"data:\"."; return false; }
        var comma = value.IndexOf(',');
        if (comma < 0) { error = "A data: URL needs a comma before the data."; return false; }
        var header = value[5..comma];
        var body = value[(comma + 1)..];
        var parts = header.Split(';');
        var base64 = parts.Length > 1 && parts[^1].Equals("base64", StringComparison.OrdinalIgnoreCase);
        var mime = parts[0].Length == 0 ? "text/plain;charset=US-ASCII" : string.Join(";", parts.Take(base64 ? parts.Length - 1 : parts.Length));
        if (base64)
        {
            var decoded = Decode(Uri.UnescapeDataString(body), Base64Variant.Standard, false);
            if (!decoded.Success) { error = $"{decoded.Error} (offset {decoded.ErrorOffset} in the data part, which starts at offset {comma + 1})"; return false; }
            result = new(mime, decoded.Bytes, true);
            return true;
        }
        try { result = new(mime, Encoding.Latin1.GetBytes(Uri.UnescapeDataString(body)), false); return true; }
        catch (UriFormatException e) { error = e.Message; return false; }
    }

    public static string Sha256Hex(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>
    /// Streams a file or other stream into Base64 text in chunks (MIME line breaks preserved across chunks),
    /// reporting progress in bytes and returning the SHA-256 of the input.
    /// </summary>
    public static async Task<string> EncodeStreamAsync(Stream input, TextWriter output, Base64Variant variant, bool padding, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        // 57 input bytes make exactly one 76-character MIME line, and 57 is a multiple of 3.
        const int chunk = 57 * 1024 * 3;
        var buffer = new byte[chunk];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;
        var pending = 0;
        var firstLine = true;
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(pending, chunk - pending), cancellationToken);
            if (read == 0) break;
            hash.AppendData(buffer, pending, read);
            pending += read; total += read;
            if (pending < chunk) continue;
            await WriteChunk(buffer.AsSpan(0, pending).ToArray(), false);
            pending = 0;
            progress?.Report(total);
        }
        if (pending > 0 || total == 0) await WriteChunk(buffer.AsSpan(0, pending).ToArray(), true);
        progress?.Report(total);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();

        async Task WriteChunk(byte[] bytes, bool last)
        {
            if (bytes.Length == 0) return;
            var text = Convert.ToBase64String(bytes);
            if (variant == Base64Variant.UrlSafe) text = text.Replace('+', '-').Replace('/', '_');
            if (!padding && last) text = text.TrimEnd('=');
            if (variant == Base64Variant.Mime)
            {
                // Each full chunk is a whole number of 76-character lines, so lines restart at every chunk.
                if (!firstLine) await output.WriteAsync("\r\n");
                text = WrapLines(text);
            }
            firstLine = false;
            await output.WriteAsync(text.AsMemory(), cancellationToken);
        }
    }
}
