# WP-012 — Canonical product taxonomy and Discovery boundary

## Metadata

- **Status:** Planned
- **Priority:** Critical
- **Phase:** E — Architectural truth and product boundary
- **Depends on:** —
- **Review gate:** xhigh (public product contract, open-core boundary)

## Context

The repository currently has more than one incompatible description of the KynticAI product line.

The root README uses the intended product hierarchy:

- **Scout — Explore**
- **Fortress — Prove**
- **Elite — Scale**

However, `docs/source-of-truth-naming-map.md` declares itself canonical while still describing older concepts such as private runtime, assisted private tier, and Scout Cloud as if they were the product hierarchy. Several architecture and positioning documents repeat that older model.

The Discovery boundary has also drifted. `AGENTS.md` correctly distinguishes:

- the generic local codebase Discovery Agent;
- the public metadata-only Scout Discovery MCP;
- the commercial KynticAI Discovery MCP buyer workflow, Discovery Signature and private handoff logic, which belong outside Scout.

Some canonical/public docs still describe the commercial Discovery MCP as a Scout/public capability.

This is dangerous because future contributors and coding agents can faithfully follow the wrong "source of truth" and reintroduce product/boundary mistakes.

## Objective

Create one unambiguous public product model and make every public Scout document conform to it:

**KynticAI → Scout / Fortress / Elite**

Treat Cloud/control-plane services as optional architecture/commercial infrastructure rather than the third product. Reconcile the Discovery boundary so no public document directs commercial Discovery implementation back into Scout.

Do not change runtime behaviour in this package.

## Required decisions

1. `KynticAI Scout` is the open-source **Explore** product.
2. `KynticAI Fortress` is the private **Prove** product/runtime.
3. `KynticAI Elite` is the **Scale** offering.
4. "Cloud", "control plane" and hosted commercial services are components/services, not a replacement product name for Elite.
5. The generic Discovery Agent and metadata-only Scout Discovery MCP remain public.
6. Discovery Signature generation, buyer/readiness orchestration and private customer handoff remain private and must not be described as Scout features.

## Scope

At minimum inspect and reconcile:

- `README.md`
- `AGENTS.md`
- `docs/source-of-truth-naming-map.md`
- `docs/product-positioning.md`
- `docs/open-core-boundary.md`
- `docs/control-plane-data-plane.md`
- `docs/cloud-commercial-control.md`
- `docs/roadmap.md`
- `docs/commercial-readiness-summary.md`
- `docs/discovery-agent-mcp.md`
- docs-site product/architecture pages
- public marketing copy in `apps/web`

## Tasks

1. Rewrite the naming map so it is genuinely canonical and starts with Scout/Fortress/Elite.
2. Define Cloud/control-plane terminology separately from product-tier terminology.
3. Remove old "private runtime / assisted private tier" product hierarchy language unless retained only as explicit backwards-compatibility implementation terminology.
4. Reconcile every public document against the canonical map.
5. Reconcile Discovery wording against `AGENTS.md`; remove any public claim that Scout ships Discovery Signature generation or the commercial buyer journey.
6. Add a lightweight automated/public-safety consistency check for the most dangerous obsolete phrases if practical without creating brittle prose tests.
7. Update changelog/unreleased notes.
8. Do a final repo-wide search for contradictory tier names and Discovery ownership.

## Acceptance criteria

- [ ] One document is the canonical naming source and it matches Scout → Fortress → Elite.
- [ ] No current public document presents Cloud as the third KynticAI product.
- [ ] Cloud/control-plane architecture remains documented as optional infrastructure.
- [ ] No current public document places commercial Discovery Signature/buyer-handoff implementation in Scout.
- [ ] README, docs site, roadmap, positioning and open-core boundary agree.
- [ ] Historical release notes are preserved as history unless factually wrong; current docs do not inherit obsolete terminology.
- [ ] No private implementation details are added while fixing names.

## Verification

Run public-safe text/reference checks and docs builds. For final executable sign-off, follow `LOCAL_VALIDATION.md`; when workstation constraints prevent complete validation, use the disposable GCP exact-SHA gate in `docs/testing/gcp-precloud-validation.md`.

Report every intentionally retained legacy term and why it cannot be removed.
