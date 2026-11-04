using System.Collections.Generic;
using System.IO;

namespace BuildCompanion;

public sealed record SetupItem(string Key, string RelativePath, bool Present, bool Required);

public static class SetupCheck
{
    /// <summary>Read-only inventory. The batch files bootstrap missing pinned dependencies themselves.</summary>
    public static IReadOnlyList<SetupItem> Inspect(string root)
    {
        SetupItem Item(string key, string relative, bool required) => new(key, relative, File.Exists(Path.Combine(root, relative)), required);
        return new[]
        {
            Item("build", "build.bat", true),
            Item("installer", "build-installer.bat", true),
            Item("download", "download-dependencies.bat", true),
            Item("dotnet", Path.Combine(".tools", "dotnet-path.txt"), false),
            Item("node", Path.Combine(".tools", "node-path.txt"), false),
            Item("app", BuildPlan.RelativeApplication, false),
        };
    }

    /// <summary>The repository can be built when its three entrypoints exist; missing .tools files are prepared on first build.</summary>
    public static bool CanBuild(IReadOnlyList<SetupItem> items)
    {
        foreach (var item in items) if (item.Required && !item.Present) return false;
        return true;
    }
}
