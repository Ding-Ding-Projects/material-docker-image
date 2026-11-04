using System.Windows;
using System.Windows.Input;
using MaterialDockerImage.Services;

namespace MaterialDockerImage.Shell;

/// <summary>
/// Pick mode for the element editor: the next click on the window chooses the element to customise instead of activating it.
/// Escape cancels. The click is swallowed, so picking a button never presses it.
/// </summary>
public static class AppearancePicker
{
    private static Window? window;
    private static Cursor? previousCursor;

    public static bool Active => window != null;

    public static void Start()
    {
        if (window != null) { Stop(); return; }
        var main = Application.Current?.MainWindow;
        if (main == null || AppHost.Current is not { } host) return;
        ElementEditor.Close();
        window = main;
        previousCursor = main.Cursor;
        main.Cursor = Cursors.Cross;
        main.PreviewMouseLeftButtonDown += Down;
        main.PreviewMouseLeftButtonUp += Swallow;
        main.PreviewKeyDown += OnKey;
        host.Notify.Info("Click the element to customize. Press Escape to cancel.");
    }

    public static void Stop()
    {
        if (window == null) return;
        window.PreviewMouseLeftButtonDown -= Down;
        window.PreviewMouseLeftButtonUp -= Swallow;
        window.PreviewKeyDown -= OnKey;
        window.Cursor = previousCursor;
        window = null;
    }

    private static void Swallow(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private static void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Stop();
        e.Handled = true;
    }

    private static void Down(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var hit = ElementAppearance.Climb(e.OriginalSource as DependencyObject);
        Stop();
        if (hit == null) { AppHost.Current?.Notify.Info("That spot has no element that can be customized."); return; }
        ElementEditor.Show(hit);
    }
}
