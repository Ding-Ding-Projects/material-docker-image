using System;
using System.Collections.Generic;
using System.Linq;

namespace MaterialDockerImage.UI;

/// <summary>A flag of a Dockerfile instruction with the minimum docker/dockerfile frontend that supports it.</summary>
public sealed record DockerfileFlagInfo(string Name, bool TakesValue, string Doc, string Example, string? MinSyntax = null, string? LabsMin = null, bool Repeatable = false, string[]? Values = null)
{
    /// <summary>The text inserted for the flag: "--name=" for valued flags, "--name" for switches.</summary>
    public string Insert => "--" + Name + (TakesValue ? "=" : "");
}

public sealed record DockerfileInstructionInfo(string Name, string Signature, string Doc, string Example, DockerfileFlagInfo[] Flags);

public sealed record DockerfileMountOption(string Key, string Doc, string[] Values);

/// <summary>
/// Reference data for Dockerfile suggestions, taken from the Dockerfile reference option tables (minimum frontend
/// versions per flag) and the RUN --mount option table.
/// </summary>
public static class DockerfileCompletionData
{
    private static readonly string[] Bool = ["true", "false"];

    public static readonly DockerfileInstructionInfo[] Instructions =
    [
        new("FROM", "FROM [--platform=<platform>] <image>[:<tag>][@<digest>] [AS <name>]",
            "Starts a build stage from a base image. ARG instructions before the first FROM can be used in FROM lines. Use scratch for an empty base.",
            "FROM --platform=$BUILDPLATFORM golang:1.23 AS build",
            [new("platform", true, "Platform of the base image, for example linux/amd64 or $BUILDPLATFORM for cross-compilation.", "FROM --platform=$BUILDPLATFORM node:22")]),
        new("RUN", "RUN [--mount=<spec>] [--network=<mode>] [--security=<mode>] <command> | [\"exec\", \"arg\"...]",
            "Runs a command in a new layer. Shell form runs through the SHELL (default /bin/sh -c); exec form runs the program directly and does not expand variables.",
            "RUN --mount=type=cache,target=/root/.cache/pip pip install -r requirements.txt",
            [
                new("mount", true, "Mounts a bind, cache, tmpfs, secret or ssh source for this RUN only. Repeatable.", "--mount=type=cache,target=/root/.npm", "1.2", Repeatable: true),
                new("network", true, "Network for this RUN: default, none (no network) or host (needs the network.host entitlement).", "--network=none", "1.3", Values: ["default", "none", "host"]),
                new("security", true, "sandbox (default) or insecure, which runs with elevated privileges and needs the security.insecure entitlement.", "--security=insecure", "1.20", Values: ["sandbox", "insecure"]),
                new("device", true, "Attaches a CDI device (name[,required]). Needs BuildKit 0.20 or newer and the device entitlement.", "--device=vendor.com/gpu=all", "1.27", "1.14", Repeatable: true),
            ]),
        new("CMD", "CMD [\"executable\", \"param\"...] | CMD <command>",
            "Default command (or default arguments for ENTRYPOINT) when a container starts. Only the last CMD counts.",
            "CMD [\"node\", \"server.js\"]", []),
        new("LABEL", "LABEL <key>=<value> [<key>=<value>...]",
            "Adds metadata to the image. Use the org.opencontainers.image.* keys for standard metadata.",
            "LABEL org.opencontainers.image.source=\"https://example.com/repo\"", []),
        new("MAINTAINER", "MAINTAINER <name>",
            "Deprecated. Use LABEL org.opencontainers.image.authors instead.",
            "LABEL org.opencontainers.image.authors=\"team@example.com\"", []),
        new("EXPOSE", "EXPOSE <port>[/<tcp|udp>] [<port>[/<protocol>]...]",
            "Documents the ports the container listens on. It does not publish them; use -p or Compose ports for that.",
            "EXPOSE 8080/tcp", []),
        new("ENV", "ENV <key>=<value> [<key>=<value>...]",
            "Sets environment variables for later instructions and for running containers. Values persist in the image, so never put secrets here.",
            "ENV NODE_ENV=production PORT=8080", []),
        new("ADD", "ADD [--chown=<user>] [--chmod=<perms>] [--link] [--checksum=<hash>] [--keep-git-dir] <src>... <dest>",
            "Copies local files, remote URLs or Git repositories. Local tar archives are extracted. Prefer COPY for plain local files.",
            "ADD --checksum=sha256:… https://example.com/tool.tar.gz /opt/",
            [
                new("chown", true, "Owner of the copied files: user, user:group, uid or uid:gid.", "--chown=app:app"),
                new("chmod", true, "Permissions of the copied files in octal (and symbolic from 1.14).", "--chmod=755", "1.2"),
                new("link", false, "Copies into an independent layer so it can be reused when earlier layers change.", "--link", "1.4", Values: Bool),
                new("checksum", true, "Verifies a remote source: sha256:<hex> for HTTP, a commit SHA prefix for Git.", "--checksum=sha256:24454f8…", "1.6"),
                new("keep-git-dir", false, "Keeps the .git directory when adding a Git repository.", "--keep-git-dir=true", "1.1", Values: Bool),
                new("unpack", false, "Controls archive extraction (remote archives are not unpacked by default).", "--unpack=true", "1.17", Values: Bool),
                new("exclude", true, "Skips sources matching a pattern. Repeatable.", "--exclude=*.md", "1.19", "1.7", Repeatable: true),
            ]),
        new("COPY", "COPY [--from=<stage|image>] [--chown=<user>] [--chmod=<perms>] [--link] [--parents] [--exclude=<pattern>] <src>... <dest>",
            "Copies files from the build context, another stage or an image into the image.",
            "COPY --from=build /out/app /usr/local/bin/app",
            [
                new("from", true, "Copies from an earlier stage, an image or a named build context instead of the build context.", "--from=build"),
                new("chown", true, "Owner of the copied files: user, user:group, uid or uid:gid.", "--chown=app:app"),
                new("chmod", true, "Permissions of the copied files in octal; symbolic forms such as u=rwX need 1.14.", "--chmod=644", "1.2"),
                new("link", false, "Copies into an independent layer so it can be reused when earlier layers change.", "--link", "1.4", Values: Bool),
                new("parents", false, "Keeps the parent directories of each source (use ./ in the source to pick the pivot).", "--parents", "1.20", "1.7", Values: Bool),
                new("exclude", true, "Skips sources matching a pattern. Repeatable.", "--exclude=**/*.test.js", "1.19", "1.7", Repeatable: true),
            ]),
        new("ENTRYPOINT", "ENTRYPOINT [\"executable\", \"param\"...] | ENTRYPOINT <command>",
            "The executable a container runs. CMD supplies its default arguments. Prefer the exec form so signals reach the process.",
            "ENTRYPOINT [\"/usr/local/bin/app\"]", []),
        new("VOLUME", "VOLUME [\"/path\"...] | VOLUME <path>...",
            "Declares mount points for external storage. Changes made to the path after VOLUME in the Dockerfile are discarded.",
            "VOLUME [\"/data\"]", []),
        new("USER", "USER <user>[:<group>] | USER <uid>[:<gid>]",
            "Sets the user (and group) for later RUN, CMD and ENTRYPOINT. Running as non-root is recommended.",
            "USER 10001:10001", []),
        new("WORKDIR", "WORKDIR <absolute path>",
            "Sets the working directory for later instructions, creating it if needed. Use absolute paths.",
            "WORKDIR /app", []),
        new("ARG", "ARG <name>[=<default>] [<name>[=<default>]...]",
            "Declares a build argument. ARGs before the first FROM are global (usable in FROM); inside a stage, declare ARG again to use it. Values are visible in the image history, so never use ARG for secrets.",
            "ARG NODE_VERSION=22", []),
        new("ONBUILD", "ONBUILD <INSTRUCTION>",
            "Adds a trigger that runs when this image is used as a base. ONBUILD ONBUILD, FROM and MAINTAINER are not allowed.",
            "ONBUILD COPY . /app", []),
        new("STOPSIGNAL", "STOPSIGNAL <signal>",
            "The signal sent to stop the container, by name (SIGTERM) or number.",
            "STOPSIGNAL SIGQUIT", []),
        new("HEALTHCHECK", "HEALTHCHECK [--interval=<d>] [--timeout=<d>] [--start-period=<d>] [--start-interval=<d>] [--retries=<n>] CMD <command> | HEALTHCHECK NONE",
            "Tells the engine how to test that the container still works. Only the last HEALTHCHECK counts; NONE disables an inherited one.",
            "HEALTHCHECK --interval=30s --timeout=5s CMD curl -f http://localhost/ || exit 1",
            [
                new("interval", true, "Time between checks. Default 30s.", "--interval=30s"),
                new("timeout", true, "Time a single check may take before it counts as failed. Default 30s.", "--timeout=5s"),
                new("start-period", true, "Start-up time during which failures do not count. Default 0s.", "--start-period=10s"),
                new("start-interval", true, "Time between checks during the start period. Default 5s. Needs Docker Engine 25 or newer.", "--start-interval=2s"),
                new("retries", true, "Consecutive failures before the container is unhealthy. Default 3.", "--retries=3", Values: ["3", "5", "10"]),
            ]),
        new("SHELL", "SHELL [\"executable\", \"parameters\"]",
            "Changes the shell used by shell-form RUN, CMD and ENTRYPOINT. Must be a JSON array.",
            "SHELL [\"/bin/bash\", \"-o\", \"pipefail\", \"-c\"]", []),
    ];

    public static DockerfileInstructionInfo? Find(string keyword) => Instructions.FirstOrDefault(i => i.Name.Equals(keyword, StringComparison.OrdinalIgnoreCase));

    public static readonly (string Name, string Doc, string Example, string? MinSyntax, string[] Values)[] Directives =
    [
        ("syntax", "Chooses the Dockerfile frontend image. docker/dockerfile:1 follows the latest stable 1.x release.", "# syntax=docker/dockerfile:1", null,
            ["docker/dockerfile:1", "docker/dockerfile:1-labs", "docker/dockerfile-upstream:master"]),
        ("escape", "Sets the line-continuation and escape character: backslash (default) or backtick, which suits Windows paths.", "# escape=`", null, ["\\", "`"]),
        ("check", "Configures build checks: skip rules, treat warnings as errors, or enable experimental checks.", "# check=skip=JSONArgsRecommended;error=true", "1.8",
            ["skip=", "error=true", "experimental=all"]),
    ];

    public static readonly DockerfileMountOption[] MountTypes =
    [
        new("bind", "Read-only bind of the build context, a stage or an image (the default type).", []),
        new("cache", "Persistent cache folder shared between builds (for package managers and compilers). Not exported with --cache-to.", []),
        new("tmpfs", "Temporary in-memory folder.", []),
        new("secret", "Mounts a build secret as a file (default /run/secrets/<id>) or an environment variable. Nothing reaches the image.", []),
        new("ssh", "Forwards the SSH agent socket for private Git or SSH access.", []),
    ];

    /// <summary>Option keys valid for each mount type, with their values where they are a fixed set.</summary>
    public static IReadOnlyList<DockerfileMountOption> MountOptions(string type) => type switch
    {
        "cache" =>
        [
            new("id", "Cache identifier. Defaults to the target path.", []),
            new("target", "Mount path inside the build container.", CacheTargets),
            new("sharing", "How concurrent builds share the cache: shared (default), private or locked.", ["shared", "private", "locked"]),
            new("readonly", "Mounts read-only (also ro).", Bool),
            new("from", "Stage or image used as the cache base.", []),
            new("source", "Subpath in the from source.", []),
            new("mode", "Permissions of a new cache folder. Default 0755.", ["0755", "0777", "0700"]),
            new("uid", "Owner user ID of a new cache folder. Default 0.", ["0", "1000", "65532"]),
            new("gid", "Owner group ID of a new cache folder. Default 0.", ["0", "1000", "65532"]),
        ],
        "tmpfs" =>
        [
            new("target", "Mount path inside the build container.", []),
            new("size", "Size limit in bytes.", []),
        ],
        "secret" =>
        [
            new("id", "Secret ID passed with docker build --secret id=…", []),
            new("target", "File path. Default /run/secrets/<id>.", []),
            new("env", "Exposes the secret as this environment variable instead of a file (1.10 or newer).", []),
            new("required", "Fails the build when the secret is missing. Default false.", Bool),
            new("mode", "File permissions. Default 0400.", ["0400", "0440", "0444"]),
            new("uid", "Owner user ID. Default 0.", ["0", "1000", "65532"]),
            new("gid", "Owner group ID. Default 0.", ["0", "1000", "65532"]),
        ],
        "ssh" =>
        [
            new("id", "SSH agent or key ID passed with --ssh. Default \"default\".", ["default"]),
            new("target", "Socket path. Default /run/buildkit/ssh_agent.N.", []),
            new("required", "Fails the build when the agent is missing. Default false.", Bool),
            new("mode", "Socket permissions. Default 0600.", ["0600"]),
            new("uid", "Owner user ID. Default 0.", ["0", "1000", "65532"]),
            new("gid", "Owner group ID. Default 0.", ["0", "1000", "65532"]),
        ],
        _ =>
        [
            new("target", "Mount path inside the build container (also dst, destination).", []),
            new("source", "Path in the from source. Default is its root.", []),
            new("from", "Stage, image or named context to bind. Default is the build context.", []),
            new("readwrite", "Allows writes (discarded after the RUN). Also rw.", Bool),
        ],
    };

    /// <summary>Well-known cache folders of common package managers and compilers.</summary>
    public static readonly string[] CacheTargets =
    [
        "/root/.npm", "/usr/local/share/.cache/yarn", "/root/.local/share/pnpm/store", "/root/.cache/pip", "/root/.cache/uv",
        "/go/pkg/mod", "/root/.cache/go-build", "/root/.nuget/packages", "/root/.m2", "/root/.gradle", "/usr/local/cargo/registry",
        "/var/cache/apt", "/var/lib/apt", "/var/cache/apk",
    ];

    public static readonly (string Name, string Doc)[] PlatformArgs =
    [
        ("TARGETPLATFORM", "Platform of the build result, for example linux/arm64."),
        ("TARGETOS", "OS part of TARGETPLATFORM, for example linux."),
        ("TARGETARCH", "Architecture part of TARGETPLATFORM, for example arm64."),
        ("TARGETVARIANT", "Variant part of TARGETPLATFORM, for example v7."),
        ("BUILDPLATFORM", "Platform of the machine running the build."),
        ("BUILDOS", "OS part of BUILDPLATFORM."),
        ("BUILDARCH", "Architecture part of BUILDPLATFORM."),
        ("BUILDVARIANT", "Variant part of BUILDPLATFORM."),
    ];

    public static readonly string[] ProxyArgs = ["HTTP_PROXY", "HTTPS_PROXY", "FTP_PROXY", "NO_PROXY", "ALL_PROXY"];

    public static readonly (string Name, string Doc)[] BuildKitArgs =
    [
        ("BUILDKIT_INLINE_CACHE", "Writes inline cache metadata into the image config."),
        ("BUILDKIT_MULTI_PLATFORM", "Forces multi-platform output even for one platform."),
        ("BUILDKIT_CONTEXT_KEEP_GIT_DIR", "Keeps .git when the context is a Git repository."),
        ("BUILDKIT_SYNTAX", "Overrides the frontend image, like # syntax."),
        ("BUILDKIT_SANDBOX_HOSTNAME", "Hostname seen by RUN commands."),
        ("BUILDKIT_CACHE_MOUNT_NS", "Namespace for cache mount IDs."),
        ("SOURCE_DATE_EPOCH", "Timestamp used for reproducible builds (1.5 or newer)."),
    ];

    public static readonly string[] Platforms = ["linux/amd64", "linux/arm64", "linux/arm/v7", "linux/arm/v6", "linux/386", "linux/ppc64le", "linux/s390x", "linux/riscv64", "windows/amd64"];

    public static readonly (string Name, string Doc)[] Signals =
    [
        ("SIGTERM", "Default stop signal: asks the process to exit."), ("SIGINT", "Interrupt, as from Ctrl+C."), ("SIGQUIT", "Quit; nginx uses it for a graceful shutdown."),
        ("SIGHUP", "Hang-up; many servers reload configuration on it."), ("SIGUSR1", "User-defined signal 1."), ("SIGUSR2", "User-defined signal 2."),
        ("SIGWINCH", "Apache httpd uses it for a graceful stop."), ("SIGKILL", "Kills immediately without cleanup."),
    ];

    public static readonly (string Value, string Doc)[] Shells =
    [
        ("[\"/bin/bash\", \"-o\", \"pipefail\", \"-c\"]", "Bash with pipefail, so a failing command in a pipe fails the RUN."),
        ("[\"/bin/sh\", \"-c\"]", "The default Linux shell."),
        ("[\"/bin/ash\", \"-eo\", \"pipefail\", \"-c\"]", "Alpine's ash with exit-on-error and pipefail."),
        ("[\"pwsh\", \"-Command\"]", "PowerShell 7."),
        ("[\"powershell\", \"-Command\"]", "Windows PowerShell (Windows images)."),
        ("[\"cmd\", \"/S\", \"/C\"]", "The default Windows shell."),
    ];

    public static readonly (string Port, string Doc)[] CommonPorts =
    [
        ("80", "HTTP"), ("443", "HTTPS"), ("3000", "Node.js and many development servers"), ("5000", "Flask and ASP.NET development"), ("8000", "Django, uvicorn"),
        ("8080", "Alternative HTTP"), ("5432", "PostgreSQL"), ("3306", "MySQL and MariaDB"), ("6379", "Redis"), ("27017", "MongoDB"), ("9000", "PHP-FPM and others"),
        ("25565", "Minecraft Java Edition"), ("19132/udp", "Minecraft Bedrock Edition"),
    ];

    public static readonly (string Name, string Doc)[] OciLabels =
    [
        ("org.opencontainers.image.title", "Human-readable title."), ("org.opencontainers.image.description", "Description."),
        ("org.opencontainers.image.source", "URL of the source code."), ("org.opencontainers.image.version", "Version of the packaged software."),
        ("org.opencontainers.image.authors", "Contact details of the maintainers."), ("org.opencontainers.image.licenses", "SPDX license expression."),
        ("org.opencontainers.image.url", "URL with more information."), ("org.opencontainers.image.documentation", "URL of the documentation."),
        ("org.opencontainers.image.vendor", "Distributing organisation."), ("org.opencontainers.image.revision", "Source control revision."),
        ("org.opencontainers.image.created", "Creation date (RFC 3339)."),
    ];

    /// <summary>Curated, clearly labelled list of popular official images. Not read from any registry.</summary>
    public static readonly (string Name, string Doc)[] PopularImages =
    [
        ("alpine", "Minimal Alpine Linux."), ("debian", "Debian Linux."), ("ubuntu", "Ubuntu Linux."), ("busybox", "Tiny Unix toolbox."),
        ("node", "Node.js runtime."), ("python", "Python runtime."), ("golang", "Go toolchain."), ("rust", "Rust toolchain."),
        ("eclipse-temurin", "OpenJDK builds by Eclipse Adoptium."), ("maven", "Apache Maven with a JDK."), ("gradle", "Gradle with a JDK."),
        ("php", "PHP runtime."), ("ruby", "Ruby runtime."), ("nginx", "nginx web server."), ("httpd", "Apache HTTP Server."), ("caddy", "Caddy web server."),
        ("postgres", "PostgreSQL database."), ("mysql", "MySQL database."), ("mariadb", "MariaDB database."), ("mongo", "MongoDB database."),
        ("redis", "Redis key-value store."), ("rabbitmq", "RabbitMQ broker."), ("memcached", "Memcached."), ("traefik", "Traefik proxy."), ("haproxy", "HAProxy load balancer."),
        ("mcr.microsoft.com/dotnet/sdk", ".NET SDK (Microsoft registry)."), ("mcr.microsoft.com/dotnet/aspnet", "ASP.NET Core runtime (Microsoft registry)."),
        ("mcr.microsoft.com/dotnet/runtime", ".NET runtime (Microsoft registry)."), ("gcr.io/distroless/static-debian12", "Distroless static base (Google registry)."),
        ("scratch", "Empty base image for static binaries."),
    ];

    /// <summary>Snippets offered at the start of a line, each with the keyword it belongs to.</summary>
    public static readonly (string Label, string Insert, string Doc, string? MinSyntax)[] Snippets =
    [
        ("FROM … AS stage", "FROM ‸ AS build", "A named build stage that later stages can copy from.", null),
        ("RUN with apt cache mounts", "RUN --mount=type=cache,target=/var/cache/apt,sharing=locked \\\n    --mount=type=cache,target=/var/lib/apt,sharing=locked \\\n    apt-get update && apt-get install -y --no-install-recommends ‸",
            "Keeps apt downloads in cache mounts so rebuilds skip them. sharing=locked avoids concurrent apt runs.", "1.2"),
        ("RUN with cache mount", "RUN --mount=type=cache,target=‸ \\\n    ", "A multi-line RUN with a persistent cache folder.", "1.2"),
        ("RUN multi-line", "RUN set -eux; \\\n    ‸", "A multi-line shell command that stops at the first error.", null),
        ("RUN heredoc", "RUN <<EOF\nset -eux\n‸\nEOF", "Runs a here-document script without backslash continuations.", "1.4"),
        ("RUN with secret", "RUN --mount=type=secret,id=‸,env=TOKEN \\\n    ", "Reads a build secret as an environment variable for this RUN only.", "1.10"),
        ("COPY heredoc", "COPY <<EOF /path/to/file\n‸\nEOF", "Writes an inline file into the image.", "1.4"),
        ("HEALTHCHECK with options", "HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \\\n    CMD ‸", "A health check with explicit timing.", null),
        ("HEALTHCHECK NONE", "HEALTHCHECK NONE", "Disables a health check inherited from the base image.", null),
    ];

    /// <summary>Highest stable frontend minimum referenced by any item, used in upgrade wording.</summary>
    public const string RecommendedSyntax = "docker/dockerfile:1";
}
