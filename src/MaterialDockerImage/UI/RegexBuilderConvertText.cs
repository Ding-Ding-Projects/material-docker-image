using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>
/// Shows the converter's notes and refusal reasons in the chosen language. The English stays in <see cref="RegexConversion.Notes"/> so the conversion logic and its checks do
/// not depend on a language; only the display goes through <paramref name="translate"/>. A refusal is a lead-in plus the sentences of the engine comparison, each translated
/// on its own, and the few notes that wrap such sentences in a parenthesis are taken apart the same way. Text the .NET framework wrote stays as it is.
/// </summary>
public static partial class RegexConverter
{
    private const string RefusalLead = "Not convertible: ";
    private const string PcreJoin = " With -P (PCRE2): ";
    private const string RipgrepHead = "ripgrep's default engine cannot read this pattern (";
    private const string RipgrepTail = ") so -P is added, which needs a ripgrep built with PCRE2.";

    private static readonly string[] RefusalTails =
    [
        " (checked against PCRE2, the nearest column; Perl has no column of its own).",
        " (checked against PCRE2, the closest column; Ruby's Onigmo engine has no column of its own).",
        " (checked against PCRE2, the closest column; Java has no column of its own).",
    ];

    private static readonly Regex NotSupported = new(@"^(?<label>.+?) is not supported by (?<name>[^()]+?)(?: \((?<note>.+)\))?\.$", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private static readonly Regex RenderedNote = new(@"^Rendered with your template for (?<language>.+) (?<kind>IsMatch|Matches|Replace|Split); the notes below describe the built-in snippet\.$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>The English sentences that <see cref="TranslateNote"/> translates piece by piece, so a check can ask the language lookup for each of them.</summary>
    public static IReadOnlyList<string> NotePieces { get; } = [RefusalLead.Trim(), PcreJoin.Trim(), RipgrepHead.TrimEnd(' ', '('), RipgrepTail.TrimStart(')', ' ').Trim(), .. RefusalTails.Select(t => t.Trim())];

    /// <summary>Translates one note or refusal of a conversion. With an identity function it returns the note unchanged.</summary>
    public static string TranslateNote(string note, Func<string, string> translate)
    {
        if (string.IsNullOrEmpty(note)) return note;
        string Piece(string text) => TranslatePiece(text, translate);
        if (note.StartsWith(RefusalLead, StringComparison.Ordinal)) return translate(RefusalLead.Trim()) + " " + Refusal(note[RefusalLead.Length..], translate);
        if (note.StartsWith(RipgrepHead, StringComparison.Ordinal) && note.EndsWith(RipgrepTail, StringComparison.Ordinal))
        {
            var inner = note[RipgrepHead.Length..^RipgrepTail.Length];
            return translate(RipgrepHead.TrimEnd(' ', '(')) + " (" + RegexText.Sentences(inner, Piece) + ") " + translate(RipgrepTail.TrimStart(')', ' ').Trim());
        }
        return RegexText.Sentences(note, Piece);
    }

    /// <summary>
    /// One sentence of the engine comparison. "X is not supported by Y (note)." is rebuilt from its translated pieces, because the sentence frame is a template and a
    /// template does not translate what its holes hold; "Construct label: note" is split at the known construct label for the same reason.
    /// </summary>
    private static string TranslatePiece(string text, Func<string, string> t)
    {
        try
        {
            var found = NotSupported.Match(text);
            if (found.Success)
            {
                var note = found.Groups["note"];
                return t($"{t(found.Groups["label"].Value)} is not supported by {found.Groups["name"].Value}" + (note.Success ? $" ({t(note.Value)})" : "") + ".");
            }
        }
        catch (RegexMatchTimeoutException) { }
        try
        {
            var rendered = RenderedNote.Match(text);
            if (rendered.Success)
            {
                var kind = rendered.Groups["kind"].Value switch { "IsMatch" => "Test for a match", "Matches" => "Find all matches", var other => other };
                return t($"Rendered with your template for {rendered.Groups["language"].Value} {t(kind)}; the notes below describe the built-in snippet.");
            }
        }
        catch (RegexMatchTimeoutException) { }
        foreach (var construct in RegexFlavors.Constructs)
            if (text.StartsWith(construct.Label + ": ", StringComparison.Ordinal)) return t(construct.Label) + ": " + t(text[(construct.Label.Length + 2)..]);
        return t(text);
    }

    private static string Refusal(string reason, Func<string, string> translate)
    {
        string Piece(string text) => TranslatePiece(text, translate);
        var tail = "";
        foreach (var known in RefusalTails)
            if (reason.EndsWith(known, StringComparison.Ordinal)) { tail = " " + translate(known.Trim()); reason = reason[..^known.Length]; break; }
        var join = reason.IndexOf(PcreJoin, StringComparison.Ordinal);
        if (join < 0) return RegexText.Sentences(reason, Piece) + tail;
        return RegexText.Sentences(reason[..join], Piece) + " " + translate(PcreJoin.Trim()) + " " + RegexText.Sentences(reason[(join + PcreJoin.Length)..], Piece) + tail;
    }
}
