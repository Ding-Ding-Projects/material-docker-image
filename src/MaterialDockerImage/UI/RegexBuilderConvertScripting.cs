using System.Text;

namespace MaterialDockerImage.UI;

/// <summary>
/// Conversions to Perl and Ruby source and to a complete PowerShell script. The pattern is always written inside a single-quoted string, where only the backslash
/// and the quote need escaping, so the text reaches the engine exactly as typed. Neither Perl nor Ruby has a column in the flavor table: PCRE2 is the nearest
/// engine for Perl and the closest available one for Ruby, so differences that are not listed there are not known and the notes say so.
/// </summary>
public static partial class RegexConverter
{
    /// <summary>A single-quoted Perl or Ruby string: only \ and ' are escaped, which is exact in both languages.</summary>
    public static string SingleQuoted(string text) => "'" + text.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

    private static RegexConversion Perl(string pattern, RegexBuilderFlags flags, RegexCodeKind kind, string replacement)
    {
        const string target = "Perl";
        if (flags.RightToLeft) return Refuse(target, "Perl has no right-to-left matching.");
        var (refusals, differences) = Compatibility(pattern, flags, "pcre2");
        if (refusals.Count > 0) return new(target, "", false, false, ["Not convertible: " + string.Join(" ", refusals) + " (checked against PCRE2, the nearest column; Perl has no column of its own)."]);
        var notes = new List<string> { "The comparison table has no Perl column. PCRE2 was used as the nearest engine, so differences that are not in the table are not known. Test the pattern in Perl." };
        var letters = (flags.IgnoreCase ? "i" : "") + (flags.Multiline ? "m" : "") + (flags.Singleline ? "s" : "") + (flags.IgnorePatternWhitespace ? "x" : "") + (flags.ExplicitCapture ? "n" : "");
        if (flags.ExplicitCapture) notes.Add("The n flag needs Perl 5.22 or later.");
        if (flags.CultureInvariant) notes.Add("CultureInvariant has no equivalent.");
        if (flags.NonBacktracking) notes.Add("NonBacktracking is an engine choice and is dropped.");
        notes.Add("The pattern is kept in a single-quoted string and interpolated by qr, so $ and @ inside it are never read as variables.");
        var define = $"my $pattern = {SingleQuoted(pattern)};\nmy $re = qr/$pattern/{letters};";
        var text = define + "\n" + kind switch
        {
            RegexCodeKind.IsMatch => "my $found = $input =~ $re;",
            RegexCodeKind.Matches => "while ($input =~ /$re/g) {\n    print \"$-[0]: $&\\n\";\n}",
            RegexCodeKind.Replace => $"(my $output = $input) =~ s/$re/{PerlReplacement(replacement)}/g;",
            _ => "my @pieces = split $re, $input;",
        };
        if (kind == RegexCodeKind.Replace) notes.Add("Perl replacements use $1 and $&; a named group is $+{name}, so ${name} is not understood, and $$ is not a dollar sign. Rewrite those before use.");
        if (kind == RegexCodeKind.Split) notes.Add("Perl's split drops trailing empty pieces, which .NET keeps.");
        return Result(target, text, differences, notes);
    }

    private static string PerlReplacement(string text) => text.Replace("\\", "\\\\").Replace("/", "\\/").Replace("@", "\\@");

    private static RegexConversion Ruby(string pattern, RegexBuilderFlags flags, RegexCodeKind kind, string replacement)
    {
        const string target = "Ruby";
        if (flags.RightToLeft) return Refuse(target, "Ruby has no right-to-left matching.");
        var (refusals, differences) = Compatibility(pattern, flags, "pcre2");
        if (refusals.Count > 0) return new(target, "", false, false, ["Not convertible: " + string.Join(" ", refusals) + " (checked against PCRE2, the closest column; Ruby's Onigmo engine has no column of its own)."]);
        var notes = new List<string> { "The comparison table has no Ruby column. PCRE2 was used as the closest engine, so differences that are not in the table are not known. Test the pattern in Ruby." };
        var options = new List<string>();
        if (flags.IgnoreCase) options.Add("Regexp::IGNORECASE");
        if (flags.Singleline) options.Add("Regexp::MULTILINE");
        if (flags.IgnorePatternWhitespace) options.Add("Regexp::EXTENDED");
        notes.Add("In Ruby ^ and $ always match at the start and end of every line (use \\A and \\z for the whole text), and the Regexp::MULTILINE option means what Singleline means in .NET.");
        if (flags.CultureInvariant) notes.Add("CultureInvariant has no equivalent.");
        if (flags.NonBacktracking) notes.Add("NonBacktracking is an engine choice and is dropped.");
        if (flags.ExplicitCapture) notes.Add("ExplicitCapture is dropped: Ruby groups capture unless they are named, in which case the unnamed ones stop capturing.");
        var define = $"re = Regexp.new({SingleQuoted(pattern)}" + (options.Count > 0 ? ", " + string.Join(" | ", options) : "") + ")";
        var text = define + "\n" + kind switch
        {
            RegexCodeKind.IsMatch => "found = re.match?(input)",
            RegexCodeKind.Matches => "input.scan(re) { puts \"#{$~.begin(0)}: #{$~[0]}\" }",
            RegexCodeKind.Replace => $"output = input.gsub(re, {SingleQuoted(replacement)})",
            _ => "pieces = input.split(re)",
        };
        if (kind == RegexCodeKind.Replace) notes.Add("Ruby replacements use \\1 and \\k<name>; $1 and ${name} are not understood. Rewrite the replacement before use.");
        return Result(target, text, differences, notes);
    }

    private static RegexConversion PowerShellScript(string pattern, RegexBuilderFlags flags, RegexCodeKind kind, string replacement)
    {
        var notes = new List<string> { "A complete script: save it as a .ps1 file and pass the text file to read. The match timeout of 250 ms is reported as an error instead of hanging." };
        var options = RegexBuilderFlags.All.Where(f => flags.Get(f.Name)).Select(f => f.Name).ToList();
        var optionText = options.Count == 0 ? "None" : string.Join(", ", options);
        string Quote(string s) => "'" + s.Replace("'", "''") + "'";
        var sb = new StringBuilder();
        sb.AppendLine("[CmdletBinding()]")
          .AppendLine("param(")
          .AppendLine("    [Parameter(Mandatory)][string]$Path")
          .AppendLine(")")
          .AppendLine()
          .AppendLine("Set-StrictMode -Version Latest")
          .AppendLine("$ErrorActionPreference = 'Stop'")
          .AppendLine()
          .AppendLine($"$regex = [regex]::new({Quote(pattern)}, {Quote(optionText)}, [TimeSpan]::FromMilliseconds(250))")
          .AppendLine("$text = Get-Content -LiteralPath $Path -Raw")
          .AppendLine()
          .AppendLine("try {");
        switch (kind)
        {
            case RegexCodeKind.IsMatch: sb.AppendLine("    $regex.IsMatch($text)"); break;
            case RegexCodeKind.Replace: sb.AppendLine($"    $regex.Replace($text, {Quote(replacement)})"); notes.Add("In the replacement, $1 and ${name} are expanded by .NET because the replacement is in single quotes."); break;
            case RegexCodeKind.Split: sb.AppendLine("    $regex.Split($text)"); break;
            default:
                sb.AppendLine("    foreach ($match in $regex.Matches($text)) {")
                  .AppendLine("        [pscustomobject]@{ Index = $match.Index; Length = $match.Length; Value = $match.Value }")
                  .AppendLine("    }");
                break;
        }
        sb.AppendLine("}")
          .AppendLine("catch [System.Text.RegularExpressions.RegexMatchTimeoutException] {")
          .AppendLine("    Write-Error 'The pattern took longer than 250 ms on this text and was stopped.'")
          .Append('}');
        if (pattern.Contains('\n') || pattern.Contains('\r')) notes.Add("The pattern contains a line break. A single-quoted PowerShell string may span lines, but check it survives your editor.");
        return new("PowerShell script", sb.ToString(), true, false, notes);
    }
}
