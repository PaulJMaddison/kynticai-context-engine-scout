# KynticAI Scout — Work Package Backlog

This directory is the delivery backlog for KynticAI Scout.

The original backlog was derived from the 2026-08-04 public-repository audit.
It was expanded on 2026-08-27 after a second architecture/product-boundary
review of the current `main` branch covering runtime design, persistence,
deployment, public/private ownership, documentation truth, APIs and long-term
maintainability.

Each work package is a standalone implementation prompt. It contains its own
context, objective, scope, constraints, tasks, acceptance criteria and
verification expectations so it can be handed directly to an engineer or
coding agent.

## How to use this backlog

1. Work is organised into phases A-H. Earlier completed work remains recorded
   in A-D. The 2026-08-27 architecture programme starts at Phase E.
2. Follow package dependencies rather than attempting the whole programme in
   one change.
3. Preserve the public/private boundary in `AGENTS.md`. Do not move Fortress
   or Elite implementation details into Scout while cleaning boundaries.
4. Every package must leave the repository internally consistent and include
   focused behavioural tests where runtime behaviour changes.
5. Public API, SDK, connector-contract, data-model, security, persistence,
   concurrency and migration changes require xhigh review before completion.
6. Follow `LOCAL_VALIDATION.md` for executable proof. When workstation
   constraints prevent complete validation, use the disposable GCP exact-SHA
   gate in `docs/testing/gcp-precloud-validation.md`.
7. Update the package status only after its acceptance criteria are actually
   met. Do not mark a package complete from static review alone when it
   requires PostgreSQL, deployment, concurrency or browser proof.

## Package index

| ID | Package | Phase | Priority | Depends on | Status |
|---|---|---|---|---|---|
| WP-001 | Public boundary remediation (remove proprietary leaks) | A | High | — | Complete |
| WP-002 | Docs reference integrity and naming source of truth | A | High | — | Complete |
| WP-003 | Docs-site reconciliation and publishing alignment | A | High | — | Complete |
| WP-004 | Cross-tenant authorisation hardening | B | High | — | Complete |
| WP-005 | API surface hardening | B | Medium | — | Complete |
| WP-006 | SDK compatibility fix (Fact value type) | B | Medium | — | Complete |
| WP-007 | CI/CD reference hardening (intentionally disabled) | C | High | WP-001 | Complete |
| WP-008 | Browser proof and frontend polish | C | Medium | — | Complete |
| WP-009 | Roadmap and changelog reconciliation | D | Medium | WP-007 | Complete |
| WP-010 | Missing user documentation | D | Medium | — | Complete |
| WP-011 | Connector authoring and marketplace documentation | D | Medium | — | Complete |
| WP-012 | Canonical product taxonomy and Discovery boundary | E | Critical | — | Planned |
| WP-013 | Make the Scout inference boundary real | E | Critical | WP-012 | Planned |
| WP-014 | Decouple CustomerOps demo data from production Scout | E | Critical | WP-012 | Planned |
| WP-015 | Extract sales next-action heuristics from Scout core | E | High | WP-014 | Planned |
| WP-016 | Separate runtime mode from control-plane features | E | High | WP-012 | Planned |
| WP-017 | Make workspace isolation truthful and enforceable | F | Critical | WP-016 | Planned |
| WP-018 | Make hosted Data Protection persistence real | F | Critical | WP-016 | Planned |
| WP-019 | Atomic cross-instance source-event idempotency | F | Critical | — | Planned |
| WP-020 | Correct production backup/source responsibility | F | High | WP-014 | Planned |
| WP-021 | Separate source truth from derived Scout state | G | High | WP-014, WP-015 | Planned |
| WP-022 | Remove stale repository state and correct release truth | G | High | WP-012 | Planned |
| WP-023 | Clarify KynticAI Score as a companion product | G | Medium | WP-012 | Planned |
| WP-024 | Make core, examples, tools and integrations obvious | H | Medium | WP-013, WP-014, WP-015, WP-023 | Planned |
| WP-025 | Rationalise REST, GraphQL and SDK surfaces | H | Medium | WP-013, WP-014, WP-017 | Planned |
| WP-026 | Activate continuous integration when GitHub permits it | H | High | WP-022 + external unblock | Blocked |

## Phase summary

### Phase A — Public safety and boundary

Completed remediation of proprietary/public-boundary leaks and broken public
references.

### Phase B — Product hardening

Completed cross-tenant, API and SDK hardening from the earlier audit.

### Phase C — Delivery engineering

Completed browser proof and hardened reference CI definitions. Live GitHub
Actions remain intentionally disabled until the external account restriction
is resolved.

### Phase D — Documentation completeness

Completed the earlier documentation, roadmap and connector-authoring backlog.

### Phase E — Architectural truth and product boundary

Do this first in the new programme.

- **WP-012** establishes Scout → Fortress → Elite as the canonical product
  model, makes Cloud a component rather than the third product, and fixes the
  Discovery ownership boundary.
- **WP-013** makes "Scout does not call an AI model" true in runtime code by
  moving inference into a reference consumer.
- **WP-014** removes the fictional CustomerOps database as a mandatory
  production dependency.
- **WP-015** moves sales-specific next-action heuristics and fixed weights out
  of the generic core.
- **WP-016** separates deployment/runtime mode from optional commercial and
  control-plane feature flags.

### Phase F — Production correctness

- **WP-017** either implements real workspace isolation end-to-end or stops
  presenting Workspace as a security boundary.
- **WP-018** proves Data Protection keys really persist across hosted
  restart/redeploy, not merely that a path string is configured.
- **WP-019** fixes issue #40 at the PostgreSQL/application boundary with real
  cross-instance idempotency.
- **WP-020** corrects backup/restore responsibility so Scout owns Scout state,
  not the customer's upstream enterprise systems.

### Phase G — Semantic and documentation truth

- **WP-021** separates retained source evidence from recalculable/materialised
  Scout context in terminology and upgrade contracts.
- **WP-022** removes stale "current branch truth", sprint-status duplication
  and incorrect release claims.
- **WP-023** makes KynticAI Score clearly a separate companion product rather
  than a Scout scoring-engine capability.

### Phase H — Maintainability and developer experience

- **WP-024** makes the monorepo's core, reference applications, integrations
  and tooling boundaries obvious.
- **WP-025** chooses a deliberate long-term API/SDK compatibility strategy,
  with versioned REST as the recommended canonical machine contract.
- **WP-026** activates safe automatic CI only after GitHub can actually run it
  and a real green workflow is observed.

## Recommended execution order

The safest sequence is:

```text
WP-012
  ├─> WP-013
  ├─> WP-014 -> WP-015 -> WP-020 -> WP-021
  ├─> WP-016 -> WP-017
  │           └──────────────┐
  ├─> WP-018                │
  ├─> WP-022                │
  └─> WP-023                │
                             v
WP-019 (can run independently) -> WP-024 -> WP-025 -> WP-026
```

WP-019 should be prioritised independently before horizontally scaled
production event/webhook ingestion.

## Audit evidence

The 2026-08-27 review covered:

- repository topology and current `main`
- root contributor/agent/session state
- product positioning, naming and open-core boundary documents
- production configuration and readiness validation
- Docker/Render deployment definitions
- Scout and CustomerOps persistence
- connector/capture and Scout→Fortress continuity design
- background jobs and distributed leases
- model/agent execution paths
- next-action/relationship heuristics
- REST, GraphQL and SDK surfaces
- current work-package/release/roadmap state
- open source-event idempotency issue #40

The new packages deliberately preserve areas that reviewed well: exact payload
evidence, generation membership, durable Scout→Fortress ownership transfer,
conservative capture claims, fail-closed machine scopes, connector redirect
hardening, bounded/recoverable background work and cross-instance lease design.
