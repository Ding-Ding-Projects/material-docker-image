using System;

namespace MaterialDockerImage.UI;

/// <summary>
/// Scales the amplitude of an uncompressed PCM WAV file so a cue can play at the chosen volume with System.Media.SoundPlayer, which has no volume of its own.
/// Pure byte work: no file, no audio device. A file that is not plain PCM (8, 16 or 24 bits) is refused with null so the caller stays silent
/// instead of playing it louder than the person asked for.
/// </summary>
public static class WavScaler
{
    public const int MaxBytes = 4 * 1024 * 1024;

    /// <summary>Returns a scaled copy of <paramref name="wav"/>, or null when the bytes are not a supported WAV file. A gain of 1 returns an identical copy and 0 returns silence.</summary>
    public static byte[]? Scale(byte[] wav, double gain)
    {
        if (wav == null || wav.Length < 44 || wav.Length > MaxBytes || !double.IsFinite(gain)) return null;
        gain = Math.Clamp(gain, 0, 1);
        if (!Tag(wav, 0, "RIFF") || !Tag(wav, 8, "WAVE")) return null;
        int bits = 0, format = 0, dataStart = -1, dataLength = 0;
        var position = 12;
        while (position + 8 <= wav.Length)
        {
            var size = (long)BitConverter.ToUInt32(wav, position + 4);
            var body = position + 8;
            if (Tag(wav, position, "fmt "))
            {
                if (size < 16 || body + 16 > wav.Length) return null;
                format = BitConverter.ToUInt16(wav, body);
                bits = BitConverter.ToUInt16(wav, body + 14);
            }
            else if (Tag(wav, position, "data"))
            {
                dataStart = body;
                dataLength = (int)Math.Min(size, wav.Length - body);
                break;
            }
            var next = body + size + (size & 1);
            if (next > wav.Length) return null;
            position = (int)next;
        }
        if (format != 1 || dataStart < 0 || bits is not (8 or 16 or 24)) return null;
        var bytesPerSample = bits / 8;
        dataLength -= dataLength % bytesPerSample;
        var copy = (byte[])wav.Clone();
        if (gain >= 1) return copy;
        for (var i = dataStart; i + bytesPerSample <= dataStart + dataLength; i += bytesPerSample)
        {
            switch (bits)
            {
                case 8:
                    copy[i] = (byte)Math.Clamp((int)Math.Round((wav[i] - 128) * gain) + 128, 0, 255);
                    break;
                case 16:
                    var s16 = (short)(wav[i] | (wav[i + 1] << 8));
                    var scaled16 = (short)Math.Clamp((int)Math.Round(s16 * gain), short.MinValue, short.MaxValue);
                    copy[i] = (byte)(scaled16 & 0xFF); copy[i + 1] = (byte)((scaled16 >> 8) & 0xFF);
                    break;
                default:
                    var s24 = wav[i] | (wav[i + 1] << 8) | (wav[i + 2] << 16);
                    if ((s24 & 0x800000) != 0) s24 |= unchecked((int)0xFF000000);
                    var scaled24 = (int)Math.Clamp(Math.Round(s24 * gain), -8388608, 8388607);
                    copy[i] = (byte)(scaled24 & 0xFF); copy[i + 1] = (byte)((scaled24 >> 8) & 0xFF); copy[i + 2] = (byte)((scaled24 >> 16) & 0xFF);
                    break;
            }
        }
        return copy;
    }

    private static bool Tag(byte[] data, int offset, string tag) =>
        offset + 4 <= data.Length && data[offset] == tag[0] && data[offset + 1] == tag[1] && data[offset + 2] == tag[2] && data[offset + 3] == tag[3];
}
