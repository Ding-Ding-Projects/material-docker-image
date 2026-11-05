using System;
using System.Windows;

namespace BuildCompanion;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Release check: write the commit of the source packed into this executable to a file and exit without a window.
        var probe = Array.IndexOf(e.Args, "--embedded-source-commit");
        if (probe >= 0)
        {
            var code = 2;
            try
            {
                if (probe + 1 < e.Args.Length && System.IO.Path.IsPathRooted(e.Args[probe + 1]) && EmbeddedSource.PackedCommit() is { } commit)
                {
                    System.IO.File.WriteAllText(e.Args[probe + 1], commit);
                    code = 0;
                }
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.IO.InvalidDataException or System.Text.Json.JsonException) { }
            Shutdown(code);
            return;
        }
        // Matches the main application: software rendering lets window capture tools read the frame.
        if (Array.IndexOf(e.Args, "--software-rendering") >= 0) System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var window = new MainWindow(e.Args, AppContext.BaseDirectory);
        MainWindow = window;
        window.Show();
    }
}
