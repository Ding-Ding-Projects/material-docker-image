# Review of rendered frames

Frames reviewed: the 54 captures of the built application in `evidence/screenshots/` (workflow run 41698541353, source `afc72c0df9be118fdb6d07c238a4ff3b458edc0d`), compared with the artboards in `project/`. The hosted canvas could not be rendered to frames in the design session, so the review used the built screens, which are the frames that matter for a native application.

## Reference

- Stageshow pinned copy at commit `802057409787133703c56001080169e347305572`. References read: `SKILL.md`, `references/craft.md`. Gold examples are marketing compositions; none was copied because these artboards are real application screens. Rules applied: restraint (real words where the eye lands), spacing and alignment, one icon family at one size per context, five inks plus one accent, status colour meaning status, chrome for the task, no invented marks. Not applicable: cursor choreography, loops, framing and crop rules for marketing heroes, bento layouts, device frames.

## Findings and fixes

1. Captures were stretched by the runner's 1024 x 768 screen. Fixed by a 1920 x 1080 runner desktop and capture mode that lays content out at the requested size.
2. Dialogs opened at a 320 px maximum width when the overlay had not been measured yet. Fixed by sizing against the window.
3. At 960 x 720 the Images and Containers inspectors squeezed the table. Fixed: inspectors move under the list below 1100 px.
4. Table filter chips wrapped into a narrow column beside the search. Fixed: chips wrap under the search field.
5. Settings overlapped the last rail destinations on short windows. Fixed: the rail scrolls and shows icons only below 900 px height.
6. The output panel took about a fifth of every screen while idle. Fixed: it stays closed until an operation starts and opens by itself.
7. Inspectors in captures were empty because nothing was selected. Fixed for captures by selecting the newest real record.

## Differences from the artboards, accepted

- Example content in the artboards (project names, registry accounts, sample charts) is replaced by real engine data or truthful empty states.
- The Build editor has line numbers but no syntax colouring.
- Monitor charts show CPU and memory for the focused container; network and block I/O are on the container cards.
- Registry offers no "Verified publisher" filter because the CLI search has none, and builders are never made the default.

## Open

- Interactive review on a personal machine with Docker Desktop and Linux containers.
- Light theme and minimum-size frames were reviewed by contact sheet only.
