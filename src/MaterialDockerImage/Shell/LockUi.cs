using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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

    /// <summary>The lock a click on an element answers to, so a click on a locked, visibly disabled control can open that exact lock's unlock prompt.</summary>
    public sealed record ClickTarget(string Id, string Label);
    private static readonly DependencyProperty ClickTargetProperty = DependencyProperty.RegisterAttached("ClickTarget", typeof(ClickTarget), typeof(LockUi));
    private static readonly UnlockPromptGate Gate = new();

    /// <summary>Marks an element (and everything inside it) as answering to a lock when it is locked. A click on it then opens the unlock prompt instead of doing nothing.</summary>
    public static void SetClickTarget(DependencyObject element, string id, string label) => element.SetValue(ClickTargetProperty, new ClickTarget(id, label));

    private static DependencyObject? ParentOf(DependencyObject node) =>
        node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node) ?? (node as FrameworkContentElement)?.Parent;

    /// <summary>The nearest locked lock the clicked element answers to, or null when the click is on nothing locked.</summary>
    public static ClickTarget? FindLockedTarget(DependencyObject? start)
    {
        for (var node = start; node != null; node = ParentOf(node))
            if (node.GetValue(ClickTargetProperty) is ClickTarget target && IsLocked(target.Id)) return target;
        return null;
    }

    /// <summary>
    /// Installs the disabled-click prompt on a window or popup root: a left click that lands on a locked control (which is disabled, so it would otherwise ignore the click) is
    /// swallowed, the protected action does not run, and that control's own unlock prompt opens. The pointer position is hit-tested because a disabled element does not receive the event itself.
    /// </summary>
    public static void InstallDisabledClickPrompt(UIElement root)
    {
        root.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((_, e) =>
        {
            var hit = System.Windows.Media.VisualTreeHelper.HitTest(root, e.GetPosition(root))?.VisualHit;
            var target = FindLockedTarget(hit) ?? FindLockedTarget(e.OriginalSource as DependencyObject);
            if (target == null) return;
            e.Handled = true;
            if (Gate.Click(target.Id, true) == DisabledClickDecision.OpenPrompt) _ = UnlockThenAsync(target.Id, target.Label);
        }), true);
    }

    /// <summary>
    /// Asks for the unlock of one lock (one prompt at a time per lock), and when the person gets through runs <paramref name="after"/>, the action they were attempting.
    /// A lock unlocked for "this action only" closes again once that action has run.
    /// </summary>
    public static async Task UnlockThenAsync(string id, string label, Action? after = null)
    {
        if (!Gate.TryBegin(id)) return;
        try
        {
            if (IsLocked(id) && !await ToggleAsync(id, label)) return;
            if (after == null) return;
            after();
            ActionPerformed(id);
        }
        finally { Gate.End(id); }
    }

    /// <summary>Adds the lock, unlock, change-policy and remove-policy items for a tab, tab group or navigation destination to an existing menu.</summary>
    public static void AddLockItems(ContextMenu menu, string id, string label, string lockHeader, string unlockHeader)
    {
        var toggle = Ui.MenuItem(lockHeader, Icons.Lock, () => _ = ToggleAsync(id, label));
        var change = Ui.MenuItem("Change lock policy…", Icons.ShieldLock, () => _ = ChangePolicyAsync(id, label));
        menu.Items.Add(toggle); menu.Items.Add(change);
        menu.Opened += (_, _) =>
        {
            var locked = IsLocked(id);
            toggle.Header = Loc.T(locked ? unlockHeader : lockHeader);
            toggle.Icon = Ui.Icon(locked ? Icons.LockOpen : Icons.Lock, 18, false);
            change.IsEnabled = !locked;
        };
    }

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

    /// <summary>"This action only" unlocks waiting for their action: the id and, for a settings row, the values it had when it was unlocked.</summary>
    private static readonly Dictionary<string, string> ArmedOnce = new(StringComparer.Ordinal);

    /// <summary>
    /// Locks the element, asking for a policy the first time and reusing the stored one afterwards, or unlocks it by climbing the ladder.
    /// An unlock lasts as long as the lock's chosen duration. <paramref name="guard"/> wraps each save so the owning page can ignore its own settings echo.
    /// </summary>
    public static async Task<bool> ToggleAsync(string id, string label, Action<Action>? guard = null)
    {
        guard ??= save => save();
        if (!IsLocked(id))
        {
            if (!Host.Locks.HasPolicy(id) && !await LockDialogs.ConfigureAsync(id, label)) return false;
            if (!LockLifecycle.Relock(Host.Settings, Host.Locks, id)) { Host.Notify.Warning("This setting could not be locked because the lock list is full."); return false; }
            ArmedOnce.Remove(id);
            LockManager.SetLabel(Host.Settings, id, label);
            guard(() => Host.SaveSettings(ElementLocks.HistorySummary(label, true)));
        }
        else
        {
            if (!await LockDialogs.LadderAsync(id, label)) return false;
            LockLifecycle.Unlock(Host.Settings, Host.Locks, id, out var until);
            var duration = Host.Locks.DurationOf(id);
            if (duration == UnlockDuration.Once) ArmedOnce[id] = ElementLocks.Snapshot(Host.Settings, id);
            guard(() => Host.SaveSettings(ElementLocks.HistorySummary(label, false)));
            Host.Notify.Info(until != null ? Loc.T("Unlocked until") + " " + until.Value.ToLocalTime().ToString("HH:mm") + "." : duration == UnlockDuration.Once ? "Unlocked for this action only." : duration == UnlockDuration.UntilRestart ? "Unlocked until the app closes." : "Unlocked.");
        }
        Refresh(id);
        return true;
    }

    /// <summary>Locks an unlocked control again right away. Locking needs no credential.</summary>
    public static void RelockNow(string id, string label)
    {
        if (IsLocked(id)) return;
        if (!LockLifecycle.Relock(Host.Settings, Host.Locks, id)) return;
        ArmedOnce.Remove(id);
        Host.SaveSettings(ElementLocks.HistorySummary(label, true));
        Host.Notify.Info("Locked again.");
        Refresh(id);
    }

    /// <summary>Called after the protected action ran. A lock unlocked for "this action only" closes again.</summary>
    public static void ActionPerformed(string id)
    {
        if (!ArmedOnce.ContainsKey(id)) return;
        ArmedOnce.Remove(id);
        if (!LockLifecycle.ConsumeOnce(Host.Settings, Host.Locks, id)) return;
        Host.SaveSettings(ElementLocks.HistorySummary(LockManager.LabelOf(Host.Settings, id), true));
        Refresh(id);
    }

    /// <summary>After any settings save: a settings row unlocked for one action closes again once its values have changed. Returns the ids locked again.</summary>
    public static IReadOnlyList<string> SettingsSaved()
    {
        var closed = new List<string>();
        foreach (var (id, before) in ArmedOnce.ToList())
        {
            if (ElementLocks.Snapshot(Host.Settings, id) == before) continue;
            ArmedOnce.Remove(id);
            if (LockLifecycle.ConsumeOnce(Host.Settings, Host.Locks, id)) closed.Add(id);
        }
        return closed;
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
            ArmedOnce.Remove(id);
            Host.Notify.Success("Lock policy removed.");
        }
    }

    private static void FillMenu(ContextMenu menu, string id, string label, Action<Action>? guard)
    {
        var toggle = Ui.MenuItem("Lock this control…", Icons.Lock, () => _ = ToggleAsync(id, label, guard));
        var change = Ui.MenuItem("Change lock policy…", Icons.ShieldLock, () => _ = ChangePolicyAsync(id, label));
        var remove = Ui.MenuItem("Remove lock policy…", Icons.Delete, () => _ = RemovePolicyAsync(id, label), null, true);
        var manage = Ui.MenuItem("Locks manager…", Icons.ShieldLock, () => Host.NavigateWith("Settings", "Locks"));
        menu.Items.Add(toggle); menu.Items.Add(change); menu.Items.Add(remove); menu.Items.Add(manage);
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
        if (target.ContextMenu == null)
        {
            target.ContextMenu = new ContextMenu();
            // A text box loses its built-in clipboard menu when it gets one of its own, so the same commands are put back.
            if (target is TextBox)
            {
                foreach (var command in new[] { ApplicationCommands.Cut, ApplicationCommands.Copy, ApplicationCommands.Paste })
                    target.ContextMenu.Items.Add(new MenuItem { Command = command, CommandTarget = target });
                target.ContextMenu.Items.Add(new Separator());
            }
        }
        else target.ContextMenu.Items.Add(new Separator());
        FillMenu(target.ContextMenu, id, label, guard);
        ContextMenuService.SetShowOnDisabled(target, true);
        target.SetValue(MenuHostProperty, true);
    }

    private static readonly DependencyProperty MenuHostProperty = DependencyProperty.RegisterAttached("LockMenuHost", typeof(bool), typeof(LockUi));

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
                if (locked) { if (target.GetValue(LockedBeforeProperty) is not string) enabledBefore = target.IsEnabled; target.IsEnabled = false; target.SetValue(System.Windows.Automation.AutomationProperties.HelpTextProperty, Loc.T("Locked. Click it to unlock.")); target.ToolTip = Loc.T("Locked. Click it to unlock."); }
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
        SetClickTarget(target, id, label);
        AttachMenu(target, id, label, guard);
        HookAction(target, id);
        Apply();
    }

    /// <summary>Tells the lock when the person has used the element, so an unlock for one action ends after it.</summary>
    private static void HookAction(FrameworkElement target, string id)
    {
        void Done() { if (ArmedOnce.ContainsKey(id)) target.Dispatcher.BeginInvoke(() => ActionPerformed(id), System.Windows.Threading.DispatcherPriority.ApplicationIdle); }
        switch (target)
        {
            case ButtonBase button: button.Click += (_, _) => Done(); break;
            case Selector selector: selector.SelectionChanged += (_, _) => Done(); break;
            case RangeBase range: range.ValueChanged += (_, _) => Done(); break;
            case TextBox box: var text = box.Text; box.LostFocus += (_, _) => { if (box.Text != text) { text = box.Text; Done(); } }; break;
        }
    }

    private static readonly Regex Slugger = new("[^a-z0-9]+", RegexOptions.Compiled);

    /// <summary>The stable lock id for a page's action button: "btn.&lt;page&gt;.&lt;label&gt;", lower case, at most 40 characters.</summary>
    public static string ButtonId(string pageKey, string label)
    {
        var id = "btn." + Slugger.Replace(pageKey.ToLowerInvariant(), "-").Trim('-') + "." + Slugger.Replace(label.ToLowerInvariant(), "-").Trim('-');
        id = id.Length > 40 ? id[..40] : id;
        return id.TrimEnd('.', '-', '_');
    }

    /// <summary>
    /// Makes every interactive element on a page lockable: the page's major action buttons keep their existing ids, and every other button, switch, option, drop-down, text field and slider
    /// gets a stable derived id (its automation id when it has one, otherwise its page, kind, English name and position among identical elements).
    /// Elements built later are picked up the next time the page is shown. Settings rows already carry their own lock and are skipped.
    /// </summary>
    public static void AttachPage(DependencyObject root, string pageKey)
    {
        var filled = Application.Current.TryFindResource("Btn.Filled"); var danger = Application.Current.TryFindResource("Btn.Danger");
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        void Walk(DependencyObject node, bool insideRow)
        {
            if (node is FrameworkElement element)
            {
                var row = insideRow || element.GetValue(MenuHostProperty) is true;
                if (!row && element.GetValue(ApplyProperty) == null && Kind(element) is { } kind)
                {
                    var automationId = System.Windows.Automation.AutomationProperties.GetAutomationId(element);
                    var name = System.Windows.Automation.AutomationProperties.GetName(element);
                    var english = Loc.Original(name);
                    if (element is Button b && b.Style != null && (ReferenceEquals(b.Style, filled) || ReferenceEquals(b.Style, danger)) && !string.IsNullOrWhiteSpace(name))
                        Attach(element, ButtonId(pageKey, name), name);
                    else
                    {
                        var key = kind + "|" + (string.IsNullOrWhiteSpace(automationId) ? english : automationId);
                        seen[key] = seen.TryGetValue(key, out var n) ? n + 1 : 0;
                        Attach(element, ElementIdentity.Derive(pageKey, automationId, english, kind, seen[key]), ElementIdentity.Describe(kind, english, automationId));
                    }
                }
                insideRow = row;
            }
            foreach (var child in LogicalTreeHelper.GetChildren(node)) if (child is DependencyObject d) Walk(d, insideRow);
        }
        Walk(root, false);
    }

    /// <summary>The element kinds that can be locked from their context menu, or null for anything else.</summary>
    private static string? Kind(FrameworkElement element) => element switch
    {
        RadioButton => "RadioButton",
        CheckBox => "CheckBox",
        ToggleButton => "ToggleButton",
        Button => "Button",
        ComboBox => "ComboBox",
        TextBox { IsReadOnly: false } => "TextBox",
        Slider => "Slider",
        _ => null
    };
}
