using System;
using System.Collections.Generic;
using System.Linq;

namespace MaterialDockerImage.UI;

/// <summary>A starter that builds a complete draft from scratch. Description states exactly what it creates.</summary>
public sealed record DockerfileStarter(string Id, string Label, string Description, Func<DockerfileDocument> Create);

/// <summary>Starter presets for the Creator. Tags are reasonable current defaults; the lint still asks for digest pinning.</summary>
public static class DockerfileStarters
{
    public static readonly IReadOnlyList<DockerfileStarter> All =
    [
        new("node", "Node.js", "Three stages: deps (npm ci --omit=dev with an npm cache mount), build (npm ci and npm run build) and runtime (node:22-alpine, NODE_ENV=production, USER node, EXPOSE 8080, CMD node dist/server.js).", Node),
        new("python", "Python", "Two stages: build (python:3.13-slim, pip wheels from requirements.txt with a pip cache mount) and runtime (installs the wheels, non-root user, EXPOSE 8000, CMD python -m app).", PythonPip),
        new("python-uv", "Python (uv)", "Two stages: build (uv sync --frozen with a uv cache mount from pyproject.toml and uv.lock) and runtime (python:3.13-slim with the virtual environment, non-root user, EXPOSE 8000, CMD uvicorn).", PythonUv),
        new("dotnet", ".NET", "Two stages: build (mcr.microsoft.com/dotnet/sdk:9.0, dotnet restore with a NuGet cache mount, dotnet publish) and runtime (mcr.microsoft.com/dotnet/aspnet:9.0, USER app, EXPOSE 8080, ENTRYPOINT dotnet App.dll).", DotNet),
        new("go", "Go", "Two stages: build (golang:1.24-alpine, go mod download and a static CGO_ENABLED=0 build with module and build caches) and runtime (gcr.io/distroless/static-debian12:nonroot, ENTRYPOINT /app/server).", Go),
        new("java", "Java", "Two stages: build (maven:3-eclipse-temurin-21, dependency:go-offline and package with a Maven cache mount) and runtime (eclipse-temurin:21-jre, non-root user, EXPOSE 8080, java -jar app.jar).", Java),
        new("rust", "Rust", "Two stages: build (rust:1-slim, cargo build --release with registry and target cache mounts) and runtime (debian:bookworm-slim, non-root user, ENTRYPOINT /usr/local/bin/app).", Rust),
        new("static", "Static site", "Two stages: build (node:22-alpine, npm ci and npm run build) and runtime (nginxinc/nginx-unprivileged:1.27-alpine serving dist on port 8080).", StaticSite),
        new("blank", "Blank", "One stage named app based on alpine:3.21 with no instructions.", Blank),
    ];

    public static DockerfileStarter? Find(string id) => All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    private static DockerInstruction Run(string command, params string[] caches) => new() { Kind = InstructionKind.Run, Command = command, CacheTargets = caches.ToList() };
    private static DockerInstruction Copy(string destination, params string[] sources) => new() { Kind = InstructionKind.Copy, Sources = sources.ToList(), Destination = destination };
    private static DockerInstruction CopyFrom(string from, string source, string destination, string chown = "") => new() { Kind = InstructionKind.Copy, From = from, Sources = { source }, Destination = destination, Chown = chown };
    private static DockerInstruction Workdir(string path) => new() { Kind = InstructionKind.Workdir, Value = path };
    private static DockerInstruction Env(params (string Name, string Value)[] pairs) => new() { Kind = InstructionKind.Env, Pairs = pairs.Select(p => new NameValue(p.Name, p.Value)).ToList() };
    private static DockerInstruction User(string user) => new() { Kind = InstructionKind.User, Value = user };
    private static DockerInstruction Expose(string port) => new() { Kind = InstructionKind.Expose, Port = port, Protocol = "tcp" };
    private static DockerInstruction Exec(InstructionKind kind, params string[] args) => new() { Kind = kind, ExecForm = true, Arguments = args.ToList() };
    private static DockerInstruction Health(string command) => new() { Kind = InstructionKind.Healthcheck, Interval = "30s", Timeout = "5s", StartPeriod = "10s", Retries = "3", Command = command };
    private static DockerfileStage Stage(string name, string image, params DockerInstruction[] instructions) => new() { Name = name, BaseImage = image, Instructions = instructions.ToList() };
    private static DockerfileDocument Document(string origin, params DockerfileStage[] stages) => new() { SyntaxHeader = true, Syntax = DockerfileModel.DefaultSyntax, Origin = origin, Stages = stages.ToList() };

    public static DockerfileDocument Node() => Document("Node.js service starter",
        Stage("deps", "node:22-alpine", Workdir("/app"), Copy("./", "package.json", "package-lock.json"), Run("npm ci --omit=dev", "/root/.npm")),
        Stage("build", "deps", Run("npm ci", "/root/.npm"), Copy(".", "."), Run("npm run build")),
        Stage("runtime", "node:22-alpine", Env(("NODE_ENV", "production")), Workdir("/app"),
            CopyFrom("deps", "/app/node_modules", "./node_modules"), CopyFrom("build", "/app/dist", "./dist", "node:node"),
            Expose("8080"), User("node"), Health("wget -qO- http://localhost:8080/ || exit 1"), Exec(InstructionKind.Cmd, "node", "dist/server.js")));

    public static DockerfileDocument PythonPip() => Document("Python (pip) service starter",
        Stage("build", "python:3.13-slim", Workdir("/src"), Copy("./", "requirements.txt"), Run("pip wheel --wheel-dir /wheels -r requirements.txt", "/root/.cache/pip")),
        Stage("runtime", "python:3.13-slim", Env(("PYTHONDONTWRITEBYTECODE", "1"), ("PYTHONUNBUFFERED", "1")), Workdir("/app"),
            CopyFrom("build", "/wheels", "/wheels"), Run("pip install --no-index --find-links=/wheels /wheels/* && rm -rf /wheels"),
            Copy(".", "."), Run("useradd --system --uid 10001 --no-create-home app"), Expose("8000"), User("app"),
            Exec(InstructionKind.Cmd, "python", "-m", "app")));

    public static DockerfileDocument PythonUv() => Document("Python with uv starter",
        Stage("build", "ghcr.io/astral-sh/uv:python3.13-bookworm-slim", Env(("UV_COMPILE_BYTECODE", "1"), ("UV_LINK_MODE", "copy")), Workdir("/app"),
            Copy("./", "pyproject.toml", "uv.lock"), Run("uv sync --frozen --no-install-project --no-dev", "/root/.cache/uv"),
            Copy(".", "."), Run("uv sync --frozen --no-dev", "/root/.cache/uv")),
        Stage("runtime", "python:3.13-slim", Env(("PATH", "/app/.venv/bin:$PATH")), Workdir("/app"),
            Run("useradd --system --uid 10001 --no-create-home app"), CopyFrom("build", "/app", "/app", "app:app"),
            Expose("8000"), User("app"), Exec(InstructionKind.Cmd, "uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000")));

    public static DockerfileDocument DotNet() => Document("ASP.NET service starter",
        Stage("build", "mcr.microsoft.com/dotnet/sdk:9.0", Workdir("/src"), Copy("./", "*.csproj"), Run("dotnet restore", "/root/.nuget/packages"),
            Copy(".", "."), Run("dotnet publish -c Release -o /app/publish --no-restore", "/root/.nuget/packages")),
        Stage("runtime", "mcr.microsoft.com/dotnet/aspnet:9.0", Workdir("/app"), CopyFrom("build", "/app/publish", "."),
            Env(("ASPNETCORE_HTTP_PORTS", "8080")), Expose("8080"), User("app"), Exec(InstructionKind.Entrypoint, "dotnet", "App.dll")));

    public static DockerfileDocument Go() => Document("Go static binary starter",
        Stage("build", "golang:1.24-alpine", Workdir("/src"), Copy("./", "go.mod", "go.sum"), Run("go mod download", "/go/pkg/mod"),
            Copy(".", "."), Run("CGO_ENABLED=0 go build -trimpath -ldflags=\"-s -w\" -o /out/server ./cmd/server", "/go/pkg/mod", "/root/.cache/go-build")),
        Stage("runtime", "gcr.io/distroless/static-debian12:nonroot", CopyFrom("build", "/out/server", "/app/server"),
            Expose("8080"), Exec(InstructionKind.Entrypoint, "/app/server")));

    public static DockerfileDocument Java() => Document("Java (Maven) service starter",
        Stage("build", "maven:3-eclipse-temurin-21", Workdir("/src"), Copy("./", "pom.xml"), Run("mvn -B dependency:go-offline", "/root/.m2"),
            Copy(".", "."), Run("mvn -B -DskipTests package && cp target/*.jar /app.jar", "/root/.m2")),
        Stage("runtime", "eclipse-temurin:21-jre", Workdir("/app"), Run("useradd --system --uid 10001 --no-create-home app"),
            CopyFrom("build", "/app.jar", "/app/app.jar"), Expose("8080"), User("app"), Exec(InstructionKind.Entrypoint, "java", "-jar", "/app/app.jar")));

    public static DockerfileDocument Rust() => Document("Rust service starter",
        Stage("build", "rust:1-slim", Workdir("/src"), Copy(".", "."),
            Run("cargo build --release && cp target/release/app /usr/local/bin/app", "/usr/local/cargo/registry", "/src/target")),
        Stage("runtime", "debian:bookworm-slim", Run("useradd --system --uid 10001 --no-create-home app"),
            CopyFrom("build", "/usr/local/bin/app", "/usr/local/bin/app"), User("app"), Exec(InstructionKind.Entrypoint, "/usr/local/bin/app")));

    public static DockerfileDocument StaticSite() => Document("Static site starter",
        Stage("build", "node:22-alpine", Workdir("/src"), Copy("./", "package.json", "package-lock.json"), Run("npm ci", "/root/.npm"), Copy(".", "."), Run("npm run build")),
        Stage("runtime", "nginxinc/nginx-unprivileged:1.27-alpine", CopyFrom("build", "/src/dist", "/usr/share/nginx/html"), Expose("8080"),
            Health("wget -qO- http://localhost:8080/ || exit 1")));

    public static DockerfileDocument Blank() => Document("Blank starter", Stage("app", "alpine:3.21"));
}
