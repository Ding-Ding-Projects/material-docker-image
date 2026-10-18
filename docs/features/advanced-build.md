# Advanced image builds

The Build workspace keeps the Dockerfile editor beside the selected local context. Advanced controls expand in place and use structured Docker CLI arguments.

## Outputs

- **Load into local engine** is the default. This editor accepts one platform for a local load.
- **Push to registry** accepts multiple tags and platforms and requires confirmation naming the destination tags. Docker retains ownership of registry authentication.
- **Export OCI archive** writes to a selected local archive path. The parent directory must exist, and replacing an existing archive requires confirmation. Exporter availability depends on the installed builder.

The Image tag field accepts comma-separated or line-separated tags. Labels use one `key=value` per line. Label keys support namespace punctuation; values retain spaces exactly. The application does not shell-interpret values.

## Cache

Cache import and export accept one BuildKit specification per line, such as `type=local,src=C:/cache` or `type=registry,ref=registry.example/team/cache`. The installed builder decides which backends it supports. Export is a write operation and requires confirmation. Do not place credentials in these fields.

## BuildKit secret files

Add a secret row, choose an existing local file, and enter the identifier used by the Dockerfile's `RUN --mount=type=secret` instruction. The application passes the selected path to BuildKit without reading or persisting the file's contents. Selections remain in the current window only. The Dockerfile itself must avoid printing secrets or copying them into an image.

## Check before building

**Check Dockerfile with BuildKit** invokes the installed builder's actual `--check` route against the saved Dockerfile. Unsupported builder versions report their real error. A check is distinct from a successful build, image load, registry publication or container health result.

## Limits and verification

Input validation has focused regression coverage. Successful local builds have been exercised separately from remote registry, cache-backend and multi-architecture acceptance. Those latter paths remain capability-dependent and are not inferred from a local build.

## 廣東話

建置頁可以揀載入本機引擎、發布去登錄服務，或者匯出 OCI 封存檔。發布同覆寫都會清楚確認目的地。標籤值保留原本空格，唔經主機命令殼層。快取支援程度由實際建置器決定。秘密檔案只傳路徑畀 BuildKit，介面唔讀取內容；Dockerfile 本身亦唔可以印出或者複製秘密。檢查通過唔等於建置、發布或者服務健康已經通過。
