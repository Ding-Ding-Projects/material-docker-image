using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Base for the Swarm tabs: the snapshot binding (context and staleness check), a reload hook and shared helpers.
/// Every list is bound to the engine context it was loaded from.
/// </summary>
public abstract class SwarmTabView : Grid
{
    protected static AppHost Host => AppHost.Current;
    protected readonly Func<string?> Context;
    protected readonly Func<bool> IsCurrent;
    /// <summary>Reloads this tab after an action changed engine state.</summary>
    public Func<Task>? Reload { get; set; }
    /// <summary>Short summary for the page subtitle after each load.</summary>
    public event Action<string>? Summary;

    protected SwarmTabView(Func<string?> context, Func<bool> isCurrent) { Context = context; IsCurrent = isCurrent; }

    public abstract Task LoadAsync();

    protected void SetSummary(string text) => Summary?.Invoke(text);

    protected bool Stale()
    {
        if (IsCurrent()) return false;
        Host.Notify.Error(PageKit.StaleMessage(Context()));
        return true;
    }

    protected async Task AfterChange()
    {
        if (Reload != null) await Reload(); else await LoadAsync();
    }

    protected Task<CommandResult?> Run(string label, IReadOnlyList<string> args, string? standardInput = null) =>
        Host.Execute(label, args, boundContext: Context(), standardInput: standardInput);

    protected async Task InspectAsync(string kind, string id, string title)
    {
        var r = await Host.Read([kind, "inspect", id], Context());
        if (!r.Success) { Host.Notify.Error($"Inspect {title} failed: {r.StandardError.Trim()}"); return; }
        ResourceTextViewer.Show(title, PageKit.FormatInspect(r.StandardOutput, true) ?? r.StandardOutput, $"docker {kind} inspect · environment values masked");
    }

    protected static async Task Guard(Func<Task> action)
    {
        try { await action(); } catch (Exception e) when (e is not OutOfMemoryException) { Host.Notify.Error(e.Message); }
    }

    public static Tone ToneOf(SwarmHealth health) => health switch
    {
        SwarmHealth.Healthy => Tone.Success,
        SwarmHealth.Partial => Tone.Warning,
        SwarmHealth.Down => Tone.Error,
        _ => Tone.Neutral,
    };

    /// <summary>A toolbar row above a table.</summary>
    protected void LayoutWith(UIElement toolbar, UIElement content)
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        if (toolbar is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 0, 12);
        Children.Add(toolbar);
        Children.Add(content.At(1));
    }

    /// <summary>A single content area with no toolbar row (the page header carries the primary actions).</summary>
    protected void LayoutContent(UIElement content) => Children.Add(content);
}
