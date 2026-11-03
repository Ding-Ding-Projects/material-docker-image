using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Context actions. Context management commands are CLI-local (they edit the docker CLI configuration), so reads run
/// without a --context flag. Nothing here runs <c>docker context use</c>: switching only changes this window.
/// </summary>
public sealed class ContextActions
{
    private static AppHost Host => AppHost.Current;
    private readonly Func<Task> reload;
    private readonly Func<string?> cliCurrent;

    public ContextActions(Func<Task> reload, Func<string?> cliCurrent) { this.reload = reload; this.cliCurrent = cliCurrent; }

    /// <summary>The context this window sends commands to: its own selection, or the CLI's current context when it has none.</summary>
    public string? WindowContext => Host.Context ?? cliCurrent();

    /// <summary>Reads a CLI-local command with no --context flag.</summary>
    public static Task<CommandResult> ReadLocal(IReadOnlyList<string> args, CancellationToken token = default)
    {
        try { return Host.Docker.ExecuteAsync(args, null, null, token); }
        catch (System.ComponentModel.Win32Exception) { return Task.FromResult(new CommandResult(-1, "", "The docker command was not found. Install Docker or add it to PATH.")); }
    }

    public void UseInWindow(ContextEntry entry)
    {
        if (entry.Error.Length > 0) { Host.Notify.Error($"Context {entry.Name} has a configuration error: {entry.Error}"); return; }
        Host.SetContext(entry.Name);
        if (!Host.IsCapture) Host.History.Record("context", $"This window now uses engine context {entry.Name} (the docker CLI's current context is unchanged)");
        Host.Notify.Success(Loc.Voice($"This window now uses context {entry.Name}. Other windows and the docker CLI are unchanged.", "Switched.", "轉咗。"));
    }

    public async Task InspectAsync(ContextEntry entry)
    {
        var r = await ReadLocal(["context", "inspect", entry.Name]);
        if (!r.Success) { Host.Notify.Error($"Inspect {entry.Name} failed: {r.StandardError.Trim()}"); return; }
        ResourceTextViewer.Show("Context " + entry.Name, PageParts.Sanitize(r.StandardOutput), "docker context inspect · TLS material is listed by file name only; key contents are never shown");
    }

    public ContextMenu BuildMenu(ContextEntry entry)
    {
        var menu = new ContextMenu();
        menu.Opened += (_, _) =>
        {
            menu.Items.Clear();
            foreach (var item in MenuItems(entry)) menu.Items.Add(item);
        };
        menu.Items.Add(new MenuItem { Header = "" });
        return menu;
    }

    private IEnumerable<object> MenuItems(ContextEntry entry)
    {
        yield return Ui.MenuItem("Use in this window", Icons.SwapHoriz, () => UseInWindow(entry));
        yield return Ui.MenuItem("Inspect", Icons.DataObject, () => _ = Guard(() => InspectAsync(entry)));
        yield return Ui.MenuItem("Copy name", Icons.ContentCopy, () => PageKit.Copy(entry.Name, "the context name"));
        yield return Ui.MenuItem("Copy endpoint", Icons.ContentCopy, () => PageKit.Copy(entry.Endpoint, "the endpoint"));
        yield return new Separator();
        yield return Ui.MenuItem("Update description or endpoint…", Icons.Edit, () => ContextForm.ShowUpdate(entry, reload));
        yield return Ui.MenuItem("Export…", Icons.FileExport, () => _ = Guard(() => ExportAsync(entry)));
        yield return new Separator();
        yield return Ui.MenuItem("Remove…", Icons.Delete, () => _ = Guard(() => RemoveAsync(entry)), null, true);
    }

    public void ShowMenu(FrameworkElement anchor, ContextEntry entry)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var item in MenuItems(entry)) menu.Items.Add(item);
        menu.IsOpen = true;
    }

    private static async Task Guard(Func<Task> action)
    {
        try { await action(); } catch (Exception e) when (e is not OutOfMemoryException) { Host.Notify.Error(e.Message); }
    }

    public async Task ExportAsync(ContextEntry entry)
    {
        var path = PageKit.PickSavePath(PageKit.SafeFileName(entry.Name) + ".dockercontext", "Docker context export|*.dockercontext|All files|*.*");
        if (path == null) return;
        if (!await Host.Overlay.ConfirmAsync("Export context " + entry.Name,
            "The export contains the endpoint and any TLS certificate and private key stored with this context. Treat the file like a password and share it only with people who should control this engine." + (File.Exists(path) ? " The existing file at the chosen path is replaced when the export succeeds." : ""),
            "Export")) return;
        // Export to a temporary file next to the destination, then replace, so a failed export never destroys the old file.
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp");
        var result = await Host.Execute("Export context " + entry.Name, ["context", "export", entry.Name, temporary]);
        if (result?.Success == true && File.Exists(temporary))
        {
            File.Move(temporary, path, true);
            Host.Notify.Info($"Saved {Path.GetFileName(path)} ({Ui.FormatBytes(new FileInfo(path).Length)}).");
        }
        else if (File.Exists(temporary)) File.Delete(temporary);
    }

    public async Task ImportAsync()
    {
        var file = PageKit.PickOpenPath("Choose a context export", "Docker context export|*.dockercontext;*.tar|All files|*.*");
        if (file == null) return;
        var suggested = Path.GetFileNameWithoutExtension(file);
        var name = await Host.Overlay.PromptAsync("Import context", "Context name", ContextEndpoint.ValidateName(suggested) == null ? suggested : "",
            "The imported context is added to the docker CLI configuration under this name. Names must be unique.", ContextEndpoint.ValidateName);
        if (name == null) return;
        var result = await Host.Execute("Import context " + name, ["context", "import", name, file]);
        if (result?.Success == true && !Host.IsCapture) Host.History.Record("context", $"Imported context {name} from {Path.GetFileName(file)}");
        await reload();
    }

    public async Task RemoveAsync(ContextEntry entry)
    {
        if (ContextEndpoint.RemovalRefusal(entry.Name, Host.Context, cliCurrent()) is { } refusal) { Host.Notify.Error(refusal); return; }
        var cliNote = entry.CliCurrent ? " It is the docker CLI's current context; the CLI refuses to remove it until another context is current there." : "";
        if (!await Host.Overlay.ConfirmAsync("Remove context " + entry.Name,
            $"Removes the context and its stored TLS material from the docker CLI configuration. The engine itself and its data are not touched. Endpoint: {entry.Endpoint}.{cliNote}", "Remove", true)) return;
        var result = await Host.Execute("Remove context " + entry.Name, ["context", "rm", entry.Name]);
        if (result?.Success == true && !Host.IsCapture) Host.History.Record("context", $"Removed context {entry.Name} (endpoint {entry.Endpoint})");
        await reload();
    }
}
