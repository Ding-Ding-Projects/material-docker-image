using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>One log line with its stream. Raw is the text exactly as docker printed it (including any timestamp).</summary>
internal sealed record LogLine(bool IsError, string Time, string Text, string Raw)
{
    public string Stream => IsError ? "stderr" : "stdout";
}

/// <summary>
/// Log viewer for one container: tail size, timestamps, follow (streams docker container logs --follow in the background
/// with a cancellation token this view owns), stderr filter, search, copy and save.
/// </summary>
internal sealed class ContainerLogs : DockPanel
{
    private static AppHost Host => AppHost.Current;
    private const int KeepLines = 5000;
    private readonly List<LogLine> all = new();
    private readonly ObservableCollection<LogLine> shown = new();
    private readonly ListBox list = new();
    private readonly SearchFilter search = new("Search logs", 220);
    private readonly TextBlock count = Ui.Caption("");
    private readonly TextBlock status = Ui.Caption("");
    private readonly ToggleButtonState follow = new(false);
    private readonly ToggleButtonState timestamps = new(true);
    private readonly ToggleButtonState stderrOnly = new(false);
    private int tail = 500;
    private ContainerRow? container;
    private string? context;
    private CancellationTokenSource? streaming;
    private readonly StringBuilder partialOut = new();
    private readonly StringBuilder partialErr = new();
    private int session;

    private sealed class ToggleButtonState(bool value) { public bool Value = value; }

    public ContainerLogs()
    {
        LastChildFill = true;
        var tailPicker = Ui.Segmented(["100", "500", "2000"], "500", v => { tail = int.Parse(v, CultureInfo.InvariantCulture); Reload(); }, false);
        tailPicker.ToolTip = Loc.T("Number of recent lines to fetch (--tail)");
        System.Windows.Automation.AutomationProperties.SetName(tailPicker, Loc.T("Lines to fetch"));
        var followChip = Ui.Chip("Follow", false, v => { follow.Value = v; Reload(); });
        followChip.ToolTip = Loc.T("Stream new lines as they are written (docker container logs --follow). Stops when you leave this tab, choose another container or leave the page.");
        var timeChip = Ui.Chip("Timestamps", true, v => { timestamps.Value = v; Reload(); });
        var errChip = Ui.Chip("stderr only", false, v => { stderrOnly.Value = v; Render(); });
        var copy = Ui.IconButton(Icons.ContentCopy, "Copy shown lines", () => PageKit.Copy(ShownText(), $"{shown.Count} log lines"));
        var save = Ui.IconButton(Icons.Save, "Save shown lines as", () => PageKit.SaveText(PageKit.SafeFileName(container?.Name ?? "container") + "-logs.txt", "Log files|*.log;*.txt|All files|*.*", ShownText(), $"{shown.Count} log lines"));
        var reload = Ui.IconButton(Icons.Refresh, "Reload logs", Reload);

        var row1 = new WrapPanel();
        foreach (var e in new FrameworkElement[] { search, count }) { e.Margin = new Thickness(0, 0, 8, 6); e.VerticalAlignment = VerticalAlignment.Center; row1.Children.Add(e); }
        var row2 = new WrapPanel();
        foreach (var e in new FrameworkElement[] { tailPicker, followChip, timeChip, errChip, reload, copy, save }) { e.Margin = new Thickness(0, 0, 6, 6); e.VerticalAlignment = VerticalAlignment.Center; row2.Children.Add(e); }
        Children.Add(Ui.VStack(0, row1, row2).Dock(System.Windows.Controls.Dock.Top));
        Children.Add(status.Dock(System.Windows.Controls.Dock.Bottom).Margin(2, 6, 0, 0));

        list.ItemsSource = shown;
        list.ItemTemplate = LineTemplate();
        var itemStyle = new Style(typeof(ListBoxItem), Application.Current.TryFindResource(typeof(ListBoxItem)) as Style);
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 1, 6, 1)));
        itemStyle.Setters.Add(new Setter(MinHeightProperty, 0.0));
        list.ItemContainerStyle = itemStyle;
        list.SelectionMode = SelectionMode.Extended;
        VirtualizingPanel.SetIsVirtualizing(list, true);
        System.Windows.Automation.AutomationProperties.SetName(list, Loc.T("Container logs"));
        var frame = Ui.Card(list, 4, "SurfaceLowest", 12);
        Children.Add(frame);
        search.Changed += Render;
        IsVisibleChanged += (_, _) => { if (!IsVisible) Stop(); };
    }

    private static DataTemplate LineTemplate()
    {
        var grid = new FrameworkElementFactory(typeof(DockPanel));
        var time = new FrameworkElementFactory(typeof(TextBlock));
        time.SetBinding(TextBlock.TextProperty, new Binding(nameof(LogLine.Time)));
        time.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        time.SetResourceReference(TextBlock.ForegroundProperty, "OnSurfaceVariant");
        time.SetValue(TextBlock.FontSizeProperty, 12.0);
        time.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
        time.SetValue(DockPanel.DockProperty, System.Windows.Controls.Dock.Left);
        var stream = new FrameworkElementFactory(typeof(TextBlock), "stream");
        stream.SetBinding(TextBlock.TextProperty, new Binding(nameof(LogLine.Stream)));
        stream.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        stream.SetResourceReference(TextBlock.ForegroundProperty, "Tertiary");
        stream.SetValue(TextBlock.FontSizeProperty, 12.0);
        stream.SetValue(FrameworkElement.WidthProperty, 52.0);
        stream.SetValue(DockPanel.DockProperty, System.Windows.Controls.Dock.Left);
        var text = new FrameworkElementFactory(typeof(TextBlock), "text");
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(LogLine.Text)));
        text.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        text.SetResourceReference(TextBlock.ForegroundProperty, "OnSurface");
        text.SetValue(TextBlock.FontSizeProperty, 12.5);
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        grid.AppendChild(time); grid.AppendChild(stream); grid.AppendChild(text);
        var template = new DataTemplate { VisualTree = grid };
        var err = new DataTrigger { Binding = new Binding(nameof(LogLine.IsError)), Value = true };
        err.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension("Error"), "stream"));
        template.Triggers.Add(err);
        return template;
    }

    public void SetContainer(ContainerRow? row, string? engineContext)
    {
        if (row?.Id == container?.Id && engineContext == context) return;
        Stop();
        container = row; context = engineContext;
        all.Clear(); shown.Clear(); Render();
        status.Text = "";
    }

    /// <summary>Loads the logs for the current container (or starts following when Follow is on).</summary>
    public void Reload()
    {
        Stop();
        if (container == null) return;
        all.Clear(); shown.Clear();
        _ = LoadAsync(container, ++session);
    }

    public void Stop()
    {
        session++;
        if (streaming == null) return;
        if (follow.Value) status.Text = $"Stopped following. {all.Count} lines kept. Turn Follow off and on, or reload, to resume.";
        // The loader owns disposal of its token source; here we only cancel it.
        streaming.Cancel();
        streaming = null;
    }

    private async Task LoadAsync(ContainerRow row, int mySession)
    {
        var args = new List<string> { "container", "logs", "--tail", tail.ToString(CultureInfo.InvariantCulture) };
        if (timestamps.Value) args.Add("--timestamps");
        if (follow.Value) args.Add("--follow");
        args.Add(row.Id);
        var cts = new CancellationTokenSource();
        streaming = cts;
        partialOut.Clear(); partialErr.Clear();
        status.Text = follow.Value ? $"Following {row.Name} · last {tail} lines, then new lines as they arrive · up to {KeepLines} kept" : $"Last {tail} lines of {row.Name} · not following";
        var progress = new Progress<DockerOutput>(chunk => { if (mySession == session) Accept(chunk); });
        try
        {
            var result = await Host.Docker.ExecuteAsync(args, context, progress, cts.Token);
            // Progress callbacks are posted to the dispatcher; let pending ones run before the final flush.
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            if (mySession != session) return;
            Flush();
            if (result.Cancelled) status.Text = $"Stopped. {all.Count} lines kept.";
            else if (!result.Success) status.Text = "docker container logs exited with code " + result.ExitCode + ".";
            else status.Text = (follow.Value ? "The log stream ended (the container stopped). " : "") + $"{all.Count} lines loaded · {DateTime.Now:HH:mm:ss}";
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException or IOException)
        {
            if (mySession == session) status.Text = "Logs could not be read: " + e.Message;
        }
        finally
        {
            if (streaming == cts) streaming = null;
            cts.Dispose();
        }
    }

    private void Accept(DockerOutput chunk)
    {
        var buffer = chunk.IsError ? partialErr : partialOut;
        buffer.Append(chunk.Text.Replace("\r\n", "\n"));
        var text = buffer.ToString();
        var last = text.LastIndexOf('\n');
        if (last < 0) return;
        buffer.Clear().Append(text[(last + 1)..]);
        var stick = IsAtEnd();
        foreach (var line in text[..last].Split('\n')) Add(line, chunk.IsError);
        Trim();
        UpdateCount();
        if (stick && shown.Count > 0) list.ScrollIntoView(shown[^1]);
    }

    private void Flush()
    {
        if (partialOut.Length > 0) { Add(partialOut.ToString(), false); partialOut.Clear(); }
        if (partialErr.Length > 0) { Add(partialErr.ToString(), true); partialErr.Clear(); }
        Trim();
        UpdateCount();
    }

    private void Add(string raw, bool isError)
    {
        raw = raw.TrimEnd('\r');
        var time = "";
        var body = raw;
        if (timestamps.Value)
        {
            var space = raw.IndexOf(' ');
            if (space > 0 && DateTimeOffset.TryParse(raw[..space], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t))
            {
                time = t.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
                body = raw[(space + 1)..];
            }
        }
        var line = new LogLine(isError, time, body, raw);
        all.Add(line);
        if (Passes(line)) shown.Add(line);
    }

    private void Trim()
    {
        var excess = all.Count - KeepLines;
        if (excess <= 0) return;
        var removed = all.GetRange(0, excess).ToHashSet();
        all.RemoveRange(0, excess);
        while (shown.Count > 0 && removed.Contains(shown[0])) shown.RemoveAt(0);
    }

    private bool Passes(LogLine line) => (!stderrOnly.Value || line.IsError) && search.Matches(line.Raw);

    private void Render()
    {
        shown.Clear();
        foreach (var line in all) if (Passes(line)) shown.Add(line);
        UpdateCount();
    }

    private void UpdateCount() =>
        count.Text = search.Status.Length > 0 ? search.Status : search.IsActive || stderrOnly.Value ? $"{shown.Count} of {all.Count}" : $"{all.Count} lines";

    private bool IsAtEnd()
    {
        if (shown.Count == 0) return true;
        var scroller = FindScroll(list);
        return scroller == null || scroller.VerticalOffset >= scroller.ScrollableHeight - 2;
    }

    private static ScrollViewer? FindScroll(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer s) return s;
            if (FindScroll(child) is { } nested) return nested;
        }
        return null;
    }

    private string ShownText() => string.Join(Environment.NewLine, shown.Select(l => (l.IsError ? "[stderr] " : "[stdout] ") + l.Raw));
}
