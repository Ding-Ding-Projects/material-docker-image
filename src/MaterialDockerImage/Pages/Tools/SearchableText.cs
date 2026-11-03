using System.Text;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Read-only monospace text with its own search. While a search is active only matching lines are shown,
/// prefixed with their original line numbers, so long command output and diffs stay navigable.
/// </summary>
public sealed class SearchableText : DockPanel
{
    private string text = "";
    private readonly TextBox box;
    private readonly TextBlock status = Ui.Caption("");
    public SearchFilter Search { get; }
    public string Text { get => text; set { text = value ?? ""; Apply(); } }

    public SearchableText(string placeholder = "Search output", double minHeight = 160, double maxHeight = double.PositiveInfinity)
    {
        LastChildFill = true;
        Search = new SearchFilter(placeholder, 280);
        var copy = Ui.IconButton(Icons.ContentCopy, "Copy shown text", () =>
        {
            if (box!.Text.Length == 0) return;
            Clipboard.SetText(box.Text);
            Services.AppHost.Current.Notify.Info("Copied the shown text.");
        });
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        bar.Children.Add(Search.Dock(System.Windows.Controls.Dock.Left));
        bar.Children.Add(copy.Dock(System.Windows.Controls.Dock.Right));
        bar.Children.Add(status.Margin(12, 0, 8, 0));
        Children.Add(bar.Dock(System.Windows.Controls.Dock.Top));
        box = Ui.CodeBox("", minHeight);
        box.IsReadOnly = true;
        box.MaxHeight = maxHeight;
        Children.Add(box);
        Search.Changed += Apply;
    }

    private void Apply()
    {
        if (!Search.IsActive) { box.Text = text; status.Text = text.Length == 0 ? "" : $"{CountLines(text)} lines"; return; }
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var shown = new StringBuilder();
        var hits = 0;
        var width = lines.Length.ToString().Length;
        for (var i = 0; i < lines.Length; i++)
        {
            if (!Search.Matches(lines[i])) continue;
            hits++;
            shown.Append((i + 1).ToString().PadLeft(width)).Append("  ").AppendLine(lines[i]);
        }
        box.Text = shown.ToString();
        status.Text = Search.Status.Length > 0 ? Search.Status : $"{hits} of {lines.Length} lines match";
    }

    private static int CountLines(string value) => value.Length == 0 ? 0 : value.Count(c => c == '\n') + (value.EndsWith('\n') ? 0 : 1);
}
