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

- `build.bat -Tests` on Windows: 23 core checks, 691 UI-state checks (structured inputs, Dockerfile graph, local history including restore of a restore and path containment, personal wording bounds, language modes and funny levels, Cantonese and bilingual coverage of 50 sampled page strings and sentence templates, Base64 variants and strict offsets, every Creator starter round trip, exec-form escaping, lint findings and an automatic fix) and 13 documentation checklist checks.
- Two regressions were observed failing before their repairs: history revisions made in the same second listed out of order (restore returned the wrong revision), and the documentation site was stale after new articles.
- `Tests and screenshots` workflow run 41698541353 at source `afc72c0df9be118fdb6d07c238a4ff3b458edc0d`: the published application rendered all 18 surfaces in dark and light themes at 1440 x 960 and in dark at 960 x 720, on Windows Server 2025 with a real Windows container engine and labelled resources that were removed afterwards. The 54 captures are in `evidence/screenshots/` with hashes in `evidence/inventory.json`. They were reviewed; layout problems they showed (stretched captures, a 320 px dialog limit, squeezed inspectors, wrapped chips, rail overlap, a tall empty output panel) were fixed before this run.
- Capture mode only renders; interactive behaviour of each workflow on a personal machine (clicking through builds, runs, removals, prunes, sign-in, history restores) has not been exercised in this rewrite. Earlier native workflow evidence (source f370d66 and release v0.5.1) belongs to the previous interface and is not relabelled.

## Known limits

- Language modes cover the shell and the copy of every destination page (about 3,200 Cantonese phrases and sentence templates). Docker CLI output, engine data, log lines and some long explanations assembled from several fragments are shown as produced and stay English in every mode.
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

## Polish pass (4 November 2026)

- Small-window layout fixes found in the 960 x 720 captures: the title bar search shows a complete short label instead of a truncated one, the idle operation panel no longer reserves an empty progress bar, stacked list and inspector layouts keep at least 240 px for the list, the Images statistic tiles fold away on short windows (the subtitle carries the same counts), and segmented buttons size to their widest label so a selected label with its check mark is never cut.
- Capture mode accepts `--capture-language English|Cantonese|Bilingual` and `--capture-scale 125|150|200` (the window keeps its size in device-independent pixels and renders at the matching DPI, so a 1440 x 960 capture at 150 % is 2160 x 1440 pixels) so the language and display-scale matrix can be captured from the built application. Capture mode never loads imported personal wording.
- A personal wording `.json` file can be dropped anywhere on the window (`Shell/VocabularyDrop.cs`, rules in `UI/VocabularyDropRules.cs`). It is validated with the same rules as the Settings upload; a valid file is applied and saved at once and a full-window headline holds for about three seconds (a short fade when motion is reduced, announced through a polite live region); an invalid file is refused, changes nothing and plays the default beep. A valid drop plays the Windows tada sound. Both sounds follow the action sounds setting and low-stimulation mode. Settings › Language remains the keyboard route and clears the wording.
- `build/test.ps1` and `build/publish.ps1` no longer stop when the SDK writes to standard error (for example the first-run notice of a freshly extracted SDK on Windows PowerShell 5.1); native commands are judged by exit code.
- Releases are created without the Latest badge; only a run from `main` whose commit is still the tip of `main` marks its release Latest, so slower branch runs no longer take the badge.
- `evidence/uh-features-audit.md` is a source-only audit of the shared feature requirements against this tree: 27 delivered, 43 partial, 25 missing, 20 not applicable, with a prioritised backlog. It is not runtime evidence.
- Verification: `build.bat -Tests` passed 23 core, 681 UI-state and 13 checklist checks. The new headline check was observed failing with the rule broken and passing after restoring it. The drop, celebration and sounds have not yet been exercised by hand in the built application.
- Follow-up after inspecting local 960 x 720 captures of the built application: segmented buttons keep their original fill behaviour and the Monitor interval and sampling groups size to their labels; Containers and Minecraft queue a refresh that arrives while one is running (for example after an engine context switch) instead of dropping it, and capture mode waits for such a queued refresh; stacked inspectors take one third of the height with a 150 px floor. Remaining: at 960 x 720 with a row selected, Images shows about one and a half list rows.
- The built application (0.99.2) was driven on a hidden desktop through UI Automation on a host at 150 % display scale with Docker Desktop: Setup (Next, Skip for now) and the Containers destination listed 25 containers from the desktop-linux context. Icon glyphs appear as unnamed text elements in the accessibility tree.
- Minimum-size layout from the design proposal in `design/polish-2026-11/ImagesCompact.dc.html`: on a short window the stacked detail of Images, Containers, Security and Swarm services folds into a one-line summary of the selected row with Show details; Images and Security statistic tiles take one line each; Prune dangling sits in the filter row (`DataTable.ToolbarAction`). At 960 x 720 Images and Containers now show four list rows and Security two (previously one to two). Swarm services was changed the same way but was not captured with a swarm.
- Language fixes found in Cantonese captures of the built application: the operation panel and dialog host are created after the saved language is applied (they were English for every saved language); table selection and row-count lines use translated templates; templates with little fixed text only fill their placeholders with single tokens, so "{0} of {1}" no longer rewrites ordinary sentences such as the Appearance preview description. Capture mode sets its language and theme in memory only and never writes `settings.json` (it previously saved on every engine context switch and on close). UI-state checks: 694, including both new guards observed failing before their fixes.

## Build companion (November 2026)

`src/BuildCompanion` is a separate WPF window that runs the root `build.bat` and `build-installer.bat` with a validated `-Version`, streams their output, shows phase, elapsed time and the real exit code, and can cancel the process tree. `build/publish-companion.ps1` publishes it as `out/companion/MaterialDockerImageBuild.exe` with a receipt, `build-installer.bat` calls it, and the release workflow uploads and verifies the executable and `companion.json`. Logic checks run in `tests/UiState` (747 checks in total). The window itself has not been opened or driven interactively. Details: `docs/features/build-companion.md`.
