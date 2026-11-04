using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Local Base64 toolkit: encode and decode with Standard, URL-safe and MIME variants, padding, strict or lenient
/// decoding with exact error offsets, text encodings, data: URLs, streamed file input with progress, cancel and
/// SHA-256 round-trip verification. Nothing leaves the computer.
/// </summary>
internal sealed class Base64Panel
{
    private const int LiveLimit = 1_000_000;
    private const int DisplayLimit = 1_000_000;
    private const long InMemoryFileLimit = 48L * 1024 * 1024;
    private const int HistoryContentLimit = 64 * 1024;
    private static AppHost Host => AppHost.Current;

    private bool encode = true;
    private Base64Variant variant = Base64Variant.Standard;
    private bool padding = true;
    private bool strict = true;
    private bool dataUrl;
    private bool keepContent;
    private TextEncodingKind encoding = TextEncodingKind.Utf8;

    private readonly TextBox input = Ui.CodeBox("", 180);
    private readonly TextBox output = Ui.CodeBox("", 180);
    private readonly TextBox mime = Ui.Input("text/plain", "MIME type", true);
    private readonly TextBlock inputInfo = Ui.Caption("");
    private readonly TextBlock outputInfo = Ui.Caption("");
    private readonly TextBlock inputLabel = Ui.Overline("Input");
    private readonly TextBlock outputLabel = Ui.Overline("Output");
    private readonly TextBlock error = Ui.Text("", 13, null, "Error");
    private readonly TextBlock hashes = Ui.Mono("", 12, "OnSurfaceVariant", true);
    private readonly ProgressBar progress = new() { Height = 6, Minimum = 0, Maximum = 1, Visibility = Visibility.Collapsed };
    private readonly Button cancel;
    private readonly FrameworkElement strictRow, paddingRow, mimeRow;
    private readonly Border directionControl;
    private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private CancellationTokenSource? work;

    // Current input source: typed text, or a loaded file (bytes for encoding, text for decoding).
    private string? fileName;
    private byte[]? fileBytes;
    private string? fileText;
    private long fileSize;
    // Current result: full output text (encode) or bytes (decode).
    private string outputText = "";
    private byte[] outputBytes = [];
    private string lastSummary = "";

    public FrameworkElement Root { get; }

    public Base64Panel()
    {
        input.TextWrapping = TextWrapping.Wrap; output.TextWrapping = TextWrapping.Wrap;
        input.MaxHeight = 320; output.MaxHeight = 320; output.IsReadOnly = true;
        System.Windows.Automation.AutomationProperties.SetName(input, Loc.T("Input"));
        System.Windows.Automation.AutomationProperties.SetName(output, Loc.T("Output"));
        input.TextChanged += (_, _) => { if (fileName == null) { debounce.Stop(); debounce.Start(); } };
        mime.TextChanged += (_, _) => { debounce.Stop(); debounce.Start(); };
        debounce.Tick += async (_, _) =>
        {
            debounce.Stop();
            if (fileName != null || input.Text.Length > LiveLimit) return;
            try { await ConvertAsync(false); }
            catch (Exception e) when (e is not OutOfMemoryException) { error.Text = e.Message; }
        };
        cancel = Ui.Button("Cancel", Icons.Cancel, ButtonKind.Text, () => work?.Cancel());
        cancel.Visibility = Visibility.Collapsed;

        var direction = directionControl = Ui.Segmented(["Encode", "Decode"], "Encode", v => { encode = v == "Encode"; if (fileName != null) ClearInput(); Sync(); Changed(); });
        var variants = Ui.Segmented(["Standard", "URL-safe", "MIME"], "Standard", v => { variant = v switch { "URL-safe" => Base64Variant.UrlSafe, "MIME" => Base64Variant.Mime, _ => Base64Variant.Standard }; Changed(); });
        var encodings = Ui.Combo(["UTF-8", "UTF-16LE", "Latin-1", "ASCII"], "UTF-8");
        encodings.SelectionChanged += (_, _) => { encoding = (encodings.SelectedItem as string) switch { "UTF-16LE" => TextEncodingKind.Utf16LE, "Latin-1" => TextEncodingKind.Latin1, "ASCII" => TextEncodingKind.Ascii, _ => TextEncodingKind.Utf8 }; Sync(); Changed(); };
        paddingRow = Ui.Switch("Padding", true, v => { padding = v; Changed(); }, "Adds '=' so the length is a multiple of 4. In strict decoding, padding must then be present.");
        strictRow = Ui.Field("Decoding", Ui.Segmented(["Strict", "Lenient"], "Strict", v => { strict = v == "Strict"; Changed(); }),
            "Strict accepts only the chosen variant's alphabet, exact padding, canonical final bits and (for MIME) CRLF lines of at most 76 characters. Lenient accepts both alphabets, ignores whitespace and missing padding. Errors give the exact character offset.");
        mimeRow = Ui.Field("MIME type", mime, "The media type written into the data: URL, for example image/png or text/plain;charset=utf-8.", "image/png");
        var dataSwitch = Ui.Switch("data: URL", false, v => { dataUrl = v; Sync(); Changed(); }, "Encode builds data:<type>;base64,…. Decode parses a data: URL and shows its type.");
        var keep = Ui.Switch("Keep conversion content in local history", false, v => keepContent = v,
            "Off: history records only a summary (direction, sizes, variant). On: inputs up to 64 KiB are stored with their output so they can be reviewed in Activity. Larger inputs are always summary only. Leave this off for tokens or credentials.");

        var swap = Ui.Button("Swap input and output", Icons.SwapHoriz, ButtonKind.Text, Swap);
        var open = Ui.Button("Open file…", Icons.FolderOpen, ButtonKind.Text, () => _ = Ui.RunGuarded(input, OpenFileAsync));
        var clearFile = Ui.Button("Clear", Icons.Close, ButtonKind.Text, ClearInput);
        var convert = Ui.AsyncButton("Convert", Icons.PlayArrow, ButtonKind.Tonal, () => ConvertAsync(true));
        var save = Ui.Button("Save as file…", Icons.Save, ButtonKind.Tonal, Save);
        var copy = Ui.Button("Copy", Icons.ContentCopy, ButtonKind.Filled, Copy);

        var options = new WrapPanel();
        foreach (var e in new UIElement[] { Ui.Field("Direction", direction), Ui.Field("Variant", variants, "Standard uses + and /. URL-safe (RFC 4648 section 5) uses - and _ so the text can sit in URLs and file names. MIME breaks lines every 76 characters with CRLF."), Ui.Field("Text encoding", encodings, "How text is turned into bytes before encoding, and how decoded bytes are shown as text. Characters the encoding cannot represent are reported with their offset.") })
        { if (e is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 20, 12); options.Children.Add(e); }
        var toggles = new WrapPanel();
        foreach (var e in new FrameworkElement[] { paddingRow, strictRow, dataSwitch, mimeRow })
        { e.Margin = new Thickness(0, 0, 24, 12); if (e is CheckBox c) c.MaxWidth = 340; toggles.Children.Add(e); }

        var inputHead = new DockPanel();
        inputHead.Children.Add(Ui.HStack(4, open, clearFile).Dock(Dock.Right));
        inputHead.Children.Add(Ui.VStack(2, inputLabel, inputInfo));
        var outputHead = new DockPanel();
        outputHead.Children.Add(Ui.HStack(4, convert, swap).Dock(Dock.Right));
        outputHead.Children.Add(Ui.VStack(2, outputLabel, outputInfo));
        var footer = new DockPanel();
        footer.Children.Add(Ui.HStack(8, save, copy).Dock(Dock.Right));
        footer.Children.Add(Ui.Caption("Converted locally as you type (inputs up to 1 MB; use Convert for larger ones). Copy, Save and file conversions add an entry to local history."));
        var body = Ui.VStack(12, options, toggles, inputHead, input, Ui.Columns(8, [1, 0], progress, cancel), outputHead, output, error, hashes, footer, keep);
        Root = Ui.Scroll(Ui.Card(body, 20, "SurfaceLow", 16).Margin(0, 0, 8, 16));
        Sync();
    }

    private void Sync()
    {
        strictRow.Visibility = encode ? Visibility.Collapsed : Visibility.Visible;
        mimeRow.Visibility = dataUrl && encode ? Visibility.Visible : Visibility.Collapsed;
        inputLabel.Text = encode ? $"Input · {(fileName != null ? "file" : Base64Tools.EncodingName(encoding) + " text")}" : $"Input · {(dataUrl ? "data: URL" : "Base64 text")}";
        outputLabel.Text = encode ? $"Output · {(dataUrl ? "data: URL" : $"Base64, {Base64Tools.VariantName(variant)}{(padding ? ", padded" : ", no padding")}")}" : $"Output · {Base64Tools.EncodingName(encoding)} text";
    }

    private void Changed() { Sync(); if (fileName == null) { debounce.Stop(); debounce.Start(); } }

    private void ClearInput()
    {
        work?.Cancel();
        fileName = null; fileBytes = null; fileText = null; fileSize = 0;
        input.IsReadOnly = false; input.Clear();
        SetOutput("", [], "");
        hashes.Text = ""; error.Text = "";
        Sync();
    }

    private void SetOutput(string text, byte[] bytes, string info)
    {
        outputText = text; outputBytes = bytes;
        output.Text = text.Length > DisplayLimit ? text[..DisplayLimit] + $"\n\n[Showing the first {DisplayLimit:N0} of {text.Length:N0} characters. Save or Copy uses the full output.]" : text;
        outputInfo.Text = info;
    }

    private async Task ConvertAsync(bool explicitRun)
    {
        error.Text = "";
        if (fileName != null && fileBytes == null && fileText == null) return;
        if (fileName == null && input.Text.Length == 0) { SetOutput("", [], ""); inputInfo.Text = ""; hashes.Text = ""; lastSummary = ""; return; }
        if (encode)
        {
            byte[] bytes;
            if (fileBytes != null) bytes = fileBytes;
            else
            {
                var converted = Base64Tools.GetBytes(input.Text, encoding);
                if (!converted.Success) { error.Text = converted.Error!; SetOutput("", [], ""); input.Select(converted.ErrorOffset, 1); return; }
                bytes = converted.Bytes;
            }
            inputInfo.Text = fileName != null ? $"{fileName} · {bytes.Length:N0} bytes" : $"{input.Text.Length:N0} characters · {bytes.Length:N0} bytes ({Base64Tools.EncodingName(encoding)})";
            string text;
            try { text = dataUrl ? Base64Tools.BuildDataUrl(mime.Text, bytes) : await Heavy(bytes.Length, () => Base64Tools.Encode(bytes, variant, padding)); }
            catch (ArgumentException e) { error.Text = e.Message; return; }
            SetOutput(text, [], $"{text.Length:N0} characters · {Encoding.ASCII.GetByteCount(text):N0} bytes");
            // Round trip: decode what was produced and compare digests.
            var back = dataUrl ? (Base64Tools.TryParseDataUrl(text, out var parsed, out _) ? parsed!.Bytes : []) : (await Heavy(text.Length, () => Base64Tools.Decode(text, variant, true, padding))).Bytes;
            ShowHashes(bytes, back, "input", "round trip (decoded output)");
            lastSummary = $"Encoded {bytes.Length:N0} bytes{(fileName != null ? " from " + fileName : $" of {Base64Tools.EncodingName(encoding)} text")} to {(dataUrl ? "a data: URL" : Base64Tools.VariantName(variant) + " Base64" + (padding ? "" : " without padding"))} ({text.Length:N0} characters)";
        }
        else
        {
            var source = fileText ?? input.Text;
            inputInfo.Text = (fileName != null ? fileName + " · " : "") + $"{source.Length:N0} characters";
            Base64DecodeResult result;
            string? type = null;
            if (dataUrl)
            {
                if (!Base64Tools.TryParseDataUrl(source, out var parsed, out var problem)) { error.Text = problem!; SetOutput("", [], ""); return; }
                result = new(true, parsed!.Bytes, null, -1); type = parsed.MimeType;
            }
            else
            {
                var token = NewWork(source.Length > LiveLimit);
                try { result = await Heavy(source.Length, () => Base64Tools.Decode(source, variant, strict, padding, p => Dispatch(p), token)); }
                catch (OperationCanceledException) { error.Text = Loc.T("Canceled."); return; }
                finally { EndWork(); }
            }
            if (!result.Success)
            {
                error.Text = $"{result.Error} At character offset {result.ErrorOffset:N0}" + Location(source, result.ErrorOffset) + ".";
                SetOutput("", [], "");
                if (fileName == null && result.ErrorOffset >= 0 && result.ErrorOffset <= input.Text.Length) input.Select(result.ErrorOffset, Math.Min(1, input.Text.Length - result.ErrorOffset));
                hashes.Text = "";
                return;
            }
            var text = Base64Tools.TryGetString(result.Bytes, encoding, out var textError);
            var shown = text ?? $"[Binary output: {result.Bytes.Length:N0} bytes that are not valid {Base64Tools.EncodingName(encoding)} text ({textError}). Save as file to keep the exact bytes.]\n\n" + Hex(result.Bytes, 256);
            SetOutput(shown, result.Bytes, $"{result.Bytes.Length:N0} bytes" + (type != null ? " · type " + type : "") + (text == null ? " · binary" : ""));
            var again = dataUrl ? result.Bytes : Base64Tools.Decode(Base64Tools.Encode(result.Bytes, variant, padding), variant, true, padding).Bytes;
            ShowHashes(result.Bytes, again, "decoded output", "round trip (re-encoded and decoded)");
            lastSummary = $"Decoded {source.Length:N0} characters of {(dataUrl ? "data: URL" : Base64Tools.VariantName(variant) + " Base64, " + (strict ? "strict" : "lenient"))}{(fileName != null ? " from " + fileName : "")} to {result.Bytes.Length:N0} bytes";
        }
        if (explicitRun && fileName == null) Record("Convert");
    }

    private static string Location(string text, int offset)
    {
        if (offset < 0 || offset > text.Length) return "";
        var line = 1; var column = 1;
        for (var i = 0; i < offset; i++) { if (text[i] == '\n') { line++; column = 1; } else column++; }
        return $" (line {line}, column {column})";
    }

    private static string Hex(byte[] bytes, int max)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < Math.Min(bytes.Length, max); i += 16)
        {
            sb.Append(i.ToString("X6")).Append("  ");
            for (var j = i; j < Math.Min(i + 16, bytes.Length); j++) sb.Append(bytes[j].ToString("X2")).Append(' ');
            sb.AppendLine();
        }
        if (bytes.Length > max) sb.Append($"… {bytes.Length - max:N0} more bytes");
        return sb.ToString();
    }

    private void ShowHashes(byte[] original, byte[] roundTrip, string first, string second)
    {
        var a = Base64Tools.Sha256Hex(original);
        var b = Base64Tools.Sha256Hex(roundTrip);
        hashes.Text = $"SHA-256 {first}: {a}\nSHA-256 {second}: {b} · {(a == b ? "match" : "DIFFERENT")}";
    }

    private static Task<T> Heavy<T>(int size, Func<T> work) => size > 200_000 ? Task.Run(work) : Task.FromResult(work());

    private CancellationToken NewWork(bool show)
    {
        work?.Cancel();
        work = new CancellationTokenSource();
        if (show) { progress.Value = 0; progress.Visibility = Visibility.Visible; cancel.Visibility = Visibility.Visible; }
        return work.Token;
    }
    private void EndWork() { progress.Visibility = Visibility.Collapsed; cancel.Visibility = Visibility.Collapsed; }
    private void Dispatch(double value) => progress.Dispatcher.BeginInvoke(() => progress.Value = value);

    private async Task OpenFileAsync()
    {
        var dialog = new OpenFileDialog { Title = Loc.T(encode ? "Choose a file to encode" : "Choose a Base64 text file to decode"), Filter = encode ? "All files|*.*" : "Text files|*.txt;*.b64;*.base64|All files|*.*" };
        if (dialog.ShowDialog() != true) return;
        var info = new FileInfo(dialog.FileName);
        error.Text = "";
        if (info.Length > InMemoryFileLimit)
        {
            if (!encode) { error.Text = $"The file is {Ui.FormatBytes(info.Length)}. Decoding is limited to {Ui.FormatBytes(InMemoryFileLimit)} in this tool."; return; }
            await EncodeToFileAsync(info);
            return;
        }
        var token = NewWork(true);
        try
        {
            var bytes = new byte[info.Length];
            await using (var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            {
                var read = 0;
                while (read < bytes.Length)
                {
                    var n = await stream.ReadAsync(bytes.AsMemory(read, Math.Min(1 << 20, bytes.Length - read)), token);
                    if (n == 0) break;
                    read += n;
                    progress.Value = bytes.Length == 0 ? 1 : (double)read / bytes.Length;
                }
                if (read != bytes.Length) Array.Resize(ref bytes, read);
            }
            fileName = info.Name; fileSize = bytes.LongLength;
            if (encode) { fileBytes = bytes; fileText = null; }
            else
            {
                fileBytes = null;
                fileText = Encoding.ASCII.GetString(bytes);
            }
            input.Text = $"[File {info.Name} · {Ui.FormatBytes(fileSize)} · SHA-256 {Base64Tools.Sha256Hex(bytes)}]\nThe file content is used as input. Clear to type text instead.";
            input.IsReadOnly = true;
            Sync();
        }
        catch (OperationCanceledException) { error.Text = Loc.T("Loading was canceled."); return; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { error.Text = Loc.T("The file could not be read:") + " " + e.Message; return; }
        finally { EndWork(); }
        await ConvertAsync(false);
        Record("File");
    }

    /// <summary>Very large inputs are streamed straight to an output file instead of being held in memory.</summary>
    private async Task EncodeToFileAsync(FileInfo info)
    {
        if (dataUrl) { error.Text = Loc.T("data: URLs are built only for files up to 48 MB."); return; }
        var target = new SaveFileDialog { Title = Loc.T("Save the Base64 output"), FileName = info.Name + ".b64", Filter = "Base64 text|*.b64;*.txt|All files|*.*" };
        if (target.ShowDialog() != true) return;
        if (string.Equals(Path.GetFullPath(target.FileName), info.FullName, StringComparison.OrdinalIgnoreCase)) { error.Text = Loc.T("Choose a different file for the output."); return; }
        var token = NewWork(true);
        try
        {
            await using var source = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            await using var writer = new StreamWriter(target.FileName, false, new UTF8Encoding(false));
            var report = new Progress<long>(done => progress.Value = info.Length == 0 ? 1 : (double)done / info.Length);
            var hash = await Base64Tools.EncodeStreamAsync(source, writer, variant, padding, report, token);
            ClearInput();
            hashes.Text = $"SHA-256 input: {hash}\nRound trip not computed: the output was streamed to disk to keep memory use low.";
            outputInfo.Text = $"Written to {target.FileName}";
            lastSummary = $"Encoded {info.Name} ({Ui.FormatBytes(info.Length)}) to {Path.GetFileName(target.FileName)} as {Base64Tools.VariantName(variant)} Base64";
            Record("File");
            Host.Notify.Success(lastSummary + ".");
        }
        catch (OperationCanceledException) { error.Text = Loc.T("Canceled. The partial output file was left in place; delete it if you do not need it."); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { error.Text = Loc.T("Encoding failed:") + " " + e.Message; }
        finally { EndWork(); }
    }

    private void Swap()
    {
        if (!encode && outputBytes.Length > 0 && Base64Tools.TryGetString(outputBytes, encoding, out _) == null) { error.Text = Loc.T("The output is binary, so it cannot be placed in the text input."); return; }
        var next = outputText;
        ClearInput();
        // Selecting the other segment runs its callback, which flips the direction and converts again.
        var target = encode ? 1 : 0;
        if (directionControl.Child is Grid grid && grid.Children.Count > target && grid.Children[target] is RadioButton radio) radio.IsChecked = true;
        else { encode = !encode; Sync(); }
        input.Text = next;
    }

    private void Copy()
    {
        if (outputText.Length == 0) { Host.Notify.Info("There is no output to copy yet."); return; }
        if (!encode && outputBytes.Length > 0 && Base64Tools.TryGetString(outputBytes, encoding, out _) == null) { Host.Notify.Warning("The output is binary. Use Save as file to keep the exact bytes."); return; }
        Clipboard.SetText(outputText);
        Record("Copy");
        Host.Notify.Info($"Copied {outputText.Length:N0} characters.");
    }

    private void Save()
    {
        if (outputText.Length == 0 && outputBytes.Length == 0) { Host.Notify.Info("There is no output to save yet."); return; }
        var dialog = new SaveFileDialog { FileName = encode ? "output.b64" : fileName != null ? Path.GetFileNameWithoutExtension(fileName) + ".bin" : "decoded.bin", Filter = encode ? "Base64 text|*.b64;*.txt|All files|*.*" : "All files|*.*" };
        if (dialog.ShowDialog() != true) return;
        if (encode) File.WriteAllText(dialog.FileName, outputText, new UTF8Encoding(false));
        else File.WriteAllBytes(dialog.FileName, outputBytes);
        Record("Save");
        Host.Notify.Success(Loc.T("Saved") + " " + Path.GetFileName(dialog.FileName) + ".");
    }

    private void Record(string how)
    {
        if (lastSummary.Length == 0) return;
        var size = fileName != null ? fileSize : Encoding.UTF8.GetByteCount(input.Text);
        Dictionary<string, string>? files = null;
        if (keepContent && fileName == null && size <= HistoryContentLimit && outputText.Length <= HistoryContentLimit * 2)
            files = new() { [$"tools/base64/{DateTime.Now:yyyyMMdd-HHmmss-fff}.txt"] = $"Direction: {(encode ? "encode" : "decode")}\n--- input ---\n{input.Text}\n--- output ---\n{outputText}\n" };
        Host.History.Record("base64", $"{how}: {lastSummary}" + (files == null ? " (summary only)" : ""), files);
    }
}
