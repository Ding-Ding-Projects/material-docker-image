using System.Speech.Synthesis;
using System.Windows.Threading;

namespace MaterialDockerImage.Services;

/// <summary>Optional spoken narration of notifications. Off by default, one utterance at a time, debounced.</summary>
public static class Narrator
{
    private static SpeechSynthesizer? synthesizer;
    private static DateTime last = DateTime.MinValue;
    private static string lastText = "";
    public static Func<bool> Enabled { get; set; } = () => false;
    public static string Voice { get; set; } = "";
    public static int Rate { get; set; }
    public static string? Unavailable { get; private set; }

    public static IReadOnlyList<string> Voices()
    {
        try { using var s = new SpeechSynthesizer(); return s.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo.Name).ToList(); }
        catch (Exception e) when (e is PlatformNotSupportedException or InvalidOperationException or System.Runtime.InteropServices.COMException or TypeInitializationException) { Unavailable = e.Message; return []; }
    }

    public static void Speak(string text)
    {
        if (!Enabled() || string.IsNullOrWhiteSpace(text)) return;
        if (text == lastText && DateTime.UtcNow - last < TimeSpan.FromSeconds(3)) return;
        last = DateTime.UtcNow; lastText = text;
        try
        {
            synthesizer ??= new SpeechSynthesizer();
            synthesizer.SpeakAsyncCancelAll();
            synthesizer.Rate = Math.Clamp(Rate, -10, 10);
            if (Voice.Length > 0) { try { synthesizer.SelectVoice(Voice); } catch (ArgumentException) { } }
            synthesizer.SpeakAsync(text);
        }
        catch (Exception e) when (e is PlatformNotSupportedException or InvalidOperationException or System.Runtime.InteropServices.COMException or TypeInitializationException) { Unavailable = e.Message; }
    }
}
