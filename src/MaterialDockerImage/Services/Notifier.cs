using System.Collections.ObjectModel;
using System.Media;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Services;

public enum NoticeKind { Info, Success, Warning, Error, Progress }

public sealed record Notice(DateTimeOffset When, NoticeKind Kind, string Message)
{
    public string WhenText => When.ToLocalTime().ToString("HH:mm:ss");
    public string KindText => Kind.ToString();
}

/// <summary>Non-blocking toasts in the bottom corner plus a reviewable notification centre. Modal dialogs are only for decisions.</summary>
public sealed class Notifier
{
    private readonly StackPanel host;
    private readonly Dispatcher dispatcher;
    public ObservableCollection<Notice> Centre { get; } = new();
    public int Unread { get; private set; }
    public event Action? UnreadChanged;
    public Func<bool> SoundsEnabled { get; set; } = () => true;
    public Action<string>? Narrate { get; set; }

    public Notifier(StackPanel toastHost)
    {
        host = toastHost;
        dispatcher = toastHost.Dispatcher;
    }

    public void Info(string message) => Show(NoticeKind.Info, message);
    public void Success(string message) => Show(NoticeKind.Success, message);
    public void Warning(string message) => Show(NoticeKind.Warning, message);
    public void Error(string message) => Show(NoticeKind.Error, message);

    public void Show(NoticeKind kind, string message, string? actionLabel = null, Action? action = null)
    {
        if (!dispatcher.CheckAccess()) { dispatcher.Invoke(() => Show(kind, message, actionLabel, action)); return; }
        var notice = new Notice(DateTimeOffset.Now, kind, message);
        Centre.Insert(0, notice);
        while (Centre.Count > 500) Centre.RemoveAt(Centre.Count - 1);
        Unread++; UnreadChanged?.Invoke();
        PlayCue(kind);
        Narrate?.Invoke(message);
        var (icon, tone) = kind switch
        {
            NoticeKind.Success => (Icons.CheckCircle, Tone.Success),
            NoticeKind.Warning => (Icons.Warning, Tone.Warning),
            NoticeKind.Error => (Icons.Error, Tone.Error),
            NoticeKind.Progress => (Icons.Pending, Tone.Primary),
            _ => (Icons.Info, Tone.Neutral)
        };
        var keys = Ui.ToneKeys(tone);
        var row = new DockPanel { LastChildFill = true };
        row.Children.Add(Ui.Icon(icon, 20, true, kind == NoticeKind.Info ? "Primary" : keys.Foreground).Margin(0, 0, 12, 0).Dock(Dock.Left));
        Border? card = null;
        var close = Ui.IconButton(Icons.Close, "Dismiss notification", () => Remove(card!), "OnSurfaceVariant");
        close.Width = close.Height = close.MinHeight = 32; close.FontSize = 16;
        row.Children.Add(close.Dock(Dock.Right));
        if (actionLabel != null && action != null) row.Children.Add(Ui.Button(actionLabel, null, ButtonKind.Text, () => { action(); Remove(card!); }).Dock(Dock.Right));
        row.Children.Add(Ui.Text(message, 14, null, "OnSurface", true, false));
        card = Ui.Card(row, 12, "SurfaceHighest", 12);
        card.Margin = new Thickness(0, 8, 0, 0);
        card.Width = 400;
        card.Focusable = true;
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = .35 };
        AutomationProperties.SetName(card, kind + ": " + message);
        AutomationProperties.SetLiveSetting(card, kind is NoticeKind.Error or NoticeKind.Warning ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);
        card.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Remove(card); };
        host.Children.Add(card);
        while (host.Children.Count > 4) host.Children.RemoveAt(0);
        UIElementAutomationPeer.CreatePeerForElement(card)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        if (kind is NoticeKind.Info or NoticeKind.Success or NoticeKind.Progress)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            timer.Tick += (_, _) => { timer.Stop(); if (!card.IsKeyboardFocusWithin && !card.IsMouseOver) Remove(card); else timer.Start(); };
            timer.Start();
        }
    }

    private void Remove(Border card) => host.Children.Remove(card);
    public void MarkRead() { Unread = 0; UnreadChanged?.Invoke(); }
    public void ClearToasts() => host.Children.Clear();

    private void PlayCue(NoticeKind kind)
    {
        if (!SoundsEnabled()) return;
        try
        {
            switch (kind)
            {
                case NoticeKind.Error: SystemSounds.Hand.Play(); break;
                case NoticeKind.Warning: SystemSounds.Exclamation.Play(); break;
                case NoticeKind.Success: SystemSounds.Asterisk.Play(); break;
            }
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
}
