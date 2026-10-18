# Documentation website design

The separate public documentation website is a task guide for the native Windows application. Its first screen shows honest development status and routes to Build, Run, Canvas, complete embedded articles and a wiki/reference section. It does not claim a release or verified runtime acceptance.

Use a restrained blue/slate visual language, strong reading hierarchy, task navigation and a two-column walkthrough with a visitor-owned checklist. At 320px the layout becomes one column. Local system fonts and local scripts/styles avoid remote dependencies. Light/dark themes, English/Cantonese/bilingual presentation, reduced motion, visible keyboard focus and labeled controls are required.

Walkthrough controls change the explanation; checklist progress persists only in visitor storage. A structured command example changes in response to safe bounded inputs and is explicitly never executed. Search filters article sections; its adjacent local regex editor supports a deliberately restricted linear subset, with an explicit RegexBuddy parity gap rather than a misleading completeness claim.

The dependency-free renderer embeds every Markdown article present under docs/features plus README, ROADMAP and HANDOFF as the available wiki/reference corpus. Internal article links resolve locally. Unavailable articles are identified visibly. Source links are optional supplements, never substitutes for embedded content. A content manifest records source hashes and generation time; generation time is not represented as build/release provenance.

Independent design review, browser interaction, responsive capture and deployed-route verification remain pending until separately evidenced. The initial website is not full shared-feature acceptance: complete regex parity, the complete universal feature inventory and registered Material component provenance remain explicit gaps.
