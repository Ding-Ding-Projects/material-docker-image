using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MaterialDockerImage.UI;

/// <summary>
/// One account in the general authenticator. The secret is held as Base32 text only inside this object; the object never prints it
/// (<see cref="ToString"/> shows issuer and name), and it is only written to disk inside a DPAPI-protected blob or an encrypted backup.
/// </summary>
public sealed class OtpAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Issuer { get; set; } = "";
    public string Name { get; set; } = "";
    public string SecretBase32 { get; set; } = "";
    public string Algorithm { get; set; } = Totp.DefaultAlgorithm;
    public int Digits { get; set; } = Totp.DefaultDigits;
    public int Period { get; set; } = Totp.DefaultPeriod;

    [System.Text.Json.Serialization.JsonIgnore]
    public string Label => Issuer.Length == 0 ? Name : Name.Length == 0 ? Issuer : Issuer + " · " + Name;
    public byte[]? SecretBytes() => Base32.Decode(SecretBase32);
    public override string ToString() => Label;
    public OtpAccount Clone() => (OtpAccount)MemberwiseClone();
}

/// <summary>Validation shared by the otpauth:// parser, manual entry, the vault and encrypted import.</summary>
public static class OtpAccountRules
{
    public const int MaxIssuer = 60, MaxName = 100, MinSecretBytes = 10, MaxSecretBytes = 128, MaxAccounts = 200;
    public static readonly string[] Algorithms = ["SHA1", "SHA256", "SHA512"];

    /// <summary>Returns a readable problem for the account, or null when it is usable.</summary>
    public static string? Validate(OtpAccount a)
    {
        if (a.Issuer.Length > MaxIssuer || a.Issuer.Any(char.IsControl)) return $"The issuer must be at most {MaxIssuer} characters without control characters.";
        if (a.Name.Length > MaxName || a.Name.Any(char.IsControl)) return $"The account name must be at most {MaxName} characters without control characters.";
        if (a.Issuer.Trim().Length == 0 && a.Name.Trim().Length == 0) return "Enter an issuer or an account name.";
        var secret = Base32.Decode(a.SecretBase32);
        if (secret == null) return "The secret must be Base32 text (letters A to Z and digits 2 to 7).";
        if (secret.Length < MinSecretBytes) return "The secret is too short to be a real authenticator secret.";
        if (secret.Length > MaxSecretBytes) return "The secret is too long.";
        if (!Algorithms.Contains(a.Algorithm, StringComparer.Ordinal)) return "The algorithm must be SHA1, SHA256 or SHA512.";
        if (a.Digits is < 6 or > 8) return "Codes have 6 to 8 digits.";
        if (a.Period is < 1 or > 300) return "The period must be between 1 and 300 seconds.";
        if (a.Id.Length is 0 or > 64 || a.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')) return "The account id is not valid.";
        return null;
    }

    /// <summary>Builds an account from manual entry. Spaces, hyphens and lower case in the secret are tolerated.</summary>
    public static bool TryCreate(string? issuer, string? name, string? secret, string algorithm, int digits, int period, out OtpAccount? account, out string error)
    {
        account = null;
        var normalized = new string((secret ?? "").Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant().TrimEnd('=');
        var candidate = new OtpAccount { Issuer = (issuer ?? "").Trim(), Name = (name ?? "").Trim(), SecretBase32 = normalized, Algorithm = algorithm.ToUpperInvariant(), Digits = digits, Period = period };
        var problem = Validate(candidate);
        if (problem != null) { error = problem; return false; }
        account = candidate; error = "";
        return true;
    }
}

/// <summary>Parses the standard otpauth://totp/ link that authenticator QR codes carry. Parameters in the link are honoured, never replaced by defaults.</summary>
public static class OtpauthUri
{
    public const int MaxLength = 2048;

    public static bool TryParse(string? text, out OtpAccount? account, out string error)
    {
        account = null;
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) { error = "Paste an otpauth:// link."; return false; }
        if (text.Length > MaxLength) { error = "That link is too long to be an authenticator link."; return false; }
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || !uri.Scheme.Equals("otpauth", StringComparison.OrdinalIgnoreCase)) { error = "That is not an otpauth:// link."; return false; }
        if (uri.Host.Equals("hotp", StringComparison.OrdinalIgnoreCase)) { error = "Counter-based (HOTP) accounts are not supported. Only time-based (TOTP) accounts are."; return false; }
        if (!uri.Host.Equals("totp", StringComparison.OrdinalIgnoreCase)) { error = "The link must be of the form otpauth://totp/…"; return false; }

        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var key = Uri.UnescapeDataString((eq < 0 ? pair : pair[..eq]).Replace('+', ' '));
            var value = eq < 0 ? "" : Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
            if (!query.TryAdd(key, value)) { error = $"The link repeats the \"{(key.Length > 20 ? key[..20] : key)}\" parameter."; return false; }
        }

        var label = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        var colon = label.IndexOf(':');
        var labelIssuer = colon < 0 ? "" : label[..colon].Trim();
        var labelName = (colon < 0 ? label : label[(colon + 1)..]).Trim();
        var issuer = query.TryGetValue("issuer", out var fromParameter) && fromParameter.Trim().Length > 0 ? fromParameter.Trim() : labelIssuer;

        var algorithm = Totp.DefaultAlgorithm;
        if (query.TryGetValue("algorithm", out var algorithmText))
        {
            algorithm = algorithmText.Trim().ToUpperInvariant();
            if (!OtpAccountRules.Algorithms.Contains(algorithm)) { error = "The link asks for an algorithm that is not supported. Use SHA1, SHA256 or SHA512."; return false; }
        }
        var digits = Totp.DefaultDigits;
        if (query.TryGetValue("digits", out var digitsText) && !int.TryParse(digitsText, NumberStyles.None, CultureInfo.InvariantCulture, out digits)) { error = "The digits value in the link is not a number."; return false; }
        var period = Totp.DefaultPeriod;
        if (query.TryGetValue("period", out var periodText) && !int.TryParse(periodText, NumberStyles.None, CultureInfo.InvariantCulture, out period)) { error = "The period value in the link is not a number."; return false; }
        if (!query.TryGetValue("secret", out var secret)) { error = "The link has no secret."; return false; }
        if (labelName.Length == 0) labelName = issuer.Length > 0 ? "" : "Account";
        return OtpAccountRules.TryCreate(issuer, labelName, secret, algorithm, digits, period, out account, out error);
    }

    /// <summary>Builds the link for an account. Only used to prove the round trip and for a deliberate, confirmed secret export; the app never shows it by default.</summary>
    public static string Build(OtpAccount a)
    {
        var label = (a.Issuer.Length > 0 ? Uri.EscapeDataString(a.Issuer) + ":" : "") + Uri.EscapeDataString(a.Name);
        var issuer = a.Issuer.Length > 0 ? "&issuer=" + Uri.EscapeDataString(a.Issuer) : "";
        return $"otpauth://totp/{label}?secret={a.SecretBase32}{issuer}&algorithm={a.Algorithm}&digits={a.Digits}&period={a.Period}";
    }
}

/// <summary>The code shown for an account right now: the current code, the next one, and the seconds left.</summary>
public readonly record struct OtpReading(string Code, string NextCode, int SecondsLeft, int Period)
{
    /// <summary>"123 456" for six digits, "1234 5678" for eight: grouped for reading aloud and typing.</summary>
    public static string Group(string code)
    {
        var half = (code.Length + 1) / 2;
        return code.Length < 6 ? code : code[..half] + " " + code[half..];
    }

    public string Grouped => Group(Code);
    public string NextGrouped => Group(NextCode);
    /// <summary>True in the last few seconds of a period, when the next code is the one to type.</summary>
    public bool Expiring => SecondsLeft <= 5;
}

public static class OtpClock
{
    public static OtpReading Read(OtpAccount account, DateTimeOffset now)
    {
        var secret = account.SecretBytes() ?? throw new InvalidOperationException("The account has no readable secret.");
        var step = Totp.Step(now, account.Period);
        return new OtpReading(Totp.Hotp(secret, step, account.Digits, account.Algorithm), Totp.Hotp(secret, step + 1, account.Digits, account.Algorithm), Totp.SecondsLeft(now, account.Period), account.Period);
    }
}

/// <summary>Labelled sample accounts for capture mode, so a screenshot never shows a real secret or a real code.</summary>
public static class AuthenticatorSample
{
    public static IReadOnlyList<OtpAccount> Accounts { get; } =
    [
        new OtpAccount { Id = "sample-1", Issuer = "Example Mail", Name = "sample@example.invalid", SecretBase32 = Base32.Encode(Encoding.ASCII.GetBytes("sample-secret-0001")) },
        new OtpAccount { Id = "sample-2", Issuer = "Example Cloud", Name = "demo-user", SecretBase32 = Base32.Encode(Encoding.ASCII.GetBytes("sample-secret-0002")), Digits = 8 },
        new OtpAccount { Id = "sample-3", Issuer = "Example Bank", Name = "sample account", SecretBase32 = Base32.Encode(Encoding.ASCII.GetBytes("sample-secret-0003")), Algorithm = "SHA256", Period = 60 }
    ];
}

/// <summary>
/// The list of accounts, kept in one file protected with the Windows account (DPAPI in the app, a stand-in in checks). The whole
/// document is encrypted, so issuers and names are protected as well as secrets. A file that cannot be opened is never overwritten.
/// </summary>
public sealed class AuthenticatorVault
{
    public const int MaxFileBytes = 512 * 1024;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MaterialDockerImage.Authenticator.Accounts.v1");
    private readonly ISecretProtector protector;
    private readonly string path;
    private readonly List<OtpAccount> accounts = new();

    public AuthenticatorVault(ISecretProtector protector, string path) { this.protector = protector; this.path = path; }

    public IReadOnlyList<OtpAccount> Accounts => accounts;
    /// <summary>Why the file could not be read, or null. While set, every change is refused so the file is not replaced.</summary>
    public string? Problem { get; private set; }

    private sealed class Document { public int Version { get; set; } = 1; public List<OtpAccount> Accounts { get; set; } = new(); }
    private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };

    public void Load()
    {
        accounts.Clear(); Problem = null;
        if (!File.Exists(path)) return;
        try
        {
            if (new FileInfo(path).Length > MaxFileBytes) throw new InvalidDataException("The authenticator file is larger than 512 KiB.");
            var plain = protector.Unprotect(File.ReadAllBytes(path), Entropy) ?? throw new InvalidDataException("The authenticator file cannot be opened by this Windows account.");
            Document document;
            try { document = JsonSerializer.Deserialize<Document>(plain, Json) ?? throw new InvalidDataException("The authenticator file is empty."); }
            finally { CryptographicOperations.ZeroMemory(plain); }
            if (document.Version != 1) throw new InvalidDataException("The authenticator file has an unsupported version.");
            if (document.Accounts.Count > OtpAccountRules.MaxAccounts) throw new InvalidDataException("The authenticator file holds too many accounts.");
            foreach (var account in document.Accounts)
            {
                if (OtpAccountRules.Validate(account) is { } problem) throw new InvalidDataException("An account in the file is not valid: " + problem);
                accounts.Add(account);
            }
        }
        catch (Exception e) when (e is InvalidDataException or JsonException or IOException or UnauthorizedAccessException)
        {
            accounts.Clear();
            Problem = e is JsonException ? "The authenticator file is not valid." : e.Message;
        }
    }

    private void Save()
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(new Document { Accounts = accounts }, Json);
        try
        {
            var blob = protector.Protect(plain, Entropy);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporary, blob);
            File.Move(temporary, path, true);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public bool IsDuplicate(OtpAccount a) => accounts.Any(x => x.Issuer == a.Issuer && x.Name == a.Name && x.SecretBase32 == a.SecretBase32 && x.Algorithm == a.Algorithm && x.Digits == a.Digits && x.Period == a.Period);

    /// <summary>Adds an account. Returns null when added, otherwise the reason it was not.</summary>
    public string? Add(OtpAccount account)
    {
        if (Problem != null) return Problem;
        if (OtpAccountRules.Validate(account) is { } problem) return problem;
        if (accounts.Count >= OtpAccountRules.MaxAccounts) return $"At most {OtpAccountRules.MaxAccounts} accounts can be stored.";
        if (IsDuplicate(account)) return "That account is already stored.";
        accounts.Add(account.Clone());
        Save();
        return null;
    }

    /// <summary>Adds many accounts (an import). Duplicates are skipped; nothing is added if the result would exceed the limit.</summary>
    public (int Added, int Skipped, string? Error) AddMany(IEnumerable<OtpAccount> incoming)
    {
        if (Problem != null) return (0, 0, Problem);
        var fresh = new List<OtpAccount>(); var skipped = 0;
        foreach (var a in incoming)
        {
            if (OtpAccountRules.Validate(a) != null || IsDuplicate(a) || fresh.Any(f => f.SecretBase32 == a.SecretBase32 && f.Issuer == a.Issuer && f.Name == a.Name)) { skipped++; continue; }
            fresh.Add(a.Clone());
        }
        if (accounts.Count + fresh.Count > OtpAccountRules.MaxAccounts) return (0, 0, $"Importing would exceed the limit of {OtpAccountRules.MaxAccounts} accounts.");
        foreach (var a in fresh) { if (accounts.Any(x => x.Id == a.Id)) a.Id = Guid.NewGuid().ToString("N"); accounts.Add(a); }
        if (fresh.Count > 0) Save();
        return (fresh.Count, skipped, null);
    }

    public string? Rename(string id, string issuer, string name)
    {
        if (Problem != null) return Problem;
        var account = accounts.FirstOrDefault(a => a.Id == id);
        if (account == null) return "That account no longer exists.";
        var candidate = account.Clone(); candidate.Issuer = issuer.Trim(); candidate.Name = name.Trim();
        if (OtpAccountRules.Validate(candidate) is { } problem) return problem;
        account.Issuer = candidate.Issuer; account.Name = candidate.Name;
        Save();
        return null;
    }

    public bool Delete(string id)
    {
        if (Problem != null) return false;
        var removed = accounts.RemoveAll(a => a.Id == id) > 0;
        if (removed) Save();
        return removed;
    }

    /// <summary>Moves an account up (-1) or down (+1) in the list.</summary>
    public bool Move(string id, int delta)
    {
        if (Problem != null) return false;
        var index = accounts.FindIndex(a => a.Id == id);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= accounts.Count) return false;
        (accounts[index], accounts[target]) = (accounts[target], accounts[index]);
        Save();
        return true;
    }
}

/// <summary>
/// Encrypted backup of the accounts: AES-256-GCM with a key derived from a passphrase by PBKDF2-SHA256, a random salt and nonce, and the
/// header bound as associated data. The file never holds a secret, an issuer or a name in readable form.
/// </summary>
public static class AuthenticatorBackup
{
    public const string FormatName = "material-docker-image.authenticator-backup";
    public const int DefaultIterations = 600_000, MinIterations = 100_000, MaxIterations = 2_000_000, MinPassphrase = 8, MaxFileBytes = 512 * 1024;

    private sealed class Envelope
    {
        public string Format { get; set; } = FormatName;
        public int Version { get; set; } = 1;
        public string Kdf { get; set; } = "PBKDF2-SHA256";
        public int Iterations { get; set; }
        public string Salt { get; set; } = "";
        public string Nonce { get; set; } = "";
        public string Ciphertext { get; set; } = "";
        public string Tag { get; set; } = "";
    }

    private sealed class Payload { public List<OtpAccount> Accounts { get; set; } = new(); }
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    private static readonly JsonSerializerOptions PayloadOptions = new() { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };

    public static string? PassphraseProblem(string? passphrase) => passphrase == null || passphrase.Length < MinPassphrase ? $"Use a passphrase of at least {MinPassphrase} characters." : passphrase.Length > 256 ? "Use a passphrase of at most 256 characters." : null;

    private static byte[] Key(string passphrase, byte[] salt, int iterations) => Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt, iterations, HashAlgorithmName.SHA256, 32);
    private static byte[] Header(int version, int iterations) => Encoding.ASCII.GetBytes($"{FormatName}|{version}|PBKDF2-SHA256|{iterations}");

    public static string Export(IEnumerable<OtpAccount> accounts, string passphrase, int iterations = DefaultIterations)
    {
        if (PassphraseProblem(passphrase) is { } problem) throw new ArgumentException(problem);
        if (iterations is < MinIterations or > MaxIterations) throw new ArgumentOutOfRangeException(nameof(iterations));
        var plain = JsonSerializer.SerializeToUtf8Bytes(new Payload { Accounts = accounts.ToList() }, PayloadOptions);
        var salt = RandomNumberGenerator.GetBytes(16); var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length]; var tag = new byte[16];
        var key = Key(passphrase, salt, iterations);
        try { using var aes = new AesGcm(key, 16); aes.Encrypt(nonce, plain, cipher, tag, Header(1, iterations)); }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
        return JsonSerializer.Serialize(new Envelope { Iterations = iterations, Salt = Convert.ToBase64String(salt), Nonce = Convert.ToBase64String(nonce), Ciphertext = Convert.ToBase64String(cipher), Tag = Convert.ToBase64String(tag) }, Options);
    }

    /// <summary>Decrypts and validates a backup. Throws <see cref="InvalidDataException"/> with a readable reason; nothing is partly returned.</summary>
    public static List<OtpAccount> Import(string text, string passphrase)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaxFileBytes) throw new InvalidDataException("The file is larger than 512 KiB, so it is not an authenticator backup.");
        Envelope envelope;
        try { envelope = JsonSerializer.Deserialize<Envelope>(text, Options) ?? throw new InvalidDataException("The file is empty."); }
        catch (JsonException) { throw new InvalidDataException("The file is not an authenticator backup."); }
        if (envelope.Format != FormatName || envelope.Version != 1 || envelope.Kdf != "PBKDF2-SHA256") throw new InvalidDataException("The file is not a supported authenticator backup.");
        if (envelope.Iterations is < MinIterations or > MaxIterations) throw new InvalidDataException("The backup uses an unsupported key setting.");
        byte[] salt, nonce, cipher, tag;
        try { salt = Convert.FromBase64String(envelope.Salt); nonce = Convert.FromBase64String(envelope.Nonce); cipher = Convert.FromBase64String(envelope.Ciphertext); tag = Convert.FromBase64String(envelope.Tag); }
        catch (FormatException) { throw new InvalidDataException("The backup is damaged."); }
        if (salt.Length != 16 || nonce.Length != 12 || tag.Length != 16 || cipher.Length == 0) throw new InvalidDataException("The backup is damaged.");
        var key = Key(passphrase ?? "", salt, envelope.Iterations);
        var plain = new byte[cipher.Length];
        try
        {
            try { using var aes = new AesGcm(key, 16); aes.Decrypt(nonce, cipher, tag, plain, Header(envelope.Version, envelope.Iterations)); }
            catch (CryptographicException) { throw new InvalidDataException("The passphrase is wrong, or the file was changed."); }
            Payload payload;
            try { payload = JsonSerializer.Deserialize<Payload>(plain, PayloadOptions) ?? throw new InvalidDataException("The backup is empty."); }
            catch (JsonException) { throw new InvalidDataException("The backup is damaged."); }
            if (payload.Accounts.Count > OtpAccountRules.MaxAccounts) throw new InvalidDataException("The backup holds too many accounts.");
            foreach (var a in payload.Accounts) if (OtpAccountRules.Validate(a) is { } problem) throw new InvalidDataException("An account in the backup is not valid: " + problem);
            return payload.Accounts;
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }
}
