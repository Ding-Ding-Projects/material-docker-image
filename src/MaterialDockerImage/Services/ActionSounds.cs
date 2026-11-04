using System.IO;
using System.Media;
using System.Windows;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Services;

/// <summary>
/// The single playback path for action sound effects. A cue never blocks the action, never changes its result, and plays only
/// when the Settings switch is on, Low stimulation is off, the window is not producing captures, the volume is above zero and it is not quiet hours. The mapping is in SoundCueRules.
/// </summary>
public static class ActionSounds
{
    /// <summary>Plays a cue when the settings allow it. Safe to call from any thread.</summary>
    public static void Play(SoundCue cue)
    {
        var host = AppHost.Current;
        if (host == null || !SoundCueRules.MayPlay(host.Settings, host.IsCapture)) return;
        Emit(cue);
    }

    /// <summary>
    /// Plays without re-checking the Settings switch (the caller applied the same rule), but never inside the quiet hours or at volume zero.
    /// At 100 percent the Windows system sound plays at its own level. Below that, a copy of the sound's WAV file scaled to the chosen volume plays
    /// through SoundPlayer; if that copy cannot be made (no file, a compressed file, an unreadable file) the cue stays silent rather than playing too loud.
    /// A playback problem is swallowed: it never reaches the action that asked for the cue.
    /// </summary>
    public static void Emit(SoundCue cue)
    {
        var settings = AppHost.Current?.Settings;
        var volume = settings?.SoundVolume ?? 100;
        if (volume <= 0) return;
        if (settings != null && QuietHours.IsQuiet(settings.QuietHoursEnabled, settings.QuietHoursStart, settings.QuietHoursEnd, DateTime.Now)) return;
        _ = Task.Run(() =>
        {
            try
            {
                if (volume < 100)
                {
                    var scaled = ScaledCopy(cue, volume);
                    if (scaled == null) return;
                    using var stream = new MemoryStream(scaled, false);
                    using var quiet = new SoundPlayer(stream);
                    quiet.PlaySync();
                    return;
                }
                switch (cue)
                {
                    case SoundCue.Failure: SystemSounds.Hand.Play(); break;
                    case SoundCue.Warning: SystemSounds.Exclamation.Play(); break;
                    case SoundCue.Removal: SystemSounds.Question.Play(); break;
                    case SoundCue.Copy: case SoundCue.DropRefused: SystemSounds.Beep.Play(); break;
                    case SoundCue.Drop:
                        var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "tada.wav");
                        if (File.Exists(file)) { using var player = new SoundPlayer(file); player.PlaySync(); } else SystemSounds.Asterisk.Play();
                        break;
                    default: SystemSounds.Asterisk.Play(); break;
                }
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or TimeoutException or ArgumentException or NotSupportedException or System.Security.SecurityException or FormatException) { }
        });
    }

    private static readonly object CacheLock = new();
    private static readonly Dictionary<SoundCue, byte[]?> Cache = new();
    private static int cachedVolume = -1;

    /// <summary>The cue's system sound file scaled to the volume, cached for the current volume only. Null when it cannot be made.</summary>
    private static byte[]? ScaledCopy(SoundCue cue, int volume)
    {
        lock (CacheLock)
        {
            if (cachedVolume != volume) { Cache.Clear(); cachedVolume = volume; }
            if (Cache.TryGetValue(cue, out var hit)) return hit;
        }
        byte[]? result = null;
        try
        {
            var path = SoundFile(cue);
            if (path != null && new FileInfo(path) is { Exists: true, Length: > 0 and <= WavScaler.MaxBytes }) result = WavScaler.Scale(File.ReadAllBytes(path), QuietHours.Gain(volume));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException) { result = null; }
        lock (CacheLock) { if (cachedVolume == volume) Cache[cue] = result; }
        return result;
    }

    /// <summary>The WAV file Windows plays for the cue, from the person's own sound scheme, falling back to the stock file name.</summary>
    private static string? SoundFile(SoundCue cue)
    {
        var (eventName, stock) = cue switch
        {
            SoundCue.Failure => ("SystemHand", "Windows Critical Stop.wav"),
            SoundCue.Warning => ("SystemExclamation", "Windows Exclamation.wav"),
            SoundCue.Removal => ("SystemQuestion", "Windows Question.wav"),
            SoundCue.Copy or SoundCue.DropRefused => (".Default", "Windows Default.wav"),
            SoundCue.Drop => ("", "tada.wav"),
            _ => ("SystemAsterisk", "Windows Background.wav")
        };
        var media = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");
        if (eventName.Length > 0)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"AppEvents\Schemes\Apps\.Default\" + eventName + @"\.Current");
                var configured = key?.GetValue(null) as string;
                if (string.IsNullOrWhiteSpace(configured)) return null; // the person's scheme has no sound for this event
                var expanded = Environment.ExpandEnvironmentVariables(configured);
                if (File.Exists(expanded)) return expanded;
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException) { }
        }
        var fallback = Path.Combine(media, stock);
        return File.Exists(fallback) ? fallback : null;
    }

    /// <summary>Copies text and plays the copy cue only after the clipboard accepted it.</summary>
    public static void Copy(string text)
    {
        Clipboard.SetText(text);
        Play(SoundCue.Copy);
    }
}
