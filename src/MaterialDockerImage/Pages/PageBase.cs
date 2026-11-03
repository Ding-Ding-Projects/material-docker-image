using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

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
        if (loadedOnce) return;
        loadedOnce = true;
        await SafeRefresh();
    }

    public virtual void OnShown() { }

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
        Children.Add(header);
        Grid.SetRow(body, 2);
        Children.Add(body);
    }

    protected static string Ago(string value) => value;
}

public sealed record Destination(string Key, string Icon, string Description, Func<PageBase> Create, bool OnRail = true);
