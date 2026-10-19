# Feature checklist continuation

## Continuation on 2026-10-18

The saved checklist candidate is `ca1a09752b262288422236db22375baa83db5b05`, derived from `8f24b80d1bc81b4fd1696ea8c5cdaae52a9882b5`. Continue its bounded review, browser verification, default-branch integration and deployment. The productive application has no source changes in this continuation.

Independent review found that optional source-reference metadata could render unescaped HTML. A separate in-memory reproduction confirmed the path; the saved inventory contains no injected content. The repair rejects verification metadata on source references, renders bindings only for verification evidence, and escapes all binding text. The new regression was observed failing before repair. The exact `build.bat -Tests` entrypoint now passes 23 core, 51 UI-state and 13 checklist checks, including 123 omission cases. Independent review accepted the repair and confirmed unchanged rendered output for the saved inventory.

The repair is being preserved on `ai/fix/20261018-checklist-evidence`. Hidden-browser verification of the original candidate is running separately with its original source binding. Runtime, release and deployment evidence must be recorded independently before completion. Existing preparation progress, historical native evidence, other tasks and retained browser profiles are preserved.

Next: finish and inspect the browser receipts, preserve publishable evidence, merge the reviewed candidate and repair into main, verify deployment and normal release, apply the matching existing-installation update safely, and record final source/remote proof. No complete-product acceptance is claimed.

Continue the approved documentation feature-completion checklist in the public Ding-Ding-Projects/material-docker-image repository. The productive application remains native Windows WPF; this task changes only documentation, its renderer and focused local verification.

## Candidate

Branch `codex/feature-checklist` started at `8f24b80d1bc81b4fd1696ea8c5cdaae52a9882b5`. The source-controlled inventory retains all 109 required IDs, 10 product workflows and four milestones. All required entries remain unverified. Existing workflows and the checklist are implemented with verification pending; health-check configuration, device mappings and GPU access remain planned. No entry claims full verification.

The generated home page has category/status filters, native expandable details, English/Cantonese/bilingual copy, shared light/dark styling, reduced-motion inheritance and narrow-layout rules. Visitors cannot change completion status. The independent preparation checklist keeps its original `mdi-guide-v1` state and four checkbox identifiers.

One JSON inventory generates the visible checklist, capability summary and manifest counts. The original 109-ID baseline is separate validation data, not a second status source. The renderer embeds 13 complete Markdown articles and retains the original three content-addressed PNGs. Generation now has deterministic source hashes instead of a current-time field.

## Verification and remaining work

The exact `build.bat -Tests` entrypoint passed 23 core checks, 51 UI-state/parser checks and the focused checklist suite. The checklist suite covers all missing IDs independently, duplicate/status/language/evidence checks, public-only links, completion proof requirements, filter logic and actual renderer stale-output rejection. Public text passed `scripts/scan-public.mjs`; single-word context and pixels still require human review.

Independent review, actual hidden-browser keyboard and interaction checks, supported language/theme/narrow-layout coverage and deployed-route/hash verification remain pending. Controller tests are not browser or visual proof. No new screenshot, native behavior, release, installation update or complete product acceptance is claimed.

The integration owner coordinates preservation push, review, runtime evidence, default-branch integration and publication. This implementation lane does not merge, push or publish. Preserve all earlier native capture and v0.5.1 evidence at their original source bindings. Do not relabel older receipts as proof for this candidate. Update existing issue 1 and Discussion 2 through the integration owner. Projects access lacks read:project, and Status Hub enrollment is absent.
