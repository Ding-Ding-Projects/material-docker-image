using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace MaterialDockerImage.UI;

/// <summary>
/// What unlocking asks for. The numeric values are stored in locks.json, so existing values never change: new policies are only appended.
/// <see cref="Authenticator"/> is the authenticator code on its own; the combined policies ask for every listed factor in a fixed order (PIN, password, authenticator).
/// </summary>
public enum LockPolicy { None = 0, Pin = 1, Authenticator = 2, Password = 3, PinPassword = 4, PasswordTotp = 5, PinTotp = 6, PasswordPinTotp = 7 }

/// <summary>Protects a secret at rest. The app uses Windows DPAPI for the current user; checks may substitute their own.</summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] plain, byte[] entropy);
    byte[]? Unprotect(byte[] protectedBytes, byte[] entropy);
}

/// <summary>Windows DPAPI, current-user scope, no UI. A blob only opens for the same Windows user and the same entropy.</summary>
public sealed class DpapiProtector : ISecretProtector
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref Blob input, string? description, ref Blob entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, ref Blob entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr handle);
    private const int UiForbidden = 1;

    public static bool IsAvailable => OperatingSystem.IsWindows();

    public byte[] Protect(byte[] plain, byte[] entropy) => Run(plain, entropy, true) ?? throw new CryptographicException("The secret could not be protected for this Windows user.");
    public byte[]? Unprotect(byte[] protectedBytes, byte[] entropy) => Run(protectedBytes, entropy, false);

    private static byte[]? Run(byte[] data, byte[] entropy, bool protect)
    {
        if (!IsAvailable) throw new PlatformNotSupportedException("Windows DPAPI is not available here.");
        var input = Make(data); var salt = Make(entropy);
        try
        {
            Blob output;
            var ok = protect ? CryptProtectData(ref input, null, ref salt, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output) : CryptUnprotectData(ref input, IntPtr.Zero, ref salt, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output);
            if (!ok) return null;
            try { var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, output.Length); return result; }
            finally { LocalFree(output.Data); }
        }
        finally { Marshal.FreeHGlobal(input.Data); Marshal.FreeHGlobal(salt.Data); }
    }

    private static Blob Make(byte[] bytes)
    {
        var pointer = Marshal.AllocHGlobal(Math.Max(1, bytes.Length));
        if (bytes.Length > 0) Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return new Blob { Length = bytes.Length, Data = pointer };
    }
}

/// <summary>One lock's own credential and attempt state. Nothing in here is ever logged, exported or put in history.</summary>
public sealed class LockRecord
{
    public string Id { get; set; } = "";
    public LockPolicy Policy { get; set; }
    public string? PinHash { get; set; }
    /// <summary>Salted PBKDF2 hash of the password. The password itself is never stored or logged.</summary>
    public string? PasswordHash { get; set; }
    /// <summary>How long an unlock lasts. Records written before durations existed read as <see cref="UnlockDuration.Manual"/>.</summary>
    public UnlockDuration Duration { get; set; }
    /// <summary>True while the lock is unlocked under a duration, so a restart or the timer knows to lock it again.</summary>
    public bool GrantActive { get; set; }
    /// <summary>When a timed unlock ends (Unix seconds); zero for the other durations.</summary>
    public long GrantUntilUnix { get; set; }
    /// <summary>The TOTP secret, protected at rest (Base64 of the DPAPI blob). Never the plain secret.</summary>
    public string? TotpProtected { get; set; }
    public long LastTotpStep { get; set; } = -1;
    public List<string> RecoveryHashes { get; set; } = new();
    public int Failures { get; set; }
    public long WaitUntilUnix { get; set; }
}

/// <summary><see cref="NextFactor"/> means one factor of a combined policy was right and the next is needed; nothing is unlocked yet.</summary>
public enum UnlockOutcome { Unlocked, Wrong, Waiting, NotConfigured, Unavailable, NextFactor }

public sealed record UnlockResult(UnlockOutcome Outcome, int FreeAttemptsLeft = 0, DateTimeOffset? WaitUntil = null)
{
    public bool Unlocked => Outcome == UnlockOutcome.Unlocked;
}

/// <summary>The rungs of the unlock ladder: confirm, credential, the honest wait, and the recovery code.</summary>
public enum LadderStage { Confirm, Credential, Wait, Recovery }

/// <summary>
/// Credentials for the app's own locks, kept in their own file apart from settings.json, exports and history.
/// A lock with no policy only asks for a confirmation. PIN and authenticator locks check a credential, are rate limited
/// with a persisted exponential backoff, and can be cleared by one of the single-use recovery codes made at setup.
/// A lock is still a speed bump for oneself, not security: anyone who can delete the data folder can remove it.
/// </summary>
public sealed class LockStore
{
    public const int MaxRecords = 200;
    private readonly string? path;
    private readonly ISecretProtector? protector;
    private readonly Func<DateTimeOffset> clock;
    private readonly int iterations;
    private readonly Dictionary<string, LockRecord> records = new(StringComparer.Ordinal);
    private byte[] installSalt = RandomNumberGenerator.GetBytes(16);

    /// <summary>A null path keeps everything in memory (capture mode and checks).</summary>
    public LockStore(string? path, ISecretProtector? protector, Func<DateTimeOffset>? clock = null, int iterations = CredentialHasher.Iterations)
    {
        this.path = path; this.protector = protector; this.clock = clock ?? (() => DateTimeOffset.UtcNow); this.iterations = iterations;
        Load();
    }

    public static string DefaultPath => Path.Combine(UserSettings.DataDirectory, "locks.json");
    public IReadOnlyCollection<LockRecord> Records => records.Values;
    public byte[] InstallSalt => installSalt;
    public LockRecord? Get(string id) => records.TryGetValue(id, out var r) ? r : null;
    public LockPolicy PolicyOf(string id) => Get(id)?.Policy ?? LockPolicy.None;
    public bool HasPolicy(string id) => records.ContainsKey(id);
    public int RecoveryLeft(string id) => Get(id)?.RecoveryHashes.Count ?? 0;

    private sealed class FileShape { public int Version { get; set; } = 1; public string InstallSalt { get; set; } = ""; public List<LockRecord> Locks { get; set; } = new(); }

    private void Load()
    {
        if (path == null || !File.Exists(path)) return;
        try
        {
            var shape = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(path));
            if (shape == null) return;
            var salt = Convert.FromBase64String(shape.InstallSalt);
            if (salt.Length == 16) installSalt = salt;
            foreach (var r in shape.Locks.Take(MaxRecords)) if (ElementLocks.IsValidId(r.Id) && Enum.IsDefined(r.Policy)) { if (!Enum.IsDefined(r.Duration)) r.Duration = UnlockDuration.Manual; r.RecoveryHashes = r.RecoveryHashes.Take(RecoveryCodes.Count * 2).ToList(); records[r.Id] = r; }
        }
        catch (Exception e) when (e is JsonException or FormatException or IOException or UnauthorizedAccessException) { records.Clear(); LoadError = "The lock file could not be read, so its locks were ignored: " + e.Message; }
    }

    public string? LoadError { get; private set; }

    private void Save()
    {
        if (path == null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new FileShape { InstallSalt = Convert.ToBase64String(installSalt), Locks = records.Values.OrderBy(r => r.Id, StringComparer.Ordinal).ToList() }, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }

    /// <summary>
    /// Sets or replaces one lock's policy. Returns the recovery codes in plain text exactly once (they are stored only as hashes),
    /// or null for a confirm-only lock. Throws for an invalid PIN or authenticator secret; nothing is stored in that case.
    /// </summary>
    public IReadOnlyList<string>? Configure(string id, LockPolicy policy, string? pin = null, byte[]? totpSecret = null, string? password = null, UnlockDuration duration = UnlockDuration.UntilRestart)
    {
        var factors = LockPolicies.Factors(policy);
        if (!ElementLocks.IsValidId(id)) throw new ArgumentException("Invalid lock id.", nameof(id));
        if (!records.ContainsKey(id) && records.Count >= MaxRecords) throw new InvalidOperationException("The lock list is full.");
        if (!Enum.IsDefined(policy)) throw new ArgumentException("Unknown lock policy.", nameof(policy));
        if (!Enum.IsDefined(duration) || duration == UnlockDuration.Manual) duration = UnlockDuration.UntilRestart;
        var record = new LockRecord { Id = id, Policy = policy, Duration = duration };
        IReadOnlyList<string>? codes = null;
        if (factors.HasFlag(LockFactors.Pin))
        {
            if (!PinRules.IsValid(pin)) throw new ArgumentException(PinRules.Problem(pin), nameof(pin));
        }
        if (factors.HasFlag(LockFactors.Password))
        {
            if (!PasswordRules.IsValid(password)) throw new ArgumentException(PasswordRules.Problem(password), nameof(password));
        }
        if (factors.HasFlag(LockFactors.Totp))
        {
            if (totpSecret == null || totpSecret.Length < 10) throw new ArgumentException("An authenticator lock needs a secret of at least 10 bytes.", nameof(totpSecret));
            if (protector == null) throw new InvalidOperationException("No secret protection is available, so an authenticator lock cannot be stored.");
            record.TotpProtected = Convert.ToBase64String(protector.Protect(totpSecret, installSalt));
        }
        if (factors.HasFlag(LockFactors.Pin)) record.PinHash = CredentialHasher.Hash(pin!, installSalt, iterations);
        if (factors.HasFlag(LockFactors.Password)) record.PasswordHash = CredentialHasher.Hash(password!, installSalt, iterations);
        if (policy != LockPolicy.None)
        {
            codes = RecoveryCodes.Generate();
            record.RecoveryHashes = codes.Select(c => CredentialHasher.Hash(RecoveryCodes.Normalize(c), installSalt, iterations)).ToList();
        }
        records[id] = record;
        Save();
        return codes;
    }

    public bool Remove(string id) { var removed = records.Remove(id); if (removed) Save(); return removed; }

    /// <summary>The honest wait state for the ladder, or null when no wait is running.</summary>
    public DateTimeOffset? WaitUntil(string id)
    {
        var r = Get(id);
        if (r == null || r.WaitUntilUnix <= 0) return null;
        var until = DateTimeOffset.FromUnixTimeSeconds(r.WaitUntilUnix);
        return until > clock() ? until : null;
    }

    /// <summary>Which rung the person is on, from the stored state alone. The recovery rung is entered by choice, never by default.</summary>
    public LadderStage Stage(string id)
    {
        var r = Get(id);
        if (r == null || r.Policy == LockPolicy.None) return LadderStage.Confirm;
        return WaitUntil(id) != null ? LadderStage.Wait : LadderStage.Credential;
    }

    /// <summary>
    /// Checks the one credential of a single-factor policy (PIN, password or authenticator code). During a wait the answer is refused unchecked and uncounted;
    /// a wrong answer extends the wait. A combined policy is never answered by one value: use <see cref="Begin"/> for it, and this method refuses without counting.
    /// </summary>
    public UnlockResult Attempt(string id, string? input)
    {
        var r = Get(id);
        if (r == null || r.Policy == LockPolicy.None) return new UnlockResult(UnlockOutcome.Unlocked);
        if (LockPolicies.Steps(r.Policy).Count != 1) return new UnlockResult(UnlockOutcome.Wrong);
        return Begin(id)!.Submit(input);
    }

    /// <summary>Starts one bounded attempt at a lock with a policy, walking its factors in order. Null for a lock with no policy.</summary>
    public UnlockAttempt? Begin(string id)
    {
        var r = Get(id);
        return r == null || r.Policy == LockPolicy.None ? null : new UnlockAttempt(this, id, LockPolicies.Steps(r.Policy));
    }

    internal enum FactorCheck { Ok, Wrong, Unavailable }

    /// <summary>Checks one factor against a record without touching the failure count. The authenticator step is returned, not yet spent.</summary>
    internal FactorCheck VerifyFactor(string id, LockFactors factor, string? input, out long totpStep)
    {
        totpStep = -1;
        var r = Get(id);
        if (r == null || input == null) return FactorCheck.Wrong;
        switch (factor)
        {
            case LockFactors.Pin: return CredentialHasher.Verify(input, r.PinHash, installSalt) ? FactorCheck.Ok : FactorCheck.Wrong;
            case LockFactors.Password: return CredentialHasher.Verify(input, r.PasswordHash, installSalt) ? FactorCheck.Ok : FactorCheck.Wrong;
            case LockFactors.Totp:
                var secret = ReadSecret(r);
                if (secret == null) return FactorCheck.Unavailable;
                var step = Totp.Verify(secret, input, clock(), r.LastTotpStep);
                CryptographicOperations.ZeroMemory(secret);
                if (step < 0) return FactorCheck.Wrong;
                totpStep = step;
                return FactorCheck.Ok;
            default: return FactorCheck.Wrong;
        }
    }

    internal UnlockResult RegisterFailure(string id)
    {
        var r = Get(id)!;
        r.Failures++;
        var wait = AttemptLimiter.WaitAfter(r.Failures);
        if (wait > TimeSpan.Zero) r.WaitUntilUnix = (clock() + wait).ToUnixTimeSeconds();
        Save();
        return new UnlockResult(UnlockOutcome.Wrong, AttemptLimiter.FreeAttemptsLeft(r.Failures), wait > TimeSpan.Zero ? clock() + wait : null);
    }

    internal UnlockResult RegisterSuccess(string id, long totpStep)
    {
        var r = Get(id)!;
        r.Failures = 0; r.WaitUntilUnix = 0;
        if (totpStep >= 0) r.LastTotpStep = totpStep;
        Save();
        return new UnlockResult(UnlockOutcome.Unlocked);
    }

    /// <summary>Checks an authenticator code without consuming its step. Used while setting up and by the Authenticator tool.</summary>
    public bool CheckCode(string id, string? code)
    {
        var r = Get(id);
        if (r == null || !LockPolicies.Factors(r.Policy).HasFlag(LockFactors.Totp)) return false;
        var secret = ReadSecret(r);
        if (secret == null) return false;
        var step = Totp.Verify(secret, code, clock());
        CryptographicOperations.ZeroMemory(secret);
        return step >= 0;
    }

    /// <summary>Spends one recovery code. It works during a wait too, is single-use, and clears the wait and the failure count.</summary>
    public UnlockResult UseRecoveryCode(string id, string? code)
    {
        var r = Get(id);
        if (r == null || r.Policy == LockPolicy.None) return new UnlockResult(UnlockOutcome.NotConfigured);
        var normal = RecoveryCodes.Normalize(code);
        if (!RecoveryCodes.LooksValid(normal)) return new UnlockResult(UnlockOutcome.Wrong, 0);
        var hit = r.RecoveryHashes.FirstOrDefault(h => CredentialHasher.Verify(normal, h, installSalt));
        if (hit == null) return new UnlockResult(UnlockOutcome.Wrong, 0);
        r.RecoveryHashes.Remove(hit); r.Failures = 0; r.WaitUntilUnix = 0;
        Save();
        return new UnlockResult(UnlockOutcome.Unlocked);
    }


    // ---- Unlock durations ----------------------------------------------------------------------------------------------

    /// <summary>Starts the unlock grant for a lock that was just unlocked. Returns when a timed grant ends, or null for the other durations.</summary>
    public DateTimeOffset? StartGrant(string id)
    {
        var r = Get(id);
        if (r == null || r.Duration == UnlockDuration.Manual) return null;
        r.GrantActive = true;
        r.GrantUntilUnix = UnlockDurations.Span(r.Duration) is { } span ? (clock() + span).ToUnixTimeSeconds() : 0;
        Save();
        return r.GrantUntilUnix > 0 ? DateTimeOffset.FromUnixTimeSeconds(r.GrantUntilUnix) : null;
    }

    /// <summary>Ends a grant (the lock is being locked again, or removed). Safe to call for a lock with no grant.</summary>
    public void EndGrant(string id)
    {
        var r = Get(id);
        if (r == null || !r.GrantActive && r.GrantUntilUnix == 0) return;
        r.GrantActive = false; r.GrantUntilUnix = 0;
        Save();
    }

    public bool GrantIsActive(string id) => Get(id)?.GrantActive == true;
    public UnlockDuration DurationOf(string id) => Get(id)?.Duration ?? UnlockDuration.Manual;
    /// <summary>The soonest end among timed grants still running, or null when none is.</summary>
    public DateTimeOffset? NextExpiry() => records.Values.Where(r => r.GrantActive && r.GrantUntilUnix > 0).Select(r => DateTimeOffset.FromUnixTimeSeconds(r.GrantUntilUnix)).Cast<DateTimeOffset?>().Min();
    public DateTimeOffset Now => clock();

    private byte[]? ReadSecret(LockRecord r)
    {
        if (protector == null || r.TotpProtected == null) return null;
        try { return protector.Unprotect(Convert.FromBase64String(r.TotpProtected), installSalt); }
        catch (Exception e) when (e is FormatException or CryptographicException) { return null; }
    }
}
