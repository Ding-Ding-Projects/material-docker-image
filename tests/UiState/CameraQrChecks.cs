using MaterialDockerImage.UI;
using ZXing;
using ZXing.QrCode;

/// <summary>Checks for the camera QR scan rules: sampling cadence, acceptance, refusal, timeouts, error messages and camera order. No camera is opened.</summary>
public static class CameraQrChecks
{
    static int count;

    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        count++;
        Console.WriteLine("PASS " + name);
    }

    static readonly DateTimeOffset T0 = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
    static DateTimeOffset At(double seconds) => T0 + TimeSpan.FromSeconds(seconds);

    /// <summary>A synthetic camera frame holding a QR code made by the ZXing writer, with a quiet zone and a flat grey surround.</summary>
    static (byte[] Pixels, int Width, int Height) Frame(string text)
    {
        var matrix = new QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, 0, 0);
        const int scale = 5, margin = 40;
        var size = matrix.Width * scale + margin * 2;
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var mx = (x - margin) / scale; var my = (y - margin) / scale;
                var inside = x >= margin && y >= margin && mx < matrix.Width && my < matrix.Height;
                var dark = inside && matrix[mx, my];
                var v = (byte)(dark ? 20 : 235);
                var i = (y * size + x) * 4;
                pixels[i] = pixels[i + 1] = pixels[i + 2] = v; pixels[i + 3] = 255;
            }
        return (pixels, size, size);
    }

    static (byte[] Pixels, int Width, int Height) Blank()
    {
        var p = new byte[200 * 200 * 4]; Array.Fill(p, (byte)200);
        return (p, 200, 200);
    }

    public static int Run()
    {
        count = 0;
        Accepts();
        Refuses();
        Cadence();
        Timeouts();
        Errors();
        Order();
        Cantonese();
        return count;
    }

    static void Accepts()
    {
        var link = "otpauth://totp/Example%20Mail:me@example.com?secret=JBSWY3DPEHPK3PXP&issuer=Example%20Mail&algorithm=SHA256&digits=8&period=45";
        var f = Frame(link);
        var session = new CameraQrSession(T0);
        var step = session.Offer(f.Pixels, f.Width, f.Height, At(1));
        Check(step.Kind == CameraScanKind.Accepted && step.Account is { Issuer: "Example Mail", Name: "me@example.com", Algorithm: "SHA256", Digits: 8, Period: 45 }, "a synthetic camera frame holding a ZXing QR code is accepted with the link's parameters");
        Check(session.Finished && session.Offer(f.Pixels, f.Width, f.Height, At(5)).Kind == CameraScanKind.Skipped, "after the first valid link the scan is finished and later frames are ignored");
        var blank = Blank();
        Check(new CameraQrSession(T0).Offer(blank.Pixels, blank.Width, blank.Height, At(1)).Kind == CameraScanKind.Searching, "a frame with no QR code keeps the scan searching");
    }

    static void Refuses()
    {
        var web = Frame("https://example.com/not-a-link");
        var session = new CameraQrSession(T0);
        var step = session.Offer(web.Pixels, web.Width, web.Height, At(1));
        Check(step.Kind == CameraScanKind.Rejected && step.Account == null && step.Message.Contains("not an authenticator link") && !session.Finished, "a QR code that is not an authenticator link is refused and the scan keeps going");
        var migration = Frame("otpauth-migration://offline?data=CjEKCkhlbGxvId6tvu8SGEV4YW1wbGU6YWxpY2VAZ29vZ2xlLmNvbRgBIAEoATACEAEYASAA");
        var m = new CameraQrSession(T0).Offer(migration.Pixels, migration.Width, migration.Height, At(1));
        Check(m.Kind == CameraScanKind.Rejected && m.Message.Contains("export code") && m.Account == null, "an otpauth-migration export code is refused with its own reason");
        var bad = Frame("otpauth://totp/Example:me?secret=not-base32!!");
        Check(new CameraQrSession(T0).Offer(bad.Pixels, bad.Width, bad.Height, At(1)).Kind == CameraScanKind.Rejected, "an otpauth link with an unreadable secret is refused by the shared parser");
        var again = session.Offer(web.Pixels, web.Width, web.Height, At(2));
        Check(again.Kind == CameraScanKind.Searching, "the same wrong code held in front of the camera is not announced again straight away");
        var later = session.Offer(web.Pixels, web.Width, web.Height, At(8));
        Check(later.Kind == CameraScanKind.Rejected, "the same wrong code is announced again after the repeat interval");
    }

    static void Cadence()
    {
        var blank = Blank();
        var s = new CameraQrSession(T0);
        Check(s.ShouldSample(At(1)), "the first frame is sampled");
        s.Offer(blank.Pixels, blank.Width, blank.Height, At(1));
        Check(!s.ShouldSample(At(1.1)), "a frame 100 ms later is not sampled");
        Check(s.Offer(blank.Pixels, blank.Width, blank.Height, At(1.1)).Kind == CameraScanKind.Skipped, "an unsampled frame is skipped without a decode");
        Check(s.ShouldSample(At(1) + CameraQrRules.SampleInterval), "a frame one full interval later is sampled");
    }

    static void Timeouts()
    {
        var blank = Blank();
        var s = new CameraQrSession(T0);
        Check(s.Tick(T0 + CameraQrRules.ScanTimeout - TimeSpan.FromSeconds(1)).Kind == CameraScanKind.Skipped && !s.Finished, "a tick before the timeout does nothing");
        var end = s.Tick(T0 + CameraQrRules.ScanTimeout);
        Check(end.Kind == CameraScanKind.TimedOut && end.Message == CameraQrRules.MessageFor(CameraFailure.Timeout) && s.Finished, "a tick at the timeout ends the scan with the timeout message");
        var viaFrame = new CameraQrSession(T0).Offer(blank.Pixels, blank.Width, blank.Height, T0 + CameraQrRules.ScanTimeout + TimeSpan.FromSeconds(1));
        Check(viaFrame.Kind == CameraScanKind.TimedOut, "a frame that arrives after the timeout also ends the scan rather than searching forever");
        Check(CameraQrRules.StartTimeout < CameraQrRules.ScanTimeout, "the camera start allowance is shorter than the whole scan");
    }

    static void Errors()
    {
        Check(CameraQrRules.Classify(unchecked((int)0x80070005)) == CameraFailure.PermissionDenied, "access denied maps to permission denied");
        Check(CameraQrRules.Classify(unchecked((int)0xC00D3704)) == CameraFailure.InUse && CameraQrRules.Classify(unchecked((int)0x80070020)) == CameraFailure.InUse, "a failed stream start and a sharing violation map to camera in use");
        Check(CameraQrRules.Classify(unchecked((int)0xC00D36D5)) == CameraFailure.NoCamera, "no capture devices maps to no camera");
        Check(CameraQrRules.Classify(0x12345678) == CameraFailure.Unavailable, "an unknown error is reported as unavailable, never guessed");
        var messages = Enum.GetValues<CameraFailure>().Select(CameraQrRules.MessageFor).ToList();
        Check(messages.Distinct().Count() == messages.Count && messages.All(m => m.Length > 20), "every failure has its own plain message");
        Check(CameraQrRules.MessageFor(CameraFailure.PermissionDenied).Contains("Windows Settings") && CameraQrRules.MessageFor(CameraFailure.NoCamera).Contains("No camera") && CameraQrRules.MessageFor(CameraFailure.InUse).Contains("another app"), "the permission, missing and busy messages say what to do");
    }

    static void Cantonese()
    {
        var before = MaterialDockerImage.Services.Loc.Language;
        MaterialDockerImage.Services.Loc.Language = "Cantonese";
        try
        {
            var english = Enum.GetValues<CameraFailure>().Select(CameraQrRules.MessageFor).Append(CameraQrRules.Searching).Append("QR code read.").Append("From the camera…").ToList();
            Check(english.All(m => MaterialDockerImage.Services.Loc.T(m) != m), "every camera failure and status message has a Cantonese translation");
        }
        finally { MaterialDockerImage.Services.Loc.Language = before; }
    }

    static void Order()
    {
        var ordered = CameraQrRules.Order([new("3", "Integrated IR Camera"), new("2", "USB Webcam"), new("1", "integrated camera"), new("", "No id")]);
        Check(ordered.Select(c => c.Id).SequenceEqual(["1", "2", "3"]), "colour cameras come first by name, infrared last, and a camera without an id is dropped");
        Check(CameraQrRules.Order([]).Count == 0, "no cameras gives an empty list, not an error");
    }
}
