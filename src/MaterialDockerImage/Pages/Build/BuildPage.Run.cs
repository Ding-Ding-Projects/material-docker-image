using System.Windows.Controls;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Engine actions of the Build page: check, build (with the timeline and result views) and build and run.</summary>
public sealed partial class BuildPage
{
    private void SetRunning(bool running)
    {
        foreach (var button in new[] { check, buildRun, build }) button.IsEnabled = !running;
        if (!running) UpdateBuildLabel();
    }

    private void UpdateBuildLabel()
    {
        var label = options.Call switch { BuildKitCall.Check => "Run check", BuildKitCall.Outline => "Show outline", BuildKitCall.Targets => "List targets", _ => "Build" };
        if (build.Content is StackPanel { Children.Count: > 0 } content && content.Children[^1] is TextBlock text) text.Text = Loc.T(label);
        System.Windows.Automation.AutomationProperties.SetName(build, Loc.T(label));
        buildRun.IsEnabled = options.Call == BuildKitCall.Build && !BuildRunner.AnyActive;
    }

    private async Task CheckAsync()
    {
        if (!Directory.Exists(options.ContextFolder.Text)) throw new ArgumentException("Choose an existing build context folder first.");
        if (!await EnsureSavedAsync("The Dockerfile check")) return;
        if (BuildRunner.AnyActive) { Host.Notify.Warning("A build is running. Wait for its result or cancel it first."); return; }
        var args = new List<string> { "buildx", "build", "--check", "--file", dockerfilePath };
        var target = (options.Target.Text ?? "").Trim();
        if (target.Length > 0) { DockerService.ValidateOperand(target, "target"); args.AddRange(["--target", target]); }
        args.Add(Path.GetFullPath(options.ContextFolder.Text));
        await Host.Execute("BuildKit Dockerfile check", args, boundContext: Host.Context);
    }

    /// <summary>Runs the selected call (build, check, outline or targets). Returns the result, or null when nothing ran.</summary>
    private async Task<CommandResult?> BuildAsync()
    {
        if (!Directory.Exists(options.ContextFolder.Text)) throw new ArgumentException("Choose an existing build context folder.");
        if (!await EnsureSavedAsync("The build")) return null;
        var call = options.Call;
        var metadataFile = call == BuildKitCall.Build ? Path.Combine(Path.GetTempPath(), "material-docker-image-build-" + Guid.NewGuid().ToString("N") + ".json") : null;
        var args = await options.CreateArgumentsAsync(dockerfilePath, metadataFile);
        if (args == null) return null;
        Host.Settings.BuildFolder = Path.GetFullPath(options.ContextFolder.Text);
        options.ContextFolder.Text = Host.Settings.BuildFolder;
        Host.Settings.ImageTag = string.Join(", ", options.Tags.Tags);
        Host.SaveSettings(null);
        UpdateSubtitle();
        var summary = options.Summary();
        var context = Host.Context;
        tabs.SelectedItem = runTab;
        var label = call switch { BuildKitCall.Check => "BuildKit check", BuildKitCall.Outline => "BuildKit outline", BuildKitCall.Targets => "BuildKit targets", _ => "Build image" };
        try
        {
            var result = await runner.RunAsync(label, args, context, null, string.Join(", ", summary.Tags));
            if (result?.Success != true) return result;
            timeline.SetResult(call switch
            {
                BuildKitCall.Build => BuildResultCard.Create(ReadMetadata(metadataFile), summary, timeline.Progress.ResultDigest, context),
                BuildKitCall.Targets => BuildKitCallResult.ParseTargets(result.StandardOutput) is { } targets
                    ? BuildCallResultView.Targets(targets, name => { options.Target.Text = name; Host.Notify.Info(Loc.T("Build target set to") + " " + name); })
                    : BuildCallResultView.Unparsed("targets"),
                BuildKitCall.Outline => BuildKitCallResult.ParseOutline(result.StandardOutput) is { } outline ? BuildCallResultView.Outline(outline) : BuildCallResultView.Unparsed("outline"),
                _ => null
            });
            return result;
        }
        finally
        {
            if (metadataFile != null) try { File.Delete(metadataFile); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Reads the metadata file buildx wrote. It holds digests and references only, never secrets.</summary>
    private static BuildKitMetadata? ReadMetadata(string? path)
    {
        if (path == null || !File.Exists(path)) return null;
        try
        {
            var info = new FileInfo(path);
            return info.Length > 4 * 1024 * 1024 ? null : BuildKitMetadata.Parse(File.ReadAllText(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    private async Task BuildAndRunAsync()
    {
        if (options.Call != BuildKitCall.Build) throw new InvalidOperationException("Build and run needs the Build mode. Switch Mode back to Build.");
        if (options.Destination != BuildKitDestination.Load) throw new InvalidOperationException("Build and run needs the Load locally destination so the image exists in this engine.");
        var tag = options.Tags.Tags.FirstOrDefault();
        if (tag == null) throw new ArgumentException("Add a tag so the built image can be run.");
        var result = await BuildAsync();
        if (result?.Success != true) return;
        Host.HandOff["Images.RunImage"] = tag;
        Host.Navigate("Images");
    }
}
