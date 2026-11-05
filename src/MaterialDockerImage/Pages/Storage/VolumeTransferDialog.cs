using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>One tab of the volume transfer dialog: its body, its primary action label and the work the action performs.</summary>
internal sealed class TransferPanel
{
    public required FrameworkElement Body { get; init; }
    public required string ActionLabel { get; init; }
    /// <summary>Runs the work. The callback shows a progress line (null hides it). Returns a problem text, or null on success.</summary>
    public required Func<Action<string?>, Task<string?>> Apply { get; init; }
}

/// <summary>
/// "Back up volume" dialog with in-dialog Backup and Restore tabs, a progress bar while the helper container runs and one
/// shared footer. Backup and Restore keep their own validation, confirmations and commands.
/// </summary>
internal static class VolumeTransferDialog
{
    private static AppHost Host => AppHost.Current;

    public static void Show(IReadOnlyList<VolumeRow> volumes, string? selected, bool restoreTab, Func<string?> context, Func<bool> isCurrent, Func<Task> reload)
    {
        var names = volumes.Select(v => v.Name).ToList();
        var panels = new Dictionary<string, TransferPanel>(StringComparer.Ordinal)
        {
            ["Backup"] = VolumeBackupDialog.BuildPanel(volumes, selected, context, isCurrent),
            ["Restore"] = VolumeRestoreDialog.BuildPanel(selected, names, context, isCurrent, reload),
        };
        var tab = restoreTab ? "Restore" : "Backup";
        Action? close = null;
        var bodyHost = new ContentControl();
        var error = Ui.Text("", 13, null, "Error", true, false);
        var statusText = Ui.Text("", 13, null, "OnSurface", true, false);
        var bar = new ProgressBar { IsIndeterminate = true, Height = 4, Minimum = 0, Maximum = 100 };
        var progress = Ui.VStack(6, statusText, bar);
        progress.Visibility = Visibility.Collapsed;
        Border seg = null!;
        var buttons = new Dictionary<string, Button>(StringComparer.Ordinal);
        Button cancel = null!;

        void SetBusy(bool busy)
        {
            seg.IsEnabled = !busy;
            cancel.IsEnabled = !busy;
        }
        void Status(string? text)
        {
            progress.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
            if (text != null) statusText.Text = text;
        }
        foreach (var (name, panel) in panels)
        {
            var key = name;
            var button = Ui.AsyncButton(panel.ActionLabel, key == "Backup" ? Icons.CloudUpload : Icons.Restore, ButtonKind.Filled, async () =>
            {
                error.Text = "";
                SetBusy(true);
                string? problem;
                try { problem = await panel.Apply(Status); }
                finally { Status(null); SetBusy(false); }
                if (problem == null) close!(); else error.Text = problem;
            });
            buttons[key] = button;
        }
        cancel = Ui.Button("Cancel", null, ButtonKind.Text, () => close!());
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        cancel.Margin = new Thickness(0, 0, 8, 0);
        footer.Children.Add(cancel);
        foreach (var b in buttons.Values) footer.Children.Add(b);

        void Swap()
        {
            error.Text = "";
            bodyHost.Content = panels[tab].Body;
            foreach (var (k, b) in buttons) b.Visibility = k == tab ? Visibility.Visible : Visibility.Collapsed;
        }
        seg = Ui.Segmented(["Backup", "Restore"], tab, t => { tab = t; Swap(); });
        Swap();

        var tile = Ui.Card(Ui.Icon(Icons.CloudUpload, 22, true, "OnPrimaryContainer"), 10, "PrimaryContainer", 12);
        var head = Ui.Columns(12, [0, 1, 0], tile, Ui.Title("Back up volume", 20), Ui.IconButton(Icons.Close, "Close", () => close!()));
        head.Children[1].SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        var body = Ui.VStack(14, head, seg, bodyHost, progress, error, footer);
        close = Host.Overlay.Show(body, 660);
    }
}
