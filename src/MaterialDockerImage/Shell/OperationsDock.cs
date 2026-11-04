using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Shell;

/// <summary>Collapsible operation panel: real progress, elapsed time, cancel, bounded searchable output.</summary>
public sealed class OperationsDock : Border
{
    private readonly TextBox output = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(0), FontSize = 12, VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(12, 8, 12, 8) };
    private readonly StringBuilder transcript = new();
    private readonly TextBlock title = Ui.Text("Ready", 14, FontWeights.SemiBold);
    private readonly TextBlock detail = Ui.Text("No operation is running.", 13, null, "OnSurfaceVariant", false);
    // Hidden until the first operation starts, so the idle status line keeps its full width on small windows.
    private readonly ProgressBar progress = new() { Width = 160, Minimum = 0, Maximum = 1, Value = 0, Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly Button cancel;
    private readonly Button toggle;
    private readonly SearchFilter search = new("Search output", 200);
    private readonly Grid body;
    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private Stopwatch? watch;
    private string currentLabel = "";
    public event Action? CancelRequested;
    public bool Expanded { get; private set; } = true;
    /// <summary>Short windows give the output panel less height so the page keeps its working space.</summary>
    public bool CompactHeight { set { bodyHeight = value ? 96 : 170; body.Height = bodyHeight; } }
    private double bodyHeight = 170;
    public string Transcript => transcript.ToString();

    public OperationsDock()
    {
        SetResourceReference(BackgroundProperty, "SurfaceLow");
        CornerRadius = new CornerRadius(16);
        Margin = new Thickness(0, 12, 0, 0);
        output.SetResourceReference(Control.FontFamilyProperty, "MonoFont");
        output.SetResourceReference(Control.BackgroundProperty, "SurfaceLow");
        AutomationProperties.SetName(output, Loc.T("Operation output"));
        cancel = Ui.Button("Cancel", Icons.Stop, ButtonKind.Outlined, () => CancelRequested?.Invoke());
        cancel.SetResourceReference(Control.ForegroundProperty, "Error");
        cancel.IsEnabled = false;
        toggle = Ui.IconButton(Icons.ExpandMore, "Collapse output", () => SetExpanded(!Expanded));
        var header = new DockPanel { Margin = new Thickness(16, 6, 8, 6) };
        header.Children.Add(Ui.Icon(Icons.Terminal, 18, false, "Primary").Margin(0, 0, 10, 0).Dock(System.Windows.Controls.Dock.Left));
        var right = Ui.HStack(4, search, Ui.IconButton(Icons.ContentCopy, "Copy output", () => { Clipboard.SetText(output.Text); AppHost.Current?.Notify.Info("Output copied."); }), Ui.IconButton(Icons.Delete, "Clear output", Clear), cancel, toggle);
        header.Children.Add(right.Dock(System.Windows.Controls.Dock.Right));
        header.Children.Add(progress.Dock(System.Windows.Controls.Dock.Right));
        header.Children.Add(Ui.HStack(10, title, detail));
        body = new Grid { Height = 170 };
        body.Children.Add(output);
        Child = Ui.Rows(0, [0, 0], header, body);
        clock.Tick += (_, _) => { if (watch != null) detail.Text = $"{Loc.T("Working")} · {watch.Elapsed:mm\\:ss} {Loc.T("elapsed")}"; };
        search.Changed += Render;
    }

    public void SetExpanded(bool expanded)
    {
        var changed = Expanded != expanded;
        Expanded = expanded;
        if (changed && IsLoaded && Motion.Enabled)
        {
            // The panel height animates; the real state (Visibility) is applied before expanding and after collapsing.
            if (expanded) { body.Visibility = Visibility.Visible; Motion.AnimateHeight(body, 0, bodyHeight); }
            else Motion.AnimateHeight(body, body.ActualHeight > 0 ? body.ActualHeight : bodyHeight, 0, () => { if (!Expanded) body.Visibility = Visibility.Collapsed; });
        }
        else body.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        toggle.Content = expanded ? Icons.ExpandMore : Icons.ExpandLess;
        AutomationProperties.SetName(toggle, Loc.T(expanded ? "Collapse output" : "Expand output"));
    }

    public void Begin(string label)
    {
        currentLabel = label;
        if (!Expanded) SetExpanded(true);
        watch = Stopwatch.StartNew();
        title.Text = label;
        detail.Text = Loc.T("Working");
        progress.IsIndeterminate = true; progress.Value = 0;
        progress.Visibility = Visibility.Visible;
        cancel.IsEnabled = true;
        clock.Start();
        Append($"[{DateTimeOffset.Now:HH:mm:ss}] {label}");
    }

    /// <summary>Records the actual result. Cancellation and unknown outcomes never read as success.</summary>
    public void End(bool success, bool cancelled, int exitCode)
    {
        clock.Stop();
        var elapsed = watch?.Elapsed ?? TimeSpan.Zero;
        watch = null;
        cancel.IsEnabled = false;
        progress.IsIndeterminate = false;
        Motion.SmoothProgress(progress, success ? 1 : 0);
        detail.Text = cancelled ? $"Canceled after {elapsed.TotalSeconds:F1}s. Engine-side work may continue; refresh before retrying." : success ? $"Completed in {elapsed.TotalSeconds:F1}s (exit 0)." : $"Failed (exit {exitCode}) after {elapsed.TotalSeconds:F1}s. Review the output.";
        Append($"[{DateTimeOffset.Now:HH:mm:ss}] {currentLabel}: {(cancelled ? "canceled" : success ? "completed" : "failed, exit " + exitCode)}");
    }

    public void Append(string line)
    {
        transcript.AppendLine(line);
        if (transcript.Length > 400_000) transcript.Remove(0, transcript.Length - 300_000);
        if (search.IsActive) { if (search.Matches(line)) output.AppendText(line + Environment.NewLine); }
        else output.AppendText(line + Environment.NewLine);
        if (output.Text.Length > 400_000) output.Text = output.Text[^300_000..];
        output.ScrollToEnd();
    }

    private void Render()
    {
        var lines = transcript.ToString().Split('\n');
        output.Text = string.Join("\n", search.IsActive ? lines.Where(l => search.Matches(l)) : lines);
        output.ScrollToEnd();
    }

    public void Clear() { transcript.Clear(); output.Clear(); }
}
