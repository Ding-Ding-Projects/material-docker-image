using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Compact stage outline shown beside the Build editor ("canvas split"). It is a read-only view of
/// DockerfileGraph for the current editor text; selecting a stage jumps to its FROM line.
/// </summary>
public sealed class StageMap : Border
{
    private readonly StackPanel list = new();
    private readonly TextBlock state = Ui.Caption("");
    public event Action<int>? LineRequested;
    public event Action? OpenCanvasRequested;

    public StageMap()
    {
        CornerRadius = new CornerRadius(12); Padding = new Thickness(12);
        SetResourceReference(BackgroundProperty, "SurfaceContainer");
        var open = Ui.Button("Open Canvas", Icons.AccountTree, ButtonKind.Text, () => OpenCanvasRequested?.Invoke());
        open.HorizontalAlignment = HorizontalAlignment.Left;
        var header = Ui.Columns(8, [1, 0], Ui.Overline("Stages"), open);
        var dock = new DockPanel();
        dock.Children.Add(header.Dock(System.Windows.Controls.Dock.Top));
        dock.Children.Add(state.Dock(System.Windows.Controls.Dock.Bottom));
        dock.Children.Add(Ui.Scroll(list));
        state.Margin = new Thickness(0, 8, 0, 0);
        Child = dock;
    }

    public void Update(DockerGraph graph)
    {
        list.Children.Clear();
        foreach (var stage in graph.Stages)
        {
            var incoming = graph.Edges.Where(e => e.To == stage.Index).Select(e => (e.Label == "FROM" ? "FROM " : "COPY --from=") + graph.Stages[e.From].Name).ToList();
            var body = Ui.VStack(2,
                Ui.HStack(8, Ui.Icon(Icons.Layers, 18, false, "Primary"), Ui.Text(stage.Name, 14, FontWeights.SemiBold, "OnSurface", false, false)),
                Ui.Mono(stage.Base, 12, "OnSurfaceVariant"),
                Ui.Caption($"Line {stage.Line + 1} · {stage.Instructions.Count} {Loc.T(stage.Instructions.Count == 1 ? "instruction" : "instructions")}"),
                incoming.Count == 0 ? null : Ui.Mono(string.Join("\n", incoming), 12, "Tertiary", true));
            var line = stage.Line;
            var button = new Button { Style = (Style)Application.Current.FindResource("Btn.Text"), Content = body, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 8, 10, 8), Height = double.NaN, MinHeight = 0, Margin = new Thickness(0, 0, 0, 6) };
            System.Windows.Automation.AutomationProperties.SetName(button, $"Stage {stage.Name}, line {stage.Line + 1}");
            button.Click += (_, _) => LineRequested?.Invoke(line);
            list.Children.Add(button);
        }
        state.Text = graph.Warnings.Count > 0 ? string.Join(" ", graph.Warnings) : $"{graph.Stages.Count} {Loc.T(graph.Stages.Count == 1 ? "stage" : "stages")} · {graph.Edges.Count} {Loc.T(graph.Edges.Count == 1 ? "link" : "links")}";
    }
}
