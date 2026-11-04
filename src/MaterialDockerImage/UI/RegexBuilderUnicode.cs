using System.Globalization;

namespace MaterialDockerImage.UI;

/// <summary>One named Unicode block that .NET reads as <c>\p{IsName}</c>, with the code point range that the engine itself reports.</summary>
public sealed record RegexUnicodeBlock(string Name, int Start, int End)
{
    public string Positive => @"\p{Is" + Name + "}";
    public string Negative => @"\P{Is" + Name + "}";
    public string Range => $"U+{Start:X4} to U+{End:X4}";
    /// <summary>The block name with a space before each capital letter and after "and", for display only.</summary>
    public string Title => System.Text.RegularExpressions.Regex.Replace(Name, "(?<=[a-z0-9])(?=[A-Z])|(?<=[a-z])(?=and[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(250));
}

/// <summary>
/// The Unicode blocks that .NET supports in <c>\p{IsName}</c>, from Basic Latin to Specials. .NET knows only blocks of the Basic Multilingual Plane;
/// it has no Unicode script properties (<c>\p{Greek}</c>), no later blocks and no characters outside the Basic Multilingual Plane. The table was
/// produced by asking the engine for the first and last character of each name, and the checks ask it again.
/// </summary>
public static class RegexUnicodeBlocks
{
    private const string Spec = "BasicLatin:0000-007F;Latin-1Supplement:0080-00FF;LatinExtended-A:0100-017F;LatinExtended-B:0180-024F;IPAExtensions:0250-02AF;SpacingModifierLetters:02B0-02FF;CombiningDiacriticalMarks:0300-036F;Greek:0370-03FF;GreekandCoptic:0370-03FF;Cyrillic:0400-04FF;CyrillicSupplement:0500-052F;Armenian:0530-058F;Hebrew:0590-05FF;Arabic:0600-06FF;Syriac:0700-074F;Thaana:0780-07BF;Devanagari:0900-097F;Bengali:0980-09FF;Gurmukhi:0A00-0A7F;Gujarati:0A80-0AFF;Oriya:0B00-0B7F;Tamil:0B80-0BFF;Telugu:0C00-0C7F;Kannada:0C80-0CFF;Malayalam:0D00-0D7F;Sinhala:0D80-0DFF;Thai:0E00-0E7F;Lao:0E80-0EFF;Tibetan:0F00-0FFF;Myanmar:1000-109F;Georgian:10A0-10FF;HangulJamo:1100-11FF;Ethiopic:1200-137F;Cherokee:13A0-13FF;UnifiedCanadianAboriginalSyllabics:1400-167F;Ogham:1680-169F;Runic:16A0-16FF;Tagalog:1700-171F;Hanunoo:1720-173F;Buhid:1740-175F;Tagbanwa:1760-177F;Khmer:1780-17FF;Mongolian:1800-18AF;Limbu:1900-194F;TaiLe:1950-197F;KhmerSymbols:19E0-19FF;PhoneticExtensions:1D00-1D7F;LatinExtendedAdditional:1E00-1EFF;GreekExtended:1F00-1FFF;GeneralPunctuation:2000-206F;SuperscriptsandSubscripts:2070-209F;CurrencySymbols:20A0-20CF;CombiningDiacriticalMarksforSymbols:20D0-20FF;CombiningMarksforSymbols:20D0-20FF;LetterlikeSymbols:2100-214F;NumberForms:2150-218F;Arrows:2190-21FF;MathematicalOperators:2200-22FF;MiscellaneousTechnical:2300-23FF;ControlPictures:2400-243F;OpticalCharacterRecognition:2440-245F;EnclosedAlphanumerics:2460-24FF;BoxDrawing:2500-257F;BlockElements:2580-259F;GeometricShapes:25A0-25FF;MiscellaneousSymbols:2600-26FF;Dingbats:2700-27BF;MiscellaneousMathematicalSymbols-A:27C0-27EF;SupplementalArrows-A:27F0-27FF;BraillePatterns:2800-28FF;SupplementalArrows-B:2900-297F;MiscellaneousMathematicalSymbols-B:2980-29FF;SupplementalMathematicalOperators:2A00-2AFF;MiscellaneousSymbolsandArrows:2B00-2BFF;CJKRadicalsSupplement:2E80-2EFF;KangxiRadicals:2F00-2FDF;IdeographicDescriptionCharacters:2FF0-2FFF;CJKSymbolsandPunctuation:3000-303F;Hiragana:3040-309F;Katakana:30A0-30FF;Bopomofo:3100-312F;HangulCompatibilityJamo:3130-318F;Kanbun:3190-319F;BopomofoExtended:31A0-31BF;KatakanaPhoneticExtensions:31F0-31FF;EnclosedCJKLettersandMonths:3200-32FF;CJKCompatibility:3300-33FF;CJKUnifiedIdeographsExtensionA:3400-4DBF;YijingHexagramSymbols:4DC0-4DFF;CJKUnifiedIdeographs:4E00-9FFF;YiSyllables:A000-A48F;YiRadicals:A490-A4CF;HangulSyllables:AC00-D7AF;HighSurrogates:D800-DB7F;HighPrivateUseSurrogates:DB80-DBFF;LowSurrogates:DC00-DFFF;PrivateUse:E000-F8FF;PrivateUseArea:E000-F8FF;CJKCompatibilityIdeographs:F900-FAFF;AlphabeticPresentationForms:FB00-FB4F;ArabicPresentationForms-A:FB50-FDFF;VariationSelectors:FE00-FE0F;CombiningHalfMarks:FE20-FE2F;CJKCompatibilityForms:FE30-FE4F;SmallFormVariants:FE50-FE6F;ArabicPresentationForms-B:FE70-FEFF;HalfwidthandFullwidthForms:FF00-FFEF;Specials:FFF0-FFFF;";

    public static readonly IReadOnlyList<RegexUnicodeBlock> All = Spec.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(entry =>
    {
        var name = entry[..entry.IndexOf(':')];
        var range = entry[(entry.IndexOf(':') + 1)..].Split('-');
        return new RegexUnicodeBlock(name, int.Parse(range[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture), int.Parse(range[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }).ToList();

    /// <summary>The note shown beside the picker: what .NET cannot do with blocks and scripts.</summary>
    public const string ScriptNotice = @".NET supports Unicode blocks only. It has no script properties such as \p{Greek}, and its blocks stop at the Basic Multilingual Plane.";

    /// <summary>The blocks whose name, display title or range contains the query (ignoring case); an empty query lists all.</summary>
    public static IReadOnlyList<RegexUnicodeBlock> Search(string? query)
    {
        var q = (query ?? "").Trim();
        if (q.Length == 0) return All;
        return All.Where(b => b.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || b.Title.Contains(q, StringComparison.OrdinalIgnoreCase) || b.Range.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>The block that holds a character (the first when two names share the range), or null for a surrogate pair or an unknown value.</summary>
    public static RegexUnicodeBlock? For(int codePoint) => codePoint is < 0 or > 0xFFFF ? null : All.FirstOrDefault(b => codePoint >= b.Start && codePoint <= b.End);

    /// <summary>The pattern text for a block, or for everything outside it.</summary>
    public static string Pattern(string name, bool negated)
    {
        var block = All.FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("'" + name + "' is not a Unicode block that .NET knows.");
        return negated ? block.Negative : block.Positive;
    }
}
