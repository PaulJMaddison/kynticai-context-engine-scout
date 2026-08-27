# WP-023 — Clarify KynticAI Score as a companion product, not a Scout feature

## Metadata

- **Status:** Complete
- **Priority:** Medium
- **Phase:** G — Semantic and documentation truth
- **Depends on:** WP-012
- **Review gate:** standard (package/product boundary)

## Context

`docs/score-api.md` correctly says the KynticAI Score API is contract-only and Scout does not calculate scores. The canonical naming map also identifies Score as a separate KynticAI product.

The Scout roadmap nevertheless lists "Score API" as a shipped Scout capability, and the Scout TypeScript SDK exports a Score client.

That creates packaging ambiguity: a companion product contract is being presented as a Scout product feature.

## Objective

Make the Score relationship explicit and unsurprising.

Choose one of these coherent patterns:

1. keep the public contract/client in this monorepo but label it everywhere as a **companion KynticAI Score contract**; or
2. move the Score contract/client to its own public package/repository and retain only an integration example/link in Scout.

Prefer the smallest change that gives a clean product boundary.

## Tasks

1. Inventory Score references, schema, SDK exports, samples and docs.
2. Decide whether the contract belongs physically in Scout based on coupling and release/versioning needs.
3. If retained, move it under a clearly labelled companion/integrations namespace and stop listing it as Scout core capability.
4. If extracted, preserve compatibility for existing imports with a deprecation window or documented migration.
5. Ensure Scout never claims to calculate Score results.
6. Update roadmap, README, docs-site and SDK docs.

## Acceptance criteria

- [ ] A new user can tell immediately that KynticAI Score is separate from Scout.
- [ ] Scout core capability lists do not imply a scoring engine ships here.
- [ ] Existing consumers have a documented compatibility path.
- [ ] Score contract semantics remain unchanged unless separately reviewed.

## Verification

TypeScript build/tests, contract schema tests, docs/reference checks and any compatibility test needed for moved exports.
