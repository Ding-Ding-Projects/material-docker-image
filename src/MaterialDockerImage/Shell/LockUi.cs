using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Shell;

/// <summary>
/// The shared helper that locks any element: settings rows and major page action buttons use the same context menu,
/// the same policy wizard and the same unlock ladder. A locked element is disabled and stays that way even if its page re-enables it.
/// </summary>
public static class LockUi
{
    private static AppHost Host => AppHost.Current;
    private static readonly List<(string Id, WeakReference<FrameworkElement> Element)> Attached = new();
    private static readonly DependencyProperty ApplyProperty = DependencyProperty.RegisterAttached("LockApply", typeof(Action), typeof(LockUi));
    private static readonly DependencyProperty LockedBeforeProperty = DependencyProperty.RegisterAttached("LockedId", typeof(string), typeof(LockUi));

    public static bool IsLocked(string id) => Host.IsLocked(id);

    /// <summary>Registers an element's own apply action so <see cref="Refresh"/> reaches it when the lock with this id changes.</summary>
    public static void Subscribe(string id, FrameworkElement element, Action apply)
    {
        element.SetValue(ApplyProperty, apply);
        Attached.Add((id, new WeakReference<FrameworkElement>(element)));
    }

    /// <summary>Re-applies the locked or unlocked look to every attached element with this id.</summary>
    public static void Refresh(string id)
    {
        Attached.RemoveAll(a => !a.Element.TryGetTarget(out _));
        foreach (var (attachedId, reference) in Attached.ToList())
            if (attachedId == id && reference.TryGetTarget(out var element) && element.GetValue(ApplyProperty) is Action apply) apply();
    }

    /// <summary>
    /// Locks the element, asking for a policy the first time and reusing the stored one afterwards, or unlocks it by climbing the ladder.
    /// <paramref name="guard"/> wraps each save so the owning page can ignore its own settings echo.
    /// </summary>
    public static async Task ToggleAsync(string id, string label, Action<Action>? guard = null)
    {
        guard ??= save => save();
        if (!IsLocked(id))
        {
            if (!Host.Locks.HasPolicy(id) && !await LockDialogs.ConfigureAsync(id, label)) return;
            if (!ElementLocks.Lock(Host.Settings, id)) { Host.Notify.Warning("This setting could not be locked because the lock list is full."); return; }
            guard(() => Host.SaveSettings(ElementLocks.HistorySummary(label, true)));
        }
        else
        {
            if (!await LockDialogs.LadderAsync(id, label)) return;
            ElementLocks.Unlock(Host.Settings, id);
            guard(() => Host.SaveSettings(ElementLocks.HistorySummary(label, false)));
        }
        Refresh(id);
    }

    /// <summary>Opens the policy wizard to change how an unlocked element locks. A locked element must be unlocked first.</summary>
    public static async Task ChangePolicyAsync(string id, string label)
    {
        if (IsLocked(id)) { Host.Notify.Warning("Unlock this control before changing its lock policy."); return; }
        await LockDialogs.ConfigureAsync(id, label);
    }

    public static async Task RemovePolicyAsync(string id, string label)
    {
        if (IsLocked(id)) { Host.Notify.Warning("Unlock this control before removing its lock policy."); return; }
        if (!Host.Locks.HasPolicy(id)) return;
        if (await Host.Overlay.ConfirmAsync("Remove this lock policy?", label + "\n\n" + Loc.T("Its PIN or authenticator secret and its recovery codes are deleted. The control stays unlocked."), "Remove", true))
        {
            Host.Locks.Remove(id);
            Host.Notify.Success("Lock policy removed.");
        }
    }

    private static void FillMenu(ContextMenu menu, string id, string label, Action<Action>? guard)
    {
        var toggle = Ui.MenuItem("Lock this control…", Icons.Lock, () => _ = ToggleAsync(id, label, guard));
        var change = Ui.MenuItem("Change lock policy…", Icons.ShieldLock, () => _ = ChangePolicyAsync(id, label));
        var remove = Ui.MenuItem("Remove lock policy…", Icons.Delete, () => _ = RemovePolicyAsync(id, label), null, true);
        menu.Items.Add(toggle); menu.Items.Add(change); menu.Items.Add(remove);
        menu.Opened += (_, _) =>
        {
            var locked = IsLocked(id);
            toggle.Header = Loc.T(locked ? "Unlock this control…" : "Lock this control…");
            toggle.Icon = Ui.Icon(locked ? Icons.LockOpen : Icons.Lock, 18, false);
            change.IsEnabled = !locked; remove.IsEnabled = !locked && Host.Locks.HasPolicy(id);
        };
    }

    /// <summary>Adds the lock menu to an element without disabling it (a settings row disables its own control).</summary>
    public static void AttachMenu(FrameworkElement target, string id, string label, Action<Action>? guard = null)
    {
        if (target.ContextMenu == null) target.ContextMenu = new ContextMenu();
        else target.ContextMenu.Items.Add(new Separator());
        FillMenu(target.ContextMenu, id, label, guard);
        ContextMenuService.SetShowOnDisabled(target, true);
    }

    /// <summary>Makes an element lockable: lock menu, disabled while locked, and kept disabled if its page tries to re-enable it.</summary>
    public static void Attach(FrameworkElement target, string id, string label, Action<Action>? guard = null)
    {
        if (target.GetValue(ApplyProperty) != null) return;
        var applying = false; var enabledBefore = true;
        void Apply()
        {
            var locked = IsLocked(id);
            applying = true;
            try
            {
                if (locked) { if (target.GetValue(LockedBeforeProperty) is not string) enabledBefore = target.IsEnabled; target.IsEnabled = false; target.SetValue(System.Windows.Automation.AutomationProperties.HelpTextProperty, Loc.T("Locked. Right-click and choose Unlock this control.")); target.ToolTip = Loc.T("Locked. Right-click and choose Unlock this control."); }
                else if (target.GetValue(LockedBeforeProperty) is string) { target.IsEnabled = enabledBefore; target.ClearValue(FrameworkElement.ToolTipProperty); target.ClearValue(System.Windows.Automation.AutomationProperties.HelpTextProperty); }
                target.SetValue(LockedBeforeProperty, locked ? id : null);
            }
            finally { applying = false; }
        }
        target.IsEnabledChanged += (_, e) =>
        {
            if (applying) return;
            if (IsLocked(id) && target.IsEnabled) { applying = true; try { target.IsEnabled = false; } finally { applying = false; } }
        };
        Subscribe(id, target, Apply);
        AttachMenu(target, id, label, guard);
        Apply();
    }

    private static readonly Regex Slugger = new("[^a-z0-9]+", RegexOptions.Compiled);

    /// <summary>The stable lock id for a page's action button: "btn.&lt;page&gt;.&lt;label&gt;", lower case, at most 40 characters.</summary>
    public static string ButtonId(string pageKey, string label)
    {
        var id = "btn." + Slugger.Replace(pageKey.ToLowerInvariant(), "-").Trim('-') + "." + Slugger.Replace(label.ToLowerInvariant(), "-").Trim('-');
        id = id.Length > 40 ? id[..40] : id;
        return id.TrimEnd('.', '-', '_');
    }

    /// <summary>Makes a page's major action buttons (filled and danger buttons) lockable. Buttons built later are picked up the next time the page is shown.</summary>
    public static void AttachPage(DependencyObject root, string pageKey)
    {
        var filled = Application.Current.TryFindResource("Btn.Filled"); var danger = Application.Current.TryFindResource("Btn.Danger");
        void Walk(DependencyObject node)
        {
            if (node is Button b && b.Style != null && (ReferenceEquals(b.Style, filled) || ReferenceEquals(b.Style, danger)))
            {
                var name = System.Windows.Automation.AutomationProperties.GetName(b);
                if (!string.IsNullOrWhiteSpace(name)) Attach(b, ButtonId(pageKey, name), name);
            }
            foreach (var child in LogicalTreeHelper.GetChildren(node)) if (child is DependencyObject d) Walk(d);
        }
        Walk(root);
    }
}
