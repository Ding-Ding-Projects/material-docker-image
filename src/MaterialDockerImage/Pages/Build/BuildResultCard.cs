using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Result of a successful build, read from the buildx metadata file: image digest, config digest, names, build
/// reference and attestation facts, with Run, Push and Copy digest actions.
/// </summary>
public static class BuildResultCard
{
    private static AppHost Host => AppHost.Current;

    /// <param name="metadata">Parsed metadata, or null when the file was missing or unreadable.</param>
    /// <param name="requested">What the request asked for (destination, tags, attestations).</param>
    /// <param name="progressDigest">Digest seen in the progress stream, used when metadata has none.</param>
    public static FrameworkElement Create(BuildKitMetadata? metadata, BuildKitRequestSummary requested, string? progressDigest, string? boundContext)
    {
        var digest = metadata?.ImageDigest ?? progressDigest;
        var tags = metadata?.ImageNames.Count > 0 ? metadata.ImageNames : requested.Tags;
        var rows = new StackPanel();
        void Row(string label, string? value, bool mono = true)
        {
            var text = value is { Length: > 0 } ? (mono ? Ui.Mono(value, 13, "OnSurface", true) : Ui.Text(value, 13, null, "OnSurface", true, false)) : Ui.Text(Loc.T("Not reported"), 13, null, "OnSurfaceVariant");
            Ui.Add(rows, Ui.Columns(12, [-150, 1], Ui.Caption(label), text), 6);
        }
        Row("Image digest", digest);
        Row("Config digest", metadata?.ConfigDigest);
        Row("Names", string.Join(", ", tags));
        Row("Build reference", metadata?.BuildRef);
        // Attestations are chips (green when recorded or requested), as in the design.
        var chips = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        Border Chip(string text, string icon)
        {
            var label = Ui.HStack(6, Ui.Icon(icon, 14, true, "OnSuccessContainer"), Ui.Text(text, 12, FontWeights.SemiBold, "OnSuccessContainer", false, false));
            var chip = new Border { Child = label, Height = 28, Padding = new Thickness(10, 0, 10, 0), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 8, 4) };
            chip.SetResourceReference(Border.BackgroundProperty, "SuccessContainer");
            return chip;
        }
        if (requested.Sbom) chips.Children.Add(Chip(Loc.T("SBOM requested"), Icons.Inventory2));
        if (requested.Provenance != BuildKitProvenance.Default) chips.Children.Add(Chip(Loc.T("Provenance") + " " + Loc.T(requested.Provenance.ToString()), Icons.Fingerprint));
        if (metadata?.HasProvenance == true) chips.Children.Add(Chip(Loc.T("provenance recorded in metadata"), Icons.GppGood));
        if (chips.Children.Count == 0) chips.Children.Add(Ui.Text(Loc.T("None requested; the builder default applies"), 13, null, "OnSurfaceVariant", false, false));
        var attestationRow = Ui.Columns(12, [-150, 1], Ui.Caption("Attestations"), chips);

        // Actions sit on the right: Copy digest (outlined), Push (tonal), Run (filled, the main action).
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        if (digest != null)
            actions.Children.Add(Ui.Button("Copy digest", Icons.ContentCopy, ButtonKind.Outlined, () => { ActionSounds.Copy(digest); Host.Notify.Info("Copied the image digest."); }).Margin(0, 0, 8, 0));
        if (requested.Destination == BuildKitDestination.Load && requested.Tags.Count > 0)
            actions.Children.Add(Ui.AsyncButton("Push", Icons.CloudUpload, ButtonKind.Tonal, () => PushAsync(requested.Tags, boundContext)).Margin(0, 0, 8, 0));
        if (requested.Destination == BuildKitDestination.Load && tags.Count > 0)
            actions.Children.Add(Ui.Button("Run", Icons.PlayArrow, ButtonKind.Filled, () => { Host.HandOff["Images.RunImage"] = tags[0]; Host.Navigate("Images"); }));

        var meta = Ui.Text(string.Join(" · ", tags.Take(1)), 12, null, "OnSurfaceVariant", false, false);
        var title = Ui.Columns(8, [0, 1, 0], Ui.Icon(Icons.CheckCircle, 20, true, "Success"), Ui.Text("Build succeeded", 15, FontWeights.SemiBold, "OnSurface", false), meta);
        var note = metadata == null ? Ui.Caption("The metadata file was not written or could not be read, so only what the progress output reported is shown.") : null;
        return Ui.Card(Ui.VStack(10, title, note, rows, attestationRow, actions), 16, "SurfaceContainer", 16);
    }

    private static async Task PushAsync(IReadOnlyList<string> tags, string? context)
    {
        if (!await Host.Overlay.ConfirmAsync("Push these tags to their registries?", string.Join(Environment.NewLine, tags) + Environment.NewLine + Environment.NewLine + Loc.T("Each push uses the existing Docker sign-in for its registry."), "Push")) return;
        if (BuildRunner.AnyActive) { Host.Notify.Warning("A build is still running. Wait for its result before pushing."); return; }
        foreach (var tag in tags)
        {
            var result = await Host.Execute("Push " + tag, ["image", "push", tag], boundContext: context);
            if (result?.Success != true) return;
        }
    }
}

/// <summary>The parts of a request the result card needs after the run.</summary>
public sealed record BuildKitRequestSummary(BuildKitDestination Destination, IReadOnlyList<string> Tags, bool Sbom, BuildKitProvenance Provenance);
