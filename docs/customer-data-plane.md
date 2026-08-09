# Customer Data Plane

This page explains how Context Engine · Scout runs beside customer systems as a customer-controlled data plane for exact data items, relationships, attribution paths, outcomes, governed context, and local APIs.

We do not build the brain. We build the nervous system. Scout carries trusted business context from existing operational systems to the customer's own AI tools, workflows, reports, apps, local LLMs, and agents.

## What It Is

The customer data plane is the part of Scout that runs near the customer's source systems. It owns:

- source access and connector configuration
- connector credentials and customer-approved source mappings
- selector definitions and selector execution
- semantic attributes and context facts
- exact data items, exact linked records, relationships, attribution paths, outcomes, context snapshots, and governed JSON/context packages
- confidence, freshness, explanations, provenance, masking, and audit
- API clients, local users, roles, scopes, and operational configuration
- REST, GraphQL, SDK, and internal-service access for downstream consumers

The customer data plane is the thing a paid pilot proves.

## What It Sits Beside

Scout can sit beside CRM, ERP, SQL databases, data warehouses, support desks, billing systems, product telemetry, spreadsheets, SharePoint, email engagement exports, web events, old .NET systems, and other operational sources.

Those systems remain systems of record. Scout does not require the customer to replace them before proving value.

## What Leaves The Data Plane By Default

By default, raw operational records, connector credentials, customer-specific mappings, exact linked records, relationships, attribution paths, outcomes, context facts, context snapshots, local JSON packages, prompt context packages, message bodies, document bodies, attachments, local databases, and logs with sensitive payloads should not leave the customer-controlled data plane.

An optional hosted or private Cloud/control plane can manage commercial metadata such as accounts, licences, downloads, update channels, support access, entitlement metadata, and optional aggregate usage. It should not receive raw operational customer data by default.

Cloud aggregate usage payloads may include tenant/control-plane identifiers, package version, feature usage counters, health/status, timestamps, and audit/control-plane event metadata. They must not include raw CRM records, support-ticket text, billing rows, customer email addresses, account names/domains, source payloads, connector secrets, context facts/snapshots, prompts, generated content, local evidence-pack JSON, recommendations, citation IDs, weighted signals, relationship type names, attribution paths, confidence, caveats, hashed subject/account identifiers, or per-entity relationship metadata by default.

## Exact Authorised Data In The Demo Plane

For the synthetic sales walkthrough, Scout links only the customer-approved records needed for the selected subject and objective:

- normalised email address and CRM contact/account profile
- account registration or profile fields
- sales activity, open opportunity, and prior won/lost outcome signals
- email replies or meetings booked
- web conversion and pricing-page events
- support tickets and support status
- product usage summaries
- billing health, payment status, and days-past-due signals

These records are carried as exact linked records with citation IDs. Deterministic relationships such as email-to-contact, contact-to-account, account-to-opportunity, account/contact-to-activity, contact-to-email-engagement, account/contact-to-web-conversion, account/contact-to-support-ticket, account/contact-to-product-usage, account-to-billing, and account/contact-to-outcome are built in the local data plane. Attribution paths record what happened and in what order. Scout may emit `BasicRelationshipEngine` fallback signals to support a recommended next action and a compatibility handoff artefact for proof-mode private analysis. Those probabilities are local/basic proof, not Fortress canonical scoring. Advanced relationship-set analysis, attribution-path comparison, outcome matching, and governed JSON handoff belong to Fortress/private extensions. Optional Cloud/control-plane output remains aggregate usage metadata only: counters, status, timestamps, package version, and audit/control-plane event metadata, not relationship types, attribution paths, weights, recommendations, confidence, caveats, citation IDs, raw operational records, or per-entity relationship metadata.

The B2B SaaS sales/customer-success path remains the primary demo. The companion relationship-intelligence proof fixture also covers ecommerce conversion, support churn, recruitment, finance retention, and healthcare operations with synthetic records only. Those examples are local proof artefacts for deterministic tests and docs, not production customer deployments, regulated datasets, vendor-certified connector runs, or traction evidence.

## How Context Is Produced

1. Approved source signals are read from existing systems or safe exports.
2. Selectors map authorised fields, events, and metrics into semantic attributes.
3. Context facts are written with confidence, freshness, explanation, and provenance.
4. Relationship sets link data items such as email address, email reply or meeting booked, web conversion, pricing-page visit, account registration/profile, CRM contact, opportunity, support ticket, product usage, billing health, and won/lost sale outcome.
5. Attribution paths record what happened, in what order, for a user, account, workflow, or other business entity.
6. Context snapshots version the facts and local relationship state.
7. Downstream consumers retrieve context through REST, GraphQL, SDKs, internal services, governed JSON packages, or `POST /api/v1/intelligence/next-action`.
8. In the wider Context Engine direction, Fortress/private modules can add advanced analysis before a customer-owned LLM, workflow engine, or app explains the ranked next action.

## Why It Matters Commercially

The paid value is implementation speed, connector work, selector design, governance, support, production rehearsal, and operational confidence.

A buyer can prove one workflow without a replatforming programme. A CTO can verify boundaries, scopes, API contracts, provenance, audit, backup expectations, and integration behaviour before expanding into private enterprise connectors or future hosted control-plane services.

## Open Core Boundary

The public repo includes the reusable open core: local demo, customer data-plane foundations, APIs, SDKs, generic connectors, mock connectors, selector engine, exact data items, local relationships, provenance, audit, docs, and extension points.

Paid/private scope can include Fortress connector implementations, advanced relationship analysis, attribution-path analysis, SSO, SCIM, advanced governance, compliance exports, deployment packs, SLAs, private cloud support, and optional hosted control-plane operations. Those paid implementations are intentionally not shipped in this public repository.
