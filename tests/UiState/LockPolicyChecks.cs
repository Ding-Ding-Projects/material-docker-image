using MaterialDockerImage.UI;
using Loc = MaterialDockerImage.Services.Loc;

/// <summary>
/// Checks for the lock catalogue additions: the password policy, combined policies, unlock durations, locks on arbitrary elements with the Locks manager,
/// and the Support Tickets desk. Logic only: nothing here drives the built window.
/// </summary>
public static class LockPolicyChecks
{
    static int count;

    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        count++;
        Console.WriteLine("PASS " + name);
    }

    static void Reject(Action action, string name)
    {
        try { action(); }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException) { count++; Console.WriteLine("PASS " + name); return; }
        throw new Exception("Expected refusal: " + name);
    }

    sealed class XorProtector : ISecretProtector
    {
        public bool Fail;
        static byte[] Key(byte[] entropy) => System.Security.Cryptography.SHA256.HashData(entropy);
        public byte[] Protect(byte[] plain, byte[] entropy) { var k = Key(entropy); return new byte[] { 0x46 }.Concat(plain.Select((b, i) => (byte)(b ^ k[i % k.Length]))).ToArray(); }
        public byte[]? Unprotect(byte[] protectedBytes, byte[] entropy) { if (Fail || protectedBytes.Length < 2 || protectedBytes[0] != 0x46) return null; var k = Key(entropy); return protectedBytes.Skip(1).Select((b, i) => (byte)(b ^ k[i % k.Length])).ToArray(); }
    }

    public static int Run()
    {
        count = 0;
        Policies();
        PasswordAndCombined();
        Durations();
        Elements();
        Manager();
        Tickets();
        Wording();
        return count;
    }

    static void Policies()
    {
        Check((int)LockPolicy.Pin == 1 && (int)LockPolicy.Authenticator == 2, "stored policy numbers of the existing policies never change");
        Check(LockPolicies.Choosable.Count == 8 && LockPolicies.Choosable.Distinct().Count() == 8, "the wizard offers eight distinct policies");
        var order = new (LockPolicy Policy, LockFactors[] Steps)[]
        {
            (LockPolicy.Pin, [LockFactors.Pin]), (LockPolicy.Password, [LockFactors.Password]), (LockPolicy.Authenticator, [LockFactors.Totp]),
            (LockPolicy.PinPassword, [LockFactors.Pin, LockFactors.Password]), (LockPolicy.PasswordTotp, [LockFactors.Password, LockFactors.Totp]),
            (LockPolicy.PinTotp, [LockFactors.Pin, LockFactors.Totp]), (LockPolicy.PasswordPinTotp, [LockFactors.Pin, LockFactors.Password, LockFactors.Totp])
        };
        Check(order.All(o => LockPolicies.Steps(o.Policy).SequenceEqual(o.Steps)), "every policy asks its factors in the order PIN, password, authenticator");
        Check(LockPolicies.Steps(LockPolicy.None).Count == 0 && !LockPolicies.IsMultiFactor(LockPolicy.Password) && LockPolicies.IsMultiFactor(LockPolicy.PinTotp), "a confirm-only lock has no steps and only combined policies are multi-factor");
        Check(LockPolicies.Choosable.All(p => LockPolicies.From(LockPolicies.Factors(p)) == p), "a set of factors maps back to exactly one policy");
        Check(PasswordRules.IsValid("correct horse") && !PasswordRules.IsValid("short") && !PasswordRules.IsValid(null) && !PasswordRules.IsValid(new string('a', 129)) && !PasswordRules.IsValid("tab\there1234"), "password rules bound the length and refuse control characters");
    }

    static LockStore Store(string? path, ISecretProtector? protector, Func<DateTimeOffset> clock) => new(path, protector, clock, 10_000);

    static void PasswordAndCombined()
    {
        var folder = Path.Combine(Path.GetTempPath(), "mdi-lockpolicy-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "locks.json");
        var clock = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var protector = new XorProtector();
        try
        {
            var store = Store(path, protector, () => clock);
            var codes = store.Configure("pw.only", LockPolicy.Password, null, null, "violet umbrella 7")!;
            var text = File.ReadAllText(path);
            Check(codes.Count == 8 && store.Get("pw.only")!.PasswordHash != null && store.Get("pw.only")!.PinHash == null, "a password lock keeps a hash and makes recovery codes");
            Check(!text.Contains("violet umbrella 7") && !text.Contains("umbrella"), "the password is never written to the lock file");
            Check(store.Get("pw.only")!.PasswordHash!.StartsWith("pbkdf2-sha256$", StringComparison.Ordinal), "the password is stored as a salted PBKDF2 hash");
            Check(store.Stage("pw.only") == LadderStage.Credential && store.Attempt("pw.only", "violet umbrella 7").Unlocked, "the right password unlocks");
            var wrong = store.Attempt("pw.only", "violet umbrella 8");
            Check(wrong.Outcome == UnlockOutcome.Wrong && wrong.FreeAttemptsLeft == 1 && store.Get("pw.only")!.Failures == 1, "a wrong password costs a free try and is counted");
            Check(!store.Attempt("pw.only", null).Unlocked && !store.Attempt("pw.only", "").Unlocked, "an empty or missing password never unlocks");
            store.Attempt("pw.only", "nope nope nope");
            Check(store.Attempt("pw.only", "nope again!").WaitUntil == clock.AddSeconds(30) && store.Attempt("pw.only", "violet umbrella 7").Outcome == UnlockOutcome.Waiting, "password attempts share the wait ladder and the right password is refused during a wait");
            Reject(() => store.Configure("pw.short", LockPolicy.Password, null, null, "short"), "a short password is refused");
            Check(!store.HasPolicy("pw.short"), "a refused password stores nothing");
            Reject(() => store.Configure("pw.pin", LockPolicy.PinPassword, "123456", null, null), "a PIN plus password policy needs the password");
            Reject(() => store.Configure("pw.pin2", LockPolicy.PinPassword, null, null, "long enough pw"), "a PIN plus password policy needs the PIN");
            Reject(() => store.Configure("pw.t", LockPolicy.PasswordTotp, null, null, "long enough pw"), "a password plus authenticator policy needs the secret");
            Reject(() => store.Configure("pw.t2", LockPolicy.PasswordPinTotp, "123456", null, "long enough pw"), "the three-factor policy needs the secret too");
            Reject(() => store.Configure("pw.t3", LockPolicy.PasswordTotp, null, Totp.NewSecret(), null), "a policy missing a credential stores nothing");
            Check(!store.HasPolicy("pw.t3") && !store.HasPolicy("pw.t2"), "a refused combined policy leaves no record");
            var noVault = Store(null, null, () => clock);
            Reject(() => noVault.Configure("pw.novault", LockPolicy.PasswordTotp, null, Totp.NewSecret(), "long enough pw"), "an authenticator factor is refused when no secret protection exists");

            // Combined policy: PIN plus password
            store.Configure("combo.two", LockPolicy.PinPassword, "482913", null, "violet umbrella 7");
            var attempt = store.Begin("combo.two")!;
            Check(attempt.Steps.SequenceEqual(new[] { LockFactors.Pin, LockFactors.Password }) && attempt.Next == LockFactors.Pin, "a combined attempt starts at the first factor");
            Check(store.Attempt("combo.two", "482913").Outcome == UnlockOutcome.Wrong && store.Get("combo.two")!.Failures == 0, "one value cannot answer a combined policy and the refusal is not counted");
            var step1 = attempt.Submit("482913");
            Check(step1.Outcome == UnlockOutcome.NextFactor && !step1.Unlocked && attempt.Next == LockFactors.Password && !attempt.Complete, "the right PIN moves on to the password without unlocking");
            var step2 = attempt.Submit("violet umbrella 7");
            Check(step2.Unlocked && attempt.Complete, "the right PIN and the right password unlock");

            var second = store.Begin("combo.two")!;
            second.Submit("482913");
            var failed = second.Submit("wrong password");
            Check(failed.Outcome == UnlockOutcome.Wrong && second.Index == 0 && second.Next == LockFactors.Pin && store.Get("combo.two")!.Failures == 1, "a wrong second factor counts once and sends the attempt back to the first factor");
            var third = store.Begin("combo.two")!;
            Check(third.Submit("000000").Outcome == UnlockOutcome.Wrong && third.Next == LockFactors.Pin && store.Get("combo.two")!.Failures == 2, "a wrong first factor counts against the same budget");
            Check(store.Begin("combo.two")!.Submit("111111").WaitUntil != null && store.Stage("combo.two") == LadderStage.Wait, "the shared budget starts a wait after the free tries");
            var during = store.Begin("combo.two")!;
            Check(during.Submit("482913").Outcome == UnlockOutcome.Waiting && during.Index == 0, "during a wait even the right first factor is refused and not counted");
            Check(store.UseRecoveryCode("combo.two", "ABCDE-FGHJK").Outcome == UnlockOutcome.Wrong, "a made-up recovery code does not clear a combined lock");
            clock = clock.AddSeconds(31);
            Check(store.Begin("combo.two")!.Submit("482913").Outcome == UnlockOutcome.NextFactor, "after the wait the first factor is accepted again");
            var progress = store.Begin("combo.two")!;
            progress.Submit("482913");
            var reopenedProgress = Store(path, protector, () => clock).Begin("combo.two")!;
            Check(reopenedProgress.Index == 0 && reopenedProgress.Next == LockFactors.Pin, "an attempt abandoned half way is not remembered: a new prompt starts at the first factor");

            // Three factors with a real authenticator code
            var secret = Totp.NewSecret();
            store.Configure("combo.three", LockPolicy.PasswordPinTotp, "246810", secret, "three factor pw");
            string Code(long offset = 0) => Totp.Hotp(secret, Totp.Step(clock) + offset);
            var all = store.Begin("combo.three")!;
            Check(all.Steps.SequenceEqual(new[] { LockFactors.Pin, LockFactors.Password, LockFactors.Totp }), "the three-factor policy asks PIN, password, then the code");
            all.Submit("246810"); all.Submit("three factor pw");
            Check(store.Get("combo.three")!.LastTotpStep == -1, "the authenticator step is not spent before every factor is right");
            var good = all.Submit(Code());
            Check(good.Unlocked && store.Get("combo.three")!.LastTotpStep == Totp.Step(clock), "the code step is spent only when the whole policy is satisfied");
            var replay = store.Begin("combo.three")!;
            replay.Submit("246810"); replay.Submit("three factor pw");
            Check(replay.Submit(Code()).Outcome == UnlockOutcome.Wrong, "the same authenticator code cannot be replayed on a combined lock");
            var skew = store.Begin("combo.three")!;
            clock = clock.AddSeconds(900);
            skew.Submit("246810"); skew.Submit("three factor pw");
            Check(skew.Submit(Code(-4)).Outcome == UnlockOutcome.Wrong, "a code outside the clock-skew window is refused on a combined lock");
            protector.Fail = true;
            var gone = store.Begin("combo.three")!;
            gone.Submit("246810"); gone.Submit("three factor pw");
            Check(gone.Submit("123456").Outcome == UnlockOutcome.Unavailable && gone.Index == 0, "an unreadable authenticator secret is reported honestly and restarts the attempt");
            protector.Fail = false;
            Check(!store.CheckCode("combo.two", "123456") && store.CheckCode("combo.three", Code()), "the code checker only works for locks that have an authenticator factor");

            // Credentials belong to one lock each
            store.Configure("iso.a", LockPolicy.Password, null, null, "alpha password");
            store.Configure("iso.b", LockPolicy.Password, null, null, "bravo password");
            Check(!store.Attempt("iso.b", "alpha password").Unlocked && store.Attempt("iso.a", "alpha password").Unlocked, "one lock's password never opens another lock");
            var hashA = store.Get("iso.a")!.PasswordHash; store.Configure("iso.c", LockPolicy.Password, null, null, "alpha password");
            Check(store.Get("iso.c")!.PasswordHash != hashA, "the same password on two locks gets two different salted hashes");

            // Persistence
            var reopened = Store(path, protector, () => clock);
            Check(reopened.PolicyOf("combo.three") == LockPolicy.PasswordPinTotp && reopened.Begin("combo.two") != null && reopened.Get("pw.only")!.PasswordHash != null, "combined and password policies survive a restart");
            var legacyJson = "{\"Version\":1,\"InstallSalt\":\"" + Convert.ToBase64String(new byte[16]) + "\",\"Locks\":[{\"Id\":\"old.pin\",\"Policy\":1,\"PinHash\":null,\"RecoveryHashes\":[],\"Failures\":0,\"WaitUntilUnix\":0},{\"Id\":\"bad.policy\",\"Policy\":99}]}";
            var legacyPath = Path.Combine(folder, "legacy.json"); File.WriteAllText(legacyPath, legacyJson);
            var legacy = Store(legacyPath, protector, () => clock);
            Check(legacy.PolicyOf("old.pin") == LockPolicy.Pin && legacy.DurationOf("old.pin") == UnlockDuration.Manual && !legacy.HasPolicy("bad.policy"), "a lock file from before durations loads, reads as manual, and an unknown policy number is dropped");
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    static void Durations()
    {
        var clock = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var folder = Path.Combine(Path.GetTempPath(), "mdi-lockdur-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "locks.json");
        try
        {
            var settings = new UserSettings();
            var store = Store(path, null, () => clock);
            Check(UnlockDurations.Choosable.Count == 4 && !UnlockDurations.Choosable.Contains(UnlockDuration.Manual), "the wizard offers once, 5 minutes, 1 hour and until the app closes");
            Check(UnlockDurations.Span(UnlockDuration.FiveMinutes) == TimeSpan.FromMinutes(5) && UnlockDurations.Span(UnlockDuration.OneHour) == TimeSpan.FromHours(1) && UnlockDurations.Span(UnlockDuration.Once) == null && UnlockDurations.Span(UnlockDuration.UntilRestart) == null, "timed durations have lengths and the others do not");
            DateTimeOffset? onceUntil = null, fiveUntil = null, hourUntil = null, restartUntil = null;
            bool Lock(string id, UnlockDuration d) { store.Configure(id, LockPolicy.None, null, null, null, d); ElementLocks.Lock(settings, id); return ElementLocks.IsLocked(settings, id); }

            // This action only
            Check(Lock("dur.once", UnlockDuration.Once) && LockLifecycle.Unlock(settings, store, "dur.once", out onceUntil) && onceUntil == null && !ElementLocks.IsLocked(settings, "dur.once") && store.GrantIsActive("dur.once"), "an unlock for one action opens the lock and starts a grant");
            Check(LockLifecycle.ConsumeOnce(settings, store, "dur.once") && ElementLocks.IsLocked(settings, "dur.once") && !store.GrantIsActive("dur.once"), "after the one action the lock closes again");
            Check(!LockLifecycle.ConsumeOnce(settings, store, "dur.once"), "a lock that is already closed has nothing to consume");

            // Five minutes
            Check(Lock("dur.five", UnlockDuration.FiveMinutes) && LockLifecycle.Unlock(settings, store, "dur.five", out fiveUntil) && fiveUntil == clock.AddMinutes(5), "a five minute unlock records when it ends");
            Check(!LockLifecycle.ConsumeOnce(settings, store, "dur.five"), "a timed unlock is not closed by an action");
            clock = clock.AddSeconds(299);
            Check(LockLifecycle.Reconcile(settings, store, false).Count == 0 && !ElementLocks.IsLocked(settings, "dur.five"), "one second before the end the lock is still open");
            var reopenedMidway = Store(path, null, () => clock);
            Check(reopenedMidway.GrantIsActive("dur.five") && reopenedMidway.Get("dur.five")!.GrantUntilUnix == fiveUntil!.Value.ToUnixTimeSeconds(), "a timed grant and its expiry are saved");
            Check(LockLifecycle.Reconcile(settings, store, true).Count == 0 && !ElementLocks.IsLocked(settings, "dur.five"), "a restart before the end keeps a timed unlock open");
            clock = clock.AddSeconds(2);
            var relocked = LockLifecycle.Reconcile(settings, store, false);
            Check(relocked.SequenceEqual(new[] { "dur.five" }) && ElementLocks.IsLocked(settings, "dur.five") && !store.GrantIsActive("dur.five"), "after five minutes the lock closes by itself");

            // One hour
            Check(Lock("dur.hour", UnlockDuration.OneHour) && LockLifecycle.Unlock(settings, store, "dur.hour", out hourUntil) && hourUntil == clock.AddHours(1), "an hour unlock records when it ends");
            clock = clock.AddMinutes(59);
            Check(LockLifecycle.Reconcile(settings, store, false).Count == 0, "59 minutes in the hour unlock is still open");
            clock = clock.AddMinutes(2);
            Check(LockLifecycle.Reconcile(settings, store, false).SequenceEqual(new[] { "dur.hour" }), "after the hour the lock closes by itself");

            // Until the app closes
            Check(Lock("dur.restart", UnlockDuration.UntilRestart) && LockLifecycle.Unlock(settings, store, "dur.restart", out restartUntil) && restartUntil == null, "an until-restart unlock has no timer");
            clock = clock.AddHours(30);
            Check(LockLifecycle.Reconcile(settings, store, false).Count == 0 && !ElementLocks.IsLocked(settings, "dur.restart"), "an until-restart unlock does not expire while the app runs");
            Check(LockLifecycle.Reconcile(settings, store, true).SequenceEqual(new[] { "dur.restart" }) && ElementLocks.IsLocked(settings, "dur.restart"), "at the next launch an until-restart unlock is locked again");
            Check(Lock("dur.once2", UnlockDuration.Once) && LockLifecycle.Unlock(settings, store, "dur.once2", out _) && LockLifecycle.Reconcile(settings, store, true).Contains("dur.once2"), "an unlock for one action that never ran is also closed at launch");

            // Manual and the launch default
            var legacySettings = new UserSettings(); ElementLocks.Lock(legacySettings, "language"); ElementLocks.Unlock(legacySettings, "language");
            Check(LockLifecycle.Reconcile(legacySettings, store, true).Count == 0 && !store.GrantIsActive("language"), "a lock with no duration record is never closed by itself");
            store.Configure("dur.default", LockPolicy.Pin, "123456");
            Check(store.DurationOf("dur.default") == UnlockDuration.UntilRestart, "a new lock defaults to being locked again on launch");
            store.Configure("dur.manual", LockPolicy.None, null, null, null, UnlockDuration.Manual);
            Check(store.DurationOf("dur.manual") == UnlockDuration.UntilRestart, "the manual duration cannot be chosen for a new lock");

            // Locking again ends the grant, and an unlock cannot be started for a lock that is not locked
            ElementLocks.Lock(settings, "dur.five2"); store.Configure("dur.five2", LockPolicy.None, null, null, null, UnlockDuration.FiveMinutes);
            LockLifecycle.Unlock(settings, store, "dur.five2", out _);
            Check(LockLifecycle.Relock(settings, store, "dur.five2") && !store.GrantIsActive("dur.five2") && store.Get("dur.five2")!.GrantUntilUnix == 0, "locking again by hand ends the grant");
            Check(!LockLifecycle.Unlock(settings, store, "never.locked", out _) && !LockLifecycle.Relock(settings, store, "Bad Id!"), "unlocking something that is not locked, or locking a bad id, changes nothing");
            Check(store.NextExpiry() == null, "with no timed grant running there is no next expiry");
            Lock("dur.next", UnlockDuration.FiveMinutes); LockLifecycle.Unlock(settings, store, "dur.next", out _);
            Check(store.NextExpiry() == clock.AddMinutes(5), "the next expiry is the soonest running timed grant");
            Check(File.ReadAllText(path).Contains("GrantUntilUnix"), "the expiry is persisted in the lock file");
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    static void Elements()
    {
        var a = ElementIdentity.Derive("Build", "BuildImageButton", "Build image", "Button");
        Check(a == ElementIdentity.Derive("Build", "BuildImageButton", "Renamed label", "Button", 5), "an automation id keeps the same lock when the label or position changes");
        Check(a != ElementIdentity.Derive("Images", "BuildImageButton", "Build image", "Button"), "the same automation id on another page is another lock");
        Check(ElementIdentity.Derive("Build", null, "OK", "Button") == ElementIdentity.Derive("Build", null, "OK", "Button"), "an id derived from a name is stable");
        Check(ElementIdentity.Derive("Build", null, "OK", "Button", 0) != ElementIdentity.Derive("Build", null, "OK", "Button", 1), "two identical buttons on one page get different locks");
        Check(ElementIdentity.Derive("Build", null, "OK", "Button") != ElementIdentity.Derive("Build", null, "OK", "CheckBox"), "the element type is part of an unnamed identity");
        Check(ElementIdentity.Derive("Build", null, null, "Slider", 0) != ElementIdentity.Derive("Build", null, null, "Slider", 1), "unnamed elements are told apart by their position");
        var samples = new[] { ElementIdentity.Derive("A very long page key indeed", new string('x', 300), null, "Button"), ElementIdentity.Derive("", null, "名稱 with ünïcode & symbols!!", "TextBox", 3), ElementIdentity.Derive("p", null, null, ""), ElementIdentity.Derive("日本語", "ID WITH SPACES", "n", "ComboBox") };
        Check(samples.All(ElementLocks.IsValidId) && samples.All(s => s.Length <= 40), "every derived id fits the lock id pattern and 40 characters");
        Check(samples.Distinct().Count() == samples.Length, "different elements get different ids even when their readable part is cut short");
        var longA = ElementIdentity.Derive("p", new string('a', 60) + "1", null, "Button"); var longB = ElementIdentity.Derive("p", new string('a', 60) + "2", null, "Button");
        Check(longA != longB, "two long automation ids that share a prefix stay different");
        Check(ElementIdentity.Slug("Build  Image!!") == "build-image" && ElementIdentity.Slug("  ") == "", "slugs are lower case with single hyphens");
        Check(ElementIdentity.Describe("Button", "Build image", "x") == "Button: Build image" && ElementIdentity.Describe("Slider", null, "VolumeSlider") == "Slider: VolumeSlider" && ElementIdentity.Describe("Border", null, null) == "Border: (unnamed)", "the manager label names the element kind and its accessible name");
    }

    static void Manager()
    {
        var folder = Path.Combine(Path.GetTempPath(), "mdi-lockmgr-" + Guid.NewGuid().ToString("N"));
        var clock = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        try
        {
            var settings = new UserSettings();
            var store = Store(Path.Combine(folder, "locks.json"), null, () => clock);
            var buttonId = ElementIdentity.Derive("Build", null, "Build image", "Button");
            store.Configure(buttonId, LockPolicy.Password, null, null, "manager password");
            ElementLocks.Lock(settings, buttonId); LockManager.SetLabel(settings, buttonId, ElementIdentity.Describe("Button", "Build image", null));
            ElementLocks.Lock(settings, "language");
            store.Configure("theme", LockPolicy.Pin, "135790");
            var rows = LockManager.Rows(settings, store);
            Check(rows.Select(r => r.Id).SequenceEqual(new[] { buttonId, "language", "theme" }.OrderBy(x => x, StringComparer.Ordinal)), "the manager lists every locked id and every id with a policy once");
            var buttonRow = rows.Single(r => r.Id == buttonId);
            Check(buttonRow.Label == "Button: Build image" && buttonRow.Locked && buttonRow.Policy == LockPolicy.Password && buttonRow.RecoveryLeft == 8, "a row shows the label, the state, the policy and the recovery codes left");
            Check(rows.Single(r => r.Id == "language").Label == "Setting: language" && !rows.Single(r => r.Id == "language").HasPolicyRecord && !rows.Single(r => r.Id == "theme").Locked, "a settings row without a record and an unlocked row with one are both listed honestly");
            Check(LockManager.Filter(rows, t => t.Contains("Password", StringComparison.OrdinalIgnoreCase)).Select(r => r.Id).SequenceEqual(new[] { buttonId }), "the search matches the policy name");
            Check(LockManager.Filter(rows, t => t.Contains("Locked", StringComparison.Ordinal)).Select(r => r.Id).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(new[] { buttonId, "language" }.OrderBy(x => x, StringComparer.Ordinal)), "a locked lock is always labelled locked in search text");
            Check(LockManager.Filter(rows, t => System.Text.RegularExpressions.Regex.IsMatch(t, "^el\\.build")).Count == 1, "the search text works with a regular expression predicate");

            Check(!LockManager.Remove(settings, store, buttonId) && store.HasPolicy(buttonId) && settings.LockLabels.ContainsKey(buttonId), "a lock that is locked cannot be removed until it is unlocked");
            ElementLocks.Unlock(settings, buttonId);
            Check(LockManager.Remove(settings, store, buttonId) && !store.HasPolicy(buttonId) && !settings.LockLabels.ContainsKey(buttonId) && !LockManager.Rows(settings, store).Any(r => r.Id == buttonId), "removing an unlocked lock deletes the lock, its credentials and its label");
            Check(!LockManager.Remove(settings, store, "no.such.lock"), "removing something that does not exist reports nothing removed");
            var (removed, skipped) = LockManager.RemoveMany(settings, store, new[] { "theme", "language", "theme" });
            Check(removed.SequenceEqual(new[] { "theme" }) && skipped.SequenceEqual(new[] { "language" }) && ElementLocks.IsLocked(settings, "language"), "a bulk removal removes the unlocked locks and reports the locked ones it skipped");

            store.Configure("exp.one", LockPolicy.Password, null, null, "export secret pw");
            var exportRows = LockManager.ExportRows(LockManager.Rows(settings, store));
            var csv = TableExport.Render("CSV", "Locks", LockManager.ExportHeaders, exportRows, clock);
            Check(csv.Contains("exp.one") && !csv.Contains("export secret pw") && !csv.Contains("pbkdf2") && !csv.Contains(store.Get("exp.one")!.RecoveryHashes[0]), "an export describes the locks and never includes a credential or a hash");

            var labels = ElementLocks.NormalizeLabels(new Dictionary<string, string> { ["good.id"] = "  A label  ", ["Bad Id"] = "x", ["blank"] = "   ", ["ctl"] = "bad\u0007label", ["long"] = new string('z', 200) });
            Check(labels.Count == 2 && labels["good.id"] == "A label" && labels["long"].Length == ElementLocks.MaxLabelLength, "labels are trimmed, bounded and refused when invalid");
            LockManager.SetLabel(settings, "Bad Id!", "x");
            Check(!settings.LockLabels.ContainsKey("Bad Id!"), "a label for an invalid id is not stored");
            var current = new UserSettings(); ElementLocks.Lock(current, buttonId); LockManager.SetLabel(current, buttonId, "Button: Build image");
            var imported = new UserSettings(); ElementLocks.Preserve(current, imported);
            Check(imported.LockLabels[buttonId] == "Button: Build image" && imported.LockedSettings.Contains(buttonId), "an import or reset keeps lock labels along with the locks");
            var roundTrip = System.Text.Json.JsonSerializer.Deserialize<UserSettings>(current.ToJson())!; roundTrip.Normalize();
            Check(roundTrip.LockLabels.TryGetValue(buttonId, out var kept) && kept == "Button: Build image", "lock labels survive a save and load");
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    static void Tickets()
    {
        var clock = DateTimeOffset.Parse("2026-11-04T10:00:00Z");
        var folder = Path.Combine(Path.GetTempPath(), "mdi-tickets-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "tickets.json");
        try
        {
            var desk = new SupportTicketStore(path, () => clock);
            Check(desk.Tickets.Count == 0, "a new desk has no tickets");
            var first = desk.Create(TicketCategory.ForgotPassword, TicketSeverity.Critical, "  I forgot the password to the build button.  ", "btn.build.build-image");
            Check(first.Number == "TKT-20261104-0001" && first.Status == TicketStatus.Open && first.Description == "I forgot the password to the build button." && first.LockId == "btn.build.build-image" && first.Messages.Count == 0, "a ticket gets a local number, starts open and has no reply yet");
            Check(desk.Create(TicketCategory.ForgotPin, TicketSeverity.Low, "second").Number == "TKT-20261104-0002", "ticket numbers count up within a day");
            clock = clock.AddDays(1);
            Check(desk.Create(TicketCategory.LostAuthenticator, TicketSeverity.Medium, "next day").Number == "TKT-20261105-0001", "ticket numbers restart each day");
            Check(desk.Tickets[0].Number == "TKT-20261105-0001", "tickets are listed newest first");
            Reject(() => desk.Create(TicketCategory.SomethingElse, TicketSeverity.Low, "   "), "a ticket needs a description");
            Reject(() => desk.Create((TicketCategory)99, TicketSeverity.Low, "x"), "an unknown category is refused");
            check_long(desk);
            Check(desk.Create(TicketCategory.SomethingElse, TicketSeverity.Low, "bad lock", "Not A Valid Id").LockId == "", "an invalid lock id is not kept on a ticket");

            // Status advances and replies arrive
            Check(desk.Advance(first.Number) && first.Status == TicketStatus.Acknowledged && first.Messages.Single().Code == SupportDesk.FirstResponse, "advancing acknowledges the ticket with the first response");
            Check(desk.Advance(first.Number) && first.Status == TicketStatus.Investigating && first.Messages.Count == 2, "advancing again moves it to investigating");
            Check(desk.Advance(first.Number) && first.Status == TicketStatus.Resolved && first.Messages.Last().Code == SupportDesk.Resolution, "the last step resolves it with the resolution message");
            Check(!desk.Advance(first.Number) && first.Messages.Count == 3 && !desk.Advance("TKT-00000000-0000"), "a resolved or unknown ticket does not advance");
            var quick = desk.Tickets.First(t => t.Description == "second");
            Check(desk.ResolveNow(quick.Number) && quick.Status == TicketStatus.Resolved && quick.Messages.Select(m => m.Code).SequenceEqual(new[] { SupportDesk.FirstResponse, SupportDesk.Investigating, SupportDesk.Resolution }), "resolving straight away still records every reply");
            Check(!desk.ResolveNow(quick.Number), "resolving twice changes nothing");

            // The status also moves by itself while the person waits
            var waiting = desk.Create(TicketCategory.ForgotPin, TicketSeverity.High, "waiting");
            Check(desk.Tick() == 0, "a brand new ticket does not move at once");
            clock = clock.AddSeconds(21);
            Check(desk.Tick() >= 1 && waiting.Status == TicketStatus.Acknowledged, "after the pause the status advances by itself");
            clock = clock.AddSeconds(21); desk.Tick(); clock = clock.AddSeconds(21); desk.Tick();
            Check(waiting.Status == TicketStatus.Resolved && desk.Tick() == 0, "waiting long enough resolves the ticket and then nothing more moves");

            // Persistence and the file
            var reopened = new SupportTicketStore(path, () => clock);
            Check(reopened.Tickets.Count == desk.Tickets.Count && reopened.Get(first.Number)!.Messages.Count == 3 && reopened.Get(first.Number)!.Status == TicketStatus.Resolved, "tickets and their replies survive a restart");
            reopened.Create(TicketCategory.ForgotPin, TicketSeverity.Low, "after restart");
            Check(reopened.Tickets.Select(t => t.Number).Distinct().Count() == reopened.Tickets.Count, "numbers stay unique after a restart");
            File.WriteAllText(path, "{ not json");
            var broken = new SupportTicketStore(path, () => clock);
            Check(broken.Tickets.Count == 0 && broken.LoadError != null, "a damaged ticket file is ignored with an honest message");

            // Search, bulk and export
            var set = new SupportTicketStore(null, () => clock);
            set.Create(TicketCategory.ForgotPin, TicketSeverity.Low, "alpha"); set.Create(TicketCategory.ForgotPassword, TicketSeverity.High, "bravo"); set.Create(TicketCategory.LostRecoveryCodes, TicketSeverity.High, "charlie");
            Check(set.Filter(t => t.Contains("bravo")).Count == 1 && set.Filter(t => t.Contains("High")).Count == 2 && set.Filter(t => t.Contains("Forgot my PIN")).Count == 1, "search matches the description, the severity and the category");
            Check(set.Filter(t => System.Text.RegularExpressions.Regex.IsMatch(t, "TKT-\\d{8}-0002")).Count == 1, "search text carries the ticket number for a regular expression");
            var csv = TableExport.Render("CSV", "Tickets", SupportTicketStore.ExportHeaders, SupportTicketStore.ExportRows(set.Tickets), clock);
            Check(csv.Contains("alpha") && csv.Contains("TKT-") && csv.Split('\n').Length >= 4, "tickets export with their numbers and descriptions");
            Check(set.RemoveMany(set.Tickets.Take(2).Select(t => t.Number)) == 2 && set.Tickets.Count == 1 && set.Remove(set.Tickets[0].Number) && set.Tickets.Count == 0, "tickets can be removed one at a time or in bulk");

            // The bit is a bit
            Check(SupportDesk.Disclosure == "Nothing is sent anywhere. A ticket exists only on this computer, no network request is made, no data is collected, and nobody is reading it.", "the disclosure line is exact");
            Check(new[] { SupportDesk.FirstResponse, SupportDesk.Investigating, SupportDesk.Resolution }.All(c => SupportDesk.Message(c).Length > 0 && SupportDesk.LeadIn(c).Length > 0), "every reply has a fact and a playful lead-in");
            var action = SupportDesk.Resolve(@"C:\Users\someone\AppData\Local\MaterialDockerImage");
            Check(action.Folder == @"C:\Users\someone\AppData\Local\MaterialDockerImage" && !action.DeletesAnything, "the resolution is only the folder to open and it deletes nothing");
            var source = SourceText("UI", "SettingsSafety.SupportTickets.cs");
            Check(!source.Contains("System.Net") && !source.Contains("HttpClient") && !source.Contains("WebRequest") && !source.Contains("Socket"), "the ticket desk makes no network call");
            Check(!source.Contains("Directory.Delete") && !source.Contains("File.Delete") && !source.Contains("RemoveDirectory"), "the ticket desk has no way to delete the data folder");
            var dialogSource = SourceText("Shell", "SupportDeskUi.cs");
            Check(dialogSource.Contains("OpenFolder") && !dialogSource.Contains("Directory.Delete") && !dialogSource.Contains("System.Net") && dialogSource.Contains("SupportDesk.Disclosure"), "the ticket screen opens the folder, never deletes it, shows the disclosure and makes no network call");
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    static void check_long(SupportTicketStore desk)
    {
        var big = desk.Create(TicketCategory.SomethingElse, TicketSeverity.Low, new string('q', 5000));
        Check(big.Description.Length == SupportTicketStore.MaxDescription, "a long description is cut to the limit");
        var full = new SupportTicketStore(null);
        for (var i = 0; i < SupportTicketStore.MaxTickets; i++) full.Create(TicketCategory.SomethingElse, TicketSeverity.Low, "t" + i);
        Reject(() => full.Create(TicketCategory.SomethingElse, TicketSeverity.Low, "one too many"), "the ticket list is bounded");
    }

    static string SourceText(string folder, string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "MaterialDockerImage"))) dir = dir.Parent;
        var p = Path.Combine(dir!.FullName, "src", "MaterialDockerImage", folder, file);
        return File.Exists(p) ? File.ReadAllText(p) : "";
    }

    static void Wording()
    {
        var language = Loc.Language; var school = Loc.SchoolMode;
        Loc.SchoolMode = false; Loc.Language = "Cantonese";
        var samples = new List<string>();
        foreach (var p in LockPolicies.Choosable) samples.Add(LockPolicies.Label(p));
        foreach (var d in UnlockDurations.Choosable) samples.Add(UnlockDurations.Label(d));
        samples.Add(UnlockDurations.Label(UnlockDuration.Manual));
        foreach (var f in new[] { LockFactors.Pin, LockFactors.Password, LockFactors.Totp }) samples.Add(LockPolicies.FactorLabel(f));
        foreach (var c in Enum.GetValues<TicketCategory>()) samples.Add(SupportDesk.CategoryLabel(c));
        foreach (var s in Enum.GetValues<TicketSeverity>()) samples.Add(SupportDesk.SeverityLabel(s));
        foreach (var s in Enum.GetValues<TicketStatus>()) samples.Add(SupportDesk.StatusLabel(s));
        foreach (var c in new[] { SupportDesk.FirstResponse, SupportDesk.Investigating, SupportDesk.Resolution }) { samples.Add(SupportDesk.Message(c)); samples.Add(SupportDesk.LeadIn(c)); }
        samples.Add(SupportDesk.Disclosure); samples.Add(SupportDesk.SeverityNote); samples.Add(PasswordRules.Problem("x")!);
        samples.AddRange(UiStrings);
        var missing = samples.Distinct().Where(x => Loc.T(x) == x).ToList();
        foreach (var m in missing) Console.WriteLine("MISSING Cantonese: " + m);
        Check(missing.Count == 0, "every new lock, manager and Support Tickets string has Cantonese wording");
        Check(samples.Distinct().Where(x => HasCjk(Loc.T(x))).Count() == samples.Distinct().Count(), "the Cantonese wording is real Chinese text");
        Loc.Language = "Bilingual";
        Check(Loc.T(SupportDesk.Disclosure).StartsWith(SupportDesk.Disclosure + " · ", StringComparison.Ordinal), "bilingual mode keeps the exact English disclosure first");
        Loc.Language = language; Loc.SchoolMode = school;
    }

    static bool HasCjk(string text) => text.Any(c => c >= 0x4E00 && c <= 0x9FFF);

    /// <summary>Strings used directly by the lock wizard, the Locks manager and the Support Tickets screen. Kept here so a missing translation turns the check red.</summary>
    static readonly string[] UiStrings =
    [
        "Lock policy", "How long should unlocking last?", "Set up the factors in order.", "Unlocking will only ask you to confirm. Pick a PIN, a password or an authenticator app for something harder to click through.",
        "Password (8 to 128 characters)", "Repeat the password", "The password is stored as a salted PBKDF2 hash, never as typed.", "The two passwords do not match.", "Enter your password.", "Step 1 of 2",
        "Unlocked until", "Unlocked for this action only.", "Unlocked until the app closes.", "Unlocked.", "Locked again.", "A timed unlock ended, so the lock is closed again.",
        "Locks", "Locks manager", "Locks manager…", "Search locks", "No locks yet.", "Right-click any button, switch, option, field or slider and choose Lock this control… to add one.",
        "Every lock the app knows about, whether it guards a settings row, a button, a switch, a field, a slider or a drop-down. Each lock has its own policy and its own credentials; unlocking one never unlocks another.",
        "What it is", "Policy", "Unlock lasts", "Locked only", "Has a credential", "Lock again", "Remove lock", "Remove selected locks", "Locked", "Unlocked",
        "Its PIN, password or authenticator secret and its recovery codes are deleted. The control stays unlocked.", "Lock removed.", "Locks removed.", "Skipped because they are locked:", "Unlock this lock before removing it.", "Setting: language",
        "No lock has a PIN, password or authenticator yet. Lock a control from its right-click menu to choose one.",
        "Support Tickets", "Support Tickets…", "Forgotten your password?", "Search tickets", "No tickets yet.", "File a ticket above. It stays on this computer.", "File a ticket", "Ticket", "Category", "Severity", "Status", "Description",
        "Describe what happened", "The data folder", "Open the data folder", "Copy folder path", "The desk opens the folder and stands back. Deleting it is your own act.", "Advance status", "Resolve", "Remove ticket",
        "Ticket filed on this computer.", "Ticket removed.", "Tickets removed.", "Waiting for a reply that is not coming. Advance the status to see what the desk says.",
        "The only way back into a locked control is to delete the app's data folder yourself.", "Welcome to the Department of Forgotten Things.",
        "Say what happened first. The desk cannot help with a blank page.", "The ticket list is full. Remove old tickets first.", "Lock labels", "Never"
    ];
}
