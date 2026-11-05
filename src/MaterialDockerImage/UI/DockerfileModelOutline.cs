using System;
using System.Collections.Generic;

namespace MaterialDockerImage.UI;

/// <summary>One row of the Creator's stage outline: the keyword and its one-line argument. InstructionIndex is -1 for the FROM line.</summary>
public sealed record DockerfileOutlineRow(string Keyword, string Argument, int InstructionIndex);

/// <summary>Where the draft stands relative to the file it was last saved to.</summary>
public enum CreatorSaveState { DraftOnly, Saved, UnsavedChanges }

/// <summary>Framework-free text for the Creator: step titles, the stage outline and the save state shown in the header.</summary>
public static class DockerfileOutline
{
    /// <summary>A short, plain title for the step editor, by instruction kind.</summary>
    public static string Title(InstructionKind kind) => kind switch
    {
        InstructionKind.Run => "Run commands",
        InstructionKind.Copy => "Copy files",
        InstructionKind.Add => "Add files",
        InstructionKind.Workdir => "Working directory",
        InstructionKind.Env => "Environment variables",
        InstructionKind.Arg => "Build argument",
        InstructionKind.Expose => "Exposed port",
        InstructionKind.User => "User",
        InstructionKind.Cmd => "Default command",
        InstructionKind.Entrypoint => "Entry point",
        InstructionKind.Healthcheck => "Health check",
        InstructionKind.Label => "Labels",
        InstructionKind.Volume => "Volumes",
        InstructionKind.StopSignal => "Stop signal",
        InstructionKind.Shell => "Shell",
        InstructionKind.OnBuild => "Instruction for child images",
        InstructionKind.Comment => "Comment",
        _ => "Raw instruction",
    };

    /// <summary>The FROM line of a stage as the outline shows it.</summary>
    public static string FromArgument(DockerfileStage stage)
    {
        var parts = new List<string>();
        if (stage.Platform.Trim().Length > 0) parts.Add("--platform=" + stage.Platform.Trim());
        parts.Add(stage.BaseImage.Trim().Length > 0 ? stage.BaseImage.Trim() : "?");
        if (stage.Name.Trim().Length > 0) { parts.Add("AS"); parts.Add(stage.Name.Trim()); }
        return string.Join(" ", parts);
    }

    /// <summary>The outline of one stage: its FROM line, then one row per instruction (first line of the summary only).</summary>
    public static IReadOnlyList<DockerfileOutlineRow> Rows(DockerfileStage stage)
    {
        var rows = new List<DockerfileOutlineRow> { new("FROM", FromArgument(stage), -1) };
        for (var i = 0; i < stage.Instructions.Count; i++)
        {
            var instruction = stage.Instructions[i];
            var summary = DockerfileModel.Summary(instruction).Replace("\r", "");
            var newline = summary.IndexOf('\n');
            if (newline >= 0) summary = summary[..newline] + " …";
            rows.Add(new DockerfileOutlineRow(DockerfileModel.Keyword(instruction.Kind), summary, i));
        }
        return rows;
    }

    /// <summary>The option shown beside the target in a compact mount row: id (cache, secret, ssh), source (bind) or size (tmpfs).</summary>
    public static string MountKeyOption(string type) => type.Trim().ToLowerInvariant() switch
    {
        RunMounts.Bind => "source",
        RunMounts.Tmpfs => "size",
        _ => "id",
    };

    /// <summary>The rendered options of a mount other than type, target and the key option, for the compact row.</summary>
    public static string MountOtherOptions(RunMount mount)
    {
        var key = MountKeyOption(mount.Type) + "=";
        var parts = RunMounts.Render(mount).Split(',');
        var rest = new List<string>();
        foreach (var part in parts)
        {
            if (part.StartsWith("type=", StringComparison.Ordinal) || part.StartsWith("target=", StringComparison.Ordinal) || part.StartsWith(key, StringComparison.Ordinal)) continue;
            rest.Add(part);
        }
        return string.Join(",", rest);
    }

    /// <summary>
    /// Draft only when the draft was never written to a file; Saved when the rendered text equals what was last written;
    /// otherwise UnsavedChanges.
    /// </summary>
    public static CreatorSaveState SaveState(string savedPath, string savedText, string currentText) =>
        savedPath.Length == 0 ? CreatorSaveState.DraftOnly
        : string.Equals(savedText, currentText, StringComparison.Ordinal) ? CreatorSaveState.Saved
        : CreatorSaveState.UnsavedChanges;
}
