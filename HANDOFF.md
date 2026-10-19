# Implementation handoff

## Objective

Deliver a native Windows WPF workspace for building and running Docker images, resource management, diagnostics and an interactive native canvas. The productive application remains WPF.

## Feature checklist candidate

The documentation now generates a prominent maintainer-owned completion section from `website/feature-inventory.json`. It preserves 109 required IDs, 10 product workflows and four milestones. All required rows remain unverified; implemented workflow/checklist rows retain open acceptance actions and health/device/GPU milestones remain planned. Category/status filters, native keyboard disclosure, localized details and responsive styles are separate modules. The original preparation checklist and `mdi-guide-v1` state are unchanged.

The same inventory generates the capability summary and manifest counts. Generation is deterministic and `--check` rejects stale output. Local positive/negative checks cover missing IDs, status/evidence restrictions, public links and filter behavior. Real-browser interaction, pixels, deployed content and independent review remain pending until separately recorded. No new screenshot or release claim is made by this implementation.

## Implemented behavior

Build includes multiple tags, labels, target/platform choices, local loading, registry push, OCI archive selection, cache import/export and local BuildKit secret-file rows. Publication/cache writes and existing archive replacement require confirmation. Secret selections are not persisted, and secret file bytes are not read or logged by the application.

Run includes explicit JSON command arguments, non-secret environment pairs, entrypoint, user, working directory, CPU/memory limits, restart policy, additional ports and bind/volume mounts. Ports default to loopback, additional mounts default to read-only, and no-new-privileges is enabled by default. Container creation reports the real process result without claiming service health.

The native Canvas offers stage/object selection, a source inspector, supported stage alias/base edits, target selection, source-line navigation, zoom/pan/fit and refreshed container/network/volume relationships. Complex or oversized Dockerfiles remain source-only with original text preserved. Context/generation checks protect resource snapshots, and Compose Save remains tied to its loaded path.

## Verified baseline

- 23 core checks and 51 UI-state/parser checks passed through the exact root build entrypoint.
- Source `f370d66133ffc3fe5f80967f5bafb33c28bc74f2` completed a real native Build/Run workflow. Exact image-label and environment-value whitespace were checked.
- Normal release [v0.5.1](https://github.com/Ding-Ding-Projects/material-docker-image/releases/tag/v0.5.1) is the verified delivery baseline at `ad5d3ac5c02588e8efcc926715119b87a9c7c6d5`. [Windows run 39112649034](https://github.com/Ding-Ding-Projects/material-docker-image/actions/runs/39112649034) succeeded. The tag resolves to that exact source; five downloadable assets had their sizes and digests verified.
- Actual native visual scope is normal and minimum window sizes, English, dark theme, 100% scaling. Full matrix acceptance remains incomplete.
- Current README image paths are `evidence/build-workspace.png`, `evidence/dockerfile-canvas.png` and `evidence/dockerfile-canvas-minimum.png`. The retained and inspected captures bind to source 3ce1ed7, local version 0.5.0. Build is 1440x960; populated Canvas is 1440x960 and 960x720. The minimum graph intentionally pans and the inspector scrolls.
- Earlier real-engine smoke used uniquely owned resources and exact output. Earlier captures retain their original source binding and never prove a later candidate.

## Delivered baseline and original restart evidence

The v0.5.1 baseline includes source `3ce1ed7895253edf8e0ccf99e6aaaca8a70ef22a`, which restores the saved Build folder/Dockerfile on restart. Its original local 0.5.0 build and genuine installer production succeeded. A real native Build resolved the relative example folder to its absolute location. After closing and relaunching from another working directory without an open-file argument, UI Automation read back Dockerfile source equal to the actual example after line-ending normalization. The restart receipt passed, the uniquely owned image was removed, and the owned root process closed. Original screenshots remain bound to 3ce1ed7/0.5.0 rather than being relabelled as v0.5.1 captures.

## Current-host update and installed setup

The existing installation was updated through `Update.exe` to 0.5.1.0. Its executable SHA-256 is `4429ED021FF6F95D49A3CD1CF2B0C3F4DECABEDDA44554ACB35C991056FED0B9`, matching the published package. Existing settings were preserved. The installed hidden-desktop launch successfully detected Docker, exercised Setup Back/Next/Finish, persisted `SetupComplete=true` at step 2, and opened an inspected productive Build workspace. The owned process was closed.

This proves the bounded current-host supported update, launch and setup flow. It does not prove fresh installation, the automatic update-ready banner, cancellation, rollback or uninstall acceptance.

## Deployed documentation

[Deployment run 39112649029](https://github.com/Ding-Ding-Projects/material-docker-image/actions/runs/39112649029) succeeded for ad5d3ac. The [live documentation](https://ding-ding-projects.github.io/material-docker-image/) delivered 12 articles, 109 neutral capability identifiers and three PNGs byte-identical to their retained source evidence. At 1440 and 320 pixel widths, no horizontal overflow was observed. Guide Next, checklist, theme and bilingual-choice persistence passed. The exact About homepage was verified as that deployed home URL. These bounded checks do not establish complete documentation or universal feature acceptance.

## Remaining work

OCI output, registry publication, cache backends and secret-file controls exist, but all runtime combinations have not been exercised. Complete required-feature acceptance remains open across all 109 retained identifiers. Portable documentation/build companions, full RegexBuddy parity, complete localization/appearance/narration, server-management integration, GPU/device/health-check controls and interactive terminals remain gaps.

Installer publication and actual installed lifecycle/update evidence are separate verdicts. Normal/minimum native evidence does not establish the full language/theme/100%-200% scale matrix, design-reference parity or exhaustive accessibility.

## Next actions

Preserve the verified v0.5.1 delivery evidence, original source-bound captures and restart receipt. Extend current-host proof to the remaining installer/update lifecycle cases and complete the explicit capability gaps. A later documentation push may create another build; this baseline does not claim to be the permanently newest release. Update evidence states only after receipts exist and retain every unfinished capability row.
