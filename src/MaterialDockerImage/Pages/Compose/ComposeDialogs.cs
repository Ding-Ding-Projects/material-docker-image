using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

public sealed record ComposeUpOptions(bool Build, bool RemoveOrphans, string PullPolicy, bool ForceRecreate, bool DryRun = false);
public sealed record ComposeDownOptions(bool Volumes, bool Orphans, bool DryRun);
public sealed record ComposeLogOptions(int Tail, bool Follow, bool Timestamps);

/// <summary>Option dialogs for Compose actions. They only collect choices; the page runs the commands.</summary>
public static class ComposeDialogs
{
    private static AppHost Host => AppHost.Current;

    private static Grid Actions(Action cancel, string label, string icon, ButtonKind kind, Action ok) =>
        Ui.Columns(8, [1, 0, 0], new Border(), Ui.Button("Cancel", null, ButtonKind.Text, cancel), Ui.Button(label, icon, kind, ok));

    public static Task<ComposeUpOptions?> UpAsync(string target, string? service)
    {
        var done = new TaskCompletionSource<ComposeUpOptions?>();
        Action? close = null;
        var build = Ui.Switch("Build images before starting", false, _ => { }, "--build: rebuild services that have a build section");
        var orphans = Ui.Switch("Remove orphans", false, _ => { }, "--remove-orphans: remove containers of services no longer in the file");
        var recreate = Ui.Switch("Force recreate", false, _ => { }, "--force-recreate: recreate containers even when nothing changed");
        var pull = Ui.Combo(["Default (missing)", "always", "missing", "never"], "Default (missing)");
        var dryRun = Ui.Switch("Dry run", false, _ => { }, "--dry-run: Compose reports what it would do without changing containers, networks or volumes");
        var body = Ui.VStack(14,
            Ui.Title(service == null ? "Up" : "Up " + service, 20),
            Ui.Caption("Starts in the background (--detach). " + target),
            build, orphans, recreate, dryRun,
            Ui.Field("Pull policy", pull, "When to pull images before starting. always pulls every time, missing pulls only absent images, never uses local images only. Default leaves the choice to Compose (missing).", "--pull always"),
            Actions(() => { done.TrySetResult(null); close!(); }, "Up", Icons.PlayArrow, ButtonKind.Filled, () =>
            {
                var policy = pull.SelectedIndex <= 0 ? "" : (string)pull.SelectedItem;
                done.TrySetResult(new ComposeUpOptions(build.IsChecked == true, orphans.IsChecked == true, policy, recreate.IsChecked == true, dryRun.IsChecked == true)); close!();
            }));
        close = Host.Overlay.Show(body, 540, () => done.TrySetResult(null));
        return done.Task;
    }

    /// <summary>Returns the choices or null when canceled. Volume removal needs a separate super confirmation by the caller unless it is a dry run.</summary>
    public static Task<ComposeDownOptions?> DownAsync(string project, string target)
    {
        var done = new TaskCompletionSource<ComposeDownOptions?>();
        Action? close = null;
        var orphans = Ui.Switch("Remove orphans", false, _ => { }, "Also remove containers of services no longer in the file");
        var volumes = Ui.Switch("Also remove volumes", false, _ => { }, "Deletes named volumes declared in the file and anonymous volumes. Needs a second, two-key confirmation.");
        volumes.SetResourceReference(Control.ForegroundProperty, "Error");
        var dryRun = Ui.Switch("Dry run", false, _ => { }, "--dry-run: Compose reports what it would stop and remove without changing anything");
        var body = Ui.VStack(14,
            Ui.Title("Down " + project, 20),
            Ui.Text("Stops and removes the project's containers and networks. Images are kept. Volumes are kept unless you choose to remove them.", 14, null, "OnSurfaceVariant", true, false),
            Ui.Caption(target),
            orphans, volumes, dryRun,
            Actions(() => { done.TrySetResult(null); close!(); }, "Down", Icons.Stop, ButtonKind.Danger, () => { done.TrySetResult(new ComposeDownOptions(volumes.IsChecked == true, orphans.IsChecked == true, dryRun.IsChecked == true)); close!(); }));
        close = Host.Overlay.Show(body, 540, () => done.TrySetResult(null));
        return done.Task;
    }

    public static Task<ComposeLogOptions?> LogsAsync(string scope)
    {
        var done = new TaskCompletionSource<ComposeLogOptions?>();
        Action? close = null;
        var tail = Ui.Combo(["100", "500", "1000", "5000", "all"], "500");
        var follow = Ui.Switch("Follow", true, _ => { }, "Keep streaming new lines until you stop or close the viewer");
        var timestamps = Ui.Switch("Timestamps", false, _ => { }, "Prefix each line with the engine's timestamp");
        var body = Ui.VStack(14,
            Ui.Title("Logs · " + scope, 20),
            Ui.Field("Lines from the end", tail, "How many existing lines to show before following. all reads the whole log, which can be large.", "--tail 500"),
            follow, timestamps,
            Actions(() => { done.TrySetResult(null); close!(); }, "Show logs", Icons.Terminal, ButtonKind.Filled, () =>
            {
                var t = tail.SelectedItem as string == "all" ? -1 : int.Parse((string)tail.SelectedItem);
                done.TrySetResult(new ComposeLogOptions(t, follow.IsChecked == true, timestamps.IsChecked == true)); close!();
            }));
        close = Host.Overlay.Show(body, 480, () => done.TrySetResult(null));
        return done.Task;
    }
}
