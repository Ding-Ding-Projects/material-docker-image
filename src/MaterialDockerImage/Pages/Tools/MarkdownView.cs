using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// A small Markdown renderer for the offline documentation: headings, paragraphs, bullet and numbered lists,
/// fenced code blocks, inline code, bold, italic, simple tables (shown as monospace) and links. Links are reported
/// through <see cref="LinkClicked"/> so the host decides how to open them.
/// </summary>
public sealed class MarkdownView : StackPanel
{
    private static readonly Regex Inline = new(@"(`[^`]+`)|(\*\*[^*]+\*\*)|(__[^_]+__)|(\*[^*\s][^*]*\*)|(_[^_\s][^_]*_)|(\[[^\]]+\]\([^)\s]+\))", RegexOptions.Compiled);
    private static readonly Regex Ordered = new(@"^\s*(\d+)[.)]\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex Bullet = new(@"^(\s*)[-*+]\s+(.*)$", RegexOptions.Compiled);
    public event Action<string>? LinkClicked;

    public void Render(string markdown)
    {
        Children.Clear();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var paragraph = new StringBuilder();
        void Flush()
        {
            if (paragraph.Length == 0) return;
            Children.Add(Block(paragraph.ToString().Trim(), 14, FontWeights.Normal, "OnSurface", new Thickness(0, 0, 0, 10)));
            paragraph.Clear();
        }
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                Flush();
                var code = new StringBuilder();
                for (i++; i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal); i++) code.AppendLine(lines[i]);
                Children.Add(Code(code.ToString().TrimEnd('\n', '\r')));
                continue;
            }
            if (line.Trim().Length == 0) { Flush(); continue; }
            var heading = Regex.Match(line, @"^(#{1,6})\s+(.*)$");
            if (heading.Success)
            {
                Flush();
                var level = heading.Groups[1].Length;
                var size = level switch { 1 => 24, 2 => 19, 3 => 16, _ => 14 };
                Children.Add(Block(heading.Groups[2].Value.Trim(), size, FontWeights.SemiBold, "OnSurface", new Thickness(0, level == 1 ? 0 : 12, 0, 8)));
                continue;
            }
            if (line.TrimStart().StartsWith('|'))
            {
                Flush();
                var table = new StringBuilder();
                for (; i < lines.Length && lines[i].TrimStart().StartsWith('|'); i++)
                    if (!Regex.IsMatch(lines[i], @"^\s*\|[\s:|-]+\|\s*$")) table.AppendLine(lines[i].Trim());
                i--;
                Children.Add(Code(table.ToString().TrimEnd()));
                continue;
            }
            var bullet = Bullet.Match(line);
            var ordered = Ordered.Match(line);
            if (bullet.Success || ordered.Success)
            {
                Flush();
                var indent = bullet.Success ? bullet.Groups[1].Value.Length / 2 : 0;
                var marker = bullet.Success ? "•" : ordered.Groups[1].Value + ".";
                var text = bullet.Success ? bullet.Groups[2].Value : ordered.Groups[2].Value;
                var row = new DockPanel { Margin = new Thickness(8 + indent * 18, 0, 0, 4) };
                row.Children.Add(Ui.Text(marker, 14, null, "OnSurfaceVariant", false, false).Margin(0, 0, 8, 0).Dock(Dock.Left));
                row.Children.Add(Block(text, 14, FontWeights.Normal, "OnSurface", new Thickness(0)));
                Children.Add(row);
                continue;
            }
            if (line.StartsWith('>')) { Flush(); Children.Add(Block(line.TrimStart('>', ' '), 14, FontWeights.Normal, "OnSurfaceVariant", new Thickness(12, 0, 0, 10))); continue; }
            paragraph.Append(line.Trim()).Append(' ');
        }
        Flush();
    }

    private Border Code(string text)
    {
        var t = Ui.Mono(text, 12.5, "OnSurface", false);
        var scroll = new ScrollViewer { Content = t, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        var card = Ui.Card(scroll, 12, "SurfaceContainer", 8);
        card.Margin = new Thickness(0, 0, 0, 12);
        return card;
    }

    private TextBlock Block(string text, double size, FontWeight weight, string brush, Thickness margin)
    {
        var block = new TextBlock { FontSize = size, FontWeight = weight, TextWrapping = TextWrapping.Wrap, Margin = margin, LineHeight = Math.Round(size * 1.45) };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        AddInlines(block.Inlines, text);
        return block;
    }

    private void AddInlines(InlineCollection target, string text)
    {
        var position = 0;
        foreach (Match m in Inline.Matches(text))
        {
            if (m.Index > position) target.Add(new Run(text[position..m.Index]));
            var v = m.Value;
            if (m.Groups[1].Success)
            {
                var run = new Run(v[1..^1]);
                run.SetResourceReference(TextElement.FontFamilyProperty, "MonoFont");
                run.SetResourceReference(TextElement.BackgroundProperty, "SurfaceHigh");
                target.Add(run);
            }
            else if (m.Groups[2].Success || m.Groups[3].Success) target.Add(new Bold(new Run(v[2..^2])));
            else if (m.Groups[4].Success || m.Groups[5].Success) target.Add(new Italic(new Run(v[1..^1])));
            else
            {
                var close = v.IndexOf("](", StringComparison.Ordinal);
                var label = v[1..close];
                var href = v[(close + 2)..^1];
                var link = new Hyperlink(new Run(label)) { ToolTip = href };
                link.SetResourceReference(TextElement.ForegroundProperty, "Primary");
                link.Click += (_, _) => LinkClicked?.Invoke(href);
                target.Add(link);
            }
            position = m.Index + m.Length;
        }
        if (position < text.Length) target.Add(new Run(text[position..]));
    }

    /// <summary>Plain text of a Markdown document for searching (markup characters removed).</summary>
    public static string PlainText(string markdown) => Regex.Replace(markdown, @"[#*_`>|\[\]]", " ");
}
