# Required-feature coverage audit

This is a source audit of Material Docker Image against the maintainer's shared feature requirements (115 identifiers). Date: 2026-11-04. Source commit: `47aee4929ef473b57dc101e68de257ac7266e003`.

It reads the code, scripts, workflows and documents in this repository and classifies each requirement. It does not run the application, the tests or the workflows, so no row is a verified acceptance result. A setting that is saved but never used counts as Partial. "Not applicable" always names the reason, and external-state items (GitHub Discussions, Projects, issues) are marked that way because source cannot show them.

Identifier note: thirteen identifiers in the maintainer list are not in `website/required-feature-ids.json` under the same name. For seven of them the project list uses a neutral name, which this audit uses: `layout-overflow-matrix`, `public-screenshot-gallery`, `mobile-website`, `offline-documentation-companion`, `private-conversation-discipline`, `shared-instruction-portable-editions` and `wiki-documentation-sync`. The six with no counterpart are `job-experience-document-label`, `action-sound-effects`, `live-charts`, `base64-tools`, `minecraft-world-generator` and `drawesome-drawing`.

## Summary

| Status | Count |
| --- | ---: |
| Delivered | 27 |
| Partial | 43 |
| Missing | 25 |
| Not applicable | 20 |
| Total | 115 |

| Category | Delivered | Partial | Missing | Not applicable |
| --- | ---: | ---: | ---: | ---: |
| Language, voice and personalization | 1 | 8 | 3 | 1 |
| Material Design 3, appearance and layout | 2 | 8 | 1 | 0 |
| Navigation, search and commands | 4 | 6 | 1 | 0 |
| Safety, locks and authentication | 1 | 0 | 5 | 1 |
| Records, history and messages | 4 | 2 | 0 | 1 |
| Built-in tools and hand-off | 2 | 3 | 3 | 2 |
| Completeness and evidence | 1 | 4 | 2 | 1 |
| Documentation site and shared links | 3 | 3 | 2 | 1 |
| Status Hub | 0 | 0 | 1 | 3 |
| Build and install | 4 | 1 | 3 | 0 |
| Release pipeline | 2 | 2 | 3 | 1 |
| Repository records and README | 3 | 6 | 1 | 4 |
| Instruction-repository deliverables and games | 0 | 0 | 0 | 5 |

## Language, voice and personalization

| Id | Requirement | Status | Evidence / gap |
| --- | --- | --- | --- |
| `job-experience-document-label` | Job-related products show "Job Experience Document" instead of "Resume", with a toggle | Not applicable | The product is a Docker workspace, not a job or document tool. |
| `language` | Persisted English, Cantonese and bilingual modes on every surface | Partial | `Services/Loc.cs` (about 190 translated keys), `Pages/Settings/SettingsSections.cs`, `UserSettings.Language`, three modes in `website/template.html`. The roadmap records that page-specific copy is not yet localized in Cantonese or bilingual mode. |
| `funny-emoji` | Two funny-level sliders (English, Cantonese) plus a dialog emoji switch | Partial | Sliders 1 to 5 default to 5 and a dialog-emoji switch exist (`Pages/Settings/SettingsSections.cs`, `Loc.Voice`, `Loc.Emoji`), but only about 15 call sites use `Loc.Voice`, there is no per-slider reset, and the website has neither control. |
| `personal-vocabulary` | Local personal-vocabulary JSON upload with loaded, invalid, replace and clear states | Partial | `Loc.LoadVocabulary` validates a versioned, bounded file before applying and `Settings > Language` has choose and clear. Open pages only refresh on reopen, and the website has no upload control. |
| `vocabulary-drop-apply` | Dropping a vocabulary JSON anywhere on the window applies it with a celebration | Missing | No `AllowDrop` or drop handler exists anywhere in `src/`. |
| `school-mode` | Shared, renameable School mode that forces English and hides playful content | Partial | `UserSettings.SchoolMode` is wired into `Loc` and the dim-sum surprise. It is not renameable, not shared across other products, and not on the website. |
| `narration` | Spoken narrator with English, Cantonese or Both, strictly serialized, voice pickers | Partial | `Services/Narrator.cs` (System.Speech), voice, rate and test in `Settings > Sounds`. Off by default and de-duplicated. There is no English/Cantonese/Both choice or separate Cantonese voice picker, and no website narrator. |
| `action-sound-effects` | Short polished action sounds with a persisted Settings switch | Partial | `UserSettings.Sounds` and `Notifier.PlayCue` play three Windows system sounds on notifications only. There are no custom cues for meaningful actions, no volume, quiet-hours or reduced-sound control, and no unavailable-audio path beyond a swallowed exception. |
| `scheduled-settings` | Schedule language, theme, density, colours, fonts and motion | Missing | No schedule model or UI. `Theme` supports "System" only as a live follow, not a schedule. |
| `display-name` | User can rename the product everywhere, with one-action reset | Delivered | `Settings > About` display name with validation and reset, bound to the title bar and window title (`MainWindow.xaml.cs`). Notification text and installer shortcut names still use the shipped name. |
| `adhd-modes` | Independent Focus, Low stimulation, Time awareness, One thing at a time and Momentum modes | Partial | All five toggles persist (`Settings > Focus`). Momentum has no consumer ("This version does not show them yet"), and the website has none of them. |
| `dim-sum` | 10% startup chance of one dim-sum dish with bilingual names and a picture | Partial | `MainWindow.MaybeDimSum` draws 10% once per launch and shows a toast with English and Cantonese names from an embedded list. There is no picture and no link to the public catalogue. |
| `dim-sum-photo-source` | Dim-sum photos come only from the public photo catalogue | Missing | No photos or catalogue client exist. |

## Material Design 3, appearance and layout

| Id | Requirement | Status | Evidence / gap |
| --- | --- | --- | --- |
| `material-design` | Full Material Design 3 Expressive conformance with no legacy elements | Partial | `Themes/Material.xaml` (1,060 lines of M3 roles and templates), `Services/Theme.cs`. Expressive shape and motion conformance has not been audited, and the website uses a plain system-font stylesheet rather than M3 tokens. |
| `workflow-navigation` | Discrete destinations, custom window chrome, every navigable-looking control works | Delivered | Frameless shell, rail, workspace and utility tabs, title bar window controls (`MainWindow.xaml.cs`, `Pages/Destinations.cs`). Interactive acceptance of every destination is still an open roadmap item. |
| `motion` | M3 motion everywhere, reduced-motion path, customizable and schedulable | Partial | `Services/Motion.cs`, `MotionAttached.cs`, motion level and speed in the appearance editor, reduced motion and low stimulation honoured. Not schedulable. |
| `live-charts` | Liveline-style live charts with live dot, gradient, badge and reduced-motion path | Delivered | `Pages/Monitor/LiveChart.cs` (smoothed line, pulsing latest-sample dot, gradient fill, crosshair, keyboard scrubbing, automation summary) used by `MonitorPage`. |
| `appearance-editor` | Edit appearance on every element and state through a context menu and keyboard path | Partial | `Pages/Settings/AppearanceEditor.cs`, `Shell/AppearancePanel.cs`, `AppearanceTargeting.cs`, Ctrl+Shift+E, page-area and rail context menus. Coverage is by colour role and page area, not every element, state, tab or group. |
| `color-picker` | Infinite colour picker with a translator across many formats | Partial | `Pages/Settings/AppearanceColorPicker.cs` and `UI/AppearanceColor.cs` convert HEX/HEX8, RGB, HSL, HSV, HWB, CMYK, OKLab, OKLCH with contrast badges. No named-colour or CIELAB support was found, and not every colour control in the app uses it. |
| `logo-customization` | Logo presets plus a local custom image upload | Missing | No logo setting or upload. |
| `overlay-panels` | Overlays paint their own surface, stay in the viewport and scroll | Partial | `Shell/OverlayHost.cs` bounds cards to host width and height and wraps content in a scroller. Only three minimum-size captures exist as evidence. |
| `collapse-filters` | Collapsible search, filter and statistics panels with persisted state | Partial | `UserSettings.CollapsedFilters` is stored but nothing reads it. Only the output panel (`OperationsDock`) collapses. |
| `accessibility-sizing` | Always-reachable accessibility menu with text size, contrast, motion, focus, targets, keyboard help | Partial | Menu with text size, high contrast, reduced motion, larger targets, read-aloud and shortcut list (`MainWindow.ShowAccessibility`, `Settings > Accessibility`). No visible-focus option, no support entry, and 200% scale evidence is not recorded. |
| `functional-ui` | Every control that looks usable works; empty states are truthful | Partial | `Ui.EmptyState` and guarded commands throughout. The roadmap lists exercising every destination interactively as open. |

## Navigation, search and commands

| Id | Requirement | Status | Evidence / gap |
| --- | --- | --- | --- |
| `onboarding-wizard` | Practical in-app first-run wizard with progress, Back/Next, resume and skip | Delivered | `Pages/SetupPage.cs`, `Pages/Setup/SetupSteps.cs` (detect Docker, engine context, project folder, language/appearance), persisted `SetupStep` and `SetupComplete`, "Skip for now". |
| `tabs` | One workspace tab, explicit New tab catalogue with search and regex, no automatic tabs | Delivered | `MainWindow.ShowCatalog` (search plus adjacent regex builder), Ctrl+T/W/Tab, pin tab, persisted `OpenTabs`. |
| `regex-search` | Complete RegexBuddy-parity regex builder next to every search | Partial | `UI/SearchBuilderPopover.cs`, `RegexBuilderEngine.cs` (bounded replace preview), `RegexBuilderExplain.cs`, `RegexBuilderLibrary.cs`, and a guard `SearchSurfaceCatalogue.cs` registering every search surface. Roadmap: full RegexBuddy parity is still open (no flavour conversion, tracing, benchmarking or code generation found). |
| `command-palette` | Ctrl+Shift+F palette listing every command, page, setting and appearance control | Delivered | `Shell/CommandPalette.cs` with search plus regex builder, live controls in rows, shortcut chips and a full-window mode. |
| `context-menus` | Target-specific context menu with keyboard equivalent on every element | Partial | Menus on rail items, the title bar, page areas, tabs and `DataTable` rows. There is no menu on every element, and no "Lock this element" entry. |
| `menu-shortcuts` | Context-menu shortcuts shown right-aligned and derived from the real binding | Partial | `Ui.CommandMenuItem` takes shortcut text from `AppCommand.Gesture` (`UI/Ui.cs`); a source scan in `tests/UiState/RecordsChecks.cs` fails on a hand-typed shortcut that is not a declared list-local key. List-local keys, the palette hint and the accessibility shortcut list are still typed text, and the menus were not checked in the built window. |
| `rich-controls` | Real controls instead of printed values wherever a value is shown | Partial | Run editor, appearance editor, `DataTable` columns and settings rows use real controls. Several inspectors still print values as text. |
| `guided-forms` | Pickers, suggested defaults, inline validation, novice-friendly forms | Partial | `UI/StructuredInputs.cs`, `Pages/Suggestions/EditorValuePickers.cs`, Creator and Run editor forms. Not audited for every form. |
| `info-icon-guidance` | Per-control info icon buttons with contextual help | Partial | One `Ui.Info` component (`UI/Ui.cs`) is used by every settings row, with a default-and-undo fallback (`UI/InfoGuidance.cs`), and by the field labels that lacked it. The guard covers settings rows only; the icon was not exercised in the built window. |
| `settings-explanations` | Every setting explains itself with a provenance line | Delivered | `Pages/Settings/SettingRows.cs` (explanation, "more" text, provenance line saying default or saved, per-row reset). |
| `blank-editors` | Presets and real-default starting points for blank editors | Partial | Dockerfile Creator starters and project detection (`Pages/Creator`, `UI/DockerfileModelStarters.cs`). Compose and other editors have no preset picker. |

## Safety, locks and authentication

| Id | Requirement | Status | Evidence / gap |
| --- | --- | --- | --- |
| `super-confirmation` | Two-key confirmation for every destructive action | Delivered | `OverlayHost.SuperConfirmAsync` (two switches, slide-to-confirm, emergency exit) used by cleanup, removal, reset and prune flows in 17 files. |
| `element-locks` | Lock any element, tab or appearance value with PIN, password, authenticator or similar | Missing | No lock model or wizard. |
| `support-tickets` | Support Tickets recovery desk for locked-out users | Missing | Depends on element locks, which do not exist yet. |
| `unlock-ladder` | Dim-sum, sums, whack-a-mole and clock ladder for unlocking | Missing | Depends on element locks, which do not exist yet. |
| `authenticator` | Built-in TOTP authenticator destination | Missing | No TOTP or `otpauth` code. |
| `qr-pairing` | Local QR drawing for authenticator pairing | Missing | No QR encoder; depends on the authenticator. |
| `honest-monetization` | Free by default, honest if paid, never nagging | Not applicable | The product is free and has no monetization surface. |

## Records, history and messages

| Id | Requirement | Status | Evidence / gap |
| --- | --- | --- | --- |
| `local-history` | Local version history beside the data directory with diff, restore, export | Delivered | `Services/HistoryStore.cs` (LibGit2Sharp repository under local app data), `Pages/ActivityPage.cs`, `docs/features/local-history.md`. |
| `exports` | Export every record in every faithful format with loss disclosure | Partial | `DataTable` exports or copies CSV, TSV, JSON schema 1, Markdown and HTML through `UI/TableExport.cs` with checked escaping; settings JSON export/import, history ZIP and the Docker diagnostic report remain. Non-table views and loss disclosure beyond display text are not covered. |
| `bulk-actions` | Multi-select, select-all, invert and bulk bars on every list | Delivered | `UI/DataTable.cs` (shift/ctrl selection, select all matching, invert, clear, bulk bar) is the base of the app lists, including the notification centre and history. |
| `changelog` | Changelog viewer with every release, categories and clickable commit references | Partial | Tools > Changelog (`Pages/Tools/ChangelogView.cs`, `UI/ChangelogModel.cs`) lists a git-log history bundled at build time by `build/make-changelog.ps1`, grouped by tag, with regex search, typed date range, commit links and Markdown copy/export. No calendar picker, no per-language voice, and nothing was driven in the built window. |
| `offline-docs` | Bundled offline documentation browser rendered by a shared Markdown renderer | Delivered | Articles embedded by the project file; `Pages/Tools/DocsBrowser.cs`, `MarkdownView.cs`, search and internal link routing. |
| `rendered-provider-text` | Forge release notes, issues and commit messages rendered as Markdown | Not applicable | The product does not display forge-authored text. Revisit if release notes are added to the changelog. |
| `notifications` | Non-blocking toasts with a notification centre | Delivered | `Services/Notifier.cs` (corner toasts, auto-dismiss except errors and warnings, live-region announcements) and the centre table in `MainWindow.xaml.cs`. |

## Built-in tools and hand-off

| Id | Requirement | Status | Evidence / gap |
| --- | --- | --- | --- |
| `file-converter` | Universal local file converter | Missing | No converter. Only Base64 and Docker-specific conversions exist. |
| `base64-tools` | Full local Base64 toolkit | Delivered | `Pages/Tools/Base64Panel.cs`, `UI/Base64Tools.cs`. Individual variant options were not re-audited. |
| `minecraft-server-manager` | Server creator and manager on local process, local container, SSH process, SSH container | Partial | `Pages/Minecraft/*` (six-step wizard, console, players, properties, files, backups, version picker). It only targets containers on the selected engine context (local or SSH Docker context). Local Windows process and SSH process targets, flavour and custom-plugin installation are absent. |
| `minecraft-world-generator` | World generator in each server tab with typed customization and area canvas | Partial | `MinecraftWorldTab.cs` and `MinecraftAreaCanvas.cs` pre-generate an area with Chunky (default 100 x 100 chunks). No typed world customization or per-version jar extraction. |
| `ollama-suite` | One-click managed Ollama installer and manager | Missing | No Ollama code. |
| `external-editor` | Detect, choose and persist an external editor | Delivered | `Pages/Build/ExternalEditor.cs`, `Settings > Engine` editor picker with validation and reset. |
| `download-handoff` | Browser-extension download hand-off dialogs | Not applicable | Applies only to browser extensions and their paired applications. |
| `forge-publishing` | Publish a repository with multi-account sign-in | Not applicable | The product does not publish repositories. It has registry sign-in only. |
| `progress-recovery` | Real progress in the dialog, no re-entry, recovery where it broke | Partial | `Shell/OperationsDock.cs` (progress, elapsed time, cancel), `Ui.AsyncButton` re-entry guard, BuildKit progress. Not every dialog hosts its own progress. |
| `drawesome-drawing` | Drawing and annotation toolset | Missing | No drawing tool. |

## Completeness and evidence

| Id | Requirement | Status | Evidence / gap |
| --- | --- | --- | --- |
| `completeness-parity` | Per-surface completeness inventory with hand-written required list and guard | Partial | `website/feature-inventory.json`, `website/required-feature-ids.json` (109 ids), `scripts/test-feature-checklist.mjs`. Every row is still "unverified" and the list predates the current 115 ids. |
| `front-provenance` | Version and updated-at time with seconds and timezone on the first screen | Partial | Title bar shows version and update time (`MainWindow.xaml.cs`). The website home page does not. |
| `product-evidence` | Real captures of every surface, state, theme and language, grouped under collapsible sections | Partial | `evidence/screenshots` (dark, light and minimum size) and `evidence/inventory.json`. English only, no empty or error states, no collapsible grouping in the README. |
| `screen-recording` | Recorded session of the built application shown in the README | Missing | No recording exists. |
| `design-reference-parity` | Reference viewer and parity comparison against the checked-in design | Missing | `design/` holds the reference, but there is no deterministic reference renderer or comparison diff. |
| `design-folder` | Design surfaces are stored in the repository `design/` folder | Delivered | `design/README.md`, `CANVAS.md`, `STATES.md`, `WEBSITE.md`, `design/workspace-redesign-2026-11/`. |
| `layout-overflow-matrix` | Layout and clipping matrix across sizes, languages, themes and display scales | Delivered | `--capture-probe` (`Shell/LayoutProbe.cs`) over 264 surfaces: three languages at 1440, 1200 and 960 px, light theme and 200 % scale. Clipping found was fixed; the final run reports no overflow (`evidence/layout-matrix-2026-11/summary.json`). |
| `public-screenshot-gallery` | Separate public screenshot gallery for a private source repository | Not applicable | Applies to private source repositories; this repository is public and already publishes captures. |

## Documentation site and shared links

| Id | Requirement | Status | Evidence / gap |
| --- | --- | --- | --- |
| `landing-site` | Material Design 3 documentation site carrying every feature | Partial | `website/` (generated by `render.mjs`, deployed by `.github/workflows/pages.yml`) with walkthroughs, an example, search and 13 embedded articles. Styling is not M3 and not every feature is covered. |
| `mobile-website` | Responsive from about 320 px with viewport meta, no sideways scroll | Delivered | `website/template.html` viewport meta, 760 px breakpoint, wrapping controls. `docs/features/feature-checklist.md` records a bounded 320 and 1440 px browser check. |
| `installer-download-button` | Verified installer download button on Home | Missing | The site links to the source only; no release manifest or asset button. |
| `feature-articles` | One detailed, complete article per feature with suggested next articles | Partial | 19 guides in `docs/features` render on the site. They are grouped by area, not one per feature, and carry no "next articles" lists. |
| `vendored-fonts` | Fonts vendored completely by a script | Partial | The app bundles Material Symbols (`Assets/Fonts`) and the site uses system fonts, but there is no vendoring script. |
| `shared-link-embed` | Real product picture when the link is shared | Missing | No `social-preview.png` and no Open Graph tags in `website/template.html`. |
| `homepage-link` | Repository About website field points to the exact deployed site | Delivered | Recorded as verified in `ROADMAP.md`; not re-read from the host in this audit. |
| `vocabulary-unlock-boundary` | Private wording never appears before authentication | Delivered | The site is public and uses ordinary language; `scripts/scan-public.mjs` scans tracked files. No authenticated surface exists. |
| `offline-documentation-companion` | Private repository ships its documentation as a packaged desktop app | Not applicable | Applies to private repositories; this repository hosts its site publicly. |

## Status Hub

| Id | Requirement | Status | Evidence / gap |
| --- | --- | --- | --- |
| `status-hub` | Register with the shared Status Hub and show a status surface | Missing | No Hub client or status surface (`docs/features/capability-inventory.md` lists it as unverified). |
| `discord-status-bridge` | Discord bridge for the Hub | Not applicable | A feature of the Hub service itself. |
| `panic-webhooks` | Verified panic dispatch | Not applicable | A feature of the Hub service itself. |
| `tidbyt-displays` | Tidbyt status frames | Not applicable | A feature of the Hub service itself, and currently suspended by the maintainer. |

## Build and install

| Id | Requirement | Status | Evidence / gap |
| --- | --- | --- | --- |
| `build-entrypoints` | Root build.bat and build-installer.bat that bootstrap everything | Delivered | `build.bat`, `build-installer.bat`, `download-dependencies.bat`, `build/*.ps1` propagate exit codes and fetch Node, the .NET SDK, NuGet and Squirrel with pinned hashes (`build/dependencies.json`). No fresh-machine receipt, and no elevation step. |
| `fresh-build-run-command` | One copy-paste command that builds and runs on a fresh machine | Missing | README documents `build.bat` but there is no `--run` option in `build/publish.ps1`. |
| `dependency-fetcher` | One-click dependency fetcher | Delivered | `download-dependencies.bat` and `build/bootstrap.ps1` (hash-verified downloads into `.tools`). Windows only, so no shell variant is required. |
| `bundled-dependencies` | Installer bundles every dependency; works offline | Partial | Self-contained .NET publish (`build/publish.ps1`). The app still requires an installed Docker CLI, which Setup detects and reports. |
| `squirrel-installer` | Genuine Squirrel.Windows installer | Delivered | `build/package.ps1` produces Setup.exe, RELEASES and the full package with Squirrel 2.0.1, and `App.xaml.cs` handles lifecycle hooks. No delta packages. |
| `self-signing` | Sign only when the format requires it, with a project-owned credential | Delivered | Unsigned by design; the release notes state it. No signature is required for the produced formats. |
| `auto-updates` | Chrome-style background updates with validated feed | Partial | `Services/UpdateService.cs` checks the published RELEASES file for an installed copy and stages it with Update.exe; `UI/UpdateFeed.cs` is checked; `Shell/UpdateBanner.cs` shows the unsigned-build banner and restarts only on confirmation. Never run against a real feed or installed copy; no signature check or deferral setting. |
| `app-icon` | Original logo and packaged multi-resolution icon | Missing | No `ApplicationIcon` in the project file and no `.ico` or logo master source. |

## Release pipeline

| Id | Requirement | Status | Evidence / gap |
| --- | --- | --- | --- |
| `release-workflow` | A unique normal release on every push | Delivered | `.github/workflows/release.yml` builds, packages and publishes a uniquely tagged non-draft release, then reads back draft, prerelease, tag and assets. |
| `release-timing` | Workflow start, completion and duration in release notes | Delivered | Present in the notes block of `release.yml`. |
| `release-line-counts` | Line counts produced by the workflow in release notes | Missing | No counter script and no table in the notes. |
| `release-dim-sum-photo` | Release notes reference the public dim-sum catalogue | Missing | Notes state that no catalogue entry was selected. |
| `dim-sum-code-names` | Dim-sum code name beside each version | Missing | No code names in the build or releases. |
| `ci-bootstrap` | Explicit workflow dependency inventory and safe bootstrap | Partial | Bootstrapping happens through `build-installer.bat`, but no inventory file sits beside the workflow. |
| `runner-selection` | Choose runners from a live inventory | Partial | Uses the GitHub-hosted `windows-2025` runner; no recorded self-hosted inventory check. |
| `encrypted-public-builder` | Encrypted builder for private repositories | Not applicable | This repository is public. |

## Repository records and README

| Id | Requirement | Status | Evidence / gap |
| --- | --- | --- | --- |
| `tabbed-readme` | README opens with an index and folds long sections into details blocks | Partial | README has an index line and sections but no `<details>` blocks. |
| `human-time-estimate` | Labelled estimate of how long a person would take to write the project | Missing | Not in the README. |
| `sanitized-instruction-copy` | Sanitized mirror of shared instructions in README and AGENTS.md | Partial | `AGENTS.md` carries project scope and rules; the README has no mirror section. |
| `private-conversation-discipline` | Sanitized vocabulary-discipline block in AGENTS.md | Delivered | `AGENTS.md` contains the "Agent conversation vocabulary" block. |
| `vocabulary-hash-lock` | Builds and pushes gated on a currency lock | Partial | `build.bat` runs `scripts/check-vocabulary.mjs` and skips when the private source is absent. No pre-push hook is checked in. |
| `roadmap-checklist` | ROADMAP.md as a real checkbox list | Delivered | `ROADMAP.md` uses grouped `- [ ]` and `- [x]` items. |
| `feature-docs` | One Markdown file per feature in a categorized folder with indexes | Partial | `docs/features/README.md` indexes 19 guides, but they are one flat folder grouped by area. |
| `postman-collections` | Postman collections for HTTP APIs | Not applicable | The application exposes no HTTP API. |
| `wiki-documentation-sync` | Wiki and site source kept current | Partial | `wiki/Home.md` is 11 lines and is rendered by `render.mjs`. Publication to the GitHub wiki was not checked. |
| `handoff-record` | HANDOFF.md plus a handoff section on a GitHub issue | Partial | `HANDOFF.md` exists (43 lines). The issue handoff cannot be checked from source. |
| `closeout-prompt` | CLOSEOUT_PROMPT.md at the repository root | Delivered | `CLOSEOUT_PROMPT.md` exists (13 lines). |
| `discussion-records` | Features listed in private Discussions | Not applicable | Maintainer process on external state; not checkable from source. |
| `project-board` | GitHub Project linked to the repository | Not applicable | External state; not checkable from source. |
| `operational-skill` | Repository-specific operational skill | Not applicable | Maintained outside the repository in the maintainer skill catalogue. |

## Instruction-repository deliverables and games

| Id | Requirement | Status | Evidence / gap |
| --- | --- | --- | --- |
| `readme-prompt-banner` | README opens with the prompt banner | Not applicable | Applies only to the maintainer's canonical instructions repository. |
| `shared-instruction-portable-editions` | Single-file editions of the shared instructions | Not applicable | Applies only to the canonical instructions repository. |
| `project-profile` | Project memory folders | Not applicable | Applies only to the canonical instructions repository. |
| `roblox-model-catalogue` | Shared Roblox model catalogue use | Not applicable | Applies only to Roblox games. |
| `roblox-visual-realism` | Roblox visual realism | Not applicable | Applies only to Roblox games. |

## Prioritised implementation backlog

Ordered by user impact inside each lane. Ids refer to the tables above.

### Language and voice
1. `vocabulary-drop-apply` and `personal-vocabulary`: drop-anywhere import with the celebration, live refresh without reopening pages (`Services/Loc.cs`, `MainWindow.xaml.cs`, `Pages/Settings/SettingsSections.cs`).
2. `language` and `funny-emoji`: translate page-specific copy and route more messages through `Loc.Voice` (`Services/Loc.cs`, every `Pages/*`).
3. `action-sound-effects`, `narration`, `school-mode`, `adhd-modes` (Momentum consumer), `scheduled-settings`: finish settings that exist but do little (`Services/Notifier.cs`, `Services/Narrator.cs`, `UI/UserSettings.cs`).
4. `dim-sum`, `dim-sum-photo-source`: pictures from the public catalogue (`MainWindow.xaml.cs`).

### Appearance and layout
5. `collapse-filters`: wire the stored state to `SearchFilter` and statistics strips (`UI/SearchFilter.cs`, `UI/UserSettings.cs`).
6. `logo-customization` and `app-icon`: logo presets, upload, packaged `.ico` (`MaterialDockerImage.csproj`, `Pages/Settings`, `build/package.ps1`).
7. `appearance-editor`, `color-picker`, `accessibility-sizing`, `layout-overflow-matrix`: per-element coverage, named colours and CIELAB, visible-focus option, full language/theme/scale evidence.

> Update 2026-11-04: priorities 9, 10 (shortcut text), 12, 13 (tables) and 17 have a first implementation on branch `ai/feature/20261104-records-tools`; the rows above state what remains.

### Navigation and search
8. `regex-search`: RegexBuddy-parity workbench (`UI/RegexBuilder*.cs`, `UI/SearchBuilderPopover.cs`).
9. `info-icon-guidance`: shared info-button component (`UI/Ui.cs`, `Pages/Settings/SettingRows.cs`).
10. `context-menus` and `menu-shortcuts`: derive shortcut text from `AppCommand.Gesture` (`Services/Commands.cs`, `UI/Ui.cs`).

### Safety and records
11. `element-locks`, `support-tickets`, `unlock-ladder`, `authenticator`, `qr-pairing`: new lock model and tools (new `Services/Locks`, `Pages/Tools`, `Shell/OverlayHost.cs`).
12. `changelog`: per-release list with commit references (`Pages/ToolsPage.cs`).
13. `exports`: formats and loss disclosure beyond tables (`UI/DataTable.cs`, per-page exporters).

### Container controls
14. `minecraft-server-manager`, `minecraft-world-generator`: local-process and SSH-process targets, plugin installer, typed world options (`Pages/Minecraft/*`, `UI/MinecraftServerSpec.cs`).
15. The unlisted roadmap items for health checks, device mappings, GPU access and terminals (`Pages/Images/RunEditor*.cs`, `Core/DockerService.cs`).
16. `file-converter`, `ollama-suite`, `drawesome-drawing`: new tools (`Pages/Tools`).

### Build and release
17. `auto-updates`: feed check and staged Squirrel update (`App.xaml.cs`, new `Services/Updater.cs`, `build/package.ps1`).
18. `fresh-build-run-command`: `--run` in `build.bat` (`build/publish.ps1`, `README.md`).
19. `release-line-counts`, `dim-sum-code-names`, `release-dim-sum-photo`, `ci-bootstrap`, `runner-selection`: workflow additions (`.github/workflows/release.yml`, a new counter script).

### Documentation site
20. `installer-download-button`, `shared-link-embed`, `front-provenance` (site): release manifest button, social preview image, version and timestamp (`website/template.html`, `website/render.mjs`).
21. `landing-site`, `feature-articles`, `vendored-fonts`: Material Design 3 styling, one article per feature with next-article links, font script.

### Repository records
22. `completeness-parity`: move the 109-id list to the current ids and update statuses (`website/required-feature-ids.json`, `website/feature-inventory.json`).
23. `product-evidence`, `screen-recording`, `design-reference-parity`: more state captures, a recording, a reference renderer.
24. `tabbed-readme`, `human-time-estimate`, `sanitized-instruction-copy`, `wiki-documentation-sync`, `feature-docs`, `vocabulary-hash-lock`: README folding and estimate, wiki content, hook.

## Update after the 4 November 2026 feature pass

Source-level changes since the audit commit (still not runtime verification unless a receipt says so):

- Now implemented with checks: personal wording drop, page-copy localization, display name, application icon, logo customization, container health checks, device mappings, GPU access, build companion, offline documentation companion, download button, link previews, front provenance, documentation search, changelog, exports, menu shortcuts, info icons, update checks, attention modes, action sounds, collapsible filters.
- Partial: regular expression parity (see `docs/features/regex-parity.md`), element locks (no PIN or authenticator policies), scheduled settings (language and theme only, local file source only), context menus.
- Unchanged: unlock ladder, support tickets, authenticator, dim sum photo source review, release line counts.
- Interactive evidence: `evidence/interactive-2026-11/receipt.json`.

## Update at the end of the 4 November 2026 pass (main 0108f3b)

Supersedes the partial and unchanged lists of the previous update where they differ.

- Element locks: confirm-only, PIN and authenticator-app policies on settings rows and major page buttons, with credentials in `locks.json` (`Shell/LockUi.cs`). Still partial: password and combined policies, unlock durations, locks on arbitrary elements.
- Unlock ladder: confirm, credential, persistent exponential wait and single-use recovery codes. Still partial: the dim sum, sums and whack-a-mole rungs and Support Tickets are not built.
- Authenticator: a Tools section with an in-process QR code for lock enrolment. A general authenticator for arbitrary accounts is not built.
- Scheduled settings: seven settings by time, weekday and optional date range, with a local JSON file, an https address or a Home Assistant entity as value source. The remote sources have not talked to a real server.
- Action sounds: volume and quiet hours added; reduced-volume playback has not been tried on a real audio device.
- Regular expression parity: 63 of 83 topics implemented, 8 partial, 12 unavailable (most are impossible on the .NET engine). The popover was opened in the built window on a hidden desktop; its tool sections have logic checks only.
- Interactive evidence: the receipt now has 17 steps, including a successful registry sign-in and sign-out, an interactive terminal through the classic console option, Compose up and down, exec, file changes, the prune confirmation and Emergency exit, a settings history restore, the registry sign-in failure path and the regex popover. Not exercised: the terminal's default Windows Terminal path.
- Checks: `build.bat -Tests` passes 23 core, 1,621 UI-state and 24 checklist checks.

## Update after the built-window drive (4 November 2026, afternoon)

- Driven in the built window and recorded in the receipt (25 steps): the authenticator (codes match an independent RFC 6238 calculation), a support ticket, School mode with a PIN, a PIN lock through a lockout, the dish game and unlock, an anchored regex filter, registry sign-in and sign-out, and an interactive terminal through the classic console option.
- Fixed because of that drive: the dish game never opened (a reused Cancel button), anchored regex filters matched nothing in tables (rows were matched as one string), the School mode PIN note did not update, Sign out buttons shared one accessible name, and several hints could not wrap.
- `layout-overflow-matrix` is delivered: a layout probe covered 264 surfaces and the clipping it found was fixed.
- Not driven: the per-element appearance editor (its entry points need keyboard focus or a pointer, which a hidden-desktop window does not receive), the sums and whack-a-mole rungs, combined lock policies, the timed relock and the Locks manager, and the default Windows Terminal path.
- Still not built: tab, tab-group and appearance-value locks, the keypad PIN entry, a School mode passkey, QR image or camera registration for the authenticator, and the regex topics marked partial or unavailable in `docs/features/regex-parity.md`.
- Checks: `build.bat -Tests` passes 23 core, 1,974 UI-state and 24 checklist checks.
