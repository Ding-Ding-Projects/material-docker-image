using System;
using System.Collections.Generic;

namespace MaterialDockerImage.UI;

/// <summary>How a Compose key's value is written, which decides the inserted text.</summary>
public enum ComposeKeyShape { Scalar, Map, List }

public sealed record ComposeKeyInfo(string Key, ComposeKeyShape Shape, string Doc, string Example = "");

/// <summary>Compose Specification keys by indentation context, with short descriptions and value sets.</summary>
public static class ComposeCompletionData
{
    private static ComposeKeyInfo S(string key, string doc, string example = "") => new(key, ComposeKeyShape.Scalar, doc, example);
    private static ComposeKeyInfo M(string key, string doc, string example = "") => new(key, ComposeKeyShape.Map, doc, example);
    private static ComposeKeyInfo L(string key, string doc, string example = "") => new(key, ComposeKeyShape.List, doc, example);

    public static readonly ComposeKeyInfo[] TopLevel =
    [
        M("services", "The containers of the application, one entry per service."),
        M("volumes", "Named volumes that services can mount."),
        M("networks", "Networks that services can join. A default network is created automatically."),
        M("secrets", "Sensitive data granted to services, mounted at /run/secrets/<name>."),
        M("configs", "Non-sensitive configuration files granted to services."),
        L("include", "Other Compose files to include as part of this application."),
        S("name", "Project name. Defaults to the folder name.", "name: shop"),
    ];

    public static readonly ComposeKeyInfo[] Service =
    [
        S("image", "Image to start the container from. With build, the built image gets this name.", "image: nginx:1.27"),
        M("build", "Builds the image from a Dockerfile. Short form: build: ./dir.", "build:\n  context: .\n  dockerfile: Dockerfile"),
        L("ports", "Publishes container ports on the host: [host_ip:][host:]container[/protocol].", "ports:\n  - \"8080:80\""),
        L("expose", "Ports reachable by other services only (not published on the host)."),
        M("environment", "Environment variables. Prefer secrets for passwords and tokens.", "environment:\n  NODE_ENV: production"),
        L("env_file", "Files with KEY=value lines added to the environment.", "env_file:\n  - .env"),
        L("volumes", "Named volumes or host paths mounted into the container: source:target[:mode].", "volumes:\n  - data:/var/lib/postgresql/data"),
        M("depends_on", "Start order and readiness conditions on other services.", "depends_on:\n  db:\n    condition: service_healthy"),
        M("healthcheck", "Command that reports whether the container is healthy.", "healthcheck:\n  test: [\"CMD\", \"curl\", \"-f\", \"http://localhost\"]"),
        M("develop", "Development settings used by docker compose watch.", "develop:\n  watch:\n    - action: sync\n      path: ./src\n      target: /app/src"),
        L("profiles", "The service only starts when one of these profiles is enabled.", "profiles: [debug]"),
        M("deploy", "Replicas, resources and restart policy (also honoured by docker compose for resources)."),
        S("restart", "Restart policy: no, always, on-failure[:max] or unless-stopped.", "restart: unless-stopped"),
        S("command", "Overrides the image's CMD.", "command: [\"npm\", \"start\"]"),
        S("entrypoint", "Overrides the image's ENTRYPOINT."),
        S("container_name", "Fixed container name (prevents scaling).", "container_name: web"),
        S("hostname", "Container hostname."),
        S("user", "User (and group) to run as: name, uid or uid:gid.", "user: \"1000:1000\""),
        S("working_dir", "Working directory inside the container."),
        L("networks", "Networks this service joins (list or map with aliases)."),
        S("network_mode", "bridge, host, none, service:<name> or container:<name>."),
        L("secrets", "Secrets from the top-level secrets section granted to this service."),
        L("configs", "Configs from the top-level configs section granted to this service."),
        M("labels", "Container labels.", "labels:\n  com.example.team: web"),
        M("logging", "Logging driver and options."),
        L("links", "Legacy links to other services. Networks make services reachable by name already."),
        L("extra_hosts", "Additional /etc/hosts entries: host:ip.", "extra_hosts:\n  - \"host.docker.internal:host-gateway\""),
        S("pull_policy", "When to pull the image: always, never, missing or build."),
        S("platform", "Target platform, for example linux/amd64.", "platform: linux/arm64"),
        S("init", "Runs an init process that forwards signals and reaps zombies."),
        S("privileged", "Gives the container extended privileges. Avoid unless required."),
        S("read_only", "Mounts the container's root filesystem read-only."),
        S("stdin_open", "Keeps STDIN open (docker run -i)."),
        S("tty", "Allocates a terminal (docker run -t)."),
        L("cap_add", "Linux capabilities to add."),
        L("cap_drop", "Linux capabilities to drop."),
        L("devices", "Host devices mapped into the container."),
        L("dns", "Custom DNS servers."),
        M("ulimits", "Resource limits such as nofile."),
        S("shm_size", "Size of /dev/shm, for example 1gb."),
        S("stop_signal", "Signal sent to stop the container."),
        S("stop_grace_period", "Time to wait before killing the container, for example 30s."),
        S("mem_limit", "Memory limit, for example 512m."),
        S("cpus", "Number of CPUs, for example 1.5."),
        L("tmpfs", "Temporary in-memory mounts."),
        S("extends", "Reuses the configuration of another service."),
    ];

    public static readonly ComposeKeyInfo[] Build =
    [
        S("context", "Build context folder (or Git URL), relative to the Compose file.", "context: ."),
        S("dockerfile", "Dockerfile path relative to the context.", "dockerfile: Dockerfile"),
        S("dockerfile_inline", "Dockerfile content written inline."),
        M("args", "Build arguments.", "args:\n  NODE_VERSION: 22"),
        S("target", "Stage to build in a multi-stage Dockerfile.", "target: runtime"),
        L("cache_from", "Cache sources."),
        L("cache_to", "Cache export destinations."),
        L("platforms", "Platforms to build for."),
        L("secrets", "Build secrets available to RUN --mount=type=secret."),
        L("ssh", "SSH agent sockets or keys for the build."),
        L("tags", "Extra tags for the built image."),
        M("labels", "Image labels."),
        M("additional_contexts", "Named contexts usable with COPY --from=<name>."),
        S("network", "Network for RUN instructions during the build."),
        S("no_cache", "Builds without cache."),
        S("pull", "Always pulls newer base images."),
    ];

    public static readonly ComposeKeyInfo[] Healthcheck =
    [
        S("test", "Command to run: [\"CMD\", ...], [\"CMD-SHELL\", \"...\"] or NONE.", "test: [\"CMD-SHELL\", \"pg_isready -U postgres\"]"),
        S("interval", "Time between checks. Default 30s."), S("timeout", "Time a check may take. Default 30s."),
        S("retries", "Failures before unhealthy. Default 3."), S("start_period", "Start-up time during which failures do not count."),
        S("start_interval", "Time between checks during the start period."), S("disable", "Disables the image's health check."),
    ];

    public static readonly ComposeKeyInfo[] DependsOnEntry =
    [
        S("condition", "service_started, service_healthy or service_completed_successfully.", "condition: service_healthy"),
        S("restart", "Restarts this service when the dependency is updated."),
        S("required", "When false, a missing or disabled dependency only gives a warning."),
    ];

    public static readonly ComposeKeyInfo[] Develop = [L("watch", "Rules that sync, rebuild or restart when files change.", "watch:\n  - action: sync\n    path: ./src\n    target: /app/src")];

    public static readonly ComposeKeyInfo[] WatchRule =
    [
        S("action", "sync, rebuild, sync+restart, restart or sync+exec."), S("path", "Host path to watch, relative to the Compose file."),
        S("target", "Container path for sync actions."), L("ignore", "Patterns to ignore."), L("include", "Only watch matching files."),
    ];

    public static readonly ComposeKeyInfo[] Deploy =
    [
        S("mode", "replicated or global."), S("replicas", "Number of containers."), M("resources", "CPU and memory limits and reservations."),
        M("restart_policy", "When and how often to restart."), M("placement", "Placement constraints."), M("update_config", "Rolling update settings."),
        M("rollback_config", "Rollback settings."), M("labels", "Service labels."), S("endpoint_mode", "vip or dnsrr."),
    ];

    public static readonly ComposeKeyInfo[] Resources = [M("limits", "Upper limits."), M("reservations", "Guaranteed resources.")];
    public static readonly ComposeKeyInfo[] Limits = [S("cpus", "CPUs, for example '0.5'."), S("memory", "Memory, for example 512M."), S("pids", "Maximum processes.")];
    public static readonly ComposeKeyInfo[] Reservations = [S("cpus", "CPUs, for example '0.25'."), S("memory", "Memory, for example 256M."), L("devices", "Devices such as GPUs.")];
    public static readonly ComposeKeyInfo[] Logging = [S("driver", "Logging driver."), M("options", "Driver options such as max-size.")];
    public static readonly ComposeKeyInfo[] PortLong = [S("target", "Container port."), S("published", "Host port."), S("host_ip", "Host IP to bind."), S("protocol", "tcp or udp."), S("app_protocol", "Application protocol hint."), S("mode", "host or ingress."), S("name", "Port name.")];
    public static readonly ComposeKeyInfo[] VolumeLong = [S("type", "bind, volume, tmpfs or npipe."), S("source", "Host path or volume name."), S("target", "Container path."), S("read_only", "Mount read-only."), M("bind", "Bind options."), M("volume", "Volume options."), M("tmpfs", "tmpfs options.")];
    public static readonly ComposeKeyInfo[] VolumeTop = [S("driver", "Volume driver. Default local."), M("driver_opts", "Driver options."), S("external", "The volume already exists and is not created by Compose."), M("labels", "Volume labels."), S("name", "Actual volume name.")];
    public static readonly ComposeKeyInfo[] NetworkTop = [S("driver", "Network driver (bridge by default)."), M("driver_opts", "Driver options."), S("attachable", "Allows standalone containers to attach."), S("enable_ipv6", "Enables IPv6."), S("external", "The network already exists."), S("internal", "No outside connectivity."), M("ipam", "IP address management."), M("labels", "Network labels."), S("name", "Actual network name.")];
    public static readonly ComposeKeyInfo[] SecretTop = [S("file", "File with the secret, relative to the Compose file."), S("environment", "Environment variable holding the secret."), S("external", "Secret already exists in the engine."), S("name", "Actual secret name.")];
    public static readonly ComposeKeyInfo[] ConfigTop = [S("file", "File with the config."), S("environment", "Environment variable holding the config."), S("content", "Inline config content."), S("external", "Config already exists."), S("name", "Actual config name.")];

    public static readonly HashSet<string> BooleanKeys = new(StringComparer.Ordinal) { "init", "privileged", "read_only", "stdin_open", "tty", "external", "attachable", "internal", "enable_ipv6", "disable", "no_cache", "pull", "required" };

    /// <summary>Fixed value sets by key (context-specific keys are resolved in ComposeCompletion).</summary>
    public static readonly Dictionary<string, (string Value, string Doc)[]> Values = new(StringComparer.Ordinal)
    {
        ["restart"] = [("no", "Never restart (default)."), ("always", "Always restart."), ("on-failure", "Restart on a non-zero exit."), ("unless-stopped", "Restart unless stopped by you.")],
        ["pull_policy"] = [("always", "Pull every time."), ("never", "Never pull."), ("missing", "Pull only when missing (default)."), ("build", "Build the image instead.")],
        ["condition"] = [("service_started", "Wait until the dependency started."), ("service_healthy", "Wait until its health check passes."), ("service_completed_successfully", "Wait until it exited with code 0.")],
        ["action"] = [("sync", "Copy changed files into the container."), ("rebuild", "Rebuild the image and recreate the container."), ("sync+restart", "Sync, then restart the container."), ("restart", "Restart the container."), ("sync+exec", "Sync, then run a command.")],
        ["protocol"] = [("tcp", "TCP."), ("udp", "UDP.")],
        ["endpoint_mode"] = [("vip", "Virtual IP (default)."), ("dnsrr", "DNS round robin.")],
    };

    public static readonly string[] Capabilities = ["NET_ADMIN", "NET_BIND_SERVICE", "NET_RAW", "SYS_ADMIN", "SYS_PTRACE", "SYS_TIME", "CHOWN", "DAC_OVERRIDE", "SETUID", "SETGID", "ALL"];
}
