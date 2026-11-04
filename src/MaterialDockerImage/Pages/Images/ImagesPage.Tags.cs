using System.Windows;
using MaterialDockerImage.UI;
using MaterialDockerImage.Services;

namespace MaterialDockerImage.Pages;

/// <summary>Tag-oriented image operations: retag to several names at once, push every tag of a repository, copy a pull command.</summary>
public sealed partial class ImagesPage
{
    private const int MaxRetagTargets = 50;

    internal async Task RetagManyAsync(ImageRow image)
    {
        if (!EnsureCurrent()) return;
        var box = Ui.CodeBox(image.Tagged ? image.Repository + ":" : "", 120);
        box.AcceptsReturn = true;
        var form = Ui.VStack(12,
            Ui.Caption($"Adds every name below to image {image.ShortId}. Nothing is copied or uploaded; each name is created with docker image tag."),
            Ui.Field("New references, one per line", box,
                $"Each line is registry/repository:tag. Empty lines and duplicates are ignored. At most {MaxRetagTargets} names at once.",
                "ghcr.io/acme/api:1.4.0\nghcr.io/acme/api:1.4\nghcr.io/acme/api:latest"));
        PageKit.Dialog(Loc.T("Retag") + " " + image.Name + " to multiple tags", null, form, "Add tags", async () =>
        {
            var targets = box.Text.Replace("\r\n", "\n").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToList();
            if (targets.Count == 0) return "Enter at least one reference.";
            if (targets.Count > MaxRetagTargets) return $"Enter at most {MaxRetagTargets} references at once.";
            foreach (var t in targets)
            {
                if (PageKit.ValidateReference(t, false) is { } problem) return $"\"{t}\": {problem}";
                if (t.EndsWith(':')) return $"\"{t}\": enter a tag after the colon.";
            }
            if (!EnsureCurrent()) return PageKit.StaleMessage(SnapshotContext);
            var failures = new List<string>();
            foreach (var t in targets)
            {
                var result = await Host.Execute($"Tag {image.Reference} as {t}", ["image", "tag", image.Id, t], boundContext: SnapshotContext);
                if (result == null) { failures.Add(t + ": not started"); break; }
                if (!result.Success) failures.Add($"{t}: {PageKit.Failure(result)}");
            }
            Report("Tag", targets.Count, failures);
            await SafeRefresh();
            return failures.Count == 0 ? null : $"{failures.Count} of {targets.Count} tags were not added. See the notification and the output panel.";
        }, width: 620);
        await Task.CompletedTask;
    }

    internal async Task PushRepositoryAsync(ImageRow image)
    {
        if (!EnsureCurrent()) return;
        if (!image.Tagged) { Host.Notify.Warning("This image has no repository name. Tag it as registry/repository:tag first."); return; }
        var repository = image.Repository;
        if (PageKit.ValidateReference(repository, false, false) is { } problem) { Host.Notify.Error($"\"{repository}\": {problem}"); return; }
        var tags = table.Items.Where(i => i.Tagged && i.Repository == repository).Select(i => i.Tag).Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal).ToList();
        var list = string.Join("\n", tags.Take(30).Select(t => "• " + repository + ":" + t)) + (tags.Count > 30 ? $"\n…and {tags.Count - 30} more" : "");
        var ok = await Host.Overlay.ConfirmAsync($"Push all {tags.Count} tags of {repository}",
            $"docker image push --all-tags uploads every local tag of this repository to {ImageRow.RegistryOf(repository)} using the credentials the engine already has:\n\n{list}\n\nThe engine pushes the tags it has at that moment, which may differ if it changed since the last refresh. Pushed images may become visible to anyone who can read the repository.",
            "Push all tags");
        if (!ok || !EnsureCurrent()) return;
        var result = await Host.Execute("Push all tags of " + repository, ["image", "push", "--all-tags", repository], boundContext: SnapshotContext);
        if (result != null && !result.Success) Host.Notify.Error("Push all tags did not complete. " + PageKit.Failure(result));
    }

    internal void CopyPullCommand(ImageRow image)
    {
        if (image.Tagged) { PageKit.Copy("docker pull " + image.Reference, "the pull command"); return; }
        if (image.Repository != "<none>" && image.Digest.StartsWith("sha256:", StringComparison.Ordinal))
        {
            PageKit.Copy("docker pull " + image.Repository + "@" + image.Digest, "the pull command");
            return;
        }
        Host.Notify.Warning("This image has no repository name or digest, so it cannot be pulled from a registry. Tag and push it first.");
    }
}
