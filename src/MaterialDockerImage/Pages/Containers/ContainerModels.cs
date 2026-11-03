using System.Text.Json;
using MaterialDockerImage.Core;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>One row of docker container ls --all.</summary>
internal sealed record ContainerRow(string Id, string Name, string Image, string State, string Status, string Ports, string RunningFor, string CreatedAt, string Command, string Labels)
{
    public bool IsRunning => State == "running";
    public bool IsPaused => State == "paused";
    public bool IsStopped => State is "exited" or "created" or "dead";
    public bool IsRestarting => State == "restarting";
    public string ShortId => Id.Length > 12 ? Id[..12] : Id;
    public string StateLabel => State.Length == 0 ? "unknown" : State;
    public string ComposeProject => Labels.Split(',').Select(l => l.Split('=', 2)).FirstOrDefault(p => p.Length == 2 && p[0] == "com.docker.compose.project")?[1] ?? "";
    public string PortsText => Ports.Length == 0 ? "—" : Ports;

    public Tone StateTone => State switch
    {
        "running" => Tone.Success,
        "paused" => Tone.Warning,
        "restarting" => Tone.Tertiary,
        "dead" => Tone.Error,
        "created" => Tone.Primary,
        _ => Tone.Neutral
    };

    public static ContainerRow From(JsonElement row) => new(
        DockerJson.Text(row, "ID"), DockerJson.Text(row, "Names"), DockerJson.Text(row, "Image"), DockerJson.Text(row, "State").ToLowerInvariant(),
        DockerJson.Text(row, "Status"), DockerJson.Text(row, "Ports"), DockerJson.Text(row, "RunningFor"), DockerJson.Text(row, "CreatedAt"),
        DockerJson.Text(row, "Command"), DockerJson.Text(row, "Labels"));
}
