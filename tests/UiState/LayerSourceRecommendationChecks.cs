using System.Text.Json;
using MaterialDockerImage.UI;
using F = LayerSourceRecommendationFixtures;

/// <summary>Checks for the docker scout recommendations parser and the Dockerfile layer source matcher, against real captured output.</summary>
public static class LayerSourceRecommendationChecks
{
    static int count;

    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        count++;
        Console.WriteLine("PASS " + name);
    }

    static List<string> History(string jsonl) =>
        jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonDocument.Parse(l).RootElement.GetProperty("CreatedBy").GetString() ?? "").ToList();

    public static int Run()
    {
        count = 0;
        Recommendations();
        LayerSources();
        return count;
    }

    static void Recommendations()
    {
        var httpd = ScoutRecommendationParser.Parse(F.HttpdOut, F.HttpdErr);
        Check(httpd.Target == "httpd:2.4" && httpd.TargetDigest == "c6b7745aa371", "recommendations: target and digest are read from the header");
        Check(httpd.BaseImage?.Reference == "debian:13-slim", "recommendations: detected base image is read");
        Check(httpd.BaseImage!.Counts == new ScoutVulnerabilityCounts(0, 3, 0, 9, 0), "recommendations: base image counts 0C 3H 0M 9L");
        Check(httpd.BaseImage.Fact("Size") == "30 MB" && httpd.BaseImage.Fact("OS") == "13.7" && httpd.BaseImage.Fact("Digest").StartsWith("sha256:1e1a2217", StringComparison.Ordinal), "recommendations: base image facts keep their printed values");
        Check(httpd.BaseAutoDetected, "recommendations: the auto-detected base image note is recognised on standard error");
        Check(httpd.RefreshShown && !httpd.RefreshUpToDate && httpd.UpdateShown && !httpd.NoUpdates, "recommendations: both sections are present with tables");
        var refresh = httpd.Refresh.ToList();
        Check(refresh.Count == 1 && refresh[0].Tag == "13-slim" && refresh[0].Reasons.SequenceEqual(["Newer image for same tag"]), "recommendations: one refresh row with its reason");
        Check(refresh[0].AlsoKnownAs.SequenceEqual(["13.7-slim", "trixie-slim", "trixie-20261104-slim"]), "recommendations: aliases are read from the Also known as list");
        Check(refresh[0].Counts == new ScoutVulnerabilityCounts(0, 2, 1, 26, 0), "recommendations: refresh counts 0C 2H 1M 26L");
        Check(refresh[0].Delta == new ScoutVulnerabilityDelta(null, -1, 1, 17, null), "recommendations: change line aligns -1 High, +1 Medium, +17 Low and leaves Critical unchanged");
        Check(refresh[0].Pushed == "14 hours ago" && refresh[0].Detail("Size") == "30 MB" && refresh[0].Benefits.Contains("Minor OS version update"), "recommendations: pushed, size detail and benefits");
        var updates = httpd.Updates.ToList();
        Check(updates.Select(u => u.Tag).SequenceEqual(["stable-slim", "13"]), "recommendations: change rows keep Docker Scout's order");
        Check(updates[1].Detail("Size") == "49 MB" && updates[1].AlsoKnownAs.Contains("latest") && updates[1].Reasons.SequenceEqual(["Tag is latest"]), "recommendations: second change row details");
        Check(updates[0].Benefits.Contains("stable-slim was pulled 46K times last month") && updates[0].Detail("Slim") == "v", "recommendations: benefit text and details are kept exactly");

        var minecraft = ScoutRecommendationParser.Parse(F.MinecraftOut);
        Check(minecraft.BaseImage?.Reference == "eclipse-temurin:21-jre" && minecraft.RefreshUpToDate && !minecraft.Refresh.Any(), "recommendations: an up-to-date refresh section has no rows");
        Check(minecraft.Updates.Select(u => u.Tag).SequenceEqual(["25-jre", "27-jre"]) && minecraft.Updates.All(u => u.Delta == null), "recommendations: rows without a change line have no delta");
        Check(minecraft.Updates.First().Reasons.SequenceEqual(["Major runtime version update"]) && minecraft.Updates.First().Detail("Runtime") == "25.0.5_8", "recommendations: runtime detail is read");

        var nginx = ScoutRecommendationParser.Parse(F.NginxOut);
        Check(nginx.BaseImage?.Counts == new ScoutVulnerabilityCounts(0, 0, 0, 0, 1) && nginx.RefreshUpToDate && nginx.NoUpdates && nginx.Recommendations.Count == 0, "recommendations: unrated count and no recommendations");
        Check(!nginx.BaseAutoDetected, "recommendations: no auto-detected note without standard error");

        var debian = ScoutRecommendationParser.Parse(F.DebianOut);
        Check(debian.NoBaseImage && debian.BaseImage == null && debian.Problem.Contains("image has no base image", StringComparison.Ordinal), "recommendations: an image without a base image is reported as such");

        var failed = ScoutRecommendationParser.Parse("", F.PullFailedErr);
        Check(failed.BaseImage == null && !failed.NoBaseImage && failed.Recommendations.Count == 0 && failed.Target == "", "recommendations: a failed pull yields nothing");

        Check(ScoutRecommendationParser.Delta("    0C     2H     1M    26L  ", "          -1     +1    +17  ") == null, "recommendations: a misaligned change line is refused rather than guessed");
        Check(ScoutRecommendationParser.Counts("12 packages") == null, "recommendations: text that is not a count line is not counted");
        Check(ScoutRecommendationParser.SizeBytes("5.8 MB") == 5_800_000 && ScoutRecommendationParser.SizeBytes("unknown") == null, "recommendations: printed sizes are parsed in decimal units");
        var colored = ScoutRecommendationParser.Parse("\u001b[1m Target\u001b[0m   │  httpd:2.4\n");
        Check(colored.Target == "httpd:2.4", "recommendations: ANSI styling is removed before parsing");
    }

    static void LayerSources()
    {
        var history = History(F.ProbeHistory);
        Check(history.Count == 20, "layer source: the real probe history has 20 rows");
        var notes = DockerfileLayerSource.Match(F.ProbeDockerfile, history);
        int Line(int row) => notes.TryGetValue(row, out var n) ? n.Line : -1;
        Check(Line(0) == 29 && Line(1) == 28 && Line(2) == 27 && Line(3) == 26, "layer source: CMD shell form, ENTRYPOINT, STOPSIGNAL and VOLUME map to their lines");
        Check(!notes.ContainsKey(4), "layer source: HEALTHCHECK is accounted for but gets no note, because its recorded form is not reproduced exactly");
        Check(Line(5) == 24 && Line(6) == 23 && Line(7) == 22, "layer source: USER, EXPOSE and LABEL map to their lines");
        Check(Line(8) == 21 && Line(9) == 20 && Line(10) == 18, "layer source: RUN with a cache mount, exec-form RUN and a continued RUN map to their first lines");
        Check(Line(11) == 17 && Line(12) == 16 && Line(13) == 15 && Line(14) == 14, "layer source: ADD and COPY map to their lines");
        Check(notes[14].FromStage == "deps" && notes[13].FromStage == "build" && notes[12].FromStage == "", "layer source: COPY --from names the source stage");
        Check(Line(15) == 13 && Line(16) == 11 && Line(17) == 10, "layer source: WORKDIR, ENV with an expanded build argument and ARG map to their lines");
        Check(notes[18].IsBase && notes[19].IsBase && notes[19].BaseImage == "alpine:3.20", "layer source: rows below the final FROM are labelled with the base image");

        var edited = F.ProbeDockerfile.Replace("USER 1000", "USER 1001", StringComparison.Ordinal);
        Check(DockerfileLayerSource.Match(edited, history).Count == 0, "layer source: one changed instruction removes every note instead of guessing");
        var extra = F.ProbeDockerfile.Replace("EXPOSE 8080", "EXPOSE 8080\nENV EXTRA=1", StringComparison.Ordinal);
        Check(DockerfileLayerSource.Match(extra, history).Count == 0, "layer source: an instruction missing from the history removes every note");
        Check(DockerfileLayerSource.Match("FROM alpine:3.20\nCMD [\"/bin/sh\"]\n", history).Count == 0, "layer source: an unrelated Dockerfile that only matches the base image is refused");
        Check(DockerfileLayerSource.Match(F.ProbeDockerfile, history.Take(5).ToList()).Count == 0, "layer source: a history that ends before every instruction is accounted for gives no notes");
        var twins = "FROM alpine:3.20 AS a\nRUN echo hi\nFROM alpine:3.20 AS b\nRUN echo hi\n";
        Check(DockerfileLayerSource.Match(twins, ["RUN /bin/sh -c echo hi # buildkit", "CMD [\"/bin/sh\"]"]).Count == 0, "layer source: two stages that fit with different lines are ambiguous and give no notes");
        Check(DockerfileLayerSource.Match("", history).Count == 0 &&DockerfileLayerSource.Match(F.ProbeDockerfile, []).Count == 0, "layer source: empty inputs give no notes");

        var stages = DockerfileLayerSource.ParseStages(F.ProbeDockerfile, out var globals);
        Check(stages.Count == 3 && stages[0].Name == "deps" && stages[1].Base == "deps" && stages[2].Base == "alpine:3.20" && globals.Count == 0, "layer source: stages and bases are parsed");
        Check(stages[2].Instructions.Single(i => i.Line == 18).Arguments.Contains("echo done", StringComparison.Ordinal), "layer source: continuation lines are joined into the instruction on its first line");

        Check(DockerfileLayerSource.NormaliseRecorded("RUN |1 VERSION=1.2.3 /bin/sh -c echo hi # buildkit") == "RUN /bin/sh -c echo hi", "layer source: BuildKit build-argument prefix and suffix are removed");
        Check(DockerfileLayerSource.NormaliseRecorded("/bin/sh -c #(nop)  CMD [\"sh\"]") == "CMD [\"sh\"]" && DockerfileLayerSource.NormaliseRecorded("/bin/sh -c apk add curl") == "RUN /bin/sh -c apk add curl", "layer source: legacy builder records are unwrapped");

        // A target stage: an image built with --target deps matches the first stage only.
        var depsHistory = new List<string> { "RUN /bin/sh -c echo deps > deps.txt # buildkit", "WORKDIR /app", "CMD [\"/bin/sh\"]", "ADD alpine-minirootfs-3.20.10-x86_64.tar.gz / # buildkit" };
        var depsNotes = DockerfileLayerSource.Match(F.ProbeDockerfile, depsHistory);
        Check(depsNotes.Count == 4 && depsNotes[0].Line == 4 && depsNotes[1].Line == 3 && depsNotes[2].IsBase, "layer source: an earlier target stage is matched when the final stage does not fit");

        // Global ARG in FROM: the base reference is expanded from its default.
        var argBase = "ARG BASE=alpine:3.20\nFROM ${BASE}\nRUN echo hi\n";
        var argNotes = DockerfileLayerSource.Match(argBase, ["RUN /bin/sh -c echo hi # buildkit", "CMD [\"/bin/sh\"]"]);
        Check(argNotes[0].Line == 3 && argNotes[1].BaseImage == "alpine:3.20", "layer source: a FROM that uses a global ARG default is expanded");
        var unknownBase = DockerfileLayerSource.Match("FROM ${BASE}\nRUN echo hi\n", ["RUN /bin/sh -c echo hi # buildkit", "CMD [\"/bin/sh\"]"]);
        Check(unknownBase[0].Line == 2 && !unknownBase.ContainsKey(1), "layer source: an unresolved base reference gets no base note");
    }
}
