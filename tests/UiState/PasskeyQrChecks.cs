using MaterialDockerImage.UI;
using ZXing;
using ZXing.QrCode;

/// <summary>Checks for the School mode passkey choice, the controls School mode hides, and reading an authenticator QR code from a picture.</summary>
public static class PasskeyQrChecks
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

    sealed class Scratch : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mdi-passkey-" + Guid.NewGuid().ToString("N"), "school-mode.json");
        public void Dispose() { try { Directory.Delete(System.IO.Path.GetDirectoryName(Path)!, true); } catch (IOException) { } }
    }

    public static int Run()
    {
        count = 0;
        Availability();
        Plans();
        Hidden();
        Store();
        Files();
        Pictures();
        Links();
        return count;
    }

    static void Availability()
    {
        Check(SchoolPasskeyRules.ChoiceProblem(HelloAvailability.Available) == null, "the passkey can be chosen when Windows Hello is available");
        foreach (var state in new[] { HelloAvailability.NoDevice, HelloAvailability.NotConfigured, HelloAvailability.DisabledByPolicy, HelloAvailability.Busy, HelloAvailability.Unsupported })
            Check(SchoolPasskeyRules.ChoiceProblem(state) is { Length: > 10 } why && why.Contains("Windows Hello"), "the passkey cannot be chosen when Windows Hello is " + state + ", and the reason is stated");
        Check(Enum.GetValues<HelloAvailability>().Select(SchoolPasskeyRules.Describe).Distinct().Count() == Enum.GetValues<HelloAvailability>().Length, "every Windows Hello state has its own plain description");
    }

    static void Plans()
    {
        Check(SchoolPasskeyRules.Plan(SchoolOffMethod.Secret, HelloAvailability.NoDevice, true) is { Route: SchoolOffRoute.Secret, Notice: null }, "with the secret method the PIN or password is asked for, whatever Windows Hello reports");
        Check(SchoolPasskeyRules.Plan(SchoolOffMethod.Secret, HelloAvailability.Available, false) is { Route: SchoolOffRoute.Free }, "with the secret method and no secret set, the mode turns off freely");
        Check(SchoolPasskeyRules.Plan(SchoolOffMethod.Passkey, HelloAvailability.Available, true) is { Route: SchoolOffRoute.Passkey, Notice: null }, "with the passkey method and Windows Hello available, Windows Hello is asked");
        var fallback = SchoolPasskeyRules.Plan(SchoolOffMethod.Passkey, HelloAvailability.NotConfigured, true);
        Check(fallback.Route == SchoolOffRoute.Secret && fallback.Notice == SchoolPasskeyRules.Describe(HelloAvailability.NotConfigured) && fallback.Fallback == SchoolPasskeyRules.FallbackToSecret, "an unavailable passkey is reported honestly and falls back to the PIN or password");
        var free = SchoolPasskeyRules.Plan(SchoolOffMethod.Passkey, HelloAvailability.DisabledByPolicy, false);
        Check(free.Route == SchoolOffRoute.Free && free.Notice != null && free.Fallback == SchoolPasskeyRules.FallbackFree, "an unavailable passkey with no secret says so and turns the mode off without a check rather than locking the user in");
        Check(Enum.GetValues<HelloAvailability>().Where(h => h != HelloAvailability.Available).All(h => SchoolPasskeyRules.Plan(SchoolOffMethod.Passkey, h, true).Route == SchoolOffRoute.Secret), "no unavailable Windows Hello state ever lets the passkey route through");
        Check(SchoolPasskeyRules.FromText("passkey") == SchoolOffMethod.Passkey && SchoolPasskeyRules.FromText("secret") == SchoolOffMethod.Secret && SchoolPasskeyRules.FromText(null) == SchoolOffMethod.Secret && SchoolPasskeyRules.FromText("PASSKEY") == SchoolOffMethod.Secret && SchoolPasskeyRules.FromText("garbage") == SchoolOffMethod.Secret, "an unknown stored method reads as the PIN or password, never as the passkey");
    }

    static void Hidden()
    {
        foreach (var id in new[] { "language", "funnyen", "funnyyue", "vocabulary" })
            Check(SchoolHiddenControls.HidesSetting(id, true) && !SchoolHiddenControls.HidesSetting(id, false), "under School mode the " + id + " setting is hidden, and only then");
        Check(SchoolHiddenControls.HidesCommand("set.language", true) && !SchoolHiddenControls.HidesCommand("set.language", false), "under School mode the language command is hidden from the palette, and only then");
        Check(!SchoolHiddenControls.HidesSetting("emoji", true) && !SchoolHiddenControls.HidesSetting("school", true) && !SchoolHiddenControls.HidesCommand("set.theme", true), "School mode hides nothing else, so the mode itself stays switchable");
    }

    static void Store()
    {
        using var scratch = new Scratch();
        var clock = new Clock();
        var store = new SchoolModeStore(scratch.Path, clock.Read, 10_000);
        Check(store.Method == SchoolOffMethod.Secret, "a fresh mode turns off with the PIN or password");
        store.Enable();
        store.SetSecret("482913");
        store.SetMethod(SchoolOffMethod.Passkey);
        Check(store.Method == SchoolOffMethod.Passkey, "the passkey choice is kept");
        var text = File.ReadAllText(scratch.Path);
        Check(text.Contains("\"Method\": \"passkey\"") && !text.Contains("482913"), "only the choice is stored; the PIN stays a hash and no passkey material exists");
        Check(new SchoolModeStore(scratch.Path, clock.Read, 10_000).Method == SchoolOffMethod.Passkey, "the choice survives a reload");
        Check(store.TurnOffAfterPasskey(false) == SchoolOffOutcome.Wrong && store.Enabled, "a refused Windows Hello check leaves the mode on");
        store.SetMethod(SchoolOffMethod.Secret);
        Check(store.TurnOffAfterPasskey(true) == SchoolOffOutcome.Wrong && store.Enabled, "a Windows Hello success cannot turn the mode off unless the passkey is the chosen method");
        store.SetMethod(SchoolOffMethod.Passkey);
        Check(store.SecretMatches("482913") && !store.SecretMatches("000000") && store.Enabled, "checking the secret without turning the mode off changes nothing");
        Check(store.TurnOffAfterPasskey(true) == SchoolOffOutcome.TurnedOff && !store.Enabled && store.TurnOffAfterPasskey(true) == SchoolOffOutcome.AlreadyOff, "a verified Windows Hello check turns the mode off");
        store.Reset();
        Check(store.Method == SchoolOffMethod.Secret, "resetting the mode forgets the passkey choice");
        File.WriteAllText(scratch.Path, "{\"Version\":1,\"Enabled\":true,\"Method\":\"surprise\"}");
        Check(new SchoolModeStore(scratch.Path, clock.Read, 10_000).Method == SchoolOffMethod.Secret, "a record holding an unknown method reads as the PIN or password");
    }

    static void Files()
    {
        Check(OtpQrImport.FileProblem(@"C:\a\code.png", 1000) == null && OtpQrImport.FileProblem(@"C:\a\code.JPG", 1000) == null && OtpQrImport.FileProblem(@"C:\a\code.tiff", 1000) == null, "common picture types are accepted");
        Check(OtpQrImport.FileProblem(@"C:\a\code.pdf", 1000) != null && OtpQrImport.FileProblem(@"C:\a\code.exe", 1000) != null && OtpQrImport.FileProblem("", 1) != null && OtpQrImport.FileProblem(null, 1) != null, "other file types and an empty name are refused");
        Check(OtpQrImport.FileProblem(@"C:\a\code.png", OtpQrImport.MaxFileBytes + 1) != null && OtpQrImport.FileProblem(@"C:\a\code.png", 0) != null && OtpQrImport.FileProblem(@"C:\a\code.png", OtpQrImport.MaxFileBytes) == null, "oversized and empty pictures are refused at the boundary");
    }

    /// <summary>Renders a QR module matrix as BGRA pixels with a quiet zone, dark modules black and the rest white.</summary>
    static (byte[] Pixels, int Width, int Height) Render(bool[,] modules, int scale = 6, bool invert = false)
    {
        var n = modules.GetLength(0); var size = (n + 8) * scale;
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var mx = x / scale - 4; var my = y / scale - 4;
                var dark = mx >= 0 && my >= 0 && mx < n && my < n && modules[my, mx];
                var value = (byte)((dark ^ invert) ? 0 : 255);
                var i = (y * size + x) * 4;
                pixels[i] = pixels[i + 1] = pixels[i + 2] = value; pixels[i + 3] = 255;
            }
        return (pixels, size, size);
    }

    /// <summary>Encodes text with the ZXing writer, independently of the app's own encoder, and renders it.</summary>
    static (byte[] Pixels, int Width, int Height) ZXingPicture(string text, bool invert = false)
    {
        var matrix = new QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, 0, 0);
        var modules = new bool[matrix.Height, matrix.Width];
        for (var y = 0; y < matrix.Height; y++) for (var x = 0; x < matrix.Width; x++) modules[y, x] = matrix[x, y];
        return Render(modules, 6, invert);
    }

    static void Pictures()
    {
        var link = "otpauth://totp/Example%20Mail:me@example.com?secret=JBSWY3DPEHPK3PXP&issuer=Example%20Mail&algorithm=SHA256&digits=8&period=45";
        var (pixels, w, h) = ZXingPicture(link);
        Check(OtpQrImport.TryDecode(pixels, w, h, out var text, out var error) && text == link && error == "", "a QR picture made by the ZXing writer decodes to exactly its text");
        Check(OtpQrImport.TryFromPixels(pixels, w, h, out var account, out error) && account!.Issuer == "Example Mail" && account.Name == "me@example.com" && account.SecretBase32 == "JBSWY3DPEHPK3PXP" && account.Algorithm == "SHA256" && account.Digits == 8 && account.Period == 45, "the picture becomes an account that keeps the link's issuer, algorithm, digits and period");

        var original = new OtpAccount { Issuer = "Round Trip", Name = "tester@example.com", SecretBase32 = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", Algorithm = "SHA512", Digits = 7, Period = 60 };
        var built = ZXingPicture(OtpauthUri.Build(original));
        Check(OtpQrImport.TryFromPixels(built.Pixels, built.Width, built.Height, out var restored, out error) && restored!.Issuer == original.Issuer && restored.Name == original.Name && restored.SecretBase32 == original.SecretBase32 && restored.Algorithm == original.Algorithm && restored.Digits == original.Digits && restored.Period == original.Period, "an account written as an otpauth link, drawn as a QR picture and read back is the same account");

        var own = Render(QrCode.Encode(link));
        Check(OtpQrImport.TryDecode(own.Pixels, own.Width, own.Height, out var ownText, out _) && ownText == link, "the app's own pairing QR code can be read by the picture reader");

        var inverted = ZXingPicture(link, true);
        Check(OtpQrImport.TryDecode(inverted.Pixels, inverted.Width, inverted.Height, out var invertedText, out _) && invertedText == link, "a light-on-dark QR picture is read too");

        var blank = new byte[200 * 200 * 4]; Array.Fill(blank, (byte)255);
        Check(!OtpQrImport.TryDecode(blank, 200, 200, out _, out error) && error == "No QR code was found in that picture.", "a picture with no QR code says so");
        Check(!OtpQrImport.TryDecode(new byte[16], 200, 200, out _, out _) && !OtpQrImport.TryDecode(blank, 0, 200, out _, out _) && !OtpQrImport.TryDecode(blank, 9000, 9000, out _, out _), "a truncated buffer and impossible sizes are refused");

        var other = ZXingPicture("https://example.com/not-a-code");
        Check(!OtpQrImport.TryFromPixels(other.Pixels, other.Width, other.Height, out account, out error) && account == null && error == "That QR code is not an authenticator link.", "a QR code that is not an authenticator link is refused with that reason");
    }

    static void Links()
    {
        Check(!OtpQrImport.TryAccount("otpauth-migration://offline?data=CjEKCkhlbGxvId6tvu8SGEV4YW1wbGU6", out var account, out var error) && account == null && error.Contains("several accounts"), "a Google Authenticator export code is refused with an explanation");
        Check(!OtpQrImport.TryAccount("otpauth://hotp/Example:me?secret=JBSWY3DPEHPK3PXP&counter=1", out _, out error) && error.Contains("HOTP"), "a counter-based link is refused by the shared parser");
        Check(!OtpQrImport.TryAccount("otpauth://totp/Example:me?secret=!!!", out _, out error) && error.Length > 0, "a link with a bad secret is refused by the shared parser");
        Check(OtpQrImport.TryAccount("  OTPAUTH://totp/Example:me?secret=JBSWY3DPEHPK3PXP  ", out account, out _) && account!.Digits == 6 && account.Period == 30 && account.Algorithm == "SHA1", "surrounding spaces and capitals are tolerated and the defaults apply when the link names none");
    }
}
