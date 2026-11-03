# Implementation handoff

## Objective

A native Windows WPF workspace for Docker with a complete Material 3 interface. The November 2026 rewrite replaced every window surface, added the Dockerfile Creator, live monitoring, cleanup, registry, diagnostics, tools, settings and local history destinations, and replaced every screenshot with captures of the built application.

## Structure

- `src/MaterialDockerImage/Themes/Material.xaml`: Material 3 roles and control templates (buttons, fields, combo boxes, switches, chips, segmented buttons, rail and tab items, lists, menus, tooltips, scroll bars, sliders, expanders).
- `Shell/`: overlay host (in-window dialogs, super confirmation), operation panel, command palette.
- `Services/`: `AppHost` (engine access, foreground operations with real results, background reads, navigation and hand-offs), theme, language and funny levels with the personal wording overlay, notifications, command registry, narration, local history (LibGit2Sharp).
- `UI/`: shared builders (`Ui`), `DataTable<T>` (search with regex builder, filters, multi-select, bulk bar, context menus with shortcuts, export), `SearchFilter`, structured input validation, Dockerfile graph, the Creator model, parser, starters, lint and undo history, the Base64 toolkit, icons generated from the Material Symbols codepoints.
- `Pages/`: one class per destination with helpers in a folder per destination.
- `Assets/Fonts/`: Material Symbols Rounded subset to the icons in use (outline and filled, Apache 2.0).
- `design/workspace-redesign-2026-11/`: the design canvas for every screen, with its handoff and review notes.

## Verification

- `build.bat -Tests` on Windows: 23 core checks, 113 UI-state checks (structured inputs, Dockerfile graph, local history including restore of a restore and path containment, personal wording bounds, language modes and funny levels, Base64 variants and strict offsets, every Creator starter round trip, exec-form escaping, lint findings and an automatic fix) and 13 documentation checklist checks.
- Two regressions were observed failing before their repairs: history revisions made in the same second listed out of order (restore returned the wrong revision), and the documentation site was stale after new articles.
- `Tests and screenshots` workflow run 41698541353 at source `afc72c0df9be118fdb6d07c238a4ff3b458edc0d`: the published application rendered all 18 surfaces in dark and light themes at 1440 x 960 and in dark at 960 x 720, on Windows Server 2025 with a real Windows container engine and labelled resources that were removed afterwards. The 54 captures are in `evidence/screenshots/` with hashes in `evidence/inventory.json`. They were reviewed; layout problems they showed (stretched captures, a 320 px dialog limit, squeezed inspectors, wrapped chips, rail overlap, a tall empty output panel) were fixed before this run.
- Capture mode only renders; interactive behaviour of each workflow on a personal machine (clicking through builds, runs, removals, prunes, sign-in, history restores) has not been exercised in this rewrite. Earlier native workflow evidence (source f370d66 and release v0.5.1) belongs to the previous interface and is not relabelled.

## Known limits

- Language modes cover the shell and common labels; page-specific copy is English in all modes.
- Density and Momentum settings are saved but have little visible effect; the external editor setting is used by Build only.
- The regex builder covers construction, snippets, flags and sample testing; full RegexBuddy parity remains open.
- Element locks, the per-element appearance editor, authenticator, school-mode PIN, dim sum pictures and scheduled settings from the shared feature catalogue are not implemented.
- The capability inventory statuses were not changed by this rewrite; health checks, devices, GPUs and terminals now have controls in the run editor and the Containers inspector and need status review with evidence.
- Full theme, language and 100 to 200 percent scale matrix acceptance remains open.

## Next actions

Exercise every destination interactively on a Windows machine with Docker Desktop, record receipts, update the capability inventory rows with that evidence, and extend localization to page copy.

## Advanced v2 (November 2026)

The second pass added Build v2 with a BuildKit timeline and Bake, image detail tabs and a Security destination (Docker Scout), engine contexts and Swarm, volume backup, restore and clone, more Compose tools, Dockerfile Creator v2 with an editable two-way source, editor suggestions with interactive value pickers, an advanced regular expression builder on all search fields, a motion system and appearance editor, and a Minecraft server manager.

- Verification: 676 UI-state checks (including a search surface catalogue guard over 107 surfaces), 23 core checks and 13 documentation checks. `Tests and screenshots` run 41718216967 at source `40fa2560a0d05536d4b5aafdb8aee5acf6a99c3e` rendered 24 surfaces in dark and light at 1440 x 960 and dark at 960 x 720 (72 captures in `evidence/screenshots/`), including the Creator with Suggestions open. The review found and fixed light-theme captures rendering dark and a squeezed Creator source editor before this run.
- Not yet exercised interactively on Windows: popups and pickers (placement, focus), the regex builder popover, animations, Docker Scout analysis (the runner has no Scout plugin), Swarm with a real swarm, Minecraft servers against the itzg images, and Bake against a real bake file.
- The Dockerfile Creator gap analysis is in `docs/research/dockerfile-creator-gap-analysis.md`.
