# Capability inventory

Implemented controls, focused checks, real native execution and complete acceptance are separate states. The productive application is native WPF. All 109 required identifiers below remain retained. No row claims complete acceptance from a partial workflow.

## Product workflows

| Workflow | Implemented behavior and bounded evidence |
| --- | --- |
| Structured Docker execution | 23 core checks passed for argument separation, output bounds, cancellation and parsing. |
| Build workspace | Multiple tags, labels, target/platform selection, local load, registry push, OCI archive, cache import/export and secret-file rows implemented. Real native build at f370d66 verified exact label whitespace. OCI/cache/secret and registry runtime combinations are not all exercised. |
| Run editor | Explicit JSON argv, non-secret environment, entrypoint/user/workdir, CPU/memory, restart policy and additional port/mount rows implemented. Real native run at f370d66 verified exact environment whitespace. GPU, devices, health-check configuration and interactive terminals remain gaps. |
| Native Canvas | Stage graph/object selection, inspector, supported alias/base edits, target selection, source navigation, zoom/pan/fit and inspected runtime memberships implemented. 51 UI-state/parser checks passed. Graph rendering is not proof of every interaction or Dockerfile construct. |
| Images, resources and Compose | Image/container/volume/network operations and loaded Compose save-path/context protection implemented. Complete workflow acceptance remains open. |
| Restart restoration | Source 3ce1ed7 local 0.5.0 build/installer production and actual restart restoration passed, preserving the absolute example folder and exact source after line-ending normalization. Included in verified normal v0.5.1 at ad5d3ac. Original capture provenance remains 3ce1ed7/0.5.0. |
| Package and release | Normal v0.5.1 at ad5d3ac5c02588e8efcc926715119b87a9c7c6d5 was verified through successful run 39112649034, exact tag source and five asset sizes/digests. |
| Installed update and setup | Existing installation updated through Update.exe to 0.5.1.0; executable matched the published package and settings were preserved. Installed hidden launch detected Docker, exercised Setup Back/Next/Finish, persisted completion at step 2 and displayed productive Build. Fresh install, update-ready banner, cancellation and uninstall acceptance remain open. |
| Current native captures | English, dark, 100% scaling at normal/minimum sizes only. README captures are retained and inspected for 3ce1ed7 local 0.5.0: Build 1440x960, populated Canvas 1440x960 and 960x720. Minimum graph panning and inspector scrolling are intentional. Full matrix is incomplete. |
| Documentation and companions | Deployment 39112649029 for ad5d3ac delivered 12 articles, 109 neutral identifiers and three byte-identical PNGs. 1440/320 widths had no horizontal overflow; guide/checklist/theme/bilingual persistence and exact About homepage passed. Complete documentation/wiki acceptance and portable documentation/build companions remain incomplete. |

## Required-feature inventory

<!-- Generated from website/feature-inventory.json by website/render.mjs. -->

123 entries retain 109 required identifiers, 10 product workflows and 4 milestones. Fully verified: 0. Implemented with verification pending: 28. Planned: 0. Unverified: 95.

### Interface and personalization

| Identifier | Capability | Status | Remaining actions |
| --- | --- | --- | --- |
| `accessibility-sizing` | Accessibility Sizing | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for accessibility-sizing. |
| `adhd-modes` | Adhd Modes | Implemented, verification pending | Drive each mode in the built window, capture the Focus and Momentum states and check them in all language modes, themes and display scales. |
| `app-icon` | App Icon | Implemented, verification pending | Inspect the icon in the installed shortcut, taskbar and Settings app list from the released installer, and capture it from the built application. |
| `appearance-editor` | Appearance Editor | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for appearance-editor. |
| `blank-editors` | Blank Editors | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for blank-editors. |
| `changelog` | Changelog | Implemented, verification pending | Calendar date picker with month/year jump, range presets and the language-mode voice for entries are not built; entries show original subject lines only, newer releases published after a build are not listed, and nothing was driven in the built window yet. |
| `layout-overflow-matrix` | Layout Overflow Matrix | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for layout-overflow-matrix. |
| `color-picker` | Color Picker | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for color-picker. |
| `mobile-website` | Mobile Website | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for mobile-website. |
| `dim-sum` | Dim Sum | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for dim-sum. |
| `dim-sum-code-names` | Dim Sum Code Names | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for dim-sum-code-names. |
| `dim-sum-photo-source` | Dim Sum Photo Source | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for dim-sum-photo-source. |
| `display-name` | Display Name | Implemented, verification pending | Verify rename, reset, restart persistence and the history revision count interactively in the built application in every language mode and theme. |
| `front-provenance` | Front Provenance | Implemented, verification pending | Verify the stamp on the deployed page after the next documentation change, and decide whether the in-application front screen needs its own provenance verification record. |
| `functional-ui` | Functional Ui | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for functional-ui. |
| `funny-emoji` | Funny Emoji | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for funny-emoji. |
| `guided-forms` | Guided Forms | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for guided-forms. |
| `honest-monetization` | Honest Monetization | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for honest-monetization. |
| `human-time-estimate` | Human Time Estimate | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for human-time-estimate. |
| `info-icon-guidance` | Info Icon Guidance | Implemented, verification pending | Roughly a dozen fields were filled by hand and the guard counts only settings rows; other forms may still have labels without an info icon, and the icon was not exercised in the built window. |
| `language` | Language | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for language. |
| `logo-customization` | Logo Customization | Implemented, verification pending | Verify the picker interactively in the built application at every language mode, theme and display scale; add crop, fit and background editing, scheduled settings and appearance export, and capture the real selected logo. |
| `material-design` | Material Design | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for material-design. |
| `motion` | Motion | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for motion. |
| `narration` | Narration | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for narration. |
| `notifications` | Notifications | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for notifications. |
| `onboarding-wizard` | Onboarding Wizard | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for onboarding-wizard. |
| `operational-skill` | Operational Skill | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for operational-skill. |
| `overlay-panels` | Overlay Panels | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for overlay-panels. |
| `rendered-provider-text` | Rendered Provider Text | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for rendered-provider-text. |
| `rich-controls` | Rich Controls | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for rich-controls. |
| `sanitized-instruction-copy` | Sanitized Instruction Copy | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for sanitized-instruction-copy. |
| `scheduled-settings` | Scheduled Settings | Unverified | Add the remaining schedulable settings and the API and Home Assistant sources, then drive the editor in the built window. |
| `screen-recording` | Screen Recording | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for screen-recording. |
| `settings-explanations` | Settings Explanations | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for settings-explanations. |
| `shared-link-embed` | Shared Link Embed | Implemented, verification pending | Confirm how real platforms render a shared link to the deployed site (platform crawlers were not exercised) and refresh the preview image when the application is redesigned. |
| `support-tickets` | Support Tickets | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for support-tickets. |
| `vendored-fonts` | Vendored Fonts | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for vendored-fonts. |

### Navigation and workflow

| Identifier | Capability | Status | Remaining actions |
| --- | --- | --- | --- |
| `bulk-actions` | Bulk Actions | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for bulk-actions. |
| `collapse-filters` | Collapse Filters | Implemented, verification pending | Check every collapsible surface in the built window at narrow widths and in all language modes; panels other than chip bars and statistic strips are not collapsible yet. |
| `command-palette` | Command Palette | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for command-palette. |
| `context-menus` | Context Menus | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for context-menus. |
| `exports` | Exports | Implemented, verification pending | Non-table views, settings and history exports keep their own formats; no export disclosure of lossy columns beyond display text, and no export was driven in the built window yet. |
| `external-editor` | External Editor | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for external-editor. |
| `local-history` | Local History | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for local-history. |
| `menu-shortcuts` | Menu Shortcuts | Implemented, verification pending | List-local keys (Delete, Enter, Alt+Up, Alt+Down, Ctrl+A, Ctrl+C) are still typed beside their handlers; the palette hint and the accessibility shortcut list are still static text; nothing was checked in the built window. |
| `progress-recovery` | Progress Recovery | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for progress-recovery. |
| `regex-search` | Regex Search | Unverified | Close the partial and unavailable rows of the RegexBuddy parity matrix that .NET allows (replacement analysis, match navigation, tree export, more conversion targets, parameters), keep the engine-impossible rows documented, verify the popover and its dialogs interactively, localize engine messages, and capture source-bound HuiShots. |
| `tabs` | Tabs | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for tabs. |
| `workflow-navigation` | Workflow Navigation | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for workflow-navigation. |

### Safety and privacy

| Identifier | Capability | Status | Remaining actions |
| --- | --- | --- | --- |
| `private-conversation-discipline` | Private Conversation Discipline | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for private-conversation-discipline. |
| `authenticator` | Authenticator | Unverified | Build the general authenticator for arbitrary accounts with live codes, import routes and super-confirmed secrets export, then drive both sections in the built window. |
| `element-locks` | Element Locks | Unverified | Drive the lock wizard, the disabled behaviour and the ladder in the built window; add the password policies, unlock durations, tab, group and appearance locks, locks on every element and the recovery desk. |
| `personal-vocabulary` | Personal Vocabulary | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for personal-vocabulary. |
| `school-mode` | School Mode | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for school-mode. |
| `self-signing` | Self Signing | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for self-signing. |
| `super-confirmation` | Super Confirmation | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for super-confirmation. |
| `unlock-ladder` | Unlock Ladder | Unverified | Build the dim sum, ten sums and whack-a-mole rungs with their hourly budget and nonce grading, School mode starting at the sums, then drive the ladder in the built window. |
| `vocabulary-drop-apply` | Vocabulary Drop Apply | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for vocabulary-drop-apply. |
| `vocabulary-hash-lock` | Vocabulary Hash Lock | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for vocabulary-hash-lock. |
| `vocabulary-unlock-boundary` | Vocabulary Unlock Boundary | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for vocabulary-unlock-boundary. |

### Build and delivery

| Identifier | Capability | Status | Remaining actions |
| --- | --- | --- | --- |
| `auto-updates` | Auto Updates | Implemented, verification pending | Never exercised against a real published feed or an installed copy: no staged update, banner, restart or rollback has been observed; there is no feed signature check and no download progress or deferral setting. |
| `build-entrypoints` | Build Entrypoints | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for build-entrypoints. |
| `bundled-dependencies` | Bundled Dependencies | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for bundled-dependencies. |
| `ci-bootstrap` | Ci Bootstrap | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for ci-bootstrap. |
| `dependency-fetcher` | Dependency Fetcher | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for dependency-fetcher. |
| `download-handoff` | Download Handoff | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for download-handoff. |
| `encrypted-public-builder` | Encrypted Public Builder | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for encrypted-public-builder. |
| `forge-publishing` | Forge Publishing | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for forge-publishing. |
| `fresh-build-run-command` | Fresh Build Run Command | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for fresh-build-run-command. |
| `installer-download-button` | Installer Download Button | Implemented, verification pending | Verify the deployed home page on the live site, on a phone-width screen, in each language mode and with a release that carries every named asset; confirm keyboard use and the rate-limited path against the real service. |
| `release-dim-sum-photo` | Release Dim Sum Photo | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for release-dim-sum-photo. |
| `release-line-counts` | Release Line Counts | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for release-line-counts. |
| `release-timing` | Release Timing | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for release-timing. |
| `release-workflow` | Release Workflow | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for release-workflow. |
| `runner-selection` | Runner Selection | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for runner-selection. |
| `squirrel-installer` | Squirrel Installer | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for squirrel-installer. |

### Documentation and evidence

| Identifier | Capability | Status | Remaining actions |
| --- | --- | --- | --- |
| `closeout-prompt` | Closeout Prompt | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for closeout-prompt. |
| `completeness-parity` | Completeness Parity | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for completeness-parity. |
| `design-folder` | Design Folder | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for design-folder. |
| `design-reference-parity` | Design Reference Parity | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for design-reference-parity. |
| `discussion-records` | Discussion Records | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for discussion-records. |
| `feature-articles` | Feature Articles | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for feature-articles. |
| `feature-docs` | Feature Docs | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for feature-docs. |
| `handoff-record` | Handoff Record | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for handoff-record. |
| `homepage-link` | Homepage Link | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for homepage-link. |
| `landing-site` | Landing Site | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for landing-site. |
| `offline-docs` | Offline Docs | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for offline-docs. |
| `offline-documentation-companion` | Offline Documentation Companion | Implemented, verification pending | Run the packaging in the hosted release workflow, download the attached archive from a real release and open it from a file location in a browser; the interactive offline experience and the other portable documentation duties (the full wiki) remain unverified. |
| `product-evidence` | Product Evidence | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for product-evidence. |
| `project-board` | Project Board | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for project-board. |
| `project-profile` | Project Profile | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for project-profile. |
| `public-screenshot-gallery` | Public Screenshot Gallery | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for public-screenshot-gallery. |
| `readme-prompt-banner` | Readme Prompt Banner | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for readme-prompt-banner. |
| `roadmap-checklist` | Roadmap Checklist | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for roadmap-checklist. |
| `tabbed-readme` | Tabbed Readme | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for tabbed-readme. |
| `shared-instruction-portable-editions` | Shared Instruction Portable Editions | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for shared-instruction-portable-editions. |
| `wiki-documentation-sync` | Wiki Documentation Sync | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for wiki-documentation-sync. |

### Tools and integrations

| Identifier | Capability | Status | Remaining actions |
| --- | --- | --- | --- |
| `discord-status-bridge` | Discord Status Bridge | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for discord-status-bridge. |
| `file-converter` | File Converter | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for file-converter. |
| `minecraft-server-manager` | Minecraft Server Manager | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for minecraft-server-manager. |
| `ollama-suite` | Ollama Suite | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for ollama-suite. |
| `panic-webhooks` | Panic Webhooks | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for panic-webhooks. |
| `postman-collections` | Postman Collections | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for postman-collections. |
| `qr-pairing` | Qr Pairing | Unverified | Scan the QR code with a real authenticator app, check it in both themes in the built window, and add the image, clipboard and camera registration routes. |
| `roblox-model-catalogue` | Roblox Model Catalogue | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for roblox-model-catalogue. |
| `roblox-visual-realism` | Roblox Visual Realism | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for roblox-visual-realism. |
| `status-hub` | Status Hub | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for status-hub. |
| `tidbyt-displays` | Tidbyt Displays | Unverified | Confirm implementation, documentation, localization, persistence where applicable, focused regression checks and source-bound interaction evidence for tidbyt-displays. |

### Product workflows

| Identifier | Capability | Status | Remaining actions |
| --- | --- | --- | --- |
| `workflow-structured-execution` | Structured Docker execution | Implemented, verification pending | Complete the remaining workflow combinations and independent acceptance described in the source reference. Existing bounded evidence does not establish full verification. |
| `workflow-build-workspace` | Build workspace | Implemented, verification pending | Complete the remaining workflow combinations and independent acceptance described in the source reference. Existing bounded evidence does not establish full verification. |
| `workflow-run-editor` | Run editor | Implemented, verification pending | Complete the remaining workflow combinations and independent acceptance described in the source reference. Existing bounded evidence does not establish full verification. |
| `workflow-native-canvas` | Native Canvas | Implemented, verification pending | Complete the remaining workflow combinations and independent acceptance described in the source reference. Existing bounded evidence does not establish full verification. |
| `workflow-resource-management` | Images, resources and Compose | Implemented, verification pending | Complete the remaining workflow combinations and independent acceptance described in the source reference. Existing bounded evidence does not establish full verification. |
| `workflow-restart-restoration` | Restart restoration | Implemented, verification pending | Complete the remaining workflow combinations and independent acceptance described in the source reference. Existing bounded evidence does not establish full verification. |
| `workflow-package-release` | Package and release | Implemented, verification pending | Complete the remaining workflow combinations and independent acceptance described in the source reference. Existing bounded evidence does not establish full verification. |
| `workflow-installed-setup` | Installed update and setup | Implemented, verification pending | Complete the remaining workflow combinations and independent acceptance described in the source reference. Existing bounded evidence does not establish full verification. |
| `workflow-native-captures` | Current native captures | Implemented, verification pending | Complete the remaining workflow combinations and independent acceptance described in the source reference. Existing bounded evidence does not establish full verification. |
| `workflow-documentation-companions` | Documentation and companions | Implemented, verification pending | Complete the remaining workflow combinations and independent acceptance described in the source reference. Existing bounded evidence does not establish full verification. |

### Planned milestones

| Identifier | Capability | Status | Remaining actions |
| --- | --- | --- | --- |
| `feature-checklist` | Feature completion checklist | Implemented, verification pending | Complete the capture-promotion and cleanup receipt. Isolated profile removal was rejected, so captures remain retained privately. Verify each later deployment separately; full accessibility and higher physical scales remain outside the bounded evidence. |
| `container-health-checks` | Container health checks | Implemented, verification pending | Exercise the editor fields and the live inspector refresh in the built WPF application, bound to the exact source commit and artifact hash, with captures; no GUI interaction was performed yet. |
| `container-device-mappings` | Container device mappings | Implemented, verification pending | Exercise the device rows and cgroup rule field in the built WPF application with captures, and test a real host device on a supported engine host; no GUI interaction was performed yet. |
| `container-gpu-access` | Container GPU access | Implemented, verification pending | Verify with a GPU-capable engine host that a container really sees the GPU, and exercise the controls in the built WPF application with captures; neither was performed. |


## Known limits

Complete RegexBuddy parity, full localization, protected local wording import, shared school mode, narration, advanced appearance controls, server-management integration, portable documentation/build companions and complete display-scale acceptance are not established. They must not be described as fully delivered behavior. All 109 required rows remain unverified for complete acceptance even where bounded implementation evidence exists above.

Remote engines, alternative architectures, GPU/device/health-check controls and interactive terminals need additional implementation or independent runtime acceptance. A successful local native build/run does not verify these routes. OCI export, registry push, supported cache backends and secret-file builds need their own runtime evidence.

Current native visual scope does not establish Cantonese/bilingual modes, light theme, 125%, 150% or 200% scaling. Normal v0.5.1 at ad5d3ac is the verified delivery baseline and includes the 3ce1ed7 restart fix. Its bounded current-host update/launch/setup proof does not establish fresh-install or full installer/update lifecycle acceptance. The documentation persistence checks do not expand native-language or scale evidence. All 109 neutral identifiers in the inventory remain unverified for complete acceptance.
