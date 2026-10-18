# Diagnostics and safety

Diagnostics records actual CLI and engine responses. Separate read-only inspection from a smoke test that creates resources. Controlled smoke tests use unique owned names, labels, and exact teardown targets. Failed teardown remains an unsuccessful overall result.

Commands are invoked without a shell. User values become individual process arguments. Docker context selection is per command. Existing Docker credential helpers retain ownership of authentication; credentials must not enter command previews, application logs or diagnostic exports.

Output is bounded to protect memory. A truncated output buffer must be identified as truncated, never described as complete. Cancellation can stop the CLI before the engine has reconciled its work. Refresh state and inspect the resource rather than assuming rollback.

Only run trusted build projects. Docker engine access carries substantial authority over its host. This application does not make an untrusted Dockerfile, image, bind mount or Compose project safe.

## Sources

- [Docker buildx build](https://docs.docker.com/reference/cli/docker/buildx/build/)
- [Protect access to the Docker daemon](https://docs.docker.com/engine/security/protect-access/)
- [Docker CLI reference](https://docs.docker.com/reference/cli/docker/)

## 廣東話

診斷結果要分清楚唯讀檢查同會建立資源嘅測試。測試只可以清理自己建立嘅項目。指令用獨立參數執行，唔經命令殼層。取消本機指令唔保證引擎已經回復原狀，要重新讀取狀態。只執行可信嘅建置專案。
