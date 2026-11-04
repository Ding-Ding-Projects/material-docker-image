using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MaterialDockerImage.UI;

/// <summary>
/// A small dependency-free QR Code encoder (ISO/IEC 18004): byte mode, error-correction level L, versions 1 to 10 (up to 271 bytes).
/// It draws nothing and never touches the network, so the pairing secret stays in this process.
/// </summary>
public static class QrCode
{
    // Per version 1..10, level L: data codewords per block group (count, size) twice, and error-correction codewords per block.
    private static readonly (int Ec, int Blocks1, int Data1, int Blocks2, int Data2)[] Spec =
    {
        (7, 1, 19, 0, 0), (10, 1, 34, 0, 0), (15, 1, 55, 0, 0), (20, 1, 80, 0, 0), (26, 1, 108, 0, 0),
        (18, 2, 68, 0, 0), (20, 2, 78, 0, 0), (24, 2, 97, 0, 0), (30, 2, 116, 0, 0), (18, 2, 68, 2, 69)
    };
    private static readonly int[][] Alignment =
    {
        Array.Empty<int>(), new[] { 6, 18 }, new[] { 6, 22 }, new[] { 6, 26 }, new[] { 6, 30 }, new[] { 6, 34 },
        new[] { 6, 22, 38 }, new[] { 6, 24, 42 }, new[] { 6, 26, 46 }, new[] { 6, 28, 50 }
    };
    public const int MaxVersion = 10;
    public const int QuietZone = 4;

    /// <summary>The largest text, in UTF-8 bytes, that fits.</summary>
    public static int MaxBytes => DataCodewords(MaxVersion) - (MaxVersion >= 10 ? 3 : 2);

    public static int DataCodewords(int version) { var s = Spec[version - 1]; return s.Blocks1 * s.Data1 + s.Blocks2 * s.Data2; }
    public static int Size(int version) => 17 + 4 * version;

    /// <summary>Encodes UTF-8 text. Returns the dark-module matrix (without the quiet zone). Throws when the text is too long.</summary>
    public static bool[,] Encode(string text)
    {
        var data = Encoding.UTF8.GetBytes(text);
        var version = 0;
        for (var v = 1; v <= MaxVersion; v++)
        {
            var overhead = v >= 10 ? 3 : 2;
            if (data.Length + overhead <= DataCodewords(v)) { version = v; break; }
        }
        if (version == 0) throw new ArgumentException("The text is too long for a QR code in this encoder (" + MaxBytes + " bytes at most).");
        var codewords = Interleave(version, DataBits(version, data));
        return Build(version, codewords).Matrix;
    }

    public static int VersionFor(string text) { var n = Encoding.UTF8.GetByteCount(text); for (var v = 1; v <= MaxVersion; v++) if (n + (v >= 10 ? 3 : 2) <= DataCodewords(v)) return v; return 0; }

    internal static byte[] DataBits(int version, byte[] data)
    {
        var bits = new List<bool>();
        void Put(int value, int count) { for (var i = count - 1; i >= 0; i--) bits.Add(((value >> i) & 1) == 1); }
        Put(0b0100, 4);
        Put(data.Length, version >= 10 ? 16 : 8);
        foreach (var b in data) Put(b, 8);
        var capacity = DataCodewords(version) * 8;
        Put(0, Math.Min(4, capacity - bits.Count));
        while (bits.Count % 8 != 0) bits.Add(false);
        var bytes = new List<byte>();
        for (var i = 0; i < bits.Count; i += 8) { var v = 0; for (var j = 0; j < 8; j++) v = (v << 1) | (bits[i + j] ? 1 : 0); bytes.Add((byte)v); }
        for (var pad = 0; bytes.Count < DataCodewords(version); pad++) bytes.Add(pad % 2 == 0 ? (byte)0xEC : (byte)0x11);
        return bytes.ToArray();
    }

    /// <summary>Splits into blocks, appends Reed-Solomon error correction and interleaves, as the standard requires.</summary>
    internal static byte[] Interleave(int version, byte[] data)
    {
        var s = Spec[version - 1];
        var blocks = new List<byte[]>(); var ecs = new List<byte[]>();
        var offset = 0;
        for (var b = 0; b < s.Blocks1 + s.Blocks2; b++)
        {
            var size = b < s.Blocks1 ? s.Data1 : s.Data2;
            var block = data.AsSpan(offset, size).ToArray(); offset += size;
            blocks.Add(block); ecs.Add(ReedSolomon(block, s.Ec));
        }
        var result = new List<byte>();
        for (var i = 0; i < Math.Max(s.Data1, s.Data2); i++) foreach (var block in blocks) if (i < block.Length) result.Add(block[i]);
        for (var i = 0; i < s.Ec; i++) foreach (var ec in ecs) result.Add(ec[i]);
        return result.ToArray();
    }

    private static readonly byte[] Exp = new byte[512];
    private static readonly byte[] Log = new byte[256];
    static QrCode()
    {
        var x = 1;
        for (var i = 0; i < 255; i++) { Exp[i] = (byte)x; Log[x] = (byte)i; x <<= 1; if (x >= 256) x ^= 0x11D; }
        for (var i = 255; i < 512; i++) Exp[i] = Exp[i - 255];
    }
    private static byte Mul(int a, int b) => a == 0 || b == 0 ? (byte)0 : Exp[Log[a] + Log[b]];

    /// <summary>Reed-Solomon over GF(256) with the QR polynomial 0x11D. Exposed internally so a published vector can check it.</summary>
    internal static byte[] ReedSolomon(byte[] data, int ecCount)
    {
        var generator = new byte[] { 1 };
        for (var i = 0; i < ecCount; i++)
        {
            var next = new byte[generator.Length + 1];
            for (var j = 0; j < generator.Length; j++) { next[j] ^= generator[j]; next[j + 1] ^= Mul(generator[j], Exp[i]); }
            generator = next;
        }
        var remainder = new byte[ecCount];
        foreach (var b in data)
        {
            var factor = (byte)(b ^ remainder[0]);
            Array.Copy(remainder, 1, remainder, 0, ecCount - 1); remainder[ecCount - 1] = 0;
            for (var j = 0; j < ecCount; j++) remainder[j] ^= Mul(generator[j + 1], factor);
        }
        return remainder;
    }

    /// <summary>The 15 format bits for level L and a mask (BCH(15,5), XOR 0x5412).</summary>
    internal static int FormatBits(int mask)
    {
        var data = (0b01 << 3) | mask; var rem = data;
        for (var i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        return ((data << 10) | (rem & 0x3FF)) ^ 0x5412;
    }

    /// <summary>The 18 version bits for version 7 and above (BCH(18,6)).</summary>
    internal static int VersionBits(int version)
    {
        var rem = version;
        for (var i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
        return (version << 12) | (rem & 0xFFF);
    }

    private sealed record Built(bool[,] Matrix, int Mask);

    private static Built Build(int version, byte[] codewords)
    {
        var size = Size(version);
        var function = new bool[size, size];
        var best = default(bool[,]); var bestMask = 0; var bestPenalty = int.MaxValue;
        for (var mask = 0; mask < 8; mask++)
        {
            var modules = new bool[size, size];
            DrawFunctionPatterns(version, modules, function, mask);
            PlaceData(modules, function, codewords, mask);
            var penalty = Penalty(modules);
            if (penalty < bestPenalty) { bestPenalty = penalty; best = modules; bestMask = mask; }
        }
        return new Built(best!, bestMask);
    }

    private static void DrawFunctionPatterns(int version, bool[,] m, bool[,] f, int mask)
    {
        var size = m.GetLength(0);
        void Set(int r, int c, bool dark) { m[r, c] = dark; f[r, c] = true; }
        for (var i = 0; i < size; i++) { Set(6, i, i % 2 == 0); Set(i, 6, i % 2 == 0); }
        void Finder(int r0, int c0)
        {
            for (var dr = -1; dr <= 7; dr++)
                for (var dc = -1; dc <= 7; dc++)
                {
                    var r = r0 + dr; var c = c0 + dc;
                    if (r < 0 || c < 0 || r >= size || c >= size) continue;
                    var ring = Math.Max(Math.Abs(dr - 3), Math.Abs(dc - 3));
                    Set(r, c, ring != 4 && ring != 2 ? dr >= 0 && dr <= 6 && dc >= 0 && dc <= 6 : false);
                }
        }
        Finder(0, 0); Finder(0, size - 7); Finder(size - 7, 0);
        var centres = Alignment[version - 1];
        foreach (var r in centres)
            foreach (var c in centres)
            {
                if ((r == 6 && c == 6) || (r == 6 && c == size - 7) || (r == size - 7 && c == 6)) continue;
                for (var dr = -2; dr <= 2; dr++) for (var dc = -2; dc <= 2; dc++) Set(r + dr, c + dc, Math.Max(Math.Abs(dr), Math.Abs(dc)) != 1);
            }
        // Format information, two copies.
        var format = FormatBits(mask);
        bool Bit(int i) => ((format >> i) & 1) == 1;
        for (var i = 0; i <= 5; i++) Set(i, 8, Bit(i));
        Set(7, 8, Bit(6)); Set(8, 8, Bit(7)); Set(8, 7, Bit(8));
        for (var i = 9; i < 15; i++) Set(8, 14 - i, Bit(i));
        for (var i = 0; i < 8; i++) Set(8, size - 1 - i, Bit(i));
        for (var i = 8; i < 15; i++) Set(size - 15 + i, 8, Bit(i));
        Set(size - 8, 8, true);
        if (version >= 7)
        {
            var bits = VersionBits(version);
            for (var i = 0; i < 18; i++) { var dark = ((bits >> i) & 1) == 1; var a = size - 11 + i % 3; var b = i / 3; Set(a, b, dark); Set(b, a, dark); }
        }
    }

    private static bool Masked(int mask, int r, int c) => mask switch
    {
        0 => (r + c) % 2 == 0, 1 => r % 2 == 0, 2 => c % 3 == 0, 3 => (r + c) % 3 == 0,
        4 => (r / 2 + c / 3) % 2 == 0, 5 => r * c % 2 + r * c % 3 == 0, 6 => (r * c % 2 + r * c % 3) % 2 == 0, _ => ((r + c) % 2 + r * c % 3) % 2 == 0
    };

    private static void PlaceData(bool[,] m, bool[,] f, byte[] codewords, int mask)
    {
        var size = m.GetLength(0); var bit = 0; var total = codewords.Length * 8;
        for (var right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (var vertical = 0; vertical < size; vertical++)
                for (var j = 0; j < 2; j++)
                {
                    var c = right - j; var upward = ((right + 1) & 2) == 0; var r = upward ? size - 1 - vertical : vertical;
                    if (f[r, c]) continue;
                    var dark = bit < total && ((codewords[bit >> 3] >> (7 - (bit & 7))) & 1) == 1;
                    bit++;
                    m[r, c] = dark ^ Masked(mask, r, c);
                }
        }
    }

    private static int Penalty(bool[,] m)
    {
        var n = m.GetLength(0); var score = 0;
        for (var pass = 0; pass < 2; pass++)
            for (var a = 0; a < n; a++)
            {
                var run = 1;
                for (var b = 1; b < n; b++)
                {
                    bool Cell(int i) => pass == 0 ? m[a, i] : m[i, a];
                    if (Cell(b) == Cell(b - 1)) { run++; if (run == 5) score += 3; else if (run > 5) score++; } else run = 1;
                }
                for (var b = 0; b + 10 < n + 0 && b <= n - 11; b++)
                {
                    bool Cell(int i) => pass == 0 ? m[a, b + i] : m[b + i, a];
                    var p1 = new[] { true, false, true, true, true, false, true, false, false, false, false };
                    var p2 = p1.Reverse().ToArray();
                    if (Enumerable.Range(0, 11).All(i => Cell(i) == p1[i]) || Enumerable.Range(0, 11).All(i => Cell(i) == p2[i])) score += 40;
                }
            }
        for (var r = 0; r < n - 1; r++) for (var c = 0; c < n - 1; c++) if (m[r, c] == m[r, c + 1] && m[r, c] == m[r + 1, c] && m[r, c] == m[r + 1, c + 1]) score += 3;
        var dark = 0; foreach (var cell in m) if (cell) dark++;
        score += Math.Abs(dark * 100 / (n * n) - 50) / 5 * 10;
        return score;
    }

    /// <summary>Reads the data bytes back out of a matrix (the reverse of placement). Used by checks to prove the matrix carries what was encoded.</summary>
    public static string? Decode(bool[,] matrix)
    {
        var size = matrix.GetLength(0);
        if ((size - 17) % 4 != 0) return null;
        var version = (size - 17) / 4;
        if (version is < 1 or > MaxVersion) return null;
        var f = new bool[size, size];
        var scratch = new bool[size, size];
        // Recover the mask from the first format copy.
        var format = 0;
        for (var i = 0; i <= 5; i++) format |= (matrix[i, 8] ? 1 : 0) << i;
        format |= (matrix[7, 8] ? 1 : 0) << 6; format |= (matrix[8, 8] ? 1 : 0) << 7; format |= (matrix[8, 7] ? 1 : 0) << 8;
        for (var i = 9; i < 15; i++) format |= (matrix[8, 14 - i] ? 1 : 0) << i;
        var mask = -1;
        for (var candidate = 0; candidate < 8; candidate++) if (FormatBits(candidate) == format) mask = candidate;
        if (mask < 0) return null;
        DrawFunctionPatterns(version, scratch, f, mask);
        var raw = new List<bool>();
        for (var right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (var vertical = 0; vertical < size; vertical++)
                for (var j = 0; j < 2; j++)
                {
                    var c = right - j; var upward = ((right + 1) & 2) == 0; var r = upward ? size - 1 - vertical : vertical;
                    if (f[r, c]) continue;
                    raw.Add(matrix[r, c] ^ Masked(mask, r, c));
                }
        }
        var codewords = new byte[raw.Count / 8];
        for (var i = 0; i < codewords.Length; i++) { var v = 0; for (var j = 0; j < 8; j++) v = (v << 1) | (raw[i * 8 + j] ? 1 : 0); codewords[i] = (byte)v; }
        // De-interleave the data codewords.
        var s = Spec[version - 1]; var blockCount = s.Blocks1 + s.Blocks2;
        var data = new List<byte>(); var index = 0;
        var perBlock = new List<byte>[blockCount]; for (var b = 0; b < blockCount; b++) perBlock[b] = new List<byte>();
        for (var i = 0; i < Math.Max(s.Data1, s.Data2); i++) for (var b = 0; b < blockCount; b++) { var size2 = b < s.Blocks1 ? s.Data1 : s.Data2; if (i < size2) perBlock[b].Add(codewords[index++]); }
        foreach (var block in perBlock) data.AddRange(block);
        if (data.Count < 3) return null;
        // Parse byte mode.
        var bits = new List<bool>(); foreach (var b in data) for (var j = 7; j >= 0; j--) bits.Add(((b >> j) & 1) == 1);
        int Read(int pos, int count) { var v = 0; for (var i = 0; i < count; i++) v = (v << 1) | (bits[pos + i] ? 1 : 0); return v; }
        if (Read(0, 4) != 0b0100) return null;
        var lengthBits = version >= 10 ? 16 : 8; var length = Read(4, lengthBits);
        var bytes = new byte[length];
        for (var i = 0; i < length; i++) bytes[i] = (byte)Read(4 + lengthBits + i * 8, 8);
        return Encoding.UTF8.GetString(bytes);
    }
}
