using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;
using System.Windows.Media;

namespace MaterialDockerImage.Pages;

/// <summary>Base for every destination. Pages build their own UI in code from the shared Ui helpers.</summary>
public abstract class PageBase : Grid
{
    protected static AppHost Host => AppHost.Current;
    /// <summary>Destination key used for navigation, tabs and the palette (for example "Images").</summary>
    public abstract string Key { get; }
    private bool loadedOnce;
    /// <summary>Snapshot binding for engine-scoped pages. Actions must check <see cref="IsCurrent"/> before acting.</summary>
    protected string? SnapshotContext { get; private set; }
    protected long SnapshotGeneration { get; private set; }
    protected bool IsCurrent => Host.IsCurrent(SnapshotContext, SnapshotGeneration);

    protected PageBase()
    {
        SnapshotContext = Host.Context; SnapshotGeneration = Host.Generation;
        Host.ContextChanged += () => { SnapshotContext = Host.Context; SnapshotGeneration = Host.Generation; if (IsVisible) _ = SafeRefresh(); else loadedOnce = false; };
    }

    /// <summary>Loads real data. Called on first show, on engine context change and from Refresh buttons.</summary>
    public virtual Task RefreshAsync() => Task.CompletedTask;

    public async Task SafeRefresh()
    {
        try { await RefreshAsync(); }
        catch (Exception e) when (e is not OutOfMemoryException) { Host.Notify.Error($"{Loc.T(Key)}: {e.Message}"); }
    }

    public async Task ShowAsync()
    {
        OnShown();
        Shell.LockUi.AttachPage(this, Key);
        if (loadedOnce) return;
        loadedOnce = true;
        await SafeRefresh();
    }

    public virtual void OnShown() { }

    /// <summary>True while a refresh, or one queued behind it, is still loading. Capture mode waits for it to settle.</summary>
    public virtual bool Busy => false;

    /// <summary>Commands this page adds to the command palette.</summary>
    public virtual IEnumerable<AppCommand> PageCommands() => [];

    /// <summary>Optional element the palette teleports to for a settings or command id.</summary>
    public virtual FrameworkElement? FindTarget(string id) => null;

    /// <summary>Standard page layout: a header row and a body that fills the rest.</summary>
    protected void Layout(UIElement header, UIElement body, double gap = 16)
    {
        Children.Clear(); RowDefinitions.Clear();
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(gap) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Children.Add(WithHero(header));
        Grid.SetRow(body, 2);
        Children.Add(body);
    }

    protected static string Ago(string value) => value;

    /// <summary>Adds the destination's icon in a tonal tile beside the page header, so every page opens with the same identity.</summary>
    private UIElement WithHero(UIElement header)
    {
        if (Destinations.Find(Key) is not { } destination) return header;
        var glyph = Ui.Icon(destination.Icon, 24, true, "OnPrimaryContainer");
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        var tile = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(14), Child = glyph, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 16, 0) };
        tile.SetResourceReference(Border.BackgroundProperty, "PrimaryContainer");
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(tile, Dock.Left);
        row.Children.Add(tile);
        row.Children.Add(header);
        return row;
    }
}

public sealed record Destination(string Key, string Icon, string Description, Func<PageBase> Create, bool OnRail = true);
