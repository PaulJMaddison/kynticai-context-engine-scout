# WP-015 — Extract sales next-action heuristics from Scout core

## Metadata

- **Status:** Complete
- **Priority:** High
- **Phase:** E — Architectural truth and product boundary
- **Depends on:** WP-014
- **Review gate:** xhigh (architecture, public contracts)

## Context

Scout contains useful sales/customer intelligence demonstrations, but some of that logic currently sits in the core application layer.

Examples include `NextActionIntelligenceService` and `BasicRelationshipEngine`, which reason over opportunities, sales activity, email engagement, support, usage, billing and web journeys and apply fixed fallback weights.

The implementation correctly states that these weights are not Fortress canonical weights. Even so, numeric values such as 0.88 or 0.74 look authoritative to downstream consumers despite being demo/business heuristics.

A generic context engine should provide relationships and evidence without silently defining a universal sales model.

## Objective

Move sales-specific next-action scoring, sales weights and recommendation construction into a clearly labelled reference use case while preserving generic relationship/provenance primitives in Scout core.

## Tasks

1. Identify which parts of `BasicRelationshipEngine` are truly generic relationship mechanics and which encode business-specific sales/retention opinions.
2. Move fixed sales/customer heuristic weights to the reference application/example created or clarified by WP-014.
3. Move `NextActionIntelligenceService` or split it so the Scout core contains only generic evidence/relationship packaging.
4. Ensure public API names clearly distinguish generic context from example next-action output.
5. Where a legacy public contract exposes the heuristic result, preserve compatibility through a deprecated reference endpoint/package or document a versioned change.
6. Make any retained numeric confidence/weight semantics explicit: measured confidence, deterministic rule strength and business heuristic must not be conflated.
7. Keep Fortress canonical-analysis ownership language public-safe and implementation-free.
8. Add tests showing generic Scout relationships do not depend on the sales/customer reference schema.

## Do not do

- Do not invent a new "universal" weighting model.
- Do not copy Fortress/private algorithms into Scout.
- Do not silently change numeric semantics on an existing v1 API.
- Do not remove useful reference behaviour without leaving a runnable example.

## Acceptance criteria

- [ ] Scout core does not hard-code sales/RevOps business weights as platform truth.
- [ ] Sales next-action remains available only as a clearly labelled reference/example if retained.
- [ ] Generic relationship/evidence contracts remain stable or are versioned deliberately.
- [ ] Documentation stops implying demo heuristic scores are calibrated/canonical.
- [ ] Core tests have no dependency on CustomerOps sales entities.

## Verification

Run API/SDK compatibility tests plus focused relationship-contract tests and the full repository validation gate.
