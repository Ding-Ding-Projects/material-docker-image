using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
namespace MaterialDockerImage;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--software-rendering", StringComparer.Ordinal))
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        var lifecycle = e.Args.FirstOrDefault(x => x.StartsWith("--squirrel-", StringComparison.Ordinal));
        if (lifecycle is "--squirrel-install" or "--squirrel-updated" or "--squirrel-uninstall" or "--squirrel-obsolete")
        {
            // Squirrel invokes lifecycle hooks during maintenance. Never open a workspace here.
            if (lifecycle != "--squirrel-obsolete")
            {
                var updater = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Update.exe"));
                if (File.Exists(updater))
                {
                    var start = new ProcessStartInfo(updater) { UseShellExecute = false, CreateNoWindow = true };
                    start.ArgumentList.Add(lifecycle == "--squirrel-uninstall" ? "--removeShortcut" : "--createShortcut");
                    start.ArgumentList.Add("MaterialDockerImage.exe");
                    start.ArgumentList.Add("--shortcut-locations=StartMenu");
                    using var process = Process.Start(start);
                    if (process != null && !process.WaitForExit(15000)) { Shutdown(1); return; }
                    if (process != null && process.ExitCode != 0) { Shutdown(process.ExitCode); return; }
                }
            }
            Shutdown(0);
            return;
        }
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        var openIndex = Array.IndexOf(e.Args, "--open-dockerfile");
        if (openIndex >= 0 && openIndex + 1 < e.Args.Length)
        {
            try { window.OpenDockerfile(e.Args[openIndex + 1]); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            { MessageBox.Show(window, "The requested Dockerfile could not be opened. " + exception.Message, "Open Dockerfile", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
    }
}
