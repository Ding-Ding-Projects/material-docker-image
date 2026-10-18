# Material Docker Image design handoff

Status: design proposal for independent implementation review. This document is not proof of implemented behavior, runtime testing, accessibility, or visual conformance.

Target: native Windows WPF on .NET 10. The coordinating task confirmed SDK 10.0.401 on the development host. No browser wrapper is proposed for the productive application.

## Productive workspace

Start on the last workspace or an honest empty state offering Open folder, Create build project, and Connect engine. Use a custom title bar with functioning native window behaviors, visible version and provenance-based updated-at time, active engine/context, command search, notifications and accessibility access.

The left workspace tab strip identifies each repository once by stable identity. Selecting an already-open folder focuses its existing workspace. Ordinary menu navigation never creates workspace tabs. A visible New tab control opens the complete searchable destination catalog; explicit selection creates utility tabs. Support tab docking, pinning, groups, overflow and keyboard discovery without losing drafts.

Within the workspace, provide Build, Images, Containers, Compose, Storage and networks, Diagnostics, History and Settings destinations. Keep the contextual primary action beside the selected content. A resizable inspector shows selected-object details; a collapsible operation panel shows genuine progress and returns users to the initiating surface. At narrow sizes collapse the inspector first and simplify navigation before reducing editor space.

## Build workflow

Use context and configuration controls beside an editable Dockerfile. Offer a source/canvas split and a focused source-only view. The build form includes context path, Dockerfile path, engine, builder, platform, target stage, tags, build arguments, protected secret sources, labels, cache and output controls. Resolve capability choices from the real engine. Show validation beside the relevant field. Preserve unsupported Dockerfile syntax exactly.

Build performs the actual requested operation. Build and run first obtains the immutable image result and then opens or applies the explicit run configuration against that image. Never resolve a mutable tag again silently. Results show actual stage events, output, elapsed time and process exit result. Unknown progress remains indeterminate. Cancellation distinguishes local process termination from confirmed engine-side cancellation.

Provide per-control optional information buttons, target-specific explanations and isolated synthetic examples. Examples must never execute the engine or copy live protected values.

## Images and containers

Images use a sortable inventory with tags, immutable ID, platform, reported size, date and container relationships. Actions include pull, inspect, tag, run, import/export, publish and delete. Registry actions identify the real account and destination and keep credentials out of displayed commands and logs.

The run editor has structured sections for identity/command, ports, environment, mounts, networks, resources, restart/health and advanced security. Port rows include bind address, host port, container port and protocol. Mount rows distinguish bind paths and named volumes, show the engine host context and make write access prominent. Never preselect privileged execution or broad host exposure.

Container detail provides Overview, Logs, Processes, Resources, Files, Configuration and History. Lifecycle actions reflect observed state and remain pending until reconciliation. Logs offer bounded retention, pause/resume following, stream labels, timestamps, search and export. Interactive execution is an explicit workflow, never a side effect of opening details.

## Compose and resources

Compose pairs source editing with resolved service details and topology. Preserve fields that a structured editor cannot represent. Identify project, file set, profiles, environment source and engine. Validate resolved configuration, review changes, apply and display partial outcomes per service. Stopping services is separate from removing containers and persistent volumes.

Volumes show attachment relationships, driver and available usage. Networks show driver, scope, IPAM and endpoint relationships. Destructive workflows review exact candidates and distinguish unused images, stopped containers, cache, networks and volumes. Never combine data deletion under an ambiguous cleanup action.

## Diagnostics and operation model

Diagnostics remain useful offline: executable discovery, versions, connection, API compatibility, builder/platform support, Compose availability, resources, registry access, project validation and sanitized operation evidence. Separate read-only checks from opt-in smoke tests. Tests identify and label every created resource and report teardown separately.

Use idle, validating, queued, running, succeeded, failed, canceled and outcome-unknown states. Disconnects and interrupted observation cannot become success or an automatic duplicate retry. Reconcile first. Protect against re-entry in the service and control. Export diagnostics only after sanitization and review; exclude credentials, personal wording and protected environment values before persistence.

## Accessibility and presentation

Use registered Material Design 3 primitives with documented provenance; styling stock controls alone is not conformance evidence. Keep keyboard focus, accessible names and status descriptions, reduced motion, high contrast, scalable text, narrator opt-in and persistent accessibility access. Provide English, Cantonese and bilingual resources. Separate code and exact Docker identifiers from translated presentation. Long labels wrap or move into an accessible inspector instead of clipping.

Every required shared feature needs its own implementation and evidence inventory row, including search/regex tools, local personal wording, guided controls, settings, history, exports and destructive confirmation. This handoff does not waive any absent shared feature.

Proposed logical client sizes are 1440 x 960 normal and 960 x 720 minimum, subject to built verification at 100%, 125%, 150% and 200% scale, both themes and all three languages. Matching reference and product tuples are required for parity evidence.

See [canvas specification](CANVAS.md) and [state inventory](STATES.md). Independent review must precede implementation; record actual deviations and unverified behavior rather than claiming this proposal was executed.
