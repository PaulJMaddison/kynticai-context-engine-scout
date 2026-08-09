---
title: Open Source vs Enterprise
description: What ships in the free Context Engine · Scout open-source proof path and what routes to Fortress.
---

KynticAI follows an **open-core** model. The public **Scout** repository is
the free, MIT-licensed local proof path for Context Engine data-plane
mechanics. Production/private scale and canonical scoring route to Fortress.

## What's Included in Scout (Open Source)

Scout is a complete local/open-source proof of a self-hostable customer
data-plane foundation. The open-source repository includes:

| Area | What You Get |
|---|---|
| **Semantic Engine** | Selector execution, fact materialisation, confidence scoring |
| **Exact Data Items** | Subject-scoped records, data items, citations, masking decisions, and provenance |
| **Relationship JSON** | Governed relationships, attribution-path evidence, basic fallback-only signals, caveats, and next-action options for approved consumers |
| **Context Snapshots** | Point-in-time business profiles with provenance |
| **GraphQL + REST APIs** | Full query surface for all context data |
| **TypeScript SDK** | Typed client for Node.js and browser environments |
| **.NET SDK** | Typed client for C# applications |
| **Admin Console** | React-based UI for data sources, selectors, schemas, and context |
| **SQLite Local Mode** | Zero-dependency local development and demos |
| **PostgreSQL Support** | Production database with migrations |
| **Generic Connectors** | SQL, REST, CSV, and mock connector plugins |
| **Extension Interfaces** | Stable contracts for building custom connectors and extensions |
| **Audit & Provenance** | Traceable access, recomputation, and governance records |
| **Blueprint Import** | AI-generated configuration import (no AI API calls required) |
| **Docker Support** | Single-container and Compose-based deployment |
| **Demo Data** | Realistic seeded B2B SaaS dataset for evaluation |

## What Routes To Fortress

Fortress extends the Scout proof path with capabilities designed for private
sovereign production deployments. These capabilities are not included in the
Scout repository and are not open source.

Enterprise capabilities include:

- canonical analysis modules for relationship sets, attribution paths, comparable examples, and outcome-pattern scoring
- Vendor-certified connectors (e.g. Salesforce, HubSpot, Dynamics,
  Snowflake, SAP, and others)
- Enterprise SSO / SAML / SCIM identity integration
- Advanced governance and compliance exports
- Credential vault integrations
- Managed deployment packs and installers
- production support, deployment governance, and SLA-backed operation

:::note
Fortress internals are not published in the Scout repository. The list
above describes the *category* of enterprise capability, not implementation
details.
:::

## How They Relate

Scout defines stable public extension interfaces. Fortress/private modules
implement those interfaces outside this public codebase and plug into the
Scout core via dependency injection — no forking required.

```
┌─────────────────────────────────────────────┐
│  KynticAI Scout (open source, MIT)          │
│  Customer data plane, APIs, SDKs, admin UI, │
│  exact items, generic connectors, seams     │
├─────────────────────────────────────────────┤
│  Fortress/private modules                   │
│  canonical analysis, connectors, governance │
│  compliance, managed deployment             │
└─────────────────────────────────────────────┘
```

## Enquiries

For enterprise licensing or technical questions, visit
[kynticai.com](https://kynticai.com).

## Next Steps

- [What is KynticAI Scout?](/getting-started/what-is-scout/) for an
  introduction to the platform.
- [Connector Basics](/concepts/connector-basics/) for how data flows from
  source systems into the semantic layer.
- [Self-Hosting](/self-hosting/) for the public data-plane deployment
  checklist.
