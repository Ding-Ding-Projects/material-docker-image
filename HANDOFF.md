# Implementation handoff

## Objective

Deliver a native Windows WPF workspace for building and running Docker images, resource management, diagnostics and an interactive native canvas. The productive application remains WPF.

## Implemented behavior

Build includes multiple tags, labels, target/platform choices, local loading, registry push, OCI archive selection, cache import/export and local BuildKit secret-file rows. Publication/cache writes and existing archive replacement require confirmation. Secret selections are not persisted, and secret file bytes are not read or logged by the application.

Run includes explicit JSON command arguments, non-secret environment pairs, entrypoint, user, working directory, CPU/memory limits, restart policy, additional ports and bind/volume mounts. Ports default to loopback, additional mounts default to read-only, and no-new-privileges is enabled by default. Container creation reports the real process result without claiming service health.

The native Canvas offers stage/object selection, a source inspector, supported stage alias/base edits, target selection, source-line navigation, zoom/pan/fit and refreshed container/network/volume relationships. Complex or oversized Dockerfiles remain source-only with original text preserved. Context/generation checks protect resource snapshots, and Compose Save remains tied to its loaded path.

## Verified baseline

- 23 core checks and 51 UI-state/parser checks passed through the exact root build entrypoint.
- Source `f370d66133ffc3fe5f80967f5bafb33c28bc74f2` completed a real native Build/Run workflow. Exact image-label and environment-value whitespace were checked.
- Normal release [v0.4.1](https://github.com/Ding-Ding-Projects/material-docker-image/releases/tag/v0.4.1) belongs to that source. [Run 39111325684](https://github.com/Ding-Ding-Projects/material-docker-image/actions/runs/39111325684) succeeded.
- Actual native visual scope is normal and minimum window sizes, English, dark theme, 100% scaling. Full matrix acceptance remains incomplete.
- Current README image paths are `evidence/build-workspace.png`, `evidence/dockerfile-canvas.png` and `evidence/dockerfile-canvas-minimum.png`. The retained and inspected captures bind to source 3ce1ed7, local version 0.5.0. Build is 1440x960; populated Canvas is 1440x960 and 960x720. The minimum graph intentionally pans and the inspector scrolls.
- Earlier real-engine smoke used uniquely owned resources and exact output. Earlier captures retain their original source binding and never prove a later candidate.

## Current candidate

Source `3ce1ed7895253edf8e0ccf99e6aaaca8a70ef22a` restores the saved Build folder/Dockerfile on restart. The local 0.5.0 build and genuine installer production succeeded. A real native Build resolved the relative example folder to its absolute location. After closing and relaunching from another working directory without an open-file argument, UI Automation read back Dockerfile source equal to the actual example after line-ending normalization. The restart receipt passed, the uniquely owned image was removed, and the owned root process closed. Public release verification for this source remains pending.

## Remaining work

OCI output, registry publication, cache backends and secret-file controls exist, but all runtime combinations have not been exercised. Complete required-feature acceptance remains open across all 109 retained identifiers. Portable documentation/build companions, full RegexBuddy parity, complete localization/appearance/narration, server-management integration, GPU/device/health-check controls and interactive terminals remain gaps.

Installer publication and actual installed lifecycle/update evidence are separate verdicts. Normal/minimum native evidence does not establish the full language/theme/100%-200% scale matrix, design-reference parity or exhaustive accessibility.

## Next actions

Verify public release publication for source 3ce1ed7 and complete installed lifecycle/update acceptance independently of successful local packaging. Retain the current source-bound captures and restart receipt. Update evidence states only after receipts exist. Preserve every unfinished capability row and use the smallest decisive checks for each remaining workflow.

