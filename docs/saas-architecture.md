# Scout Deployment, Tenant and Workspace Architecture

This file keeps its historical name for link compatibility. The current Scout architecture is simpler than the old "SaaS" wording implied.

## Deployment modes

Scout separates **how the data plane runs** from **whether optional commercial services are enabled**.

Preferred current modes:

- `LocalDemo` — local evaluation with fictional/demo data.
- `SelfHosted` — customer-controlled production data plane.
- `ManagedDataPlane` — a production data plane operated as a managed deployment.

`BackendOnly` and `SaaS` remain compatibility values for older configuration. New production deployments should prefer `SelfHosted` or `ManagedDataPlane`.

Choosing a runtime mode does **not** automatically enable the optional KynticAI control plane, usage reporting or billing-related metadata. Those capabilities have their own explicit settings.

## Production data store

Production Scout requires one Scout PostgreSQL store for Scout-owned state:

- tenants and operators;
- connector configuration and protected credential references;
- selectors and mapping definitions;
- retained capture evidence and continuity state;
- materialised context facts and snapshots;
- source events and recompute jobs;
- audit and usage records;
- API clients and onboarding metadata.

The fictional `CustomerOpsDbContext` is reference/demo data. It is not a required production Scout database.

Real CRM, ERP, support, billing, warehouse and other operational data remains in the customer's source systems and reaches Scout through approved connectors or event ingestion.

## Tenants are the security boundary

A `Tenant` is the current hard data and authorisation boundary.

Production code must enforce tenant ownership at API, service and database-query layers. Where two teams require hard separation, use separate tenants.

## Workspaces are organisational groupings

`Workspace` and `WorkspaceMember` exist for grouping configuration, API clients, onboarding and related operational metadata inside a tenant.

**Workspaces are not currently a complete security/isolation boundary.**

Several core context records remain tenant-scoped. Therefore:

- do not promise cross-workspace data isolation;
- do not use workspaces to separate mutually untrusted groups;
- use separate tenants where hard isolation is required;
- `SaaS:RequireWorkspaceScope=true` is blocked by production readiness until end-to-end workspace isolation is implemented.

This is deliberate fail-closed behaviour rather than pretending the current schema provides a guarantee that it does not.

## Context

`ContextFact` and `ContextSnapshot` are **materialised Scout context**, not source-system master data.

Scout retains source evidence separately where capture is enabled. Derived context can be recalculated; retained evidence and generation membership are the basis for controlled Scout → Fortress continuity.

## Optional control plane

A separate optional control plane may manage commercial metadata such as licences, downloads, update channels, support access, deployment registration and approved aggregate usage.

It is independently enabled and is not required to run Scout.

It must not receive raw customer operational records, retained source evidence, context facts, relationship intelligence, prompts or generated customer content by default.

## APIs

The canonical machine API is the versioned REST surface under `/api/v1`.

GraphQL remains a supported secondary query surface.

The older `/api/rest` surface is retained for compatibility and should not receive new product capabilities.

Some historical endpoint/type names still contain `Saas` for compatibility. Treat them as legacy contract names, not the current product taxonomy.

## Reference/demo data

LocalDemo may opt into the fictional CustomerOps database and sales/customer reference scenarios.

That reference data exists to demonstrate Scout. It is not a hidden staging schema that production customers must populate.

## Product boundary

The product progression is:

**Scout — Explore → Fortress — Prove → Elite — Scale**

Cloud/control-plane services are supporting infrastructure rather than a replacement product name for Elite.

See:

- [Product and Naming Map](source-of-truth-naming-map.md)
- [Customer Data and Optional Control Plane](control-plane-data-plane.md)
- [Open-Core Boundary](open-core-boundary.md)
- [Production Install Checklist](production-install-checklist.md)
