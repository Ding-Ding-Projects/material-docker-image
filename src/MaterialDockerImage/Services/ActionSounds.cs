using System.IO;
using System.Media;
using System.Windows;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Services;

/// <summary>
/// The single playback path for action sound effects. A cue never blocks the action, never changes its result, and plays only
/// when the Settings switch is on, Low stimulation is off and the window is not producing captures. The mapping is in SoundCueRules.
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

    /// <summary>Plays without re-checking the settings; the caller has already applied the same rule (the notifier's own switch).</summary>
    public static void Emit(SoundCue cue)
    {
        _ = Task.Run(() =>
        {
            try
            {
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
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or TimeoutException) { }
        });
    }

    /// <summary>Copies text and plays the copy cue only after the clipboard accepted it.</summary>
    public static void Copy(string text)
    {
        Clipboard.SetText(text);
        Play(SoundCue.Copy);
    }
}
