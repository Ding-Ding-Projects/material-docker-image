using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Core;
using MaterialDockerImage.UI;
using Microsoft.Win32;
using MaterialDockerImage.Services;

namespace MaterialDockerImage.Pages;

/// <summary>
/// World tools for one server: pre-generating a chosen area with the Chunky plugin or mod on that server (progress read
/// from its log), and exporting the world folder to a folder or ZIP on this computer with verification.
/// </summary>
internal sealed class MinecraftWorldTab : MinecraftTab
{
    private readonly MinecraftAreaCanvas canvas = new();
    private readonly TextBox widthBox = Ui.Input("100", "Width in chunks", true);
    private readonly TextBox depthBox = Ui.Input("100", "Depth in chunks", true);
    private readonly TextBox centreXBox = Ui.Input("0", "Centre chunk X", true);
    private readonly TextBox centreZBox = Ui.Input("0", "Centre chunk Z", true);
    private readonly TextBlock areaError = Ui.Text("", 12, null, "Error", true, false);
    private readonly TextBlock readout = Ui.Text("", 13, null, "OnSurface", true, false);
    private readonly TextBox commandPreview = Ui.CodeBox("", 90);
    private readonly ContentControl chunkyState = new();
    private readonly ProgressBar progress = new() { Height = 8, Minimum = 0, Maximum = 100 };
    private readonly TextBlock progressText = Ui.Caption("No generation started from this page yet.");
    private readonly ReadOnlySearchText replies = new("Search replies", 80, 220);
    private readonly StringBuilder replyLog = new();
    private readonly Button startButton;
    private readonly TextBox exportPath = Ui.Input("", "Export destination", true);
    private readonly TextBlock exportResult = Ui.Caption("");
    private string exportMode = "Folder";
    private bool syncing;
    private bool chunkyInstalled;
    private CancellationTokenSource? follow;
    private int version;

    public MinecraftWorldTab(MinecraftServerDetail detail) : base(detail)
    {
        commandPreview.IsReadOnly = true;
        System.Windows.Automation.AutomationProperties.SetName(commandPreview, Services.Loc.T("Chunky commands"));
        canvas.AreaChanged += a => SyncFields(a);
        foreach (var box in new[] { widthBox, depthBox, centreXBox, centreZBox }) { box.Width = 110; box.TextChanged += (_, _) => FromFields(); }
        var span = Ui.Segmented(["80", "160", "400", "1000"], "160", v => canvas.SetSpan(int.Parse(v, CultureInfo.InvariantCulture)), false);
        span.Width = 280; span.HorizontalAlignment = HorizontalAlignment.Left;
        var reset = Ui.Button("Default 100 × 100", Icons.CenterFocusStrong, ButtonKind.Text, () => { canvas.Area = MinecraftChunkArea.Default; SyncFields(canvas.Area); });
        var fields = Ui.Wrap(12,
            Ui.Field("Width", widthBox, "East–west size of the area in chunks (16 blocks each).", "100"),
            Ui.Field("Depth", depthBox, "North–south size of the area in chunks.", "100"),
            Ui.Field("Centre X", centreXBox, "Chunk X of the centre. Chunk 0 covers blocks 0 to 15; spawn is usually near chunk 0, 0.", "0"),
            Ui.Field("Centre Z", centreZBox, "Chunk Z of the centre (negative is north).", "0"));
        startButton = Ui.AsyncButton("Start generation", Icons.PlayArrow, ButtonKind.Filled, StartAsync);
        var controls = Ui.Wrap(8,
            startButton,
            Ui.AsyncButton("Pause", Icons.Pause, ButtonKind.Tonal, () => SendAsync("chunky pause")),
            Ui.AsyncButton("Continue", Icons.PlayArrow, ButtonKind.Tonal, async () => { await SendAsync("chunky continue"); Follow(); }),
            Ui.AsyncButton("Cancel generation", Icons.Stop, ButtonKind.Text, CancelAsync),
            Ui.Button("Follow progress", Icons.Monitoring, ButtonKind.Text, Follow));
        var generator = MinecraftControls.Section("Pre-generate an area",
            "Generation runs on this server, using the Chunky plugin or mod installed on it: players may notice lag while it works. Vanilla and Bedrock servers cannot use Chunky.",
            chunkyState,
            Ui.Field("Area", canvas, "Drag on the grid to draw the area. Arrow keys move it by one chunk; Shift and arrow keys resize it. The default is 100 × 100 chunks (1600 × 1600 blocks) around 0, 0.", "100 × 100 chunks = radius 800 blocks"),
            Ui.Wrap(12, span, reset),
            fields, areaError, readout,
            Ui.Field("Commands sent through RCON", commandPreview, "Exactly what Start generation sends with rcon-cli, one command at a time."),
            controls, progress, progressText, replies);
        var browse = Ui.Button("Browse", Icons.FolderOpen, ButtonKind.Outlined, BrowseExport);
        var mode = Ui.Segmented(["Folder", "ZIP"], "Folder", v => { exportMode = v; exportPath.Text = ""; }, false);
        mode.Width = 200; mode.HorizontalAlignment = HorizontalAlignment.Left;
        var export = MinecraftControls.Section("Export world",
            "Copies the world folder out of the server with docker cp after saving it through RCON, then verifies the file count and size of what was written.",
            Ui.Field("Output", mode, "Folder writes a new folder and never overwrites an existing one. ZIP writes to a temporary file and renames it only after it is verified.", "survival-world-20250101-1200.zip"),
            Ui.Field("Destination", Ui.Columns(8, [1, 0], exportPath, browse), "For Folder: the parent folder; a new folder named after the world and time is created inside. For ZIP: the archive file name."),
            Ui.AsyncButton("Export world", Icons.FileExport, ButtonKind.Filled, ExportAsync), exportResult);
        Children.Add(Ui.Scroll(Ui.VStack(16, generator, export)));
        SyncFields(canvas.Area);
        IsVisibleChanged += (_, _) => { if (!IsVisible) StopFollowing(); };
    }

    public override void OnShown() => _ = DetectAsync(++version);
    public override void OnHidden() => StopFollowing();

    private void SyncFields(MinecraftChunkArea a)
    {
        syncing = true;
        widthBox.Text = a.WidthChunks.ToString(CultureInfo.InvariantCulture);
        depthBox.Text = a.DepthChunks.ToString(CultureInfo.InvariantCulture);
        centreXBox.Text = ((a.MinChunkX + a.MaxChunkX + 1) / 2).ToString(CultureInfo.InvariantCulture);
        centreZBox.Text = ((a.MinChunkZ + a.MaxChunkZ + 1) / 2).ToString(CultureInfo.InvariantCulture);
        syncing = false;
        areaError.Text = "";
        Describe(a);
    }

    private void FromFields()
    {
        if (syncing) return;
        static bool Int(TextBox b, int min, int max, out int v) => int.TryParse(b.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) && v >= min && v <= max;
        if (!Int(widthBox, 1, 10000, out var w) || !Int(depthBox, 1, 10000, out var d)) { areaError.Text = Loc.T("Width and depth are whole numbers of chunks from 1 to 10000."); return; }
        if (!Int(centreXBox, -1_875_000, 1_875_000, out var x) || !Int(centreZBox, -1_875_000, 1_875_000, out var z)) { areaError.Text = Loc.T("Centre chunks are whole numbers within the world border (±1,875,000)."); return; }
        areaError.Text = "";
        canvas.Area = MinecraftChunkArea.Centred(w, d, x, z);
        Describe(canvas.Area);
    }

    private void Describe(MinecraftChunkArea a)
    {
        readout.Text = $"{a.WidthChunks} × {a.DepthChunks} chunks = {a.ChunkCount:N0} chunks · {a.WidthBlocks:N0} × {a.DepthBlocks:N0} blocks · centre block {a.CentreBlockX}, {a.CentreBlockZ} · radius {a.RadiusBlocksX}" + (a.IsSquare ? "" : $" × {a.RadiusBlocksZ}") + " blocks";
        commandPreview.Text = string.Join(Environment.NewLine, a.ChunkyCommands());
    }

    private async Task DetectAsync(int mine)
    {
        var s = Server;
        startButton.IsEnabled = false;
        if (!s.IsJava) { chunkyState.Content = Notice("Bedrock Dedicated Server has no plugin or mod loader, so area pre-generation is not available for this server."); return; }
        if (!MinecraftSpecRules.SupportsChunky(s.Spec.Flavour)) { chunkyState.Content = Notice($"Chunky needs Paper, Purpur, Folia, Spigot, Bukkit, Fabric, Forge or NeoForge. This server is {s.FlavourText}. Change the server type in Properties to use pre-generation.", Tone.Warning); return; }
        if (!s.Spec.RconEnabled) { chunkyState.Content = Notice("RCON is off, so Chunky commands cannot be sent. Turn it on in Properties.", Tone.Warning); return; }
        var listed = s.Spec.ModrinthProjects.Any(p => p.Equals("chunky", StringComparison.OrdinalIgnoreCase));
        if (!s.IsRunning)
        {
            chunkyState.Content = listed ? Notice("Chunky is listed in MODRINTH_PROJECTS. Start the server to install it and generate.", Tone.Warning) : InstallCard("The server is stopped and Chunky is not listed in MODRINTH_PROJECTS.");
            return;
        }
        var r = await Host.Read(["exec", s.Id, "sh", "-c", "ls /data/plugins /data/mods 2>/dev/null"], Context);
        if (mine != version) return;
        chunkyInstalled = r.StandardOutput.Contains("chunky", StringComparison.OrdinalIgnoreCase);
        if (chunkyInstalled)
        {
            chunkyState.Content = Ui.Card(Ui.Columns(10, [0, 1], Ui.Icon(Icons.CheckCircle, 20, true, "OnSuccessContainer"), Ui.Text("Chunky found in /data/" + (MinecraftSpecRules.IsPluginFlavour(s.Spec.Flavour) ? "plugins" : "mods") + " on " + s.Name + ".", 13, null, "OnSuccessContainer", true, false)), 12, "SuccessContainer", 12);
            startButton.IsEnabled = true;
        }
        else chunkyState.Content = listed ? Notice("Chunky is listed in MODRINTH_PROJECTS but no Chunky file was found yet. Check the console for download errors, or restart the server.", Tone.Warning) : InstallCard("Chunky was not found in /data/plugins or /data/mods.");
    }

    private FrameworkElement InstallCard(string reason)
    {
        var install = Ui.AsyncButton("Install Chunky…", Icons.Extension, ButtonKind.Tonal, InstallAsync);
        return Ui.Card(Ui.VStack(10, Ui.Text(reason + " Installing adds chunky to MODRINTH_PROJECTS and recreates the container (the world volume is kept); the image downloads the build matching this server.", 13, null, "OnWarningContainer", true, true), install), 12, "WarningContainer", 12);
    }

    private async Task InstallAsync()
    {
        if (!EnsureCurrent()) return;
        var spec = Server.Spec.Clone();
        spec.ModrinthProjects.Add("chunky");
        if (!await Host.Overlay.ConfirmAsync(Loc.T("Install Chunky on") + " " + Server.Name + "?", Loc.T("Adds chunky to MODRINTH_PROJECTS and recreates the container with the same world volume.") + " " + (Server.IsRunning ? "The server is saved, stopped and started again." : "Start the server afterwards to download it."), "Install and recreate")) return;
        await MinecraftOps.RecreateAsync(Server, spec, Context);
        await Detail.ReloadAsync();
    }

    private void Reply(string text)
    {
        replyLog.AppendLine(text);
        replies.Text = replyLog.ToString();
    }

    private async Task<bool> SendAsync(string command)
    {
        if (!EnsureCurrent()) return false;
        var r = await MinecraftOps.CommandAsync(Server, command, Context);
        var reply = MinecraftOutput.StripFormatting(r.Success ? r.StandardOutput : MinecraftOps.Trim(r)).Trim();
        Reply("› " + command + (reply.Length > 0 ? Environment.NewLine + "‹ " + reply : ""));
        if (!r.Success) Host.Notify.Error(command + " failed: " + MinecraftOps.Trim(r));
        return r.Success;
    }

    private async Task StartAsync()
    {
        if (!chunkyInstalled || !Server.CanRcon) { Host.Notify.Error("Chunky must be installed and the server running with RCON on."); return; }
        var a = canvas.Area;
        if (!await Host.Overlay.ConfirmAsync(Loc.T("Generate") + " " + a.ChunkCount.ToString("N0", CultureInfo.CurrentCulture) + " chunks on " + Server.Name + "?", $"Chunky generates {a.WidthBlocks:N0} × {a.DepthBlocks:N0} blocks around {a.CentreBlockX}, {a.CentreBlockZ} on the running server. It uses CPU and disk and can take a long time for large areas; you can pause or cancel it here.", "Start")) return;
        Follow();
        foreach (var c in a.ChunkyCommands())
            if (!await SendAsync(c)) { progressText.Text = Loc.T("Stopped:") + " " + c + " failed. See the replies below."; return; }
        progressText.Text = Loc.T("Started. Progress appears when Chunky reports it in the server log.");
        Host.History.Record("minecraft", $"Started Chunky pre-generation on {Server.Name}: {a.WidthChunks} × {a.DepthChunks} chunks around block {a.CentreBlockX}, {a.CentreBlockZ}");
    }

    private async Task CancelAsync()
    {
        if (!await Host.Overlay.ConfirmAsync("Cancel generation?", "Stops the Chunky task. Chunks generated so far stay in the world.", "Cancel generation", true)) return;
        if (!await SendAsync("chunky cancel")) return;
        if (replyLog.ToString().Contains("confirm", StringComparison.OrdinalIgnoreCase)) await SendAsync("chunky confirm");
        StopFollowing();
        progressText.Text = Loc.T("Canceled.");
    }

    private void StopFollowing() { follow?.Cancel(); follow = null; }

    /// <summary>Follows new log lines and updates the progress bar from Chunky's progress messages.</summary>
    private void Follow()
    {
        StopFollowing();
        var s = Server;
        if (!s.IsRunning) { progressText.Text = Loc.T("The server is not running."); return; }
        var cts = new CancellationTokenSource();
        follow = cts;
        var partial = new StringBuilder();
        progressText.Text = Loc.T("Following the server log for Chunky progress…");
        var since = DateTimeOffset.UtcNow.AddSeconds(-5).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var report = new Progress<DockerOutput>(o =>
        {
            if (follow != cts) return;
            partial.Append(o.Text.Replace("\r\n", "\n"));
            var text = partial.ToString();
            var last = text.LastIndexOf('\n');
            if (last < 0) return;
            partial.Clear().Append(text[(last + 1)..]);
            foreach (var line in text[..last].Split('\n'))
            {
                if (MinecraftOutput.ParseChunkyProgress(line) is { } p)
                {
                    progress.Value = Math.Clamp(p.Percent, 0, 100);
                    progressText.Text = $"{p.Percent:0.00}% · {p.Chunks:N0} chunks processed" + (p.Eta != null ? " · about " + p.Eta + " left" : "") + " · from the server log";
                }
                if (MinecraftOutput.IsChunkyFinished(line)) { progress.Value = 100; progressText.Text = Loc.T("Chunky reported the task finished:") + " " + line.Trim(); }
            }
        });
        _ = Run();
        async Task Run()
        {
            try { await Host.Docker.ExecuteAsync(["container", "logs", "--follow", "--since", since, s.Id], Context, report, cts.Token); }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException or IOException) { progressText.Text = Loc.T("Could not follow the log:") + " " + e.Message; }
            finally { if (follow == cts) follow = null; cts.Dispose(); }
        }
    }

    private void BrowseExport()
    {
        if (exportMode == "Folder")
        {
            var picker = new OpenFolderDialog { Title = Services.Loc.T("Choose where to create the exported world folder") };
            if (picker.ShowDialog() == true) exportPath.Text = picker.FolderName;
        }
        else
        {
            var dialog = new SaveFileDialog { FileName = DefaultExportName() + ".zip", Filter = "ZIP archive|*.zip", OverwritePrompt = true };
            if (dialog.ShowDialog() == true) exportPath.Text = dialog.FileName;
        }
    }

    private string DefaultExportName() => PageKit.SafeFileName(Server.Name + "-" + Server.LevelName + "-" + DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture));

    private static (int Files, long Bytes) Measure(string folder)
    {
        var files = new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).ToList();
        return (files.Count, files.Sum(f => f.Length));
    }

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), false);
    }

    private async Task ExportAsync()
    {
        if (!EnsureCurrent()) return;
        var destination = exportPath.Text.Trim();
        if (destination.Length == 0) { exportResult.Text = "Choose a destination with Browse first."; return; }
        var s = Server;
        if (s.CanRcon && !(await MinecraftOps.CommandAsync(s, "save-all flush", Context)).Success) Host.Notify.Warning("save-all flush failed; the export may miss the most recent changes.");
        else if (s.IsRunning && !s.CanRcon) Host.Notify.Warning("The world could not be saved first (no RCON); the export is taken from the live files.");
        var temp = Path.Combine(Path.GetTempPath(), "MaterialDockerImage", "mc-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
        var source = s.IsJava ? "/data/" + s.LevelName : "/data/worlds";
        try
        {
            var cp = await Host.Execute($"Copy world of {s.Name} out of the container", ["container", "cp", s.Id + ":" + source, temp], true, Context);
            if (cp is not { Success: true }) { exportResult.Text = "docker cp failed; nothing was written to the destination."; return; }
            var (files, bytes) = await Task.Run(() => Measure(temp));
            string written;
            if (exportMode == "Folder")
            {
                var target = Path.Combine(destination, DefaultExportName());
                if (Directory.Exists(target) || File.Exists(target)) { exportResult.Text = target + " already exists. Folder export never overwrites; choose another folder."; return; }
                await Task.Run(() => CopyTree(temp, target));
                var check = await Task.Run(() => Measure(target));
                if (check != (files, bytes)) { exportResult.Text = $"Verification failed: expected {files} files and {Ui.FormatBytes(bytes)}, found {check.Files} files and {Ui.FormatBytes(check.Bytes)} in {target}."; return; }
                written = target;
            }
            else
            {
                var zip = destination.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? destination : destination + ".zip";
                var partial = zip + ".partial";
                if (File.Exists(partial)) File.Delete(partial);
                await Task.Run(() => ZipFile.CreateFromDirectory(temp, partial, CompressionLevel.Optimal, false));
                var check = await Task.Run(() =>
                {
                    using var archive = ZipFile.OpenRead(partial);
                    var entries = archive.Entries.Where(e => !e.FullName.EndsWith('/')).ToList();
                    return (entries.Count, entries.Sum(e => e.Length));
                });
                if (check != (files, bytes)) { File.Delete(partial); exportResult.Text = $"Verification failed: the ZIP holds {check.Item1} files and {Ui.FormatBytes(check.Item2)}, expected {files} files and {Ui.FormatBytes(bytes)}. Nothing was kept."; return; }
                File.Move(partial, zip, true);
                written = zip;
            }
            exportResult.Text = $"Exported and verified: {files:N0} files, {Ui.FormatBytes(bytes)} → {written}";
            Host.History.Record("minecraft", $"Exported world of {s.Name} ({files} files, {Ui.FormatBytes(bytes)}) to {written}");
            Host.Notify.Show(Services.NoticeKind.Success, "World exported to " + written, "Open folder", () => PageParts.OpenExternal(exportMode == "Folder" ? written : Path.GetDirectoryName(written)!));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            exportResult.Text = Loc.T("The export failed:") + " " + e.Message;
        }
        finally
        {
            try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
