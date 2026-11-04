using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using MaterialDockerImage.UI;
using MaterialDockerImage.Services;

namespace MaterialDockerImage.Pages;

/// <summary>Compact per-container card: CPU with a sparkline, memory, network I/O, block I/O and PIDs from the latest real sample.</summary>
public sealed class ContainerStatCard : Border
{
    private readonly TextBlock name = Ui.Text("", 14, FontWeights.SemiBold, "OnSurface", false, false);
    private readonly TextBlock cpu = Ui.Text("", 14, FontWeights.SemiBold, "Primary", false, false);
    private readonly TextBlock mem = Ui.Mono("", 12, "OnSurfaceVariant");
    private readonly TextBlock net = Ui.Mono("", 12, "OnSurfaceVariant");
    private readonly TextBlock block = Ui.Mono("", 12, "OnSurfaceVariant");
    private readonly TextBlock pids = Ui.Mono("", 12, "OnSurfaceVariant");
    private readonly TextBlock age = Ui.Caption("");
    private readonly Sparkline spark = new() { SeriesBrushKey = "Primary", Margin = new Thickness(0, 6, 0, 0) };
    public string ContainerName { get; }
    public event Action<string>? Picked;

    public ContainerStatCard(string containerName)
    {
        ContainerName = containerName;
        name.Text = containerName;
        CornerRadius = new CornerRadius(16);
        Padding = new Thickness(14, 12, 14, 12);
        BorderThickness = new Thickness(2);
        Focusable = true;
        Cursor = Cursors.Hand;
        FocusVisualStyle = null;
        SetResourceReference(BackgroundProperty, "SurfaceContainer");
        var top = new DockPanel();
        top.Children.Add(cpu.Dock(Dock.Right));
        top.Children.Add(name);
        static Grid Pair(string label, TextBlock value) => Ui.Columns(8, [-72, 1], Ui.Caption(label), value);
        Child = Ui.VStack(4, top, spark, Pair("Memory", mem), Pair("Net I/O", net), Pair("Block I/O", block), Pair("PIDs", pids), age);
        MouseLeftButtonUp += (_, _) => Picked?.Invoke(ContainerName);
        KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { Picked?.Invoke(ContainerName); e.Handled = true; } };
        GotKeyboardFocus += (_, _) => SetResourceReference(BorderBrushProperty, "Primary");
        LostKeyboardFocus += (_, _) => UpdateBorder();
        AutomationProperties.SetName(this, containerName);
        UpdateBorder();
    }

    private bool selected;
    public bool IsSelectedCard { get => selected; set { selected = value; UpdateBorder(); } }
    private void UpdateBorder()
    {
        if (IsKeyboardFocused) return;
        if (selected) SetResourceReference(BorderBrushProperty, "Primary"); else BorderBrush = System.Windows.Media.Brushes.Transparent;
        SetResourceReference(BackgroundProperty, selected ? "SecondaryContainer" : "SurfaceContainer");
    }

    public void Update(IReadOnlyList<StatsSample> samples, bool running)
    {
        if (samples.Count == 0) { cpu.Text = "—"; mem.Text = net.Text = block.Text = pids.Text = "—"; age.Text = Services.Loc.T("No samples yet"); spark.SetValues([]); return; }
        var s = samples[^1];
        cpu.Text = s.CpuPercent is { } c ? $"{c:0.0}%" : s.CpuText;
        mem.Text = s.MemText.Length > 0 ? s.MemText : "—";
        net.Text = s.NetText.Length > 0 ? s.NetText : "—";
        block.Text = s.BlockText.Length > 0 ? s.BlockText : "—";
        pids.Text = s.PidsText.Length > 0 ? s.PidsText : "—";
        spark.SetValues(samples.Where(x => x.CpuPercent.HasValue).Select(x => x.CpuPercent!.Value).ToList());
        age.Text = running ? $"Sampled {s.Time.ToLocalTime():HH:mm:ss}" : $"Not in the latest sample (last seen {s.Time.ToLocalTime():HH:mm:ss})";
        AutomationProperties.SetHelpText(this, $"CPU {cpu.Text}, memory {mem.Text}, network {net.Text}, block {block.Text}, PIDs {pids.Text}. {age.Text}");
    }
}
