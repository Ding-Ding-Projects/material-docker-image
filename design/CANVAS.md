# Interactive canvas specification

Status: proposed behavior, not runtime proof. Implement with a real native WPF Canvas or equivalent native scene control. Both canvases manipulate/select meaningful objects rather than presenting decorative diagrams.

## Shared interaction

Provide zoom in/out, fit, reset view, pan, selection, keyboard traversal and a synchronized accessible object list. Zoom about the pointer or focused object, keep text readable and expose zoom numerically. Space-drag or middle-drag pans; keyboard controls provide an equivalent. Escape cancels manipulation. A click selects and opens details; double-click focuses the corresponding source span or record. Selection has visible focus independent of color. Maintain viewport state separately per workspace and graph.

Represent nodes by stable internal identity, not display label. Edge selection explains its relationship. Layout is deterministic for the same data. Preserve manually arranged positions when refreshing surviving identities. Provide automatic arrangement and a reset action. Large graphs use bounded rendering and an honest loading state. Do not animate constantly; reduced motion disables animated transitions.

The accessible list exposes every node, relationship, state and available action. Canvas-only pointer operation is incomplete. Search focuses a matching node and reports hidden matches. Context menus retain their actual keyboard shortcuts.

## Editable build-stage graph

Each FROM stage is a node with base image, alias or ordinal, relevant instruction count and validation status. Edges distinguish a stage used as a base from COPY --from relationships. External image sources appear as external nodes. A selected stage exposes its instructions and focuses the exact source range.

Provide Add stage, Rename stage, Change base, Add copy-from, Edit instruction and Select build target through validated forms. Persist layout independently from source semantics. Dragging nodes rearranges the visual layout only; it must not reorder Dockerfile instructions. Execution-order changes require an explicit validated source change.

Use a lossless source model retaining comments, whitespace, continuations and unsupported syntax. Only enable graph edits where the parser can map exact source spans and verify an unambiguous transformation. Apply a source patch with undo, reparse, and update the graph in one transaction. Renaming a stage updates only resolved references to that stage. Cycle checks must account for stage-base and copy dependencies. A failed edit changes neither representation.

Do not claim complete Dockerfile grammar support. Unknown directives, dynamic references, heredocs or unresolved variable expressions remain visible as source-only content when unsupported. Show a specific explanation and an Open in source action. Keep building from the original source available where Docker accepts it. Never regenerate the whole document from a reduced graph and discard unrepresented syntax.

Source edits debounce validation and preserve selection when identity survives. Invalid source freezes the last valid graph with a conspicuous stale indicator or shows a partial graph explicitly labeled partial. Neither view represents current valid configuration until parsing succeeds. Unsaved canvas and source edits share one draft and undo history.

During builds, annotate nodes only with events reliably mapped to those stages. Unmapped output remains in the live operation log. Never invent elapsed times, percentages or cache hits.

## Observed runtime topology

Display containers, networks and volumes as different labeled shapes with textual type identifiers. Edges represent inspected network membership and mount relationships. Group Compose services by project when real labels establish that ownership. Ports appear as selected-node details, not guessed connectivity edges. External traffic is not inferred from published-port configuration.

Container nodes show observed lifecycle and health; networks show driver and endpoints; volumes show attachment mode and destination. Every snapshot carries observation time and selected engine identity. Disconnection retains the last snapshot marked stale; removed objects reconcile without preserving actionable ghost selections.

Dragging runtime nodes changes layout only. Connect/disconnect and attach/run actions are explicit forms identifying both endpoints, actual engine, consequences and final result. Creating an edge by gesture may open that form, never mutate immediately. Do not move running containers between engines or infer authorization from the diagram.

## Required checks

Verify pointer and keyboard pan/zoom/select/fit; accessible-list parity; stable identity after refresh; deterministic layout; long bilingual labels; all scales/themes; node overlap and offscreen recovery; empty and large graphs; stale snapshots; unsupported syntax retention; source-to-graph and graph-to-source edits; undo; stage rename references; invalid/cyclic edits; cancellation; engine disconnection; and rejection of destructive gesture-only mutations.

Capture actual built canvases with deterministic fixtures. Synthetic reference diagrams are design data, not evidence of engine operation.
