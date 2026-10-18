using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

public sealed record DockerStage(int Index, int Line, string Name, string Base, string Platform, IReadOnlyList<string> Instructions);
public sealed record GraphEdge(int From, int To, string Label);
public sealed record DockerGraph(IReadOnlyList<DockerStage> Stages, IReadOnlyList<GraphEdge> Edges, IReadOnlyList<string> Warnings);
public static class DockerfileGraph
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);
    public static DockerGraph Parse(string source)
    {
        if (HasUnsupportedSyntax(source)) return new(Array.Empty<DockerStage>(), Array.Empty<GraphEdge>(), new[] { "Source-only mode: continuations, heredocs or escape directives require the text editor. Original source is retained unchanged." });
        var lines = source.Replace("\r\n", "\n").Split('\n');
        var stages = new List<DockerStage>(); var edges = new List<GraphEdge>(); var warnings = new List<string>();
        var bodies = new List<List<string>>();
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var from = Regex.Match(line, @"^FROM\s+(?:(--platform=\S+)\s+)?(\S+)(?:\s+AS\s+([\w.-]+))?\s*$", RegexOptions.IgnoreCase, Timeout);
            if (from.Success)
            {
                var index = stages.Count;
                stages.Add(new(index, i, from.Groups[3].Success ? from.Groups[3].Value : index.ToString(), from.Groups[2].Value, from.Groups[1].Value, Array.Empty<string>()));
                bodies.Add(new());
                var parent = stages.Take(index).FirstOrDefault(x => string.Equals(x.Name, from.Groups[2].Value, StringComparison.OrdinalIgnoreCase));
                if (parent != null) edges.Add(new(parent.Index, index, "FROM"));
            }
            else if (line.StartsWith("FROM ", StringComparison.OrdinalIgnoreCase)) warnings.Add($"Line {i + 1}: complex FROM syntax is preserved but cannot be edited visually.");
            else if (stages.Count > 0)
            {
                bodies[^1].Add(line);
                var copy = Regex.Match(line, @"^(?:COPY|ADD)\s+.*?--from=(\S+)", RegexOptions.IgnoreCase, Timeout);
                if (copy.Success)
                {
                    var name = copy.Groups[1].Value;
                    var parent = stages.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) || x.Index.ToString() == name);
                    if (parent != null && parent.Index != stages.Count - 1) edges.Add(new(parent.Index, stages.Count - 1, "COPY"));
                }
            }
        }
        for (int i = 0; i < stages.Count; i++) stages[i] = stages[i] with { Instructions = bodies[i] };
        if (stages.Count == 0) warnings.Add("No supported FROM stage found. Load or write a Dockerfile in Build, then parse it here.");
        if (lines.Any(x => x.TrimEnd().EndsWith('\\'))) warnings.Add("Continued instructions are shown as source lines. Visual edits are limited to single-line FROM declarations.");
        return new(stages, edges, warnings);
    }
    public static string EditStage(string source, DockerStage stage, string newBase, string newName)
    {
        if (HasUnsupportedSyntax(source)) throw new InvalidOperationException("Complex source syntax requires the text editor; no visual change was applied.");
        if (!Regex.IsMatch(newBase, @"^[A-Za-z0-9_./:@${}-]+$", RegexOptions.None, Timeout) ||
            !Regex.IsMatch(newName, @"^[A-Za-z][A-Za-z0-9_.-]*$", RegexOptions.None, Timeout))
            throw new ArgumentException("Use a valid base image reference and an alias beginning with a letter.");
        var lines = source.Replace("\r\n", "\n").Split('\n');
        var current = Parse(source).Stages.FirstOrDefault(x => x.Index == stage.Index);
        if (current == null || current.Line != stage.Line || current.Base != stage.Base || current.Name != stage.Name)
            throw new InvalidOperationException("The Dockerfile changed. Parse it again before applying stage edits.");
        if (Parse(source).Stages.Any(x => x.Index != stage.Index && string.Equals(x.Name, newName, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Each stage alias must be unique.");
        if (newName != stage.Name && Regex.IsMatch(source, @"(?:--from=|FROM\s+)" + Regex.Escape(stage.Name) + @"(?:\s|$)", RegexOptions.IgnoreCase, Timeout))
            throw new InvalidOperationException("This alias is referenced by another stage. Rename it in the text editor so every reference can be reviewed together.");
        var lineStart = 0;
        for (int i = 0; i < stage.Line; i++) lineStart = source.IndexOf('\n', lineStart) + 1;
        var lineEnd = source.IndexOf('\n', lineStart);
        if (lineEnd < 0) lineEnd = source.Length;
        if (lineEnd > lineStart && source[lineEnd - 1] == '\r') lineEnd--;
        var original = source.Substring(lineStart, lineEnd - lineStart);
        var match = Regex.Match(original, @"^(\s*FROM\s+(?:--platform=\S+\s+)?)(\S+)(\s+AS\s+([\w.-]+))?(\s*)$", RegexOptions.IgnoreCase, Timeout);
        if (!match.Success) throw new InvalidOperationException("The source span is ambiguous; use the text editor.");
        var replacement = match.Groups[1].Value + newBase + (match.Groups[3].Success ? match.Groups[3].Value[..(match.Groups[3].Value.Length - match.Groups[4].Value.Length)] + newName : " AS " + newName) + match.Groups[5].Value;
        return source[..lineStart] + replacement + source[lineEnd..];
    }
    private static bool HasUnsupportedSyntax(string source) => source.Contains("<<", StringComparison.Ordinal) || source.Split('\n').Any(x => x.TrimEnd('\r',' ','\t').EndsWith('\\') || x.TrimStart().StartsWith("# escape", StringComparison.OrdinalIgnoreCase));
}
