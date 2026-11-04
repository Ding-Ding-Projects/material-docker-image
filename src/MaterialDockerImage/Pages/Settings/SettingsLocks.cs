using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.Shell;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>The Locks section of Settings: every lock the app knows about, in one searchable list with unlock, lock again, policy and removal.</summary>
public sealed partial class SettingsPage
{
    private void LocksSection()
    {
        var table = new DataTable<LockRow>("Locks manager", "Search locks")
        {
            EmptyTitle = Loc.T("No locks yet."),
            EmptyMessage = Loc.T("Right-click any button, switch, option, field or slider and choose Lock this control… to add one."),
            Key = r => r.Id
        };
        void Refresh() => table.SetItems(LockManager.Rows(Host.Settings, Host.Locks));
        table.Column("Lock", r => r.Id, 230, true)
            .Column("What it is", r => Loc.T(r.Label), 200)
            .Column("Policy", r => Loc.T(LockPolicies.Label(r.Policy)), 200)
            .Column("Unlock lasts", r => Loc.T(UnlockDurations.Label(r.Duration)), 150)
            .ChipColumn("State", r => r.Locked ? Loc.T("Locked") : r.UnlockedUntil is { } until ? Loc.T("Unlocked until") + " " + until.ToLocalTime().ToString("HH:mm") : Loc.T("Unlocked"), r => r.Locked ? Tone.Warning : Tone.Success, 150);
        table.Filter("Locked only", r => r.Locked);
        table.Filter("Has a credential", r => r.Policy != LockPolicy.None);

        async Task Unlock(LockRow row) { await LockUi.ToggleAsync(row.Id, row.Label); Refresh(); }
        async Task Remove(IReadOnlyList<LockRow> selected)
        {
            if (selected.Count == 0) return;
            if (!await Host.Overlay.ConfirmAsync(selected.Count == 1 ? "Remove lock" : "Remove selected locks", string.Join("\n", selected.Select(r => r.Id)) + "\n\n" + Loc.T("Its PIN, password or authenticator secret and its recovery codes are deleted. The control stays unlocked."), "Remove", true)) return;
            var (removed, skipped) = LockManager.RemoveMany(Host.Settings, Host.Locks, selected.Select(r => r.Id));
            if (removed.Count > 0) Host.SaveSettings("Removed " + removed.Count + " lock(s)");
            foreach (var id in removed) LockUi.Refresh(id);
            if (removed.Count > 0) Host.Notify.Success(removed.Count == 1 ? "Lock removed." : "Locks removed.");
            if (skipped.Count > 0) Host.Notify.Warning(Loc.T("Skipped because they are locked:") + " " + string.Join(", ", skipped) + ". " + Loc.T("Unlock this lock before removing it."));
            Refresh();
        }
        table.BulkAction("Remove selected locks", Icons.Delete, Remove, true);
        table.RowAction("Unlock this control…", Icons.LockOpen, async r => { if (r.Locked) await Unlock(r); else Host.Notify.Info("Unlocked."); });
        table.RowAction("Lock again", Icons.Lock, r => { LockUi.RelockNow(r.Id, r.Label); Refresh(); return Task.CompletedTask; });
        table.RowAction("Change lock policy…", Icons.ShieldLock, async r => { await LockUi.ChangePolicyAsync(r.Id, r.Label); Refresh(); });
        table.RowAction("Remove lock", Icons.Delete, r => Remove([r]), null, true);
        table.RowAction("Support Tickets…", Icons.Help, r => { SupportDeskUi.Show(r.Id); return Task.CompletedTask; });
        table.Height = 360;
        Refresh();

        Extra(Ui.Caption("Every lock the app knows about, whether it guards a settings row, a button, a switch, a field, a slider or a drop-down. Each lock has its own policy and its own credentials; unlocking one never unlocks another."));
        Extra(Ui.Caption(Loc.T(ElementLocks.Disclosure)).Margin(0, 4, 0, 4));
        Extra(table);
        Extra(Ui.Card(LockDialogs.RecoveryFooter(), 16, "SurfaceContainer", 12).Margin(0, 12, 0, 0));
    }
}
