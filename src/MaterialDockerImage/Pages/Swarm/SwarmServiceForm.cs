using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Create-service form: image, name, mode and replicas, published ports, environment, placement constraints and a
/// command preview. Environment values are masked in the preview and never written to output or local history.
/// </summary>
public static class SwarmServiceForm
{
    public static void Show(Func<string?> context, Func<bool> isCurrent, Func<Task> reload)
    {
        var host = AppHost.Current;
        var image = Ui.Input("", "Image", true);
        var name = Ui.Input("", "Name", true);
        var global = false;
        var replicas = Ui.Input("1", "Replicas", true);
        var mode = Ui.Segmented(["Replicated", "Global"], "Replicated", v => { global = v == "Global"; replicas.IsEnabled = !global; });
        var ports = new SwarmTextRows("Port mapping", "Add port");
        var environment = new KeyValueRows("Variable", "Value");
        var constraints = new SwarmTextRows("Constraint", "Add constraint");
        var preview = Ui.Mono("", 12, "OnSurfaceVariant", true);
        List<string> Args()
        {
            if (PageKit.ValidateReference(image.Text.Trim()) is { } problem) throw new ArgumentException(problem);
            var count = 1;
            if (!global && !int.TryParse(replicas.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out count)) throw new ArgumentException("Replicas must be a whole number.");
            return SwarmRules.CreateServiceArgs(image.Text.Trim(), name.Text, global, count, ports.Values, environment.Values(), constraints.Values);
        }
        void Update() { try { preview.Text = PageKit.Preview(Args(), context()); } catch (ArgumentException e) { preview.Text = e.Message; } }
        image.TextChanged += (_, _) => Update(); name.TextChanged += (_, _) => Update(); replicas.TextChanged += (_, _) => Update();
        ports.Changed += Update; environment.Changed += Update; constraints.Changed += Update;
        mode.AddHandler(System.Windows.Controls.Primitives.ToggleButton.CheckedEvent, new RoutedEventHandler((_, _) => Update()));
        var body = Ui.VStack(14,
            Ui.Columns(12, [3, 2],
                Ui.Field("Image", image, "The image every task runs. Nodes pull it themselves, so use a registry reference all nodes can reach.", "nginx:1.27"),
                Ui.Field("Name", name, "Optional. Without a name the swarm generates one.", "web")),
            Ui.Columns(12, [3, 2],
                Ui.Field("Mode", mode, "Replicated runs the number of tasks you choose. Global runs exactly one task on every eligible node.", "--mode global"),
                Ui.Field("Replicas", replicas, "How many tasks a replicated service runs. 0 creates the service without starting tasks.", "3")),
            Ui.Field("Published ports", ports, "published:target with an optional /udp. The routing mesh publishes the port on every node.", "8080:80"),
            Ui.Field("Environment", environment, "Set as --env NAME=value. Values are masked in the preview and never recorded in output or local history. Use a secret for passwords.", "LOG_LEVEL=info"),
            Ui.Field("Placement constraints", constraints, "Only nodes matching every constraint run tasks.", "node.role==worker · node.labels.zone==a"),
            Ui.Card(Ui.VStack(4, Ui.Overline("Command"), preview), 12, "SurfaceHigh", 12));
        PageKit.Dialog("Create service", "docker service create · waits for the tasks to converge", body, "Create", async () =>
        {
            if (!isCurrent()) return PageKit.StaleMessage(context());
            List<string> args;
            try { args = Args(); } catch (ArgumentException e) { return e.Message; }
            var label = Loc.T("Create service") + " " + (name.Text.Trim().Length > 0 ? name.Text.Trim() : "from " + image.Text.Trim());
            var result = await host.Execute(label, args, boundContext: context());
            if (result?.Success != true) return PageKit.Failure(result);
            await reload();
            return null;
        }, width: 680);
        ports.AddRow("");
        Update();
    }
}
