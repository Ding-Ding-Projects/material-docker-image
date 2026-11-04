using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace MaterialDockerImage.UI;

/// <summary>The rungs of the game ladder that can shorten a lock's wait: a dish question, ten sums, whack-a-mole, then the plain clock.</summary>
public enum LadderRung { DimSum, Sums, Mole, Clock }

public enum LadderUnavailable { None, BudgetSpent, FellThisLockout, NotWaiting }

/// <summary>Why an answer was refused rather than graded.</summary>
public enum LadderReject { None, UnknownChallenge, Expired, WrongKind, TooEarly, BudgetSpent }

public enum LadderStepKind
{
    /// <summary>The game was won: the caller may now clear the wait (never the credential).</summary>
    Cleared,
    /// <summary>A wrong dish with tries left; ask a fresh question.</summary>
    Retry,
    /// <summary>The rung was lost; the ladder moved down (to the clock, it ends).</summary>
    Fell,
    /// <summary>The answer was refused. The challenge is spent either way.</summary>
    Rejected
}

public sealed record LadderStep(LadderStepKind Kind, LadderRung Rung, LadderReject Reject = LadderReject.None, int WrongDishes = 0, int Hits = 0, int Needed = 0)
{
    public bool Cleared => Kind == LadderStepKind.Cleared;
    public bool Finished => Kind is LadderStepKind.Cleared || Rung == LadderRung.Clock;
}

public sealed record LadderOffer(bool Available, LadderRung Rung, int SkipsLeft, LadderUnavailable Why);

public sealed record DishName(string Id, string English, string Cantonese);

/// <summary>One dish question. The correct choice lives only inside the ladder; it is not on this object.</summary>
public sealed record DishChallenge(string Nonce, IReadOnlyList<string> Choices, bool HasPicture, string Clue, DateTimeOffset ExpiresAt);

public sealed record SumsChallenge(string Nonce, IReadOnlyList<string> Problems, DateTimeOffset ExpiresAt);

public sealed record MoleSlot(int Cell, int AppearMs, int VisibleMs);

public sealed record MoleChallenge(string Nonce, int DurationMs, int Cells, int Needed, IReadOnlyList<MoleSlot> Moles, DateTimeOffset ExpiresAt);

/// <summary>One tap on a cell, with the time in milliseconds since the round began, as the game view reports it.</summary>
public sealed record MoleHit(int Cell, int AtMs);

/// <summary>Fixed numbers of the ladder, in one place so the dialogs, the copy and the checks agree.</summary>
public static class LadderRules
{
    public const int DishChoices = 4, WrongDishesAllowed = 5, SumCount = 10;
    public const int MoleCells = 9, MoleCount = 14, MoleNeeded = 8, MoleRoundMs = 20_000, MoleFirstMs = 500, MoleSlotMs = 1300, MoleVisibleMs = 1000;
    public const int SkipsPerHour = 3;
    /// <summary>The on-screen rules of the mole round. A check keeps the numbers in this sentence equal to the constants above.</summary>
    public const string MoleIntro = "A mole pops up in one cell at a time and stays for one second. Click it, or press its number key 1 to 9. Hit at least 8 of 14 in 20 seconds. Nothing here is animated.";
    public static readonly TimeSpan SkipWindow = TimeSpan.FromHours(1), DishLife = TimeSpan.FromMinutes(5), SumsLife = TimeSpan.FromMinutes(10), MoleGrace = TimeSpan.FromMinutes(1);

    /// <summary>The one place that decides where the ladder starts. Under School mode the dish rung is absent, not skipped with a message.</summary>
    public static LadderRung StartingRung(bool schoolMode) => schoolMode ? LadderRung.Sums : LadderRung.DimSum;

    public static LadderRung AfterFailure(LadderRung rung) => rung switch { LadderRung.DimSum => LadderRung.Sums, LadderRung.Sums => LadderRung.Mole, _ => LadderRung.Clock };

    /// <summary>The effective rung for a stored one: School mode lifts anything on the dish rung to the sums.</summary>
    public static LadderRung Effective(LadderRung stored, bool schoolMode) => schoolMode && stored == LadderRung.DimSum ? LadderRung.Sums : stored;

    public static int MoleStart(int index) => MoleFirstMs + index * MoleSlotMs;
}

/// <summary>
/// What the ladder remembers between launches, apart from locks.json: when waits were skipped (the rolling budget) and how far each lockout got.
/// Nothing here is a credential; deleting the file only refills the budget, and the lock file is untouched.
/// </summary>
public sealed class LadderLedger
{
    public sealed class Progress { public long Stamp { get; set; } public LadderRung Rung { get; set; } public int WrongDishes { get; set; } }
    private sealed class FileShape { public int Version { get; set; } = 1; public List<long> Skips { get; set; } = new(); public Dictionary<string, Progress> Locks { get; set; } = new(); }

    private readonly string? path;
    private readonly Func<DateTimeOffset> clock;
    private FileShape data = new();

    /// <summary>A null path keeps everything in memory (capture mode and checks).</summary>
    public LadderLedger(string? path, Func<DateTimeOffset>? clock = null)
    {
        this.path = path; this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        if (path == null || !File.Exists(path)) return;
        try
        {
            var shape = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(path));
            if (shape == null) return;
            data = new FileShape { Skips = shape.Skips.Take(64).ToList(), Locks = shape.Locks.Where(p => ElementLocks.IsValidId(p.Key)).Take(ElementLocks.MaxLocks).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal) };
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { data = new FileShape(); }
    }

    public static string DefaultPath => Path.Combine(UserSettings.DataDirectory, "ladder.json");

    private void Prune()
    {
        var cutoff = (clock() - LadderRules.SkipWindow).ToUnixTimeSeconds();
        data.Skips.RemoveAll(s => s <= cutoff || s > clock().ToUnixTimeSeconds() + 60);
    }

    /// <summary>How many waits may still be skipped in the rolling hour. The cap is what makes the ladder safe rather than clever.</summary>
    public int SkipsLeft() { Prune(); return Math.Max(0, LadderRules.SkipsPerHour - data.Skips.Count); }

    public void RecordSkip() { Prune(); data.Skips.Add(clock().ToUnixTimeSeconds()); Save(); }

    /// <summary>The stored progress for a lockout, or a fresh one when this is a different lockout from the one stored.</summary>
    public Progress ProgressFor(string lockId, long stamp, LadderRung start)
    {
        if (data.Locks.TryGetValue(lockId, out var p) && p.Stamp == stamp) return p;
        p = new Progress { Stamp = stamp, Rung = start };
        if (data.Locks.Count >= ElementLocks.MaxLocks && !data.Locks.ContainsKey(lockId)) data.Locks.Remove(data.Locks.Keys.First());
        data.Locks[lockId] = p;
        return p;
    }

    public void Save()
    {
        if (path == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the budget then lives for this launch only */ }
    }
}

/// <summary>
/// The games that can end a lockout wait early. The ladder only ever clears the WAITING: it never signs anyone in, never refunds the attempt
/// budget and never slows the lengthening of the waits it skips. Every question is generated here and graded here against a single-use nonce
/// that is consumed before grading. The view only sees questions; the correct answers are not on the challenge objects.
/// </summary>
public sealed class UnlockLadder
{
    private enum Kind { Dish, Sums, Mole }
    private sealed record Pending(string LockId, long Stamp, Kind Kind, DateTimeOffset Issued, DateTimeOffset Expires, object Secret);
    private sealed record DishSecret(int Correct, DishName Target);
    private sealed record MoleSecret(IReadOnlyList<MoleSlot> Moles);

    private readonly LadderLedger ledger;
    private readonly Func<DateTimeOffset> clock;
    private readonly Random rng;
    private readonly Dictionary<string, Pending> pending = new(StringComparer.Ordinal);

    public UnlockLadder(LadderLedger ledger, Func<DateTimeOffset>? clock = null, Random? rng = null)
    {
        this.ledger = ledger; this.clock = clock ?? (() => DateTimeOffset.UtcNow); this.rng = rng ?? new Random();
    }

    public LadderLedger Ledger => ledger;

    private static string NewNonce() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    /// <summary>Whether the ladder is offered for this lockout, and on which rung it starts or resumes.</summary>
    public LadderOffer Offer(string lockId, long lockoutStamp, bool schoolMode)
    {
        var left = ledger.SkipsLeft();
        var start = LadderRules.StartingRung(schoolMode);
        if (lockoutStamp <= 0) return new LadderOffer(false, LadderRung.Clock, left, LadderUnavailable.NotWaiting);
        var progress = ledger.ProgressFor(lockId, lockoutStamp, start);
        var rung = LadderRules.Effective(progress.Rung, schoolMode);
        if (rung == LadderRung.Clock) return new LadderOffer(false, rung, left, LadderUnavailable.FellThisLockout);
        if (left <= 0) return new LadderOffer(false, rung, 0, LadderUnavailable.BudgetSpent);
        return new LadderOffer(true, rung, left, LadderUnavailable.None);
    }

    private (LadderLedger.Progress Progress, LadderRung Rung)? Open(string lockId, long stamp, bool school, LadderRung wanted)
    {
        var offer = Offer(lockId, stamp, school);
        if (!offer.Available || offer.Rung != wanted) return null;
        return (ledger.ProgressFor(lockId, stamp, LadderRules.StartingRung(school)), offer.Rung);
    }

    private string Issue(string lockId, long stamp, Kind kind, TimeSpan life, object secret)
    {
        foreach (var old in pending.Where(p => p.Value.LockId == lockId && p.Value.Kind == kind).Select(p => p.Key).ToList()) pending.Remove(old);
        var now = clock();
        var nonce = NewNonce();
        pending[nonce] = new Pending(lockId, stamp, kind, now, now + life, secret);
        return nonce;
    }

    // ---- Dish ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// One dish, four choices. With a picture the question is "which dish is this"; without one it names the Cantonese name and asks for the
    /// English one, so an offline run never pretends to have a photo. Returns null when this rung is not on offer (including under School mode).
    /// </summary>
    public DishChallenge? NextDish(string lockId, long stamp, bool schoolMode, IReadOnlyList<DishName> pool, bool hasPicture)
    {
        if (Open(lockId, stamp, schoolMode, LadderRung.DimSum) == null) return null;
        var usable = pool.GroupBy(d => d.English, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        if (usable.Count < LadderRules.DishChoices) return null;
        var picks = usable.OrderBy(_ => rng.Next()).Take(LadderRules.DishChoices).ToList();
        var correct = rng.Next(picks.Count);
        var clue = hasPicture ? "" : picks[correct].Cantonese;
        var nonce = Issue(lockId, stamp, Kind.Dish, LadderRules.DishLife, new DishSecret(correct, picks[correct]));
        return new DishChallenge(nonce, picks.Select(p => p.English).ToList(), hasPicture, clue, pending[nonce].Expires);
    }

    /// <summary>Which dish a picture challenge shows, for the view's image loader only. Reading it does not consume the challenge.</summary>
    public DishName? PictureFor(string nonce) => pending.TryGetValue(nonce, out var p) && p.Secret is DishSecret s ? s.Target : null;

    public LadderStep AnswerDish(string lockId, long stamp, bool schoolMode, string nonce, int? choice)
    {
        var take = Consume(lockId, stamp, nonce, Kind.Dish, out var reject, out var spent);
        var progress = ledger.ProgressFor(lockId, stamp, LadderRules.StartingRung(schoolMode));
        if (take == null && reject == LadderReject.UnknownChallenge) return new LadderStep(LadderStepKind.Rejected, progress.Rung, reject, progress.WrongDishes);
        var secret = (DishSecret?)(spent?.Secret);
        if (take != null && (choice is not int pick || pick < 0 || pick >= LadderRules.DishChoices)) reject = LadderReject.WrongKind;
        var right = take != null && secret != null && choice is int c && c == secret.Correct;
        if (right) return Win(lockId, stamp, progress);
        progress.WrongDishes++;
        if (progress.WrongDishes >= LadderRules.WrongDishesAllowed) { progress.Rung = LadderRules.AfterFailure(LadderRung.DimSum); ledger.Save(); return new LadderStep(LadderStepKind.Fell, progress.Rung, reject, progress.WrongDishes); }
        ledger.Save();
        return new LadderStep(LadderStepKind.Retry, LadderRung.DimSum, reject, progress.WrongDishes);
    }

    // ---- Sums ------------------------------------------------------------------------------------------------------------------

    public static (string Text, int Answer) MakeSum(Random random)
    {
        switch (random.Next(3))
        {
            case 0: { var a = random.Next(2, 100); var b = random.Next(2, 100); return ($"{a} + {b}", a + b); }
            case 1: { var a = random.Next(10, 100); var b = random.Next(2, a); return ($"{a} − {b}", a - b); }
            default: { var a = random.Next(2, 10); var b = random.Next(2, 10); return ($"{a} × {b}", a * b); }
        }
    }

    public SumsChallenge? NextSums(string lockId, long stamp, bool schoolMode)
    {
        if (Open(lockId, stamp, schoolMode, LadderRung.Sums) == null) return null;
        var sums = Enumerable.Range(0, LadderRules.SumCount).Select(_ => MakeSum(rng)).ToList();
        var nonce = Issue(lockId, stamp, Kind.Sums, LadderRules.SumsLife, sums.Select(s => s.Answer).ToArray());
        return new SumsChallenge(nonce, sums.Select(s => s.Text).ToList(), pending[nonce].Expires);
    }

    /// <summary>All ten must be right. A single wrong sum, a missing answer or an answer of the wrong kind drops the ladder to whack-a-mole.</summary>
    public LadderStep AnswerSums(string lockId, long stamp, bool schoolMode, string nonce, IReadOnlyList<int>? answers)
    {
        var take = Consume(lockId, stamp, nonce, Kind.Sums, out var reject, out var spent);
        var progress = ledger.ProgressFor(lockId, stamp, LadderRules.StartingRung(schoolMode));
        if (take == null && reject == LadderReject.UnknownChallenge) return new LadderStep(LadderStepKind.Rejected, progress.Rung, reject);
        if (take != null && answers != null && spent!.Secret is int[] expected && answers.Count == expected.Length && expected.SequenceEqual(answers)) return Win(lockId, stamp, progress);
        if (take != null && (answers == null || answers.Count != LadderRules.SumCount)) reject = LadderReject.WrongKind;
        progress.Rung = LadderRules.AfterFailure(LadderRung.Sums); ledger.Save();
        return new LadderStep(LadderStepKind.Fell, progress.Rung, reject);
    }

    // ---- Whack-a-mole ----------------------------------------------------------------------------------------------------------

    public MoleChallenge? NextMole(string lockId, long stamp, bool schoolMode)
    {
        if (Open(lockId, stamp, schoolMode, LadderRung.Mole) == null) return null;
        var moles = new List<MoleSlot>();
        var last = -1;
        for (var i = 0; i < LadderRules.MoleCount; i++)
        {
            var cell = rng.Next(LadderRules.MoleCells - 1); if (cell >= last && last >= 0) cell++;
            moles.Add(new MoleSlot(cell, LadderRules.MoleStart(i), LadderRules.MoleVisibleMs)); last = cell;
        }
        var nonce = Issue(lockId, stamp, Kind.Mole, TimeSpan.FromMilliseconds(LadderRules.MoleRoundMs) + LadderRules.MoleGrace, new MoleSecret(moles));
        return new MoleChallenge(nonce, LadderRules.MoleRoundMs, LadderRules.MoleCells, LadderRules.MoleNeeded, moles, pending[nonce].Expires);
    }

    /// <summary>
    /// Grades a round. A submission that arrives before the round has actually lasted its full duration is refused. A tap counts only against a
    /// mole that was in that cell at that moment, and each mole counts once.
    /// </summary>
    public LadderStep AnswerMole(string lockId, long stamp, bool schoolMode, string nonce, IReadOnlyList<MoleHit>? hits)
    {
        var take = Consume(lockId, stamp, nonce, Kind.Mole, out var reject, out var spent);
        var progress = ledger.ProgressFor(lockId, stamp, LadderRules.StartingRung(schoolMode));
        if (take == null && reject == LadderReject.UnknownChallenge) return new LadderStep(LadderStepKind.Rejected, progress.Rung, reject);
        var counted = 0;
        if (take != null && spent != null)
        {
            if (hits == null) reject = LadderReject.WrongKind;
            else if (clock() - spent.Issued < TimeSpan.FromMilliseconds(LadderRules.MoleRoundMs)) reject = LadderReject.TooEarly;
            else counted = CountHits(((MoleSecret)spent.Secret).Moles, hits);
        }
        if (reject == LadderReject.None && counted >= LadderRules.MoleNeeded) return Win(lockId, stamp, progress, counted);
        progress.Rung = LadderRules.AfterFailure(LadderRung.Mole); ledger.Save();
        return new LadderStep(LadderStepKind.Fell, progress.Rung, reject, 0, counted, LadderRules.MoleNeeded);
    }

    /// <summary>Counts hits against the schedule: right cell, while the mole was up, inside the round, one hit per mole.</summary>
    public static int CountHits(IReadOnlyList<MoleSlot> moles, IReadOnlyList<MoleHit> hits)
    {
        var used = new HashSet<int>();
        foreach (var hit in hits.Where(h => h.AtMs >= 0 && h.AtMs <= LadderRules.MoleRoundMs).OrderBy(h => h.AtMs).Take(500))
            for (var i = 0; i < moles.Count; i++)
                if (!used.Contains(i) && moles[i].Cell == hit.Cell && hit.AtMs >= moles[i].AppearMs && hit.AtMs < moles[i].AppearMs + moles[i].VisibleMs) { used.Add(i); break; }
        return used.Count;
    }

    // ---- Shared ----------------------------------------------------------------------------------------------------------------

    /// <summary>Takes the challenge out of the table before anything is graded, so a wrong answer cannot be retried against it and a right one cannot be replayed.</summary>
    private Pending? Consume(string lockId, long stamp, string nonce, Kind kind, out LadderReject reject, out Pending? spent)
    {
        reject = LadderReject.None; spent = null;
        if (!pending.Remove(nonce, out var p)) { reject = LadderReject.UnknownChallenge; return null; }
        spent = p;
        if (p.LockId != lockId || p.Stamp != stamp || p.Kind != kind) { reject = LadderReject.WrongKind; return null; }
        if (clock() > p.Expires) { reject = LadderReject.Expired; return null; }
        return p;
    }

    private LadderStep Win(string lockId, long stamp, LadderLedger.Progress progress, int hits = 0)
    {
        if (ledger.SkipsLeft() <= 0) return new LadderStep(LadderStepKind.Rejected, progress.Rung, LadderReject.BudgetSpent);
        ledger.RecordSkip();
        progress.Rung = LadderRung.Clock;
        ledger.Save();
        return new LadderStep(LadderStepKind.Cleared, LadderRung.Clock, LadderReject.None, progress.WrongDishes, hits, LadderRules.MoleNeeded);
    }
}
