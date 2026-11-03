using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>Outcome of a Docker Scout command, classified from its exit status and output only.</summary>
public enum ScoutState { Ready, PluginMissing, NotSignedIn, Failed, Cancelled }

/// <summary>Classifies Docker Scout command output into honest states without inventing results.</summary>
public static class ScoutStatus
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex Version = new(@"v?(\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.\-]+)?)", RegexOptions.None, RegexTimeout);
    private static readonly Regex Missing = new(@"'scout' is not a docker command|unknown command:?\s+""?scout|docker:\s+'scout'|unknown (shorthand )?flag.*scout|is not a docker command", RegexOptions.IgnoreCase, RegexTimeout);
    private static readonly Regex SignIn = new(@"\blog ?in\b|\bsign ?in\b|\bnot (?:logged|signed) in\b|\bnot authenticated\b|\bauthentication required\b|\bunauthori[sz]ed\b|\bdocker id\b", RegexOptions.IgnoreCase, RegexTimeout);

    /// <summary>Classifies the result of <c>docker scout version</c>.</summary>
    public static ScoutState ClassifyVersion(int exitCode, string output) =>
        exitCode == 0 && !Missing.IsMatch(output) ? ScoutState.Ready : ScoutState.PluginMissing;

    /// <summary>Classifies a failed Scout analysis from its combined output.</summary>
    public static ScoutState ClassifyFailure(string output)
    {
        if (Missing.IsMatch(output)) return ScoutState.PluginMissing;
        if (SignIn.IsMatch(output)) return ScoutState.NotSignedIn;
        return ScoutState.Failed;
    }

    /// <summary>The version number printed by <c>docker scout version</c>, or an empty string.</summary>
    public static string ParseVersion(string output)
    {
        foreach (var line in output.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.IndexOf("version", StringComparison.OrdinalIgnoreCase) < 0) continue;
            var m = Version.Match(line);
            if (m.Success) return m.Groups[1].Value;
        }
        var any = Version.Match(output);
        return any.Success ? any.Groups[1].Value : "";
    }

    public static string Explain(ScoutState state) => state switch
    {
        ScoutState.Ready => "Docker Scout is available.",
        ScoutState.PluginMissing => "The Docker Scout CLI plugin is not installed for this docker command. It is included with Docker Desktop; on Docker Engine install the docker-scout plugin, then check again.",
        ScoutState.NotSignedIn => "Docker Scout needs a signed-in Docker account (docker login) with access to the image. Sign in from Registry, then analyze again.",
        ScoutState.Cancelled => "The analysis was canceled before it finished. No results were recorded.",
        _ => "Docker Scout could not analyze the image. The exact command output is shown below."
    };
}
