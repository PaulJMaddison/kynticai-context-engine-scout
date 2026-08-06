# Roadmap

This roadmap describes the current direction of KynticAI Scout at a high level. It is intended to help contributors understand the shape of the public open source core and the likely boundary to future commercial offerings.

## Shipped

These capabilities are part of the public open-core deliverable in this repository today. Each links to its primary documentation and the release that introduced or formalised it.

| Capability | Documentation | Introduced |
| --- | --- | --- |
| Semantic/selector engine | [docs/saas-architecture.md](saas-architecture.md) | v2.0.0 |
| Context facts and snapshots | [docs/public-api-contract.md](public-api-contract.md) | v2.0.0 |
| GraphQL and REST APIs | [docs/public-api-contract.md](public-api-contract.md) | v2.0.0 |
| SQLite and PostgreSQL data access | [docs/getting-started.md](getting-started.md), [docs/hosted-deployment.md](hosted-deployment.md) | v1.0.0 |
| Connector plugin model and catalogue | [docs/connector-plugin-model.md](connector-plugin-model.md), [docs/connector-marketplace.md](connector-marketplace.md) | v2.0.0 |
| Blueprint Import | [docs/context-consumers.md](context-consumers.md) | v2.0.0 |
| Webhook signing secrets | [docs/webhook-events.md](webhook-events.md) | v2.3.0 |
| M2M identity and API clients | [docs/machine-to-machine-identity.md](machine-to-machine-identity.md), [docs/api-scopes.md](api-scopes.md) | v2.0.0 |
| Score API | [docs/score-api.md](score-api.md) | main after v2.8.0 |
| Discovery MCP/agent | [docs/discovery-agent-mcp.md](discovery-agent-mcp.md) | main after v2.8.0 |
| n8n node | [docs/n8n-node.md](n8n-node.md) | main after v2.8.0 |
| docs site | [docs-site/README.md](../docs-site/README.md) | main after v2.8.0 |
| Scout pilot setup wizard | [docs/paid-pilot-setup.md](paid-pilot-setup.md) | main after v2.8.0 |

Capabilities marked "main after v2.8.0" are merged into the open-core repository on the default branch and are not yet part of a tagged release. The [root CHANGELOG](../CHANGELOG.md) is the canonical record of releases.

## Directional priorities

These are directions for the open core, not committed delivery dates or scope. Priorities may change as the work-package backlog evolves:

- strengthen the semantic context model
- improve selector execution, provenance, confidence, and freshness handling
- improve the GraphQL and REST developer experience
- improve SDK usability
- keep local self-hosting and demo flows simple
- provide stable extension contracts for future enterprise modules
- improve documentation, tests, and examples

## Public/private boundary

The intended repository split is:

- this public repository for the open source core
- private enterprise repositories for paid extension implementations
- private control-plane repositories for hosted commercial operations

### Likely future private enterprise areas

These are likely to be developed outside the public repo:

- real enterprise connectors across CRM, warehouse, email, chat, calendar, analytics, work management, and knowledge systems
- SSO/SAML implementations
- Stripe, Paddle, or other billing-provider integrations
- customer-specific deployment templates
- private cloud automation
- credential vault integrations
- enterprise policy engines
- compliance report exporters
- support-backed observability and operational tooling

This list is here to clarify boundary expectations, not to imply that those implementations already exist in the public repository. Capabilities beyond the open-core deliverable remain outside this repository.

## Scout Cloud and managed control plane

KynticAI Scout Cloud is an optional, support-only offering today. It can manage accounts, licences, downloads, update channels, support access, and optional aggregate usage metadata; it is not required to run the data plane and must not receive raw customer operational data or derived context by default. A managed control-plane offering is a next candidate step for Scout Cloud; if it proceeds, it will likely focus on:

- hosted operations
- tenant administration
- managed upgrades
- usage metering and operational packaging
- hosted control-plane concerns that do not belong in the open source core

## How we track

Shipped and planned work is tracked as public work packages in [docs/work-packages/README.md](work-packages/README.md). The roadmap above summarises the shipped features and the directional priorities; the work-package backlog carries the concrete planned slices and their status, so the two should be read together.

## Roadmap principles

- The open source core should remain useful without paid features.
- Public interfaces are welcome when they make the core cleaner and more extensible.
- Paid implementation code should not be mixed into the public repo by accident.
- Fictional demo data, safe defaults, and honest documentation matter as much as runtime code.
