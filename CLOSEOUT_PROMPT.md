# Closeout prompt

```text
Continue the Material Docker Image project (Ding-Ding-Projects/material-docker-image), a native Windows WPF Docker workspace on .NET 10.

State on main (4 November 2026):
- Small-window polish from captures of the built application at 960 x 720: complete title bar search label, no idle progress bar, list-first stacked layouts that fold the detail into a one-line summary with Show details on short windows (Images, Containers, Security, Swarm services), compact statistic tiles, Prune dangling in the filter row. Design reference: design/polish-2026-11/ImagesCompact.dc.html.
- Capture mode: --capture-language English|Cantonese|Bilingual and --capture-scale 125|150|200 (same window size in device-independent pixels, rendered at the matching DPI). Capture mode never writes settings.json and never loads imported personal wording.
- Personal wording file can be dropped anywhere on the window (validation, three-second headline, tada or default beep; Settings remains the keyboard route).
- Cantonese and bilingual copy on every destination page (about 3,400 phrases and sentence templates); short templates only fill placeholders with single tokens.
- Release workflow: only a run from main whose commit is still the tip of main marks its release Latest; a run superseded by a newer main commit stops cleanly with a notice.
- Containers and Minecraft queue a refresh that arrives mid-load instead of dropping it.
- evidence/uh-features-audit.md: source audit of the shared feature requirements (27 delivered, 43 partial, 25 missing, 20 not applicable at 47aee49) with a prioritised backlog.
- Checks: build.bat -Tests passes 23 core, 694 UI-state and 13 checklist checks.

In progress on separate branches (not yet on main): a single-file build companion (ai/feature/20261104-build-companion), container health-check, device and GPU controls (ai/feature/20261104-container-controls), application icon, logo customization and display-name rename (ai/feature/20261104-app-identity).

Next:
1. Review, test and merge the three branches above, then capture the full language x theme x 100-200 % matrix with the capture options and review every surface.
2. Work through the backlog in evidence/uh-features-audit.md (regular expression parity, element locks, scheduled settings, action sound mapping, changelog and exports, documentation site download button and previews).
3. Remaining English in Cantonese mode: about 40 interpolated strings, Docker-composed messages, the Workspace tab prefix and "No operation is running.".
4. Interactive verification so far used UI Automation on a hidden desktop (Setup, Containers list); exercise build, run, logs, exec, compose, prune and history restore the same way and record receipts.
```
