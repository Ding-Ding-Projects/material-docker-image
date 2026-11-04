using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Live preview of the exact buildx argument list. Updates shortly after the last edit; invalid input shows the
/// validation message instead of a command. Copy writes the shown command to the clipboard.
/// </summary>
public sealed class BuildCommandPreview : StackPanel
{
    private readonly Func<IReadOnlyList<string>> create;
    private readonly TextBox box = Ui.CodeBox("", 72);
    private readonly TextBlock problem = Ui.Text("", 12, null, "Error");
    private readonly TextBlock count = Ui.Caption("");
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public BuildCommandPreview(Func<IReadOnlyList<string>> create)
    {
        this.create = create;
        box.IsReadOnly = true;
        box.TextWrapping = TextWrapping.Wrap;
        box.MaxHeight = 220;
        System.Windows.Automation.AutomationProperties.SetName(box, Loc.T("Command preview"));
        var copy = Ui.IconButton(Icons.ContentCopy, "Copy command", () =>
        {
            if (box.Text.Length == 0) return;
            ActionSounds.Copy(box.Text);
            AppHost.Current.Notify.Info("Copied the build command.");
        });
        var head = new DockPanel();
        head.Children.Add(copy.Dock(System.Windows.Controls.Dock.Right));
        head.Children.Add(count.Dock(System.Windows.Controls.Dock.Right).Margin(0, 0, 8, 0));
        var label = Ui.Wrap(0, Ui.Overline("Command preview"), Ui.InfoButton("Command preview",
            "The exact arguments the Build button passes to docker, in order. Paths that are not chosen yet appear as placeholders, the metadata file is a new temporary file for each build, and confirmations (push, overwrite, cache export) still happen when you build. Secret contents and environment values are never included.",
            "docker buildx build --progress plain --tag hello-api:latest --load C:\\Projects\\hello-api"));
        head.Children.Add(label);
        problem.Visibility = Visibility.Collapsed;
        Children.Add(head);
        Children.Add(box.Margin(0, 6, 0, 0));
        Children.Add(problem.Margin(0, 4, 0, 0));
        timer.Tick += (_, _) => { timer.Stop(); Render(); };
    }

    /// <summary>Schedules a refresh (debounced).</summary>
    public void Invalidate() { timer.Stop(); timer.Start(); }

    public void Render()
    {
        try
        {
            var args = create();
            box.Text = BuildKitCommand.Format(args);
            count.Text = $"{args.Count} {Loc.T("arguments")}";
            problem.Visibility = Visibility.Collapsed;
            box.Opacity = 1;
        }
        catch (Exception e) when (e is ArgumentException or IOException or NotSupportedException or InvalidOperationException)
        {
            problem.Text = e.Message;
            problem.Visibility = Visibility.Visible;
            box.Opacity = .5;
        }
    }
}
