using MaterialDockerImage.Pages;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

/// <summary>Checks for the command palette live second lines and the Canvas build-result footers.</summary>
public static class LiveLinesChecks
{
    static int count;

    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        count++;
        Console.WriteLine("PASS " + name);
    }

    public static int Run()
    {
        count = 0;
        var language = Loc.Language;
        try
        {
            Loc.Language = "English";
            PaletteLines();
            Footers();
            Loc.Language = "Cantonese";
            var untranslated = PaletteLiveLines.Templates.Concat(CanvasBuildFooters.Templates).Where(t => Loc.T(t) == t).ToList();
            Check(untranslated.Count == 0, "every live line and build footer template has Cantonese" + (untranslated.Count > 0 ? ": " + string.Join(", ", untranslated) : ""));
            Check(PaletteLiveLines.Containers(12, 25) == "12 運行中 · 共 25 個", "the containers live line reads in Cantonese with the counts kept");
            Check(Loc.T("Restart the selected container") != "Restart the selected container", "the new palette command title has Cantonese");
        }
        finally { Loc.Language = language; }
        return count;
    }

    static void PaletteLines()
    {
        Check(PaletteLiveLines.Containers(12, 25) == "12 running · 25 total", "containers live line shows running and total");
        Check(PaletteLiveLines.Images(105, "86.6 GB") == "105 images · 86.6 GB" && PaletteLiveLines.Images(1, "5 MB") == "1 image · 5 MB", "images live line counts and sizes");
        Check(PaletteLiveLines.Compose(3, 1, 0) == "3 projects · 1 running" && PaletteLiveLines.Compose(1, 0, 2) == "1 project · 0 running · 2 recent files", "compose live line omits recent files when there are none");
        Check(PaletteLiveLines.Contexts("desktop-linux", true) == "Current context: desktop-linux" && PaletteLiveLines.Contexts(null, false) == "Context list unavailable", "contexts live line names the current context or says the list failed");
        Check(PaletteLiveLines.Monitor(true, 2, 4, null) == "Sampling every 2 s · 4 containers sampled", "monitor live line while sampling");
        Check(PaletteLiveLines.Monitor(false, 2, 1, null) == "Sampling paused · 1 container sampled", "monitor live line while paused");
        Check(PaletteLiveLines.Security(5, 105) == "5 of 105 images analyzed", "security live line counts analyzed images");
        Check(PaletteLiveLines.Swarm(false, false, 0) == "Swarm inactive" && PaletteLiveLines.Swarm(true, true, 3) == "Swarm active · 3 nodes"
              && PaletteLiveLines.Swarm(true, false, 0) == "Swarm active · Worker node" && PaletteLiveLines.Swarm(null, false, 0) == "Swarm state unavailable", "swarm live line covers inactive, manager, worker and unreadable");
        Check(PaletteLiveLines.StoppedContainers(0) == "Nothing stopped to remove" && PaletteLiveLines.StoppedContainers(3) == "3 stopped containers", "prune live line counts stopped containers");
        Check(PaletteLiveLines.SelectedContainer("hello-api", "Up 4 minutes") == "Selected container · hello-api · Up 4 minutes", "selected container line keeps the engine status literal");
        Check(PaletteLiveLines.Choose(null, "Reload the list", "Containers") == "Reload the list" && PaletteLiveLines.Choose("  ", null, "Containers") == "Containers"
              && PaletteLiveLines.Choose("12 running · 25 total", "Reload the list", "Containers") == "12 running · 25 total", "the live line replaces the description only when it has text");
        var search = PaletteLiveLines.SearchText("Go to Containers Container lifecycle, logs", "12 running · 25 total");
        Check(search.Contains("lifecycle") && search.Contains("12 running"), "search text keeps the static description and adds the live line");
        Check(PaletteLiveLines.SearchText("static", null) == "static", "search text without a live line is unchanged");
        Check(CommandDescriptions.For("containers.restart-selected") != null, "the new restart command has a description");
    }

    static void Footers()
    {
        const string source = "FROM node:22-alpine AS deps\nRUN npm ci\nFROM node:22-alpine AS build\nCOPY . .\nRUN npm run build\nFROM node:22-alpine AS runtime\nCOPY --from=deps /a /a\nCOPY --from=build /b /b\n";
        var stages = new List<(int, string)> { (0, "deps"), (1, "build"), (2, "runtime") };
        var finished = new DateTimeOffset(2026, 11, 5, 14, 2, 30, TimeSpan.Zero);
        var at = finished.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        var steps = new List<BuildStepFact>
        {
            new("deps", 1, 2, "cached", 0), new("deps", 2, 2, "cached", 0),
            new("build", 1, 3, "cached", 0), new("build", 2, 3, "done", 1.5), new("build", 3, 3, "done", 8.1),
            new("runtime", 1, 3, "cached", 0), new("runtime", 2, 3, "done", 0.2), new("runtime", 3, 3, "done", 0.3),
        };
        var run = new CanvasBuildRun(CanvasBuildFooters.Hash(source), "", finished, true, false, steps, "hello:latest", "");
        var map = CanvasBuildFooters.Map(stages, run, source);
        Check(map.Count == 3, "every built stage gets a footer");
        Check(map[0].Text == "cached · from the build at " + at && map[0].Outcome == "cached", "a fully cached stage says cached and names the build time");
        Check(map[1].Text == "9.6 s · 1 of 3 steps cached · from the build at " + at, "a rebuilt stage shows its run time and cache use");
        Check(map[1].Detail.Contains("has not changed"), "the footer explains that it belongs to that build of unchanged text");
        Check(!map[2].Text.Contains("built image"), "no image size is invented before docker image inspect reported one");
        var sized = CanvasBuildFooters.Map(stages, run with { ImageBytes = 182_400_000 }, source);
        Check(sized[2].Text.StartsWith("182.4 MB built image · ", StringComparison.Ordinal) && !sized[0].Text.Contains("MB"), "only the target stage shows the reported image size");
        Check(CanvasBuildFooters.Map(stages, run, source + "RUN echo changed\n").Count == 0, "an edit since the build hides every footer");
        Check(CanvasBuildFooters.Map(stages, run, source.Replace("\n", "\r\n")).Count == 3, "a line-ending change alone does not hide the footers");
        Check(CanvasBuildFooters.Map(stages, null, source).Count == 0, "no build in this session means no footers");
        var targeted = CanvasBuildFooters.Map(stages, run with { Target = "deps", Steps = steps.Take(2).ToList(), ImageBytes = 48_000_000 }, source);
        Check(targeted.Count == 1 && targeted[0].Text.StartsWith("48 MB built image · cached", StringComparison.Ordinal), "a build of an earlier target shows only that stage, with the size");
        var failed = CanvasBuildFooters.Map(stages, run with { Success = false, Steps = [new("deps", 1, 2, "cached", 0), new("deps", 2, 2, "error", 3.2)] }, source);
        Check(failed.Count == 1 && failed[0].Outcome == "failed" && failed[0].Text.StartsWith("failed at step 2/2", StringComparison.Ordinal), "a failed step marks its stage as failed with the step position");
        var canceled = CanvasBuildFooters.Map(stages, run with { Success = false, Cancelled = true, Steps = [new("build", 2, 3, "canceled", 1)] }, source);
        Check(canceled.Count == 1 && canceled[1].Outcome == "canceled", "a canceled step marks its stage as canceled");
        Check(CanvasBuildFooters.Matches("stage-1", 1, "1", 3) && CanvasBuildFooters.Matches("DEPS", 0, "deps", 3) && !CanvasBuildFooters.Matches("deps", 1, "build", 3), "steps match stages by name or by the stage-N label");
        Check(CanvasBuildFooters.Matches(null, 0, "0", 1) && !CanvasBuildFooters.Matches(null, 0, "base", 2), "an unlabelled step belongs to the stage only in a one-stage Dockerfile");
        var single = "FROM alpine\nRUN echo hi\n";
        var singleRun = new CanvasBuildRun(CanvasBuildFooters.Hash(single), "", finished, true, false, [new(null, 1, 2, "cached", 0), new(null, 2, 2, "done", 0.4)], "", "");
        Check(CanvasBuildFooters.Map([(0, "0")], singleRun, single)[0].Text == "0.4 s · 1 of 2 steps cached · from the build at " + at, "a one-stage build maps its unlabelled steps");
        Check(CanvasBuildFooters.FormatBytes(950) == "950 B" && CanvasBuildFooters.FormatBytes(86_600_000_000) == "86.6 GB", "sizes use the application's decimal units");
    }
}
