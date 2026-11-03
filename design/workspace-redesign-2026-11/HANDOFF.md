# Workspace redesign (November 2026)

Status: design reference for the Material 3 rewrite of every window surface. This folder is a design record, not proof of implemented behaviour. Implemented behaviour and its evidence live in the source, tests and `evidence/`.

## What is here

- `project/canvas.json`: canvas index (18 artboards, frames, notes).
- `project/*.dc.html`: one self-contained artboard per screen at the normal window size, 1440 x 960.
  - `Main.dc.html`: Build workspace (configuration, Dockerfile editor, build output).
  - `Creator.dc.html`: interactive Dockerfile Creator (starters, stages, instruction cards, live preview, checks).
  - `Images.dc.html`, `RunContainer.dc.html`, `Containers.dc.html`, `Compose.dc.html`, `Canvas.dc.html`, `Storage.dc.html`, `Monitor.dc.html`, `System.dc.html`, `Registry.dc.html`, `Diagnostics.dc.html`, `Tools.dc.html`, `Settings.dc.html`, `Palette.dc.html`, `Setup.dc.html`.
  - `TitleBar.dc.html` and `Rail.dc.html`: shared components imported by every screen.
- `assets/`: the two subset Material Symbols Rounded fonts (outline and filled) the artboards use. In the hosted canvas they are uploaded assets referenced as `/_blob/d9af12d36fa91b053ce96cda69b424eb` (outline) and `/_blob/7fc89dce2cd820b081369e0fff242eae` (filled). The application ships the same files from `src/MaterialDockerImage/Assets/Fonts/`.

## Look

Material 3 dark with a blue seed; the light theme mirrors the same roles. Roles: surface `#111318`, surface containers `#191C20` / `#1D2024` / `#282A2F` / `#33353A`, on-surface `#E2E2E9`, on-surface-variant `#C4C6D0`, outline `#8E9099`, outline-variant `#44474F`, primary `#ADC6FF` on `#002E69`, primary container `#004494`, secondary container `#3E4759`, tertiary `#82D3E0`, success `#7FD99A`, warning `#F9BD5C`, error `#FFB4AB`. Shapes: 16 px cards, 20 px pills and buttons, 28 px dialogs, 8 px fields and chips. Type: Segoe UI Variable with Cascadia Mono for identifiers, code and logs. Icons: Material Symbols Rounded, one size per context.

## Structure every screen shares

- Frameless window with a custom title bar: identity with version and updated-at provenance, the command palette entry (`Ctrl+Shift+F`), the engine context selector with a reachability dot, notifications with an unread badge, accessibility tools, and working window controls.
- Navigation rail on the left with the settings destination pinned to the bottom.
- Workspace tab strip: the project workspace tab plus utility tabs opened from New tab.
- Every list has its own search with an adjacent regular expression builder, filter chips, multi-select with a bulk action bar, row context menus that show working shortcuts, and export.
- Destructive work uses the two-key super confirmation shown on `System.dc.html`.
- A collapsible operation panel shows the real progress and output of the running operation.

## Design process notes

- Frames use the fixed normal window size because these are native window screens, not responsive web pages. The minimum window size (960 x 720) is checked on the built application, not in the artboards.
- The Stageshow skill was followed from the pinned copy at commit `802057409787133703c56001080169e347305572` because a fresh install was not permitted in the design session. Its restraint, spacing, icon, colour and review rules were applied. Its animation, cursor choreography, marketing framing and bento rules do not apply to real application screens and were not used.
- Content in the artboards is illustrative (example project names, image tags and registry accounts). The application itself shows only real engine data and truthful empty states.
- The review of rendered frames is recorded in `REHEARSAL.md`.

## Advanced v2 boards

The second pass added boards for the deeper features, laid out below the original rows:

- `BuildV2.dc.html`: BuildKit step timeline with cache hits, durations, digest and result card.
- `Bake.dc.html`: groups, targets with platforms and the resolved target definition.
- `ImagesV2.dc.html` and `Security.dc.html`: image detail tabs (layers with size bars, platforms, Scout summary) and the security overview.
- `Contexts.dc.html`, `Swarm.dc.html` and `VolumeBackup.dc.html`: engine context cards and plugins, Swarm services, and the volume backup dialog.
- `CreatorV2.dc.html`: the Creator with mounts, heredocs and the editable source.
- `Appearance.dc.html`: the appearance studio (colour, motion, shape, density, type) with a live preview.
- `Minecraft.dc.html`: Minecraft server cards, console with RCON and server properties.

Every board uses the current Material Symbols subset (221 icons) so rail and page icons render.
