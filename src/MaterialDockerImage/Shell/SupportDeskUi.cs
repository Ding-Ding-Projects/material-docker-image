using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Shell;

/// <summary>
/// The Support Tickets screen: a pretend service desk for people locked out of their own locks. It is entirely local. The plain disclosure line states that nothing is sent anywhere,
/// the comedy is only the wording, and the one real resolution opens the data folder so the person can delete it themselves. The app never deletes it.
/// </summary>
public static class SupportDeskUi
{
    private static AppHost Host => AppHost.Current;
    private static SupportTicketStore? store;
    private static SupportTicketStore Store => store ??= new SupportTicketStore(Host.IsCapture ? null : SupportTicketStore.DefaultPath);

    /// <summary>Opens the desk in a dialog over the current page. Used by the unlock prompt, the lock setting and the Locks manager.</summary>
    public static void Show(string? lockId = null)
    {
        Action? close = null;
        var panel = Panel(lockId);
        var closeButton = Ui.Button("Close", null, ButtonKind.Text, () => close?.Invoke());
        closeButton.HorizontalAlignment = HorizontalAlignment.Right;
        close = Host.Overlay.Show(Ui.VStack(14, panel, closeButton), 860);
    }

    private static string Folder => UserSettings.DataDirectory;

    /// <summary>The whole screen as a view, so the Tools page can host it too.</summary>
    public static FrameworkElement Panel(string? lockId = null)
    {
        var desk = Store;
        var category = Ui.Combo(Enum.GetValues<TicketCategory>().Select(c => Loc.T(SupportDesk.CategoryLabel(c))), Loc.T(SupportDesk.CategoryLabel(TicketCategory.ForgotPassword)));
        var severity = Ui.Combo(Enum.GetValues<TicketSeverity>().Select(s => Loc.T(SupportDesk.SeverityLabel(s))), Loc.T(SupportDesk.SeverityLabel(TicketSeverity.Critical)));
        var description = Ui.Input("", "Describe what happened");
        description.AcceptsReturn = true; description.TextWrapping = TextWrapping.Wrap; description.MinHeight = 72; description.MaxLength = SupportTicketStore.MaxDescription;
        var error = Ui.Text("", 13, null, "Error");
        var details = new StackPanel();
        var table = new DataTable<SupportTicket>("Support tickets", "Search tickets")
        {
            EmptyTitle = Loc.T("No tickets yet."),
            EmptyMessage = Loc.T("File a ticket above. It stays on this computer."),
            Key = t => t.Number
        };
        table.Column("Ticket", t => t.Number, 150, true)
            .ChipColumn("Status", t => Loc.T(SupportDesk.StatusLabel(t.Status)), t => t.Status == TicketStatus.Resolved ? Tone.Success : t.Status == TicketStatus.Open ? Tone.Warning : Tone.Primary, 120)
            .Column("Category", t => Loc.T(SupportDesk.CategoryLabel(t.Category)), 170)
            .Column("Severity", t => Loc.T(SupportDesk.SeverityLabel(t.Severity)), 90)
            .Column("Description", t => t.Description.Replace('\r', ' ').Replace('\n', ' '), 280);
        table.Height = 320;
        void Refresh() { table.SetItems(desk.Tickets); ShowDetails(table.Focused); }
        void ShowDetails(SupportTicket? ticket)
        {
            details.Children.Clear();
            if (ticket == null) return;
            details.Children.Add(Ui.Title(ticket.Number, 16));
            details.Children.Add(Ui.Caption(Loc.T("Severity") + ": " + Loc.T(SupportDesk.SeverityLabel(ticket.Severity)) + ". " + Loc.T(SupportDesk.SeverityNote)));
            if (ticket.Messages.Count == 0) details.Children.Add(Ui.Caption("Waiting for a reply that is not coming. Advance the status to see what the desk says."));
            foreach (var message in ticket.Messages)
            {
                var when = DateTimeOffset.FromUnixTimeSeconds(message.AtUnix).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                // The playful lead-in follows the funny level; the factual sentence is always there.
                var text = Loc.Voice(Loc.T(SupportDesk.Message(message.Code)), Loc.T(SupportDesk.LeadIn(message.Code)), Loc.T(SupportDesk.LeadIn(message.Code)));
                details.Children.Add(Ui.Card(Ui.VStack(4, Ui.Overline(when), Ui.Text(text, 14, null, "OnSurface", true, false)), 12, "SurfaceContainer", 12));
            }
        }
        void Resolve(IReadOnlyList<SupportTicket> selected)
        {
            foreach (var ticket in selected) desk.ResolveNow(ticket.Number);
            Refresh();
            Host.Notify.Info("Resolution: open the data folder below and delete it in your file manager. The app never deletes it for you.");
        }
        table.BulkAction("Advance status", Icons.PlayArrow, items => { foreach (var t in items) desk.Advance(t.Number); Refresh(); return Task.CompletedTask; });
        table.BulkAction("Resolve", Icons.CheckCircle, items => { Resolve(items); return Task.CompletedTask; });
        table.BulkAction("Remove ticket", Icons.Delete, items => { var n = desk.RemoveMany(items.Select(t => t.Number)); Refresh(); Host.Notify.Info(n == 1 ? "Ticket removed." : "Tickets removed."); return Task.CompletedTask; }, true);
        table.RowAction("Advance status", Icons.PlayArrow, t => { desk.Advance(t.Number); Refresh(); return Task.CompletedTask; });
        table.RowAction("Resolve", Icons.CheckCircle, t => { Resolve([t]); return Task.CompletedTask; });
        table.RowAction("Remove ticket", Icons.Delete, t => { desk.Remove(t.Number); Refresh(); Host.Notify.Info("Ticket removed."); return Task.CompletedTask; }, null, true);
        table.FocusedChanged += ShowDetails;

        var file = Ui.Button("File a ticket", Icons.Add, ButtonKind.Filled, () =>
        {
            try
            {
                var ticket = desk.Create(Enum.GetValues<TicketCategory>()[Math.Max(0, category.SelectedIndex)], Enum.GetValues<TicketSeverity>()[Math.Max(0, severity.SelectedIndex)], description.Text, lockId);
                description.Clear(); error.Text = "";
                Host.Notify.Success("Ticket filed on this computer.");
                Refresh();
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException) { error.Text = Loc.T(e.Message); }
        });
        var action = SupportDesk.Resolve(Folder);
        var open = Ui.Button("Open the data folder", Icons.FolderOpen, ButtonKind.Tonal, () => Pages.PageParts.OpenFolder(action.Folder));
        var copy = Ui.Button("Copy folder path", Icons.ContentCopy, ButtonKind.Text, () => LockDialogs.Copy(action.Folder));

        // The desk moves the status along by itself while it is on screen.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) => { if (desk.Tick() > 0) Refresh(); };

        var root = Ui.VStack(14,
            Ui.Title("Support Tickets", 20),
            // The one plain line. It is never styled by the funny level and is the same in every language mode.
            Ui.Card(Ui.Text(SupportDesk.Disclosure, 14, FontWeights.SemiBold, "OnSurface"), 12, "SurfaceContainer", 12),
            Ui.Caption(Loc.Voice(Loc.T("The only way back into a locked control is to delete the app's data folder yourself."), "Welcome to the Department of Forgotten Things.", "歡迎嚟到遺忘事務部。")),
            Ui.Card(Ui.VStack(10, Ui.Overline("The data folder"), Ui.Mono(Pages.PageParts.Sanitize(action.Folder), 13, "OnSurface", true), Ui.Caption("The desk opens the folder and stands back. Deleting it is your own act."), Ui.Wrap(8, open, copy)), 16, "SurfaceLow", 16),
            Ui.Card(Ui.VStack(10, Ui.Overline("File a ticket"), Ui.Wrap(16, Ui.Field("Category", category), Ui.Field("Severity", severity)), Ui.Field("Describe what happened", description), error, Ui.HStack(8, file)), 16, "SurfaceLow", 16),
            table,
            details);
        root.Loaded += (_, _) => { Refresh(); timer.Start(); };
        root.Unloaded += (_, _) => timer.Stop();
        return root;
    }
}
