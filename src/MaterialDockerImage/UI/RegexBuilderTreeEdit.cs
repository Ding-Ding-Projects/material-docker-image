using System.Globalization;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>The outcome of one edit made through the explanation tree. The pattern is regenerated text; nothing is applied when <see cref="Success"/> is false.</summary>
public sealed record RegexTreeEditResult(bool Success, string Pattern, string Message, int SelectStart, int SelectLength);

/// <summary>Kinds of group the tree can wrap a node in.</summary>
public enum RegexTreeWrap { Capturing, NonCapturing, Atomic, PositiveLookahead, NegativeLookahead, PositiveLookbehind, NegativeLookbehind }

/// <summary>
/// Editing a pattern by editing its explanation tree: change a quantifier, wrap a node in a group, unwrap a group or delete a node. Every edit is a text
/// operation on the span of one node of <see cref="RegexExplainer"/>, the new pattern text is regenerated, and an edit whose result the engine would
/// reject (or whose meaning would silently change, for example a quantifier moving onto a single character of a group) is refused with the reason.
/// The tree is read again after every edit, so the next edit works on the regenerated pattern.
/// </summary>
public static class RegexTreeEditor
{
    private static readonly Regex quantifierSyntax = new(@"^(?:[*+?]|\{\d+(?:,\d*)?\})\??$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex groupPrefix = new(@"^\((?:\?(?:[:>=!]|<[=!]|<[^>]*>|'[^']*'|[imnsx-]+:))?", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>The deepest node whose span is exactly the given one, or null.</summary>
    public static RegexExplainNode? NodeAt(string pattern, RegexBuilderFlags flags, int start, int length)
    {
        RegexExplainNode? found = null;
        foreach (var (node, _) in RegexExplainer.Flatten(RegexExplainer.Explain(pattern, flags)))
            if (node.Start == start && node.Length == length) found = node;
        return found;
    }

    private static RegexTreeEditResult Refuse(string pattern, string message) => new(false, pattern, message, 0, 0);

    private static RegexTreeEditResult Finish(string original, string edited, RegexBuilderFlags flags, int start, int length, string message, string? note = null)
    {
        if (edited.Length > RegexBuilderEngine.MaxPatternLength) return Refuse(original, $"The edited pattern would be longer than {RegexBuilderEngine.MaxPatternLength} characters.");
        var check = RegexBuilderEngine.Validate(edited, flags);
        if (!check.Valid) return Refuse(original, "The edit would make the pattern invalid, so it was not applied: " + check.Error);
        return new(true, edited, note == null ? message : message + " " + note, start, length);
    }

    private static bool IsSingleCharacterLiteral(RegexExplainNode node) =>
        node.Family == RegexTokenFamily.Literal && new StringInfo(node.Text).LengthInTextElements == 1;

    /// <summary>Whether a quantifier applied to this node needs the node wrapped in a non-capturing group to keep its meaning.</summary>
    private static bool NeedsWrapForQuantifier(RegexExplainNode node) => node.Family switch
    {
        RegexTokenFamily.Literal => !IsSingleCharacterLiteral(node),
        RegexTokenFamily.Alternation or RegexTokenFamily.Branch => true,
        _ => false,
    };

    /// <summary>
    /// Sets, changes or removes the quantifier of a node. <paramref name="quantifier"/> is one of <c>?</c>, <c>*</c>, <c>+</c>, <c>{n}</c>, <c>{n,}</c>, <c>{n,m}</c>
    /// with an optional trailing <c>?</c> for lazy, or empty to remove it. A multi-character literal or an alternation is wrapped in a non-capturing group first.
    /// </summary>
    public static RegexTreeEditResult SetQuantifier(string pattern, RegexBuilderFlags flags, int nodeStart, int nodeLength, string quantifier)
    {
        quantifier = (quantifier ?? "").Trim();
        if (quantifier.Length > 0 && !quantifierSyntax.IsMatch(quantifier)) return Refuse(pattern, "A quantifier is ?, *, +, {n}, {n,} or {n,m}, optionally followed by ? for lazy.");
        if (quantifier.StartsWith('{'))
        {
            var body = quantifier.TrimEnd('?')[1..^1].Split(',');
            if (!int.TryParse(body[0], NumberStyles.None, CultureInfo.InvariantCulture, out var min)) return Refuse(pattern, "The repeat count is too large.");
            if (body.Length == 2 && body[1].Length > 0 && (!int.TryParse(body[1], NumberStyles.None, CultureInfo.InvariantCulture, out var max) || max < min)) return Refuse(pattern, "The maximum repeat count is smaller than the minimum.");
        }
        var node = NodeAt(pattern, flags, nodeStart, nodeLength);
        if (node == null) return Refuse(pattern, "That part of the pattern is no longer a node of the tree. Select a row again.");
        if (node.Family is RegexTokenFamily.Error or RegexTokenFamily.Comment or RegexTokenFamily.InlineOptions or RegexTokenFamily.Anchor)
            return Refuse(pattern, "This kind of token cannot be repeated.");
        if (node.Family == RegexTokenFamily.Quantifier)
        {
            var atom = node.Children[0];
            var edited = pattern[..node.Start] + atom.Text + quantifier + pattern[(node.Start + node.Length)..];
            return Finish(pattern, edited, flags, node.Start, atom.Length + quantifier.Length,
                quantifier.Length == 0 ? "The quantifier was removed." : $"The quantifier is now {quantifier}.");
        }
        if (quantifier.Length == 0) return Refuse(pattern, "This token has no quantifier to remove.");
        if (node.Family == RegexTokenFamily.Branch)
            return Refuse(pattern, "A branch cannot carry a quantifier. Select the whole alternation, or wrap the branch in a group first.");
        var wrap = NeedsWrapForQuantifier(node);
        var inner = wrap ? "(?:" + node.Text + ")" : node.Text;
        var result = pattern[..node.Start] + inner + quantifier + pattern[(node.Start + node.Length)..];
        return Finish(pattern, result, flags, node.Start, inner.Length + quantifier.Length, $"Added the quantifier {quantifier}.",
            wrap ? "The part was wrapped in a non-capturing group so the quantifier repeats all of it." : null);
    }

    private static (string Open, string Description) Prefix(RegexTreeWrap kind) => kind switch
    {
        RegexTreeWrap.Capturing => ("(", "a capturing group"),
        RegexTreeWrap.NonCapturing => ("(?:", "a non-capturing group"),
        RegexTreeWrap.Atomic => ("(?>", "an atomic group"),
        RegexTreeWrap.PositiveLookahead => ("(?=", "a positive lookahead"),
        RegexTreeWrap.NegativeLookahead => ("(?!", "a negative lookahead"),
        RegexTreeWrap.PositiveLookbehind => ("(?<=", "a positive lookbehind"),
        _ => ("(?<!", "a negative lookbehind"),
    };

    /// <summary>Wraps a node (and its quantifier, when it has one) in a group. A capturing group renumbers the groups after it, which the message says.</summary>
    public static RegexTreeEditResult Wrap(string pattern, RegexBuilderFlags flags, int nodeStart, int nodeLength, RegexTreeWrap kind)
    {
        var node = NodeAt(pattern, flags, nodeStart, nodeLength);
        if (node == null) return Refuse(pattern, "That part of the pattern is no longer a node of the tree. Select a row again.");
        if (node.Family == RegexTokenFamily.Error) return Refuse(pattern, "An invalid token cannot be wrapped. Delete or correct it first.");
        var (open, description) = Prefix(kind);
        var edited = pattern[..node.Start] + open + node.Text + ")" + pattern[(node.Start + node.Length)..];
        return Finish(pattern, edited, flags, node.Start, node.Length + open.Length + 1, $"Wrapped the selection in {description}.",
            kind == RegexTreeWrap.Capturing ? "Capturing groups after this point are numbered one higher; check backreferences and replacements." : null);
    }

    /// <summary>
    /// Removes the group around its contents. A quantified group is only unwrapped when its contents are one single item, because the quantifier would
    /// otherwise start repeating only the last item; a group with alternatives is only unwrapped when it is the whole pattern.
    /// </summary>
    public static RegexTreeEditResult Unwrap(string pattern, RegexBuilderFlags flags, int nodeStart, int nodeLength)
    {
        var tree = RegexExplainer.Explain(pattern, flags);
        var node = RegexExplainer.Flatten(tree).Select(x => x.Node).LastOrDefault(n => n.Start == nodeStart && n.Length == nodeLength);
        if (node == null) return Refuse(pattern, "That part of the pattern is no longer a node of the tree. Select a row again.");
        if (node.Family is not (RegexTokenFamily.Group or RegexTokenFamily.Lookaround)) return Refuse(pattern, "Only a group can be unwrapped.");
        var m = groupPrefix.Match(node.Text);
        if (!m.Success || !node.Text.EndsWith(')')) return Refuse(pattern, "This group is not closed, so it cannot be unwrapped. Correct it first.");
        var contents = node.Text[m.Length..^1];
        var quantified = RegexExplainer.Flatten(tree).Any(n => n.Node.Family == RegexTokenFamily.Quantifier && n.Node.Children.Count > 0 && n.Node.Children[0] == node);
        var hasAlternation = node.Children.Any(c => c.Family == RegexTokenFamily.Alternation);
        var topLevelAlone = tree.Count == 1 && tree[0] == node;
        if (quantified && (node.Children.Count != 1 || hasAlternation || NeedsWrapForQuantifier(node.Children[0])))
            return Refuse(pattern, "The group has a quantifier and more than one item inside. Removing the group would make the quantifier repeat only the last item. Remove the quantifier first.");
        if (hasAlternation && !topLevelAlone)
            return Refuse(pattern, "The group holds alternatives, and removing it would let the alternation reach the text around it. Only a group that is the whole pattern can be unwrapped.");
        if (node.Family == RegexTokenFamily.Lookaround) return Finish(pattern, pattern[..node.Start] + contents + pattern[(node.Start + node.Length)..], flags, node.Start, contents.Length, "The lookaround was removed and its contents now consume text.");
        var edited = pattern[..node.Start] + contents + pattern[(node.Start + node.Length)..];
        return Finish(pattern, edited, flags, node.Start, contents.Length, "The group was removed.",
            m.Value == "(" || m.Value.StartsWith("(?<", StringComparison.Ordinal) || m.Value.StartsWith("(?'", StringComparison.Ordinal) ? "Capturing groups after this point are numbered one lower; check backreferences and replacements." : null);
    }

    /// <summary>Deletes a node (and its quantifier). Deleting one branch of an alternation also removes one separating bar.</summary>
    public static RegexTreeEditResult Delete(string pattern, RegexBuilderFlags flags, int nodeStart, int nodeLength)
    {
        // A branch and its only child share a span; deleting by that span means deleting the branch, with its separating bar.
        var sameSpan = RegexExplainer.Flatten(RegexExplainer.Explain(pattern, flags)).Select(x => x.Node).Where(n => n.Start == nodeStart && n.Length == nodeLength).ToList();
        var node = sameSpan.FirstOrDefault(n => n.Family == RegexTokenFamily.Branch) ?? sameSpan.LastOrDefault();
        if (node == null) return Refuse(pattern, "That part of the pattern is no longer a node of the tree. Select a row again.");
        int start = node.Start, end = node.Start + node.Length;
        if (node.Family == RegexTokenFamily.Branch)
        {
            // A branch span excludes its bars: remove it together with the bar that follows, or the one before when it is the last branch.
            if (end < pattern.Length && pattern[end] == '|') end++;
            else if (start > 0 && pattern[start - 1] == '|') start--;
        }
        if (start == 0 && end == pattern.Length) return Refuse(pattern, "Deleting this would empty the pattern. Clear the pattern box instead.");
        var edited = pattern[..start] + pattern[end..];
        return Finish(pattern, edited, flags, Math.Min(start, edited.Length), 0, "The part was deleted.");
    }
}
