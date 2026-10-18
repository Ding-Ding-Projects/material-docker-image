# Screen and state inventory

Each row requires implementation mapping, reference selector, runtime selector, localization, keyboard behavior, focused tests and actual capture evidence. No row is complete merely because it is listed here.

| Screen ID | Required states |
| --- | --- |
| onboarding | Runtime absent; disconnected; connected; optional registry skipped; resumed; complete |
| workspace | Empty; restored; multiple repositories; unavailable folder; disconnected context |
| build-editor | Empty; valid; invalid; missing context; unsaved; unsupported platform; protected input |
| build-canvas | Empty; one stage; multiple dependencies; selected stage; unsupported source; stale graph; edited; undo; invalid cycle |
| build-operation | Queued; active; cache hit; success; failure; cancellation pending; canceled; connection lost |
| image-list | Loading; empty; populated; filtered; multi-selection; stale selection |
| image-detail | Tagged; untagged; used; unavailable; unsupported field |
| run-editor | Valid; port conflict; invalid mount; advanced privilege review; pending; failed |
| container-detail | Created; running; unhealthy; paused; exited; restarting; removed externally |
| runtime-canvas | Empty; connected topology; selected node/edge; grouped Compose; stale; removed object; large graph |
| compose | Empty; valid; invalid; resolved; partial apply; active services; canceled; unavailable implementation |
| volumes | Empty; attached; unused; invalid create; deletion blocked; partial deletion |
| networks | Empty; connected; invalid create; disconnect pending; stale endpoint |
| diagnostics | Not run; running; passed; failed; unsupported; unknown; sanitized export review |
| settings | Default; customized; invalid import; valid personal wording; cleared wording; unavailable external settings |
| notifications | Empty; operations active; multiple results; recovery action |
| new-tab-catalog | Empty query; filtered; invalid regex; selected; canceled; keyboard return |

## Reference tuples

Normal client area: 1440 x 960 logical units. Proposed minimum: 960 x 720 logical units. Cover English, Cantonese and bilingual; light and dark; 100%, 125%, 150% and 200% display scale. Freeze or explicitly control time, animations, fixture data and layout seeds. Bind all runtime results to the actual source revision and binary hash.

## Review record

Independent review: pending. Built interaction: not run. Visual comparison: not run. Accessibility verification: not run. Engine-backed smoke tests: not run. These states must be updated only from actual evidence.
