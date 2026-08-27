---
title: Architecture
description: Public architecture of Context Engine · Scout and its open-source data-plane boundary.
---

Context Engine · Scout is the public, MIT-licensed local proof path for the
Universal Context Layer data-plane foundation. It runs beside existing operational systems,
turns approved source records into exact data items, relationships, attribution
paths, outcomes, governed semantic context, and local JSON packages, and exposes
that context through GraphQL, REST, SDKs, and context packages.

Scout's core context pipeline does not call an AI model. Its job is to give
customer-owned applications, reports, workflows, local LLMs, agents, and
AI-enabled products a trusted memory layer with provenance and confidence. Optional
demo workflows can consume Scout context, but context generation remains
grounded in approved source data.

## Public Boundary

| Layer | Public Scout responsibility |
|---|---|
| Source access | Generic SQL, REST, CSV, file, and mock-safe connector paths. |
| Selector engine | Rules that map approved source fields into semantic attributes. |
| Semantic schema | Canonical attributes, data types, confidence, freshness, and provenance. |
| Context records | User, account, exact linked record, relationship, attribution path, outcome, fact, snapshot, package, audit, and recomputation records. |
| API surfaces | Authenticated GraphQL, REST v1, legacy REST, TypeScript SDK, and .NET SDK. |
| Admin console | Local React admin/demo console for schemas, selectors, sources, and context. |

Private connector implementations, customer-specific mappings, managed
deployment code, Fortress canonical analysis modules, relationship-set
analysis, attribution-path analysis, private engine internals, and proprietary
roadmap material are intentionally outside the Scout repository.

## Runtime Shape

```text
Approved source systems
  -> connector plugin
  -> raw payload and provenance
  -> selector execution
  -> semantic facts and exact data items
  -> relationships, attribution paths, and outcomes
  -> governed context snapshots and packages
  -> GraphQL / REST / SDK consumers
  -> optional reference/customer-owned/private consumers for recommendation or model work
```

The API is an ASP.NET Core application. The admin console is a Vite React
application. Persistence uses Entity Framework Core with SQLite for local
evaluation and PostgreSQL for production-style self-hosting.

## Data Planes And Control Planes

Scout is designed as a customer data plane. Customer operational data,
connector credentials, exact linked records, relationships, attribution paths,
context facts, local JSON/evidence packages, snapshots, and audit data stay inside the self-hosted Scout
environment.

The public repo also contains metadata types used for SaaS-style account,
workspace, licence, API-client, billing-usage, and onboarding records. In
the open-source repo these are public-safe data-plane foundations, not a
hosted control-plane deployment promise.

Optional Cloud/control-plane payloads should stay aggregate/control-plane-only by
default: tenant/control-plane identifiers, package version, feature counters,
health/status, timestamps, and audit/control-plane event metadata. They should
not require raw source records, connector credentials, local evidence-pack
JSON, context facts or snapshots, recommendations, citation IDs, weighted
signals, attribution paths, relationship types, confidence, caveats, or per-entity relationship
metadata to leave the customer data plane.

## Core Projects

| Project | Purpose |
|---|---|
| `src/KynticAI.Scout.Api` | ASP.NET Core API, auth, REST, GraphQL, OpenAPI, health checks. |
| `src/KynticAI.Scout.Application` | Contracts, service interfaces, validation, selector inputs/results. |
| `src/KynticAI.Scout.Domain` | Domain entities, enums, and semantic attribute constants. |
| `src/KynticAI.Scout.Infrastructure` | EF Core persistence, connectors, auth services, seed data, jobs. |
| `src/KynticAI.Scout.Sdk` | Typed .NET SDK. |
| `packages/typescript/scout-sdk` | Typed TypeScript SDK. |
| `apps/web` | React admin/demo console. |

## Trust Model

Scout treats context as governed data, not as a free-form prompt cache.
Important controls include:

- tenant-scoped API access
- role and API-client scope checks
- protected connector credentials
- selector validation before execution
- exact authorised records linked by citation ID
- confidence and freshness on facts
- audit records for context and administration activity
- provenance on facts and context packages
- source-traced relationship/evidence JSON that downstream consumers can use without sending raw data to a control plane
- production readiness checks for unsafe development settings

## Open Source And Enterprise

Scout defines the public extension contracts. Fortress/private commercial
modules live outside this repository. The public docs describe only the
category boundary: canonical relationship-set analysis, vendor-specific
connectors, enterprise identity, managed deployment support, and advanced
governance belong outside Scout. Fortress and Elite remain separate KynticAI product tiers outside this Scout repository; optional control-plane services are supporting infrastructure rather than a product tier.

See [Open Source vs Enterprise](/concepts/open-source-vs-enterprise/) for
the support boundary and [Connector Authoring](/connectors/authoring/) for
the public connector contract.

## Source Of Truth

The architecture described here is grounded in the public solution layout,
the ASP.NET Core API in `src/KynticAI.Scout.Api`, the application contracts
in `src/KynticAI.Scout.Application`, the EF Core persistence model in
`src/KynticAI.Scout.Infrastructure`, and the SDKs under `src/` and
`packages/typescript/`. Future diagrams should be generated only from
public repository structure and must not imply Fortress/private internals.
