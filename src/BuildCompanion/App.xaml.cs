using System;
using System.Windows;

namespace BuildCompanion;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Matches the main application: software rendering lets window capture tools read the frame.
        if (Array.IndexOf(e.Args, "--software-rendering") >= 0) System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var window = new MainWindow(e.Args, AppContext.BaseDirectory);
        MainWindow = window;
        window.Show();
    }
}
