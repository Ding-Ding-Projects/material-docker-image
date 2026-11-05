using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>
/// Named Creator steps. A step's name is kept in the Dockerfile itself as an ordinary comment line directly above the
/// instruction, in the form <c># step: Install dependencies</c>. The file stays a valid Dockerfile, other comments are
/// untouched, and the name survives a parse and render round trip.
/// </summary>
public static class DockerfileStepNames
{
    /// <summary>The text written before the name.</summary>
    public const string Prefix = "# step: ";

    /// <summary>Longest name kept; longer names are cut so a step title stays one readable line.</summary>
    public const int MaxLength = 120;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly Regex StepLine = new(@"^\s*#\s*step:[ \t]*(\S.*?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex FromLine = new(@"^\s*FROM\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.CultureInvariant, RegexTimeout);

    /// <summary>The name in a "# step: name" line, or null when the line is not one.</summary>
    public static string? Parse(string line)
    {
        var match = StepLine.Match(line);
        if (!match.Success) return null;
        var name = Clean(match.Groups[1].Value);
        return name.Length > 0 ? name : null;
    }

    /// <summary>
    /// True when the line at <paramref name="next"/> is an instruction a step name can belong to: it directly follows
    /// (no blank line), is not another comment and is not a FROM line (a stage is named with AS instead).
    /// </summary>
    public static bool CanName(IReadOnlyList<string> lines, int next)
    {
        if (next < 0 || next >= lines.Count) return false;
        var line = lines[next].Trim();
        return line.Length > 0 && !line.StartsWith('#') && !FromLine.IsMatch(line);
    }

    /// <summary>One line, single spaces, trimmed and at most <see cref="MaxLength"/> characters.</summary>
    public static string Clean(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var text = Spaces.Replace(name, " ").Trim();
        return text.Length > MaxLength ? text[..MaxLength].TrimEnd() : text;
    }

    /// <summary>The comment line that stores <paramref name="name"/>.</summary>
    public static string Line(string name) => Prefix + Clean(name);

    /// <summary>True when the instruction carries a name that is written to the file. Comments cannot be named.</summary>
    public static bool HasName(DockerInstruction instruction) =>
        instruction.Kind != InstructionKind.Comment && Clean(instruction.StepName).Length > 0;

    /// <summary>True when the instruction kind can carry a name (every kind except a comment).</summary>
    public static bool CanBeNamed(DockerInstruction instruction) => instruction.Kind != InstructionKind.Comment;

    /// <summary>
    /// Sets or removes the name. Returns false when nothing changed (same name, or a comment). An empty name removes
    /// the "# step:" line on the next render.
    /// </summary>
    public static bool Rename(DockerInstruction instruction, string? name)
    {
        if (!CanBeNamed(instruction)) return false;
        var clean = Clean(name);
        if (clean == Clean(instruction.StepName)) { instruction.StepName = clean; return false; }
        instruction.StepName = clean;
        return true;
    }

    /// <summary>The open-step title: the step's name when it has one, otherwise the plain title of its kind.</summary>
    public static string Title(DockerInstruction instruction) =>
        HasName(instruction) ? Clean(instruction.StepName) : DockerfileOutline.Title(instruction.Kind);
}
