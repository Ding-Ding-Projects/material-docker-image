using MaterialDockerImage.UI;

/// <summary>
/// Checks for named Creator steps (the "# step: name" comment kept in the Dockerfile) and for the fixed value sets
/// behind the "Values:" line of the suggestion documentation and the value pickers.
/// </summary>
public static class CreatorStepChecks
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
        StepNames();
        StepPatches();
        ValueSets();
        HintsTranslate();
        return count;
    }

    /// <summary>Every step hint the starters produce, and every starter name, reads in Cantonese (hints are composed as
    /// "A · B" and translated phrase by phrase).</summary>
    static void HintsTranslate()
    {
        var before = MaterialDockerImage.Services.Loc.Language;
        MaterialDockerImage.Services.Loc.Language = "Cantonese";
        try
        {
            var english = new List<string>();
            foreach (var starter in DockerfileStarters.All)
            {
                var document = starter.Create();
                if (MaterialDockerImage.Services.Loc.T(document.Origin) == document.Origin && document.Origin.Length > 0) english.Add(document.Origin);
                foreach (var instruction in document.Stages.SelectMany(s => s.Instructions))
                    foreach (var phrase in DockerfileModel.Hint(instruction).Split(" · ", StringSplitOptions.RemoveEmptyEntries))
                        if (MaterialDockerImage.Services.Loc.Line(phrase) == phrase && System.Text.RegularExpressions.Regex.IsMatch(phrase, "[a-z]{3}")) english.Add(phrase);
            }
            foreach (var chip in new[] { "RUN with cache mount", "RUN heredoc script", "Non-root USER" })
                if (MaterialDockerImage.Services.Loc.T(chip) == chip) english.Add(chip);
            Check(english.Count == 0, "every starter step hint and starter name has Cantonese" + (english.Count > 0 ? ": " + string.Join(" | ", english.Distinct()) : ""));
        }
        finally { MaterialDockerImage.Services.Loc.Language = before; }
    }

    static DockerInstruction FirstRun(DockerfileDocument document) => document.Stages.SelectMany(s => s.Instructions).First(i => i.Kind == InstructionKind.Run);

    static void StepNames()
    {
        const string plain = "# syntax=docker/dockerfile:1\n\nFROM alpine AS base\n# Keep the image small\nRUN apk add --no-cache curl\n";
        var plainParsed = DockerfileModel.Parse(plain).Document;
        Check(DockerfileModel.Render(plainParsed) == plain, "a file without step comments renders back unchanged");
        Check(plainParsed.Stages[0].Instructions.All(i => i.StepName.Length == 0) && plainParsed.Stages[0].Instructions[0].Kind == InstructionKind.Comment, "ordinary comments stay comments and name nothing");

        const string named = "# syntax=docker/dockerfile:1\n\nFROM node:22-alpine AS deps\n# Dependencies are cached.\n# step: Install dependencies\nRUN apk add --no-cache \\\n    curl \\\n    git\nCOPY . .\n";
        var parsed = DockerfileModel.Parse(named);
        var stage = parsed.Document.Stages[0];
        Check(stage.Instructions.Count == 3 && stage.Instructions[0].Kind == InstructionKind.Comment && stage.Instructions[0].Value == "Dependencies are cached.", "the comment above a step comment stays a separate comment");
        Check(stage.Instructions[1].Kind == InstructionKind.Run && stage.Instructions[1].StepName == "Install dependencies", "the step comment names the continued RUN directly below it");
        Check(stage.Instructions[2].StepName.Length == 0, "the next step has no name");
        Check(DockerfileModel.Render(parsed.Document) == named, "a file with a step comment above a continued RUN round trips exactly");
        var runSpan = parsed.LineMap.First(s => s.StageIndex == 0 && s.InstructionIndex == 1);
        Check(runSpan.StartLine == 4 && runSpan.EndLine == 7, "the named step's lines include its step comment");
        Check(DockerfileModel.Summary(stage.Instructions[1]).StartsWith("apk add", StringComparison.Ordinal), "the card summary shows the instruction, not the step comment");
        Check(DockerfileStepNames.Title(stage.Instructions[1]) == "Install dependencies" && DockerfileStepNames.Title(stage.Instructions[2]) == "Copy files", "the open-step title is the name when there is one, otherwise the kind title");
        var rows = DockerfileOutline.Rows(stage);
        Check(rows[2].Name == "Install dependencies" && rows[2].Keyword == "RUN" && rows[3].Name == "", "the outline row carries the step name");

        const string blankBetween = "FROM alpine\n# step: Not attached\n\nRUN true\n";
        var loose = DockerfileModel.Parse(blankBetween).Document.Stages[0];
        Check(loose.Instructions[0].Kind == InstructionKind.Comment && loose.Instructions[1].StepName.Length == 0, "a step comment followed by a blank line stays a comment");
        var beforeFrom = DockerfileModel.Parse("# step: Base\nFROM alpine\nRUN true\n").Document;
        Check(beforeFrom.Preamble.Count == 1 && beforeFrom.Preamble[0].Kind == InstructionKind.Comment && beforeFrom.Stages[0].Instructions[0].StepName.Length == 0, "a step comment above FROM stays a comment");

        var instruction = new DockerInstruction { Kind = InstructionKind.Run, Command = "make" };
        Check(DockerfileStepNames.Rename(instruction, "  Build\r\n  the   app ") && instruction.StepName == "Build the app", "names are kept on one line with single spaces");
        Check(DockerfileModel.RenderInstruction(instruction) == "# step: Build the app\nRUN make", "the renderer writes the step comment directly above the instruction");
        Check(!DockerfileStepNames.Rename(instruction, "Build the app"), "renaming to the same name changes nothing");
        Check(DockerfileStepNames.Rename(instruction, "") && DockerfileModel.RenderInstruction(instruction) == "RUN make", "an empty name removes the step comment");
        var comment = new DockerInstruction { Kind = InstructionKind.Comment, Value = "note" };
        Check(!DockerfileStepNames.Rename(comment, "x") && !DockerfileStepNames.CanBeNamed(comment), "comments cannot be named");
        Check(DockerfileStepNames.Clean(new string('a', 300)).Length == DockerfileStepNames.MaxLength, "long names are cut to the maximum length");

        var starter = DockerfileStarters.All.First(s => s.Id == "node").Create();
        var text = DockerfileModel.Render(starter);
        Check(text.Contains("# step: Install dependencies\nRUN ", StringComparison.Ordinal) && DockerfileModel.Render(DockerfileModel.Parse(text).Document) == text, "the Node starter names its steps and round trips");
        Check(FirstRun(DockerfileModel.Deserialize(DockerfileModel.Serialize(starter))).StepName == "Install dependencies", "step names survive the draft JSON");

        var history = new DockerfileModelHistory();
        var document = DockerfileModel.Parse("FROM alpine\nRUN true\n").Document;
        history.Reset(document);
        DockerfileStepNames.Rename(FirstRun(document), "Check the shell");
        Check(history.Commit(document), "renaming a step is recorded as an undo step");
        var undone = history.Undo()!;
        Check(FirstRun(undone).StepName.Length == 0 && DockerfileModel.Render(undone) == "FROM alpine\nRUN true\n", "undo removes the name again");
        var redone = history.Redo()!;
        Check(FirstRun(redone).StepName == "Check the shell" && DockerfileModel.Render(redone).Contains("# step: Check the shell\nRUN true", StringComparison.Ordinal), "redo restores the name");
    }

    static void StepPatches()
    {
        const string source = "# syntax=docker/dockerfile:1\r\nFROM alpine AS base\r\n  # a note the user wrote\r\nRUN apk add curl \\\r\n    git\r\n\r\nCOPY . /app\r\n";
        var parsed = DockerfileModel.Parse(source);
        var state = DockerfilePatch.FromParse(source, parsed);
        var document = parsed.Document;
        var run = FirstRun(document);
        DockerfileStepNames.Rename(run, "Install tools");
        var named = DockerfilePatch.TryPatch(state, document);
        Check(named != null && named.Text == source.Replace("RUN apk add curl \\\r\n    git", "# step: Install tools\r\nRUN apk add curl \\\r\n    git"), "naming a step adds only its comment line and keeps CRLF, other comments and blank lines");
        DockerfileStepNames.Rename(run, "Install curl and git");
        var renamed = DockerfilePatch.TryPatch(named!, document);
        Check(renamed != null && renamed.Text == named!.Text.Replace("# step: Install tools", "# step: Install curl and git"), "renaming changes only the step comment line");
        DockerfileStepNames.Rename(run, "");
        var removed = DockerfilePatch.TryPatch(renamed!, document);
        Check(removed != null && removed.Text == source, "removing the name removes only that line and restores the original file");

        var reparsed = DockerfileModel.Parse(named!.Text);
        Check(FirstRun(reparsed.Document).StepName == "Install tools" && reparsed.Document.Stages[0].Instructions[0].Kind == InstructionKind.Comment, "the CRLF file with a step comment parses back with the name and the user's comment");
        var expected = document.Clone();
        DockerfileStepNames.Rename(FirstRun(expected), "Install tools");
        Check(DockerfileModel.Render(reparsed.Document) == DockerfileModel.Render(expected), "parsing the CRLF file with a step comment gives the same model as naming the step");
        var reparsedState = DockerfilePatch.FromParse(named.Text, reparsed);
        Check(DockerfilePatch.TryPatch(reparsedState, reparsed.Document)?.Text == named.Text, "an unchanged CRLF file with a step comment patches to itself");
    }

    /// <summary>The suggestions at the end of <paramref name="text"/>, without the "Pick…" picker row.</summary>
    static SuggestionResult Complete(string text)
    {
        var result = DockerfileCompletion.Complete(text, text.Length, SuggestionSources.None);
        return result with { Items = result.Items.Where(i => i.Kind != SuggestionKind.Picker).ToList() };
    }

    static void ValueSets()
    {
        var sharing = Complete("FROM alpine\nRUN --mount=type=cache,target=/var/cache/apt,sharing=");
        Check(sharing.Items.Select(i => i.Label).SequenceEqual(new[] { "shared", "private", "locked" }), "after sharing= the suggestions are exactly shared, private and locked");
        Check(sharing.Items.All(i => SuggestionPresentation.ValuesLine(i) == "Values: shared (default) · private · locked"), "sharing values show the Values line with the default marked");
        var picker = DockerfileValueKinds.Detect("FROM alpine\nRUN --mount=type=cache,sharing=lo", "FROM alpine\nRUN --mount=type=cache,sharing=lo".Length);
        Check(picker is { Kind: EditorValueKind.Choice } && picker.Options.SequenceEqual(DockerfileCompletionData.CacheSharing.Values), "the sharing picker offers exactly the reference values");

        var keys = Complete("FROM alpine\nRUN --mount=type=cache,");
        Check(keys.Items.First(i => i.Label == "sharing=").Allowed == DockerfileCompletionData.CacheSharing, "the sharing key documents its values");
        Check(SuggestionPresentation.ValuesLine(keys.Items.First(i => i.Label == "target=")) == "", "open values such as target have no Values line");
        var readOnly = Complete("FROM alpine\nRUN --mount=type=cache,ro=");
        Check(readOnly.Items.Select(i => i.Label).SequenceEqual(new[] { "true", "false" }) && readOnly.Items[0].Allowed == DockerfileCompletionData.BoolFalse, "the ro short form offers true and false");
        Check(Complete("FROM alpine\nRUN --mount=type=bind,readwrite=").Items.Select(i => i.Label).SequenceEqual(new[] { "true", "false" }), "bind readwrite offers true and false");
        Check(Complete("FROM alpine\nRUN --mount=type=").Items.Select(i => i.Label).SequenceEqual(DockerfileCompletionData.MountTypeSet.Values) && DockerfileCompletionData.MountTypeSet.Line() == "bind (default) · cache · tmpfs · secret · ssh", "mount types are the fixed set with bind as the default");

        var network = Complete("FROM alpine\nRUN --network=");
        Check(network.Items.Select(i => i.Label).SequenceEqual(new[] { "default", "none", "host" }) && SuggestionPresentation.ValuesLine(network.Items[0]) == "Values: default (default) · none · host", "RUN --network offers its modes with the default marked");
        Check(Complete("FROM alpine\nRUN --security=").Items.Select(i => i.Label).SequenceEqual(new[] { "sandbox", "insecure" }), "RUN --security offers sandbox and insecure");
        var link = DockerfileValueKinds.Detect("FROM alpine\nCOPY --link=t a /b", "FROM alpine\nCOPY --link=t".Length);
        Check(link is { Kind: EditorValueKind.Choice } && link.Options.SequenceEqual(new[] { "true", "false" }), "boolean COPY flags open a true/false picker");
        var flags = Complete("FROM alpine\nRUN --");
        Check(flags.Items.First(i => i.Label == "--network=").Allowed == DockerfileCompletionData.NetworkModes && flags.Items.First(i => i.Label == "--mount=").Allowed == null, "flag rows carry a value set only when the reference has one");
        var retries = Complete("FROM alpine\nHEALTHCHECK --retries=");
        Check(retries.Items.Count > 0 && retries.Items.All(i => i.Allowed == null), "health check retries are suggestions, not a fixed set");
        Check(DockerfileCompletionData.Instructions.Where(i => i.Name == "HEALTHCHECK").SelectMany(i => i.Flags).All(f => f.Allowed == null), "no health check option claims a fixed set");
        Check(DockerfileCompletionData.Instructions.SelectMany(i => i.Flags).Where(f => f.Allowed != null).All(f => f.Offered.SequenceEqual(f.Allowed!.Values) && (f.Allowed.Default == null || f.Allowed.Contains(f.Allowed.Default))), "every flag value set is what completion offers, and its default is one of its values");
        var escape = Complete("# escape=");
        Check(escape.Items.Select(i => i.Label).SequenceEqual(new[] { "\\", "`" }) && escape.Items.All(i => i.Allowed == DockerfileCompletionData.EscapeCharacters), "the escape directive offers backslash and backtick");
        var translated = SuggestionPresentation.ValuesLine(sharing.Items[0], t => t == "Values:" ? "可選值：" : t == "(default)" ? "（預設）" : t);
        Check(translated == "可選值： shared （預設） · private · locked", "the Values line translates its words and keeps the values literal");
    }
}
