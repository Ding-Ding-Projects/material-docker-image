using System;
using System.Collections.Generic;
using System.Linq;

namespace MaterialDockerImage.UI;

/// <summary>Why a camera scan could not start or keep going.</summary>
public enum CameraFailure { PermissionDenied, NoCamera, InUse, Timeout, Unavailable }

/// <summary>A camera as Windows lists it. Only the name and id are kept.</summary>
public sealed record CameraInfo(string Id, string Name);

/// <summary>What the scan session wants the caller to do after a frame or a clock tick.</summary>
public enum CameraScanKind { Skipped, Searching, Rejected, Accepted, TimedOut }

/// <summary>The outcome of offering one frame. <see cref="Message"/> is plain English for a live status; the caller localizes it.</summary>
public sealed record CameraScanStep(CameraScanKind Kind, string Message, OtpAccount? Account);

/// <summary>
/// Framework-free rules for registering an authenticator account from a live camera: which camera is offered first, how often a frame
/// is decoded, what a decoded code is allowed to become, and what each failure says. Nothing here touches a camera, a file or a log, and
/// no frame is ever kept: a frame is decoded and forgotten.
/// </summary>
public static class CameraQrRules
{
    /// <summary>A frame is decoded at most this often; the preview itself still shows every frame.</summary>
    public static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(250);
    /// <summary>The scan gives up after this long without a valid authenticator link.</summary>
    public static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(90);
    /// <summary>The camera must deliver a first frame within this long after it starts.</summary>
    public static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(15);
    /// <summary>A repeated refusal for the same kind of code is announced at most this often.</summary>
    public static readonly TimeSpan RepeatAnnounceInterval = TimeSpan.FromSeconds(4);

    public const string Searching = "Looking for a QR code. Hold it steady in front of the camera.";

    /// <summary>The message for a failure, honest about which one it is.</summary>
    public static string MessageFor(CameraFailure failure) => failure switch
    {
        CameraFailure.PermissionDenied => "Camera access is turned off for this app. Allow it in Windows Settings under Privacy and security, then Camera, and try again.",
        CameraFailure.NoCamera => "No camera was found on this computer.",
        CameraFailure.InUse => "The camera is being used by another app. Close that app and try again.",
        CameraFailure.Timeout => "No authenticator QR code was found in time. Check the lighting and try again, or use a picture instead.",
        _ => "The camera could not be started.",
    };

    /// <summary>Maps a Windows error code from opening the camera to a failure. Unknown codes are reported as unavailable, never guessed.</summary>
    public static CameraFailure Classify(int hresult) => unchecked((uint)hresult) switch
    {
        0x80070005 => CameraFailure.PermissionDenied,   // E_ACCESSDENIED
        0xC00D3704 => CameraFailure.InUse,              // MF_E_HW_MFT_FAILED_START_STREAMING
        0x80070020 => CameraFailure.InUse,              // sharing violation
        0x800700AA => CameraFailure.InUse,              // ERROR_BUSY
        0xC00D36D5 => CameraFailure.NoCamera,           // MF_E_NO_CAPTURE_DEVICES_AVAILABLE
        0x80070490 => CameraFailure.NoCamera,           // element not found
        _ => CameraFailure.Unavailable,
    };

    /// <summary>
    /// Orders cameras for the picker: ordinary colour cameras before infrared ones used for face sign-in, then by name, so the first entry
    /// is the one most likely to show a readable QR code. The order is stable and ignores case.
    /// </summary>
    public static IReadOnlyList<CameraInfo> Order(IEnumerable<CameraInfo> cameras) =>
        cameras.Where(c => !string.IsNullOrWhiteSpace(c.Id))
            .OrderBy(c => IsInfrared(c.Name) ? 1 : 0)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .ToList();

    private static bool IsInfrared(string name) =>
        name.Contains(" IR", StringComparison.OrdinalIgnoreCase) || name.Contains("infrared", StringComparison.OrdinalIgnoreCase);

    /// <summary>The message for a code that was read but is not accepted, or null when it is an authenticator link that parses.</summary>
    public static string? Refusal(string decodedText, out OtpAccount? account)
        => OtpQrImport.TryAccount(decodedText, out account, out var error) ? null : error;
}

/// <summary>
/// One camera scan. The caller offers frames as they arrive; the session decides whether to decode each one, accepts the first valid
/// authenticator link, refuses anything else without stopping, and gives up on time.
/// </summary>
public sealed class CameraQrSession
{
    private readonly DateTimeOffset started;
    private DateTimeOffset lastSample = DateTimeOffset.MinValue;
    private DateTimeOffset lastRefusalAnnounced = DateTimeOffset.MinValue;
    private string lastRefusal = "";

    public bool Finished { get; private set; }

    public CameraQrSession(DateTimeOffset now) { started = now; }

    /// <summary>True when this frame should be decoded. A frame that arrives too soon after the last sampled one is skipped without work.</summary>
    public bool ShouldSample(DateTimeOffset now) => !Finished && now - lastSample >= CameraQrRules.SampleInterval;

    /// <summary>Handles a clock tick with no frame, so a camera that stalls still times out.</summary>
    public CameraScanStep Tick(DateTimeOffset now)
    {
        if (Finished) return new(CameraScanKind.Skipped, "", null);
        if (now - started >= CameraQrRules.ScanTimeout)
        {
            Finished = true;
            return new(CameraScanKind.TimedOut, CameraQrRules.MessageFor(CameraFailure.Timeout), null);
        }
        return new(CameraScanKind.Skipped, "", null);
    }

    /// <summary>Offers a BGRA frame. It is decoded only when due, and never retained.</summary>
    public CameraScanStep Offer(byte[] bgra, int width, int height, DateTimeOffset now)
    {
        if (Finished) return new(CameraScanKind.Skipped, "", null);
        if (!ShouldSample(now))
        {
            var late = Tick(now);
            return late.Kind == CameraScanKind.TimedOut ? late : new(CameraScanKind.Skipped, "", null);
        }
        lastSample = now;
        if (!OtpQrImport.TryDecode(bgra, width, height, out var text, out _))
        {
            var timeout = Tick(now);
            return timeout.Kind == CameraScanKind.TimedOut ? timeout : new(CameraScanKind.Searching, CameraQrRules.Searching, null);
        }
        var refusal = CameraQrRules.Refusal(text, out var account);
        if (refusal == null)
        {
            Finished = true;
            return new(CameraScanKind.Accepted, "QR code read.", account);
        }
        // A code that is not acceptable is reported, but a still-held wrong code is not announced over and over.
        var repeat = refusal == lastRefusal && now - lastRefusalAnnounced < CameraQrRules.RepeatAnnounceInterval;
        if (!repeat) { lastRefusal = refusal; lastRefusalAnnounced = now; }
        return new(repeat ? CameraScanKind.Searching : CameraScanKind.Rejected, repeat ? CameraQrRules.Searching : refusal, null);
    }
}
