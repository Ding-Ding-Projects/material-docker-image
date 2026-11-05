using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>One run of a signature hint; required parameters are shown in bold.</summary>
public sealed record SignaturePart(string Text, bool Required);

/// <summary>
/// Framework-free decisions for how a suggestion row and its documentation pane look: the row icon, the short tag at
/// the right of the row, the chip next to the documentation title and the bold parts of a signature hint. Strings are
/// returned in English; the window layer translates them.
/// </summary>
public static class SuggestionPresentation
{
    private static readonly Regex Placeholder = new(@"[^\s,\[\]]*<[^>\s]+>[^\s,\[\]]*", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    /// <summary>The icon for a row. Pickers use the icon of the value they edit; paths tell files from folders.</summary>
    public static string Icon(SuggestionItem item) => item.Kind switch
    {
        SuggestionKind.Picker => PickerIcon(item.PickerKind),
        SuggestionKind.Path => item.Label.EndsWith('/') || item.Label.EndsWith('\\') ? Icons.FolderOpen : Icons.Description,
        SuggestionKind.Instruction => Icons.Code,
        SuggestionKind.Directive => Icons.Settings,
        SuggestionKind.Flag => Icons.Tune,
        SuggestionKind.Value => Icons.Label,
        SuggestionKind.Stage => Icons.Layers,
        SuggestionKind.Variable => Icons.DataObject,
        SuggestionKind.Image => Icons.Layers,
        SuggestionKind.Snippet => Icons.TextFields,
        SuggestionKind.Key => Icons.Key,
        _ => Icons.Edit,
    };

    /// <summary>The icon of an interactive value editor.</summary>
    public static string PickerIcon(EditorValueKind kind) => kind switch
    {
        EditorValueKind.Path => Icons.FolderOpen,
        EditorValueKind.Colour => Icons.Palette,
        EditorValueKind.Port => Icons.Public,
        EditorValueKind.Duration => Icons.Timer,
        EditorValueKind.Chmod => Icons.Lock,
        EditorValueKind.User => Icons.Person,
        EditorValueKind.Platform => Icons.DeveloperBoard,
        EditorValueKind.Image => Icons.Layers,
        EditorValueKind.Secret => Icons.Key,
        EditorValueKind.Choice => Icons.Tune,
        _ => Icons.Edit,
    };

    /// <summary>
    /// The short tag at the right of a row: "Enter" for pickers (they open an editor), otherwise the version badge,
    /// otherwise the detail. Empty when there is nothing to say.
    /// </summary>
    public static string RowTag(SuggestionItem item) =>
        item.Kind == SuggestionKind.Picker ? "Enter" : item.Badge.Length > 0 ? item.Badge : item.Detail;

    /// <summary>True when the row tag warns (a version badge the file's syntax does not reach yet, or a required value).</summary>
    public static bool RowTagWarns(SuggestionItem item) =>
        item.Kind != SuggestionKind.Picker && (item.Badge.Length > 0 && item.NeedsSyntaxUpgrade || string.Equals(RowTag(item), "required", StringComparison.OrdinalIgnoreCase));

    /// <summary>The chip next to the documentation title: the item's detail, or the kind name when it has none.</summary>
    public static string DocChip(SuggestionItem item, Func<SuggestionKind, string> kindName) =>
        item.Detail.Length > 0 ? item.Detail : kindName(item.Kind);

    /// <summary>
    /// The "Values: shared (default) · private · locked" line of the documentation pane, for options whose values are a
    /// fixed set in the Dockerfile reference. Empty when the item has no set. <paramref name="translate"/> localizes the
    /// two words; the values themselves stay literal because they are typed into the file.
    /// </summary>
    public static string ValuesLine(SuggestionItem item, Func<string, string>? translate = null)
    {
        if (item.Allowed is not { Values.Length: > 0 } allowed) return "";
        translate ??= text => text;
        return translate("Values:") + " " + allowed.Line(translate("(default)"));
    }

    /// <summary>Splits a signature hint into runs; placeholders such as target=&lt;path&gt; outside square brackets are required.</summary>
    public static IReadOnlyList<SignaturePart> Signature(string signature)
    {
        var parts = new List<SignaturePart>();
        if (string.IsNullOrEmpty(signature)) return parts;
        var depth = 0;
        var start = 0;
        var plain = new System.Text.StringBuilder();
        void FlushPlain() { if (plain.Length > 0) { parts.Add(new SignaturePart(plain.ToString(), false)); plain.Clear(); } }
        // Walk the text: bracketed spans are optional and stay plain; outside them, placeholders are marked required.
        var i = 0;
        while (i < signature.Length)
        {
            var c = signature[i];
            if (c == '[') { depth++; plain.Append(c); i++; continue; }
            if (c == ']') { depth = Math.Max(0, depth - 1); plain.Append(c); i++; continue; }
            if (depth == 0)
            {
                Match match;
                try { match = Placeholder.Match(signature, i); }
                catch (RegexMatchTimeoutException) { plain.Append(signature, i, signature.Length - i); break; }
                if (match.Success && match.Index == i && match.Length > 0 && signature.IndexOf('[', i, match.Length) < 0)
                {
                    // A placeholder token starts here only if it begins a token (after start, a space or a comma).
                    if (i == start || signature[i - 1] is ' ' or ',' or '=' or ']')
                    {
                        FlushPlain();
                        parts.Add(new SignaturePart(match.Value, true));
                        i += match.Length;
                        continue;
                    }
                }
            }
            plain.Append(c);
            i++;
        }
        FlushPlain();
        return parts;
    }
}
