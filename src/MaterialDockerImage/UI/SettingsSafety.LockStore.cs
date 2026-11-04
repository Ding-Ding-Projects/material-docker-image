using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace MaterialDockerImage.UI;

public enum LockPolicy { None, Pin, Authenticator }

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
    /// <summary>The TOTP secret, protected at rest (Base64 of the DPAPI blob). Never the plain secret.</summary>
    public string? TotpProtected { get; set; }
    public long LastTotpStep { get; set; } = -1;
    public List<string> RecoveryHashes { get; set; } = new();
    public int Failures { get; set; }
    public long WaitUntilUnix { get; set; }
}

public enum UnlockOutcome { Unlocked, Wrong, Waiting, NotConfigured, Unavailable }

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
public sealed partial class LockStore
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
            foreach (var r in shape.Locks.Take(MaxRecords)) if (ElementLocks.IsValidId(r.Id)) { r.RecoveryHashes = r.RecoveryHashes.Take(RecoveryCodes.Count * 2).ToList(); records[r.Id] = r; }
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
    public IReadOnlyList<string>? Configure(string id, LockPolicy policy, string? pin = null, byte[]? totpSecret = null)
    {
        if (!ElementLocks.IsValidId(id)) throw new ArgumentException("Invalid lock id.", nameof(id));
        if (!records.ContainsKey(id) && records.Count >= MaxRecords) throw new InvalidOperationException("The lock list is full.");
        var record = new LockRecord { Id = id, Policy = policy };
        IReadOnlyList<string>? codes = null;
        if (policy == LockPolicy.Pin)
        {
            if (!PinRules.IsValid(pin)) throw new ArgumentException(PinRules.Problem(pin), nameof(pin));
            record.PinHash = CredentialHasher.Hash(pin!, installSalt, iterations);
        }
        else if (policy == LockPolicy.Authenticator)
        {
            if (totpSecret == null || totpSecret.Length < 10) throw new ArgumentException("An authenticator lock needs a secret of at least 10 bytes.", nameof(totpSecret));
            if (protector == null) throw new InvalidOperationException("No secret protection is available, so an authenticator lock cannot be stored.");
            record.TotpProtected = Convert.ToBase64String(protector.Protect(totpSecret, installSalt));
        }
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

    /// <summary>Checks a PIN or authenticator code. During a wait the answer is refused unchecked and uncounted; a wrong answer extends the wait.</summary>
    public UnlockResult Attempt(string id, string? input)
    {
        var r = Get(id);
        if (r == null || r.Policy == LockPolicy.None) return new UnlockResult(UnlockOutcome.Unlocked);
        if (WaitUntil(id) is { } wait) return new UnlockResult(UnlockOutcome.Waiting, 0, wait);
        bool ok = false;
        if (r.Policy == LockPolicy.Pin) ok = input != null && CredentialHasher.Verify(input, r.PinHash, installSalt);
        else
        {
            var secret = ReadSecret(r);
            if (secret == null) return new UnlockResult(UnlockOutcome.Unavailable);
            var step = Totp.Verify(secret, input, clock(), r.LastTotpStep);
            CryptographicOperations.ZeroMemory(secret);
            if (step >= 0) { r.LastTotpStep = step; ok = true; }
        }
        return Finish(r, ok);
    }

    /// <summary>Checks an authenticator code without consuming its step. Used while setting up and by the Authenticator tool.</summary>
    public bool CheckCode(string id, string? code)
    {
        var r = Get(id);
        if (r?.Policy != LockPolicy.Authenticator) return false;
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

    private UnlockResult Finish(LockRecord r, bool ok)
    {
        if (ok) { r.Failures = 0; r.WaitUntilUnix = 0; Save(); return new UnlockResult(UnlockOutcome.Unlocked); }
        r.Failures++;
        var wait = AttemptLimiter.WaitAfter(r.Failures);
        if (wait > TimeSpan.Zero) r.WaitUntilUnix = (clock() + wait).ToUnixTimeSeconds();
        Save();
        return new UnlockResult(UnlockOutcome.Wrong, AttemptLimiter.FreeAttemptsLeft(r.Failures), wait > TimeSpan.Zero ? clock() + wait : null);
    }

    private byte[]? ReadSecret(LockRecord r)
    {
        if (protector == null || r.TotpProtected == null) return null;
        try { return protector.Unprotect(Convert.FromBase64String(r.TotpProtected), installSalt); }
        catch (Exception e) when (e is FormatException or CryptographicException) { return null; }
    }
}
