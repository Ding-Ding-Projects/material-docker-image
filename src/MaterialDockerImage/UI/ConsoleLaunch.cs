namespace MaterialDockerImage.UI;

/// <summary>
/// Decides how an interactive docker command is handed to a console window. By default the command goes through the
/// shell, so Windows uses the default terminal application (often Windows Terminal, which may open the session as a
/// tab in an existing window). The classic choice starts the Windows console host directly, which always opens a new
/// window on the same desktop as this app.
/// </summary>
public static class ConsoleLaunch
{
    public sealed record Plan(string FileName, string Arguments, bool UseShellExecute);

    /// <param name="tokens">docker arguments, each already validated to contain no whitespace or quotes.</param>
    /// <param name="classicConsole">true to start conhost.exe from <paramref name="systemDirectory"/> instead of the default terminal.</param>
    public static Plan Build(IReadOnlyList<string> tokens, bool classicConsole, string systemDirectory)
    {
        if (tokens.Count == 0) throw new ArgumentException("A docker command needs at least one argument.", nameof(tokens));
        foreach (var t in tokens)
            if (t.Length == 0 || t.Any(c => char.IsWhiteSpace(c) || c == '"')) throw new ArgumentException("Console arguments must not be empty or contain whitespace or quotes.", nameof(tokens));
        var joined = string.Join(" ", tokens);
        return classicConsole
            ? new Plan(Path.Combine(systemDirectory, "conhost.exe"), "docker " + joined, false)
            : new Plan("docker", joined, true);
    }
}
