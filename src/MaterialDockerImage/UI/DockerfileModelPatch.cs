using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MaterialDockerImage.UI;

/// <summary>The source text, its line map and the rendered text of every mapped element, kept in step.</summary>
public sealed record DockerfileSourceState(string Text, IReadOnlyList<DockerfileLineSpan> Lines, IReadOnlyList<string> Elements, string Header);

/// <summary>
/// Minimal text patches for GUI edits. Instead of re-rendering the whole file (which would drop the user's
/// comments, blank lines and formatting), only the lines of elements whose rendered text changed are replaced.
/// When the structure changed (elements added, removed or moved, or parser directives edited) no patch is possible
/// and the caller re-renders.
/// </summary>
public static class DockerfilePatch
{
    /// <summary>Rendered text of every element in map order: preamble instructions, then each stage's FROM and instructions.</summary>
    public static List<(int Stage, int Instruction, string Text)> Elements(DockerfileDocument document)
    {
        var list = new List<(int, int, string)>();
        for (var p = 0; p < document.Preamble.Count; p++) list.Add((-1, p, DockerfileModel.RenderInstruction(document.Preamble[p])));
        for (var s = 0; s < document.Stages.Count; s++)
        {
            list.Add((s, -1, DockerfileModel.RenderFrom(document.Stages[s])));
            for (var i = 0; i < document.Stages[s].Instructions.Count; i++) list.Add((s, i, DockerfileModel.RenderInstruction(document.Stages[s].Instructions[i])));
        }
        return list;
    }

    /// <summary>The parser directive lines the document renders (syntax, escape, check).</summary>
    public static string Header(DockerfileDocument document)
    {
        var text = DockerfileModel.Render(new DockerfileDocument { SyntaxHeader = document.SyntaxHeader, Syntax = document.Syntax, Escape = document.Escape, CheckDirective = document.CheckDirective });
        return text;
    }

    /// <summary>State for text the model just rendered.</summary>
    public static DockerfileSourceState FromRender(DockerfileDocument document)
    {
        var rendered = DockerfileModel.RenderWithMap(document);
        return new(rendered.Text, rendered.Lines, Elements(document).Select(e => e.Text).ToList(), Header(document));
    }

    /// <summary>State for text the user typed and the parser read into <paramref name="parsed"/>.</summary>
    public static DockerfileSourceState FromParse(string text, DockerfileParseResult parsed) =>
        new(text, parsed.LineMap, Elements(parsed.Document).Select(e => e.Text).ToList(), Header(parsed.Document));

    /// <summary>
    /// Applies the model changes to the text by replacing only the changed elements' lines. Returns null when the
    /// structure differs from the state, so a full render is needed.
    /// </summary>
    public static DockerfileSourceState? TryPatch(DockerfileSourceState state, DockerfileDocument document)
    {
        var elements = Elements(document);
        if (elements.Count != state.Lines.Count || elements.Count != state.Elements.Count) return null;
        for (var k = 0; k < elements.Count; k++)
            if (elements[k].Stage != state.Lines[k].StageIndex || elements[k].Instruction != state.Lines[k].InstructionIndex) return null;
        if (Header(document) != state.Header) return null;
        // Elements parsed from one line (EXPOSE 80 443) share lines and cannot be patched separately.
        for (var k = 1; k < state.Lines.Count; k++)
            if (state.Lines[k].StartLine <= state.Lines[k - 1].EndLine) return null;

        var newline = state.Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = state.Text.Replace("\r\n", "\n").Split('\n').ToList();
        var spans = state.Lines.ToList();
        var texts = state.Elements.ToList();
        var shift = 0;
        for (var k = 0; k < elements.Count; k++)
        {
            var span = spans[k] with { StartLine = spans[k].StartLine + shift, EndLine = spans[k].EndLine + shift };
            spans[k] = span;
            if (elements[k].Text == texts[k]) continue;
            if (span.EndLine >= lines.Count) return null;
            var replacement = elements[k].Text.Replace("\r\n", "\n").Split('\n');
            // Keep the indentation of the first line the user wrote.
            var indent = new string(lines[span.StartLine].TakeWhile(c => c is ' ' or '\t').ToArray());
            if (indent.Length > 0) replacement[0] = indent + replacement[0];
            lines.RemoveRange(span.StartLine, span.EndLine - span.StartLine + 1);
            lines.InsertRange(span.StartLine, replacement);
            var delta = replacement.Length - (span.EndLine - span.StartLine + 1);
            spans[k] = span with { EndLine = span.StartLine + replacement.Length - 1 };
            texts[k] = elements[k].Text;
            shift += delta;
        }
        return new(string.Join(newline, lines), spans, texts, state.Header);
    }
}
