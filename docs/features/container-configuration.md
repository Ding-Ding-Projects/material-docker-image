# Container configuration

Select an image from a refreshed inventory, then choose Run. The dialog remains bound to the engine context and generation that produced the selection. A changed context invalidates the selection.

## Ports and storage

The basic port field uses `host:container`, optionally followed by `/tcp` or `/udp`, and binds to loopback. Additional port rows expose the bind address, host port, container port and protocol independently. An all-interface address publishes more broadly and must be chosen intentionally.

Additional mount rows distinguish named volumes from bind paths, provide a container destination, and default to read-only. Browse chooses a folder on this computer. A bind source belongs to the selected engine host, which may be remote; the application does not incorrectly require a remote path to exist locally. Confirm that the engine can access any chosen source.

## Execution and environment

Command arguments are a JSON string array, for example `["/bin/echo", "hello world"]`. Each string remains a separate argument after the image operand. A shell inside a container runs only if explicitly selected as the container executable; the application does not invoke a host shell for Docker operations.

Environment entries use `NAME=value`, one per line. Everything after the first equals sign is preserved, including trailing spaces. Enter non-secret values only. Entrypoint, container user and working directory can be overridden independently; blank values retain the image behavior where Docker permits it.

## Resources and security

CPU and memory limits, restart policy, read-only root filesystem and prevention of new privileges are exposed as real controls. The engine validates their final compatibility with the chosen image and host. Preventing new privileges is enabled in this editor by default and may affect images that rely on set-user-ID behavior.

No privileged mode or host networking is enabled by this dialog. Interactive terminals open from Containers › Exec › Open terminal as `docker exec -it <container> <shell>` in a separate console window. By default Windows chooses the console through the default terminal application; Settings › Engine › Open terminals in a classic console window starts `conhost.exe` directly instead, which always opens its own window next to the app (verified on a desktop host with an alpine container, see evidence/interactive-2026-11/receipt.json). Health checks, device mappings and GPU access are described below. Compose remains available for project configuration, but does not constitute acceptance evidence for absent graphical controls.

## Health checks, devices and GPUs

Restart and health holds the health-check fields. Each field maps to one flag and is validated before Run is enabled: Health command (--health-cmd, run through the container shell), Interval (--health-interval), Timeout (--health-timeout), Retries (--health-retries, a positive whole number), Start period (--health-start-period), Start interval (--health-start-interval, needs Docker Engine 25 or later) and Disable the image health check (--no-healthcheck). Durations use Go syntax such as 30s, 1m30s or 500ms, must be at least 1ms, and zero is refused. Disabling the check cannot be combined with a command or timing option.

Resources holds GPU and device access. GPUs accepts all, a positive count, or device IDs such as device=0,1, with optional capabilities such as compute and utility; capabilities are always sent as one quoted field because the engine rejects an unquoted list. Device rows map a host path to a container path with permissions that are a non-repeating subset of r, w and m, and two rows cannot claim the same container path. Device cgroup rules take one rule per line, for example c 189:* rwm. Device paths belong to the engine host, which may not be this computer. A warning appears whenever GPUs, devices or rules are set.

The Containers inspector Overview lists the configured health check, the live status, the failing streak and the last probe outputs. While a running container with a health check is open on Overview, the inspector re-reads it every five seconds and redraws only when the status, streak or latest probe changes. Below that, Devices and GPUs lists configured devices, cgroup rules and the engine DeviceRequests. If the engine reports a start error that mentions a missing device driver, the inspector says the engine has no GPU support; a failed Run says the same instead of leaving only the raw engine text.

## Verification

A controlled native GUI build/run test verified exact fixture output, a read-only filesystem, disabled networking and owned resource removal. Input regressions cover argument separation, whitespace, IPv4/IPv6 ports, mount-option injection, remote bind paths, CPU/memory validation and supported label keys. Separate engines and resource combinations require their own runtime checks.

Health, device and GPU argument behaviour was also checked with the docker command line against the local Docker Desktop Linux engine (29.6.1); the receipt is evidence/container-controls-2026-11.json. Focused checks cover argument building, validation and parsing of real inspect output. The editor fields, the inspector refresh and a container that actually uses a GPU or host device have not been exercised in the built application, so these three features are implemented with verification pending.

## 廣東話

先喺重新整理過嘅映像清單揀項目，再開 Run。對話框會綁定原本引擎，轉咗引擎就要重新整理。連接埠預設只綁回環位址，額外欄位可以逐項揀位址、連接埠同協定。掛載路徑屬於引擎主機，唔一定係呢部電腦。參數用 JSON 字串陣列，環境值保留等號後面所有字元。資源限制同安全選項都會實際傳畀引擎；互動終端會繼續列為未完成。

「重啟同健康」有健康檢查欄位（指令、間隔、逾時、重試、啟動期、啟動期間隔同停用），「資源」有 GPU（all、數量或者裝置 ID，另可加功能）、裝置對應（權限只可以係 r、w、m 嘅不重複組合）同裝置 cgroup 規則。容器檢視會顯示健康狀態、連續失敗次數、最近檢查輸出、已設定裝置同 GPU 要求；引擎冇 GPU 支援時會如實講明。參數行為已用本機 Docker 引擎核對，但未喺已建置應用程式內實際操作，所以仍係「已實作，驗證待完成」。
