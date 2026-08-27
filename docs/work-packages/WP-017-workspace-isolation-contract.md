# WP-017 — Make workspace isolation truthful and enforceable

## Metadata

- **Status:** Complete — tenant boundary; workspace remains organisational only
- **Priority:** Critical before multi-workspace production use
- **Phase:** F — Production correctness
- **Depends on:** WP-016
- **Review gate:** xhigh (authorisation, tenancy, persistence)

## Context

Public architecture describes Workspace as the next isolation layer inside a Tenant. Current implementation and docs also acknowledge that major context primitives remain tenant-scoped and `SaaS.RequireWorkspaceScope` is false by default.

A workspace can be a UI grouping or a real security/data-isolation boundary. It cannot safely be described as both.

## Objective

Choose and implement one explicit workspace contract.

Preferred direction: if Scout supports multiple workspaces inside one tenant for real customers, workspace ownership and authorisation must reach every relevant data path. If that work is intentionally deferred, current public docs and APIs must say Workspace is organisational metadata rather than a security boundary.

## Tasks

1. Build a table of every tenant-scoped entity/API and whether workspace ownership is required:
   - data sources/installations
   - selector definitions/executions
   - semantic attributes
   - source events/capture state
   - context facts/snapshots/packages
   - API clients
   - audit and usage
   - onboarding/blueprints
2. Decide the supported isolation semantics and document the threat model.
3. If workspace isolation is supported, add additive schema changes and workspace IDs/foreign keys where required.
4. Enforce workspace membership/scope in REST, GraphQL, service queries and machine-client access.
5. Fail closed when a workspace-scoped actor omits or crosses workspace scope.
6. Define migration behaviour for existing tenant-only rows.
7. Add cross-workspace negative tests analogous to existing cross-tenant tests.
8. Ensure background jobs, webhook ingestion and recovery preserve workspace identity.
9. Update `SaaS.RequireWorkspaceScope` semantics or replace it with a clearer contract.

## Do not do

- Do not claim isolation merely because a Workspace table exists.
- Do not rely only on UI filtering.
- Do not weaken tenant isolation.
- Do not make destructive migrations without explicit migration/rollback proof.

## Acceptance criteria

Either:

**A. Real workspace boundary**
- [ ] all relevant data carries/enforces workspace scope;
- [ ] cross-workspace REST/GraphQL/background/event access is tested and denied;

or:

**B. Deliberately non-security workspace**
- [ ] public docs/API naming no longer describe Workspace as an isolation boundary;
- [ ] deployments needing hard separation use separate tenants.

Whichever option is chosen must be explicit and tested.

## Verification

Use local PostgreSQL migration proof and multi-actor/multi-workspace integration tests where local PostgreSQL is available. Otherwise record the provider-specific proof as blocked rather than using cloud infrastructure.
