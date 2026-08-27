# Scout Engineering History Notice

This file used to contain mutable "current branch truth" from an August 2026 engineering session.

That pattern is deliberately retired because a checked-in session note becomes stale and can mislead contributors and coding agents.

## Current sources of truth

Use these instead:

- product names and boundaries: `docs/source-of-truth-naming-map.md`;
- durable repository rules: `AGENTS.md`;
- implementation backlog: `docs/work-packages/README.md` and GitHub issues;
- shipped history: `CHANGELOG.md` and dated release notes;
- runtime behaviour: current code and executable tests.

## Historical continuity principles worth preserving

The earlier session established several durable Scout → Fortress rules that remain valid:

- retained exact source evidence is separate from parsed/materialised context;
- full-source coverage does not imply complete history;
- generation membership prevents deleted/absent rows being resurrected during replay;
- Scout → Fortress transfer uses a durable ownership barrier;
- connector credentials remain in the compatible customer-controlled data-plane path rather than being copied into a different engine merely because the product tier changes;
- upgrade exports are customer data and are not normal control-plane/support payloads;
- executable build/migration/provider proof is required before calling a release runtime-green.

The detailed historical branch/PR state that previously lived here is intentionally not maintained in the repository.
