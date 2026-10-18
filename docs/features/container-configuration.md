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

No privileged mode or host networking is enabled by this dialog. GPU/device configuration, health-check editing and interactive terminals remain documented gaps. Compose remains available for project configuration, but does not constitute acceptance evidence for absent graphical controls.

## Verification

A controlled native GUI build/run test verified exact fixture output, a read-only filesystem, disabled networking and owned resource removal. Input regressions cover argument separation, whitespace, IPv4/IPv6 ports, mount-option injection, remote bind paths, CPU/memory validation and supported label keys. Separate engines and resource combinations require their own runtime checks.

## 廣東話

先喺重新整理過嘅映像清單揀項目，再開 Run。對話框會綁定原本引擎，轉咗引擎就要重新整理。連接埠預設只綁回環位址，額外欄位可以逐項揀位址、連接埠同協定。掛載路徑屬於引擎主機，唔一定係呢部電腦。參數用 JSON 字串陣列，環境值保留等號後面所有字元。資源限制同安全選項都會實際傳畀引擎；未有嘅 GPU、裝置、健康檢查編輯同互動終端會繼續列為未完成。
