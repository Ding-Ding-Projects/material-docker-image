using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>One detected fact with the files that prove it.</summary>
public sealed record ProjectFinding(string Category, string Label, IReadOnlyList<string> Evidence, string Detail = "");

/// <summary>What <see cref="ProjectDetector.Detect"/> learned about a folder, in the spirit of docker init.</summary>
public sealed class ProjectDetection
{
    public string Folder { get; init; } = "";
    public string Name { get; set; } = "app";
    public List<ProjectFinding> Findings { get; } = new();
    /// <summary>node, python, go, dotnet, rust, java, static, or empty when nothing was recognised.</summary>
    public string Language { get; set; } = "";
    /// <summary>Human label of the suggestion, for example "Node.js (npm)".</summary>
    public string Suggestion { get; set; } = "";
    /// <summary>Language runtime version used for the base image, for example 22, 3.12, 1.23, 8.0.</summary>
    public string Version { get; set; } = "";
    public string Port { get; set; } = "8080";
    /// <summary>Entry file, module, binary or assembly name.</summary>
    public string Entry { get; set; } = "";
    /// <summary>npm, yarn, pnpm, pip, uv, poetry, maven, gradle.</summary>
    public string PackageManager { get; set; } = "";
    public string Framework { get; set; } = "";
    public bool HasBuildScript { get; set; }
    public bool HasStartScript { get; set; }
    public bool IsWeb { get; set; } = true;
    /// <summary>Go: package path to build, for example ./cmd/server or ".".</summary>
    public string BuildPath { get; set; } = ".";
    public string ExistingDockerfile { get; set; } = "";
    public string ExistingCompose { get; set; } = "";
    public string ExistingDockerignore { get; set; } = "";
    public bool Recognised => Language.Length > 0;
}

/// <summary>
/// Pure, read-only project detection. Reads only small manifest files (at most 1 MiB each, a bounded number of
/// directories) and never executes anything from the folder.
/// </summary>
public static class ProjectDetector
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);
    private const long MaxManifestBytes = 1024 * 1024;
    private static readonly string[] SkippedDirectories = ["node_modules", ".git", "bin", "obj", "target", ".venv", "venv", "dist", "build", "vendor", "__pycache__"];

    public static ProjectDetection Detect(string folder)
    {
        var full = Path.GetFullPath(folder);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException("The folder does not exist: " + full);
        var d = new ProjectDetection { Folder = full, Name = ServiceName(Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))) };
        bool Has(string name) => File.Exists(Path.Combine(full, name));

        foreach (var name in new[] { "Dockerfile", "Containerfile" })
            if (Has(name)) { d.ExistingDockerfile = name; d.Findings.Add(new("Existing", "Existing " + name, [name], "Applying a suggestion does not change it; writing files asks before overwriting.")); break; }
        foreach (var name in new[] { "compose.yaml", "compose.yml", "docker-compose.yaml", "docker-compose.yml" })
            if (Has(name)) { d.ExistingCompose = name; d.Findings.Add(new("Existing", "Existing Compose file", [name])); break; }
        if (Has(".dockerignore")) { d.ExistingDockerignore = ".dockerignore"; d.Findings.Add(new("Existing", "Existing .dockerignore", [".dockerignore"])); }

        if (Has("package.json")) DetectNode(d, full);
        else if (Has("pyproject.toml") || Has("requirements.txt") || Has("uv.lock") || Has("poetry.lock") || Has("manage.py")) DetectPython(d, full);
        else if (Has("go.mod")) DetectGo(d, full);
        else if (Has("Cargo.toml")) DetectRust(d, full);
        else if (Has("pom.xml") || Has("build.gradle") || Has("build.gradle.kts")) DetectJava(d, full);
        else if (FindFiles(full, "*.csproj", 3).Count > 0 || FindFiles(full, "*.sln", 1).Count > 0) DetectDotNet(d, full);
        else if (Has("index.html"))
        {
            d.Language = "static"; d.Suggestion = "Static site (nginx)"; d.Port = "8080";
            d.Findings.Add(new("Language", "Static site", ["index.html"], "Served by nginx-unprivileged on port 8080."));
        }
        return d;
    }

    // ------------------------------------------------------------------ languages

    private static void DetectNode(ProjectDetection d, string root)
    {
        d.Language = "node"; d.Port = "3000";
        var evidence = new List<string> { "package.json" };
        try
        {
            using var json = JsonDocument.Parse(ReadSmall(Path.Combine(root, "package.json")), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var rootElement = json.RootElement;
            if (rootElement.TryGetProperty("scripts", out var scripts) && scripts.ValueKind == JsonValueKind.Object)
            {
                d.HasStartScript = scripts.TryGetProperty("start", out _);
                d.HasBuildScript = scripts.TryGetProperty("build", out _);
            }
            if (rootElement.TryGetProperty("main", out var main) && main.ValueKind == JsonValueKind.String && SafeRelative(main.GetString()!)) d.Entry = main.GetString()!;
            if (rootElement.TryGetProperty("engines", out var engines) && engines.ValueKind == JsonValueKind.Object && engines.TryGetProperty("node", out var node) && node.ValueKind == JsonValueKind.String)
            {
                var major = Regex.Match(node.GetString()!, @"\d+", RegexOptions.None, RegexTimeout);
                if (major.Success) { d.Version = major.Value; d.Findings.Add(new("Version", "Node.js " + major.Value, ["package.json"], "From engines.node: " + node.GetString())); }
            }
            foreach (var section in new[] { "dependencies", "devDependencies" })
            {
                if (!rootElement.TryGetProperty(section, out var deps) || deps.ValueKind != JsonValueKind.Object) continue;
                foreach (var (package, framework, port) in new[] { ("next", "Next.js", "3000"), ("express", "Express", "3000"), ("fastify", "Fastify", "3000"), ("@nestjs/core", "NestJS", "3000"), ("vite", "Vite", "") })
                    if (d.Framework.Length == 0 && deps.TryGetProperty(package, out _)) { d.Framework = framework; if (port.Length > 0) d.Port = port; }
            }
            if (rootElement.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String) d.Name = ServiceName(name.GetString()!);
        }
        catch (JsonException) { d.Findings.Add(new("Warning", "package.json is not valid JSON", ["package.json"], "Defaults were used.")); }
        if (File.Exists(Path.Combine(root, "pnpm-lock.yaml"))) { d.PackageManager = "pnpm"; evidence.Add("pnpm-lock.yaml"); }
        else if (File.Exists(Path.Combine(root, "yarn.lock"))) { d.PackageManager = "yarn"; evidence.Add("yarn.lock"); }
        else { d.PackageManager = "npm"; if (File.Exists(Path.Combine(root, "package-lock.json"))) evidence.Add("package-lock.json"); }
        if (d.Version.Length == 0)
        {
            var nvmrc = Path.Combine(root, ".nvmrc");
            var major = File.Exists(nvmrc) ? Regex.Match(ReadSmall(nvmrc), @"\d+", RegexOptions.None, RegexTimeout) : Match.Empty;
            if (major.Success) { d.Version = major.Value; d.Findings.Add(new("Version", "Node.js " + major.Value, [".nvmrc"])); }
            else d.Version = "22";
        }
        if (d.Entry.Length == 0) d.Entry = File.Exists(Path.Combine(root, "server.js")) ? "server.js" : File.Exists(Path.Combine(root, "index.js")) ? "index.js" : "";
        d.Suggestion = "Node.js (" + d.PackageManager + ")";
        d.Findings.Insert(0, new("Language", "Node.js", evidence, $"Package manager {d.PackageManager}" + (evidence.Count == 1 ? " (no lockfile found; npm install is used instead of npm ci)" : "")));
        if (d.Framework.Length > 0) d.Findings.Add(new("Framework", d.Framework, ["package.json"]));
        d.Findings.Add(new("Scripts", d.HasStartScript ? "npm start script" : d.Entry.Length > 0 ? "Entry " + d.Entry : "No start script", ["package.json"],
            (d.HasBuildScript ? "A build script exists, so a build stage is added. " : "") + (d.HasStartScript ? "The container runs npm start." : d.Entry.Length > 0 ? "The container runs node " + d.Entry + "." : "Set the command in the CMD card.")));
    }

    private static void DetectPython(ProjectDetection d, string root)
    {
        d.Language = "python"; d.Port = "8000";
        var evidence = new List<string>();
        var text = new StringBuilder();
        foreach (var name in new[] { "pyproject.toml", "requirements.txt", "uv.lock", "poetry.lock", "manage.py", ".python-version" })
            if (File.Exists(Path.Combine(root, name))) { evidence.Add(name); if (name is "pyproject.toml" or "requirements.txt") text.Append(ReadSmall(Path.Combine(root, name))).Append('\n'); }
        d.PackageManager = evidence.Contains("uv.lock") ? "uv" : evidence.Contains("poetry.lock") ? "poetry" : "pip";
        var all = text.ToString().ToLowerInvariant();
        var requires = Regex.Match(all, @"requires-python\s*=\s*[""'][^0-9]*(3\.\d+)", RegexOptions.None, RegexTimeout);
        var pinned = File.Exists(Path.Combine(root, ".python-version")) ? Regex.Match(ReadSmall(Path.Combine(root, ".python-version")), @"3\.\d+", RegexOptions.None, RegexTimeout) : Match.Empty;
        d.Version = pinned.Success ? pinned.Value : requires.Success ? requires.Groups[1].Value : "3.13";
        if (pinned.Success || requires.Success) d.Findings.Add(new("Version", "Python " + d.Version, [pinned.Success ? ".python-version" : "pyproject.toml"]));
        bool Uses(string package) => Regex.IsMatch(all, $@"(^|[\s""',\[])({Regex.Escape(package)})\s*([<>=!~\[;""',]|$)", RegexOptions.Multiline, RegexTimeout);
        var module = File.Exists(Path.Combine(root, "app", "main.py")) ? "app.main" : File.Exists(Path.Combine(root, "main.py")) ? "main" : File.Exists(Path.Combine(root, "app.py")) ? "app" : "app";
        if (Uses("fastapi")) { d.Framework = "FastAPI"; d.Entry = module; }
        else if (Uses("flask")) { d.Framework = "Flask"; d.Entry = module; }
        else if (Uses("django") || evidence.Contains("manage.py"))
        {
            d.Framework = "Django";
            var wsgi = FindFiles(root, "wsgi.py", 2).FirstOrDefault();
            d.Entry = wsgi != null ? Path.GetFileName(Path.GetDirectoryName(wsgi)!) : "project";
        }
        else d.Entry = module;
        d.Suggestion = "Python (" + d.PackageManager + (d.Framework.Length > 0 ? ", " + d.Framework : "") + ")";
        d.Findings.Insert(0, new("Language", "Python", evidence, "Dependencies installed with " + d.PackageManager + "."));
        if (d.Framework.Length > 0) d.Findings.Add(new("Framework", d.Framework, evidence.Where(e => e is "pyproject.toml" or "requirements.txt" or "manage.py").ToList(), "Runs on port 8000."));
        d.HasStartScript = Uses("gunicorn");
    }

    private static void DetectGo(ProjectDetection d, string root)
    {
        d.Language = "go";
        var mod = ReadSmall(Path.Combine(root, "go.mod"));
        var module = Regex.Match(mod, @"^module\s+(\S+)", RegexOptions.Multiline, RegexTimeout);
        var version = Regex.Match(mod, @"^go\s+(\d+\.\d+)", RegexOptions.Multiline, RegexTimeout);
        d.Version = version.Success ? version.Groups[1].Value : "1.24";
        var evidence = new List<string> { "go.mod" };
        if (File.Exists(Path.Combine(root, "go.sum"))) evidence.Add("go.sum");
        var binary = module.Success ? module.Groups[1].Value.TrimEnd('/').Split('/').Last() : "server";
        var cmd = Path.Combine(root, "cmd");
        var commands = Directory.Exists(cmd) ? SafeDirectories(cmd).Where(x => File.Exists(Path.Combine(x, "main.go"))).Select(Path.GetFileName).OfType<string>().OrderBy(x => x, StringComparer.Ordinal).ToList() : new List<string>();
        if (commands.Count > 0) { d.BuildPath = "./cmd/" + commands[0]; binary = commands[0]; evidence.Add("cmd/" + commands[0] + "/main.go"); }
        else if (File.Exists(Path.Combine(root, "main.go"))) evidence.Add("main.go");
        d.Entry = ServiceName(binary);
        d.Suggestion = "Go " + d.Version;
        d.Findings.Insert(0, new("Language", "Go " + d.Version, evidence, (module.Success ? "Module " + module.Groups[1].Value + ". " : "") + "Builds " + d.BuildPath + " into a static binary."));
    }

    private static void DetectRust(ProjectDetection d, string root)
    {
        d.Language = "rust";
        var toml = ReadSmall(Path.Combine(root, "Cargo.toml"));
        var name = Regex.Match(toml, @"\[package\][^\[]*?\bname\s*=\s*""([^""]+)""", RegexOptions.Singleline, RegexTimeout);
        var rust = Regex.Match(toml, @"\brust-version\s*=\s*""(\d+\.\d+)", RegexOptions.None, RegexTimeout);
        d.Entry = name.Success ? name.Groups[1].Value : "app";
        d.Version = rust.Success ? rust.Groups[1].Value : "1";
        var evidence = new List<string> { "Cargo.toml" };
        if (File.Exists(Path.Combine(root, "Cargo.lock"))) evidence.Add("Cargo.lock");
        d.Suggestion = "Rust (" + d.Entry + ")";
        d.Findings.Insert(0, new("Language", "Rust", evidence, "Package " + d.Entry + (rust.Success ? ", rust-version " + d.Version : "") + "."));
    }

    private static void DetectJava(ProjectDetection d, string root)
    {
        d.Language = "java";
        var maven = File.Exists(Path.Combine(root, "pom.xml"));
        d.PackageManager = maven ? "maven" : "gradle";
        var file = maven ? "pom.xml" : File.Exists(Path.Combine(root, "build.gradle.kts")) ? "build.gradle.kts" : "build.gradle";
        var text = ReadSmall(Path.Combine(root, file));
        var version = Regex.Match(text, maven ? @"<(java\.version|maven\.compiler\.release|maven\.compiler\.target|release)>\s*(\d+)" : @"(languageVersion\s*=\s*JavaLanguageVersion\.of\(|sourceCompatibility\s*=\s*(JavaVersion\.VERSION_)?['""]?)(\d+)", RegexOptions.None, RegexTimeout);
        d.Version = version.Success ? version.Groups[maven ? 2 : 3].Value : "21";
        if (version.Success) d.Findings.Add(new("Version", "Java " + d.Version, [file]));
        if (Regex.IsMatch(text, "spring-boot", RegexOptions.None, RegexTimeout)) d.Framework = "Spring Boot";
        d.Suggestion = "Java (" + (maven ? "Maven" : "Gradle") + ")";
        d.Findings.Insert(0, new("Language", "Java", [file], "Built with " + (maven ? "Maven" : "Gradle") + "."));
        if (d.Framework.Length > 0) d.Findings.Add(new("Framework", d.Framework, [file]));
    }

    private static void DetectDotNet(ProjectDetection d, string root)
    {
        d.Language = "dotnet";
        var projects = FindFiles(root, "*.csproj", 3);
        var solutions = FindFiles(root, "*.sln", 1);
        var evidence = solutions.Concat(projects).Select(p => Path.GetRelativePath(root, p).Replace('\\', '/')).Take(4).ToList();
        string? chosen = null; var web = false; var framework = "";
        foreach (var project in projects)
        {
            var text = ReadSmall(project);
            var isWeb = Regex.IsMatch(text, @"Sdk\s*=\s*""Microsoft\.NET\.Sdk\.(Web|Worker)""", RegexOptions.None, RegexTimeout) && text.Contains("Sdk.Web", StringComparison.Ordinal);
            var tfm = Regex.Match(text, @"<TargetFrameworks?>\s*net(\d+\.\d+)", RegexOptions.None, RegexTimeout);
            if (chosen == null || (isWeb && !web)) { chosen = project; web = isWeb; framework = tfm.Success ? tfm.Groups[1].Value : framework; }
            var assembly = Regex.Match(text, @"<AssemblyName>\s*([^<\s]+)", RegexOptions.None, RegexTimeout);
            if (project == chosen) d.Entry = assembly.Success ? assembly.Groups[1].Value : Path.GetFileNameWithoutExtension(project);
        }
        d.Version = framework.Length > 0 ? framework : "9.0";
        d.IsWeb = web; d.Port = web ? "8080" : "";
        d.BuildPath = chosen == null ? "." : Path.GetRelativePath(root, chosen).Replace('\\', '/');
        if (chosen == null) d.Entry = solutions.Count > 0 ? Path.GetFileNameWithoutExtension(solutions[0]) : "App";
        d.Suggestion = (web ? "ASP.NET " : ".NET ") + d.Version;
        d.Findings.Insert(0, new("Language", web ? "ASP.NET Core" : ".NET", evidence, $"Target framework net{d.Version}; publishes {d.BuildPath} as {d.Entry}.dll."));
    }

    // ------------------------------------------------------------------ suggestion

    private static DockerInstruction Run(string command, params RunMount[] mounts) => new() { Kind = InstructionKind.Run, Command = command, Mounts = mounts.ToList() };
    private static RunMount CacheMount(string target, string sharing = "") => new() { Type = RunMounts.Cache, Target = target, Sharing = sharing };
    private static RunMount BindMount(string file) => new() { Type = RunMounts.Bind, Source = file, Target = file };
    private static DockerInstruction Copy(string destination, params string[] sources) => new() { Kind = InstructionKind.Copy, Sources = sources.ToList(), Destination = destination };
    private static DockerInstruction CopyFrom(string from, string source, string destination, string chown = "") => new() { Kind = InstructionKind.Copy, From = from, Sources = { source }, Destination = destination, Chown = chown };
    private static DockerInstruction Workdir(string path) => new() { Kind = InstructionKind.Workdir, Value = path };
    private static DockerInstruction Env(params (string, string)[] pairs) => new() { Kind = InstructionKind.Env, Pairs = pairs.Select(p => new NameValue(p.Item1, p.Item2)).ToList() };
    private static DockerInstruction Arg(string name, string value = "") => new() { Kind = InstructionKind.Arg, Name = name, HasValue = value.Length > 0, Value = value };
    private static DockerInstruction User(string user) => new() { Kind = InstructionKind.User, Value = user };
    private static DockerInstruction Expose(string port) => new() { Kind = InstructionKind.Expose, Port = port, Protocol = "tcp" };
    private static DockerInstruction Exec(InstructionKind kind, params string[] args) => new() { Kind = kind, ExecForm = true, Arguments = args.ToList() };
    private static DockerfileStage Stage(string name, string image, params DockerInstruction[] instructions) => new() { Name = name, BaseImage = image, Instructions = instructions.Where(i => i != null).ToList() };
    private const string AddUser = "useradd --system --uid 10001 --no-create-home app";

    /// <summary>A complete draft for the detected project. Never uses heredocs or line continuations.</summary>
    public static DockerfileDocument Suggest(ProjectDetection d)
    {
        var doc = new DockerfileDocument { SyntaxHeader = true, Syntax = DockerfileModel.DefaultSyntax, Origin = "Detected " + (d.Suggestion.Length > 0 ? d.Suggestion : "project") };
        switch (d.Language)
        {
            case "node": SuggestNode(d, doc); break;
            case "python": SuggestPython(d, doc); break;
            case "go":
                doc.Stages.Add(Stage("build", $"golang:{d.Version}-alpine", Workdir("/src"),
                    Run("go mod download", CacheMount("/go/pkg/mod"), BindMount("go.mod"), BindMount("go.sum")),
                    Copy(".", "."),
                    Run($"CGO_ENABLED=0 go build -trimpath -ldflags=\"-s -w\" -o /out/{d.Entry} {d.BuildPath}", CacheMount("/go/pkg/mod"), CacheMount("/root/.cache/go-build"))));
                doc.Stages.Add(Stage("runtime", "gcr.io/distroless/static-debian12:nonroot", CopyFrom("build", "/out/" + d.Entry, "/app/" + d.Entry), Expose(d.Port), Exec(InstructionKind.Entrypoint, "/app/" + d.Entry)));
                if (!File.Exists(Path.Combine(d.Folder, "go.sum"))) doc.Stages[0].Instructions[1].Mounts.RemoveAt(2);
                break;
            case "dotnet":
                doc.Stages.Add(Stage("build", $"mcr.microsoft.com/dotnet/sdk:{d.Version}", Workdir("/src"), Copy(".", "."),
                    Run($"dotnet publish {d.BuildPath} -c Release -o /app/publish", CacheMount("/root/.nuget/packages"))));
                var runtime = Stage("runtime", $"mcr.microsoft.com/dotnet/{(d.IsWeb ? "aspnet" : "runtime")}:{d.Version}", Workdir("/app"), CopyFrom("build", "/app/publish", "."));
                if (d.IsWeb) { runtime.Instructions.Add(Env(("ASPNETCORE_HTTP_PORTS", d.Port))); runtime.Instructions.Add(Expose(d.Port)); }
                runtime.Instructions.Add(User("app"));
                runtime.Instructions.Add(Exec(InstructionKind.Entrypoint, "dotnet", d.Entry + ".dll"));
                doc.Stages.Add(runtime);
                break;
            case "rust":
                doc.Stages.Add(Stage("build", $"rust:{d.Version}-slim", Workdir("/src"), Copy(".", "."),
                    Run($"cargo build --release --locked && cp target/release/{d.Entry} /usr/local/bin/app", CacheMount("/usr/local/cargo/registry"), CacheMount("/src/target"))));
                if (!File.Exists(Path.Combine(d.Folder, "Cargo.lock"))) doc.Stages[0].Instructions[2].Command = $"cargo build --release && cp target/release/{d.Entry} /usr/local/bin/app";
                doc.Stages.Add(Stage("runtime", "debian:bookworm-slim", Run(AddUser), CopyFrom("build", "/usr/local/bin/app", "/usr/local/bin/app"), Expose(d.Port), User("app"), Exec(InstructionKind.Entrypoint, "/usr/local/bin/app")));
                break;
            case "java":
                if (d.PackageManager == "maven")
                    doc.Stages.Add(Stage("build", $"maven:3-eclipse-temurin-{d.Version}", Workdir("/src"), Copy("./", "pom.xml"), Run("mvn -B dependency:go-offline", CacheMount("/root/.m2")),
                        Copy(".", "."), Run("mvn -B -DskipTests package && cp target/*.jar /app.jar", CacheMount("/root/.m2"))));
                else
                    doc.Stages.Add(Stage("build", $"gradle:8-jdk{d.Version}", Workdir("/src"), Copy(".", "."),
                        Run("gradle build -x test --no-daemon && cp $(ls build/libs/*.jar | grep -v plain | head -n 1) /app.jar", CacheMount("/root/.gradle"))));
                doc.Stages.Add(Stage("runtime", $"eclipse-temurin:{d.Version}-jre", Workdir("/app"), Run(AddUser), CopyFrom("build", "/app.jar", "/app/app.jar"), Expose(d.Port), User("app"),
                    Exec(InstructionKind.Entrypoint, "java", "-jar", "/app/app.jar")));
                break;
            case "static":
                doc.Stages.Add(Stage("runtime", "nginxinc/nginx-unprivileged:1.27-alpine", Copy("/usr/share/nginx/html/", "."), Expose("8080")));
                break;
            default:
                doc.Stages.Add(Stage("app", "alpine:3.21"));
                break;
        }
        doc.IgnorePatterns = DockerignoreFor(d);
        return doc;
    }

    private static void SuggestNode(ProjectDetection d, DockerfileDocument doc)
    {
        doc.Preamble.Add(Arg("NODE_VERSION", d.Version));
        var (lockfile, install, prodInstall, cache) = d.PackageManager switch
        {
            "pnpm" => ("pnpm-lock.yaml", "corepack enable && pnpm install --frozen-lockfile", "corepack enable && pnpm install --prod --frozen-lockfile", "/root/.local/share/pnpm/store"),
            "yarn" => ("yarn.lock", "yarn install --frozen-lockfile", "yarn install --production --frozen-lockfile", "/usr/local/share/.cache/yarn"),
            _ => (File.Exists(Path.Combine(d.Folder, "package-lock.json")) ? "package-lock.json" : "", "npm ci", "npm ci --omit=dev", "/root/.npm")
        };
        if (lockfile.Length == 0) { install = "npm install"; prodInstall = "npm install --omit=dev"; }
        RunMount[] Mounts() => lockfile.Length > 0 ? [BindMount("package.json"), BindMount(lockfile), CacheMount(cache)] : [BindMount("package.json"), CacheMount(cache)];
        doc.Stages.Add(Stage("base", "node:${NODE_VERSION}-alpine", Workdir("/usr/src/app")));
        doc.Stages.Add(Stage("deps", "base", Run(prodInstall, Mounts())));
        if (d.HasBuildScript)
            doc.Stages.Add(Stage("build", "deps", Run(install, Mounts()), Copy(".", "."), Run((d.PackageManager == "npm" ? "npm" : d.PackageManager) + " run build")));
        var final = Stage("final", "base", Env(("NODE_ENV", "production")), Copy(".", "package.json"), CopyFrom("deps", "/usr/src/app/node_modules", "./node_modules"));
        final.Instructions.Add(d.HasBuildScript ? CopyFrom("build", "/usr/src/app/dist", "./dist") : Copy(".", "."));
        final.Instructions.Add(Expose(d.Port));
        final.Instructions.Add(User("node"));
        final.Instructions.Add(d.HasStartScript ? Exec(InstructionKind.Cmd, d.PackageManager == "npm" ? "npm" : d.PackageManager, "start") : Exec(InstructionKind.Cmd, "node", d.Entry.Length > 0 ? d.Entry : "server.js"));
        doc.Stages.Add(final);
    }

    private static void SuggestPython(ProjectDetection d, DockerfileDocument doc)
    {
        doc.Preamble.Add(Arg("PYTHON_VERSION", d.Version));
        var cmd = d.Framework switch
        {
            "FastAPI" => Exec(InstructionKind.Cmd, "uvicorn", d.Entry + ":app", "--host", "0.0.0.0", "--port", d.Port),
            "Flask" => d.HasStartScript ? Exec(InstructionKind.Cmd, "gunicorn", "--bind", "0.0.0.0:" + d.Port, d.Entry + ":app") : Exec(InstructionKind.Cmd, "python", "-m", "flask", "--app", d.Entry, "run", "--host", "0.0.0.0", "--port", d.Port),
            "Django" => d.HasStartScript ? Exec(InstructionKind.Cmd, "gunicorn", "--bind", "0.0.0.0:" + d.Port, d.Entry + ".wsgi") : Exec(InstructionKind.Cmd, "python", "manage.py", "runserver", "0.0.0.0:" + d.Port),
            _ => Exec(InstructionKind.Cmd, "python", "-m", d.Entry)
        };
        if (d.PackageManager == "uv")
        {
            doc.Stages.Add(Stage("build", "ghcr.io/astral-sh/uv:python${PYTHON_VERSION}-bookworm-slim", Env(("UV_COMPILE_BYTECODE", "1"), ("UV_LINK_MODE", "copy")), Workdir("/app"),
                Run("uv sync --frozen --no-install-project --no-dev", CacheMount("/root/.cache/uv"), BindMount("pyproject.toml"), BindMount("uv.lock")),
                Copy(".", "."), Run("uv sync --frozen --no-dev", CacheMount("/root/.cache/uv"))));
            doc.Stages.Add(Stage("runtime", "python:${PYTHON_VERSION}-slim", Env(("PATH", "/app/.venv/bin:$PATH")), Workdir("/app"), Run(AddUser),
                CopyFrom("build", "/app", "/app", "app:app"), Expose(d.Port), User("app"), cmd));
            return;
        }
        var stage = Stage("runtime", "python:${PYTHON_VERSION}-slim", Env(("PYTHONDONTWRITEBYTECODE", "1"), ("PYTHONUNBUFFERED", "1")), Workdir("/app"));
        if (d.PackageManager == "poetry")
        {
            stage.Instructions.Add(Env(("POETRY_VIRTUALENVS_CREATE", "false")));
            stage.Instructions.Add(Run("pip install poetry", CacheMount("/root/.cache/pip")));
            stage.Instructions.Add(Run("poetry install --only main --no-root --no-interaction", CacheMount("/root/.cache/pypoetry"), BindMount("pyproject.toml"), BindMount("poetry.lock")));
        }
        else if (File.Exists(Path.Combine(d.Folder, "requirements.txt")))
            stage.Instructions.Add(Run("python -m pip install -r requirements.txt", CacheMount("/root/.cache/pip"), BindMount("requirements.txt")));
        else
            stage.Instructions.Add(Run("python -m pip install .", CacheMount("/root/.cache/pip"), BindMount("pyproject.toml")));
        stage.Instructions.Add(Run(AddUser));
        stage.Instructions.Add(Copy(".", "."));
        stage.Instructions.Add(Expose(d.Port));
        stage.Instructions.Add(User("app"));
        stage.Instructions.Add(cmd);
        doc.Stages.Add(stage);
    }

    /// <summary>Common and language-specific .dockerignore patterns for the detection.</summary>
    public static List<string> DockerignoreFor(ProjectDetection d)
    {
        var preset = d.Language switch { "node" => "Node.js", "python" => "Python", "go" => "Go", "dotnet" => ".NET", "rust" => "Rust", "java" => "Java", "static" => "Static site", _ => "" };
        var lines = new List<string> { "# Generated by the Dockerfile Creator from the detected project" };
        lines.AddRange(Dockerignore.Presets["Common"]);
        if (preset.Length > 0) lines.AddRange(Dockerignore.Presets[preset].Where(p => !lines.Contains(p)));
        // Build output is produced inside the image, but a static site copies the folder as is.
        if (d.Language == "node" && !d.HasBuildScript) lines.Remove("dist");
        return lines;
    }

    /// <summary>A compose.yaml that builds the Dockerfile in the folder and publishes the detected port.</summary>
    public static string ComposeFor(ProjectDetection d, DockerfileDocument document)
    {
        var sb = new StringBuilder();
        sb.Append("services:\n");
        sb.Append("  ").Append(d.Name).Append(":\n");
        sb.Append("    build:\n      context: .\n      dockerfile: Dockerfile\n");
        if (document.Target.Trim().Length > 0) sb.Append("      target: ").Append(document.Target.Trim()).Append('\n');
        var port = d.Port.Length > 0 ? d.Port : document.Stages.LastOrDefault()?.Instructions.FirstOrDefault(i => i.Kind == InstructionKind.Expose)?.Port ?? "";
        if (Regex.IsMatch(port, @"^\d{1,5}$", RegexOptions.None, RegexTimeout)) sb.Append("    ports:\n      - \"").Append(port).Append(':').Append(port).Append("\"\n");
        if (d.Language == "python") sb.Append("    environment:\n      PYTHONUNBUFFERED: \"1\"\n");
        return sb.ToString();
    }

    /// <summary>Lower-case Compose service name: letters, digits, '-' and '_'.</summary>
    public static string ServiceName(string name)
    {
        var cleaned = Regex.Replace(name.Trim().ToLowerInvariant().TrimStart('@').Replace('/', '-'), "[^a-z0-9_-]", "-", RegexOptions.None, RegexTimeout).Trim('-', '_');
        return cleaned.Length == 0 ? "app" : cleaned;
    }

    // ------------------------------------------------------------------ file helpers

    private static string ReadSmall(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxManifestBytes) return "";
            return File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
    }

    private static bool SafeRelative(string path) => path.Length > 0 && path.Length < 200 && !path.Contains("..", StringComparison.Ordinal) && !Path.IsPathRooted(path) && !path.Any(char.IsControl);

    private static IEnumerable<string> SafeDirectories(string folder)
    {
        try { return Directory.EnumerateDirectories(folder).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>Files matching a pattern within <paramref name="depth"/> levels, skipping dependency and output folders.</summary>
    private static List<string> FindFiles(string root, string pattern, int depth)
    {
        var found = new List<string>();
        void Walk(string folder, int level)
        {
            if (found.Count >= 50) return;
            try { found.AddRange(Directory.EnumerateFiles(folder, pattern).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Take(50 - found.Count)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }
            if (level >= depth) return;
            foreach (var child in SafeDirectories(folder).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(child);
                if (name.StartsWith('.') || SkippedDirectories.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                Walk(child, level + 1);
            }
        }
        Walk(root, 1);
        return found;
    }
}
