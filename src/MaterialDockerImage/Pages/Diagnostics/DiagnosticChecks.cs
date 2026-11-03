using System.Text.Json;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

public enum CheckStatus { NotRun, Running, Passed, Warning, Failed }

/// <summary>One read-only diagnostic check and its last real result.</summary>
public sealed class DiagnosticCheck
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string Command { get; init; }
    public required Func<string?, CancellationToken, Task<CheckOutcome>> Run { get; init; }
    public CheckStatus Status { get; set; } = CheckStatus.NotRun;
    public CheckOutcome? Last { get; set; }
    public DateTimeOffset? RanAt { get; set; }
}

/// <summary>Summary shown on the row, plus the exact command, exit code and output shown in the expandable detail.</summary>
public sealed record CheckOutcome(CheckStatus Status, string Value, string Detail, string Command, int? ExitCode, string Output);

/// <summary>The fixed list of read-only checks. Every value comes from the actual process output.</summary>
internal static class DiagnosticChecks
{
    private static AppHost Host => AppHost.Current;

    public static List<DiagnosticCheck> Create() =>
    [
        new() { Id = "cli", Label = "Docker CLI found", Command = "docker version --format \"{{json .}}\"", Run = (c, t) => Version(c, t, client: true) },
        new() { Id = "engine", Label = "Engine reachable", Command = "docker version --format \"{{json .}}\"", Run = (c, t) => Version(c, t, client: false) },
        new() { Id = "api", Label = "API version", Command = "docker version --format \"{{json .}}\"", Run = Api },
        new() { Id = "buildx", Label = "Buildx available", Command = "docker buildx version", Run = (c, t) => Plain(["buildx", "version"], c, t) },
        new() { Id = "compose", Label = "Compose available", Command = "docker compose version", Run = (c, t) => Plain(["compose", "version"], c, t) },
        new() { Id = "contexts", Label = "Contexts listed", Command = "docker context ls --format \"{{json .}}\"", Run = Contexts },
        new() { Id = "df", Label = "Engine disk usage", Command = "docker system df", Run = Disk },
        new() { Id = "auth", Label = "Registry auth file readable", Command = "read config.json (registry names only)", Run = (_, _) => Task.FromResult(Auth()) },
        new() { Id = "ostype", Label = "Container mode (Linux or Windows)", Command = "docker info --format \"{{json .}}\"", Run = OsType },
    ];

    private static string Combined(CommandResult r) => (r.StandardOutput + (r.StandardError.Length > 0 ? "\n" + r.StandardError : "")).Trim();
    private static string FirstLine(string s) { var line = s.Trim().Split('\n').FirstOrDefault()?.Trim() ?? ""; return PageParts.Shorten(line, 160); }

    private static JsonElement? ParseObject(string text)
    {
        try
        {
            var start = text.IndexOf('{');
            if (start < 0) return null;
            using var doc = JsonDocument.Parse(text[start..]);
            return doc.RootElement.Clone();
        }
        catch (JsonException) { return null; }
    }

    private static string Field(JsonElement? root, string section, string name)
    {
        if (root is not { } r || !r.TryGetProperty(section, out var s) || s.ValueKind != JsonValueKind.Object) return "";
        return DockerJson.Text(s, name);
    }

    private static async Task<CheckOutcome> Version(string? context, CancellationToken token, bool client)
    {
        const string command = "docker version --format \"{{json .}}\"";
        var r = await Host.Read(["version", "--format", "{{json .}}"], context, token);
        var output = Combined(r);
        var json = ParseObject(r.StandardOutput);
        if (client)
        {
            var version = Field(json, "Client", "Version");
            if (version.Length > 0) return new(CheckStatus.Passed, version, $"Client {version}, {Field(json, "Client", "Os")}/{Field(json, "Client", "Arch")}", command, r.ExitCode, output);
            return new(CheckStatus.Failed, "Not found", r.ExitCode == -1 ? "The docker command was not found. Install Docker Desktop or add docker.exe to PATH." : FirstLine(r.StandardError), command, r.ExitCode, output);
        }
        var server = Field(json, "Server", "Version");
        if (r.Success && server.Length > 0) return new(CheckStatus.Passed, "Reachable", $"Server {server} on context {context ?? "current"}", command, r.ExitCode, output);
        return new(CheckStatus.Failed, "Not reachable", (FirstLine(r.StandardError) is { Length: > 0 } e ? e : "No server section in the output.") + " Start Docker Desktop or the engine, or choose another context.", command, r.ExitCode, output);
    }

    private static async Task<CheckOutcome> Api(string? context, CancellationToken token)
    {
        const string command = "docker version --format \"{{json .}}\"";
        var r = await Host.Read(["version", "--format", "{{json .}}"], context, token);
        var json = ParseObject(r.StandardOutput);
        var clientApi = Field(json, "Client", "ApiVersion");
        var serverApi = Field(json, "Server", "ApiVersion");
        var minimum = Field(json, "Server", "MinAPIVersion");
        if (serverApi.Length == 0) return new(CheckStatus.Failed, clientApi.Length > 0 ? "client " + clientApi : "Unknown", "The server API version is unavailable because the engine did not answer.", command, r.ExitCode, Combined(r));
        var status = clientApi == serverApi ? CheckStatus.Passed : CheckStatus.Warning;
        var detail = clientApi == serverApi ? "Client and server agree" : $"Client {clientApi}, server {serverApi}" + (minimum.Length > 0 ? $", server minimum {minimum}" : "") + ". The CLI negotiates down automatically; differences are usually harmless.";
        return new(status, serverApi, detail, command, r.ExitCode, Combined(r));
    }

    private static async Task<CheckOutcome> Plain(string[] args, string? context, CancellationToken token)
    {
        var command = "docker " + string.Join(' ', args);
        var r = await Host.Read(args, context, token);
        var output = Combined(r);
        if (r.Success)
        {
            var version = System.Text.RegularExpressions.Regex.Match(r.StandardOutput, @"v?\d+\.\d+(\.\d+)?[\w.+-]*").Value;
            return new(CheckStatus.Passed, version.Length > 0 ? version : "Available", FirstLine(r.StandardOutput), command, r.ExitCode, output);
        }
        return new(CheckStatus.Failed, "Unavailable", FirstLine(r.StandardError) is { Length: > 0 } e ? e : $"Exited with code {r.ExitCode}.", command, r.ExitCode, output);
    }

    private static async Task<CheckOutcome> Contexts(string? context, CancellationToken token)
    {
        const string command = "docker context ls --format \"{{json .}}\"";
        var r = await Host.Read(["context", "ls", "--format", "{{json .}}"], null, token);
        if (!r.Success) return new(CheckStatus.Failed, "Unavailable", FirstLine(r.StandardError), command, r.ExitCode, Combined(r));
        try
        {
            var rows = DockerJson.ParseRows(r.StandardOutput);
            var names = rows.Select(x => DockerJson.Text(x, "Name") + (DockerJson.Text(x, "Current").Equals("true", StringComparison.OrdinalIgnoreCase) ? " (CLI default)" : "")).ToList();
            var selected = context ?? "the CLI default";
            return new(rows.Count > 0 ? CheckStatus.Passed : CheckStatus.Warning, rows.Count.ToString(), string.Join(", ", names) + $" · this window uses {selected}", command, r.ExitCode, Combined(r));
        }
        catch (JsonException e) { return new(CheckStatus.Warning, "Unparsed", e.Message, command, r.ExitCode, Combined(r)); }
    }

    private static async Task<CheckOutcome> Disk(string? context, CancellationToken token)
    {
        const string command = "docker system df";
        var r = await Host.Read(["system", "df"], context, token);
        if (!r.Success) return new(CheckStatus.Failed, "Unavailable", FirstLine(r.StandardError), command, r.ExitCode, Combined(r));
        var lines = r.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Skip(1).ToList();
        var summary = string.Join(" · ", lines.Select(l => System.Text.RegularExpressions.Regex.Split(l, @"\s{2,}")).Where(p => p.Length >= 4).Select(p => $"{p[0]} {p[3]}" + (p.Length >= 5 ? $" (reclaimable {p[4]})" : "")));
        return new(CheckStatus.Passed, lines.Count + " types", summary.Length > 0 ? summary : FirstLine(r.StandardOutput), command, r.ExitCode, r.StandardOutput.TrimEnd());
    }

    private static CheckOutcome Auth()
    {
        var file = PageParts.ReadAuthFile();
        var path = PageParts.DisplayPath(file);
        var command = "read " + path + " (registry names only)";
        if (file.Error != null) return new(CheckStatus.Warning, "Unreadable", file.Error, command, null, file.Error);
        if (!file.Exists) return new(CheckStatus.Passed, "Not created", "No configuration file yet; signing in creates it.", command, null, "File not present.");
        var names = string.Join(", ", file.Entries.Select(e => e.Registry));
        return new(CheckStatus.Passed, file.Entries.Count + " registries", names.Length > 0 ? names : "No registries listed",
            command, null, $"Registries: {(names.Length > 0 ? names : "none")}\nCredential store: {file.CredentialStore ?? "not set"}\n(Credential values are never read.)");
    }

    private static async Task<CheckOutcome> OsType(string? context, CancellationToken token)
    {
        const string command = "docker info --format \"{{json .}}\"";
        var r = await Host.Read(["info", "--format", "{{json .}}"], context, token);
        var json = ParseObject(r.StandardOutput);
        var os = json is { } j ? DockerJson.Text(j, "OSType") : "";
        var engineOs = json is { } k ? DockerJson.Text(k, "OperatingSystem") : "";
        // docker info prints a large document; keep only the fields this check is about.
        var shown = json is { } x ? $"OSType: {os}\nOperatingSystem: {engineOs}\nServerVersion: {DockerJson.Text(x, "ServerVersion")}\nArchitecture: {DockerJson.Text(x, "Architecture")}" : Combined(r);
        if (os.Length == 0) return new(CheckStatus.Failed, "Unknown", FirstLine(r.StandardError) is { Length: > 0 } e ? e : "The engine did not report OSType.", command, r.ExitCode, PageParts.Sanitize(shown));
        var mode = os.Equals("windows", StringComparison.OrdinalIgnoreCase) ? "Windows containers" : os.Equals("linux", StringComparison.OrdinalIgnoreCase) ? "Linux containers" : os;
        return new(CheckStatus.Passed, mode, engineOs, command, r.ExitCode, PageParts.Sanitize(shown));
    }
}
