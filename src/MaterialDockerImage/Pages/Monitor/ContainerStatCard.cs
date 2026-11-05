using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using MaterialDockerImage.UI;
using MaterialDockerImage.Services;

namespace MaterialDockerImage.Pages;

/// <summary>Compact per-container card: memory usage bar, CPU, network I/O, block I/O and PIDs from the latest real sample.</summary>
public sealed class ContainerStatCard : Border
{
    private readonly TextBlock name = Ui.Text("", 15, FontWeights.SemiBold, "OnSurface", false, false);
    private readonly TextBlock cpu = Ui.Text("", 13, null, "OnSurface", false, false);
    private readonly TextBlock mem = Ui.Mono("", 12, "OnSurfaceVariant");
    private readonly TextBlock net = Ui.Mono("", 12, "OnSurfaceVariant");
    private readonly TextBlock block = Ui.Mono("", 12, "OnSurfaceVariant");
    private readonly TextBlock pids = Ui.Mono("", 12, "OnSurfaceVariant");
    private readonly TextBlock age = Ui.Caption("");
    private readonly System.Windows.Shapes.Ellipse dot = new() { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    private readonly ProgressBar usage = new() { Height = 4, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 4, 0, 0) };
    public string ContainerName { get; }
    public event Action<string>? Picked;

    public ContainerStatCard(string containerName)
    {
        ContainerName = containerName;
        name.Text = containerName;
        CornerRadius = new CornerRadius(16);
        Padding = new Thickness(16, 14, 16, 14);
        BorderThickness = new Thickness(1);
        Focusable = true;
        Cursor = Cursors.Hand;
        FocusVisualStyle = null;
        SetResourceReference(BackgroundProperty, "SurfaceLow");
        var top = new DockPanel();
        top.Children.Add(cpu.Dock(Dock.Right));
        top.Children.Add(dot.Dock(Dock.Left));
        top.Children.Add(name);
        static Grid Pair(string label, TextBlock value) { var l = Ui.Text(label, 12, null, "OnSurfaceVariant"); return Ui.Columns(8, [-64, 1], l, value); }
        Child = Ui.VStack(6, top, Pair("Net I/O", net), Pair("Block I/O", block), Pair("Memory", mem), Pair("PIDs", pids), usage, age);
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
        if (selected) SetResourceReference(BorderBrushProperty, "Primary"); else SetResourceReference(BorderBrushProperty, "SurfaceLow");
        SetResourceReference(BackgroundProperty, "SurfaceLow");
    }

    public void Update(IReadOnlyList<StatsSample> samples, bool running)
    {
        if (samples.Count == 0) { cpu.Text = "—"; mem.Text = net.Text = block.Text = pids.Text = "—"; age.Text = Services.Loc.T("No samples yet"); usage.Value = 0; dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Outline"); return; }
        var s = samples[^1];
        cpu.Text = s.CpuPercent is { } c ? $"{c:0.0}%" : s.CpuText;
        mem.Text = s.MemText.Length > 0 ? s.MemText : "—";
        net.Text = s.NetText.Length > 0 ? s.NetText : "—";
        block.Text = s.BlockText.Length > 0 ? s.BlockText : "—";
        pids.Text = s.PidsText.Length > 0 ? s.PidsText : "—";
        usage.Value = Math.Clamp(s.MemoryPercent ?? 0, 0, 100);
        dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, running ? "Success" : "Outline");
        age.Text = running ? $"Sampled {s.Time.ToLocalTime():HH:mm:ss}" : $"Not in the latest sample (last seen {s.Time.ToLocalTime():HH:mm:ss})";
        AutomationProperties.SetHelpText(this, $"CPU {cpu.Text}, memory {mem.Text}, network {net.Text}, block {block.Text}, PIDs {pids.Text}. {age.Text}");
    }
}
