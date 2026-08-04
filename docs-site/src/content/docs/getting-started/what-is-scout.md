---
title: What is KynticAI Scout?
description: An introduction to KynticAI Scout — open-source customer data-plane infrastructure for AI-enabled products.
---

## Overview

**KynticAI Scout** is open-source customer data-plane infrastructure for
AI-enabled products. It sits beside your existing systems — CRM, ERP,
support desk, billing, data warehouses — and creates governed exact data
items, relationships, attribution paths, outcomes, and semantic context above
them so that downstream consumers receive trusted business meaning instead of
raw records.

Scout does not replace any source system. It does not call an AI model. It
builds the *nervous system* that carries business context from existing
operational data to AI tools, copilots, workflow engines, reporting
dashboards, and internal applications.

## Key Capabilities

| Capability | Description |
|---|---|
| **Selector Engine** | Admin-authored rules that turn raw fields into canonical semantic attributes |
| **Context Snapshots** | Reusable business profiles with confidence, freshness, and provenance |
| **Semantic Facts** | Discrete governed units of business meaning with audit metadata |
| **GraphQL + REST APIs** | Every context surface available through both query styles |
| **TypeScript & .NET SDKs** | Typed client libraries for integration teams |
| **Relationship JSON** | Exact linked records, relationships, attribution-path evidence, citations, masking decisions, and next-best-action context — Scout does not call an AI model |
| **Connector Framework** | Generic SQL, REST, CSV, mock connectors and extension points |
| **Audit & Provenance** | Every read, recompute, and context access is traceable |
| **Blueprint Import** | AI-generated configuration validated and imported without calling AI APIs |
| **Admin Console** | React-based UI for data sources, selectors, schemas, and context viewer |

## Architecture

Scout operates as a **customer data plane** — it runs in your environment
and your operational data stays under your control.

```
Existing Systems ──► Connectors ──► Selector Engine ──► Semantic Schema
                                                            │
                                      Exact Items + Relationships + Paths
                                                            │
                                                    Context Snapshots
                                                            │
                                              GraphQL / REST / SDKs
                                                            │
                                                    Downstream Consumers
                                              (copilots, apps, reports,
                                               workflows, agents)
```

For the demo sales workflow, exact authorised data includes normalised email
address, CRM contact/account, account registration/profile, sales activity,
opportunities, email replies, meetings booked, web conversion and pricing-page
events, support tickets, product usage summaries, billing health, and won/lost
outcome signals. Enterprise capabilities outside the open-core deliverable
compare relationship sets and return governed JSON for the customer's
LLM or KynticAI open-source LLM runtime.

An optional hosted Cloud/control plane manages only commercial metadata —
accounts, licences, downloads, support access, update channels, and optional
aggregate usage. It is not required for the data plane and must not receive raw
customer data by default.

Clarity and Importance are separate KynticAI products and are not required for
UCL/Scout, Enterprise, or Cloud.

## Licence

KynticAI Scout is released under the [MIT Licence](https://github.com/PaulJMaddison/scout/blob/main/LICENSE).

## Next Steps

- [Install Scout](/getting-started/installation/) on your machine.
- Follow the [Quickstart](/getting-started/quickstart/) to run a local demo.
- Read the [Architecture](/architecture/) overview.
- Explore the [API Overview](/apis/overview/).
