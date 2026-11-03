using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Image operations. Each one checks the snapshot, validates every operand and reports the real process result.</summary>
public sealed partial class ImagesPage
{
    private static readonly Regex Platform = new(@"^[a-z0-9]+/[a-z0-9_]+(/[a-z0-9]+)?$", RegexOptions.None, TimeSpan.FromMilliseconds(100));

    private async Task PullAsync()
    {
        if (!EnsureCurrent()) return;
        var reference = Ui.Input("", "Image reference", true);
        var platform = Ui.Input("", "Platform", true);
        var allTags = Ui.Switch("Pull all tags", false, _ => { }, "--all-tags downloads every tag of the repository. This can be very large.");
        var form = Ui.VStack(12,
            Ui.Field("Image reference", reference, "registry/repository[:tag][@digest]. Docker Hub is used when no registry host is given, and latest when no tag is given.", "nginx:1.27-alpine\nghcr.io/acme/api:1.4.0"),
            Ui.Field("Platform", platform, "Optional --platform os/arch[/variant] for multi-platform images. Leave empty for the engine's platform.", "linux/arm64"),
            allTags,
            Ui.Caption("Uses the registry credentials already stored by the engine. Sign in from Registry when a private registry asks for authentication."));
        PageKit.Dialog("Pull image", "Downloads an image from a registry into context " + (SnapshotContext ?? "default") + ".", form, "Pull", async () =>
        {
            var value = reference.Text.Trim();
            if (PageKit.ValidateReference(value, allTags.IsChecked != true, allTags.IsChecked != true) is { } problem) return problem;
            var args = new List<string> { "image", "pull" };
            if (platform.Text.Trim() is { Length: > 0 } p)
            {
                if (!Platform.IsMatch(p)) return "Platform uses os/arch[/variant], for example linux/amd64.";
                args.AddRange(["--platform", p]);
            }
            if (allTags.IsChecked == true) args.Add("--all-tags");
            args.Add(value);
            if (!EnsureCurrent()) return PageKit.StaleMessage(SnapshotContext);
            var result = await Host.Execute("Pull " + value, args, boundContext: SnapshotContext);
            if (result?.Success != true) return "Pull did not complete. " + PageKit.Failure(result);
            await SafeRefresh();
            return null;
        });
        await Task.CompletedTask;
    }

    private async Task LoadArchiveAsync()
    {
        if (!EnsureCurrent()) return;
        var path = PageKit.PickOpenPath("Choose an image archive created by docker image save", "Image archives|*.tar;*.tar.gz;*.tgz;*.tar.xz;*.tar.bz2|All files|*.*");
        if (path == null) return;
        var result = await Host.Execute("Load " + Path.GetFileName(path), ["image", "load", "--input", Path.GetFullPath(path)], boundContext: SnapshotContext);
        if (result?.Success == true)
        {
            var loaded = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(l => l.StartsWith("Loaded", StringComparison.Ordinal)).ToList();
            if (loaded.Count > 0) Host.Notify.Info(string.Join("\n", loaded.Take(6)));
            await SafeRefresh();
        }
    }

    private async Task ImportAsync()
    {
        if (!EnsureCurrent()) return;
        var file = Ui.Input("", "Tarball", true);
        var browse = Ui.Button("Choose file", Icons.FolderOpen, ButtonKind.Outlined, () =>
        {
            if (PageKit.PickOpenPath("Choose a filesystem tarball (for example from docker container export)", "Tarballs|*.tar;*.tar.gz;*.tgz;*.tar.xz|All files|*.*") is { } p) file.Text = p;
        });
        var reference = Ui.Input("", "Image reference", true);
        var platform = Ui.Input("", "Platform", true);
        var message = Ui.Input("", "Commit message");
        var form = Ui.VStack(12,
            Ui.Caption("docker image import creates a single-layer image from a root filesystem tarball. It is different from Load archive, which restores images saved with docker image save (including tags and layers). Imported images have no command or environment; set them when running."),
            Ui.Field("Tarball", Ui.Columns(8, [1, 0], file, browse), "A tar file of a root filesystem on this computer.", @"C:\exports\rootfs.tar"),
            Ui.Field("Image reference", reference, "Optional repository:tag for the new image. Without it the image is untagged.", "my-rootfs:1.0"),
            Ui.Field("Platform", platform, "Optional os/arch for the new image.", "linux/amd64"),
            Ui.Field("Commit message", message, "Optional message recorded in the image history.", "Imported from rootfs.tar"));
        PageKit.Dialog("Import tarball as image", null, form, "Import", async () =>
        {
            var path = file.Text.Trim();
            if (path.Length == 0 || !File.Exists(path)) return "Choose an existing tarball.";
            var args = new List<string> { "image", "import" };
            if (platform.Text.Trim() is { Length: > 0 } p) { if (!Platform.IsMatch(p)) return "Platform uses os/arch[/variant]."; args.AddRange(["--platform", p]); }
            if (message.Text.Trim() is { Length: > 0 } m) { try { StructuredInputs.Token(m); } catch (ArgumentException e) { return e.Message; } args.AddRange(["--message", m]); }
            args.Add(Path.GetFullPath(path));
            if (reference.Text.Trim() is { Length: > 0 } r) { if (PageKit.ValidateReference(r, false) is { } problem) return problem; args.Add(r); }
            if (!EnsureCurrent()) return PageKit.StaleMessage(SnapshotContext);
            var result = await Host.Execute("Import " + Path.GetFileName(path), args, boundContext: SnapshotContext);
            if (result?.Success != true) return "Import did not complete. " + PageKit.Failure(result);
            await SafeRefresh();
            return null;
        });
        await Task.CompletedTask;
    }

    internal async Task TagAsync(ImageRow image)
    {
        if (!EnsureCurrent()) return;
        var target = await Host.Overlay.PromptAsync("Tag " + image.Name, "New reference", image.Tagged ? image.Repository + ":" : "",
            "Adds another name to image " + image.ShortId + ". Nothing is copied or uploaded. Use registry/repository:tag to prepare a push.",
            v => PageKit.ValidateReference(v, false) ?? (PageKit.HasTag(v) || !v.EndsWith(':') ? null : "Enter a tag after the colon."));
        if (target == null) return;
        if (!EnsureCurrent()) return;
        var result = await Host.Execute($"Tag {image.Reference} as {target}", ["image", "tag", image.Id, target], boundContext: SnapshotContext);
        if (result?.Success == true) await SafeRefresh();
    }

    internal async Task PushAsync(IReadOnlyList<ImageRow> images)
    {
        if (!EnsureCurrent()) return;
        var refs = images.Where(i => i.Tagged).Select(i => i.Reference).Distinct().ToList();
        var skipped = images.Count(i => !i.Tagged);
        if (refs.Count == 0) { Host.Notify.Warning("Only tagged images can be pushed. Tag the image with registry/repository:tag first."); return; }
        var destinations = string.Join("\n", refs.Select(r => $"• {r}  →  {ImageRow.RegistryOf(r)}"));
        var ok = await Host.Overlay.ConfirmAsync(refs.Count == 1 ? "Push image" : $"Push {refs.Count} images",
            $"These references will be uploaded using the credentials the engine already has:\n\n{destinations}" + (skipped > 0 ? $"\n\n{skipped} untagged selection(s) will be skipped." : "") + "\n\nPushed images may become visible to anyone who can read the repository.", "Push");
        if (!ok || !EnsureCurrent()) return;
        var failures = new List<string>();
        foreach (var r in refs)
        {
            var result = await Host.Execute("Push " + r, ["image", "push", r], boundContext: SnapshotContext);
            if (result == null) { failures.Add(r + ": not started"); break; }
            if (!result.Success) failures.Add($"{r}: {PageKit.Failure(result)}");
        }
        Report("Push", refs.Count, failures);
    }

    internal async Task SaveAsync(IReadOnlyList<ImageRow> images)
    {
        if (!EnsureCurrent()) return;
        var refs = images.Select(i => i.Reference).Distinct().ToList();
        if (refs.Count == 0) return;
        var suggested = refs.Count == 1 ? PageKit.SafeFileName(refs[0]) + ".tar" : $"images-{refs.Count}.tar";
        var path = PageKit.PickSavePath(suggested, "Image archive|*.tar|All files|*.*");
        if (path == null || !EnsureCurrent()) return;
        var args = new List<string> { "image", "save", "--output", Path.GetFullPath(path) };
        args.AddRange(refs);
        var result = await Host.Execute(refs.Count == 1 ? "Save " + refs[0] : $"Save {refs.Count} images to one archive", args, boundContext: SnapshotContext);
        if (result?.Success == true) Host.Notify.Info($"Archive written to {path}. Load it on another engine with Load archive.");
    }

    private async Task RemoveOneAsync(ImageRow image)
    {
        if (!EnsureCurrent()) return;
        var inUse = image.Total > 0 ? $"\n\n{image.Usage} container(s) use this image; the engine refuses removal while they exist." : "";
        var detail = image.Tagged ? $"Removes the tag {image.Reference}. Image data is deleted only when no other tag or container references ID {image.ShortId}." : $"Removes image {image.ShortId}.";
        if (!await Host.Overlay.ConfirmAsync("Remove image", detail + inUse, "Remove", true) || !EnsureCurrent()) return;
        var result = await Host.Execute("Remove image " + image.Reference, ["image", "rm", image.Reference], boundContext: SnapshotContext);
        if (result != null) await SafeRefresh();
    }

    private async Task RemoveManyAsync(IReadOnlyList<ImageRow> images)
    {
        if (!EnsureCurrent()) return;
        var refs = images.Select(i => i.Reference).Distinct().ToList();
        if (refs.Count == 0) return;
        if (!await Host.Overlay.SuperConfirmAsync($"Remove {refs.Count} image reference(s)",
            "Each reference is removed with docker image rm (no force). Image data is deleted when nothing else references it. Images used by containers are refused by the engine and reported.", refs)) return;
        if (!EnsureCurrent()) return;
        var failures = new List<string>();
        foreach (var r in refs)
        {
            var result = await Host.Execute("Remove image " + r, ["image", "rm", r], boundContext: SnapshotContext);
            if (result == null) { failures.Add(r + ": not started"); break; }
            if (!result.Success) failures.Add($"{r}: {PageKit.Failure(result)}");
        }
        Report("Remove", refs.Count, failures);
        await SafeRefresh();
    }

    private async Task PruneAsync()
    {
        if (!EnsureCurrent()) return;
        var dangling = table.Items.Where(i => i.Dangling).Select(i => i.ShortId + "  " + i.Size + "  " + i.CreatedSince).ToList();
        if (dangling.Count == 0) { Host.Notify.Info("There are no dangling images in the current list. Refresh to check again."); return; }
        if (!await Host.Overlay.SuperConfirmAsync("Prune dangling images",
            "Runs docker image prune --force on context " + (SnapshotContext ?? "default") + ". It deletes untagged images that no container uses. The engine decides the final set, which may differ if the engine changed since the last refresh.", dangling)) return;
        if (!EnsureCurrent()) return;
        var result = await Host.Execute("Prune dangling images", ["image", "prune", "--force"], boundContext: SnapshotContext);
        if (result?.Success == true && result.StandardOutput.Split('\n').LastOrDefault(l => l.Contains("reclaimed", StringComparison.OrdinalIgnoreCase)) is { } reclaimed) Host.Notify.Info(reclaimed.Trim());
        await SafeRefresh();
    }

    private static void Report(string verb, int count, IReadOnlyList<string> failures)
    {
        if (failures.Count == 0) { if (count > 1) Host.Notify.Success($"{verb}: all {count} completed."); return; }
        Host.Notify.Error($"{verb}: {count - failures.Count} of {count} completed. Failed:\n" + string.Join("\n", failures.Take(8)) + (failures.Count > 8 ? $"\n…and {failures.Count - 8} more (see the output panel)." : ""));
    }
}
