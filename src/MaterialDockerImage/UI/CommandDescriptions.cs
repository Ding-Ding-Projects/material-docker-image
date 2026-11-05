namespace MaterialDockerImage.UI;

/// <summary>
/// One-line descriptions for every command palette entry, keyed by command id. The palette shows the description under
/// the title and searches it. Framework-free so tests/UiState can check that every registered id has one.
/// Every description has a Cantonese entry in Services/LocStrings.PaletteDescriptions.cs.
/// </summary>
public static class CommandDescriptions
{
    private static readonly Dictionary<string, string> Fixed = new(StringComparer.Ordinal)
    {
        // Shell
        ["palette"] = "Search every command, destination and setting in one place",
        ["tab.new"] = "Open the catalogue of destinations, tools and settings in a new tab",
        ["tab.close"] = "Close the current tab",
        ["tab.next"] = "Switch to the tab on the right, wrapping around at the end",
        ["tab.search"] = "Find an open tab by its title and jump to it",
        ["tab.close-containing"] = "Close every tab whose title contains the text or pattern you enter",
        ["tab.close-not-containing"] = "Close every tab whose title does not contain the text or pattern you enter",
        ["notifications"] = "Review recent messages, warnings and finished operations",
        ["accessibility"] = "Text size, contrast, motion and narration in one quick menu",
        ["output.toggle"] = "Open or close the docked panel with command output and logs",
        ["contexts.refresh"] = "Reload engine contexts and check which ones are reachable",
        // Settings rows with live controls
        ["set.theme"] = "Follow Windows, or always use light or dark colours",
        ["set.motion"] = "Shorten or remove animations across the app",
        ["set.contrast"] = "Stronger outlines and text colours for readability",
        ["set.language"] = "English, Hong Kong Cantonese, or both side by side",
        ["set.sounds"] = "Short sounds when an action finishes or fails",
        ["set.output"] = "Keep the output panel open at the bottom of the window",
        ["set.schedules"] = "Change settings automatically by time, date or schedule",
        ["set.locks"] = "Add, change or remove PIN, password and authenticator locks",
        ["adhd.focus"] = "Hide nonessential chrome and narrow the navigation rail",
        ["adhd.lowstim"] = "Turn off action sounds and every animation",
        ["adhd.momentum"] = "Count operations finished in a row this session",
        ["adhd.time"] = "Show a clock in the title bar",
        ["adhd.onething"] = "Opening a destination reuses the current tab",
        ["tools.support-tickets"] = "Local help desk for getting back into a lock you cannot open",
        // Pages
        ["activity.open"] = "Browse saved versions of your files and restore one",
        ["activity.folder"] = "Open the local history folder in File Explorer",
        ["build.open"] = "Load a Dockerfile from disk into the Build editor",
        ["build.save"] = "Write the Build editor's Dockerfile back to its file",
        ["build.check"] = "Run docker buildx build --check and list the findings",
        ["build.build"] = "Build the image from the Dockerfile and context folder",
        ["build.vscode"] = "Open the current Dockerfile in Visual Studio Code",
        ["build.timeline"] = "Follow each BuildKit step with its status and duration",
        ["build.history"] = "Previous builds recorded by buildx history",
        ["build.cancel"] = "Stop the build started from the Build page",
        ["bake.folder"] = "Choose a folder and find its bake or Compose files",
        ["bake.file"] = "Open a docker-bake.hcl, JSON or Compose file",
        ["bake.print"] = "Resolve the targets and groups with docker buildx bake --print",
        ["bake.build"] = "Build the targets ticked in the bake plan",
        ["bake.cancel"] = "Stop the bake that is running",
        ["canvas.fit"] = "Zoom the graph so every stage fits the view",
        ["canvas.parse"] = "Draw the stage graph of the Dockerfile open in Build",
        ["compose.open"] = "Load a compose.yaml or docker-compose.yml file",
        ["compose.refresh"] = "Reload Compose projects from docker compose ls",
        ["containers.run"] = "Start a new container from a local image",
        ["containers.refresh"] = "Reload the container list from the engine",
        ["containers.prune"] = "Delete every stopped container after you confirm",
        ["containers.restart-selected"] = "Restart the container focused in the Containers list",
        ["contexts.create"] = "Add an engine endpoint over SSH, TCP or TLS",
        ["contexts.import"] = "Add a context from an exported archive",
        ["creator.import"] = "Parse a Dockerfile from disk into editable stages",
        ["creator.detect"] = "Find the language and build files in a folder and draft a Dockerfile",
        ["creator.buildcommand"] = "Copy the docker buildx build command for this draft",
        ["creator.step.rename"] = "Name the open Creator step; the name is kept as a # step: comment above it",
        ["creator.tobuild"] = "Send the Creator draft to the Build page",
        ["diagnostics.run"] = "Check the engine connection, versions and health",
        ["diagnostics.report"] = "Preview and save a report with secrets removed",
        ["diagnostics.events"] = "Follow docker events from the engine as they happen",
        ["images.pull"] = "Download an image from a registry by name and tag",
        ["images.load"] = "Load images from a docker save tar archive",
        ["images.import"] = "Create an image from a filesystem tarball",
        ["images.openDetail"] = "Show the selected image full page with its layers, platforms and vulnerabilities",
        ["images.prune"] = "Delete untagged images that no container uses",
        ["images.retag"] = "Add several tags to the selected image at once",
        ["images.pushRepository"] = "Upload every tag of the selected repository",
        ["images.copyPull"] = "Copy a docker pull command for the selected image",
        ["images.refresh"] = "Reload the local image list from the engine",
        ["minecraft.create"] = "Guided setup for a Java or Bedrock server in a container",
        ["minecraft.refresh"] = "Reload Minecraft servers and their status",
        ["minecraft.backups"] = "Open the folder that holds world backups",
        ["monitor.pause"] = "Stop or restart the live CPU, memory and network charts",
        ["monitor.sample"] = "Read container statistics once, right now",
        ["registry.signin"] = "Store registry credentials with docker login",
        ["registry.search"] = "Find public images on Docker Hub",
        ["registry.builders"] = "List buildx builders and prune their build cache",
        ["security.scanAll"] = "Check every local image for known vulnerabilities",
        ["security.scanSelected"] = "Check the selected image for known vulnerabilities",
        ["security.export"] = "Save the vulnerability findings as JSON or CSV",
        ["security.cancel"] = "Stop the Docker Scout scan that is running",
        ["security.refresh"] = "Reload images and saved findings",
        ["settings.export"] = "Save every setting to a JSON file",
        ["settings.import"] = "Restore settings from an exported JSON file",
        ["settings.school"] = "English-only wording; turning it off needs the PIN or password you set",
        ["settings.open"] = "Appearance, language, accessibility and more",
        ["setup.open"] = "Walk through engine detection and first-run choices again",
        ["storage.volumes"] = "Open Storage on the list of volumes",
        ["storage.volume.create"] = "Make a named volume with a driver and options",
        ["storage.network.create"] = "Make a bridge or overlay network with its subnet",
        ["editor.suggest"] = "Show completions at the caret in a Dockerfile or Compose editor",
        ["editor.pick"] = "Open a picker for the value under the caret",
        ["editor.suggest.typing"] = "Show completions automatically as you type",
        ["swarm.refresh"] = "Reload swarm services, stacks and nodes",
        ["swarm.service.create"] = "Create a swarm service with its image and replicas",
        ["system.measure"] = "Show what images, containers, volumes and cache use on disk",
        ["system.cleanup"] = "Choose unused images, containers, volumes and cache to remove",
        ["tools.base64"] = "Encode or decode text, files and data URLs",
        ["tools.docs"] = "Read the bundled guides without a network connection",
        ["tools.authenticator"] = "Enrol and read authenticator codes for locks",
        ["tools.changelog"] = "This build's version and its release history",
        // Appearance
        ["appearance.customize"] = "Change the colour, shape, size or visibility of the focused element",
        ["appearance.pick"] = "Click any element to customize how it looks",
        ["appearance.overrides"] = "List every element you customized and reset any of them",
        ["appearance.overrides.reset"] = "Remove every element customization",
        ["appearance.editor"] = "Colours, fonts, shape, motion, density and presets",
        ["appearance.element"] = "Pick colour roles and style for the focused element",
        ["appearance.undo"] = "Step back one appearance change",
        ["appearance.redo"] = "Reapply the appearance change you undid",
        ["appearance.motionlevel"] = "Off, reduced, standard or expressive transitions",
        ["appearance.density"] = "Compact, comfortable or spacious spacing",
        ["appearance.rainbow"] = "Slowly cycle the accent colour through the hues",
        // Destinations reuse the destination descriptions, which are already translated.
        ["go.Build"] = "Build images from a Dockerfile and context folder",
        ["go.Creator"] = "Create a Dockerfile step by step with live checks",
        ["go.Images"] = "Local image inventory, pull, tag, push, export",
        ["go.Containers"] = "Container lifecycle, logs, stats, exec, files",
        ["go.Bake"] = "Bake files: list, inspect and build targets",
        ["go.Security"] = "Vulnerabilities, SBOMs and provenance for local images",
        ["go.Compose"] = "Compose projects, services and source",
        ["go.Canvas"] = "Dockerfile stage graph and runtime topology",
        ["go.Contexts"] = "Engine contexts and plugins",
        ["go.Swarm"] = "Swarm services, stacks, nodes, secrets and configs",
        ["go.Minecraft"] = "Minecraft servers in containers: create, configure, run and back up",
        ["go.Storage"] = "Volumes and networks",
        ["go.Monitor"] = "Live CPU, memory, network and disk charts",
        ["go.System"] = "Disk usage, cleanup and engine information",
        ["go.Registry"] = "Registry sign-in, Docker Hub search and builders",
        ["go.Diagnostics"] = "Engine checks, events and sanitized reports",
        ["go.Tools"] = "Base64, documentation and changelog",
        ["go.Activity"] = "Local version history and notifications",
        ["go.Settings"] = "Appearance, language, accessibility and more",
        ["go.Setup"] = "First-run setup wizard",
    };

    /// <summary>Creator starters, keyed by starter id (command id "creator.starter." + id).</summary>
    private static readonly Dictionary<string, string> Starters = new(StringComparer.Ordinal)
    {
        ["node"] = "Three-stage Node.js Dockerfile with a non-root production runtime",
        ["python"] = "Two-stage Python Dockerfile that installs wheels from requirements.txt",
        ["python-uv"] = "Two-stage Python Dockerfile using uv sync and a virtual environment",
        ["dotnet"] = "Two-stage .NET Dockerfile with the SDK build and the ASP.NET runtime",
        ["go"] = "Two-stage Go Dockerfile with a static build on distroless",
        ["java"] = "Two-stage Java Dockerfile with a Maven build and a JRE runtime",
        ["rust"] = "Two-stage Rust Dockerfile with a release build on Debian slim",
        ["static"] = "Two-stage static site Dockerfile served by unprivileged nginx",
        ["blank"] = "One empty Alpine stage to start from scratch",
    };

    /// <summary>Built-in appearance presets, keyed by preset id (command id "appearance.preset." + id).</summary>
    private static readonly Dictionary<string, string> Presets = new(StringComparer.Ordinal)
    {
        ["builtin.default-dark"] = "The standard dark colours and shapes",
        ["builtin.default-light"] = "The standard light colours and shapes",
        ["builtin.high-contrast"] = "Black surfaces, white text and a yellow accent",
        ["builtin.ocean"] = "Dark teal accent with softer, rounder corners",
        ["builtin.forest"] = "Dark green accent with tighter corners",
        ["builtin.sunset"] = "Light orange accent, round corners and bold headings",
        ["builtin.mono"] = "Dark grey accent with square corners and flat surfaces",
    };

    public const string StarterPrefix = "creator.starter.";
    public const string PresetPrefix = "appearance.preset.";

    /// <summary>The English description for a command id, or null when none is known. Translate it with Loc.T when shown.</summary>
    public static string? For(string id)
    {
        if (Fixed.TryGetValue(id, out var text)) return text;
        if (id.StartsWith(StarterPrefix, StringComparison.Ordinal) && Starters.TryGetValue(id[StarterPrefix.Length..], out text)) return text;
        if (id.StartsWith(PresetPrefix, StringComparison.Ordinal) && Presets.TryGetValue(id[PresetPrefix.Length..], out text)) return text;
        return null;
    }

    /// <summary>Every description text, for the Cantonese coverage check.</summary>
    public static IEnumerable<string> AllTexts => Fixed.Values.Concat(Starters.Values).Concat(Presets.Values).Distinct();

    /// <summary>The second line under a palette title: the description when one exists, otherwise the group.</summary>
    public static string SubLine(string? description, string group) => string.IsNullOrWhiteSpace(description) ? group : description;

    /// <summary>The text a palette row is searched by: both languages of the title and description, plus keywords and group.</summary>
    public static string SearchText(string titleShown, string title, string? descriptionShown, string? description, string keywords, string group) =>
        string.Join(" ", new[] { titleShown, title, descriptionShown ?? "", description ?? "", keywords, group }.Where(s => s.Length > 0).Distinct());
}
