# Dockerfile Creator: how much more advanced does it need to be?

Research date: 2026-11-03. Baseline: Dockerfile frontend `docker/dockerfile:1.26` (latest stable, July 2026). Upstream master already documents 1.27 changes.

## 1. Executive summary

- **Syntax coverage: about 70% shipped today, about 92% once the in-progress work lands.** Every one of the 18 instructions is already modelled. Most of the remaining gaps are in flags and edge cases. The in-progress list (RUN mounts, network and security, heredocs, COPY `--parents`/`--exclude`, ADD git/URL with `--checksum`, global ARGs, platform helpers, `# check=`) covers nearly all of the stable syntax. The items still missing after that are `ADD --unpack`, `--keep-git-dir`, `--link`, symbolic `--chmod` (1.14), `HEALTHCHECK --start-interval`/`NONE`, the `# escape=` and `# syntax=` directives as first-class objects, and stage/ARG description comments. `RUN --device` (GA in 1.27) and the pattern variable modifiers (`${v#p}` and similar, upstream master only) should be supported as labs features behind a syntax pin.
- **Lint coverage: about 35–40% of the useful rule surface.** Docker's own build checks have 22 rules (21 documented plus `InvalidBaseImagePlatform`). Hadolint adds about 70 DL rules and about 40 ShellCheck rules. The planned "more lint rules" work should first reach parity with all 22 BuildKit checks (exact names, so `# check=skip=` works), then take about 20 high-value hadolint rules. Ideally the app would also run `docker build --check` (or `docker buildx build --call=check`) as the authoritative engine.
- **Assistance features: about 20%.** Competitors increasingly differentiate on *knowledge* rather than syntax:
  - hover docs per instruction and flag
  - image tag completion from the registry
  - one-click digest pinning
  - base-image recommendations with CVE deltas (Docker Scout, the Docker DX extension)
  - Hardened/distroless migration (DHI, Gordon)
  - build-time insights (Docker Desktop Builds view: timing, cache use, dependencies, inline errors)
  - step debugging (Buildx DAP in Docker DX)

  Of these, the app currently offers only live preview, lint and auto-fix.
- **Project detection is behind `docker init` and Railpack.** `docker init` has 8 templates (ASP.NET Core, Go, Java/Maven, Node, PHP+Apache, Python, Rust, Other) and generates 4 files: Dockerfile, compose.yaml, .dockerignore and README.Docker.md. Railpack auto-detects 17 ecosystems and reads version files (`.nvmrc`, `go.mod`, `global.json`, `.tool-versions`, `mise.toml`, and others). The app's starters are a good base. Detection should read version files and lockfiles, pick the package manager (npm/pnpm/yarn/bun, pip/uv/poetry, Maven/Gradle), and also emit compose.yaml and .dockerignore.
- **The riskiest in-progress item is two-way live-preview editing.** It needs a lossless (full-fidelity) syntax tree that keeps comments, blank lines, continuation layout and unknown or partial text. Without one, round-tripping will silently destroy user formatting. This is the main architectural prerequisite and should come before more GUI features.
- **Overall estimate: about 55% of a best-in-class assisted Dockerfile authoring tool today, about 70% after the in-progress list.** Reaching "advanced" needs roughly 6 P0 items, 10 P1 items and a handful of P2 items (section 2). The total is on the order of two to three medium-sized milestones. The P0 set is mostly correctness (round-trip fidelity, full check parity, syntax-version awareness) rather than new surface area.

## 2. Gap checklist (prioritised)

Effort: S = days, M = 1–3 weeks, L = more than 3 weeks. "In progress" items are marked so that they can be checked against this list.

| Pri | Feature | Why | Effort |
|---|---|---|---|
| P0 | Lossless syntax tree (concrete syntax tree, CST) as the single source of truth: trivia/comments attached to nodes, error nodes for partial input, minimal text edits from GUI changes | Required for two-way sync, import fidelity and safe auto-fixes. It prevents formatting churn in diffs. | L |
| P0 | Syntax-version awareness: each feature knows its minimum frontend (table in section 3). Auto-insert or upgrade `# syntax=docker/dockerfile:1.N` (or `1-labs`) when a newer feature is used, and warn when a pinned version is too old. | Using `COPY --parents` (1.20) or `--exclude` (1.19) under an old pin fails the build. Labs features need `1-labs`. | M |
| P0 | Parity with all 22 BuildKit build checks, using the exact rule names, with `# check=skip=…;error=true;experimental=…` editing | Users already see these in `docker build`. Matching names makes skip directives and CI behaviour consistent. | M |
| P0 | Run the real checker: `docker build --check` or `docker buildx build --call=check`, mapping results to cards and lines. Optional `--call=outline`/`targets` for the stage and ARG overview. | Authoritative and always current. Covers rules that need the context, such as `CopyIgnoredFile` and `UndefinedArgInFrom`. | M |
| P0 | Finish the in-progress RUN `--mount` editor with all options per type (table 3.3), including `uid`/`gid`/`mode` for non-root and `env=` for secrets (1.10) | Cache, secret and ssh mounts are the biggest wins for speed and security. | M (in progress) |
| P0 | Cache-mount presets per package manager with correct paths and `sharing` (section 5), plus `uid`/`gid` matched to the stage USER | Wrong paths are a silent no-op, and a wrong `sharing` value corrupts apt. These are the most common mistakes. | S |
| P1 | Hover/inline docs for every instruction, flag and option, with links to docs.docker.com anchors | Standard in VS Code (rcjsuen language service) and JetBrains. Very cheap to add from a static table. | S |
| P1 | Image reference assistant: tag completion from the registry (Docker Hub, GHCR, MCR, dhi.io), "pin digest" button resolving the current manifest-list digest, a "refresh digest" action, and keeping `tag@sha256:` together | Docker best practice. Docker DX flags `notPinnedDigest` and suggests tags. | M |
| P1 | Base-image recommendation panel: slimmer or hardened alternatives (DHI `dev`/runtime pairs, distroless, `-slim`/`-alpine`), with CVE and size deltas from `docker scout recommendations`/`cves` when available | Docker Scout and DX provide this. It is a strong differentiator for a native app. | M–L |
| P1 | Hardened-image migration wizard: build stage on `-dev`/`-sdk`, runtime stage without a shell; check for shell-form CMD, RUN in the final stage, ports below 1024, UID 65532, and redundant ca-certificates installs | This is the DHI migration checklist turned into lint rules plus a wizard. | M |
| P1 | Project detection (`docker init`-like), in progress. Read version files and lockfiles, choose the package manager, emit compose.yaml, .dockerignore and README.Docker.md, and offer an "Other" template. | Parity with `docker init` and Railpack detection depth. | M (in progress) |
| P1 | .dockerignore editor, in progress. Supports `**`, `!` exceptions with last-match-wins, Go `filepath.Match` semantics and the per-Dockerfile `<name>.Dockerfile.dockerignore` override, with a live context tree and size. Link to `CopyIgnoredFile`. | Context size is a top cause of slow builds. Last-match-wins and per-Dockerfile ignore files are easy to get wrong. | M (in progress) |
| P1 | About 20 hadolint rules (section 4.2), with auto-fixes where safe | They cover package-manager hygiene that BuildKit checks do not. | M |
| P1 | Heredoc cards (in progress): `RUN <<EOF`, `<<-EOF` tab stripping, quoted delimiter to disable expansion, multiple heredocs, `COPY <<EOF /path` inline files, shebang interpreter | Heredocs are now idiomatic for multi-line RUN and inline config files. | M (in progress) |
| P1 | Global ARG and platform helpers (in progress): `ARG` before the first `FROM`, re-declaration inside a stage, `FROM --platform=$BUILDPLATFORM` cross-compile template, and `TARGETOS/TARGETARCH` wiring for Go, Rust and .NET | Multi-platform builds are a common request, and the ARG scoping rules confuse users. | S–M (in progress) |
| P1 | Remaining COPY/ADD flags: `--link`, `--chmod` symbolic (1.14), `ADD --unpack` (1.17), `--keep-git-dir`, git URL fragment builder (`#ref:subdir`), and `HTTP_AUTH_*` secret hint | These complete the stable flag surface. | S |
| P1 | HEALTHCHECK `--start-interval` (Engine 25+), `HEALTHCHECK NONE`, a duration validator, and exit-code guidance (0/1, never 2) | Missing stable options. | S |
| P1 | Secrets scanning: flag literal tokens, keys and passwords in ENV/ARG/LABEL/RUN and suggest a `--mount=type=secret,env=…` rewrite | Extends `SecretsUsedInArgOrEnv` into a fix-it. | S–M |
| P2 | Build-and-inspect loop: build from the GUI, then show per-step timing, cache hit or miss, and per-layer size (from `docker history`/buildx history), mapped back onto cards; inline errors on the failing card (as in the Desktop Builds view) | This is what Docker Desktop and Build Cloud show. It closes the edit→build→fix loop. | L |
| P2 | Multi-platform matrix: choose platforms, show which base images support them (manifest list), warn on `FromPlatformFlagConstDisallowed` and `InvalidBaseImagePlatform` | Common need for ARM and Windows users. | M |
| P2 | Supply-chain defaults panel: provenance `mode=max`, `--sbom=true`, `SOURCE_DATE_EPOCH`, OCI labels (`org.opencontainers.image.*`) with validation (hadolint DL3048–DL3058) | Provenance `mode=min` is already on by default. SBOM is opt-in. | S–M |
| P2 | Stage graph view: DAG of stages and `COPY --from`/`--mount from=` edges, target selector, unused-stage detection, description comments for `--call=outline` | Multi-stage readability. Description comments feed Docker's own outline. | M |
| P2 | Labs features behind a toggle: `RUN --device` (labs 1.14, GA in 1.27), `${v#p}`/`${v%p}`/`${v/p/r}` modifiers (upstream master only) | Forward compatibility. Must force the correct `# syntax`. | S |
| P2 | Step debugger integration (`docker buildx dap` / Docker DX debugging) | Advanced feature, low priority for a GUI creator. | L |
| P2 | Compose/Bake co-generation: one Bake target per stage, compose `build.target` | The DX extension offers Bake-target generation from stages. | M |
| P2 | Windows-container specifics: `# escape=`` ` `` for Windows paths, PowerShell `SHELL` presets, servercore/nanoserver tags | Natural fit for a native Windows app. Windows builds use the legacy builder, so no Builds view and no BuildKit-only flags apply. | S–M |

## 3. Dockerfile syntax coverage

Legend for "Ours": **Yes** = shipped; **IP** = in progress; **Partial** = shipped without all options; **No** = not planned. The version column gives the minimum `docker/dockerfile` frontend from the official reference option tables. "–" means no flag-level minimum (baseline BuildKit).

### 3.1 Parser directives, format, variables

| Feature | Syntax | Frontend / status | Ours |
|---|---|---|---|
| Syntax directive | `# syntax=docker/dockerfile:1` (or `:1.N`, `:1-labs`, `dockerfile-upstream:master`) | – (pin recommended) | Partial: not modelled as a version selector |
| Escape directive | `# escape=\` or `` # escape=` `` | – | No |
| Check directive | `# check=skip=A,B;error=true` and `experimental=all\|A,B` | 1.8 | IP |
| Comments and continuations | `#` at line start, `\` continuation; empty continuation lines warned (`NoEmptyContinuation`) | – | Partial (needs lossless round-trip) |
| Stage and ARG description comments | `# <name> <description>` immediately above `FROM … AS name` / `ARG name` (used by `--call=outline`) | experimental check `InvalidDefinitionDescription` | No |
| `$var`, `${var}`, `\$` escape | | – | Partial |
| `${v:-w}` `${v-w}` `${v:+w}` `${v+w}` | | stable | Partial (verify the preview evaluates them) |
| `${v#p}` `${v##p}` `${v%p}` `${v%%p}` `${v/p/r}` `${v//p/r}` | | **upstream master only** (pre-release) | No (P2 labs) |
| Shell vs exec (JSON) form | RUN/CMD/ENTRYPOINT; exec form does not substitute variables | – | Yes (assumed; confirm JSON escaping of backslashes) |
| Here-documents | `RUN <<EOF`, `<<-EOF`, `<<"EOF"`, multiple heredocs, `COPY <<EOF /file` | 1.4+ | IP |

### 3.2 Instructions and flags

| Instruction | Flags / forms | Frontend / status | Ours |
|---|---|---|---|
| FROM | `FROM [--platform=…] image[:tag][@digest] [AS name]`; ARGs before FROM; `scratch` | – | Yes (stages); `--platform` and digest Partial |
| RUN | shell/exec/heredoc | – | Yes |
| RUN --mount | bind (default), cache, tmpfs, secret, ssh | 1.2 | IP |
| RUN --network | `default` \| `none` \| `host` (host requires `network.host` entitlement) | 1.3 | IP |
| RUN --security | `sandbox` \| `insecure` (requires `security.insecure` entitlement) | **1.20** (labs before) | IP |
| RUN --device | `--device=name[,required]` (CDI, `device` entitlement, BuildKit 0.20+) | labs 1.14; **1.27** stable per master docs | No |
| CMD | exec/shell; only the last one counts | – | Yes |
| LABEL | `k=v …`; OCI keys | – | Yes |
| MAINTAINER | deprecated → LABEL | – | Import should convert (`MaintainerDeprecated`) |
| EXPOSE | `port[/tcp\|/udp]`, lowercase proto, no IP or host mapping | – | Yes (check casing/format rules) |
| ENV | `k=v …` (legacy `k v` warned) | – | Yes |
| ADD | local, tar auto-extract, URL, git (HTTPS/SSH, `#ref:subdir`) | – | Yes / IP (git, URL) |
| ADD --keep-git-dir | `=true` | 1.1 | Not listed → add |
| ADD --checksum | git: commit SHA prefix; HTTP: `sha256:` only | 1.6 | IP |
| ADD --chmod / --chown | as COPY | 1.2 / – | Partial? (confirm) |
| ADD --link | | 1.4 | No |
| ADD --unpack | `=true\|false` (remote tar not unpacked by default) | 1.17 | No |
| ADD --exclude | repeatable | **1.19** (labs before) | IP |
| COPY --from | stage, image or named context | – | Yes (assumed) |
| COPY --chmod | octal; **symbolic** (`u=rwX,go=rX`, `+x`) | 1.2; symbolic 1.14 | Partial |
| COPY --chown | `user:group` or numeric | – | Yes (assumed) |
| COPY --link | | 1.4 | No |
| COPY --parents | `--parents[=bool]`, `./` pivot, `**` | **1.20** (labs 1.7 before) | IP |
| COPY --exclude | repeatable, `filepath.Match` | **1.19** (labs 1.7 before) | IP |
| ENTRYPOINT | exec/shell; CMD interaction | – | Yes |
| VOLUME | JSON or space list | – | Yes |
| USER | `user[:group]` or `uid[:gid]` | – | Yes |
| WORKDIR | absolute recommended (`WorkdirRelativePath`) | – | Yes |
| ARG | default values, global scope, stage re-declaration | – | Yes; global IP |
| Automatic platform ARGs | `TARGETPLATFORM/OS/ARCH/VARIANT`, `BUILDPLATFORM/OS/ARCH/VARIANT` (global scope only) | BuildKit | IP |
| Predefined proxy ARGs | `HTTP_PROXY`, `HTTPS_PROXY`, `FTP_PROXY`, `NO_PROXY`, `ALL_PROXY` (and lower-case) | – | No (hint only) |
| BuildKit built-in args | `BUILDKIT_BUILD_NAME`, `BUILDKIT_CACHE_MOUNT_NS`, `BUILDKIT_CONTEXT_KEEP_GIT_DIR`, `BUILDKIT_GIT_ADVICE`, `BUILDKIT_GIT_DEBUG_COMMANDS`, `BUILDKIT_INLINE_CACHE`, `BUILDKIT_MULTI_PLATFORM`, `BUILDKIT_SANDBOX_HOSTNAME`, `BUILDKIT_SYNTAX`, `SOURCE_DATE_EPOCH` (1.5) | various | No |
| ONBUILD | `ONBUILD <instr>`; no `ONBUILD ONBUILD`, no FROM or MAINTAINER; `COPY --from`/`--mount from=` inside ONBUILD since **1.11** | – / 1.11 | Yes (enforce limits) |
| STOPSIGNAL | `SIGNAME` or number | – | Yes |
| HEALTHCHECK | `--interval` (30s), `--timeout` (30s), `--start-period` (0s), `--start-interval` (5s, Engine 25+), `--retries` (3); `HEALTHCHECK NONE`; last one wins | – | Partial: add `--start-interval` and `NONE` |
| SHELL | JSON array; affects shell-form RUN/CMD/ENTRYPOINT | – | Yes |

### 3.3 RUN --mount options (for the in-progress editor)

| Type | Options |
|---|---|
| bind | `target\|dst\|destination`, `source`, `from`, `rw\|readwrite` |
| cache | `id`, `target`, `ro\|readonly`, `sharing=shared\|private\|locked`, `from`, `source`, `mode` (0755), `uid` (0), `gid` (0) |
| tmpfs | `target`, `size` |
| secret | `id`, `target` (default `/run/secrets/<id>`), `env` (1.10), `required`, `mode` (0400), `uid`, `gid` |
| ssh | `id` (default `default`), `target` (default `/run/buildkit/ssh_agent.N`), `required`, `mode` (0600), `uid`, `gid` |

Note: cache mounts are not exported with `--cache-to`, so they start empty on fresh CI runners. The UI should say so.

## 4. Lint / check rule mapping

### 4.1 BuildKit build checks (all of them)

`#` marks rules that only the real BuildKit engine can evaluate well, because they need the build context or a registry.

| BuildKit check | Detects | Hadolint equivalent | Ours (likely) |
|---|---|---|---|
| StageNameCasing | Stage name not lowercase | – | ? |
| FromAsCasing | `AS` casing differs from `FROM` | – | ? |
| NoEmptyContinuation | Empty line inside a `\` continuation | – | ? |
| ConsistentInstructionCasing | Mixed instruction casing | – | ? |
| DuplicateStageName | Same stage name twice | DL3024 | likely |
| ReservedStageName | Stage named `scratch`/`context` | DL3063 | ? |
| JSONArgsRecommended | Shell-form CMD/ENTRYPOINT (signal handling) | DL3025 | likely |
| MaintainerDeprecated | `MAINTAINER` | DL4000 | likely |
| UndefinedArgInFrom | FROM uses an undeclared ARG | – | ? |
| WorkdirRelativePath | Relative WORKDIR without a prior absolute one | DL3000 | likely |
| UndefinedVar | Variable used before definition | (SC2154 partly) | ? |
| MultipleInstructionsDisallowed | Multiple CMD/ENTRYPOINT/HEALTHCHECK in one stage | DL4003, DL4004, DL3012 | likely |
| LegacyKeyValueFormat | `ENV k v` / `LABEL k v` | – | ? |
| InvalidBaseImagePlatform `#` | Pulled base platform differs from the target (source only, not in docs list) | – | No |
| RedundantTargetPlatform | `FROM --platform=$TARGETPLATFORM` | DL3065 | ? |
| SecretsUsedInArgOrEnv | Secret-looking names in ARG/ENV | DL3064 | ? |
| InvalidDefaultArgInFrom | Global ARG default yields an empty or invalid image | – | ? |
| FromPlatformFlagConstDisallowed | `--platform` with a constant | DL3029 (stricter) | ? |
| CopyIgnoredFile `#` | COPY/ADD source excluded by .dockerignore | – | needs .dockerignore engine (IP) |
| InvalidDefinitionDescription (experimental) | Malformed stage/ARG description comment | – | No |
| ExposeProtoCasing | `EXPOSE 80/TCP` | – | ? |
| ExposeInvalidFormat | IP or host mapping in EXPOSE | DL3011 (range) | ? |

Configuration surface to expose in the GUI:
- the `# check=skip=<csv|all>;error=true;experimental=<csv|all>` directive
- the `BUILDKIT_DOCKERFILE_CHECK` build argument, which takes the same syntax
- the advice to pin `# syntax` when `error=true`, because new checks arrive with new frontends

### 4.2 Hadolint rules worth adopting (not covered by BuildKit)

| Rule | What | Auto-fix? |
|---|---|---|
| DL3002 | Last USER is root | Offer `USER` with an existing non-root user or create one |
| DL3003 | `cd` in RUN; use WORKDIR | Yes |
| DL3004 | `sudo` in RUN | No (explain gosu) |
| DL3006 / DL3007 | Untagged image / `:latest` | Suggest a tag plus a digest pin |
| DL3008 / DL3013 / DL3016 / DL3018 / DL3028 / DL3062 | Unpinned apt/pip/npm/apk/gem/go install | Info-level; optional |
| DL3009 | apt lists not removed (or use a cache mount instead) | Yes (prefer the cache-mount preset) |
| DL3014 / DL3030 / DL3038 / DL3034 | Missing `-y` for apt-get/yum/dnf/zypper | Yes |
| DL3015 | Missing `--no-install-recommends` | Yes |
| DL3019 | `apk add` without `--no-cache` | Yes |
| DL3020 | ADD for local files; use COPY | Yes |
| DL3021 | COPY with more than 2 args and dest not ending in `/` | Yes |
| DL3022 / DL3023 | `COPY --from` to an undefined or its own stage | Partial |
| DL3027 | `apt` instead of `apt-get` | Yes |
| DL3042 | pip without `--no-cache-dir` (unless a cache mount is used) | Yes (context-aware) |
| DL3043 | ONBUILD chaining or FROM/MAINTAINER in ONBUILD | Error |
| DL3044 | ENV self-reference in the same statement | Split |
| DL3045 | COPY to a relative destination without WORKDIR | Suggest WORKDIR |
| DL3046 | `useradd` with a high UID without `-l` | Yes (`--no-log-init`/`-l`) |
| DL3047 | `wget` without `--progress` | Yes |
| DL3059 | Consecutive RUNs (consider consolidation) | Merge cards |
| DL3060 | yarn without `yarn cache clean` (or use a cache mount) | Yes |
| DL3061 | Bad instruction order | Error |
| DL3067 | `COPY --from=stage / /` (whole filesystem) | Warn |
| DL4001 | Both wget and curl | Info |
| DL4006 | Pipe in RUN without `set -o pipefail` / `SHELL ["/bin/bash","-o","pipefail","-c"]` | Yes |
| DL3048–DL3058 | OCI label validation (URL, RFC3339, SPDX, semver, git hash, email) | Validators in the LABEL card |
| SC2086, SC2046, SC2164, SC2154, SC2155, SC1001, SC2015 | Highest-value ShellCheck checks inside RUN/heredocs | Optional: integrate ShellCheck on Windows |

New rules suggested by this research (not in either tool):
- `apt-get` cache mount without `sharing=locked`
- cache mount `uid`/`gid` that does not match a non-root USER
- pip/npm/uv/cargo install without the matching cache mount (offer the preset)
- final stage on a `-dev`/`-sdk` image
- RUN or shell-form CMD in a distroless or hardened runtime stage
- a feature that needs a newer `# syntax` than the pinned one
- labs feature used without `1-labs`
- a non-root runtime that exposes a port below 1024

## 5. Best-practice templates and cache-mount presets

These come from Docker's best-practices guide, the cache optimisation guide and the tools' own docs.

- **Multi-stage**: separate build and runtime stages, reusable `base` stage, named stages, `COPY --from=build` only the artefacts. Build tools never reach the runtime image.
- **Base image**: Docker Official Image, Verified Publisher or DHI. Pin the tag, optionally `@sha256:` digest, and keep it updated with Dependabot or Scout (`No outdated base images` policy). Rebuild often with `--pull`.
- **Layer order**: dependency manifests first (or `--mount=type=bind` of the lockfile), install, then `COPY . .`. Keep the context small with .dockerignore.
- **Non-root**: create a user with an explicit UID/GID (`useradd --no-log-init -r -u 10001 -g app app`; `docker init` uses `adduser --disabled-password --gecos "" --home /nonexistent --shell /sbin/nologin --no-create-home --uid 10001 appuser`). Set `USER` before CMD and avoid switching back and forth. Avoid sudo (use gosu if needed). DHI runtime images default to UID 65532 and have no shell or package manager.
- **apt**: `RUN rm -f /etc/apt/apt.conf.d/docker-clean; echo 'Binary::apt::APT::Keep-Downloaded-Packages "true";' > /etc/apt/apt.conf.d/keep-cache` and then the locked cache mounts below.

| Tool | Cache mount target(s) | Notes |
|---|---|---|
| apt | `/var/cache/apt` and `/var/lib/apt`, both `sharing=locked` | Docker docs. Remove docker-clean first. |
| npm | `/root/.npm` | Docker docs. For non-root, use `$HOME/.npm` with uid/gid. |
| pnpm | `id=pnpm,target=/var/cache/pnpm` with `pnpm install --store-dir /var/cache/pnpm --frozen-lockfile` | pnpm docs. Scope the store to trusted builds. |
| yarn classic / berry | `/usr/local/share/.cache/yarn` / `/root/.yarn/berry/cache` | Tool defaults; verify with `yarn cache dir`. |
| bun | `/root/.bun/install/cache` | Tool default. |
| pip | `/root/.cache/pip` (+ bind `requirements.txt`) | Docker docs, `docker init`. |
| uv | `/root/.cache/uv` + `ENV UV_LINK_MODE=copy`, `UV_COMPILE_BYTECODE=1`, `UV_PYTHON_DOWNLOADS=0`, `UV_NO_DEV=1`; bind `uv.lock` + `pyproject.toml`; `uv sync --locked --no-install-project`, then `uv sync --locked`; `COPY --from=ghcr.io/astral-sh/uv:<ver> /uv /uvx /bin/` | uv docs. |
| Go | `/go/pkg/mod` and `/root/.cache/go-build`; cross-compile with `FROM --platform=$BUILDPLATFORM` + `GOOS=$TARGETOS GOARCH=$TARGETARCH` | Docker docs. |
| Cargo | Docker docs: `CARGO_HOME=/var/cache/cargo` cache + `/app/target/`. With the official `rust` image: `/usr/local/cargo/registry`, `/usr/local/cargo/git`, plus `target` | `target` is a cache, so copy the binary out within the same RUN. |
| Maven | `/root/.m2` (repository) | Tool default. |
| Gradle | `/root/.gradle` (or `$GRADLE_USER_HOME`) | Tool default. |
| NuGet | `/root/.nuget/packages` | Docker docs. |
| Composer | `/tmp/cache` (official composer image sets `COMPOSER_HOME=/tmp`) | Docker docs. |
| Bundler / gems | `/root/.gem` (Docker docs); with the official ruby image also `/usr/local/bundle/cache` | Verify with `bundle config`. |

Presets should set `uid`/`gid` to the stage user when USER is non-root, and should offer `id=` namespacing for multi-project caches. They should also explain that cache mounts do not survive fresh CI runners.

## 6. Assistance features in other tools

| Tool | Notable assisted-authoring features | We have? |
|---|---|---|
| **Docker DX (VS Code)** + Docker Language Server | BuildKit/Buildx build-check diagnostics live in the editor. Scout-powered hover with image vulnerability info, `notPinnedDigest`, `recommendedTag`, critical/high CVE warnings (experimental). Bake-target generation from stages. Compose↔Dockerfile cross-file completion. Build step debugging via Buildx DAP. | Lint only |
| **rcjsuen dockerfile-language-service** (used by VS Code, Zed, Theia and others) | Completion, hovers, signature help for flags, code actions (quick fixes), formatting, rename (stage names, ARGs), go-to-definition, document symbols/outline, folding, semantic highlighting, document links to Docker Hub | Partial (cards act as outline) |
| **Docker Desktop Builds view** | Build history; timing breakdown (wall clock, accumulated CPU, **cache usage**, parallelism); dependencies (images, git and HTTP ADD sources); resolved build args, secret IDs, SSH, labels and contexts; attestations and SBOM; OTLP/Jaeger traces; Dockerfile source with **errors inlined at the failing line**; list and plain-text logs; Gordon "Why did this build fail?" | No |
| **Docker Scout** | `docker scout recommendations` (refresh vs update of the base image, with fewer-CVE and smaller-size benefits), `cves`, `quickview`; policies: severity-based vulnerabilities, no copyleft, **no outdated base images** (checks pinned digests), high-profile vulnerabilities, supply-chain attestations, **default non-root user**, approved base images | No |
| **Docker Hardened Images (DHI)** | Near-zero-CVE minimal images on `dhi.io`; `-dev`/`-sdk` build variants vs shell-less runtime variants; non-root by default (UID 65532); SBOM and SLSA L3 provenance; migration checklist; Gordon-driven automatic Dockerfile migration. Docker's own language guides now default to `dhi.io/python:3.12-dev` → `dhi.io/python:3.12`. | No |
| **Gordon (Docker AI)** | Writes and modifies Dockerfiles following best practices; explains build failures; DHI migration | No (optional AI hook) |
| **`docker init`** | Detects the platform, asks version, port, package manager, build script, output dir and start command; writes Dockerfile (syntax pin, version ARG, cache and bind mounts, non-root user), compose.yaml, .dockerignore, README.Docker.md; warns before overwriting | Partial (starters) |
| **Railpack** (Nixpacks successor) / Cloud Native Buildpacks | Zero-config detection for 17 ecosystems (Node/Bun/Deno, Python, Go, Java, .NET, PHP, Ruby, Rust, Elixir, Gleam, C/C++, Zig, static, shell); version resolution from `.nvmrc`, `engines`, `go.mod`, `global.json`, `.tool-versions`, `mise.toml` and others; inspectable JSON build plan compiled to BuildKit LLB; per-step secret scoping; cache mounts by default | Partial |
| **JetBrains Docker plugin** | Dockerfile run configurations (build args, target stage, run options), gutter run icons, Services tool window with images, layers and containers; completion and inspections (help page fetch was blocked; details unverified) | Partial |
| **Hadolint** (CLI, online) | About 70 DL rules + ShellCheck in RUN; inline ignore pragmas; trusted-registry and label schema rules | Partial |

Features we lack, ranked by value per effort:
1. Hover docs
2. Digest pinning and tag completion
3. Running the real `--check`
4. Base-image and hardened recommendations
5. Secrets fix-its
6. Build results mapped onto cards (timing, cache, size, inline errors)
7. Multi-platform matrix
8. SBOM/provenance defaults
9. Stage graph
10. Debugger

## 7. Two-way editing design recommendations

Prior art:
- **rust-analyzer and Roslyn** use lossless (full-fidelity) trees that preserve all whitespace and comments and represent invalid input exactly, with `ERROR` nodes.
- **VS Code settings** has a GUI over a JSON-with-comments file. Edits are computed as minimal text edits (jsonc-parser `modify` + `applyEdits`), so comments and formatting outside the edit survive. Settings that the UI cannot represent show an **"Edit in settings.json"** link instead of a lossy control.
- **Azure Pipelines YAML editor** keeps text primary. A "task assistant" form *inserts* YAML at the cursor rather than owning the file.

Recommendations:

1. **Text is the source of truth. The GUI is a projection.** Parse into a lossless CST: tokens, trivia (whitespace, comments, continuations, blank lines) and error nodes. GUI cards bind to CST nodes. A card edit produces a minimal text patch on only that node's span, never a re-print of the whole file.
2. **Attach trivia deterministically.** Leading comments belong to the following instruction, trailing same-line comments to the preceding one, and blank-line groups are preserved. Parser directives are a special header node, valid only before the first non-directive line. Stage and ARG description comments belong to their FROM/ARG node.
3. **Preserve formatting style per node.** Keep the user's continuation layout (one flag per line vs inline), `\` vs custom escape, flag order, quoting and exec vs shell form. Only touched nodes are re-rendered, using a style inferred from neighbours. Offer an explicit "Format document" command; never format implicitly.
4. **Partial and invalid states are first-class.** When a text edit fails to parse, keep the last good GUI state for untouched nodes and show the broken span as a raw "unparsed" card with the diagnostic. Do not reset the whole GUI and do not block typing. Debounce reparses (about 150–300 ms) and reparse incrementally by instruction (Dockerfiles are line-oriented, so instruction boundaries are cheap resync points).
5. **Unknown constructs never get dropped.** Unknown flags, labs syntax, future instructions and pattern modifiers round-trip verbatim as raw cards with an "Edit as text" affordance, like VS Code's "Edit in settings.json".
6. **One undo stack shared by both views.** Each GUI action is one text transaction with a label, so Ctrl+Z works the same in text and cards. Preserve caret and selection mapping across reparses.
7. **Selection sync and accessibility.** Selecting a card reveals and highlights its text span, and moving the caret focuses the card. Announce structural changes through UI Automation live regions (for example "Added cache mount to RUN, line 12"). Give cards and the text view equivalent keyboard paths. Show diagnostics in both views with the same rule ID. Never convey a check state only through colour.
8. **Auto-fixes are text edits with a preview.** Show a diff before applying, apply as one undoable transaction, and run a round-trip test (parse → print-unchanged equals input) in CI over a corpus of real Dockerfiles: Docker official images, `docker init` output, heredoc-heavy and Windows escape files.
9. **Version-aware edits.** If a GUI action introduces a feature that needs a newer frontend, the same transaction updates or inserts `# syntax=`, and the user sees it in the diff.
10. **Pitfalls to test explicitly:**
    - comments between continuation lines
    - `# escape=`` ` `` files
    - CRLF vs LF and BOM (important on Windows)
    - trailing whitespace after `\`
    - heredoc bodies (never reformat; preserve tabs for `<<-`)
    - JSON exec form with escaped quotes and backslashes
    - ARG/ENV substitution in FROM
    - `ONBUILD` wrapping another instruction
    - empty continuation lines
    - mixed instruction casing (preserve, and lint separately)

## 8. Sources

- Dockerfile reference (current master source and docs): https://docs.docker.com/reference/dockerfile/ and https://raw.githubusercontent.com/moby/buildkit/master/frontend/dockerfile/docs/reference.md
- Version-tagged references used for stability dating (`dockerfile/1.14.0` … `1.26.0`): https://raw.githubusercontent.com/moby/buildkit/dockerfile/1.26.0/frontend/dockerfile/docs/reference.md (and the same path for 1.14.0, 1.16.0, 1.18.0, 1.19.0, 1.20.0, 1.21.0, 1.22.0, 1.24.0)
- dockerfile/1.19.0 release (`--exclude` GA): https://newreleases.io/project/github/moby/buildkit/release/dockerfile%2F1.19.0
- BuildKit releases list (1.26.0 latest): https://github.com/moby/buildkit/releases
- Build checks reference: https://docs.docker.com/reference/build-checks/
- Build checks guide (skip, error, experimental, `BUILDKIT_DOCKERFILE_CHECK`): https://docs.docker.com/build/checks/ and https://raw.githubusercontent.com/docker/docs/main/content/manuals/build/checks.md
- Linter rule set source: https://raw.githubusercontent.com/moby/buildkit/master/frontend/dockerfile/linter/ruleset.go
- Hadolint rules: https://github.com/hadolint/hadolint (README)
- `docker init`: https://docs.docker.com/reference/cli/docker/init/
- `docker init` Python output example: https://lktuan.github.io/blog/2024-07-30-docker-init
- Docker Python guide (DHI-based): https://docs.docker.com/guides/python/containerize/
- Best practices: https://docs.docker.com/build/building/best-practices/
- Cache optimisation (cache mounts): https://docs.docker.com/build/cache/optimize/
- Build context and .dockerignore: https://docs.docker.com/build/concepts/context/
- Attestations (SBOM and provenance defaults): https://docs.docker.com/build/metadata/attestations/
- uv Docker guide: https://docs.astral.sh/uv/guides/integration/docker/ (read via https://raw.githubusercontent.com/astral-sh/uv/main/docs/guides/integration/docker.md)
- pnpm Docker guide: https://pnpm.io/docker (https://raw.githubusercontent.com/pnpm/pnpm.io/main/docs/docker.md)
- Docker DX extension: https://github.com/docker/vscode-extension (README, FAQ)
- Docker Language Server: https://github.com/docker/docker-language-server
- rcjsuen Dockerfile language server: https://github.com/rcjsuen/dockerfile-language-server
- Docker Desktop Builds view: https://docs.docker.com/desktop/use-desktop/builds/
- Docker Scout recommendations: https://docs.docker.com/reference/cli/docker/scout/recommendations/
- Docker Scout policies: https://docs.docker.com/scout/policy/
- Docker Hardened Images: https://docs.docker.com/dhi/
- DHI migration: https://docs.docker.com/dhi/migration/
- DHI migration checklist: https://docs.docker.com/dhi/migration/checklist/
- Gordon: https://docs.docker.com/ai/gordon/
- Railpack: https://github.com/railwayapp/railpack and https://railpack.com/_llms-txt/languages-reference.txt and https://railpack.com/_llms-txt/architecture.txt
- JetBrains Docker plugin: https://plugins.jetbrains.com/plugin/7724-docker and https://www.jetbrains.com/help/idea/docker-images.html
- rust-analyzer lossless syntax tree design: https://github.com/rust-lang/rust-analyzer/blob/master/docs/book/src/contributing/syntax.md
- jsonc-parser (modify/applyEdits): https://github.com/microsoft/node-jsonc-parser
- VS Code settings editor ("Edit in settings.json"): https://code.visualstudio.com/docs/configure/settings
- Azure Pipelines YAML editor and task assistant: https://learn.microsoft.com/en-us/azure/devops/pipelines/get-started/yaml-pipeline-editor

Pages that could not be fetched, so their claims are marked as unverified or were sourced elsewhere:
- GitHub release pages for dockerfile/1.20.0–1.22.0 (permission timeout). Version dating came from the tagged reference files instead.
- docs.astral.sh (read via GitHub raw instead)
- railpack.com homepage (llms text files used instead)
- Docker Debug docs page (site error)
- JetBrains Docker help page (permission timeout)
