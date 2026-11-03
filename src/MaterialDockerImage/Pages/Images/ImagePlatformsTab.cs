using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Core;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Platforms tab: the local platform from docker image inspect, and the registry manifest (OCI index or Docker manifest
/// list) from docker buildx imagetools inspect --raw with platform rows and attestation manifests.
/// </summary>
internal sealed class ImagePlatformsTab : ImageDetailTab
{
    private readonly ScrollViewer view;
    private readonly ContentControl registry = new();
    private string rawManifest = "";

    public ImagePlatformsTab(ImageDetailContext context) : base(context)
    {
        var digests = context.RepoDigests;
        var local = Ui.Card(Ui.VStack(6,
            Ui.HStack(8, Ui.Icon(Icons.DeveloperBoard, 18, true, "Primary"), Ui.Overline("On this engine")),
            PageKit.Fact("Platform", context.Platform, true),
            PageKit.Fact("Repository digests", digests.Count == 0 ? "None (built, imported or tagged locally)" : string.Join("\n", digests), digests.Count > 0)), 14, "SurfaceContainer", 12);
        view = Ui.Scroll(Ui.VStack(14, local, Ui.Overline("Registry manifest"), registry));
    }

    public override string Title => "Platforms";
    public override FrameworkElement View => view;

    /// <summary>The reference to query: the pulled repository digest when known, otherwise the tag.</summary>
    private string? QueryReference => Ctx.RepoDigests.FirstOrDefault() ?? (Ctx.Image.Tagged ? Ctx.Image.Reference : null);

    protected override async Task OnActivateAsync()
    {
        if (Ctx.RepoDigests.Count > 0) { await QueryAsync(Ctx.RepoDigests[0]); return; }
        if (Ctx.Image.Tagged)
        {
            var reference = Ctx.Image.Reference;
            registry.Content = Ui.Card(Ui.VStack(8,
                Ui.Text("This image has no repository digest, so it was built, loaded or tagged locally. The registry can still be asked what it publishes under the same name, which may be a different image.", 13, null, "OnSurfaceVariant"),
                Ui.Mono(reference, 12.5, "OnSurface", true),
                Ui.AsyncButton("Query registry for this name", Icons.CloudSync, ButtonKind.Tonal, () => QueryAsync(reference))), 14, "SurfaceContainer", 12);
            return;
        }
        registry.Content = Ui.Card(Ui.Text("This image has no repository name, so there is no registry manifest to read. Tag it as registry/repository:tag and push it to publish a manifest.", 13, null, "OnSurfaceVariant"), 14, "SurfaceContainer", 12);
    }

    private async Task QueryAsync(string reference)
    {
        if (PageKit.ValidateReference(reference) is { } invalid) { registry.Content = Ui.Text(invalid, 13, null, "Error", true, false); return; }
        registry.Content = Loading("Reading the manifest of " + reference + " from the registry…");
        var result = await Host.Read(["buildx", "imagetools", "inspect", "--raw", reference], Ctx.Context);
        if (!result.Success)
        {
            var output = (result.StandardError + "\n" + result.StandardOutput).Trim();
            var hint = output.Contains("is not a docker command", StringComparison.OrdinalIgnoreCase) || output.Contains("unknown command", StringComparison.OrdinalIgnoreCase)
                ? "Docker Buildx is required to read registry manifests. It is included with Docker Desktop and the docker-buildx-plugin package."
                : "The registry could not be read. The reference may not exist there, the registry may need you to sign in, or the network may be unavailable.";
            registry.Content = Failure($"docker buildx imagetools inspect --raw {reference} exited with code {result.ExitCode}.", hint, output, () => QueryAsync(reference));
            return;
        }
        ImageManifestReport report;
        try { report = ImageManifest.Parse(result.StandardOutput); }
        catch (FormatException e) { registry.Content = Failure("The manifest could not be parsed.", e.Message, result.StandardOutput, () => QueryAsync(reference)); return; }
        rawManifest = result.StandardOutput;
        registry.Content = Render(report, reference);
    }

    private static UIElement Failure(string title, string hint, string output, Func<Task> retry)
    {
        var box = Ui.CodeBox(output.Length > 0 ? output : "(no output)", 40);
        box.IsReadOnly = true; box.MaxHeight = 140;
        return Ui.Card(Ui.VStack(8,
            Ui.HStack(8, Ui.Icon(Icons.SyncProblem, 18, true, "OnErrorContainer"), Ui.Text(title, 13.5, FontWeights.SemiBold, "OnErrorContainer", true, false)),
            Ui.Text(hint, 13, null, "OnErrorContainer"),
            box,
            Ui.AsyncButton("Try again", Icons.Refresh, ButtonKind.Text, retry)), 14, "ErrorContainer", 12);
    }

    private UIElement Render(ImageManifestReport report, string reference)
    {
        var raw = Ui.Button("View raw manifest", Icons.DataObject, ButtonKind.Text, () =>
            PageKit.ShowText("Manifest of " + reference, "docker buildx imagetools inspect --raw output", PageKit.FormatInspect(rawManifest, false) ?? rawManifest, PageKit.SafeFileName(reference) + "-manifest.json"));
        var summary = Ui.Wrap(8,
            Ui.Badge(report.KindText, Tone.Primary, true),
            report.IsIndex ? Ui.Badge($"{report.Platforms.Count} {(report.Platforms.Count == 1 ? "platform" : "platforms")}", Tone.Neutral) : null,
            report.IsIndex ? Ui.Badge($"{report.Attestations.Count} {(report.Attestations.Count == 1 ? "attestation" : "attestations")}", report.Attestations.Count > 0 ? Tone.Success : Tone.Neutral) : null);
        var head = Ui.VStack(6, Ui.Mono(reference, 12, "OnSurfaceVariant", true), Ui.Columns(8, [1, 0], summary, raw));
        if (!report.IsIndex)
        {
            return Ui.VStack(10, head, Ui.Card(Ui.VStack(6,
                Ui.Caption("The registry returned a single manifest, so this reference publishes one platform."),
                PageKit.Fact("Config digest", report.ConfigDigest, true),
                PageKit.Fact("Layers", report.LayerCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                PageKit.Fact("Compressed size", Ui.FormatBytes(report.LayerBytes))), 14, "SurfaceContainer", 12));
        }

        var search = new SearchFilter("Search platforms and digests", 260) { Margin = new Thickness(0, 4, 0, 4) };
        var list = new StackPanel();
        var status = Ui.Caption("");
        var platformByDigest = report.Platforms.ToDictionary(p => p.Digest, p => p.Platform, StringComparer.Ordinal);
        void Apply()
        {
            list.Children.Clear();
            var shown = 0;
            foreach (var p in report.Platforms.Where(p => search.Matches(Searchable(p))))
            {
                if (shown == 0) list.Children.Add(Ui.Overline("Platforms").Margin(0, 4, 0, 6));
                list.Children.Add(PlatformCard(p, MatchesLocal(p.Platform, Ctx.Platform)));
                shown++;
            }
            var attestations = report.Attestations.Where(a => search.Matches(Searchable(a))).ToList();
            if (attestations.Count > 0) list.Children.Add(Ui.Overline("Attestations (provenance and SBOM)").Margin(0, 10, 0, 6));
            foreach (var a in attestations)
            {
                list.Children.Add(AttestationCard(a, platformByDigest.TryGetValue(a.AttestsDigest, out var forPlatform) ? forPlatform : ""));
                shown++;
            }
            if (shown == 0) list.Children.Add(Ui.Caption(search.Status.Length > 0 ? search.Status : "No manifest entries match the search."));
            status.Text = search.Status.Length > 0 ? search.Status : $"{shown} of {report.Platforms.Count + report.Attestations.Count} entries shown";
        }
        search.Changed += Apply;
        Apply();
        return Ui.VStack(10, head, Ui.Wrap(8, search, status), list);
    }

    private static bool MatchesLocal(string remote, string local) =>
        local.Length > 1 && (remote == local || remote.StartsWith(local + "/", StringComparison.Ordinal) || remote.StartsWith(local + " (", StringComparison.Ordinal));

    private static string Searchable(ImageManifestEntry e) => e.Platform + " " + e.Digest + " " + e.MediaType + " " + string.Join(" ", e.Annotations.Select(a => a.Key + "=" + a.Value));

    private static UIElement PlatformCard(ImageManifestEntry p, bool matchesLocal)
    {
        var title = Ui.Mono(p.Platform, 14.5, "OnSurface", true);
        title.FontWeight = FontWeights.SemiBold;
        var head = Ui.Columns(8, [0, 1, 0], Ui.Icon(Icons.DeveloperBoard, 18, true, "Primary"), title,
            matchesLocal ? Ui.Badge("Matches local", Tone.Success, true) : new Border());
        return Card(head, p, null);
    }

    private static UIElement AttestationCard(ImageManifestEntry a, string forPlatform)
    {
        var head = Ui.Columns(8, [0, 1], Ui.Icon(Icons.VerifiedUser, 18, true, "Tertiary"),
            Ui.Text(forPlatform.Length > 0 ? "Attestation for " + forPlatform : "Attestation manifest", 13.5, FontWeights.SemiBold, "OnSurface", true, false));
        var target = a.AttestsDigest.Length > 0 ? PageKit.Fact("Describes", a.AttestsDigest, true) : null;
        return Card(head, a, target);
    }

    private static UIElement Card(UIElement head, ImageManifestEntry e, UIElement? extra)
    {
        var digest = Ui.Columns(6, [1, 0], PageKit.Fact("Digest", e.Digest, true), Ui.IconButton(Icons.ContentCopy, "Copy digest", () => PageKit.Copy(e.Digest, "the digest")));
        var stack = Ui.VStack(6, head, digest, extra, PageKit.Fact("Manifest size", e.Size > 0 ? Ui.FormatBytes(e.Size) : ""), PageKit.Fact("Media type", e.MediaType, true));
        var annotations = e.Annotations.Where(a => a.Key != ImageManifestEntry.ReferenceDigestKey).ToList();
        if (annotations.Count > 0)
        {
            var wrap = new WrapPanel();
            foreach (var a in annotations)
            {
                var badge = Ui.Badge(a.Key + "=" + a.Value, Tone.Neutral);
                badge.Margin = new Thickness(0, 0, 6, 6);
                badge.ToolTip = a.Key + "=" + a.Value;
                if (badge.Child is TextBlock t) { t.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont"); t.TextTrimming = TextTrimming.CharacterEllipsis; t.MaxWidth = 420; }
                wrap.Children.Add(badge);
            }
            stack.Children.Add(Ui.VStack(4, Ui.Overline("Annotations"), wrap));
        }
        return Ui.Card(stack, 12, "SurfaceContainer", 12).Margin(0, 0, 0, 8);
    }
}
