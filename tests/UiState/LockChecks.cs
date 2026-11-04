using System.Text;
using System.Text.RegularExpressions;
using MaterialDockerImage.UI;

/// <summary>Checks for lock policies: Base32, RFC 4226 and RFC 6238, PIN and recovery-code hashing, backoff, the unlock ladder state, the QR encoder and the credential store.</summary>
public static class LockChecks
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

    /// <summary>A stand-in for DPAPI that can be told to fail, so the "secret cannot be opened" path is exercised.</summary>
    sealed class FakeProtector : ISecretProtector
    {
        public bool Fail;
        static byte[] Key(byte[] entropy) => System.Security.Cryptography.SHA256.HashData(entropy);
        public byte[] Protect(byte[] plain, byte[] entropy) { var k = Key(entropy); return new byte[] { 0x46 }.Concat(plain.Select((b, i) => (byte)(b ^ k[i % k.Length]))).ToArray(); }
        public byte[]? Unprotect(byte[] protectedBytes, byte[] entropy) { if (Fail || protectedBytes.Length < 2 || protectedBytes[0] != 0x46) return null; var k = Key(entropy); return protectedBytes.Skip(1).Select((b, i) => (byte)(b ^ k[i % k.Length])).ToArray(); }
    }

    public static int Run()
    {
        count = 0;
        Encodings();
        Standards();
        Hashing();
        Backoff();
        Store();
        Dpapi();
        Qr();
        Wording();
        return count;
    }

    static void Encodings()
    {
        var vectors = new[] { ("", ""), ("f", "MY"), ("fo", "MZXQ"), ("foo", "MZXW6"), ("foob", "MZXW6YQ"), ("fooba", "MZXW6YTB"), ("foobar", "MZXW6YTBOI") };
        Check(vectors.All(v => Base32.Encode(Encoding.ASCII.GetBytes(v.Item1)) == v.Item2), "Base32 matches the RFC 4648 test vectors");
        Check(vectors.Where(v => v.Item1.Length > 0).All(v => Encoding.ASCII.GetString(Base32.Decode(v.Item2)!) == v.Item1), "Base32 decodes the RFC 4648 test vectors");
        Check(Base32.Decode("mzxw 6ytb-oi==")!.SequenceEqual(Encoding.ASCII.GetBytes("foobar")), "Base32 input tolerates case, spaces, hyphens and padding");
        Check(Base32.Decode("MZXW1") == null && Base32.Decode("MZXW!") == null && Base32.Decode("") == null && Base32.Decode(null) == null, "Base32 refuses characters outside its alphabet instead of guessing");
        var random = Totp.NewSecret(20);
        Check(random.Length == 20 && Base32.Decode(Base32.Encode(random))!.SequenceEqual(random), "a random secret survives a Base32 round trip");
        Check(!Totp.NewSecret().SequenceEqual(Totp.NewSecret()), "two generated secrets differ");
        Check(Base32.Group("ABCDEFGHIJ") == "ABCD EFGH IJ", "Base32 text is grouped in fours for typing");
    }

    static void Standards()
    {
        var hotp = new[] { "755224", "287082", "359152", "969429", "338314", "254676", "287922", "162583", "399871", "520489" };
        var key = Encoding.ASCII.GetBytes("12345678901234567890");
        Check(Enumerable.Range(0, 10).All(i => Totp.Hotp(key, i) == hotp[i]), "HOTP matches the ten RFC 4226 Appendix D values");
        var run = Rfc6238Vectors.Run();
        Check(run.Total == 18 && run.Passed == 18 && run.Failures.Count == 0, "all 18 RFC 6238 vectors pass for SHA-1, SHA-256 and SHA-512 at 8 digits");
        Check(Totp.Code(Rfc6238Vectors.SecretFor("SHA1"), DateTimeOffset.FromUnixTimeSeconds(59), 6) == "287082", "the 6-digit code is the tail of the 8-digit vector");
        var tampered = Rfc6238Vectors.All.ToArray(); tampered[4] = (tampered[4].Time, tampered[4].Algorithm, "00000000");
        var red = Rfc6238Vectors.Run(tampered);
        Check(red.Passed == 17 && red.Failures.Count == 1 && red.Failures[0].Contains("SHA256"), "a wrong expected vector turns the standards check red and names the case");
        Reject(() => Totp.Hotp(key, 0, 5), "fewer than six digits is refused");
        Reject(() => Totp.Hotp(key, 0, 6, "MD5"), "an unsupported algorithm is refused");

        // Verification window, skew and replay.
        var secret = Totp.NewSecret(); var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_010); var step = Totp.Step(now);
        string CodeAt(long s) => Totp.Hotp(secret, s);
        Check(Totp.Verify(secret, CodeAt(step), now) == step, "the current code is accepted and reports its step");
        Check(Totp.Verify(secret, CodeAt(step - 1), now) == step - 1 && Totp.Verify(secret, CodeAt(step + 1), now) == step + 1, "one step of clock skew either way is accepted");
        Check(Totp.Verify(secret, CodeAt(step - 2), now) < 0 && Totp.Verify(secret, CodeAt(step + 2), now) < 0, "two steps of skew is refused");
        Check(Totp.Verify(secret, CodeAt(step), now, step) < 0 && Totp.Verify(secret, CodeAt(step - 1), now, step) < 0, "a step that was already used, and every earlier one, cannot be replayed");
        Check(Totp.Verify(secret, CodeAt(step + 1), now, step) == step + 1, "the next step is still accepted after one was used");
        Check(Totp.Verify(secret, CodeAt(step)[..5], now) < 0 && Totp.Verify(secret, "12345a", now) < 0 && Totp.Verify(secret, null, now) < 0 && Totp.Verify(secret, "", now) < 0, "short, non-numeric and empty codes are refused");
        Check(Totp.Verify(secret, CodeAt(step)[..3] + " " + CodeAt(step)[3..], now) == step, "a code typed with a space is accepted");
        Check(Totp.SecondsLeft(DateTimeOffset.FromUnixTimeSeconds(60)) == 30 && Totp.SecondsLeft(DateTimeOffset.FromUnixTimeSeconds(89)) == 1, "the countdown to the period boundary is exact");

        var uri = Totp.Uri("MaterialDockerImage", "btn.build.run", secret);
        Check(uri.StartsWith("otpauth://totp/MaterialDockerImage:btn.build.run?") && uri.Contains("secret=" + Base32.Encode(secret)) && uri.Contains("issuer=MaterialDockerImage") && uri.Contains("algorithm=SHA1") && uri.Contains("digits=6") && uri.Contains("period=30"), "the otpauth link carries issuer, account, secret, algorithm, digits and period");
        Check(Totp.Uri("Ex Ample", "a b@c", secret).Contains("Ex%20Ample:a%20b%40c"), "the otpauth label is escaped");
    }

    static void Hashing()
    {
        var salt = new byte[16]; salt[0] = 7; var other = new byte[16]; other[0] = 8;
        var hash = CredentialHasher.Hash("482913", salt, 10_000);
        Check(hash.StartsWith("pbkdf2-sha256$10000$") && !hash.Contains("482913"), "a PIN is stored as a PBKDF2-SHA256 string that does not contain it");
        Check(CredentialHasher.Verify("482913", hash, salt) && !CredentialHasher.Verify("482914", hash, salt) && !CredentialHasher.Verify("", hash, salt), "only the right PIN verifies");
        Check(!CredentialHasher.Verify("482913", hash, other), "a hash does not verify under another install's salt");
        Check(CredentialHasher.Hash("482913", salt, 10_000) != hash, "the same PIN hashes differently each time (random salt per hash)");
        var parts = hash.Split('$');
        Check(!CredentialHasher.Verify("482913", null, salt) && !CredentialHasher.Verify("482913", "garbage", salt) && !CredentialHasher.Verify("482913", $"pbkdf2-sha256$5$" + parts[2] + "$" + parts[3], salt) && !CredentialHasher.Verify("482913", $"pbkdf2-sha256$99999999$" + parts[2] + "$" + parts[3], salt) && !CredentialHasher.Verify("482913", $"pbkdf2-sha256$10000$!!!$" + parts[3], salt), "malformed, low-iteration, absurd-iteration and corrupt hashes never verify");
        Check(CredentialHasher.Hash("1", salt).Split('$')[1] == CredentialHasher.Iterations.ToString() && CredentialHasher.Iterations >= 100_000, "the default work factor is at least 100,000 iterations");
        Check(new[] { "1234", "123456789012", "0000" }.All(PinRules.IsValid) && new[] { "123", "1234567890123", "12a4", "", " 1234", "١٢٣٤" }.All(p => !PinRules.IsValid(p)) && !PinRules.IsValid(null), "a PIN is 4 to 12 ASCII digits");

        var codes = RecoveryCodes.Generate();
        Check(codes.Count == 8 && codes.Distinct().Count() == 8 && codes.All(c => Regex.IsMatch(c, "^[0-9A-HJKMNP-TV-Z]{5}-[0-9A-HJKMNP-TV-Z]{5}$")), "eight distinct recovery codes in XXXXX-XXXXX form");
        Check(RecoveryCodes.Normalize(" abcde-fgh1o ") == "ABCDEFGH10" && RecoveryCodes.Normalize("IL") == "11", "typed recovery codes ignore case, spaces, hyphens and look-alike letters");
        Check(RecoveryCodes.LooksValid(codes[0]) && !RecoveryCodes.LooksValid("ABC") && !RecoveryCodes.LooksValid("UUUUU-UUUUU"), "recovery code shape is checked before hashing");
    }

    static void Backoff()
    {
        var waits = Enumerable.Range(0, 9).Select(n => (int)AttemptLimiter.WaitAfter(n).TotalSeconds).ToArray();
        Check(waits.SequenceEqual(new[] { 0, 0, 0, 30, 60, 120, 240, 480, 960 }), "two free wrong answers, then 30 s doubling each time");
        Check(AttemptLimiter.WaitAfter(40) == TimeSpan.FromHours(1) && AttemptLimiter.WaitAfter(int.MaxValue) == TimeSpan.FromHours(1), "the wait is capped at one hour");
        Check(AttemptLimiter.FreeAttemptsLeft(0) == 2 && AttemptLimiter.FreeAttemptsLeft(1) == 1 && AttemptLimiter.FreeAttemptsLeft(2) == 0 && AttemptLimiter.FreeAttemptsLeft(9) == 0, "free attempts count down to zero");
    }

    static void Store()
    {
        var folder = Path.Combine(Path.GetTempPath(), "mdi-locks-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "locks.json");
        var clock = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var protector = new FakeProtector();
        LockStore Open() => new(path, protector, () => clock, 10_000);
        try
        {
            var store = Open();
            Check(store.PolicyOf("language") == LockPolicy.None && store.Stage("language") == LadderStage.Confirm && store.Attempt("language", null).Unlocked, "a lock with no policy only asks for a confirmation");
            Check(store.Configure("language", LockPolicy.None) == null && store.HasPolicy("language") && store.Stage("language") == LadderStage.Confirm, "a confirm-only policy makes no recovery codes");

            // PIN lock
            var codes = store.Configure("theme", LockPolicy.Pin, "482913")!;
            var text = File.ReadAllText(path);
            Check(codes.Count == 8 && store.RecoveryLeft("theme") == 8, "a PIN lock makes eight recovery codes");
            Check(!text.Contains("482913") && codes.All(c => !text.Contains(c) && !text.Contains(RecoveryCodes.Normalize(c))), "neither the PIN nor any recovery code is written to the file");
            Check(store.Stage("theme") == LadderStage.Credential, "the ladder asks for the credential first");
            var first = store.Attempt("theme", "000000");
            Check(first.Outcome == UnlockOutcome.Wrong && first.FreeAttemptsLeft == 1 && first.WaitUntil == null, "the first wrong PIN costs nothing and says how many free tries remain");
            var second = store.Attempt("theme", "111111");
            Check(second.Outcome == UnlockOutcome.Wrong && second.FreeAttemptsLeft == 0 && store.Stage("theme") == LadderStage.Credential, "the second wrong PIN is still free");
            var third = store.Attempt("theme", "222222");
            Check(third.Outcome == UnlockOutcome.Wrong && third.WaitUntil == clock.AddSeconds(30) && store.Stage("theme") == LadderStage.Wait, "the third wrong PIN starts a 30 second wait");
            Check(store.Attempt("theme", "482913").Outcome == UnlockOutcome.Waiting && store.Get("theme")!.Failures == 3, "the right PIN is refused during the wait and the refused try is not counted");
            var reopened = Open();
            Check(reopened.Stage("theme") == LadderStage.Wait && reopened.WaitUntil("theme") == clock.AddSeconds(30), "the wait survives a restart");
            clock = clock.AddSeconds(29);
            Check(store.Stage("theme") == LadderStage.Wait, "29 seconds in, the wait is still running");
            clock = clock.AddSeconds(2);
            Check(store.Stage("theme") == LadderStage.Credential && store.WaitUntil("theme") == null, "after 31 seconds the credential rung is back");
            var fourth = store.Attempt("theme", "333333");
            Check(fourth.WaitUntil == clock.AddSeconds(60), "the next wrong PIN waits twice as long");
            clock = clock.AddSeconds(61);
            Check(store.Attempt("theme", "482913").Unlocked && store.Get("theme")!.Failures == 0 && store.Get("theme")!.WaitUntilUnix == 0, "the right PIN unlocks and clears the failure count");
            Check(store.Attempt("theme", "482913").Unlocked, "unlocking again with the right PIN still works");
            Check(Open().Attempt("theme", "482913").Unlocked, "the PIN still verifies after a restart");

            // Recovery codes
            var other = store.Configure("seed", LockPolicy.Pin, "135790")!;
            for (var i = 0; i < 3; i++) store.Attempt("seed", "999999");
            Check(store.Stage("seed") == LadderStage.Wait, "a lock can be put into a wait");
            Check(store.UseRecoveryCode("seed", codes[0]).Outcome == UnlockOutcome.Wrong, "another lock's recovery code does not work here");
            var used = store.UseRecoveryCode("seed", other[0].ToLowerInvariant().Replace("-", " "));
            Check(used.Unlocked && store.RecoveryLeft("seed") == 7 && store.Stage("seed") == LadderStage.Credential && store.Get("seed")!.Failures == 0, "a recovery code works during a wait, clears it and is spent");
            Check(store.UseRecoveryCode("seed", other[0]).Outcome == UnlockOutcome.Wrong && store.RecoveryLeft("seed") == 7, "a recovery code works only once");
            Check(store.UseRecoveryCode("seed", "nonsense").Outcome == UnlockOutcome.Wrong && store.UseRecoveryCode("language", "ABCDE-FGHJK").Outcome == UnlockOutcome.NotConfigured, "malformed codes and confirm-only locks refuse a recovery code");
            Check(Open().RecoveryLeft("seed") == 7, "a spent recovery code stays spent after a restart");

            // Authenticator lock
            var secret = Totp.NewSecret();
            var tcodes = store.Configure("sounds", LockPolicy.Authenticator, null, secret)!;
            var after = File.ReadAllText(path);
            Check(tcodes.Count == 8 && !after.Contains(Base32.Encode(secret)) && !after.Contains(Convert.ToBase64String(secret)) && store.Get("sounds")!.TotpProtected != null, "the authenticator secret is stored only in protected form");
            string Code(long offset = 0) => Totp.Hotp(secret, Totp.Step(clock) + offset);
            Check(store.CheckCode("sounds", Code()) && !store.CheckCode("sounds", "000000") && !store.CheckCode("language", Code()), "a code can be checked without being used");
            Check(store.CheckCode("sounds", Code()) && store.Get("sounds")!.LastTotpStep == -1, "checking a code does not consume its step");
            Check(store.Attempt("sounds", Code()).Unlocked, "the current authenticator code unlocks");
            var replay = store.Attempt("sounds", Code());
            Check(replay.Outcome == UnlockOutcome.Wrong, "the same code cannot be used twice");
            Check(store.Attempt("sounds", Code(1)).Unlocked, "the next code is accepted");
            clock = clock.AddSeconds(600);
            Check(store.Attempt("sounds", Code(-3)).Outcome == UnlockOutcome.Wrong && store.Attempt("sounds", Code(5)).Outcome == UnlockOutcome.Wrong, "codes far outside the skew window are refused");
            protector.Fail = true;
            Check(store.Attempt("sounds", "123456").Outcome == UnlockOutcome.Unavailable && !store.CheckCode("sounds", "123456"), "an unreadable secret is reported honestly, not as a wrong code");
            Check(store.UseRecoveryCode("sounds", tcodes[0]).Unlocked, "a recovery code still works when the secret cannot be read");
            protector.Fail = false;

            // Validation and removal
            Reject(() => store.Configure("Bad Id!", LockPolicy.None), "an invalid lock id is refused");
            Reject(() => store.Configure("theme", LockPolicy.Pin, "12"), "a short PIN is refused");
            Check(store.Attempt("theme", "482913").Unlocked, "a refused change leaves the earlier PIN intact");
            Reject(() => store.Configure("theme", LockPolicy.Authenticator, null, new byte[3]), "a tiny authenticator secret is refused");
            Reject(() => new LockStore(null, null).Configure("x", LockPolicy.Authenticator, null, Totp.NewSecret()), "an authenticator lock cannot be stored when no secret protection exists");
            Check(store.Remove("language") && !store.HasPolicy("language") && !store.Remove("language") && !Open().HasPolicy("language"), "a policy can be removed, once");
            for (var i = 0; i < LockStore.MaxRecords + 20; i++) try { store.Configure("row" + i, LockPolicy.None); } catch (InvalidOperationException) { }
            Check(store.Records.Count <= LockStore.MaxRecords, "the number of stored locks is bounded");

            // A damaged file is ignored with a message, never trusted.
            File.WriteAllText(path, "{ not json");
            var damaged = Open();
            Check(damaged.Records.Count == 0 && damaged.LoadError != null, "a damaged lock file is ignored and reported");
            var memory = new LockStore(null, protector, () => clock, 10_000);
            memory.Configure("language", LockPolicy.Pin, "2468");
            Check(memory.Attempt("language", "2468").Unlocked && !File.Exists(Path.Combine(folder, "none.json")), "a store without a path keeps everything in memory");
        }
        finally { try { Directory.Delete(folder, true); } catch { } }
        Check(ElementLocks.IsValidId("btn.build.build-image") && ElementLocks.IsValidId("btn.containers.remove-selected") && !ElementLocks.IsValidId("btn.page.with space"), "page action buttons get ids the lock list accepts");
        var s = new UserSettings(); ElementLocks.Lock(s, "btn.build.build-image"); var target = new UserSettings { Language = "Cantonese" }; s.Language = "English";
        Check(ElementLocks.Preserve(s, target).Count == 0 && target.LockedSettings.SequenceEqual(new[] { "btn.build.build-image" }) && target.Language == "Cantonese", "a lock on a button has no settings value to preserve and survives an import");
    }

    static void Dpapi()
    {
        if (!DpapiProtector.IsAvailable) { Console.WriteLine("SKIP Windows DPAPI round trip (not Windows)"); return; }
        var dpapi = new DpapiProtector(); var secret = Totp.NewSecret(); var entropy = new byte[] { 1, 2, 3, 4 };
        var blob = dpapi.Protect(secret, entropy);
        Check(!blob.SequenceEqual(secret) && blob.Length > secret.Length, "DPAPI output is not the plain secret");
        Check(dpapi.Unprotect(blob, entropy)!.SequenceEqual(secret), "DPAPI round-trips a secret for the same user and entropy");
        Check(dpapi.Unprotect(blob, new byte[] { 9 }) == null, "DPAPI refuses the blob under different entropy");
        var tampered = blob.ToArray(); tampered[tampered.Length / 2] ^= 0xFF;
        Check(dpapi.Unprotect(tampered, entropy) == null, "DPAPI refuses a tampered blob");
        var folder = Path.Combine(Path.GetTempPath(), "mdi-locks-dpapi-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LockStore(Path.Combine(folder, "locks.json"), dpapi, null, 10_000);
            var s = Totp.NewSecret(); store.Configure("rate", LockPolicy.Authenticator, null, s);
            var reopened = new LockStore(Path.Combine(folder, "locks.json"), dpapi, null, 10_000);
            Check(reopened.CheckCode("rate", Totp.Code(s, DateTimeOffset.UtcNow)), "an authenticator lock stored with real DPAPI verifies after a restart");
        }
        finally { try { Directory.Delete(folder, true); } catch { } }
    }

    static void Qr()
    {
        // Published Reed-Solomon vector: "HELLO WORLD" at version 1, level Q (13 data codewords, 13 error-correction codewords).
        var data = new byte[] { 32, 91, 11, 120, 209, 114, 220, 77, 67, 64, 236, 17, 236 };
        Check(QrCode.ReedSolomon(data, 13).SequenceEqual(new byte[] { 168, 72, 22, 82, 217, 54, 156, 0, 46, 15, 180, 122, 16 }), "Reed-Solomon matches the published HELLO WORLD vector");
        Check(QrCode.FormatBits(0) == 0b111011111000100, "the format bits for level L and mask 0 match the standard");
        Check(Enumerable.Range(0, 8).All(m => { var bits = QrCode.FormatBits(m) ^ 0x5412; var rem = bits; for (var i = 14; i >= 10; i--) if (((rem >> i) & 1) == 1) rem ^= 0x537 << (i - 10); return rem == 0 && ((bits >> 13) & 3) == 1 && ((bits >> 10) & 7) == m; }), "every format word is a valid BCH codeword for level L");
        Check(QrCode.VersionBits(7) == 0x07C94 && QrCode.VersionBits(8) == 0x085BC && QrCode.VersionBits(10) == 0x0A4D3, "the version information words match the standard");
        Check(QrCode.MaxBytes == 271 && QrCode.VersionFor(new string('a', 17)) == 1 && QrCode.VersionFor(new string('a', 18)) == 2 && QrCode.VersionFor(new string('a', 271)) == 10 && QrCode.VersionFor(new string('a', 272)) == 0, "capacity per version matches the standard for byte mode, level L");
        Reject(() => QrCode.Encode(new string('a', 272)), "text beyond the capacity is refused rather than truncated");

        foreach (var length in new[] { 1, 17, 18, 32, 60, 100, 134, 150, 200, 250, 271 })
        {
            var text = new string('x', length / 2) + string.Concat(Enumerable.Range(0, length - length / 2).Select(i => (char)('A' + i % 26)));
            var matrix = QrCode.Encode(text);
            var version = QrCode.VersionFor(text);
            Check(matrix.GetLength(0) == 17 + 4 * version && QrCode.Decode(matrix) == text, $"a {length}-byte text encodes at version {version} and reads back exactly");
        }
        var uri = Totp.Uri("MaterialDockerImage", "btn.containers.remove-selected", Totp.NewSecret());
        var modules = QrCode.Encode(uri);
        Check(QrCode.Decode(modules) == uri, "a real otpauth link reads back exactly from its own matrix");
        Check(QrCode.Decode(QrCode.Encode("你好 — 驗證"))== "你好 — 驗證", "non-ASCII text is encoded as UTF-8 and read back");

        var n = modules.GetLength(0);
        bool Finder(int r0, int c0) { for (var r = 0; r < 7; r++) for (var c = 0; c < 7; c++) { var edge = r is 0 or 6 || c is 0 or 6; var core = r is >= 2 and <= 4 && c is >= 2 and <= 4; if (modules[r0 + r, c0 + c] != (edge || core)) return false; } return true; }
        Check(Finder(0, 0) && Finder(0, n - 7) && Finder(n - 7, 0), "the three finder patterns are in place");
        Check(Enumerable.Range(8, n - 16).All(i => modules[6, i] == (i % 2 == 0) && modules[i, 6] == (i % 2 == 0)), "the timing patterns alternate");
        Check(modules[n - 8, 8], "the fixed dark module is set");
        var blank = (bool[,])modules.Clone(); blank[8, 0] = !blank[8, 0]; blank[8, 1] = !blank[8, 1];
        Check(QrCode.Decode(blank) is null || QrCode.Decode(blank) != uri, "a damaged format word is not read as the original text");
        // Golden matrices. Each of these was decoded back to its text by an independent decoder (OpenCV) when recorded, so a change to
        // the mask rules, module placement or error correction turns this red even though the encoder and our own reader would still agree.
        string MatrixHash(string text) { var m = QrCode.Encode(text); var rows = Enumerable.Range(0, m.GetLength(0)).Select(r => string.Concat(Enumerable.Range(0, m.GetLength(1)).Select(c => m[r, c] ? '1' : '0'))); return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.ASCII.GetBytes(string.Join("\n", rows)))).ToLowerInvariant(); }
        Check(MatrixHash("HELLO") == "eea957f1f35505469c36db756d585873cce95b7b13c387f5fcac468dfa9e40d7", "the version 1 matrix for HELLO is the one an independent scanner read");
        Check(MatrixHash(new string('Q', 60)) == "66eaf623316d6b335575b60db89e9829e9193cde1957e75eedb97e3f231c3d18", "the version 4 matrix is the one an independent scanner read");
        Check(MatrixHash(string.Concat(Enumerable.Range(0, 150).Select(i => (char)('a' + i % 26)))) == "44211b679370926eb67961e68c697e5f38a54bd65758f0f0c2b01eabea457fb0", "the version 7 matrix (with version information) is the one an independent scanner read");
        Check(MatrixHash(string.Concat(Enumerable.Range(0, 250).Select(i => (char)('A' + i % 26)))) == "6f536b0a096df49e23adb05ddddb8e948d0694db35350eb11427f928199bd6e5", "the version 10 matrix (two block sizes) is the one an independent scanner read");
        Check(QrCode.Encode("same").Cast<bool>().SequenceEqual(QrCode.Encode("same").Cast<bool>()), "encoding is deterministic");
    }

    static void Wording()
    {
        var language = MaterialDockerImage.Services.Loc.Language; var school = MaterialDockerImage.Services.Loc.SchoolMode;
        MaterialDockerImage.Services.Loc.SchoolMode = false; MaterialDockerImage.Services.Loc.Language = "Cantonese";
        var samples = new[]
        {
            "Lock this control…", "Unlock this control…", "Change lock policy…", "Choose what unlocking should ask for.", "None (confirm only)", "Authenticator app",
            "Show secret and QR code", "Recovery codes", "I have saved these recovery codes", "Use a recovery code", "Too many wrong answers. Wait it out, or use a recovery code.",
            "Authenticator for locks", "Run RFC 6238 self-test", "Set up authenticator for this lock", "That code is valid right now.",
            "2 more tries before a wait starts.", "All 18 RFC 6238 vectors match.", "17 of 18 vectors match.", "QR code that pairs an authenticator app with the lock for theme. Scan it, or type the text secret beside it."
        };
        Check(samples.All(x => MaterialDockerImage.Services.Loc.T(x) != x), "the lock, ladder and authenticator strings have Cantonese wording");
        Check(MaterialDockerImage.Services.Loc.T("2 more tries before a wait starts.").Contains('2') && MaterialDockerImage.Services.Loc.T("17 of 18 vectors match.").Contains("17") && MaterialDockerImage.Services.Loc.T("17 of 18 vectors match.").Contains("18"), "counted sentences keep their exact numbers in Cantonese");
        MaterialDockerImage.Services.Loc.Language = "Bilingual";
        Check(MaterialDockerImage.Services.Loc.T("Recovery codes").StartsWith("Recovery codes · "), "bilingual mode keeps English then Cantonese");
        MaterialDockerImage.Services.Loc.Language = language; MaterialDockerImage.Services.Loc.SchoolMode = school;
    }
}
