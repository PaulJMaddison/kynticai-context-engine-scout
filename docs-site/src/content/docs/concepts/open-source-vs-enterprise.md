---
title: Open Source vs Enterprise
description: What ships in the free KynticAI Scout open-source data-plane core and what requires a commercial licence.
---

KynticAI follows an **open-core** model. The public **Scout** repository is
the free, MIT-licensed customer data-plane foundation. Private enterprise
modules are available separately under a commercial licence.

## What's Included in Scout (Open Source)

Scout is a complete, self-hostable customer data-plane platform. The
open-source repository includes:

| Area | What You Get |
|---|---|
| **Semantic Engine** | Selector execution, fact materialisation, confidence scoring |
| **Exact Data Items** | Subject-scoped records, data items, citations, masking decisions, and provenance |
| **Relationship JSON** | Governed relationships, attribution-path evidence, basic fallback-only signals, and next-best-action workflows for approved consumers |
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

## What Requires a Commercial Licence

Private enterprise modules extend the Scout core with capabilities designed
for production enterprise deployments. They are not included in the Scout
repository and are not open source.

Enterprise capabilities include:

- proprietary analysis modules for relationship sets, attribution paths, comparable examples, and outcome-pattern scoring
- Vendor-certified connectors (e.g. Salesforce, HubSpot, Dynamics,
  Snowflake, SAP, and others)
- Enterprise SSO / SAML / SCIM identity integration
- Advanced governance and compliance exports
- Credential vault integrations
- Managed deployment packs and installers
- SLA-backed support

:::note
Private enterprise internals are not published in the Scout repository. The list
above describes the *category* of enterprise capability, not implementation
details.
:::

## How They Relate

Scout defines stable public extension interfaces. Private enterprise modules
implement those interfaces in a separate private codebase. Enterprise modules
plug into the Scout core via dependency injection — no forking required.

```
┌─────────────────────────────────────────────┐
│  KynticAI Scout (open source, MIT)          │
│  Customer data plane, APIs, SDKs, admin UI, │
│  exact items, generic connectors, seams     │
├─────────────────────────────────────────────┤
│  Private enterprise modules                 │
│  proprietary analysis modules, connectors   │
│  governance, compliance, managed deployment │
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
