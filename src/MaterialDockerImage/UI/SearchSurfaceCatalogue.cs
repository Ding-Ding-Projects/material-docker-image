using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>How a search surface gets its field.</summary>
public enum SearchSurfaceKind
{
    /// <summary>Direct SearchFilter construction (field plus its own anchored builder).</summary>
    SearchFilter,
    /// <summary>DataTable: its toolbar search is a SearchFilter.</summary>
    DataTable,
    /// <summary>A shared component that builds its search through SearchFilter (ReadOnlySearchText, TextFindBar, ...).</summary>
    Component,
    /// <summary>A text box styled or named like a search that is deliberately not a local filter (requires a reason).</summary>
    Exempt,
}

/// <summary>One registered search surface. <see cref="Key"/> is the first constructor argument as written in source.</summary>
public sealed record SearchSurface(string Id, string Owner, string File, SearchSurfaceKind Kind, string Construct, string Key, string? Reason = null);

/// <summary>A search-field construction found in source.</summary>
public sealed record SearchConstruction(string File, int Line, string Construct, string Key);

/// <summary>
/// Hand-written catalogue of every search and filter field in the app. A completeness guard (tests/UiState)
/// scans the source and fails when a construction is not registered here, or a registered one disappears.
/// To add a search field: build it with SearchFilter (or a component that does), then add one entry here.
/// </summary>
public static class SearchSurfaceCatalogue
{
    private static SearchSurface F(string id, string owner, string file, string key) => new(id, owner, file, SearchSurfaceKind.SearchFilter, "SearchFilter", key);
    private static SearchSurface T(string id, string owner, string file, string key) => new(id, owner, file, SearchSurfaceKind.DataTable, "DataTable", key);
    private static SearchSurface C(string id, string owner, string file, string construct, string key) => new(id, owner, file, SearchSurfaceKind.Component, construct, key);

    public static readonly IReadOnlyList<SearchSurface> All =
    [
        // Shell and window
        F("shell.new-tab", "Window", "MainWindow.xaml.cs", "Search destinations, tools and settings"),
        F("shell.open-tabs", "Window", "MainWindow.xaml.cs", "Search open tabs"),
        F("shell.close-tabs-by-text", "Window", "MainWindow.xaml.cs", "Text to match in tab titles"),
        T("shell.notifications", "Window", "MainWindow.xaml.cs", "Notifications"),
        F("shell.palette", "Command palette", "Shell/CommandPalette.cs", "Type a command, destination or setting"),
        F("shell.output", "Output panel", "Shell/OperationsDock.cs", "Search output"),
        F("shell.element-overrides", "Element editor", "Shell/ElementEditor.cs", "Search customized elements"),
        F("tools.authenticator-accounts", "Authenticator", "Pages/Tools/AuthenticatorAccountsPanel.cs", "Search accounts"),
        // Shared components (each instance gets its own builder)
        C("component.data-table", "DataTable", "UI/DataTable.cs", "SearchFilter", "searchPlaceholder"),
        C("component.data-table.menu", "DataTable context menu", "UI/DataTable.cs", "SearchFilter", "Filter actions"),
        C("component.read-only-text", "ReadOnlySearchText", "Pages/Tools/ReadOnlySearchText.cs", "SearchFilter", "placeholder"),
        C("component.text-find-bar", "TextFindBar", "Pages/Build/TextFindBar.cs", "SearchFilter", "placeholder"),
        C("component.searchable-picker", "SearchablePicker", "Pages/Storage/NetworksView.cs", "SearchFilter", "\"Search \" + name.ToLowerInvariant()"),
        C("component.searchable-text", "SearchableText", "Pages/Images/PageKit.cs", "SearchFilter", "placeholder"),
        C("component.searchable-text.show", "SearchableText", "Pages/Images/PageKit.cs", "SearchableText", "title"),
        C("component.resource-text-viewer", "ResourceTextViewer", "Pages/Compose/ResourceTextViewer.cs", "SearchFilter", "Search"),
        C("component.resource-text-viewer.show", "ResourceTextViewer", "Pages/Compose/ResourceTextViewer.cs", "ResourceTextViewer", "title"),
        C("component.resource-text-viewer.stream", "ResourceTextViewer", "Pages/Compose/ResourceTextViewer.cs", "ResourceTextViewer", "title"),
        // Activity
        T("activity.history", "Activity", "Pages/ActivityPage.cs", "Activity history"),
        C("activity.diff", "Activity", "Pages/Activity/ActivityInspector.cs", "ReadOnlySearchText", "Search this diff"),
        // Bake
        F("bake.targets", "Bake", "Pages/BakePage.cs", "Search targets"),
        C("bake.definition", "Bake", "Pages/Bake/BakeTargetDetail.cs", "ReadOnlySearchText", "Search definition"),
        // Build
        C("build.find", "Build", "Pages/BuildPage.cs", "TextFindBar", "editor.Box"),
        new("build.tag-input", "Build", "Pages/Build/TagInput.cs", SearchSurfaceKind.Exempt, "Field.Search", "",
            "Tag entry that only borrows the search field style; it adds image tags and filters nothing."),
        F("build.call-targets", "Build", "Pages/Build/BuildCallResultView.cs", "Search targets"),
        F("build.call-arguments", "Build", "Pages/Build/BuildCallResultView.cs", "Search arguments, secrets and SSH"),
        C("build.log", "Build", "Pages/Build/BuildTimelineView.cs", "ReadOnlySearchText", "Search build log"),
        T("build.history", "Build", "Pages/Build/BuildHistoryCard.cs", "Recent builds"),
        // Canvas
        F("canvas.objects", "Canvas", "Pages/CanvasPage.cs", "Search objects"),
        C("canvas.source", "Canvas", "Pages/CanvasPage.cs", "TextFindBar", "view.Box"),
        // Compose
        F("compose.projects", "Compose", "Pages/Compose/ComposeProjectList.cs", "Search projects"),
        T("compose.services", "Compose", "Pages/Compose/ComposeProjectPanel.cs", "Compose services"),
        C("compose.config-invalid", "Compose", "Pages/Compose/ComposeProjectPanel.cs", "ResourceTextViewer.Show", "Compose config is invalid"),
        C("compose.config-resolved", "Compose", "Pages/Compose/ComposeProjectPanel.cs", "ResourceTextViewer.Show", "\"Resolved config · \" + entry!.Name"),
        C("compose.logs", "Compose", "Pages/Compose/ComposeProjectPanel.cs", "ResourceTextViewer.ShowStream", "\"Logs · \" + (service ?? entry.Name)"),
        C("compose.tool-output", "Compose", "Pages/Compose/ComposeProjectPanel.Tools.cs", "ResourceTextViewer.Show", "title"),
        C("compose.watch", "Compose", "Pages/Compose/ComposeProjectPanel.Tools.cs", "ResourceTextViewer.ShowStream", "\"Watch · \" + entry!.Name"),
        T("compose.images", "Compose", "Pages/Compose/ComposeProjectPanel.Tools.cs", "Compose images"),
        T("compose.events", "Compose", "Pages/Compose/ComposeEventsViewer.cs", "Compose events"),
        F("compose.source", "Compose", "Pages/Compose/ComposeSourceEditor.cs", "Search source"),
        // Containers
        T("containers.list", "Containers", "Pages/ContainersPage.cs", "Containers"),
        C("containers.exec-output", "Containers", "Pages/Containers/ContainerExec.cs", "SearchableText", "Command output"),
        C("containers.processes", "Containers", "Pages/Containers/ContainerInspector.Tabs.cs", "SearchableText", "Processes"),
        C("containers.filesystem-changes", "Containers", "Pages/Containers/ContainerInspector.Tabs.cs", "SearchableText", "Filesystem changes"),
        C("containers.configuration", "Containers", "Pages/Containers/ContainerInspector.cs", "SearchableText", "Container configuration"),
        F("containers.logs", "Containers", "Pages/Containers/ContainerLogs.cs", "Search logs"),
        F("containers.run-image-picker", "Containers", "Pages/Containers/ContainersPage.Actions.cs", "Search local images"),
        // Contexts
        F("contexts.list", "Contexts", "Pages/ContextsPage.cs", "Search contexts"),
        F("contexts.plugins", "Contexts", "Pages/Contexts/ContextPluginsPanel.cs", "Search plugins"),
        C("contexts.inspect", "Contexts", "Pages/Contexts/ContextActions.cs", "ResourceTextViewer.Show", "\"Context \" + entry.Name"),
        // Creator
        F("creator.instructions", "Dockerfile Creator", "Pages/Creator/CreatorPage.Editor.cs", "Search instructions"),
        F("creator.stages", "Dockerfile Creator", "Pages/Creator/CreatorPage.Stages.cs", "Search stages"),
        F("creator.ignore-paths", "Dockerfile Creator", "Pages/Creator/CreatorPage.Ignore.cs", "Search checked paths"),
        C("creator.compose-preview", "Dockerfile Creator", "Pages/Creator/CreatorPage.Ignore.cs", "TextFindBar", "composeView.Box"),
        C("creator.preview", "Dockerfile Creator", "Pages/Creator/CreatorPage.Preview.cs", "TextFindBar", "preview.Box"),
        // Diagnostics
        F("diagnostics.checks", "Diagnostics", "Pages/DiagnosticsPage.cs", "Search checks"),
        F("diagnostics.events", "Diagnostics", "Pages/Diagnostics/EventsPanel.cs", "Search events"),
        C("diagnostics.report", "Diagnostics", "Pages/Diagnostics/DiagnosticReport.cs", "ReadOnlySearchText", "Search report"),
        // Images
        T("images.list", "Images", "Pages/ImagesPage.cs", "Images"),
        F("images.layers", "Images", "Pages/Images/ImageLayersTab.cs", "Search layers"),
        F("images.compare", "Images", "Pages/Images/ImageCompareTab.cs", "Search differences"),
        C("images.compare-picker", "Images", "Pages/Images/ImageCompareTab.cs", "SearchablePicker", "Local images"),
        C("images.scout-compare", "Images", "Pages/Images/ImageCompareTab.cs", "SearchableText", "Docker Scout compare"),
        C("images.scout-quickview", "Images", "Pages/Images/ImageScoutTab.cs", "SearchableText", "Docker Scout quickview"),
        C("images.inspect", "Images", "Pages/Images/ImageInspector.cs", "PageKit.ShowText", "\"Inspect \" + image.Name"),
        C("images.manifest", "Images", "Pages/Images/ImagePlatformsTab.cs", "PageKit.ShowText", "\"Manifest of \" + reference"),
        F("images.platforms", "Images", "Pages/Images/ImagePlatformsTab.cs", "Search platforms and digests"),
        T("images.sbom", "Images", "Pages/Images/ImageSbomTab.cs", "SBOM packages"),
        // Minecraft
        T("minecraft.servers", "Minecraft", "Pages/MinecraftPage.cs", "Minecraft servers"),
        C("minecraft.logs", "Minecraft", "Pages/MinecraftPage.cs", "ResourceTextViewer.ShowStream", "\"Logs: \" + s.Name"),
        C("minecraft.console", "Minecraft", "Pages/Minecraft/MinecraftConsoleTab.cs", "ResourceTextViewer", "Server console"),
        F("editor.image-picker", "Editors", "Pages/Suggestions/EditorValuePickers.cs", "Search local images"),
        C("minecraft.replies", "Minecraft", "Pages/Minecraft/MinecraftWorldTab.cs", "ReadOnlySearchText", "Search replies"),
        T("minecraft.backups", "Minecraft", "Pages/Minecraft/MinecraftBackupsTab.cs", "Backups"),
        T("minecraft.files", "Minecraft", "Pages/Minecraft/MinecraftFilesTab.cs", "Server files"),
        T("minecraft.players", "Minecraft", "Pages/Minecraft/MinecraftPlayersTab.cs", "Players"),
        F("minecraft.versions", "Minecraft", "Pages/Minecraft/MinecraftVersionPicker.cs", "Search versions"),
        // Monitor
        F("monitor.containers", "Monitor", "Pages/MonitorPage.cs", "Search containers"),
        // Registry
        F("registry.accounts", "Registry", "Pages/Registry/RegistryAccounts.cs", "Search signed-in registries"),
        T("registry.builders", "Registry", "Pages/Registry/BuildersPanel.cs", "Builders"),
        C("registry.builder-output", "Registry", "Pages/Registry/BuildersPanel.cs", "ReadOnlySearchText", "Search builder output"),
        T("registry.hub-results", "Registry", "Pages/Registry/HubSearch.cs", "Docker Hub results"),
        new("registry.hub-term", "Registry", "Pages/Registry/HubSearch.cs", SearchSurfaceKind.Exempt, "Ui.Input", "",
            "Docker Hub search term sent to docker search as a remote query, not a local filter; its results table has its own search and builder."),
        // Security
        T("security.images", "Security", "Pages/SecurityPage.cs", "Analyzed images"),
        C("security.scout-quickview", "Security", "Pages/Security/SecurityImagePanel.cs", "SearchableText", "Docker Scout quickview"),
        T("security.findings", "Security", "Pages/Security/ScoutFindingsView.cs", "Vulnerability findings"),
        // Settings
        F("settings.search", "Settings", "Pages/SettingsPage.cs", "Search settings"),
        F("settings.shortcuts", "Settings", "Pages/Settings/SettingsSections.cs", "Search shortcuts"),
        F("settings.fonts", "Settings", "Pages/Settings/AppearanceFontPicker.cs", "mono ? \"Search monospace and code fonts\" : \"Search installed fonts\""),
        T("settings.locks", "Settings", "Pages/Settings/SettingsLocks.cs", "Locks manager"),
        F("settings.colour-roles", "Settings", "Pages/Settings/AppearanceEditor.cs", "Search colour roles"),
        F("settings.appearance-studio", "Settings", "Pages/Settings/AppearanceStudio.cs", "Search appearance"),
        // Setup
        F("setup.contexts", "Setup", "Pages/Setup/SetupSteps.cs", "Search contexts"),
        // Storage
        T("storage.networks", "Storage", "Pages/Storage/NetworksView.cs", "Networks"),
        C("storage.network-inspect", "Storage", "Pages/Storage/NetworksView.cs", "ResourceTextViewer.Show", "\"Network \" + n.Name"),
        C("storage.network-connect", "Storage", "Pages/Storage/NetworksView.cs", "SearchablePicker", "Running containers"),
        C("storage.network-disconnect", "Storage", "Pages/Storage/NetworksView.cs", "SearchablePicker", "Connected containers"),
        T("storage.volumes", "Storage", "Pages/Storage/VolumesView.cs", "Volumes"),
        C("storage.volume-inspect", "Storage", "Pages/Storage/VolumesView.cs", "ResourceTextViewer.Show", "\"Volume \" + v.Name"),
        // Swarm
        T("swarm.services", "Swarm", "Pages/Swarm/SwarmServicesView.cs", "Swarm services"),
        C("swarm.service-logs", "Swarm", "Pages/Swarm/SwarmServicesView.cs", "ResourceTextViewer.ShowStream", "\"Logs · \" + s.Name"),
        F("swarm.tasks", "Swarm", "Pages/Swarm/SwarmServiceInspector.cs", "Search tasks"),
        T("swarm.nodes", "Swarm", "Pages/Swarm/SwarmNodesView.cs", "Swarm nodes"),
        T("swarm.stacks", "Swarm", "Pages/Swarm/SwarmStacksView.cs", "Swarm stacks"),
        T("swarm.stack-services", "Swarm", "Pages/Swarm/SwarmStacksView.cs", "Stack services"),
        T("swarm.secrets", "Swarm", "Pages/Swarm/SwarmSecretsView.cs", "Swarm secrets"),
        T("swarm.configs", "Swarm", "Pages/Swarm/SwarmConfigsView.cs", "Swarm configs"),
        C("swarm.config-content", "Swarm", "Pages/Swarm/SwarmConfigsView.cs", "ResourceTextViewer.Show", "\"Config \" + c.Name"),
        C("swarm.inspect", "Swarm", "Pages/Swarm/SwarmTabView.cs", "ResourceTextViewer.Show", "title"),
        // System
        F("system.engine-facts", "System", "Pages/System/EngineInfoCard.cs", "Search engine facts"),
        // Tools
        T("tools.support-tickets", "Tools", "Shell/SupportDeskUi.cs", "Support tickets"),
        F("tools.docs", "Tools", "Pages/Tools/DocsBrowser.cs", "Search documentation"),
        F("tools.changelog", "Tools", "Pages/Tools/ChangelogView.cs", "Search the changelog"),
        // The regex builder's own lists: each filter carries its own builder, which opens as a second popup beside the first.
        F("builder.library-filter", "Regex builder", "UI/SearchBuilderPopover.cs", "Filter the library"),
        F("builder.my-patterns-filter", "Regex builder", "UI/SearchBuilderPopover.cs", "Filter my patterns"),
        F("builder.unicode-blocks-filter", "Regex builder", "UI/SearchBuilderPopover.Subject.cs", "Filter the blocks"),
        new("builder.grep-folder", "Regex builder", "UI/SearchBuilderPopover.Tools.cs", SearchSurfaceKind.Exempt, "Ui.Input", "",
            "A folder path for the folder search, not a filter over a list; the results of the search are listed in a grid beside it."),
    ];

    /// <summary>Constructions that create a search field. Each yields the construct name and the argument list start.</summary>
    private static readonly (string Construct, Regex Pattern)[] Detectors =
    [
        ("SearchFilter", new(@"new\s+SearchFilter\s*\(|\bSearchFilter\s+\w+\s*=\s*new\s*\(", RegexOptions.NonBacktracking)),
        ("DataTable", new(@"new\s+DataTable<[^>]+>\s*\(|\bDataTable<[^>]+>\s+\w+\s*=\s*new\s*\(", RegexOptions.NonBacktracking)),
        ("ReadOnlySearchText", new(@"new\s+ReadOnlySearchText\s*\(|\bReadOnlySearchText\s+\w+\s*=\s*new\s*\(", RegexOptions.NonBacktracking)),
        ("TextFindBar", new(@"new\s+TextFindBar\s*\(|\bTextFindBar\s+\w+\s*=\s*new\s*\(", RegexOptions.NonBacktracking)),
        ("SearchablePicker", new(@"new\s+SearchablePicker\s*\(|\bSearchablePicker\s+\w+\s*=\s*new\s*\(", RegexOptions.NonBacktracking)),
        ("SearchableText", new(@"new\s+SearchableText\s*\(|\bSearchableText\s+\w+\s*=\s*new\s*\(", RegexOptions.NonBacktracking)),
        ("ResourceTextViewer", new(@"new\s+ResourceTextViewer\s*\(|\bResourceTextViewer\s+\w+\s*=\s*new\s*\(", RegexOptions.NonBacktracking)),
        ("ResourceTextViewer.Show", new(@"\bResourceTextViewer\.Show\s*\(", RegexOptions.NonBacktracking)),
        ("ResourceTextViewer.ShowStream", new(@"\bResourceTextViewer\.ShowStream\s*\(", RegexOptions.NonBacktracking)),
        ("PageKit.ShowText", new(@"\bPageKit\.ShowText\s*\(", RegexOptions.NonBacktracking)),
        // Raw search-looking text boxes that bypass SearchFilter: must be registered as Exempt with a reason.
        ("Field.Search", new(@"""Field\.Search""\s*\)", RegexOptions.NonBacktracking)),
        ("Ui.Input", new(@"Ui\.Input\s*\([^;\n]*""[^""\n]*\b(?:[Ss]earch|[Ff]ilter|[Ff]ind)\b", RegexOptions.NonBacktracking)),
    ];

    /// <summary>Files that never construct a user-facing field (the field and builder implementation themselves).</summary>
    private static bool Skipped(string relative) =>
        relative == "UI/SearchFilter.cs" || relative.StartsWith("UI/RegexBuilder", StringComparison.Ordinal) || relative == "UI/SearchSurfaceCatalogue.cs" ||
        relative.StartsWith("obj/", StringComparison.Ordinal) || relative.StartsWith("bin/", StringComparison.Ordinal);

    /// <summary>Scans every .cs file under the app source folder for search-field constructions.</summary>
    public static List<SearchConstruction> Scan(string sourceRoot)
    {
        var found = new List<SearchConstruction>();
        foreach (var path in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(sourceRoot, path).Replace('\\', '/');
            if (Skipped(relative)) continue;
            var text = File.ReadAllText(path);
            foreach (var (construct, pattern) in Detectors)
                foreach (Match m in pattern.Matches(text))
                {
                    if (IsDeclaration(text, m.Index)) continue;
                    var line = 1 + text.AsSpan(0, m.Index).Count('\n');
                    var key = construct is "Field.Search" or "Ui.Input" ? "" : FirstArgument(text, m.Index + m.Length);
                    found.Add(new(relative, line, construct, key));
                }
        }
        return found;
    }

    /// <summary>Skips class declarations and comments (for example "public sealed class SearchablePicker").</summary>
    private static bool IsDeclaration(string text, int index)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
        var prefix = text[lineStart..index].TrimStart();
        return prefix.StartsWith("//", StringComparison.Ordinal) || prefix.StartsWith("///", StringComparison.Ordinal) || prefix.StartsWith('*');
    }

    /// <summary>First argument text after an opening parenthesis, whitespace collapsed; a plain string literal loses its quotes.</summary>
    public static string FirstArgument(string text, int afterParen)
    {
        var depth = 0; var i = afterParen; var inString = false; var verbatim = false;
        for (; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (verbatim) { if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') i++; else inString = false; } }
                else if (c == '\\') i++;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') { inString = true; verbatim = i > 0 && text[i - 1] == '@'; continue; }
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') { if (depth == 0) break; depth--; }
            else if (c == ',' && depth == 0) break;
        }
        var arg = Regex.Replace(text[afterParen..i].Trim(), @"\s+", " ");
        if (arg.Length >= 2 && arg[0] == '"' && arg[^1] == '"' && arg.IndexOf('"', 1) == arg.Length - 1) arg = arg[1..^1];
        return arg;
    }

    /// <summary>
    /// Compares the scan with the catalogue. Returns problems: unregistered constructions, stale entries,
    /// duplicate ids, and exemptions without a reason. An empty list means the catalogue is complete.
    /// </summary>
    public static List<string> Verify(IReadOnlyList<SearchConstruction> found)
    {
        var problems = new List<string>();
        foreach (var dup in All.GroupBy(s => s.Id).Where(g => g.Count() > 1)) problems.Add("Duplicate catalogue id " + dup.Key);
        foreach (var s in All.Where(s => s.Kind == SearchSurfaceKind.Exempt && string.IsNullOrWhiteSpace(s.Reason))) problems.Add("Exempt surface without a reason: " + s.Id);
        var remaining = All.ToList();
        foreach (var c in found)
        {
            var raw = c.Construct is "Field.Search" or "Ui.Input";
            var match = remaining.FirstOrDefault(s => s.File == c.File && s.Key == c.Key && (raw ? s.Kind == SearchSurfaceKind.Exempt && s.Construct == c.Construct : s.Kind != SearchSurfaceKind.Exempt && s.Construct == c.Construct));
            if (match == null)
            {
                problems.Add(raw
                    ? $"{c.File}:{c.Line} builds a search-like text box without SearchFilter ({c.Construct}). Use SearchFilter, or register it as Exempt with a reason."
                    : $"{c.File}:{c.Line} constructs {c.Construct}(\"{c.Key}\") which is not registered in SearchSurfaceCatalogue.");
                continue;
            }
            remaining.Remove(match);
        }
        foreach (var s in remaining) problems.Add($"Catalogue entry {s.Id} ({s.File}, {s.Construct} \"{s.Key}\") no longer exists in source.");
        return problems;
    }
}
