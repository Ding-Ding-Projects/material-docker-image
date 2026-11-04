using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>
/// Style tips: patterns that are valid and may even be fast, but are longer, harder to read or probably not what was meant. They are separate from the
/// backtracking findings of <see cref="RegexDiagnostics"/> and never change the pattern; each one names the span it is about.
/// </summary>
public static class RegexTips
{
    private static readonly Regex plainLetter = new(@"^\p{L}", RegexOptions.NonBacktracking);
    private static readonly Regex letterEnd = new(@"\p{L}$", RegexOptions.NonBacktracking);

    public static IReadOnlyList<RegexDiagnostic> Analyze(string pattern, RegexBuilderFlags flags)
    {
        var found = new List<RegexDiagnostic>();
        if (string.IsNullOrEmpty(pattern)) return found;
        var tree = RegexExplainer.Explain(pattern, flags);
        if (RegexExplainer.Flatten(tree).Any(n => n.Node.Family == RegexTokenFamily.Error)) return found;
        LeadingTrailingWildcard(tree, flags, found);
        Sequences(tree, flags, found);
        var quantified = RegexExplainer.Flatten(tree).Where(n => n.Node.Family == RegexTokenFamily.Quantifier && n.Node.Children.Count > 0).Select(n => n.Node.Children[0]).ToHashSet();
        foreach (var (node, _) in RegexExplainer.Flatten(tree))
        {
            switch (node.Family)
            {
                case RegexTokenFamily.Alternation: Alternation(node, found); break;
                case RegexTokenFamily.Group when !quantified.Contains(node): RedundantGroup(node, found); break;
                case RegexTokenFamily.CharacterClass when flags.IgnoreCase && node.Text.StartsWith('['): CaseSets(node, found); break;
            }
        }
        var capturing = RegexExplainer.Flatten(tree).Count(n => n.Node.Family == RegexTokenFamily.Group && n.Node.Description.StartsWith("Capturing group", StringComparison.Ordinal));
        var references = RegexExplainer.Flatten(tree).Any(n => n.Node.Family is RegexTokenFamily.Backreference or RegexTokenFamily.Conditional);
        if (capturing >= 3 && !references && !flags.ExplicitCapture)
            found.Add(new("tip-many-captures", $"The pattern has {capturing} capturing groups and no backreference. If you do not need their text, (?:...) or the ExplicitCapture option keeps the match results smaller.", 0, pattern.Length));
        return found;
    }

    private static void LeadingTrailingWildcard(IReadOnlyList<RegexExplainNode> tree, RegexBuilderFlags flags, List<RegexDiagnostic> found)
    {
        if (tree.Count < 2 || tree.Any(n => n.Family == RegexTokenFamily.Alternation)) return;
        var first = tree[0];
        if (first.Family == RegexTokenFamily.Quantifier && first.Children.Count > 0 && first.Children[0].Family == RegexTokenFamily.Any && first.Text.EndsWith(".*", StringComparison.Ordinal))
            found.Add(new("tip-leading-wildcard", "A pattern that starts with .* is unnecessary for a search, because a search already finds the match anywhere. It only makes a failing search slower. Remove it, or anchor the pattern with ^.", first.Start, first.Length));
        var last = tree[^1];
        if (last.Family == RegexTokenFamily.Quantifier && last.Children.Count > 0 && last.Children[0].Family == RegexTokenFamily.Any && last.Text == ".*")
            found.Add(new("tip-trailing-wildcard", "A trailing .* matches the rest of the line but adds nothing to whether the pattern matches. Remove it unless you need the rest of the line in the match.", last.Start, last.Length));
    }

    private static void Sequences(IReadOnlyList<RegexExplainNode> nodes, RegexBuilderFlags flags, List<RegexDiagnostic> found)
    {
        for (var i = 1; i + 1 < nodes.Count; i++)
        {
            var dot = nodes[i];
            if (dot.Family != RegexTokenFamily.Any) continue;
            if (nodes[i - 1] is { Family: RegexTokenFamily.Literal } before && nodes[i + 1] is { Family: RegexTokenFamily.Literal } after && letterEnd.IsMatch(before.Text) && plainLetter.IsMatch(after.Text))
                found.Add(new("tip-unescaped-dot", "An unescaped . between letters matches any character, so " + before.Text[^1] + "." + after.Text[0] + " also matches " + before.Text[^1] + "-" + after.Text[0] + ". Write \\. if you meant a literal full stop.", dot.Start, 1));
        }
        foreach (var node in nodes) if (node.Children.Count > 0) Sequences(node.Children, flags, found);
    }

    private static void Alternation(RegexExplainNode alt, List<RegexDiagnostic> found)
    {
        var branches = alt.Children.Where(c => c.Family == RegexTokenFamily.Branch).ToList();
        if (branches.Count == 0) return;
        if (branches.Count == 2 && branches.Any(b => b.Children.Count == 0))
            found.Add(new("tip-empty-alternative", "An alternation with an empty branch, such as (a|), is an optional item written the long way. Use a? instead.", alt.Start, alt.Length));
        else if (branches.Any(b => b.Children.Count == 0))
            found.Add(new("tip-empty-alternative", "One branch of this alternation is empty, so it can match nothing at all. If that is not intended, remove the stray |.", alt.Start, alt.Length));
        if (branches.Count >= 2 && branches.All(b => b.Children is [{ Family: RegexTokenFamily.Literal } only] && only.Text.Length == 1 && @"\]^-".IndexOf(only.Text[0]) < 0))
            found.Add(new("tip-single-char-alternation", "An alternation of single characters is shorter and faster as a set: [" + string.Concat(branches.Select(b => b.Text)) + "].", alt.Start, alt.Length));
        var texts = branches.Select(b => b.Text).ToList();
        if (texts.Count != texts.Distinct().Count() && branches.Count > 1)
            found.Add(new("tip-duplicate-alternative", "Two branches of this alternation are identical. The second can never add a match.", alt.Start, alt.Length));
    }

    private static void RedundantGroup(RegexExplainNode group, List<RegexDiagnostic> found)
    {
        if (!group.Text.StartsWith("(?:", StringComparison.Ordinal) || group.Children.Count != 1) return;
        var only = group.Children[0];
        if (only.Family is RegexTokenFamily.Literal && only.Text.Length == 1 || only.Family is RegexTokenFamily.CharacterClass or RegexTokenFamily.Any or RegexTokenFamily.UnicodeCategory)
            found.Add(new("tip-redundant-group", "A non-capturing group around a single item adds nothing unless a quantifier follows it. Remove the (?: ) around " + only.Text + ".", group.Start, group.Length));
    }

    private static void CaseSets(RegexExplainNode set, List<RegexDiagnostic> found)
    {
        var t = set.Text;
        if (t.Contains("a-z", StringComparison.Ordinal) && t.Contains("A-Z", StringComparison.Ordinal))
            found.Add(new("tip-case-set", "IgnoreCase is on, so [A-Za-z] is the same as [a-z]. Keep one range.", set.Start, set.Length));
    }
}
