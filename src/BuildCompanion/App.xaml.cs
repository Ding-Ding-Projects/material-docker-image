using System;
using System.Windows;

namespace BuildCompanion;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow(e.Args, AppContext.BaseDirectory);
        MainWindow = window;
        window.Show();
    }
}
