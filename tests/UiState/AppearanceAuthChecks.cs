using System.Text;
using System.Text.Json;
using MaterialDockerImage.UI;

/// <summary>Checks for per-element appearance (identity, ranges, store, file) and the general authenticator (otpauth parsing, RFC 6238 codes, vault, encrypted backup).</summary>
public static class AppearanceAuthChecks
{
    static int count;

    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        count++;
        Console.WriteLine("PASS " + name);
    }

    sealed class StandInProtector : ISecretProtector
    {
        public bool Fail;
        static byte[] Key(byte[] entropy) => System.Security.Cryptography.SHA256.HashData(entropy);
        public byte[] Protect(byte[] plain, byte[] entropy) { var k = Key(entropy); return new byte[] { 0x47 }.Concat(plain.Select((b, i) => (byte)(b ^ k[i % k.Length]))).ToArray(); }
        public byte[]? Unprotect(byte[] protectedBytes, byte[] entropy) { if (Fail || protectedBytes.Length < 2 || protectedBytes[0] != 0x47) return null; var k = Key(entropy); return protectedBytes.Skip(1).Select((b, i) => (byte)(b ^ k[i % k.Length])).ToArray(); }
    }

    public static int Run()
    {
        count = 0;
        Elements();
        Accounts();
        return count;
    }

    static void Elements()
    {
        // Identity
        var path = new[] { new ElementKeySegment("MainWindow", null, 0), new ElementKeySegment("Grid", null, 0), new ElementKeySegment("Border", null, 2), new ElementKeySegment("Button", "btn.build", 0) };
        var key = ElementKey.Build(path);
        Check(key == "MainWindow/Grid/Border[2]/Button#btn.build", "element key joins type, name and sibling position");
        Check(ElementKey.Build(path) == key, "the same path always gives the same key");
        Check(ElementKey.Build(path.Take(3).Append(new ElementKeySegment("Button", "btn.build", 1)).ToArray()) != key, "a different sibling position gives a different key");
        Check(ElementKey.IsValid(key) && !ElementKey.IsValid("") && !ElementKey.IsValid("a b") && !ElementKey.IsValid(new string('a', 401)) && !ElementKey.IsValid("a\nb"), "key validation accepts built keys and refuses spaces, control characters and long text");
        Check(ElementKey.Segment(new ElementKeySegment("Text Block", "na me!", 0)) == "Text_Block#na_me_", "unsafe characters in a name are replaced");
        var deep = Enumerable.Range(0, 80).Select(i => new ElementKeySegment("Border", null, i % 3)).ToArray();
        var deepKey = ElementKey.Build(deep);
        Check(deepKey.Length <= ElementKey.MaxLength && deepKey.StartsWith("~/") && ElementKey.IsValid(deepKey), "a very deep path keeps its last segments within the key limit");
        Check(ElementKey.Leaf(key) == "Button#btn.build", "the leaf is the last segment");

        // Style ranges
        var wild = new ElementStyle { Background = "#112233", Foreground = "not a colour", CornerRadius = 999, FontSize = 1, FontWeight = 640, Padding = -5 }.Normalized();
        Check(wild.Background == "#112233" && wild.Foreground == null, "a good colour is kept and a malformed one is dropped");
        Check(wild.CornerRadius == ElementStyle.MaxRadius && wild.FontSize == ElementStyle.MinFont && wild.Padding == 0, "radius, font size and padding are clamped into range");
        Check(wild.FontWeight == 600, "a weight snaps to the nearest supported weight");
        Check(new ElementStyle().IsEmpty && !new ElementStyle { Hidden = true }.IsEmpty, "an untouched style is empty and a hidden one is not");
        Check(new ElementStyle { CornerRadius = double.NaN }.Normalized().CornerRadius == null, "a non-finite number is dropped");
        Check(new ElementStyle { CornerRadius = 100 }.Validate().Count == 1 && new ElementStyle { FontWeight = 640 }.Validate().Count == 1 && new ElementStyle { Background = "zzz" }.Validate().Count == 1 && new ElementStyle { Background = "#112233" }.Validate().Count == 0, "strict validation reports out-of-range values");
        Check(new ElementStyle { Background = "#FFFFFF", Foreground = "#F0F0F0" }.ContrastWarning() != null && new ElementStyle { Background = "#FFFFFF", Foreground = "#000000" }.ContrastWarning() == null && new ElementStyle { Background = "#FFFFFF" }.ContrastWarning() == null, "poor text and background contrast is flagged");
        Check(ElementStyle.LockIdFor("Background") == "roles" && ElementStyle.LockIdFor("CornerRadius") == "radius" && ElementStyle.LockIdFor("FontSize") == "fontsize" && ElementStyle.LockIdFor("Hidden") == null, "properties map to their appearance lock ids");
        var described = new ElementStyle { Background = "#112233", CornerRadius = 8, Hidden = true }.Describe();
        Check(described == "background #112233, corner radius 8, hidden" && new ElementStyle().Describe() == "no overrides", "a style describes itself in plain words");

        // Store
        var store = new ElementAppearanceStore();
        Check(store.Set(key, new ElementStyle { Background = "#112233" }) && store.Count == 1 && store.Get(key)!.Background == "#112233", "an override is stored under its key");
        Check(!store.Set("bad key", new ElementStyle { Hidden = true }) && store.Count == 1, "an invalid key is refused");
        Check(store.Set(key, new ElementStyle()) && store.Count == 0, "setting an empty style removes the override");
        for (var i = 0; i < ElementAppearanceStore.MaxItems; i++) store.Set("Window/Button[" + i + "]", new ElementStyle { Hidden = true });
        Check(store.Count == ElementAppearanceStore.MaxItems && !store.Set("Window/Extra", new ElementStyle { Hidden = true }) && store.Set("Window/Button[1]", new ElementStyle { CornerRadius = 4 }), "the store is capped but an existing element can still change");
        Check(store.Reset("Window/Button[1]") && !store.Reset("Window/Button[1]") && store.ResetAll() == ElementAppearanceStore.MaxItems - 1 && store.Count == 0, "reset removes one element and reset all removes the rest");

        // File round trip
        var two = new ElementAppearanceStore();
        two.Set("Window/Grid/Button#a", new ElementStyle { Background = "#80112233", CornerRadius = 12.5, FontWeight = 700, Hidden = true });
        two.Set("Window/Grid/Text#b", new ElementStyle { Foreground = "#FF0000", FontSize = 18, Padding = 6 });
        var json = two.ToJson();
        var back = ElementAppearanceStore.Parse(json);
        Check(back.Count == 2 && back.Get("Window/Grid/Button#a")!.Describe() == two.Get("Window/Grid/Button#a")!.Describe() && back.Get("Window/Grid/Text#b")!.FontSize == 18, "overrides survive a save and load");
        Check(back.ToJson() == json, "the saved file is stable");
        var dir = Path.Combine(Path.GetTempPath(), "mdi-elements-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(dir, "element-appearance.json");
        two.Save(file);
        Check(ElementAppearanceStore.Load(file).Count == 2 && Directory.GetFiles(dir).Length == 1, "saving writes one file atomically and loading reads it back");
        Check(ElementAppearanceStore.Load(Path.Combine(dir, "missing.json")).Count == 0, "a missing file is an empty store");
        foreach (var (bad, why) in new[]
        {
            ("{", "malformed JSON"), ("{\"format\":\"other\",\"version\":1,\"elements\":{}}", "wrong format"), ("{\"format\":\"material-docker-image.element-appearance\",\"version\":2,\"elements\":{}}", "wrong version"),
            ("{\"format\":\"material-docker-image.element-appearance\",\"version\":1,\"elements\":{\"bad key\":{\"hidden\":true}}}", "invalid key"),
            ("{\"format\":\"material-docker-image.element-appearance\",\"version\":1,\"elements\":{\"A/B\":{\"cornerRadius\":500}}}", "out-of-range radius"),
            ("{\"format\":\"material-docker-image.element-appearance\",\"version\":1,\"elements\":{\"A/B\":{\"unknown\":1}}}", "unknown property"),
        })
        {
            var refused = false;
            try { ElementAppearanceStore.Parse(bad); } catch (InvalidDataException) { refused = true; }
            Check(refused, "element file refused: " + why);
        }
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    static OtpAccount Rfc(string algorithm) => new() { Id = "rfc-" + algorithm, Issuer = "RFC", Name = algorithm, SecretBase32 = Base32.Encode(Rfc6238Vectors.SecretFor(algorithm)), Algorithm = algorithm, Digits = 8 };

    static void Accounts()
    {
        // RFC 6238 through the account model
        foreach (var (time, algorithm, expected) in Rfc6238Vectors.All)
            Check(OtpClock.Read(Rfc(algorithm), DateTimeOffset.FromUnixTimeSeconds(time)).Code == expected, $"RFC 6238 vector {algorithm} at {time} through an account");
        var at59 = OtpClock.Read(Rfc("SHA1"), DateTimeOffset.FromUnixTimeSeconds(59));
        Check(at59.SecondsLeft == 1 && at59.Expiring && at59.NextCode == OtpClock.Read(Rfc("SHA1"), DateTimeOffset.FromUnixTimeSeconds(60)).Code, "the next code is the code of the next period and the countdown counts down");
        Check(OtpReading.Group("123456") == "123 456" && OtpReading.Group("12345678") == "1234 5678" && OtpReading.Group("1234567") == "1234 567", "codes are grouped for reading");

        // otpauth parsing
        const string secret = "JBSWY3DPEHPK3PXP";
        Check(OtpauthUri.TryParse($"otpauth://totp/Example:alice%40example.com?secret={secret}&issuer=Example", out var a, out _) && a!.Issuer == "Example" && a.Name == "alice@example.com" && a.Algorithm == "SHA1" && a.Digits == 6 && a.Period == 30, "a plain otpauth link gets the standard defaults");
        Check(OtpauthUri.TryParse($"otpauth://totp/Corp:bob?secret={secret.ToLowerInvariant()}&algorithm=sha256&digits=8&period=60", out var b, out _) && b!.Algorithm == "SHA256" && b.Digits == 8 && b.Period == 60 && b.Issuer == "Corp" && b.SecretBase32 == secret, "parameters in the link are honoured instead of replaced by defaults");
        Check(OtpauthUri.TryParse($"otpauth://totp/Old:carol?secret={secret}&issuer=New", out var c, out _) && c!.Issuer == "New", "the issuer parameter wins over the label prefix");
        Check(OtpauthUri.TryParse($"otpauth://totp/dave?secret={secret}", out var d, out _) && d!.Issuer == "" && d.Name == "dave", "a link without an issuer keeps just the account name");
        Check(OtpauthUri.TryParse($"  OTPAUTH://TOTP/E%20F:g%3Ah?secret={secret}&issuer=E+F  ", out var e, out _) && e!.Issuer == "E F" && e.Name == "g:h", "scheme case, surrounding spaces, percent escapes and plus signs are handled");
        foreach (var (bad, why) in new[]
        {
            ("", "empty"), ("https://example.com", "other scheme"), ("otpauth://hotp/x?secret=" + secret + "&counter=1", "hotp"), ("otpauth://totp/x", "no secret"), ("otpauth://totp/x?secret=!!!!", "bad base32"),
            ("otpauth://totp/x?secret=ABC", "secret too short"), ($"otpauth://totp/x?secret={secret}&algorithm=MD5", "unsupported algorithm"), ($"otpauth://totp/x?secret={secret}&digits=9", "digits out of range"),
            ($"otpauth://totp/x?secret={secret}&digits=abc", "digits not a number"), ($"otpauth://totp/x?secret={secret}&period=0", "period zero"), ($"otpauth://totp/x?secret={secret}&secret={secret}", "repeated secret"),
            ("otpauth://totp/x?secret=" + secret + "&issuer=" + new string('i', 2100), "oversized link")
        })
            Check(!OtpauthUri.TryParse(bad, out var none, out var why2) && none == null && why2.Length > 0, "otpauth link refused: " + why);
        var round = new OtpAccount { Issuer = "Round Trip", Name = "me@example.com", SecretBase32 = secret, Algorithm = "SHA512", Digits = 7, Period = 45 };
        Check(OtpauthUri.TryParse(OtpauthUri.Build(round), out var parsed, out _) && parsed!.Issuer == round.Issuer && parsed.Name == round.Name && parsed.SecretBase32 == secret && parsed.Algorithm == "SHA512" && parsed.Digits == 7 && parsed.Period == 45, "building and parsing a link round-trips every parameter");
        Check(OtpAccountRules.TryCreate("Mail", "me", "jbsw y3dp-ehpk 3pxp", "sha1", 6, 30, out var manual, out _) && manual!.SecretBase32 == secret, "manual entry tolerates spaces, hyphens and lower case");
        Check(!OtpAccountRules.TryCreate("", "", secret, "SHA1", 6, 30, out _, out _) && !OtpAccountRules.TryCreate("x", "y", secret, "SHA1", 5, 30, out _, out _), "manual entry needs a name and a valid digit count");
        Check(!a!.ToString().Contains(secret) && !a.Label.Contains(secret), "an account never prints its secret");

        // Vault
        var dir = Path.Combine(Path.GetTempPath(), "mdi-auth-" + Guid.NewGuid().ToString("N"));
        var vaultFile = Path.Combine(dir, "authenticator.bin");
        var protector = new StandInProtector();
        var vault = new AuthenticatorVault(protector, vaultFile);
        vault.Load();
        Check(vault.Accounts.Count == 0 && vault.Problem == null, "a new vault is empty");
        Check(vault.Add(a) == null && vault.Add(b!) == null && vault.Add(c!) == null && vault.Accounts.Count == 3, "accounts are added");
        Check(vault.Add(a) == "That account is already stored." && vault.Accounts.Count == 3, "an exact duplicate is refused");
        var bytes = File.ReadAllBytes(vaultFile);
        var asText = Encoding.UTF8.GetString(bytes) + Encoding.Latin1.GetString(bytes);
        Check(!asText.Contains(secret) && !asText.Contains("alice") && !asText.Contains("Example") && !asText.Contains("Corp"), "the vault file holds no readable secret, issuer or name");
        var reopened = new AuthenticatorVault(protector, vaultFile); reopened.Load();
        Check(reopened.Accounts.Count == 3 && reopened.Accounts[1].Issuer == "Corp" && reopened.Accounts[1].Digits == 8 && reopened.Accounts[0].SecretBase32 == secret, "accounts survive a restart");
        var id = vault.Accounts[0].Id;
        Check(vault.Rename(id, "Renamed", "new name") == null && Reload(new AuthenticatorVault(protector, vaultFile)).Accounts[0].Label == "Renamed · new name", "a rename is saved");
        Check(vault.Rename(id, new string('x', 61), "n") != null && vault.Rename("missing", "a", "b") != null && vault.Rename(id, "", "") != null, "a rename with a too-long, unknown or empty name is refused");
        Check(vault.Move(id, 1) && vault.Accounts[1].Id == id && !vault.Move(id, 5) && Reload(new AuthenticatorVault(protector, vaultFile)).Accounts[1].Id == id, "accounts can be reordered and the order is saved");
        Check(vault.Delete(id) && !vault.Delete(id) && Reload(new AuthenticatorVault(protector, vaultFile)).Accounts.Count == 2, "an account is deleted for good");
        var before = File.ReadAllBytes(vaultFile);
        protector.Fail = true;
        var locked = Reload(new AuthenticatorVault(protector, vaultFile));
        Check(locked.Problem != null && locked.Accounts.Count == 0 && locked.Add(round) != null && !locked.Delete("x") && File.ReadAllBytes(vaultFile).SequenceEqual(before), "a vault this user cannot open reports why and refuses changes");
        File.WriteAllBytes(vaultFile, new byte[] { 1, 2, 3 });
        protector.Fail = false;
        var corrupt = Reload(new AuthenticatorVault(protector, vaultFile));
        Check(corrupt.Problem != null && corrupt.Add(round) != null && File.ReadAllBytes(vaultFile).Length == 3, "a damaged vault is never overwritten");
        File.Delete(vaultFile);
        var many = new AuthenticatorVault(protector, vaultFile); many.Load();
        var batch = Enumerable.Range(0, OtpAccountRules.MaxAccounts + 1).Select(i => new OtpAccount { Issuer = "I" + i, Name = "n", SecretBase32 = secret }).ToList();
        Check(many.AddMany(batch).Error != null && many.Accounts.Count == 0, "an import that would exceed the limit adds nothing");
        var (added, skipped, error) = many.AddMany(batch.Take(3).Append(batch[0]).Append(new OtpAccount { Issuer = "bad", SecretBase32 = "!!" }));
        Check(added == 3 && skipped == 2 && error == null && many.Accounts.Count == 3, "an import skips duplicates and invalid accounts");

        // Encrypted backup
        var accounts = new List<OtpAccount> { a, b!, round };
        var backup = AuthenticatorBackup.Export(accounts, "correct horse battery", 100_000);
        Check(!backup.Contains(secret) && !backup.Contains("alice") && !backup.Contains("Round Trip") && backup.Contains("material-docker-image.authenticator-backup"), "an encrypted backup holds no readable secret, issuer or name");
        var restored = AuthenticatorBackup.Import(backup, "correct horse battery");
        Check(restored.Count == 3 && restored[2].Algorithm == "SHA512" && restored[2].Period == 45 && restored[0].SecretBase32 == secret, "a backup restores with the right passphrase");
        Check(AuthenticatorBackup.Export(accounts, "correct horse battery", 100_000) != backup, "two backups of the same accounts differ because the salt and nonce are random");
        Refused(() => AuthenticatorBackup.Import(backup, "wrong passphrase!"), "a wrong passphrase is refused");
        var node = System.Text.Json.Nodes.JsonNode.Parse(backup)!;
        var cipher = Convert.FromBase64String(node["ciphertext"]!.GetValue<string>()); cipher[0] ^= 1; node["ciphertext"] = Convert.ToBase64String(cipher);
        Refused(() => AuthenticatorBackup.Import(node.ToJsonString(), "correct horse battery"), "a changed ciphertext is refused");
        var node2 = System.Text.Json.Nodes.JsonNode.Parse(backup)!; node2["iterations"] = 100_001;
        Refused(() => AuthenticatorBackup.Import(node2.ToJsonString(), "correct horse battery"), "a changed header is refused");
        var node3 = System.Text.Json.Nodes.JsonNode.Parse(backup)!; node3["iterations"] = 5;
        Refused(() => AuthenticatorBackup.Import(node3.ToJsonString(), "correct horse battery"), "an unsafe key setting is refused before any work is done");
        Refused(() => AuthenticatorBackup.Import("not json", "x"), "text that is not a backup is refused");
        Refused(() => AuthenticatorBackup.Import(new string('x', 600 * 1024), "x"), "an oversized file is refused");
        Check(AuthenticatorBackup.PassphraseProblem("short") != null && AuthenticatorBackup.PassphraseProblem("long enough pass") == null, "a short passphrase is refused");
        var exportRefused = false;
        try { AuthenticatorBackup.Export(accounts, "short"); } catch (ArgumentException) { exportRefused = true; }
        Check(exportRefused, "export refuses a short passphrase");

        // Capture-mode sample
        Check(AuthenticatorSample.Accounts.Count == 3 && AuthenticatorSample.Accounts.All(x => OtpAccountRules.Validate(x) == null && x.Id.StartsWith("sample-") && x.Issuer.StartsWith("Example")), "the capture sample is valid, labelled example data");
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    static AuthenticatorVault Reload(AuthenticatorVault v) { v.Load(); return v; }

    static void Refused(Action action, string name)
    {
        try { action(); }
        catch (InvalidDataException) { count++; Console.WriteLine("PASS " + name); return; }
        throw new Exception("Expected refusal: " + name);
    }
}
