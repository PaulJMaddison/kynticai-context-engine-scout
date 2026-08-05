# KynticAI Scout — Work Package Backlog

This directory is the delivery backlog for KynticAI Scout, derived from a
full audit of the public repository (docs, code, tests, CI/CD, deployment,
and brand compliance) performed on 2026-08-04.

Each work package is written as a standalone, executable prompt: it can be
handed to an engineer or an agent as-is, contains its own context, scope,
tasks, acceptance criteria, and verification commands, and respects the
`AGENTS.md` do-not-do list and the `LOCAL_VALIDATION.md` safe default.

## How to use this backlog

1. Work is organised into three phases (A = public safety, B = product
   hardening, C = delivery engineering, D = documentation completeness).
   Complete all of Phase A before anything else is shipped.
2. Packages are independent unless a dependency is listed in their metadata.
3. Each package must leave the repo passing the `LOCAL_VALIDATION.md` safe
   default (`dotnet restore/build/test`, `npm run lint/test/build` in
   `apps\web`, `npm run test` in `packages/typescript/scout-sdk`).
4. Update the status line in the package file when the package is started,
   blocked, or complete, and log outcomes in the session log.
5. Work that touches public API, SDK shape, connector contracts, data models,
   or security must use xhigh review gates before being marked complete.

## Package index

| ID | Package | Phase | Priority | Depends on | Status |
|---|---|---|---|---|---|
| WP-001 | Public boundary remediation (remove proprietary leaks) | A | High | — | Complete |
| WP-002 | Docs reference integrity and naming source of truth | A | High | — | Complete |
| WP-003 | Docs-site reconciliation and publishing alignment | A | High | — | Complete |
| WP-004 | Cross-tenant authorisation hardening | B | High | — | Complete |
| WP-005 | API surface hardening | B | Medium | — | Complete |
| WP-006 | SDK compatibility fix (Fact value type) | B | Medium | — | Complete |
| WP-007 | CI/CD re-enablement (OSS-013) | C | High | WP-001 | Complete |
| WP-008 | Browser proof and frontend polish | C | Medium | — | Complete |
| WP-009 | Roadmap and changelog reconciliation | D | Medium | WP-007 | Backlog |
| WP-010 | Missing user documentation | D | Medium | — | Backlog |
| WP-011 | Connector authoring and marketplace documentation (OSS-019) | D | Medium | — | Backlog |

## Phase summary

### Phase A — Public safety and boundary (do first)

These packages remove proprietary material, fix broken public references,
and reconcile contradictory public instructions. Nothing should be published,
tagged, or presented as deliverable until Phase A is clean.

- **WP-001** removes the private engine codename, private analysis-module
  descriptions, and the vector-capable database image from public docs and
  code.
- **WP-002** creates the missing `source-of-truth-naming-map.md`, fixes every
  broken reference to it, strips machine paths from public files, and fixes
  US/British English slips.
- **WP-003** reconciles the two doc trees (ports, install path, SDK publishing
  claims), cross-links the docs site from the main docs, and removes internal
  wording from a public page.

### Phase B — Product hardening

- **WP-004** closes a cross-tenant authorisation gap in admin REST endpoints.
- **WP-005** hardens the API surface (operation IDs, export validation, ops
  summary, scope documentation).
- **WP-006** fixes the one known SDK/API contract mismatch and removes the
  test workaround that hides it.

### Phase C — Delivery engineering

- **WP-007** re-enables CI/CD (OSS-013) and reconciles it with the
  pilot-readiness gate.
- **WP-008** completes browser proof and applies frontend copy polish.

### Phase D — Documentation completeness

- **WP-009** refreshes the roadmap and reconciles the two changelogs.
- **WP-010** writes the missing user docs (MigrationTool, pilot lead capture,
  OpenAPI export).
- **WP-011** completes the connector authoring and marketplace docs (OSS-019).

## Audit evidence

The findings behind these packages came from a full read-only audit of:

- `README.md`, `docs/**`, `docs-site/**` (80+ docs, 20-site Starlight site)
- `src/**` (API, Application, Domain, Infrastructure, SDK)
- `apps/web`, `apps/discovery-agent`, `packages/typescript/*`
- `tests/**` (207 tests across 4 projects), `scripts/**`, `.github/**`
- `deploy/**`, `tools/**`, `samples/**`

Highlighted findings that motivated each package are quoted in the package
files themselves with file:line references.
