using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Registers an authenticator account from a live camera. The dialog lists the cameras, shows a preview, and decodes sampled frames in
/// this process with the shared QR decoder; the first valid otpauth link becomes an account through the same parser as a pasted link.
/// Frames live in memory only for as long as it takes to show and decode them: nothing is saved to disk or logged. The camera is
/// released when the dialog closes, the scan finishes or fails, and when the app exits. Capture mode shows a labelled sample instead.
/// </summary>
internal sealed class AuthenticatorCameraDialog
{
    private static AppHost Host => AppHost.Current;

    private readonly Action<OtpAccount> accepted;
    private readonly Image preview = new() { Stretch = Stretch.Uniform, MaxHeight = 320, MinHeight = 160 };
    private readonly TextBlock status = Ui.Text("", 14, FontWeights.SemiBold, "OnSurface", true, false);
    private readonly ProgressBar scanning = new() { IsIndeterminate = true, Height = 4 };
    private readonly ComboBox picker = new() { MinWidth = 240 };
    private readonly Button retry;
    private readonly DispatcherTimer watchdog = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly object sessionLock = new();
    private readonly bool sample = Host.IsCapture;

    private IReadOnlyList<CameraInfo> cameras = [];
    private MediaCapture? capture;
    private MediaFrameReader? reader;
    private TypedEventHandler<MediaFrameReader, MediaFrameArrivedEventArgs>? handler;
    private CameraQrSession? session;
    private WriteableBitmap? bitmap;
    private DateTimeOffset startedAt;
    private DateTimeOffset lastPreview = DateTimeOffset.MinValue;
    private string shownStatus = "";
    private Action? close;
    private int generation;
    private int copying;
    private int decoding;
    private volatile bool sawFrame;
    private volatile bool released = true;
    private bool dialogClosed;

    private AuthenticatorCameraDialog(Action<OtpAccount> accepted)
    {
        this.accepted = accepted;
        retry = Ui.Button("Try again", Icons.Refresh, ButtonKind.Tonal, () => _ = StartAsync(Math.Max(0, picker.SelectedIndex)));
        retry.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(preview, Loc.T("Camera preview"));
        AutomationProperties.SetName(picker, Loc.T("Camera"));
        AutomationProperties.SetName(status, Loc.T("Camera scan status"));
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(scanning, Loc.T("Scanning for a QR code"));
        // Reduced motion and low stimulation get a still indicator instead of the moving bar.
        if (Host.Settings.ReducedMotion || Host.Settings.LowStimulation) { scanning.IsIndeterminate = false; scanning.Value = 0; scanning.Visibility = Visibility.Collapsed; }
        watchdog.Tick += (_, _) => Watch();
    }

    /// <summary>Opens the camera dialog. <paramref name="onAccount"/> receives the first valid account and the camera is released before it runs.</summary>
    public static void Open(Action<OtpAccount> onAccount) => new AuthenticatorCameraDialog(onAccount).Show();

    private void Show()
    {
        var stop = Ui.Button("Close", Icons.Close, ButtonKind.Text, Finish);
        var actions = Ui.HStack(8, retry, stop);
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        var body = Ui.VStack(12,
            Ui.Title("Scan a QR code with the camera", 20),
            Ui.Caption("Hold the QR code the service shows in front of the camera. It is read on this computer, nothing from the camera is saved, and the camera is turned off as soon as this window closes."),
            sample ? Ui.Badge("Sample, no camera is used", Tone.Warning) : null,
            Ui.Field("Camera", picker, "Choose another camera if the picture is dark or empty."),
            Ui.Card(preview, 8, "SurfaceLow", 12),
            scanning, status, actions);
        close = Host.Overlay.Show(body, 640, Release);
        body.Unloaded += (_, _) => Release();
        System.Windows.Application.Current.Exit += OnExit;
        if (sample) { ShowSample(); return; }
        picker.SelectionChanged += (_, _) => { if (picker.IsDropDownOpen || picker.IsKeyboardFocusWithin) _ = StartAsync(picker.SelectedIndex); };
        _ = LoadAsync();
    }

    private void ShowSample()
    {
        picker.ItemsSource = new[] { Loc.T("Sample camera") }; picker.SelectedIndex = 0; picker.IsEnabled = false;
        scanning.Visibility = Visibility.Collapsed;
        SetStatus("This is a sample. A real camera is not opened here.", false);
    }

    private async Task LoadAsync()
    {
        SetStatus("Looking for cameras…", false);
        try
        {
            var found = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture).AsTask();
            if (dialogClosed) return;
            cameras = CameraQrRules.Order(found.Select(d => new CameraInfo(d.Id, d.Name)));
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (!dialogClosed) Fail(CameraQrRules.Classify(e.HResult));
            return;
        }
        if (cameras.Count == 0) { Fail(CameraFailure.NoCamera); return; }
        picker.ItemsSource = cameras.Select(c => c.Name).ToList();
        picker.SelectedIndex = 0;
        await StartAsync(0);
    }

    private async Task StartAsync(int index)
    {
        var mine = Interlocked.Increment(ref generation);
        DisposeCamera();
        retry.Visibility = Visibility.Collapsed;
        if (index < 0 || index >= cameras.Count) { Fail(CameraFailure.NoCamera); return; }
        SetStatus("Starting the camera…", false);
        MediaCapture? mc = null; MediaFrameReader? r = null; TypedEventHandler<MediaFrameReader, MediaFrameArrivedEventArgs>? h = null;
        try
        {
            mc = new MediaCapture();
            var init = mc.InitializeAsync(new MediaCaptureInitializationSettings
            {
                VideoDeviceId = cameras[index].Id,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                SharingMode = MediaCaptureSharingMode.SharedReadOnly,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
            }).AsTask();
            if (await Task.WhenAny(init, Task.Delay(CameraQrRules.StartTimeout)) != init)
            {
                var late = mc; _ = init.ContinueWith(_ => late.Dispose(), TaskScheduler.Default);
                if (mine == generation) Fail(CameraFailure.Unavailable);
                return;
            }
            await init;
            var source = mc.FrameSources.Values.FirstOrDefault(s => s.Info.SourceKind == MediaFrameSourceKind.Color && s.Info.MediaStreamType is MediaStreamType.VideoPreview or MediaStreamType.VideoRecord)
                ?? mc.FrameSources.Values.FirstOrDefault(s => s.Info.SourceKind == MediaFrameSourceKind.Color);
            if (source == null) { mc.Dispose(); if (mine == generation) Fail(CameraFailure.Unavailable); return; }
            r = await mc.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
            r.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
            h = (sender, _) => OnFrame(sender, mine);
            r.FrameArrived += h;
            var started = await r.StartAsync();
            if (mine != generation || dialogClosed) { r.FrameArrived -= h; r.Dispose(); mc.Dispose(); return; }
            if (started != MediaFrameReaderStartStatus.Success)
            {
                r.FrameArrived -= h; r.Dispose(); mc.Dispose();
                Fail(started is MediaFrameReaderStartStatus.DeviceNotAvailable or MediaFrameReaderStartStatus.ExclusiveControlNotAvailable ? CameraFailure.InUse : CameraFailure.Unavailable);
                return;
            }
            capture = mc; reader = r; handler = h; released = false;
            lock (sessionLock) session = new CameraQrSession(DateTimeOffset.UtcNow);
            startedAt = DateTimeOffset.UtcNow; sawFrame = false;
            watchdog.Start();
            SetStatus(CameraQrRules.Searching, true);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (r != null && h != null) r.FrameArrived -= h;
            r?.Dispose(); mc?.Dispose();
            if (mine == generation && !dialogClosed) Fail(CameraQrRules.Classify(e.HResult));
        }
    }

    // ----- Frames: shown, decoded, forgotten -----

    private void OnFrame(MediaFrameReader sender, int mine)
    {
        if (mine != generation || released) return;
        if (Interlocked.Exchange(ref copying, 1) == 1) return;
        byte[] pixels; int width, height;
        try
        {
            using var frame = sender.TryAcquireLatestFrame();
            var soft = frame?.VideoMediaFrame?.SoftwareBitmap;
            if (soft == null) return;
            SoftwareBitmap? converted = null;
            try
            {
                var use = soft.BitmapPixelFormat == BitmapPixelFormat.Bgra8 ? soft : (converted = SoftwareBitmap.Convert(soft, BitmapPixelFormat.Bgra8));
                width = use.PixelWidth; height = use.PixelHeight;
                if (width <= 0 || height <= 0 || (long)width * height > OtpQrImport.MaxPixels) return;
                pixels = new byte[width * height * 4];
                use.CopyToBuffer(pixels.AsBuffer());
            }
            finally { converted?.Dispose(); }
        }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException or System.Runtime.InteropServices.COMException) { return; }
        finally { Interlocked.Exchange(ref copying, 0); }

        sawFrame = true;
        var now = DateTimeOffset.UtcNow;
        if (now - lastPreview >= TimeSpan.FromMilliseconds(66))
        {
            lastPreview = now;
            preview.Dispatcher.BeginInvoke(() => ShowFrame(pixels, width, height, mine), DispatcherPriority.Render);
        }
        bool due;
        lock (sessionLock) due = session?.ShouldSample(now) == true;
        if (due && Interlocked.Exchange(ref decoding, 1) == 0)
            _ = Task.Run(() =>
            {
                try
                {
                    CameraScanStep step;
                    lock (sessionLock) { if (session == null) return; step = session.Offer(pixels, width, height, DateTimeOffset.UtcNow); }
                    if (step.Kind != CameraScanKind.Skipped) preview.Dispatcher.BeginInvoke(() => Apply(step, mine));
                }
                finally { Interlocked.Exchange(ref decoding, 0); }
            });
    }

    private void ShowFrame(byte[] pixels, int width, int height, int mine)
    {
        if (mine != generation || released) return;
        if (bitmap == null || bitmap.PixelWidth != width || bitmap.PixelHeight != height)
        {
            bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            preview.Source = bitmap;
        }
        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
    }

    private void Apply(CameraScanStep step, int mine)
    {
        if (mine != generation || released) return;
        switch (step.Kind)
        {
            case CameraScanKind.Accepted when step.Account != null:
                var account = step.Account;
                Finish();
                accepted(account);
                return;
            case CameraScanKind.TimedOut:
                Fail(CameraFailure.Timeout);
                return;
            case CameraScanKind.Rejected:
                SetStatus(step.Message, true);
                return;
            case CameraScanKind.Searching:
                SetStatus(step.Message, false);
                return;
        }
    }

    private void Watch()
    {
        if (released) { watchdog.Stop(); return; }
        if (!sawFrame && DateTimeOffset.UtcNow - startedAt >= CameraQrRules.StartTimeout) { Fail(CameraFailure.Unavailable); return; }
        CameraScanStep step;
        lock (sessionLock) { if (session == null) return; step = session.Tick(DateTimeOffset.UtcNow); }
        if (step.Kind == CameraScanKind.TimedOut) Fail(CameraFailure.Timeout);
    }

    // ----- Status, failure, and letting go of the camera -----

    private void SetStatus(string english, bool error)
    {
        var text = Loc.T(english);
        status.SetResourceReference(TextBlock.ForegroundProperty, error ? "Error" : "OnSurface");
        if (text == shownStatus) return;   // the live region announces a change, not every sampled frame
        shownStatus = text; status.Text = text;
    }

    private void Fail(CameraFailure failure)
    {
        Interlocked.Increment(ref generation);
        DisposeCamera();
        scanning.Visibility = Visibility.Collapsed;
        SetStatus(CameraQrRules.MessageFor(failure), true);
        retry.Visibility = cameras.Count > 0 || failure == CameraFailure.NoCamera ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Releases the camera at once. Safe to call repeatedly and from any exit path.</summary>
    private void DisposeCamera()
    {
        released = true;
        watchdog.Stop();
        lock (sessionLock) session = null;
        var r = Interlocked.Exchange(ref reader, null); var h = Interlocked.Exchange(ref handler, null); var mc = Interlocked.Exchange(ref capture, null);
        try { if (r != null && h != null) r.FrameArrived -= h; } catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException) { }
        try { r?.Dispose(); } catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException) { }
        try { mc?.Dispose(); } catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException) { }
        bitmap = null; preview.Source = null;
    }

    private void Release()
    {
        if (dialogClosed) return;
        dialogClosed = true;
        Interlocked.Increment(ref generation);
        DisposeCamera();
        System.Windows.Application.Current.Exit -= OnExit;
    }

    private void OnExit(object sender, ExitEventArgs e) => Release();

    private void Finish()
    {
        Release();
        close?.Invoke();
    }
}
