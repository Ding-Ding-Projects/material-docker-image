# Roadmap

## Material 3 workspace rewrite (November 2026)

- [x] Design every window surface on a design canvas and record it in `design/workspace-redesign-2026-11`.
- [x] Replace the window with a frameless Material 3 shell: title bar with provenance, rail, workspace and utility tabs, command palette, notifications, accessibility tools, operation panel.
- [x] Rebuild every destination and add the Dockerfile Creator, Monitor, System, Registry, Tools and Activity destinations.
- [x] Add search with a regular expression builder to every list, menu and viewer.
- [x] Add append-only local history beside the application data with diff, restore, labels and export.
- [x] Replace every screenshot with captures of the built application on Windows with a real engine (dark, light and minimum size).
- [ ] Exercise every destination interactively on a personal Windows machine and record receipts.
- [x] Fix small-window truncation found in the 960 x 720 captures (title bar search, operation panel, stacked lists, Images tiles, segmented buttons).
- [x] Add language and display-scale options to capture mode and stop loading personal wording in captures.
- [ ] Capture and review the full language x theme x 100-200% scale matrix with the new options.
- [x] Accept a personal wording file dropped anywhere on the window with validation, celebration and outcome sounds (checks pass; hands-on verification pending).
- [x] Record a source audit of the shared feature requirements in `evidence/uh-features-audit.md`.
- [x] Localize page-specific copy in Cantonese and bilingual modes (built-in phrase list and sentence templates; running the built application in each mode on every destination is still to be recorded).
- [ ] Review capability inventory statuses for health checks, devices, GPUs and terminals with evidence.
- [x] Add settings row locks, scheduled language and theme, attention-mode consumers (Focus, Low stimulation, Momentum), a documented action sound mapping and persisted collapsible filters (rule checks pass; hands-on verification pending)
- [ ] Drive the new lock, schedule, attention-mode, sound and collapse controls in the built window and record receipts
- [ ] Add lock credential policies, other schedulable settings and the API and Home Assistant schedule sources

## Delivered and bounded verification

- [x] Implement native WPF Build, Images, Containers, Compose, Canvas, Volumes, Networks, Diagnostics, Settings and Setup workspaces.
- [x] Implement advanced Build destinations, labels, BuildKit cache rows and selected secret-file controls.
- [x] Implement structured Run arguments, environment, ports, mounts, resource limits, restart choices and security controls.
- [x] Implement native stage selection/editing, source navigation, zoom/pan/fit and inspected runtime relationship graphs.
- [x] Pass 23 core checks and 51 UI-state/parser checks.
- [x] Exercise a real native build/run at f370d66, including exact label and environment whitespace.
- [x] Publish normal release v0.4.1 for f370d66 through successful run 39111325684.

## Current candidate and evidence

- [x] Verify normal release v0.5.1 at ad5d3ac, including the 3ce1ed7 restart fix, successful run 39112649034, exact tag source and five asset sizes/digests.
- [x] Update the matching existing installation to 0.5.1.0, verify its executable against the published package and preserve settings.
- [x] Exercise installed Docker detection, Setup Back/Next/Finish, persisted completion and productive Build visibility on the current host.
- [x] Verify documentation deployment 39112649029, 12 articles, 109 neutral identifiers, three original capture bytes, 1440/320 layouts, interactive persistence and exact About homepage.

- [x] Verify the local 0.5.0 build, genuine installer production and actual saved-workspace restart restoration from 3ce1ed7.
- [x] Retain and inspect source-bound 0.5.0 Build and populated Canvas captures at normal 1440x960 and minimum 960x720 sizes.
- [ ] Extend English/dark/100% normal/minimum evidence to the full supported language, theme and display-scale matrix.
- [ ] Exercise OCI export, registry push, supported cache backends and secret-file builds beyond implemented controls and focused checks.
- [ ] Extend the verified current-host update/launch/setup proof to fresh install, automatic ready-banner behavior, cancellation and uninstall acceptance.

## Extended capabilities and complete acceptance

- [x] Implement and deploy the 123-entry feature checklist with category/status filters and separate visitor progress.
- [x] Verify checklist keyboard disclosure, filter/reset behavior, preparation persistence and 12 width/language/theme combinations; retain bounded source-specific evidence.
- [x] Reject source-reference verification metadata and escape rendered bindings, with an observed failing-then-passing regression and independent review.
- [x] Declare a local browser icon and include its hash in the documentation manifest.
- [ ] Complete checklist capture promotion and the full cleanup receipt; isolated profile removal was rejected and the profile remains retained.
- [ ] Implement and verify container health-check configuration (`container-health-checks`). Implemented with focused checks and a real-engine argument receipt; built-application interaction still pending.
- [ ] Implement and verify container device mapping controls (`container-device-mappings`). Implemented with focused checks and a real-engine argument receipt; built-application interaction and a real host device still pending.
- [ ] Implement and verify container GPU access controls (`container-gpu-access`). Implemented with focused checks and a real-engine argument receipt; built-application interaction and a GPU-capable host still pending.

- [ ] Verify the application icon, title-bar logo picker and display-name rename interactively in the built window and the installed shortcut (`app-icon`, `logo-customization`, `display-name`); add logo crop, fit and background editing.
- [x] Build the in-app changelog from git log at build time, with version grouping, regex search, date range and Markdown export (`changelog`; built-window interaction pending).
- [x] Export or copy every table as CSV, TSV, JSON, Markdown or HTML with checked escaping (`exports`).
- [x] Derive menu shortcut text from the command gesture and guard against hand-typed ones (`menu-shortcuts`).
- [x] One shared info-icon component used on settings rows and the previously bare field labels (`info-icon-guidance`).
- [x] Background update check against the published feed with an unsigned-build banner, a setting and a manual check (`auto-updates`).
- [ ] Exercise the changelog, exports, update banner and info icons in the built window; run the updater against a real published feed and an installed copy.
- [ ] Add the changelog calendar date picker with range presets, language-mode voice for entries, and the update deferral and progress settings.
- [ ] Complete all 109 required-feature acceptance rows without removing unfinished identifiers.
- [ ] Deliver portable documentation and dedicated build companions.
  - [x] Build companion window (`src/BuildCompanion`), single-file packaging script, release asset and logic checks (see `docs/features/build-companion.md`).
  - [ ] Interactive verification of the companion window on a desktop, and a real build driven from it.
  - [x] Portable documentation package script, offline renderer, release asset and readback wiring (`build/package-docs.ps1`); hosted-run and opened-archive verification pending.
  - [ ] Verify the hosted release carries `MaterialDockerImageDocs.zip` and that it opens from a file location.
- [ ] Establish complete RegexBuddy functional parity and independent workflow evidence.
  - [ ] Portable documentation package.
- [ ] Establish complete RegexBuddy functional parity and independent workflow evidence. Matrix in `docs/features/regex-parity.md`: 50 implemented, 18 partial, 15 unavailable (debugger trace, step counts, possessive, recursion and other engine-impossible topics, plus editable tree, line and page scopes, hex view and panel layout, which are not built). Folder search and replace with undo, match navigation, replacement analysis, tree export, parameters, flavor comparison, remembered experiments and more conversion targets are built and covered by logic checks; interactive verification of the popover and HuiShots are pending.
- [ ] Complete localization, accessibility, appearance and onboarding acceptance across supported surfaces.
- [ ] Address health-check configuration, devices, GPU controls and interactive terminals.
- [ ] Verify remote engines, alternative architectures and supported workflow combinations.
- [x] Add the home page download panel, link-preview metadata and preview image, front provenance and site-wide search with a bounded regex builder (`docs/features/documentation-site.md`).
- [ ] Complete interactive documentation/wiki content, route acceptance and deployed-homepage evidence (deployed verification of the download panel, previews and search is part of this).
