using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Resolved definition of one bake target: key facts, build arguments and the full JSON from --print.</summary>
public sealed class BakeTargetDetail : DockPanel
{
    private readonly StackPanel facts = new();
    private readonly ReadOnlySearchText json = new("Search definition", 220, 520);
    private readonly FrameworkElement empty;
    private readonly FrameworkElement body;

    public BakeTargetDetail()
    {
        empty = Ui.EmptyState(Icons.FrameInspect, "No target selected", "Click a target card to see its resolved definition: context, Dockerfile, tags, platforms, arguments and outputs as bake resolved them.");
        empty.Margin = new Thickness(0, 32, 0, 32);
        body = Ui.VStack(12, facts, Ui.VStack(6, Ui.Overline("Resolved definition (bake --print)"), json));
        body.Visibility = Visibility.Collapsed;
        Children.Add(Ui.Scroll(new Grid { Children = { empty, body } }));
    }

    public void Show(BakeTarget? target)
    {
        facts.Children.Clear();
        empty.Visibility = target == null ? Visibility.Visible : Visibility.Collapsed;
        body.Visibility = target == null ? Visibility.Collapsed : Visibility.Visible;
        if (target == null) { json.Text = ""; return; }
        Ui.Add(facts, Ui.HStack(10, Ui.Icon(Icons.DeployedCode, 22, true, "Primary"), Ui.Mono(target.Name, 16, "OnSurface")), 0);
        void Row(string label, string value) => Ui.Add(facts, Ui.Columns(12, [-120, 1], Ui.Caption(label), value.Length > 0 ? Ui.Mono(value, 13, "OnSurface", true) : Ui.Text(Loc.T("Not set"), 13, null, "OnSurfaceVariant")), 6);
        Row("Context", target.Context);
        Row("Dockerfile", target.Dockerfile);
        Row("Stage", target.Stage);
        Row("Tags", string.Join(Environment.NewLine, target.Tags));
        Row("Platforms", string.Join(", ", target.Platforms));
        Row("Outputs", string.Join(Environment.NewLine, target.Outputs));
        if (target.Args.Count > 0)
        {
            var args = new StackPanel();
            foreach (var (name, value) in target.Args) Ui.Add(args, Ui.Columns(8, [1, 2], Ui.Mono(name, 12), Ui.Mono("= " + value, 12, "OnSurfaceVariant", true)), 2);
            Ui.Add(facts, Ui.Card(Ui.VStack(6, Ui.Overline($"{Loc.T("Build arguments")} ({target.Args.Count})"), args), 12, "SurfaceContainer", 12), 10);
        }
        json.Text = target.Json;
    }
}
