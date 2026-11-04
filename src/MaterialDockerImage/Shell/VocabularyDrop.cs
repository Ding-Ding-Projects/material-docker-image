using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Shell;

/// <summary>
/// Accepts a personal wording JSON file dropped anywhere on the window. The file is validated in full with the same
/// rules as the Settings upload; a valid file is applied and saved at once and celebrated, an invalid one changes nothing.
/// The Settings upload remains the keyboard-accessible route.
/// </summary>
public sealed class VocabularyDrop
{
    private readonly Window window;
    private readonly Grid root;
    private readonly AppHost host;
    private Border? celebration;

    public VocabularyDrop(Window window, Grid root, AppHost host)
    {
        this.window = window; this.root = root; this.host = host;
        window.AllowDrop = true;
        window.PreviewDragOver += OnDragOver;
        window.PreviewDrop += OnDrop;
    }

    private static string? SinglePath(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) ? VocabularyDropRules.SinglePath(data.GetData(DataFormats.FileDrop)) : null;

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (SinglePath(e.Data) == null) return;
        // Text boxes accept file drops as text; this window-level handler claims file drops for personal wording.
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (SinglePath(e.Data) is not { } path) return;
        e.Handled = true;
        Apply(path);
    }

    /// <summary>Validates and applies one file. Returns true only when the wording was applied and saved.</summary>
    public bool Apply(string path)
    {
        if (!VocabularyDropRules.IsJson(path))
        {
            Refuse(Loc.T("Only a personal wording .json file can be dropped here. Nothing was changed."));
            return false;
        }
        var result = Loc.LoadVocabulary(path);
        if (!result.Success)
        {
            Refuse(Loc.T("The dropped file was refused and nothing was changed: ") + result.Message);
            return false;
        }
        host.Settings.PersonalVocabularyPath = Path.GetFullPath(path);
        host.SaveSettings("Personal wording file dropped");
        PlaySound(tada: true);
        Celebrate(VocabularyDropRules.Headline());
        host.Notify.Info(Loc.T("Personal wording applied from the dropped file. Clear it in Settings › Language to restore the original wording."));
        return true;
    }

    private void Refuse(string message)
    {
        PlaySound(tada: false);
        host.Notify.Error(message);
    }

    private static void PlaySound(bool tada) => ActionSounds.Play(tada ? SoundCue.Drop : SoundCue.DropRefused);

    /// <summary>A full-window, non-blocking headline: animates in, holds about three seconds, animates out.</summary>
    private void Celebrate(string headline)
    {
        if (celebration != null) root.Children.Remove(celebration);
        var text = new TextBlock { Text = headline, FontSize = 44, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 720 };
        text.SetResourceReference(TextBlock.ForegroundProperty, "OnPrimaryContainer");
        var card = new Border { Child = text, CornerRadius = new CornerRadius(28), Padding = new Thickness(40, 28, 40, 28), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        card.SetResourceReference(Border.BackgroundProperty, "PrimaryContainer");
        var layer = new Border { Child = card, IsHitTestVisible = false, Focusable = false, Background = new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)) };
        Grid.SetRowSpan(layer, 2); Panel.SetZIndex(layer, 40);
        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(text, headline);
        celebration = layer;
        root.Children.Add(layer);
        var animate = Motion.Enabled;
        var inTime = animate ? Motion.Duration(AppearanceMotion.Medium4) : TimeSpan.FromMilliseconds(150);
        layer.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, inTime));
        if (animate)
        {
            var scale = new ScaleTransform(0.6, 0.6);
            card.RenderTransform = scale; card.RenderTransformOrigin = new Point(0.5, 0.5);
            var grow = new DoubleAnimation(0.6, 1, inTime) { EasingFunction = Motion.EmphasizedDecelerate };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }
        window.Dispatcher.InvokeAsync(() => UIElementAutomationPeer.CreatePeerForElement(text)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged), DispatcherPriority.Background);
        var timer = new DispatcherTimer { Interval = inTime + VocabularyDropRules.Hold };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var outTime = animate ? Motion.Duration(AppearanceMotion.Short4) : TimeSpan.FromMilliseconds(150);
            var fade = new DoubleAnimation(1, 0, outTime);
            fade.Completed += (_, _) => { root.Children.Remove(layer); if (celebration == layer) celebration = null; };
            layer.BeginAnimation(UIElement.OpacityProperty, fade);
        };
        timer.Start();
    }
}
