using System.Text.Json;

namespace MaterialDockerImage.Core;

public sealed record DockerImage(string ID, string Repository, string Tag, string Size, string CreatedSince);
public sealed record DockerContainer(string ID, string Names, string Image, string State, string Status, string Ports);
public sealed record DockerContext(string Name, string Description, string DockerEndpoint, bool Current);
public sealed record DockerNetwork(string ID, string Name, string Driver, string Scope);
public sealed record DockerVolume(string Name, string Driver, string Mountpoint);
public sealed record PortMapping(int HostPort, int ContainerPort, string Protocol = "tcp", string? HostAddress = null);
public sealed record VolumeMapping(string Source, string Destination, bool ReadOnly = false);
public sealed record BuildOptions
{
    public string ContextPath { get; init; } = ".";
    public string? Dockerfile { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<string> Platforms { get; init; } = [];
    public IReadOnlyDictionary<string, string> BuildArguments { get; init; } = new Dictionary<string,string>();
    public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string,string>();
    public string? Target { get; init; }
    public string? Output { get; init; }
    public bool NoCache { get; init; }
    public bool Pull { get; init; }
    public bool Load { get; init; } = true;
    public bool Push { get; init; }
}
public sealed record RunOptions
{
    public string Image { get; init; } = "";
    public string? Name { get; init; }
    public bool Detached { get; init; } = true;
    public bool RemoveOnExit { get; init; }
    public bool ReadOnly { get; init; }
    public IReadOnlyList<PortMapping> Ports { get; init; } = [];
    public IReadOnlyList<VolumeMapping> Volumes { get; init; } = [];
    public IReadOnlyDictionary<string,string> Environment { get; init; } = new Dictionary<string,string>();
    public IReadOnlyDictionary<string,string> Labels { get; init; } = new Dictionary<string,string>();
    public string? Network { get; init; }
    public string? RestartPolicy { get; init; }
    public string? WorkingDirectory { get; init; }
    public string? User { get; init; }
    public string? EntryPoint { get; init; }
    public double? Cpus { get; init; }
    public string? Memory { get; init; }
    public IReadOnlyList<string> Command { get; init; } = [];
}
public enum ComposeOperation { Config, Up, Down, Ps, Logs, Build, Pull }
public sealed record ComposeOptions
{
    public ComposeOperation Operation { get; init; }
    public string ProjectDirectory { get; init; } = ".";
    public IReadOnlyList<string> Files { get; init; } = [];
    public string? ProjectName { get; init; }
    public IReadOnlyList<string> Profiles { get; init; } = [];
    public IReadOnlyList<string> Services { get; init; } = [];
    public bool Detached { get; init; } = true;
    public bool Build { get; init; }
    public bool RemoveOrphans { get; init; }
    public bool RemoveVolumes { get; init; }
    public bool Follow { get; init; }
    public int Tail { get; init; } = 500;
}

public static class DockerJson
{
    public static IReadOnlyList<JsonElement> ParseRows(string output)
    {
        var rows = new List<JsonElement>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind == JsonValueKind.Array) rows.AddRange(document.RootElement.EnumerateArray().Select(x => x.Clone()));
            else if (document.RootElement.ValueKind == JsonValueKind.Object) rows.Add(document.RootElement.Clone());
            else throw new JsonException("Expected a JSON object or array.");
        }
        return rows;
    }
    public static string Text(JsonElement row, string name) => row.TryGetProperty(name, out var value) ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString() : "";
}
