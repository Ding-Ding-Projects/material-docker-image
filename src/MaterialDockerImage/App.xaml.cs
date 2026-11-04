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
        var captureIndex = Array.IndexOf(e.Args, "--capture");
        var capture = captureIndex >= 0 && captureIndex + 1 < e.Args.Length;
        string? Arg(string name) { var i = Array.IndexOf(e.Args, name); return i >= 0 && i + 1 < e.Args.Length ? e.Args[i + 1] : null; }
        var captureLanguage = capture ? Arg("--capture-language") : null;
        var window = new MainWindow(capture, captureLanguage);
        MainWindow = window;
        if (capture)
        {
            var themeIndex = Array.IndexOf(e.Args, "--capture-theme");
            if (themeIndex >= 0 && themeIndex + 1 < e.Args.Length) window.UseCaptureTheme(e.Args[themeIndex + 1]);
            var suffix = themeIndex >= 0 && themeIndex + 1 < e.Args.Length ? "-" + e.Args[themeIndex + 1].ToLowerInvariant() : "";
            double captureWidth = 1440, captureHeight = 960;
            var widthIndex = Array.IndexOf(e.Args, "--capture-size");
            if (widthIndex >= 0 && widthIndex + 1 < e.Args.Length && e.Args[widthIndex + 1].Split('x') is [var w, var h] && double.TryParse(w, out var width) && double.TryParse(h, out var height)) { window.Width = width; window.Height = height; suffix += "-" + w + "x" + h; captureWidth = width; captureHeight = height; }
            if (captureLanguage is "Cantonese" or "Bilingual") suffix += "-" + captureLanguage.ToLowerInvariant();
            double captureScale = 1;
            if (int.TryParse(Arg("--capture-scale"), out var percent) && percent is >= 100 and <= 300 && percent != 100) { captureScale = percent / 100.0; suffix += "-s" + percent; }
            window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = 0; window.Top = 0;
            window.Show();
            window.Dispatcher.InvokeAsync(async () =>
            {
                try { var count = await window.CaptureAsync(e.Args[captureIndex + 1], suffix, captureWidth, captureHeight, captureScale); Console.WriteLine($"Captured {count} surfaces."); Shutdown(0); }
                catch (Exception exception) { Console.Error.WriteLine(exception); Shutdown(3); }
            });
            return;
        }
        DispatcherUnhandledException += (_, args) => { args.Handled = true; Services.AppHost.Current?.Notify.Error("Unexpected problem: " + args.Exception.Message); };
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
