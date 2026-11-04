using System.Text.RegularExpressions;
using MaterialDockerImage.UI;

/// <summary>Checks for the game ladder (dish, sums, whack-a-mole), its hourly budget and grading, School mode and the dim sum surprise rules.</summary>
public static class LadderChecks
{
    static int count;

    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        count++;
        Console.WriteLine("PASS " + name);
    }

    sealed class Clock
    {
        public DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        public DateTimeOffset Read() => Now;
    }

    static UnlockLadder NewLadder(Clock clock, string? path = null, int seed = 7) => new(new LadderLedger(path, clock.Read), clock.Read, new Random(seed));

    static IReadOnlyList<DishName> Pool => DimSumRules.OfflineNames;

    static int CorrectIndex(UnlockLadder ladder, DishChallenge challenge) => challenge.Choices.ToList().IndexOf(ladder.PictureFor(challenge.Nonce)!.English);

    static int Solve(string text)
    {
        var m = Regex.Match(text, @"^(\d+) ([+−×]) (\d+)$");
        int a = int.Parse(m.Groups[1].Value), b = int.Parse(m.Groups[3].Value);
        return m.Groups[2].Value switch { "+" => a + b, "−" => a - b, _ => a * b };
    }

    static List<MoleHit> PerfectHits(MoleChallenge round, int take) => round.Moles.Take(take).Select(m => new MoleHit(m.Cell, m.AppearMs + 100)).ToList();

    static LadderStep ClearDish(UnlockLadder ladder, string id, long stamp)
    {
        var q = ladder.NextDish(id, stamp, false, Pool, true)!;
        return ladder.AnswerDish(id, stamp, false, q.Nonce, CorrectIndex(ladder, q));
    }

    /// <summary>Drives a ladder from the dish rung to the end by failing every rung, returning the steps.</summary>
    static void FallToClock(Clock clock, UnlockLadder ladder, string id, long stamp)
    {
        for (var i = 0; i < LadderRules.WrongDishesAllowed; i++)
        {
            var q = ladder.NextDish(id, stamp, false, Pool, true)!;
            ladder.AnswerDish(id, stamp, false, q.Nonce, (CorrectIndex(ladder, q) + 1) % 4);
        }
        var sums = ladder.NextSums(id, stamp, false)!;
        ladder.AnswerSums(id, stamp, false, sums.Nonce, sums.Problems.Select(_ => -1).ToList());
        var round = ladder.NextMole(id, stamp, false)!;
        clock.Now += TimeSpan.FromMilliseconds(LadderRules.MoleRoundMs + 50);
        ladder.AnswerMole(id, stamp, false, round.Nonce, new List<MoleHit>());
    }

    public static int Run()
    {
        count = 0;
        Rules();
        Dish();
        Sums();
        Mole();
        Budget();
        School();
        WithLocks();
        SchoolStore();
        Surprise();
        Wording();
        return count;
    }

    static void Rules()
    {
        Check(LadderRules.StartingRung(false) == LadderRung.DimSum && LadderRules.StartingRung(true) == LadderRung.Sums, "the ladder starts at the dish rung, or at the sums under School mode");
        Check(LadderRules.AfterFailure(LadderRung.DimSum) == LadderRung.Sums && LadderRules.AfterFailure(LadderRung.Sums) == LadderRung.Mole && LadderRules.AfterFailure(LadderRung.Mole) == LadderRung.Clock && LadderRules.AfterFailure(LadderRung.Clock) == LadderRung.Clock, "each lost rung drops one step down to the clock and the clock stays the clock");
        Check(LadderRules.Effective(LadderRung.DimSum, true) == LadderRung.Sums && LadderRules.Effective(LadderRung.Mole, true) == LadderRung.Mole && LadderRules.Effective(LadderRung.DimSum, false) == LadderRung.DimSum, "School mode lifts a stored dish rung to the sums and leaves later rungs alone");
        Check(LadderRules.SkipsPerHour == 3 && LadderRules.SumCount == 10 && LadderRules.DishChoices == 4 && LadderRules.WrongDishesAllowed == 5, "the shipped figures are three skips an hour, ten sums, four dishes and five wrong dishes");
        Check(LadderRules.MoleNeeded < LadderRules.MoleCount && LadderRules.MoleSlotMs > LadderRules.MoleVisibleMs && LadderRules.MoleStart(LadderRules.MoleCount - 1) + LadderRules.MoleVisibleMs < LadderRules.MoleRoundMs, "the mole schedule fits inside the round with no two moles up at once");
        var clock = new Clock(); var ladder = NewLadder(clock);
        Check(!ladder.Offer("lock.a", 0, false).Available && ladder.Offer("lock.a", 0, false).Why == LadderUnavailable.NotWaiting, "the ladder is not offered when no wait is running");
        Check(ladder.Offer("lock.a", 5, false) is { Available: true, Rung: LadderRung.DimSum, SkipsLeft: 3 }, "the ladder is offered during a wait with the full budget");
        var shown = typeof(DishChallenge).GetProperties().Concat(typeof(SumsChallenge).GetProperties()).Concat(typeof(MoleChallenge).GetProperties()).Select(p => p.Name.ToLowerInvariant());
        Check(!shown.Any(n => n.Contains("correct") || n.Contains("answer") || n.Contains("secret") || n.Contains("solution")), "no challenge object carries its own answer");
    }

    static void Dish()
    {
        var clock = new Clock(); var ladder = NewLadder(clock);
        var q = ladder.NextDish("lock.a", 100, false, Pool, true)!;
        Check(q.Choices.Count == 4 && q.Choices.Distinct().Count() == 4 && q.HasPicture && q.Clue == "", "a dish question has four distinct choices and a picture");
        var right = CorrectIndex(ladder, q);
        var step = ladder.AnswerDish("lock.a", 100, false, q.Nonce, right);
        Check(step.Cleared && step.Finished && ladder.Ledger.SkipsLeft() == 2, "the right dish clears the wait and spends one skip");
        Check(!ladder.Offer("lock.a", 100, false).Available, "a cleared ladder is finished for that lockout");

        var offline = NewLadder(new Clock()).NextDish("lock.b", 100, false, Pool, false)!;
        Check(!offline.HasPicture && offline.Clue.Length > 0, "offline the question names the Cantonese name instead of showing a photo");
        Check(NewLadder(new Clock()).NextDish("lock.b", 100, false, Pool.Take(3).ToList(), true) == null, "fewer than four distinct dishes cannot make a question");

        var wrongClock = new Clock(); var wrong = NewLadder(wrongClock);
        LadderStep last = null!;
        var kinds = new List<LadderStepKind>();
        for (var i = 0; i < 5; i++)
        {
            var w = wrong.NextDish("lock.c", 200, false, Pool, true)!;
            last = wrong.AnswerDish("lock.c", 200, false, w.Nonce, (CorrectIndex(wrong, w) + 1) % 4);
            kinds.Add(last.Kind);
        }
        Check(kinds.Take(4).All(k => k == LadderStepKind.Retry) && kinds[4] == LadderStepKind.Fell && last.Rung == LadderRung.Sums, "four wrong dishes retry and the fifth falls to the sums");
        Check(wrong.NextDish("lock.c", 200, false, Pool, true) == null && wrong.NextSums("lock.c", 200, false) != null, "after the dish rung is lost only the sums are on offer");

        // A replayed nonce neither clears nor retries.
        var rClock = new Clock(); var replay = NewLadder(rClock);
        var rq = replay.NextDish("lock.d", 300, false, Pool, true)!; var rightIndex = CorrectIndex(replay, rq);
        replay.AnswerDish("lock.d", 300, false, rq.Nonce, (rightIndex + 1) % 4);
        var again = replay.AnswerDish("lock.d", 300, false, rq.Nonce, rightIndex);
        Check(!again.Cleared && again.Kind == LadderStepKind.Rejected && again.Reject == LadderReject.UnknownChallenge, "a spent nonce cannot be replayed with the right answer");
        Check(!replay.AnswerDish("lock.d", 300, false, "00000000000000000000000000000000", 0).Cleared, "an invented nonce is refused");
        var other = replay.NextDish("lock.d", 300, false, Pool, true)!;
        var mismatch = replay.AnswerDish("lock.e", 300, false, other.Nonce, CorrectIndex(replay, other));
        Check(!mismatch.Cleared && mismatch.Reject == LadderReject.WrongKind, "a challenge issued for one lock cannot be answered for another");

        // Expiry counts as a wrong answer and spends the nonce.
        var eClock = new Clock(); var expiry = NewLadder(eClock);
        var eq = expiry.NextDish("lock.f", 400, false, Pool, true)!; var eRight = CorrectIndex(expiry, eq);
        eClock.Now += LadderRules.DishLife + TimeSpan.FromSeconds(1);
        var late = expiry.AnswerDish("lock.f", 400, false, eq.Nonce, eRight);
        Check(!late.Cleared && late.Reject == LadderReject.Expired && late.WrongDishes == 1, "an expired dish challenge is refused even with the right answer and counts as wrong");

        // The wrong-dish count belongs to the lockout, so closing the dialog does not reset it.
        var path = Path.Combine(Path.GetTempPath(), "mdi-ladder-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var pClock = new Clock(); var first = NewLadder(pClock, path);
            for (var i = 0; i < 3; i++) { var w = first.NextDish("lock.g", 500, false, Pool, true)!; first.AnswerDish("lock.g", 500, false, w.Nonce, (CorrectIndex(first, w) + 1) % 4); }
            var second = NewLadder(pClock, path);
            var w2 = second.NextDish("lock.g", 500, false, Pool, true)!; var s2 = second.AnswerDish("lock.g", 500, false, w2.Nonce, (CorrectIndex(second, w2) + 1) % 4);
            Check(s2.WrongDishes == 4, "closing and reopening the ladder keeps the wrong-dish count for the same lockout");
            Check(NewLadder(pClock, path).Offer("lock.g", 501, false).Rung == LadderRung.DimSum, "a new lockout starts the ladder again from the top");
        }
        finally { try { File.Delete(path); } catch (IOException) { } }

        // The wrong kind of answer.
        var kClock = new Clock(); var kind = NewLadder(kClock);
        var kq = kind.NextDish("lock.h", 600, false, Pool, true)!;
        var kStep = kind.AnswerDish("lock.h", 600, false, kq.Nonce, null);
        Check(!kStep.Cleared && kStep.Reject == LadderReject.WrongKind, "an answer of the wrong kind to a dish question is not graded as right");
        var kq2 = kind.NextDish("lock.h", 600, false, Pool, true)!;
        Check(!kind.AnswerDish("lock.h", 600, false, kq2.Nonce, 9).Cleared && !kind.AnswerDish("lock.h", 600, false, kind.NextDish("lock.h", 600, false, Pool, true)!.Nonce, -1).Cleared, "a choice outside the four is never right");
    }

    static void Sums()
    {
        var clock = new Clock(); var ladder = NewLadder(clock);
        var wrongDishes = Enumerable.Range(0, 5).ToList();
        FallDish(ladder, "lock.s", 100);
        var sums = ladder.NextSums("lock.s", 100, false)!;
        Check(sums.Problems.Count == 10 && sums.Problems.All(p => Regex.IsMatch(p, @"^\d+ [+−×] \d+$")), "ten sums of the plain a + b, a − b or a × b form");
        Check(sums.Problems.All(p => Solve(p) is >= 0 and <= 198), "every sum has a small non-negative answer");
        var step = ladder.AnswerSums("lock.s", 100, false, sums.Nonce, sums.Problems.Select(Solve).ToList());
        Check(step.Cleared && ladder.Ledger.SkipsLeft() == 2, "ten right sums clear the wait");

        var failClock = new Clock(); var fail = NewLadder(failClock);
        var cleanSums = fail.NextSums("lock.t", 200, true)!;
        var answers = cleanSums.Problems.Select(Solve).ToList(); answers[9]++;
        var failed = fail.AnswerSums("lock.t", 200, true, cleanSums.Nonce, answers);
        Check(!failed.Cleared && failed.Kind == LadderStepKind.Fell && failed.Rung == LadderRung.Mole, "one wrong sum falls to whack-a-mole");
        var retry = fail.AnswerSums("lock.t", 200, true, cleanSums.Nonce, cleanSums.Problems.Select(Solve).ToList());
        Check(!retry.Cleared && retry.Reject == LadderReject.UnknownChallenge, "the same sums cannot be retried after a wrong answer");

        var shortClock = new Clock(); var shortAnswers = NewLadder(shortClock);
        var ss = shortAnswers.NextSums("lock.u", 300, true)!;
        var shortStep = shortAnswers.AnswerSums("lock.u", 300, true, ss.Nonce, ss.Problems.Select(Solve).Take(9).ToList());
        Check(!shortStep.Cleared && shortStep.Reject == LadderReject.WrongKind && shortStep.Rung == LadderRung.Mole, "nine answers, or none, is the wrong kind of answer and falls");
        var nullClock = new Clock(); var nullAnswers = NewLadder(nullClock);
        var ns = nullAnswers.NextSums("lock.v", 400, true)!;
        Check(!nullAnswers.AnswerSums("lock.v", 400, true, ns.Nonce, null).Cleared, "a missing answer list never clears");

        var expClock = new Clock(); var exp = NewLadder(expClock);
        var es = exp.NextSums("lock.w", 500, true)!;
        expClock.Now += LadderRules.SumsLife + TimeSpan.FromSeconds(1);
        var expired = exp.AnswerSums("lock.w", 500, true, es.Nonce, es.Problems.Select(Solve).ToList());
        Check(!expired.Cleared && expired.Reject == LadderReject.Expired, "expired sums are refused even when every answer is right");
        Check(exp.NextDish("lock.w", 500, true, Pool, true) == null && exp.NextSums("lock.w", 500, true) == null && exp.NextMole("lock.w", 500, true) != null, "only the rung the ladder is on can be asked for");
    }

    static void FallDish(UnlockLadder ladder, string id, long stamp)
    {
        for (var i = 0; i < LadderRules.WrongDishesAllowed; i++)
        {
            var q = ladder.NextDish(id, stamp, false, Pool, true)!;
            ladder.AnswerDish(id, stamp, false, q.Nonce, (CorrectIndex(ladder, q) + 1) % 4);
        }
    }

    /// <summary>Gets a School-mode ladder to the mole rung by losing the sums, then asks for a round.</summary>
    static MoleChallenge ToMole(UnlockLadder ladder, string id, long stamp)
    {
        var sums = ladder.NextSums(id, stamp, true)!;
        ladder.AnswerSums(id, stamp, true, sums.Nonce, new List<int>());
        return ladder.NextMole(id, stamp, true)!;
    }

    static void Mole()
    {
        var clock = new Clock(); var ladder = NewLadder(clock);
        var round = ToMole(ladder, "lock.m", 100);
        Check(round.Moles.Count == 14 && round.Needed == 8 && round.DurationMs == 20_000 && round.Moles.All(m => m.Cell is >= 0 and < 9), "a round has fourteen moles on a three by three board and needs eight hits");
        Check(round.Moles.Zip(round.Moles.Skip(1)).All(pair => pair.First.Cell != pair.Second.Cell && pair.First.AppearMs + pair.First.VisibleMs <= pair.Second.AppearMs), "one mole at a time and never the same cell twice running");
        Check(LadderRules.MoleNeeded == 8 && UnlockLadder.CountHits(round.Moles, PerfectHits(round, 14)) == 14, "tapping every mole while it is up counts every mole");
        Check(UnlockLadder.CountHits(round.Moles, round.Moles.Select(m => new MoleHit((m.Cell + 1) % 9, m.AppearMs + 100)).ToList()) == 0, "taps on an empty cell count for nothing");
        Check(UnlockLadder.CountHits(round.Moles, round.Moles.Select(m => new MoleHit(m.Cell, m.AppearMs - 1)).ToList()) == 0 && UnlockLadder.CountHits(round.Moles, round.Moles.Select(m => new MoleHit(m.Cell, m.AppearMs + m.VisibleMs)).ToList()) == 0, "a tap before the mole is up, or the moment it goes down, does not count");
        Check(UnlockLadder.CountHits(round.Moles, round.Moles.Select(m => new MoleHit(m.Cell, 25_000)).ToList()) == 0 && UnlockLadder.CountHits(round.Moles, new[] { new MoleHit(round.Moles[0].Cell, -5) }) == 0, "taps outside the round count for nothing");
        var spam = Enumerable.Range(0, 200).Select(_ => new MoleHit(round.Moles[0].Cell, round.Moles[0].AppearMs + 50)).ToList();
        Check(UnlockLadder.CountHits(round.Moles, spam) == 1, "the same cell tapped a hundred times counts once per mole");

        // Early submission is refused even with a perfect score.
        var early = ladder.AnswerMole("lock.m", 100, true, round.Nonce, PerfectHits(round, 14));
        Check(!early.Cleared && early.Reject == LadderReject.TooEarly && early.Rung == LadderRung.Clock, "a perfect score sent before the round could have finished is refused and ends the ladder");

        var lateClock = new Clock(); var late = NewLadder(lateClock);
        var lr = ToMole(late, "lock.n", 200);
        lateClock.Now += TimeSpan.FromMilliseconds(LadderRules.MoleRoundMs);
        var won = late.AnswerMole("lock.n", 200, true, lr.Nonce, PerfectHits(lr, 8));
        Check(won.Cleared && won.Hits == 8 && late.Ledger.SkipsLeft() == 2, "eight hits after the full round clear the wait and spend one skip");

        var lossClock = new Clock(); var loss = NewLadder(lossClock);
        var lo = ToMole(loss, "lock.o", 300);
        lossClock.Now += TimeSpan.FromMilliseconds(LadderRules.MoleRoundMs + 1);
        var lost = loss.AnswerMole("lock.o", 300, true, lo.Nonce, PerfectHits(lo, 7));
        Check(!lost.Cleared && lost.Kind == LadderStepKind.Fell && lost.Rung == LadderRung.Clock && lost.Hits == 7, "seven hits is a lost round and falls to the clock");
        Check(loss.NextMole("lock.o", 300, true) == null && loss.NextSums("lock.o", 300, true) == null && loss.Offer("lock.o", 300, true) is { Available: false, Why: LadderUnavailable.FellThisLockout }, "the ladder is not offered again for a lockout once the clock is reached");
        Check(loss.Ledger.SkipsLeft() == 3, "a lost ladder spends no skip");

        var nClock = new Clock(); var nullHits = NewLadder(nClock);
        var nr = ToMole(nullHits, "lock.p", 400);
        nClock.Now += TimeSpan.FromSeconds(30);
        var nullStep = nullHits.AnswerMole("lock.p", 400, true, nr.Nonce, null);
        Check(!nullStep.Cleared && nullStep.Reject == LadderReject.WrongKind, "a missing hit list is the wrong kind of answer");

        var xClock = new Clock(); var expire = NewLadder(xClock);
        var xr = ToMole(expire, "lock.q", 500);
        xClock.Now += TimeSpan.FromMilliseconds(LadderRules.MoleRoundMs) + LadderRules.MoleGrace + TimeSpan.FromSeconds(1);
        Check(expire.AnswerMole("lock.q", 500, true, xr.Nonce, PerfectHits(xr, 14)).Reject == LadderReject.Expired, "a round answered long after it ended has expired");
        var replayClock = new Clock(); var replay = NewLadder(replayClock);
        var rr = ToMole(replay, "lock.r", 600);
        replayClock.Now += TimeSpan.FromSeconds(21);
        var firstTry = replay.AnswerMole("lock.r", 600, true, rr.Nonce, PerfectHits(rr, 14));
        Check(firstTry.Cleared && !replay.AnswerMole("lock.r", 600, true, rr.Nonce, PerfectHits(rr, 14)).Cleared && replay.Ledger.SkipsLeft() == 2, "a won round cannot be replayed to spend another skip");
    }

    static void Budget()
    {
        var clock = new Clock(); var ladder = NewLadder(clock);
        for (var i = 1; i <= 3; i++) Check(ClearDish(ladder, "lock.b", i * 10).Cleared, "skip number " + i + " in the hour works");
        Check(ladder.Ledger.SkipsLeft() == 0 && ladder.Offer("lock.b", 99, false) is { Available: false, Why: LadderUnavailable.BudgetSpent, SkipsLeft: 0 }, "after three skips in an hour the ladder is no longer offered");
        Check(ladder.NextDish("lock.b", 99, false, Pool, true) == null && ladder.NextSums("lock.b", 99, false) == null, "no question is made once the budget is spent");
        clock.Now += TimeSpan.FromMinutes(59);
        Check(ladder.Ledger.SkipsLeft() == 0, "fifty-nine minutes later the budget is still spent");
        clock.Now += TimeSpan.FromMinutes(2);
        Check(ladder.Ledger.SkipsLeft() == 3 && ladder.Offer("lock.b", 99, false).Available, "after an hour the budget refills in full");
        clock.Now += TimeSpan.FromSeconds(1);

        var rolling = new Clock(); var roll = NewLadder(rolling);
        ClearDish(roll, "lock.c", 1); rolling.Now += TimeSpan.FromMinutes(30); ClearDish(roll, "lock.c", 2); rolling.Now += TimeSpan.FromMinutes(20); ClearDish(roll, "lock.c", 3);
        rolling.Now += TimeSpan.FromMinutes(11);
        Check(roll.Ledger.SkipsLeft() == 1, "the budget is a rolling hour: only the oldest skip has aged out");

        // The cap holds even when a question was issued while a skip was still available.
        var race = new Clock(); var racing = NewLadder(race);
        var held = racing.NextDish("lock.d", 5, false, Pool, true)!; var heldRight = CorrectIndex(racing, held);
        racing.Ledger.RecordSkip(); racing.Ledger.RecordSkip(); racing.Ledger.RecordSkip();
        var refused = racing.AnswerDish("lock.d", 5, false, held.Nonce, heldRight);
        Check(!refused.Cleared && refused.Reject == LadderReject.BudgetSpent, "a question held open past the budget cannot still clear a wait");

        var path = Path.Combine(Path.GetTempPath(), "mdi-ladder-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var kept = new Clock(); var first = NewLadder(kept, path);
            ClearDish(first, "lock.e", 1); ClearDish(first, "lock.e", 2);
            Check(NewLadder(kept, path).Ledger.SkipsLeft() == 1, "spent skips survive a restart");
            File.WriteAllText(path, "{ not json");
            Check(NewLadder(kept, path).Ledger.SkipsLeft() == 3, "a damaged ladder file starts with a fresh budget and does not crash");
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    static void School()
    {
        var clock = new Clock(); var ladder = NewLadder(clock);
        var offer = ladder.Offer("lock.a", 10, true);
        Check(offer.Available && offer.Rung == LadderRung.Sums, "under School mode the first rung on offer is the sums");
        Check(ladder.NextDish("lock.a", 10, true, Pool, true) == null && ladder.NextDish("lock.a", 10, true, Pool, false) == null, "under School mode no dish question is ever made, picture or not");
        Check(ladder.NextSums("lock.a", 10, true) != null, "under School mode the sums are asked first");
        var turnedOn = NewLadder(new Clock());
        var q = turnedOn.NextDish("lock.b", 11, false, Pool, true);
        Check(q != null && turnedOn.Offer("lock.b", 11, true).Rung == LadderRung.Sums && turnedOn.NextDish("lock.b", 11, true, Pool, true) == null, "turning School mode on mid-lockout removes the dish rung at once");
        var copy = string.Join("|", typeof(LadderRules).GetFields().Select(f => f.Name));
        Check(!copy.Contains("School", StringComparison.OrdinalIgnoreCase), "the rules do not name School mode anywhere a surface could print it");
    }

    static void WithLocks()
    {
        var clock = new Clock();
        var store = new LockStore(null, null, clock.Read, 10_000);
        store.Configure("lock.pin", LockPolicy.Pin, "482913");
        for (var i = 0; i < 3; i++) store.Attempt("lock.pin", "000000");
        Check(store.Stage("lock.pin") == LadderStage.Wait && store.LockoutStamp("lock.pin") > 0, "three wrong PINs start a wait and the wait has a stamp");
        var failuresBefore = store.Get("lock.pin")!.Failures;
        var stamp = store.LockoutStamp("lock.pin");
        var ladder = NewLadder(clock);
        FallDishAndWin(ladder, "lock.pin", stamp);
        Check(store.ClearWaitByLadder("lock.pin") && store.Stage("lock.pin") == LadderStage.Credential && store.WaitUntil("lock.pin") == null, "a won game ends the wait and returns to the credential prompt");
        Check(store.Get("lock.pin")!.Failures == failuresBefore, "winning does not refund the attempt count");
        var nothing = store.Attempt("lock.pin", null);
        Check(nothing.Outcome == UnlockOutcome.Wrong && !nothing.Unlocked, "a cleared ladder signs nobody in: an empty answer is still wrong");
        Check(store.Get("lock.pin")!.Failures == failuresBefore + 1 && store.WaitUntil("lock.pin") is { } next && next - clock.Now == AttemptLimiter.WaitAfter(failuresBefore + 1), "the next wrong answer still earns the longer wait the ladder skipped past");
        Check(AttemptLimiter.WaitAfter(failuresBefore + 1) > AttemptLimiter.WaitAfter(failuresBefore), "the escalation keeps lengthening after a skip");
        Check(store.UseRecoveryCode("lock.pin", "ZZZZZ-ZZZZZ").Outcome == UnlockOutcome.Wrong && store.PolicyOf("lock.pin") == LockPolicy.Pin && store.HasPolicy("lock.pin"), "the lock keeps its policy and its recovery codes after a cleared ladder");
        clock.Now += TimeSpan.FromHours(2);
        Check(store.Attempt("lock.pin", "482913").Unlocked, "the PIN is still what unlocks, once the credential rung is open");
        Check(!store.ClearWaitByLadder("lock.pin") && !store.ClearWaitByLadder("missing") && !new LockStore(null, null).ClearWaitByLadder("lock.none"), "with no wait running there is nothing for the ladder to clear");
        store.Configure("lock.confirm", LockPolicy.None);
        Check(!store.ClearWaitByLadder("lock.confirm") && store.LockoutStamp("lock.none") == 0, "a confirm-only lock has no wait to clear");
    }

    static void FallDishAndWin(UnlockLadder ladder, string id, long stamp)
    {
        var step = ClearDish(ladder, id, stamp);
        Check(step.Cleared, "the dish rung is won for the locked-out lock");
    }

    sealed class StubPath : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mdi-school-" + Guid.NewGuid().ToString("N"), "school-mode.json");
        public void Dispose() { try { Directory.Delete(System.IO.Path.GetDirectoryName(Path)!, true); } catch (IOException) { } }
    }

    static void SchoolStore()
    {
        Check(SchoolModeRules.DefaultName == "School mode" && SchoolModeRules.DisplayName("Homework time") == "Homework time" && SchoolModeRules.DisplayName("  ") == "School mode" && SchoolModeRules.DisplayName(null) == "School mode", "a renamed mode always shows its chosen name");
        Check(SchoolModeRules.NameProblem("") != null && SchoolModeRules.NameProblem(new string('x', 41)) != null && SchoolModeRules.NameProblem("bad\nname") != null && SchoolModeRules.NameProblem("Focus") == null, "names are one to forty printable characters");
        Check(SchoolModeRules.KindOf("4829") == SchoolSecretKind.Pin && SchoolModeRules.KindOf("pass word") == SchoolSecretKind.Password && SchoolModeRules.KindOf("12") == SchoolSecretKind.None && SchoolModeRules.KindOf(new string('a', 65)) == SchoolSecretKind.None && SchoolModeRules.KindOf(null) == SchoolSecretKind.None, "the secret is a 4 to 12 digit PIN or a 6 to 64 character password");

        using var scratch = new StubPath();
        var clock = new Clock();
        var store = new SchoolModeStore(scratch.Path, clock.Read, 10_000);
        Check(!store.Enabled && !store.HasSecret && store.Name == "School mode" && !File.Exists(scratch.Path), "a fresh mode is off, unnamed and has no secret");
        store.Enable();
        Check(store.Enabled && File.Exists(scratch.Path), "turning the mode on needs no secret and writes the shared record");
        Check(store.TryTurnOff(null) == SchoolOffOutcome.TurnedOff && store.TryTurnOff(null) == SchoolOffOutcome.AlreadyOff, "with no secret set the mode turns off freely");
        store.Enable();
        Check(store.SetSecret("482913") && store.HasSecret, "a PIN can be set");
        var text = File.ReadAllText(scratch.Path);
        Check(!text.Contains("482913") && text.Contains("pbkdf2-sha256"), "the PIN is stored only as a salted PBKDF2 hash");
        Check(store.TryTurnOff("111111") == SchoolOffOutcome.Wrong && store.Enabled && store.TryTurnOff(null) == SchoolOffOutcome.Wrong && store.Enabled, "a wrong or empty secret leaves the mode on");
        Check(store.TryTurnOff("222222") == SchoolOffOutcome.Wrong && store.WaitUntil != null && store.TryTurnOff("482913") == SchoolOffOutcome.Waiting && store.Enabled, "repeated wrong answers start a wait during which even the right PIN is refused");
        clock.Now += TimeSpan.FromHours(2);
        Check(store.TryTurnOff("482913") == SchoolOffOutcome.TurnedOff && !store.Enabled, "the right PIN turns the mode off after the wait");
        Check(!store.SetSecret("anotherpassword", "wrong") && store.SetSecret("anotherpassword", "482913") && store.TryTurnOff("x") == SchoolOffOutcome.AlreadyOff, "replacing the secret needs the old one");
        bool refused; try { store.SetSecret("12"); refused = false; } catch (ArgumentException) { refused = true; }
        Check(refused, "a secret that is neither a PIN nor a password is refused");
        store.Rename("Homework"); store.Enable();
        Check(store.Name == "Homework", "the mode can be renamed");
        bool badName; try { store.Rename("\u0007"); badName = false; } catch (ArgumentException) { badName = true; }
        Check(badName && store.Name == "Homework", "a bad name is refused and the old name stays");

        var other = new SchoolModeStore(scratch.Path, clock.Read, 10_000);
        Check(other.Enabled && other.Name == "Homework" && other.HasSecret, "a second app reads the same shared record");
        Check(!other.ReloadIfChanged(), "nothing has changed so nothing reloads");
        Thread.Sleep(30);
        store.TryTurnOff("anotherpassword");
        File.SetLastWriteTimeUtc(scratch.Path, DateTime.UtcNow.AddSeconds(5));
        Check(other.ReloadIfChanged() && !other.Enabled, "a running app notices another app turning the mode off, live");
        other.Enable();
        File.SetLastWriteTimeUtc(scratch.Path, DateTime.UtcNow.AddSeconds(10));
        Check(store.ReloadIfChanged() && store.Enabled, "and notices it being turned on again");

        File.Delete(scratch.Path);
        Check(store.ReloadIfChanged() && !store.Enabled && !store.HasSecret, "deleting the record resets the mode, the documented last resort");
        File.WriteAllText(scratch.Path, "{ nope");
        var broken = new SchoolModeStore(scratch.Path, clock.Read, 10_000);
        Check(!broken.Enabled && broken.LoadError != null, "an unreadable record is treated as off and says why");
        broken.Reset();
        Check(!File.Exists(scratch.Path) && !broken.Enabled, "reset removes the record");
        var legacy = new SchoolModeStore(scratch.Path, clock.Read, 10_000);
        legacy.AdoptLegacy(true);
        Check(legacy.Enabled && new SchoolModeStore(scratch.Path, clock.Read, 10_000).Enabled, "a mode an older build kept only in settings is adopted into the shared record");
        var memory = new SchoolModeStore(null, null, 10_000); memory.Enable(); memory.SetSecret("123456");
        Check(memory.Enabled && memory.TryTurnOff("123456") == SchoolOffOutcome.TurnedOff, "a store with no path works in memory");
        Check(SchoolModeRules.Disclosure.Contains("not security"), "the disclosure says it is not security");
    }

    static void Surprise()
    {
        Check(DimSumRules.Chance == 0.10 && DimSumRules.Wins(0) && DimSumRules.Wins(0.0999) && !DimSumRules.Wins(0.10) && !DimSumRules.Wins(0.5) && !DimSumRules.Wins(-0.1), "the draw wins below ten in a hundred");
        var random = new Random(42); var wins = Enumerable.Range(0, 100_000).Count(_ => DimSumRules.Wins(random.NextDouble()));
        Check(wins is > 9_500 and < 10_500, "a hundred thousand fresh draws win about ten percent of the time");
        var ready = new SurpriseContext(true, false, false, false, false, false, false, false, false);
        Check(DimSumRules.MayAppear(ready), "a settled idle app may show the surprise");
        Check(!DimSumRules.MayAppear(ready with { SetupComplete = false }) && !DimSumRules.MayAppear(ready with { FirstRun = true }), "never during first-run setup");
        Check(!DimSumRules.MayAppear(ready with { ErrorShown = true }) && !DimSumRules.MayAppear(ready with { UpdatePending = true }) && !DimSumRules.MayAppear(ready with { Busy = true }), "never during an error, an update or a running operation");
        Check(!DimSumRules.MayAppear(ready with { SchoolMode = true }) && !DimSumRules.MayAppear(ready with { QuietHours = true }) && !DimSumRules.MayAppear(ready with { Capture = true }), "never under School mode, quiet hours or a capture run");
        Check(!DimSumRules.MayAppear(ready with { AlreadyShownThisLaunch = true }), "never twice in one launch");
        Check(!typeof(UserSettings).GetProperties().Any(p => p.Name.Contains("DimSum", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Surprise", StringComparison.OrdinalIgnoreCase)), "there is no setting that switches the surprise off");

        var json = """
        {"dishes":[
          {"id":"hk-dish-0001","name":{"en":"Classic Har Gow","zhHant":"蝦餃"},"image":{"path":"images/hk-dish-0001-classic-har-gow.png","alt":{"en":"A bamboo steamer of har gow","yue":"一籠蝦餃"}}},
          {"id":"hk-dish-0002","name":{"en":"No Cantonese Name"},"image":{"path":"images/x.png"}},
          {"id":"hk-dish-0003","name":{"en":"No Image","zhHant":"冇圖"}},
          {"id":"hk-dish-0004","name":{"en":"Wrong Format","zhHant":"錯"},"image":{"path":"images/y.jpg"}},
          {"id":"hk-dish-0005","name":{"en":"Sneaky","zhHant":"偷"},"image":{"path":"images/..%2f..%2fx:y.png"}}
        ]}
        """;
        var dishes = DimSumRules.ParseCatalog(json);
        Check(dishes.Count == 2 && dishes[0].Id == "hk-dish-0001" && dishes[0].ImageFile == "hk-dish-0001-classic-har-gow.png", "the catalogue yields only dishes with both names and a PNG, using the catalogue's own names");
        Check(DimSumRules.ParseCatalog("not json").Count == 0 && DimSumRules.ParseCatalog("{}").Count == 0 && DimSumRules.ParseCatalog("{\"dishes\":5}").Count == 0, "damaged catalogues give no dishes instead of throwing");
        var urls = DimSumRules.ImageUrls(dishes[0]);
        Check(urls.Count == 3 && urls[0] == "https://github.com/Ding-Ding-Projects/dim-sum-photos/releases/download/catalog-v1/hk-dish-0001-classic-har-gow.png" && urls.All(u => u.StartsWith("https://github.com/Ding-Ding-Projects/dim-sum-photos/releases/download/catalog-v")), "pictures come only from the public catalogue's release assets");
        Check(DimSumRules.ImageUrls(new DimSumDish("a", "A", "甲", "../x.png", "", "")).Count == 0 && DimSumRules.ImageUrls(new DimSumDish("a", "A", "甲", "", "", "")).Count == 0, "a file name that could leave the release path gets no address");
        Check(DimSumRules.Caption(dishes[0], "English") == "Classic Har Gow" && DimSumRules.Caption(dishes[0], "Cantonese") == "蝦餃" && DimSumRules.Caption(dishes[0], "Bilingual") == "Classic Har Gow · 蝦餃", "the dish is named in the active language mode");
        Check(DimSumRules.Alt(dishes[0], "English") == "A bamboo steamer of har gow" && DimSumRules.Alt(dishes[0], "Cantonese") == "一籠蝦餃" && DimSumRules.Alt(new DimSumDish("a", "A", "甲", "a.png", "", ""), "English") == "A", "the picture's alternative text names the dish");
        var cache = DimSumRules.ToCache(dishes, "etag-1", DateTimeOffset.FromUnixTimeSeconds(5));
        var back = DimSumRules.FromCache(cache);
        Check(back is { Revision: "etag-1" } && back.Value.Dishes.Count == 2 && cache.Contains(DimSumRules.CatalogUrl), "the small cache records its source URL and revision and reads back");
        Check(DimSumRules.FromCache(cache.Replace(DimSumRules.CatalogUrl, "https://example.invalid/x")) == null && DimSumRules.FromCache("{") == null && DimSumRules.FromCache("{}") == null, "a cache from another source, or a damaged one, is ignored");
        Check(DimSumRules.LooksLikePng(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }) && !DimSumRules.LooksLikePng(new byte[40]) && !DimSumRules.LooksLikePng(new byte[3]), "a downloaded picture must start with the PNG signature");
        Check(DimSumRules.OfflineNames.Select(n => n.English).Distinct().Count() >= 8 && DimSumRules.OfflineNames.All(n => n.Cantonese.Length > 0), "the offline name set has enough distinct dishes for a question");
    }

    static void Wording()
    {
        Check(LadderRules.MoleIntro.Contains("at least " + LadderRules.MoleNeeded + " of " + LadderRules.MoleCount + " in " + LadderRules.MoleRoundMs / 1000 + " seconds"), "the on-screen mole rules state the real numbers");
        var keys = new[]
        {
            "Play to shorten the wait", "Games left this hour:", "The games are over for this wait. The clock is the way through.", "Name the dish", "Ten sums", "Whack-a-mole", "Start the round", "MOLE", "Check my answers",
            "Which dish is this?", "Fill in all ten sums with whole numbers.", LadderRules.MoleIntro, "Name of this mode", "PIN or password to turn it off", "Turn off", "Dim sum surprise",
            SchoolModeRules.Disclosure, "Enter a name.", "Use a PIN of 4 to 12 digits, or a password of 6 to 64 characters.", "Too many wrong answers. Try again at", "Rename this mode"
        };
        MaterialDockerImage.Services.Loc.Language = "Cantonese";
        var missing = keys.Where(k => MaterialDockerImage.Services.Loc.T(k) == k).ToList();
        Check(missing.Count == 0, "ladder, School mode and surprise strings have Cantonese wording" + (missing.Count > 0 ? ": " + string.Join(" | ", missing) : ""));
        MaterialDockerImage.Services.Loc.Language = "Bilingual";
        Check(MaterialDockerImage.Services.Loc.T("Name the dish").StartsWith("Name the dish · "), "bilingual mode keeps English then Cantonese for the ladder");
        MaterialDockerImage.Services.Loc.Language = "English";
        MaterialDockerImage.Services.Loc.SchoolMode = true;
        MaterialDockerImage.Services.Loc.Language = "Cantonese";
        Check(MaterialDockerImage.Services.Loc.T("Name the dish") == "Name the dish", "School mode keeps the ladder in English");
        MaterialDockerImage.Services.Loc.SchoolMode = false;
        MaterialDockerImage.Services.Loc.Language = "English";
    }
}
