# Open-Core Product Boundary

This document explains what belongs in the public KynticAI Scout product repository and what should remain outside it. Scout is context infrastructure for AI-enabled business systems, so the boundary protects the reusable open core while leaving room for commercial enterprise and hosted-control-plane extensions.

This repository is public-facing. It must not contain private strategy, customer-specific material, secrets, generated runtime artefacts, paid connector implementation code, or overclaiming SaaS copy.

## Repository shape

The intended long-term structure is:

- this public repository
  The public customer data-plane core, demo/admin console, SDKs, extension contracts, GraphQL and REST APIs, and local/backend-only runtime.
- private extension repositories
  Paid extension implementations such as advanced relationship-set analysis, attribution-path analysis, enterprise connectors, SSO, advanced governance, and managed deployment assets.
- private control-plane repositories
  Commercial concerns such as account management, billing seams, licences, entitlements, downloads, support access, update channels, aggregate usage metadata, data-plane registration, audit, and cloud operations.

The public repo should define the stable contracts and composition points that let those future codebases depend on the open core without copying it.

## Goal

The public repository should remain a credible, useful open source core. It should be possible for developers and technical teams to:

- understand the architecture
- run the product locally
- self-host the core
- explore the APIs and SDKs
- experiment with selectors, context snapshots, provenance, and audit flows
- build context consumers such as apps, workflows, reporting tools, copilots, local LLMs, and agents
- build extensions against stable public contracts

The public repo should not become a disguised commercial distribution channel for paid enterprise implementation code.

## Current repository inspection

The current repo already contains the main public core surfaces:

- semantic selector execution and fact materialisation in the application and infrastructure projects
- exact linked records, context facts, context snapshots, semantic attribute definitions, provenance, confidence, freshness, masking, and audit primitives
- GraphQL and REST API surfaces
- SQLite local demo mode with fictional seed data
- PostgreSQL support and migrations
- mock connector support and generic SQL/REST connector plugins
- extension interfaces for future enterprise modules
- React docs/demo/admin UI for evaluating the data plane
- documentation for SaaS architecture, connector plugins, SDKs, product positioning, and context consumers

That shape is intentional. The public repo should prove the architecture and remain useful without shipping private enterprise implementation code.

The fictional sales/next-action implementation lives under `examples/KynticAI.Scout.Reference.Sales`. It exists to demonstrate a consumer use case and must not be registered as Scout core runtime behaviour. Its fixed weights and sales recommendations are example business rules, not calibrated Scout platform intelligence.

## What belongs in this repository

The public repo is the right home for:

- the semantic engine and selector execution logic
- exact linked records, context facts, and context snapshots
- governed context and relationship/evidence contracts; business-specific next-action scoring belongs in reference consumers or private products
- provenance, freshness, confidence, and audit primitives
- GraphQL and REST APIs
- SDKs and developer tooling
- SQLite local mode and supported PostgreSQL mode
- mock connectors and deterministic demo connectors
- SQL connector examples when they are generic, safe, and free of customer-specific schema assumptions
- file connector examples or file-upload fixtures when they use fictional data and do not imply managed enterprise ingestion
- generic REST connector examples that demonstrate the contract without becoming a vendor-specific integration
- extension interfaces for connectors, auth, secrets, policy evaluation, relationship analysis, audit export, context package export, approval workflows, promotion, and usage metering
- safe mock or no-op default implementations for those interfaces
- SaaS/control-plane foundation metadata models for tenants, workspaces, subscriptions, API clients, onboarding, context package manifests, and usage metering, provided they stay provider-neutral
- local licence and control-plane configuration seams, provided they do not phone home or unlock private code inside the public repo
- context-package generation patterns and delivery-channel metadata; the legacy next-action API contract is compatibility-only and does not make sales heuristics Scout core behaviour
- demo UI, samples, and fictional seed data
- documentation and in-repo demo copy that explain architecture, extension patterns, product positioning, and the public/private boundary

## What is explicitly public

Keep these in the open core:

1. Semantic engine.
2. Context facts and snapshots.
3. GraphQL and REST APIs.
4. SQLite local demo.
5. PostgreSQL support.
6. Mock connectors.
7. SQL and file connector examples if they are generic, safe, and fictional.
8. Extension interfaces.
9. Documentation and demo/admin copy.

The key rule is that public code may define a stable contract and a safe generic implementation. Public code should not implement a paid, vendor-specific, customer-specific, or deployment-specific enterprise feature.

## What does not belong in this repository

These should normally live in a future private enterprise repository, optional commercial Cloud/control-plane codebase, or professional services delivery materials:

- private relationship-analysis implementation modules and private relationship-engine internals
- real enterprise connectors such as Salesforce, HubSpot, Dynamics, Snowflake, BigQuery, Zendesk, NetSuite, Microsoft 365 / Outlook, Gmail / Google Workspace, Slack, Microsoft Teams, Outlook Calendar, Google Calendar, Segment, Amplitude, Mixpanel, PostHog, Jira, Linear, Confluence, Notion, SharePoint, Google Drive, SAP, ServiceNow, customer data warehouses, or other paid packaged integrations
- SSO or SAML implementations
- hosted SaaS control-plane implementation
- commercial licence signing, entitlement enforcement for paid modules, and private package distribution services
- Stripe, Paddle, or other billing-provider integrations
- customer-specific deployment templates
- private cloud automation
- credential vault integrations such as HashiCorp Vault, AWS Secrets Manager, Azure Key Vault, or GCP Secret Manager
- enterprise policy engines
- compliance report exporters
- advanced governance or compliance packs intended for paid distribution
- managed deployment automation sold as part of an enterprise or SaaS offer
- customer-specific connectors, schemas, mappings, prompts, or data
- secrets, certificates, tokens, and production credentials

## What is explicitly private

Do not implement these publicly:

1. Real enterprise connectors.
2. Private relationship/vector analysis modules.
3. SSO/SAML.
4. Stripe/Paddle billing.
5. Customer specific deployment templates.
6. Private cloud automation.
7. Credential vault integrations.
8. Enterprise policy engine.
9. Compliance report exporters.

## Public interfaces vs private implementations

It is intentional for the public repo to expose extension points for future enterprise features. That means:

- interfaces can be public
- DTOs and DI hooks can be public
- mock implementations can be public
- generic examples can be public when they use fictional data and generic protocols
- documentation can explain the shape of future enterprise integration

But:

- premium implementations should stay outside this repo
- vendor-specific adapters should stay outside this repo
- customer-specific deployment and mapping assets should stay outside this repo
- examples should stay generic
- documentation should not pretend that paid implementations already ship here

## Connector boundary

The public connector layer may include:

- `mock` connectors for deterministic demos and tests
- generic `sqlDatabase` or `sqlTable` examples that read from the local demo database or a clearly documented generic schema
- generic `restApi` examples that show request, response, credentials reference, validation, health check, and provenance shape
- file or upload examples only when they use fictional fixtures and do not encode a customer data model
- catalogue placeholders for paid/private connectors, clearly marked as non-executable and impossible to configure in the public repo

The private connector layer should contain:

- packaged vendor connectors
- customer-specific connector mappings
- production credential vault adapters
- support-backed connector certification
- managed connector sync operations
- paid connector deployment templates

If a connector knows the business semantics of a named vendor or a named customer environment, it should normally be private.

## Auth, billing, governance, and deployment boundary

Public code can include simple local authentication, API client metadata, provider-neutral usage records, no-op policy evaluators, mock audit exporters, and interfaces that describe how enterprise modules plug in.

Private code should implement advanced relationship analysis, enterprise SSO/SAML, billing provider integration, commercial licence/entitlement enforcement, policy engines, compliance exporters, vault integrations, private cloud automation, Cloud commercial-control workflows, and customer-specific deployment packs.

Cloud commercial-control code must still respect the customer data-plane boundary: it may receive account, licence, entitlement, download/update, support, deployment registration, health, audit, and aggregate usage metadata, but not raw customer records or derived relationship intelligence by default. See [Cloud Commercial Control Contract](cloud-commercial-control.md).

## Decision rule

When deciding whether something belongs in the public repo, ask:

1. Does this make the open source core more useful on its own?
2. Is this a generic contract or a paid implementation?
3. Would publishing this leak customer-specific, security-sensitive, or commercial IP?
4. Can the same outcome be achieved by documenting an interface rather than shipping the implementation?
5. Does this integration mention a real enterprise vendor, a customer environment, a billing provider, an identity provider, a cloud vault, or a private deployment target?

If the answer is unclear, prefer keeping the public repo smaller and documenting the extension point clearly.
