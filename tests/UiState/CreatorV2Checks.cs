using MaterialDockerImage.UI;

/// <summary>Checks for the framework-free decisions behind the second Creator and Suggestions boards: the stage outline, step titles, compact mount rows, the save state, and how suggestion rows and signatures are presented.</summary>
public static class CreatorV2Checks
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
        Outline();
        Mounts();
        SaveState();
        Suggestions();
        return count;
    }

    static void Outline()
    {
        var stage = new DockerfileStage { Name = "deps", BaseImage = "node:22-alpine", Platform = "$BUILDPLATFORM" };
        stage.Instructions.Add(new DockerInstruction { Kind = InstructionKind.Workdir, Value = "/app" });
        stage.Instructions.Add(new DockerInstruction { Kind = InstructionKind.Run, Heredoc = true, HeredocDelimiter = "EOF", HeredocBody = "set -e\nnpm ci" });
        var rows = DockerfileOutline.Rows(stage);
        Check(rows.Count == 3 && rows[0].Keyword == "FROM" && rows[0].InstructionIndex == -1 && rows[0].Argument == "--platform=$BUILDPLATFORM node:22-alpine AS deps", "the outline starts with the stage's FROM line, platform and name included");
        Check(rows[1].Keyword == "WORKDIR" && rows[1].InstructionIndex == 0 && rows[2].InstructionIndex == 1, "each instruction is one outline row pointing at its index");
        Check(rows.All(r => !r.Argument.Contains('\n')), "outline rows are single lines even for multi-line steps");
        Check(DockerfileOutline.FromArgument(new DockerfileStage()) == "?", "a stage without a base image shows a question mark, never an invented image");
        Check(DockerfileOutline.Title(InstructionKind.Run) == "Run commands" && DockerfileOutline.Title(InstructionKind.Raw) == "Raw instruction", "every step kind has a plain title");
        Check(Enum.GetValues<InstructionKind>().Select(DockerfileOutline.Title).Distinct().Count() == Enum.GetValues<InstructionKind>().Length, "step titles are distinct per kind");
    }

    static void Mounts()
    {
        Check(DockerfileOutline.MountKeyOption("cache") == "id" && DockerfileOutline.MountKeyOption("bind") == "source" && DockerfileOutline.MountKeyOption("tmpfs") == "size" && DockerfileOutline.MountKeyOption("secret") == "id", "the compact row edits id, source or size beside the target");
        var cache = new RunMount { Type = RunMounts.Cache, Target = "/root/.npm", Id = "npm", Sharing = "locked" };
        Check(DockerfileOutline.MountOtherOptions(cache) == "sharing=locked", "the remaining options leave out type, target and the key option");
        var bind = new RunMount { Type = RunMounts.Bind, Target = "/src", Source = "package.json", From = "deps", ReadWrite = true };
        var other = DockerfileOutline.MountOtherOptions(bind);
        Check(other.Contains("from=deps") && other.Contains("rw") && !other.Contains("source=") && !other.Contains("target="), "bind mounts keep from and rw in the remaining options");
        Check(DockerfileOutline.MountOtherOptions(new RunMount { Type = RunMounts.Ssh }) == "", "a mount with only defaults has no remaining options");
    }

    static void SaveState()
    {
        Check(DockerfileOutline.SaveState("", "", "FROM a") == CreatorSaveState.DraftOnly, "a draft never written to a file is only kept automatically");
        Check(DockerfileOutline.SaveState("C:\\p\\Dockerfile", "FROM a", "FROM a") == CreatorSaveState.Saved, "the same text as last written is saved");
        Check(DockerfileOutline.SaveState("C:\\p\\Dockerfile", "FROM a", "FROM b") == CreatorSaveState.UnsavedChanges, "different text from what was last written is unsaved changes");
    }

    static void Suggestions()
    {
        var parts = SuggestionPresentation.Signature("RUN --mount=type=cache,target=<path>[,id][,sharing]");
        Check(parts.Count(p => p.Required) == 1 && parts.First(p => p.Required).Text == "target=<path>", "only the required placeholder outside brackets is bold");
        Check(string.Concat(parts.Select(p => p.Text)) == "RUN --mount=type=cache,target=<path>[,id][,sharing]", "signature parts rebuild the exact text");
        var optional = SuggestionPresentation.Signature("COPY [--from=<stage>] <src>... <dest>");
        Check(optional.Where(p => p.Required).Select(p => p.Text).SequenceEqual(new[] { "<src>...", "<dest>" }), "placeholders inside square brackets stay plain");
        Check(SuggestionPresentation.Signature("").Count == 0, "an empty signature has no parts");

        var picker = new SuggestionItem("Pick a colour", "", SuggestionKind.Picker) { PickerKind = EditorValueKind.Colour };
        Check(SuggestionPresentation.Icon(picker) == Icons.Palette && SuggestionPresentation.RowTag(picker) == "Enter" && !SuggestionPresentation.RowTagWarns(picker), "a picker row shows its value's icon and the Enter tag");
        Check(SuggestionPresentation.Icon(new SuggestionItem("src/", "src/", SuggestionKind.Path)) == Icons.FolderOpen && SuggestionPresentation.Icon(new SuggestionItem("package.json", "package.json", SuggestionKind.Path)) == Icons.Description, "path rows tell folders from files");
        var badge = new SuggestionItem("--parents", "--parents", SuggestionKind.Flag, "keeps parent folders") { Badge = "1.20+", NeedsSyntaxUpgrade = true };
        Check(SuggestionPresentation.RowTag(badge) == "1.20+" && SuggestionPresentation.RowTagWarns(badge), "a version badge the file does not reach yet is a warning tag");
        var plain = new SuggestionItem("sharing", "sharing=", SuggestionKind.Key, "apt needs locked");
        Check(SuggestionPresentation.RowTag(plain) == "apt needs locked" && !SuggestionPresentation.RowTagWarns(plain), "without a badge the row tag is the detail");
        Check(SuggestionPresentation.DocChip(plain, _ => "key") == "apt needs locked" && SuggestionPresentation.DocChip(new SuggestionItem("x", "x", SuggestionKind.Key), _ => "key") == "key", "the documentation chip falls back to the kind name");
        foreach (var kind in Enum.GetValues<EditorValueKind>())
            Check(SuggestionPresentation.PickerIcon(kind).Length > 0, "picker icon for " + kind);
    }
}
