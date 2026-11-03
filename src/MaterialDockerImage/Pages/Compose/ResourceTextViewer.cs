using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Read-only text viewer with its own search: either keeps only matching lines (log style) or jumps between matches
/// (document style). Used for inspect JSON, resolved Compose config and streaming logs.
/// </summary>
public sealed class ResourceTextViewer : Grid
{
    private readonly List<string> lines = new();
    private readonly TextBox box;
    private readonly SearchFilter search = new("Search", 280);
    private readonly CheckBox onlyMatching;
    private readonly TextBlock status = Ui.Caption("");
    private readonly int maxLines;
    private int lastFind = -1;
    public TextBox Box => box;
    public SearchFilter Search => search;
    public string AllText => string.Join(Environment.NewLine, lines);
    public bool FollowTail { get; set; } = true;

    public ResourceTextViewer(string name, bool filterLines, int maxLines = 20000, double minHeight = 320)
    {
        this.maxLines = maxLines;
        box = Ui.CodeBox("", minHeight);
        box.IsReadOnly = true;
        box.IsReadOnlyCaretVisible = true;
        System.Windows.Automation.AutomationProperties.SetName(box, Loc.T(name));
        onlyMatching = Ui.Check("Only matching lines", filterLines, _ => Render());
        onlyMatching.VerticalAlignment = VerticalAlignment.Center;
        search.Changed += () => { lastFind = -1; Render(); };
        search.Box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { FindNext(Keyboard.Modifiers == ModifierKeys.Shift); e.Handled = true; } };
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { search.Box.Focus(); search.Box.SelectAll(); e.Handled = true; }
            else if (e.Key == Key.F3) { FindNext(Keyboard.Modifiers == ModifierKeys.Shift); e.Handled = true; }
        };
        var copy = Ui.IconButton(Icons.ContentCopy, "Copy shown text", () => { Clipboard.SetText(box.Text); AppHost.Current.Notify.Info("Copied the text shown in the viewer."); });
        var next = Ui.IconButton(Icons.ExpandMore, "Next match (Enter or F3)", () => FindNext(false));
        var previous = Ui.IconButton(Icons.ExpandLess, "Previous match (Shift+Enter)", () => FindNext(true));
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        bar.Children.Add(search.Dock(Dock.Left));
        bar.Children.Add(copy.Dock(Dock.Right));
        var tools = Ui.Wrap(8, previous, next, onlyMatching, status);
        tools.Margin = new Thickness(8, 0, 0, 0);
        bar.Children.Add(tools);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Children.Add(bar);
        Children.Add(box.At(1));
    }

    public void SetText(string text)
    {
        lines.Clear();
        lines.AddRange(text.Replace("\r\n", "\n").Split('\n'));
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        Trim();
        Render();
    }

    public void Append(string chunk)
    {
        foreach (var part in chunk.Replace("\r\n", "\n").Split('\n')) if (part.Length > 0) lines.Add(part);
        Trim();
        Render();
    }

    private void Trim() { if (lines.Count > maxLines) lines.RemoveRange(0, lines.Count - maxLines); }

    private void Render()
    {
        var filtering = onlyMatching.IsChecked == true && search.IsActive;
        var atEnd = box.VerticalOffset + box.ViewportHeight >= box.ExtentHeight - 4;
        if (filtering)
        {
            var shown = lines.Where(search.Matches).ToList();
            box.Text = string.Join(Environment.NewLine, shown);
            status.Text = search.Status.Length > 0 ? search.Status : $"{shown.Count} of {lines.Count} lines match";
        }
        else
        {
            box.Text = string.Join(Environment.NewLine, lines);
            status.Text = search.IsActive ? (search.Status.Length > 0 ? search.Status : $"{lines.Count(search.Matches)} matching lines · Enter for next") : $"{lines.Count} lines" + (lines.Count >= maxLines ? $" (oldest dropped beyond {maxLines})" : "");
        }
        if (FollowTail && atEnd) box.ScrollToEnd();
    }

    private void FindNext(bool backwards)
    {
        var message = TextFind.Next(box, search, ref lastFind, backwards);
        if (message != null) status.Text = message;
    }

    private static double DialogHeight(double preferred)
    {
        var window = Application.Current?.MainWindow?.ActualHeight ?? 0;
        return window <= 0 ? preferred : Math.Max(320, Math.Min(preferred, window * 0.86 - 72));
    }

    /// <summary>Opens a viewer in an overlay with a Close button.</summary>
    public static void Show(string title, string text, string? subtitle = null, bool filterLines = false)
    {
        var viewer = new ResourceTextViewer(title, filterLines, 20000, 200) { FollowTail = false };
        viewer.SetText(text);
        Action? close = null;
        var closeButton = Ui.Button("Close", null, ButtonKind.Filled, () => close?.Invoke());
        closeButton.HorizontalAlignment = HorizontalAlignment.Right;
        var body = Ui.Rows(12, [0, 1, 0], Ui.VStack(2, Ui.Title(title, 20), subtitle == null ? null : Ui.Caption(subtitle)), viewer, closeButton);
        body.Height = DialogHeight(600);
        close = AppHost.Current.Overlay.Show(body, 900);
    }

    /// <summary>Opens a streaming viewer (for example logs with --follow). The process is canceled when the viewer closes.</summary>
    public static void ShowStream(string title, string subtitle, IReadOnlyList<string> args, string? context, string stopLabel = "Stop following", string runningText = "Following. New lines appear as the engine sends them.", string endedText = "The log stream ended.", string stoppedText = "Stopped following. The lines above were received before stopping.")
    {
        var host = AppHost.Current;
        var viewer = new ResourceTextViewer(title, true, 20000, 200);
        var cts = new CancellationTokenSource();
        var state = Ui.Caption("Connecting…");
        Action? close = null;
        var pending = new StringBuilder();
        var flush = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        flush.Tick += (_, _) => { if (pending.Length == 0) return; var text = pending.ToString(); pending.Clear(); viewer.Append(text); };
        flush.Start();
        var progress = new Progress<DockerOutput>(o => pending.Append(o.Text));
        var stop = Ui.Button(stopLabel, Icons.Stop, ButtonKind.Tonal, () => cts.Cancel());
        var closeButton = Ui.Button("Close", null, ButtonKind.Filled, () => close?.Invoke());
        var actions = Ui.Columns(8, [1, 0, 0], state, stop, closeButton);
        var body = Ui.Rows(12, [0, 1, 0], Ui.VStack(2, Ui.Title(title, 20), Ui.Caption(subtitle)), viewer, actions);
        body.Height = DialogHeight(620);
        close = host.Overlay.Show(body, 960, () => cts.Cancel());
        // The overlay's close handle does not report Close button closes, so cancel on unload as well.
        body.Unloaded += (_, _) => { cts.Cancel(); flush.Stop(); };
        _ = Run();
        async Task Run()
        {
            state.Text = Loc.T(runningText);
            try
            {
                var result = await host.Docker.ExecuteAsync(args, context, progress, cts.Token);
                await Task.Delay(250);
                if (pending.Length > 0) { viewer.Append(pending.ToString()); pending.Clear(); }
                state.Text = result.Cancelled ? Loc.T(stoppedText) : result.Success ? Loc.T(endedText) : $"The command failed with exit code {result.ExitCode}.";
                if (!result.Success && !result.Cancelled && result.StandardError.Trim().Length > 0) viewer.Append(result.StandardError);
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException or IOException)
            {
                state.Text = "Could not start the command: " + e.Message;
            }
            finally { stop.IsEnabled = false; }
        }
    }
}

/// <summary>Find next/previous inside a TextBox using a SearchFilter's query, regex mode and case setting.</summary>
public static class TextFind
{
    /// <summary>Selects the next match after <paramref name="last"/> (wrapping). Returns a status message when nothing was selected.</summary>
    public static string? Next(TextBox box, SearchFilter search, ref int last, bool backwards)
    {
        if (!search.IsActive) return null;
        var text = box.Text;
        Regex? regex = null;
        if (search.RegexMode)
        {
            try { regex = new Regex(search.Query, search.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None, TimeSpan.FromMilliseconds(100)); }
            catch (ArgumentException) { return Loc.T("Invalid pattern. Correct the expression to search."); }
        }
        var comparison = search.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        (int Index, int Length)? Find(int from, bool back)
        {
            try
            {
                if (regex != null)
                {
                    var all = regex.Matches(text).Where(m => m.Length > 0).ToList();
                    var m = back ? all.LastOrDefault(x => x.Index < from) : all.FirstOrDefault(x => x.Index >= from);
                    return m == null ? null : (m.Index, m.Length);
                }
                if (text.Length == 0) return null;
                var i = back ? (from <= 0 ? -1 : text.LastIndexOf(search.Query, Math.Min(text.Length - 1, from - 1), comparison)) : text.IndexOf(search.Query, Math.Min(text.Length, Math.Max(0, from)), comparison);
                return i < 0 ? null : (i, search.Query.Length);
            }
            catch (RegexMatchTimeoutException) { return null; }
        }
        var start = backwards ? (last < 0 ? text.Length : last) : last + 1;
        var found = Find(start, backwards) ?? Find(backwards ? text.Length : 0, backwards);
        if (found is not { } f) return Loc.T("No match.");
        last = f.Index;
        box.Focus();
        box.Select(f.Index, f.Length);
        var line = box.GetLineIndexFromCharacterIndex(f.Index);
        if (line >= 0) box.ScrollToLine(line);
        return null;
    }
}
