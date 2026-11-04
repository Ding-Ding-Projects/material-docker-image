using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MaterialDockerImage.UI;

public enum TicketCategory { ForgotPin, ForgotPassword, LostAuthenticator, LostRecoveryCodes, SomethingElse }
public enum TicketSeverity { Low, Medium, High, Critical }
public enum TicketStatus { Open, Acknowledged, Investigating, Resolved }

/// <summary>One line in a ticket's conversation. Only a code is stored; the wording comes from <see cref="SupportDesk"/> so it can be translated.</summary>
public sealed class TicketMessage
{
    public long AtUnix { get; set; }
    public string Code { get; set; } = "";
}

public sealed class SupportTicket
{
    public string Number { get; set; } = "";
    public TicketCategory Category { get; set; }
    public TicketSeverity Severity { get; set; }
    public TicketStatus Status { get; set; }
    public string Description { get; set; } = "";
    /// <summary>The lock the ticket is about, or empty. A lock id, never a credential.</summary>
    public string LockId { get; set; } = "";
    public long CreatedUnix { get; set; }
    public long UpdatedUnix { get; set; }
    public List<TicketMessage> Messages { get; set; } = new();
}

/// <summary>
/// The wording and the one real action of the support desk. The desk is the app's own joke and is entirely local: it is not a real service,
/// no person reads a ticket, and the only resolution is to open the data folder so the person can delete it themselves.
/// </summary>
public static class SupportDesk
{
    /// <summary>The plain line shown at every funny level and in every language mode. It is never styled or shortened.</summary>
    public const string Disclosure = "Nothing is sent anywhere. A ticket exists only on this computer, no network request is made, no data is collected, and nobody is reading it.";

    public const string SeverityNote = "Severity is written down and then treated exactly like every other severity.";

    public const string FirstResponse = "first-response", Investigating = "investigating", Resolution = "resolution";

    /// <summary>The factual message for a code. The playful lead-in is separate so a funny level can drop it without losing the fact.</summary>
    public static string Message(string code) => code switch
    {
        FirstResponse => "Your ticket has been logged on this computer. Please do not wait for a reply: no person will write one.",
        Investigating => "The desk has read the manual once. The manual says the only way back in is to delete the app's data folder yourself.",
        Resolution => "Resolution: open the data folder below and delete it in your file manager. The app never deletes it for you.",
        _ => ""
    };

    public static string LeadIn(string code) => code switch
    {
        FirstResponse => "Thank you for contacting the Department of Forgotten Things.",
        Investigating => "Our specialists have convened (it was one specialist, and it was the app).",
        Resolution => "Case closed with great ceremony.",
        _ => ""
    };

    /// <summary>The message a ticket reaches at each status; Open has none.</summary>
    public static string? CodeFor(TicketStatus status) => status switch
    {
        TicketStatus.Acknowledged => FirstResponse,
        TicketStatus.Investigating => Investigating,
        TicketStatus.Resolved => Resolution,
        _ => null
    };

    public static string CategoryLabel(TicketCategory category) => category switch
    {
        TicketCategory.ForgotPin => "Forgot my PIN",
        TicketCategory.ForgotPassword => "Forgot my password",
        TicketCategory.LostAuthenticator => "Lost my authenticator",
        TicketCategory.LostRecoveryCodes => "Lost my recovery codes",
        _ => "Something else"
    };

    public static string SeverityLabel(TicketSeverity severity) => severity switch { TicketSeverity.Low => "Low", TicketSeverity.Medium => "Medium", TicketSeverity.High => "High", _ => "Critical" };
    public static string StatusLabel(TicketStatus status) => status switch { TicketStatus.Open => "New", TicketStatus.Acknowledged => "Acknowledged", TicketStatus.Investigating => "Investigating", _ => "Resolved" };

    /// <summary>
    /// What the resolution button does: open this exact folder in the platform's file manager. It never deletes anything.
    /// <see cref="DeletesAnything"/> is always false; the check suite asserts it.
    /// </summary>
    public sealed record ResolutionAction(string Folder, bool DeletesAnything);
    public static ResolutionAction Resolve(string dataFolder) => new(dataFolder, false);
}

/// <summary>Tickets kept in their own local file. Nothing leaves the computer, and deleting the data folder clears them with everything else.</summary>
public sealed class SupportTicketStore
{
    public const int MaxTickets = 200, MaxDescription = 1000;
    public static readonly TimeSpan AdvanceEvery = TimeSpan.FromSeconds(20);

    private readonly string? path;
    private readonly Func<DateTimeOffset> clock;
    private readonly List<SupportTicket> tickets = new();

    public SupportTicketStore(string? path, Func<DateTimeOffset>? clock = null)
    {
        this.path = path; this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        Load();
    }

    public static string DefaultPath => Path.Combine(UserSettings.DataDirectory, "tickets.json");
    public string? LoadError { get; private set; }
    /// <summary>Newest first.</summary>
    public IReadOnlyList<SupportTicket> Tickets => tickets.OrderByDescending(t => t.CreatedUnix).ThenByDescending(t => t.Number, StringComparer.Ordinal).ToList();
    public SupportTicket? Get(string number) => tickets.FirstOrDefault(t => string.Equals(t.Number, number, StringComparison.Ordinal));

    private sealed class FileShape { public int Version { get; set; } = 1; public List<SupportTicket> Tickets { get; set; } = new(); }

    private void Load()
    {
        if (path == null || !File.Exists(path)) return;
        try
        {
            if (new FileInfo(path).Length > 4_000_000) { LoadError = "The ticket file is too large, so it was ignored."; return; }
            var shape = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(path));
            if (shape == null) return;
            foreach (var t in shape.Tickets.Take(MaxTickets))
            {
                if (string.IsNullOrWhiteSpace(t.Number) || !Enum.IsDefined(t.Category) || !Enum.IsDefined(t.Severity) || !Enum.IsDefined(t.Status)) continue;
                t.Description = Clip(t.Description, MaxDescription);
                t.LockId = ElementLocks.IsValidId(t.LockId) ? t.LockId : "";
                t.Messages = (t.Messages ?? new()).Where(m => SupportDesk.Message(m.Code).Length > 0).Take(8).ToList();
                if (tickets.All(x => x.Number != t.Number)) tickets.Add(t);
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { tickets.Clear(); LoadError = "The ticket file could not be read, so its tickets were ignored: " + e.Message; }
    }

    private static string Clip(string? text, int max) { var t = (text ?? "").Trim(); return t.Length > max ? t[..max] : t; }

    private void Save()
    {
        if (path == null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new FileShape { Tickets = tickets }, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }

    private string NextNumber()
    {
        var day = clock().UtcDateTime.ToString("yyyyMMdd");
        var prefix = "TKT-" + day + "-";
        var last = tickets.Where(t => t.Number.StartsWith(prefix, StringComparison.Ordinal)).Select(t => int.TryParse(t.Number[prefix.Length..], out var n) ? n : 0).DefaultIfEmpty(0).Max();
        return prefix + (last + 1).ToString("0000");
    }

    /// <summary>Files a ticket. The description is required; the ticket number is generated here and nowhere else.</summary>
    public SupportTicket Create(TicketCategory category, TicketSeverity severity, string? description, string? lockId = null)
    {
        var text = Clip(description, MaxDescription);
        if (text.Length == 0) throw new ArgumentException("Say what happened first. The desk cannot help with a blank page.", nameof(description));
        if (!Enum.IsDefined(category) || !Enum.IsDefined(severity)) throw new ArgumentException("Unknown category or severity.");
        if (tickets.Count >= MaxTickets) throw new InvalidOperationException("The ticket list is full. Remove old tickets first.");
        var now = clock().ToUnixTimeSeconds();
        var ticket = new SupportTicket
        {
            Number = NextNumber(), Category = category, Severity = severity, Status = TicketStatus.Open, Description = text,
            LockId = lockId != null && ElementLocks.IsValidId(lockId) ? lockId : "", CreatedUnix = now, UpdatedUnix = now
        };
        tickets.Add(ticket);
        Save();
        return ticket;
    }

    /// <summary>Moves a ticket to its next status and adds that status's message. A resolved ticket stays resolved. Returns false when nothing changed.</summary>
    public bool Advance(string number)
    {
        var t = Get(number);
        if (t == null || t.Status == TicketStatus.Resolved) return false;
        t.Status = (TicketStatus)((int)t.Status + 1);
        t.UpdatedUnix = clock().ToUnixTimeSeconds();
        if (SupportDesk.CodeFor(t.Status) is { } code) t.Messages.Add(new TicketMessage { AtUnix = t.UpdatedUnix, Code = code });
        Save();
        return true;
    }

    /// <summary>Takes a ticket straight to Resolved, adding any message it had not reached yet, so the resolution is always on the record.</summary>
    public bool ResolveNow(string number)
    {
        var t = Get(number);
        if (t == null || t.Status == TicketStatus.Resolved) return false;
        while (t.Status != TicketStatus.Resolved) Advance(number);
        return true;
    }

    /// <summary>The status advances by itself while the person waits: one step for each ticket that has sat for <see cref="AdvanceEvery"/>. Returns how many moved.</summary>
    public int Tick()
    {
        var moved = 0;
        var now = clock().ToUnixTimeSeconds();
        foreach (var t in tickets.Where(t => t.Status != TicketStatus.Resolved && now - t.UpdatedUnix >= AdvanceEvery.TotalSeconds).ToList())
            if (Advance(t.Number)) moved++;
        return moved;
    }

    public bool Remove(string number) { var removed = tickets.RemoveAll(t => t.Number == number) > 0; if (removed) Save(); return removed; }

    public int RemoveMany(IEnumerable<string> numbers)
    {
        var set = numbers.ToHashSet(StringComparer.Ordinal);
        var removed = tickets.RemoveAll(t => set.Contains(t.Number));
        if (removed > 0) Save();
        return removed;
    }

    public static string SearchText(SupportTicket t) =>
        $"{t.Number} {SupportDesk.CategoryLabel(t.Category)} {SupportDesk.SeverityLabel(t.Severity)} {SupportDesk.StatusLabel(t.Status)} {t.LockId} {t.Description}";

    public IReadOnlyList<SupportTicket> Filter(Func<string, bool> matches) => Tickets.Where(t => matches(SearchText(t))).ToList();

    public static readonly IReadOnlyList<string> ExportHeaders = ["Ticket", "Category", "Severity", "Status", "Lock", "Created (UTC)", "Description"];

    public static IReadOnlyList<string[]> ExportRows(IEnumerable<SupportTicket> rows) =>
        rows.Select(t => new[] { t.Number, SupportDesk.CategoryLabel(t.Category), SupportDesk.SeverityLabel(t.Severity), SupportDesk.StatusLabel(t.Status), t.LockId, DateTimeOffset.FromUnixTimeSeconds(t.CreatedUnix).ToString("yyyy-MM-dd HH:mm:ss"), t.Description }).ToList();
}
