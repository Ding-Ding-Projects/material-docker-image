using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Shell;

/// <summary>
/// The startup dim sum surprise: ten times in a hundred a dish appears with its name and, when the public catalogue's picture can be had, its photo.
/// It is a small toast in the corner. It never takes focus, never blocks anything, and dismisses itself. There is deliberately no switch to turn it off.
/// </summary>
public static class DimSumToast
{
    private static bool shownThisLaunch;

    public static async void MaybeShow(AppHost host, Panel toasts, Func<bool> firstRun)
    {
        try { await ShowCoreAsync(host, toasts, firstRun); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { /* a surprise that fails stays a non-event */ }
    }

    private static SurpriseContext Context(AppHost host, bool firstRun)
    {
        var s = host.Settings;
        return new SurpriseContext(s.SetupComplete, s.SchoolMode, host.Busy, firstRun, host.Overlay.IsOpen, host.Updates.State == UpdateState.Ready,
            QuietHours.IsQuiet(s.QuietHoursEnabled, s.QuietHoursStart, s.QuietHoursEnd, DateTime.Now), host.IsCapture, shownThisLaunch);
    }

    private static async Task ShowCoreAsync(AppHost host, Panel toasts, Func<bool> firstRun)
    {
        if (!DimSumRules.MayAppear(Context(host, firstRun()))) return;
        if (!DimSumRules.Wins(Random.Shared.NextDouble())) return;
        var source = GameServices.DimSum;
        var dishes = await source.LoadAsync();
        DimSumDish? dish = dishes.Count > 0 ? DimSumRules.Pick(dishes, Random.Shared) : null;
        string? picture = dish == null ? null : await source.PictureAsync(dish);
        // Everything above may have taken a while: only appear if the app is still settled.
        if (!DimSumRules.MayAppear(Context(host, firstRun()))) return;
        shownThisLaunch = true;
        var offline = DimSumRules.OfflineNames[Random.Shared.Next(DimSumRules.OfflineNames.Count)];
        dish ??= new DimSumDish(offline.Id, offline.English, offline.Cantonese, "", "", "");
        Present(host, toasts, dish, picture);
    }

    private static void Present(AppHost host, Panel toasts, DimSumDish dish, string? picture)
    {
        var language = Loc.Language;
        var caption = DimSumRules.Caption(dish, language);
        var header = Ui.Overline(Loc.Voice(Loc.T("Dim sum surprise"), "Surprise!", "驚喜！"));
        var name = Ui.Text(caption, 16, FontWeights.SemiBold, "OnSurface", true, false);
        var text = Ui.VStack(4, header, name);
        var row = new DockPanel { LastChildFill = true };
        Border? card = null;
        var close = Ui.IconButton(Icons.Close, "Dismiss notification", () => toasts.Children.Remove(card!), "OnSurfaceVariant");
        close.Width = close.Height = close.MinHeight = 32; close.FontSize = 16;
        row.Children.Add(close.Dock(Dock.Right));
        if (picture != null)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 160; bitmap.UriSource = new Uri(picture); bitmap.EndInit(); bitmap.Freeze();
                var image = new System.Windows.Controls.Image { Source = bitmap, Width = 72, Height = 72, Stretch = System.Windows.Media.Stretch.UniformToFill, Margin = new Thickness(0, 0, 12, 0) };
                AutomationProperties.SetName(image, DimSumRules.Alt(dish, language));
                row.Children.Add(image.Dock(Dock.Left));
            }
            catch (Exception e) when (e is IOException or NotSupportedException or InvalidOperationException) { /* no picture is better than a broken one */ }
        }
        else text.Children.Add(Ui.Caption("No picture is available right now."));
        row.Children.Add(text);
        card = Ui.Card(row, 12, "SurfaceHighest", 12);
        card.Margin = new Thickness(0, 8, 0, 0); card.Width = 400; card.Focusable = false;
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = .35 };
        AutomationProperties.SetName(card, Loc.T("Dim sum surprise") + ": " + caption);
        AutomationProperties.SetLiveSetting(card, AutomationLiveSetting.Polite);
        toasts.Children.Add(card);
        Motion.FadeIn(card);
        System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(card)?.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(DimSumRules.ToastSeconds) };
        timer.Tick += (_, _) => { timer.Stop(); if (!card.IsKeyboardFocusWithin && !card.IsMouseOver) toasts.Children.Remove(card); else timer.Start(); };
        timer.Start();
    }
}
