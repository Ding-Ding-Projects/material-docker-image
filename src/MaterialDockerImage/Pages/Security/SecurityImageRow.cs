using System.Globalization;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>One tagged local image (unique ID) in the Security list, with its cached Scout result for this session.</summary>
internal sealed record SecurityImageRow(string Reference, IReadOnlyList<string> OtherTags, string Id, string Size, string CreatedSince)
{
    public ScoutScanResult? Result { get; init; }

    public string Name => OtherTags.Count == 0 ? Reference : $"{Reference} (+{OtherTags.Count} {(OtherTags.Count == 1 ? "tag" : "tags")})";
    public string ShortId => (Id.StartsWith("sha256:", StringComparison.Ordinal) ? Id[7..] : Id) is var s && s.Length > 12 ? s[..12] : Id;
    public bool Analysed => Result is { Succeeded: true };
    public bool Failed => Result is { Succeeded: false };
    public ScoutSeverityCounts? Counts => Result is { Succeeded: true } r ? r.Counts : null;

    public string Count(ScoutSeverity severity) => Counts is { } c ? c.Of(severity).ToString("N0", CultureInfo.CurrentCulture) : "—";
    public Tone CountTone(ScoutSeverity severity) => Counts is { } c && c.Of(severity) > 0 ? ScoutSeverityUi.ToneOf(severity) : Tone.Neutral;
    public string FixableText => Counts is { } c ? c.Fixable.ToString("N0", CultureInfo.CurrentCulture) : "—";

    public string StatusText => Result switch
    {
        null => "Not analyzed",
        { Succeeded: true } r => r.AnalysedText,
        { State: ScoutState.NotSignedIn } => "Sign-in required",
        { State: ScoutState.PluginMissing } => "Scout missing",
        _ => "Analysis failed"
    };

    public Tone StatusTone => Result switch
    {
        null => Tone.Neutral,
        { Succeeded: true } => Tone.Success,
        { State: ScoutState.NotSignedIn or ScoutState.PluginMissing } => Tone.Warning,
        _ => Tone.Error
    };
}
