using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

public static class Destinations
{
    public static readonly IReadOnlyList<Destination> All =
    [
        new("Build", Icons.Construction, "Build images from a Dockerfile and context folder", () => new BuildPage(), true),
        new("Creator", Icons.Code, "Create a Dockerfile step by step with live checks", () => new CreatorPage(), true),
        new("Images", Icons.Layers, "Local image inventory, pull, tag, push, export", () => new ImagesPage(), true),
        new("Containers", Icons.DeployedCode, "Container lifecycle, logs, stats, exec, files", () => new ContainersPage(), true),
        new("Compose", Icons.Stacks, "Compose projects, services and source", () => new ComposePage(), true),
        new("Canvas", Icons.AccountTree, "Dockerfile stage graph and runtime topology", () => new CanvasPage(), true),
        new("Storage", Icons.Database, "Volumes and networks", () => new StoragePage(), true),
        new("Monitor", Icons.Monitoring, "Live CPU, memory, network and disk charts", () => new MonitorPage(), true),
        new("System", Icons.Dns, "Disk usage, cleanup and engine information", () => new SystemPage(), true),
        new("Registry", Icons.Cloud, "Registry sign-in, Docker Hub search and builders", () => new RegistryPage(), true),
        new("Diagnostics", Icons.Stethoscope, "Engine checks, events and sanitized reports", () => new DiagnosticsPage(), true),
        new("Tools", Icons.HomeRepairService, "Base64, documentation and changelog", () => new ToolsPage(), true),
        new("Activity", Icons.History, "Local version history and notifications", () => new ActivityPage(), true),
        new("Settings", Icons.Settings, "Appearance, language, accessibility and more", () => new SettingsPage(), true),
        new("Setup", Icons.RocketLaunch, "First-run setup wizard", () => new SetupPage(), false),
    ];
    public static Destination? Find(string key) => All.FirstOrDefault(d => d.Key == key);
}
