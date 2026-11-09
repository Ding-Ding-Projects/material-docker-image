namespace MaterialDockerImage.UI;

/// <summary>
/// Quick setups for common self-hosted web apps. Every image is pinned to an exact tag that was checked against its
/// registry in November 2026 (docs/features/self-hosted.md lists how). Values that are secret or chosen by the user
/// are written to the project's .env file; the Compose file only references them.
/// </summary>
public static class SelfHostedCatalogue
{
    // Shared images, pinned once so every app that uses them moves together.
    public const string MariaDbImage = "mariadb:11.8.9";
    public const string PostgresImage = "postgres:17.11-alpine";
    public const string RedisImage = "redis:8.8.4-alpine";

    /// <summary>A reference to a .env variable that stops Compose with a clear message when the variable is missing.</summary>
    public static string R(string variable) => "${" + variable + ":?" + variable + " is not set in .env}";
    public static string PortVariable(string key) => "PORT_" + key.ToUpperInvariant();

    public static string CategoryName(SelfHostedCategory category) => category switch
    {
        SelfHostedCategory.FilesAndSync => "Files and sync",
        SelfHostedCategory.Development => "Development",
        SelfHostedCategory.Media => "Media",
        SelfHostedCategory.HomeAndMonitoring => "Home and monitoring",
        SelfHostedCategory.Network => "Network",
        SelfHostedCategory.Productivity => "Productivity",
        _ => "AI",
    };

    public static readonly IReadOnlyList<SelfHostedCategory> Categories = Enum.GetValues<SelfHostedCategory>();

    private static KeyValuePair<string, string> E(string key, string value) => new(key, value);

    private static SelfHostedServiceSpec MariaDb(string database, string user) => new()
    {
        Name = "db",
        Image = MariaDbImage,
        Command = ["--transaction-isolation=READ-COMMITTED"],
        Environment = [E("MARIADB_DATABASE", database), E("MARIADB_USER", user), E("MARIADB_PASSWORD", R("DB_PASSWORD")), E("MARIADB_ROOT_PASSWORD", R("DB_ROOT_PASSWORD"))],
        Healthcheck = ["CMD", "healthcheck.sh", "--connect", "--innodb_initialized"],
    };

    private static SelfHostedServiceSpec Postgres(string database, string user, string image = PostgresImage) => new()
    {
        Name = "db",
        Image = image,
        Environment = [E("POSTGRES_DB", database), E("POSTGRES_USER", user), E("POSTGRES_PASSWORD", R("DB_PASSWORD"))],
        Healthcheck = ["CMD-SHELL", $"pg_isready -U {user} -d {database}"],
    };

    private static SelfHostedServiceSpec Redis(string name, bool password) => new()
    {
        Name = name,
        Image = RedisImage,
        Environment = password ? [E("REDIS_PASSWORD", R("REDIS_PASSWORD"))] : [],
        // $$ is a literal $ for Compose, so the password is read from the container environment rather than written into the command.
        Command = password ? ["sh", "-c", "exec redis-server --requirepass \"$$REDIS_PASSWORD\""] : [],
        Healthcheck = password ? ["CMD-SHELL", "redis-cli --no-auth-warning -a \"$$REDIS_PASSWORD\" ping | grep -q PONG"] : ["CMD-SHELL", "redis-cli ping | grep -q PONG"],
    };

    private static SelfHostedPort Web(string service, int container, int host, string purpose = "Web interface") => new("web", service, container, host, "tcp", purpose, true);
    private static string Url(string portKey = "web") => "http://${PUBLIC_HOST}:${" + PortVariable(portKey) + "}";

    public static readonly IReadOnlyList<SelfHostedApp> All =
    [
        new()
        {
            Id = "nextcloud", Name = "Nextcloud", Category = SelfHostedCategory.FilesAndSync,
            Description = "File sync and sharing with calendars, contacts and office add-ons.",
            MinMemoryMb = 2048, MinDiskGb = 10,
            Ports = [Web("app", 80, 8080)],
            Volumes = [new("html", "app", "/var/www/html", "Nextcloud program files, settings and user files"), new("db", "db", "/var/lib/mysql", "Database", true, SelfHostedDatabase.MariaDb), new("db", "db", "/var/lib/postgresql/data", "Database", true, SelfHostedDatabase.PostgreSql)],
            Secrets = [new("ADMIN_PASSWORD", "Administrator password", 24), new("DB_PASSWORD", "Database password"), new("DB_ROOT_PASSWORD", "Database root password", 32, false, SelfHostedDatabase.MariaDb), new("REDIS_PASSWORD", "Redis password")],
            HealthPort = "web", HealthPath = "/status.php", HealthTimeout = TimeSpan.FromMinutes(6),
            FirstLogin = "Sign in with the administrator name and password from this setup. Nextcloud finishes installing on first start, which takes a few minutes.",
            DocsUrl = "https://github.com/nextcloud/docker", Licence = "AGPL-3.0 (Nextcloud), GPL-2.0 (MariaDB), PostgreSQL licence, Redis tri-licence (AGPL-3.0, RSALv2, SSPLv1)",
            AdminUser = "admin", Databases = [SelfHostedDatabase.MariaDb, SelfHostedDatabase.PostgreSql],
            Note = "Other devices can only open Nextcloud through the addresses listed as trusted domains; this setup adds localhost and the computer's LAN addresses.",
            Services = o =>
            {
                var maria = o.Database != SelfHostedDatabase.PostgreSql;
                List<KeyValuePair<string, string>> db = maria
                    ? [E("MYSQL_HOST", "db"), E("MYSQL_DATABASE", "nextcloud"), E("MYSQL_USER", "nextcloud"), E("MYSQL_PASSWORD", R("DB_PASSWORD"))]
                    : [E("POSTGRES_HOST", "db"), E("POSTGRES_DB", "nextcloud"), E("POSTGRES_USER", "nextcloud"), E("POSTGRES_PASSWORD", R("DB_PASSWORD"))];
                return
                [
                    new() { Name = "app", Image = "nextcloud:35.0.2-apache", DependsOnHealthy = ["db", "redis"],
                        Environment = [.. db, E("REDIS_HOST", "redis"), E("REDIS_HOST_PASSWORD", R("REDIS_PASSWORD")), E("NEXTCLOUD_ADMIN_USER", R("ADMIN_USER")), E("NEXTCLOUD_ADMIN_PASSWORD", R("ADMIN_PASSWORD")), E("NEXTCLOUD_TRUSTED_DOMAINS", R("TRUSTED_HOSTS")), E("TZ", R("TZ"))] },
                    maria ? MariaDb("nextcloud", "nextcloud") : Postgres("nextcloud", "nextcloud"),
                    Redis("redis", true),
                ];
            },
        },
        new()
        {
            Id = "gitlab", Name = "GitLab CE", Category = SelfHostedCategory.Development,
            Description = "Git hosting with merge requests, issues, CI/CD and a container registry.",
            MinMemoryMb = 4096, MinDiskGb = 20,
            Ports = [Web("gitlab", 80, 8929), new("ssh", "gitlab", 22, 2224, "tcp", "Git over SSH")],
            Volumes = [new("config", "gitlab", "/etc/gitlab", "Configuration and secrets"), new("logs", "gitlab", "/var/log/gitlab", "Logs"), new("data", "gitlab", "/var/opt/gitlab", "Repositories, uploads and the bundled database")],
            Secrets = [new("GITLAB_ROOT_PASSWORD", "Password of the root account", 32)],
            HealthPort = "web", HealthPath = "/users/sign_in", HealthTimeout = TimeSpan.FromMinutes(12),
            FirstLogin = "Sign in as root with the generated root password. The first start configures GitLab and takes 5 to 10 minutes.",
            DocsUrl = "https://docs.gitlab.com/install/docker/", Licence = "MIT (GitLab Community Edition)",
            Note = "GitLab needs at least 4 GB of memory for Docker (8 GB recommended); give Docker Desktop enough memory in its settings before deploying.",
            Services = _ =>
            [
                new() { Name = "gitlab", Image = "gitlab/gitlab-ce:19.5.3-ce.0", ShmSize = "256m", HealthStartPeriod = "10m",
                    Environment = [E("GITLAB_ROOT_PASSWORD", R("GITLAB_ROOT_PASSWORD")), E("TZ", R("TZ")),
                        E("GITLAB_OMNIBUS_CONFIG", "external_url 'http://${PUBLIC_HOST}:${PORT_WEB}'; nginx['listen_port'] = 80; gitlab_rails['gitlab_shell_ssh_port'] = ${PORT_SSH}; gitlab_rails['time_zone'] = '${TZ}'")] },
            ],
        },
        new()
        {
            Id = "forgejo", Name = "Forgejo", Category = SelfHostedCategory.Development,
            Description = "Lightweight Git hosting with issues, pull requests and packages (a community fork of Gitea).",
            MinMemoryMb = 512, MinDiskGb = 5,
            Ports = [Web("forgejo", 3000, 3000), new("ssh", "forgejo", 22, 2222, "tcp", "Git over SSH")],
            Volumes = [new("data", "forgejo", "/data", "Repositories, settings and attachments"), new("db", "db", "/var/lib/postgresql/data", "Database", true)],
            Secrets = [new("DB_PASSWORD", "Database password")],
            HealthPort = "web", HealthPath = "/api/healthz",
            FirstLogin = "Open the web address and finish the installation page; the database settings are already filled in. The first account you register becomes the administrator.",
            DocsUrl = "https://forgejo.org/docs/latest/admin/installation/docker/", Licence = "GPL-3.0-or-later (Forgejo), PostgreSQL licence",
            Services = _ =>
            [
                new() { Name = "forgejo", Image = "codeberg.org/forgejo/forgejo:17.0.1", DependsOnHealthy = ["db"],
                    Environment = [E("USER_UID", "1000"), E("USER_GID", "1000"), E("FORGEJO__database__DB_TYPE", "postgres"), E("FORGEJO__database__HOST", "db:5432"), E("FORGEJO__database__NAME", "forgejo"),
                        E("FORGEJO__database__USER", "forgejo"), E("FORGEJO__database__PASSWD", R("DB_PASSWORD")), E("FORGEJO__server__ROOT_URL", Url() + "/"), E("FORGEJO__server__SSH_PORT", R("PORT_SSH"))] },
                Postgres("forgejo", "forgejo"),
            ],
        },
        new()
        {
            Id = "vaultwarden", Name = "Vaultwarden", Category = SelfHostedCategory.Productivity,
            Description = "Password manager server compatible with the Bitwarden apps and browser extensions.",
            MinMemoryMb = 256, MinDiskGb = 1,
            Ports = [Web("vaultwarden", 80, 8222)],
            Volumes = [new("data", "vaultwarden", "/data", "Vault database, attachments and keys")],
            Secrets = [new("ADMIN_TOKEN", "Token for the /admin page", 48)],
            HealthPort = "web", HealthPath = "/alive",
            FirstLogin = "Create your account on the web vault. The /admin page uses the generated admin token. Browsers only allow the web vault over HTTPS or on localhost, so put it behind a reverse proxy with a certificate before using it from other devices.",
            DocsUrl = "https://github.com/dani-garcia/vaultwarden/wiki", Licence = "AGPL-3.0",
            Services = _ =>
            [
                new() { Name = "vaultwarden", Image = "vaultwarden/server:1.37.5",
                    Environment = [E("ADMIN_TOKEN", R("ADMIN_TOKEN")), E("DOMAIN", Url()), E("SIGNUPS_ALLOWED", "true"), E("TZ", R("TZ"))] },
            ],
        },
        new()
        {
            Id = "jellyfin", Name = "Jellyfin", Category = SelfHostedCategory.Media,
            Description = "Media server that streams your films, series and music to apps and browsers.",
            MinMemoryMb = 1024, MinDiskGb = 5,
            Ports = [Web("jellyfin", 8096, 8096)],
            Volumes = [new("config", "jellyfin", "/config", "Settings and library database"), new("cache", "jellyfin", "/cache", "Transcoding and image cache"), new("media", "jellyfin", "/media", "Media library (choose a folder to use existing media)")],
            HealthPort = "web", HealthPath = "/health",
            FirstLogin = "Open the web address; the setup wizard creates the administrator and adds libraries from /media.",
            DocsUrl = "https://jellyfin.org/docs/general/installation/container", Licence = "GPL-2.0",
            Note = "Hardware transcoding is not configured; Docker Desktop on Windows has no GPU video acceleration for Linux containers.",
            Services = _ => [new() { Name = "jellyfin", Image = "jellyfin/jellyfin:12.4.20261026-211555", Environment = [E("TZ", R("TZ"))] }],
        },
        new()
        {
            Id = "immich", Name = "Immich", Category = SelfHostedCategory.Media,
            Description = "Photo and video backup with face recognition, maps and mobile apps.",
            MinMemoryMb = 6144, MinDiskGb = 20,
            Ports = [Web("immich-server", 2283, 2283)],
            Volumes = [new("library", "immich-server", "/data", "Uploaded photos and videos"), new("model-cache", "immich-machine-learning", "/cache", "Machine-learning models"), new("db", "database", "/var/lib/postgresql/data", "Database", true)],
            Secrets = [new("DB_PASSWORD", "Database password")],
            HealthPort = "web", HealthPath = "/api/server/ping", HealthTimeout = TimeSpan.FromMinutes(6),
            FirstLogin = "Open the web address and select Getting started; the first account becomes the administrator.",
            DocsUrl = "https://docs.immich.app/install/docker-compose", Licence = "AGPL-3.0 (Immich), PostgreSQL licence, BSD-3-Clause (Valkey)",
            Note = "Uses the official multi-service layout of Immich v3.3.6: server, machine learning, Valkey and PostgreSQL with VectorChord.",
            Services = _ =>
            [
                new() { Name = "immich-server", Image = "ghcr.io/immich-app/immich-server:v3.3.6", DependsOnStarted = ["redis", "database"],
                    Environment = [E("DB_HOSTNAME", "database"), E("DB_USERNAME", "postgres"), E("DB_PASSWORD", R("DB_PASSWORD")), E("DB_DATABASE_NAME", "immich"), E("REDIS_HOSTNAME", "redis"), E("TZ", R("TZ"))] },
                new() { Name = "immich-machine-learning", Image = "ghcr.io/immich-app/immich-machine-learning:v3.3.6", Environment = [E("TZ", R("TZ"))] },
                new() { Name = "redis", Image = "valkey/valkey:9.0.7", Healthcheck = ["CMD-SHELL", "redis-cli ping | grep -q PONG || exit 1"] },
                new() { Name = "database", Image = "ghcr.io/immich-app/postgres:14-vectorchord0.4.3-pgvectors0.2.0", ShmSize = "128mb",
                    Environment = [E("POSTGRES_PASSWORD", R("DB_PASSWORD")), E("POSTGRES_USER", "postgres"), E("POSTGRES_DB", "immich"), E("POSTGRES_INITDB_ARGS", "--data-checksums")] },
            ],
        },
        new()
        {
            Id = "paperless", Name = "Paperless-ngx", Category = SelfHostedCategory.Productivity,
            Description = "Document archive that scans, reads (OCR) and tags your paperwork.",
            MinMemoryMb = 2048, MinDiskGb = 10,
            Ports = [Web("webserver", 8000, 8000)],
            Volumes = [new("data", "webserver", "/usr/src/paperless/data", "Index and settings"), new("media", "webserver", "/usr/src/paperless/media", "Stored documents"),
                new("consume", "webserver", "/usr/src/paperless/consume", "Inbox: files placed here are imported"), new("export", "webserver", "/usr/src/paperless/export", "Exports"), new("db", "db", "/var/lib/postgresql/data", "Database", true)],
            Secrets = [new("ADMIN_PASSWORD", "Administrator password", 24), new("DB_PASSWORD", "Database password"), new("SECRET_KEY", "Session signing key", 50)],
            HealthPort = "web", HealthPath = "/accounts/login/", HealthTimeout = TimeSpan.FromMinutes(5),
            FirstLogin = "Sign in with the administrator name and password from this setup.",
            DocsUrl = "https://docs.paperless-ngx.com/setup/", Licence = "GPL-3.0 (Paperless-ngx), PostgreSQL licence, Redis tri-licence (AGPL-3.0, RSALv2, SSPLv1)",
            AdminUser = "admin",
            Services = _ =>
            [
                new() { Name = "webserver", Image = "ghcr.io/paperless-ngx/paperless-ngx:3.4.2", DependsOnHealthy = ["db", "broker"],
                    Environment = [E("PAPERLESS_REDIS", "redis://broker:6379"), E("PAPERLESS_DBHOST", "db"), E("PAPERLESS_DBUSER", "paperless"), E("PAPERLESS_DBPASS", R("DB_PASSWORD")),
                        E("PAPERLESS_SECRET_KEY", R("SECRET_KEY")), E("PAPERLESS_TIME_ZONE", R("TZ")), E("PAPERLESS_ADMIN_USER", R("ADMIN_USER")), E("PAPERLESS_ADMIN_PASSWORD", R("ADMIN_PASSWORD")),
                        E("PAPERLESS_CSRF_TRUSTED_ORIGINS", "http://localhost:${PORT_WEB},http://127.0.0.1:${PORT_WEB}," + Url())] },
                Redis("broker", false),
                Postgres("paperless", "paperless"),
            ],
        },
        new()
        {
            Id = "homeassistant", Name = "Home Assistant", Category = SelfHostedCategory.HomeAndMonitoring,
            Description = "Home automation hub for lights, sensors, energy and thousands of devices.",
            MinMemoryMb = 1024, MinDiskGb = 5,
            Ports = [Web("homeassistant", 8123, 8123)],
            Volumes = [new("config", "homeassistant", "/config", "Configuration, automations and history")],
            HealthPort = "web", HealthPath = "/manifest.json",
            FirstLogin = "Open the web address; onboarding creates the owner account.",
            DocsUrl = "https://www.home-assistant.io/installation/linux#docker-compose", Licence = "Apache-2.0",
            Note = "Runs with a published port instead of host networking (Docker Desktop has no host network for LAN devices), so automatic discovery of devices on the LAN is limited.",
            Services = _ => [new() { Name = "homeassistant", Image = "ghcr.io/home-assistant/home-assistant:2026.11.0", Environment = [E("TZ", R("TZ"))] }],
        },
        new()
        {
            Id = "uptimekuma", Name = "Uptime Kuma", Category = SelfHostedCategory.HomeAndMonitoring,
            Description = "Uptime monitor for websites, ports and services with status pages and alerts.",
            MinMemoryMb = 256, MinDiskGb = 1,
            Ports = [Web("uptime-kuma", 3001, 3001)],
            Volumes = [new("data", "uptime-kuma", "/app/data", "Monitors, history and settings")],
            HealthPort = "web", HealthPath = "/",
            FirstLogin = "Open the web address; the first page creates the administrator account.",
            DocsUrl = "https://github.com/louislam/uptime-kuma/wiki", Licence = "MIT",
            Services = _ => [new() { Name = "uptime-kuma", Image = "louislam/uptime-kuma:2.5.9", Environment = [E("TZ", R("TZ"))] }],
        },
        new()
        {
            Id = "adguardhome", Name = "AdGuard Home", Category = SelfHostedCategory.Network,
            Description = "Network-wide DNS server that blocks ads and trackers for every device.",
            MinMemoryMb = 256, MinDiskGb = 1,
            Ports = [new("setup", "adguardhome", 3000, 3000, "tcp", "First-run setup", true), new("web", "adguardhome", 80, 8081, "tcp", "Web interface after setup"), new("dns", "adguardhome", 53, 53, "tcp+udp", "DNS service")],
            Volumes = [new("work", "adguardhome", "/opt/adguardhome/work", "Query log and statistics"), new("conf", "adguardhome", "/opt/adguardhome/conf", "Configuration")],
            HealthPort = "setup", HealthPath = "/",
            FirstLogin = "Open the first-run setup address and create the administrator. Keep the admin interface on port 80 inside the container so the published web interface port reaches it after setup.",
            DocsUrl = "https://github.com/AdguardTeam/AdGuardHome/wiki/Docker", Licence = "GPL-3.0",
            Note = "Port 53 is often taken on Windows (for example by Internet Connection Sharing); choose another host port if it is in use, and point devices at this computer's address.",
            Services = _ => [new() { Name = "adguardhome", Image = "adguard/adguardhome:v0.107.80", Environment = [E("TZ", R("TZ"))] }],
        },
        new()
        {
            Id = "nginxproxymanager", Name = "Nginx Proxy Manager", Category = SelfHostedCategory.Network,
            Description = "Reverse proxy with free Let's Encrypt certificates and a simple web interface.",
            MinMemoryMb = 256, MinDiskGb = 1,
            Ports = [new("admin", "app", 81, 81, "tcp", "Administration interface", true), new("http", "app", 80, 80, "tcp", "HTTP proxy"), new("https", "app", 443, 443, "tcp", "HTTPS proxy")],
            Volumes = [new("data", "app", "/data", "Proxy hosts, settings and database"), new("letsencrypt", "app", "/etc/letsencrypt", "Certificates")],
            Secrets = [new("ADMIN_PASSWORD", "Administrator password", 24)],
            HealthPort = "admin", HealthPath = "/api/",
            FirstLogin = "Sign in on the administration interface with the administrator e-mail and password from this setup.",
            DocsUrl = "https://nginxproxymanager.com/setup/", Licence = "MIT",
            AdminUser = "admin@example.com", AdminIsEmail = true,
            Services = _ =>
            [
                new() { Name = "app", Image = "jc21/nginx-proxy-manager:2.16.0",
                    Environment = [E("INITIAL_ADMIN_EMAIL", R("ADMIN_USER")), E("INITIAL_ADMIN_PASSWORD", R("ADMIN_PASSWORD")), E("TZ", R("TZ"))] },
            ],
        },
        new()
        {
            Id = "grafana", Name = "Grafana and Prometheus", Category = SelfHostedCategory.HomeAndMonitoring,
            Description = "Metrics collection with Prometheus and dashboards with Grafana.",
            MinMemoryMb = 1024, MinDiskGb = 5,
            Ports = [Web("grafana", 3000, 3002, "Grafana dashboards"), new("prometheus", "prometheus", 9090, 9090, "tcp", "Prometheus metrics")],
            Volumes = [new("grafana", "grafana", "/var/lib/grafana", "Dashboards, users and settings"), new("prometheus", "prometheus", "/prometheus", "Metrics data")],
            Secrets = [new("ADMIN_PASSWORD", "Grafana administrator password", 24)],
            HealthPort = "web", HealthPath = "/api/health",
            FirstLogin = "Sign in to Grafana with the administrator name and password from this setup, then add Prometheus as a data source at http://prometheus:9090.",
            DocsUrl = "https://grafana.com/docs/grafana/latest/setup-grafana/installation/docker/", Licence = "AGPL-3.0 (Grafana), Apache-2.0 (Prometheus)",
            AdminUser = "admin",
            Services = _ =>
            [
                new() { Name = "grafana", Image = "grafana/grafana:13.3.0", DependsOnStarted = ["prometheus"],
                    Environment = [E("GF_SECURITY_ADMIN_USER", R("ADMIN_USER")), E("GF_SECURITY_ADMIN_PASSWORD", R("ADMIN_PASSWORD")), E("TZ", R("TZ"))] },
                new() { Name = "prometheus", Image = "prom/prometheus:v3.16.0" },
            ],
        },
        new()
        {
            Id = "rustfs", Name = "RustFS object storage", Category = SelfHostedCategory.FilesAndSync,
            Description = "S3-compatible object storage with a web console, in place of MinIO.",
            MinMemoryMb = 512, MinDiskGb = 5,
            Ports = [new("console", "rustfs", 9001, 9001, "tcp", "Web console", true), new("api", "rustfs", 9000, 9000, "tcp", "S3 API endpoint")],
            Volumes = [new("data", "rustfs", "/data", "Buckets and objects")],
            Secrets = [new("ACCESS_KEY", "Access key (user name)", 20), new("SECRET_KEY", "Secret key (password)", 40)],
            HealthPort = "api", HealthPath = "/health",
            FirstLogin = "Sign in to the web console with the generated access key and secret key; use the same pair in S3 clients against the S3 API port.",
            DocsUrl = "https://docs.rustfs.com/", Licence = "Apache-2.0",
            Note = "MinIO stopped publishing community container images in 2025 (its last images are no longer pullable), so this setup uses RustFS, an S3-compatible server with a similar console.",
            Services = _ =>
            [
                new() { Name = "rustfs", Image = "rustfs/rustfs:1.0.4",
                    Environment = [E("RUSTFS_ACCESS_KEY", R("ACCESS_KEY")), E("RUSTFS_SECRET_KEY", R("SECRET_KEY")), E("RUSTFS_VOLUMES", "/data"), E("RUSTFS_ADDRESS", ":9000"), E("RUSTFS_CONSOLE_ADDRESS", ":9001"), E("RUSTFS_CONSOLE_ENABLE", "true")] },
            ],
        },
        new()
        {
            Id = "n8n", Name = "n8n", Category = SelfHostedCategory.Productivity,
            Description = "Workflow automation that connects apps and APIs with a visual editor.",
            MinMemoryMb = 1024, MinDiskGb = 2,
            Ports = [Web("n8n", 5678, 5678)],
            Volumes = [new("data", "n8n", "/home/node/.n8n", "Workflows, credentials and settings")],
            Secrets = [new("ENCRYPTION_KEY", "Key that encrypts stored credentials", 40)],
            HealthPort = "web", HealthPath = "/healthz",
            FirstLogin = "Open the web address; the first page creates the owner account.",
            DocsUrl = "https://docs.n8n.io/hosting/installation/docker/", Licence = "Sustainable Use License (fair-code, source available; not an OSI open-source licence)",
            Note = "Secure cookies are turned off so the editor works over plain HTTP on the LAN; put n8n behind HTTPS before exposing it further.",
            Services = _ =>
            [
                new() { Name = "n8n", Image = "n8nio/n8n:2.46.6",
                    Environment = [E("N8N_ENCRYPTION_KEY", R("ENCRYPTION_KEY")), E("GENERIC_TIMEZONE", R("TZ")), E("TZ", R("TZ")), E("N8N_SECURE_COOKIE", "false"), E("WEBHOOK_URL", Url() + "/")] },
            ],
        },
        new()
        {
            Id = "codeserver", Name = "code-server", Category = SelfHostedCategory.Development,
            Description = "Visual Studio Code in the browser, running in a container.",
            MinMemoryMb = 1024, MinDiskGb = 2,
            Ports = [Web("code-server", 8080, 8443)],
            Volumes = [new("config", "code-server", "/home/coder/.config", "Settings and extensions"), new("project", "code-server", "/home/coder/project", "Project files")],
            Secrets = [new("PASSWORD", "Sign-in password", 24)],
            HealthPort = "web", HealthPath = "/healthz",
            FirstLogin = "Sign in with the generated password.",
            DocsUrl = "https://coder.com/docs/code-server/install#docker", Licence = "MIT",
            Services = _ => [new() { Name = "code-server", Image = "codercom/code-server:4.145.2", Environment = [E("PASSWORD", R("PASSWORD")), E("TZ", R("TZ"))] }],
        },
        new()
        {
            Id = "wordpress", Name = "WordPress", Category = SelfHostedCategory.Productivity,
            Description = "Website and blog platform with themes and plugins.",
            MinMemoryMb = 512, MinDiskGb = 2,
            Ports = [Web("wordpress", 80, 8085)],
            Volumes = [new("html", "wordpress", "/var/www/html", "WordPress files, themes, plugins and uploads"), new("db", "db", "/var/lib/mysql", "Database", true)],
            Secrets = [new("DB_PASSWORD", "Database password"), new("DB_ROOT_PASSWORD", "Database root password")],
            HealthPort = "web", HealthPath = "/wp-login.php",
            FirstLogin = "Open the web address; the installation page asks for the site title and creates the administrator.",
            DocsUrl = "https://hub.docker.com/_/wordpress", Licence = "GPL-2.0-or-later (WordPress), GPL-2.0 (MariaDB)",
            Note = "WordPress stores its own address on first install; install it from the address other devices will use.",
            Services = _ =>
            [
                new() { Name = "wordpress", Image = "wordpress:7.1.4-apache", DependsOnHealthy = ["db"],
                    Environment = [E("WORDPRESS_DB_HOST", "db"), E("WORDPRESS_DB_USER", "wordpress"), E("WORDPRESS_DB_PASSWORD", R("DB_PASSWORD")), E("WORDPRESS_DB_NAME", "wordpress")] },
                MariaDb("wordpress", "wordpress"),
            ],
        },
        new()
        {
            Id = "bookstack", Name = "BookStack", Category = SelfHostedCategory.Productivity,
            Description = "Wiki for documentation organised as shelves, books, chapters and pages.",
            MinMemoryMb = 512, MinDiskGb = 2,
            Ports = [Web("bookstack", 80, 6875)],
            Volumes = [new("config", "bookstack", "/config", "Uploads, settings and logs"), new("db", "db", "/var/lib/mysql", "Database", true)],
            Secrets = [new("APP_KEY", "Application encryption key", 32, true), new("DB_PASSWORD", "Database password"), new("DB_ROOT_PASSWORD", "Database root password")],
            HealthPort = "web", HealthPath = "/login", HealthTimeout = TimeSpan.FromMinutes(5),
            FirstLogin = "Sign in as admin@admin.com with the password 'password', then change both immediately in your profile.",
            DocsUrl = "https://docs.linuxserver.io/images/docker-bookstack/", Licence = "MIT (BookStack), GPL-3.0 (LinuxServer.io image), GPL-2.0 (MariaDB)",
            Services = _ =>
            [
                new() { Name = "bookstack", Image = "lscr.io/linuxserver/bookstack:version-v26.09.2", DependsOnHealthy = ["db"],
                    Environment = [E("PUID", "1000"), E("PGID", "1000"), E("TZ", R("TZ")), E("APP_URL", Url()), E("APP_KEY", R("APP_KEY")),
                        E("DB_HOST", "db"), E("DB_PORT", "3306"), E("DB_USERNAME", "bookstack"), E("DB_PASSWORD", R("DB_PASSWORD")), E("DB_DATABASE", "bookstack")] },
                MariaDb("bookstack", "bookstack"),
            ],
        },
        new()
        {
            Id = "freshrss", Name = "FreshRSS", Category = SelfHostedCategory.Productivity,
            Description = "Feed reader for RSS and Atom with mobile app support.",
            MinMemoryMb = 128, MinDiskGb = 1,
            Ports = [Web("freshrss", 80, 8086)],
            Volumes = [new("data", "freshrss", "/var/www/FreshRSS/data", "Feeds, users and settings"), new("extensions", "freshrss", "/var/www/FreshRSS/extensions", "Extensions")],
            HealthPort = "web", HealthPath = "/i/",
            FirstLogin = "Open the web address; the installation page sets the language, database (SQLite is fine) and administrator.",
            DocsUrl = "https://github.com/FreshRSS/FreshRSS/tree/edge/Docker", Licence = "AGPL-3.0",
            Services = _ => [new() { Name = "freshrss", Image = "freshrss/freshrss:1.30.4", Environment = [E("TZ", R("TZ")), E("CRON_MIN", "3,33")] }],
        },
        new()
        {
            Id = "syncthing", Name = "Syncthing", Category = SelfHostedCategory.FilesAndSync,
            Description = "Peer-to-peer folder synchronisation between your devices.",
            MinMemoryMb = 256, MinDiskGb = 1,
            Ports = [Web("syncthing", 8384, 8384), new("sync", "syncthing", 22000, 22000, "tcp+udp", "File transfers"), new("discovery", "syncthing", 21027, 21027, "udp", "Local discovery")],
            Volumes = [new("data", "syncthing", "/var/syncthing", "Synchronised folders and settings")],
            HealthPort = "web", HealthPath = "/rest/noauth/health",
            FirstLogin = "Open the web address and set a user name and password under Actions, Settings, GUI; Syncthing warns until you do.",
            DocsUrl = "https://github.com/syncthing/syncthing/blob/main/README-Docker.md", Licence = "MPL-2.0",
            Note = "Local discovery broadcasts do not cross the Docker Desktop network; add devices by ID if they do not appear.",
            Services = _ => [new() { Name = "syncthing", Image = "syncthing/syncthing:2.1.7", Environment = [E("PUID", "1000"), E("PGID", "1000"), E("TZ", R("TZ"))] }],
        },
        new()
        {
            Id = "openwebui", Name = "Open WebUI and Ollama", Category = SelfHostedCategory.AI,
            Description = "Chat with local language models: Ollama runs the models, Open WebUI is the interface.",
            MinMemoryMb = 8192, MinDiskGb = 20,
            Ports = [Web("open-webui", 8080, 3003)],
            Volumes = [new("data", "open-webui", "/app/backend/data", "Chats, users and settings"), new("models", "ollama", "/root/.ollama", "Downloaded models")],
            Secrets = [new("SECRET_KEY", "Session signing key", 48)],
            HealthPort = "web", HealthPath = "/health", HealthTimeout = TimeSpan.FromMinutes(8),
            FirstLogin = "Open the web address and create an account; the first account becomes the administrator. Download a model from the model selector (for example llama3.2).",
            DocsUrl = "https://docs.openwebui.com/getting-started/quick-start/", Licence = "Open WebUI License (BSD-3-Clause with a branding clause), MIT (Ollama)",
            Note = "Models run on the CPU unless Docker Desktop has GPU support enabled; large models need much more memory than the minimum shown.",
            Services = _ =>
            [
                new() { Name = "open-webui", Image = "ghcr.io/open-webui/open-webui:v0.13.3", DependsOnStarted = ["ollama"],
                    Environment = [E("OLLAMA_BASE_URL", "http://ollama:11434"), E("WEBUI_SECRET_KEY", R("SECRET_KEY")), E("TZ", R("TZ"))] },
                new() { Name = "ollama", Image = "ollama/ollama:0.40.15" },
            ],
        },
    ];

    public static SelfHostedApp? Find(string id) => All.FirstOrDefault(a => a.Id == id);

    /// <summary>Text the catalogue search matches against.</summary>
    public static IReadOnlyList<string> SearchFields(SelfHostedApp app) => [app.Name, app.Description, CategoryName(app.Category), app.Id, string.Join(" ", app.AllImages())];
}
