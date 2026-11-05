# Closeout prompt

```text
Continue the Material Docker Image project (Ding-Ding-Projects/material-docker-image), a native Windows WPF Docker workspace on .NET 10.

State on main (5 November 2026, after the design parity pass):
- Design parity: the boards in design/workspace-redesign-2026-11/ were rendered and compared with captures of the built application; shared controls and every destination's structure now follow them. The navigation rail keeps a label under every icon (deliberately overriding the icon-only boards) and sizes its items to the window height.
- Language: Cantonese covers the wording added by the parity pass and the status chip words; engine values such as network drivers stay literal.
- Personal wording: a dropped file with "version": 1 (the shared file's shape) or "schemaVersion": 1 is accepted; conflicting values are refused.
- Layout: a 264-surface probe matrix (three languages at 1440, 1200 and 960 px, light theme, 200 % Cantonese) found four overflows after the parity pass; all were fixed, along with cuts seen in the captures (inspector tab padding, the Build editor title, Images rows at 960 x 720).
- Checks: build.bat -Tests compiles the application and the build companion and passes 23 core, 2,735 UI-state and 24 checklist checks.
- Interactive receipt: evidence/interactive-2026-11/receipt.json (UI Automation on a hidden desktop). Items the maintainer declined are listed there as declinedByMaintainer.

Next:
1. Drive the later-board views with real input in a built window (minimap, regex builder panes, Creator steps, image detail actions) and record receipts.
2. Regex parity: 5 topics partial (flavor comparison, grep conversion, folder search extras, external tool integration, some localization); 8 are impossible on .NET.
3. Try the https and Home Assistant schedule sources against real servers; scan the lock QR code with a phone authenticator.
4. Consider adding --capture-probe to the CI screenshot run so overflow is caught on every change.
5. Keep HANDOFF.md, ROADMAP.md, docs/features and evidence/uh-features-audit.md current and regenerate the site with node website/render.mjs.
```
