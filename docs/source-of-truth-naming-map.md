# KynticAI Workspace Naming Map — Source of Truth

This document is the canonical source of truth for product, brand, and artefact names used across the KynticAI Scout public repository, its docs, demo copy, and the documentation site. When in doubt about a name, follow this map.

## Purpose

- Every user-facing name in the public repo must match the spellings and rules below.
- This map is referenced by the root README, the product positioning and commercial readiness documents, and the docs-site README.
- If a document contradicts this map, the document should change, not this map.

## Product and brand names

| Name | Canonical spelling | When to use |
| --- | --- | --- |
| Public brand | `KynticAI` | The company and public brand, always written with `AI`. Never write bare "Kyntic" in user-facing copy. |
| Open-core product tier | `KynticAI Scout` | The open-source, MIT-licensed public product: the open-core data plane, local demo, admin console, APIs, SDKs, and docs. |
| Universal Context Layer | `UCL` | The conceptual layer and architecture: source systems -> customer-owned data plane -> governed context for approved consumers. Write the full term once, then use `UCL`. |

Rule: `KynticAI` always carries the `AI`. Plain "Kyntic" is forbidden in all user-facing copy, docs, and demo text.

## Artefact and product names

| Name | Canonical usage |
| --- | --- |
| UCL data plane | The customer-owned data-plane layer that Scout implements. |
| Customer data plane | The same layer seen from the customer's perspective: exact data items, relationships, attribution paths, provenance, audit, and local APIs stay customer-controlled by default. |
| Control plane | The optional hosted commercial/control-plane layer (Scout Cloud). It manages accounts, licences, downloads, support access, update channels, and optional aggregate usage metadata only. It is never a data-plane store. |
| Open core | The public, MIT-licensed Scout core in this repository. |
| Private Enterprise modules | Scoped paid/private extensions: private connectors, governance, identity, deployment packs, and advanced analysis. They live outside the public repo. |
| Cloud | Optional commercial/control-plane support (Scout Cloud). Not required to run the data plane; must not receive raw customer data or derived relationship intelligence by default. |
| KynticAI Discovery MCP | The buyer-facing, metadata-only wrapper for IT-manager-led discovery: local codebase audit, connector catalogue inspection, manifest validation, metadata quality report, and Discovery Signature review. |
| KynticAI Score | A separate KynticAI product with its own public API contract (`schema/kyntic-score.openapi.yaml`). Scout does not calculate scores itself. |
| n8n node | The public n8n integration node for the Scout data plane. |
| Connector Catalogue | The public connector catalogue page and API surface (`GET /api/v1/connectors/catalogue`). |
| Connector marketplace | The broader concept of catalogue listings, placeholders, and commercial entries. Prefer "Connector Catalogue" for the public page. |
| Paid pilot | The supported first commercial offer: a scoped, implementation-led paid pilot that keeps customer operational data in the customer data plane by default. |
| Pilot setup wizard | The guided buyer onboarding flow used to install and register a paid pilot data plane. |
| Clarity and Importance | Separate KynticAI products. They are not required UCL dependencies and must not be presented as part of the open core. |

## Naming maturity rules

Names in the tables above are approved for user-facing copy. The following must never appear in public copy, docs, or demo text:

- the private engine codename and internal engine short names (refer to them generically as "the private engine" when a reference is unavoidable);
- private vector-database, embedding, and vector-pipeline product or implementation terms (refer to them generically, for example "the private relationship-analysis implementation");
- internal project keys, customer names, customer-specific mappings, paid connector implementation details, credentials, tokens, or support bundles.

Additional rules:

- Always write `KynticAI`, never bare "Kyntic", in user-facing copy.
- Use British English spellings in all user-facing copy: `behaviour`, `licence`, `organisation`, `prioritisation`. Do not use American spellings such as "behavior", "organization", or "prioritization".
- Do not claim complete self-serve SaaS, vendor-certified connectors, customer traction, or production capabilities that the public open core does not ship.
- Cloud must be described as optional commercial/control-plane support, never as a hosted data plane or a raw customer-data store.
- Scout does not call an AI model. Position it as context infrastructure for AI-enabled products.

## Consistency requirements

This map is the single source of truth. Enforced references:

- `README.md` (repo root) links here as `docs/source-of-truth-naming-map.md`.
- `docs/product-positioning.md` and `docs/commercial-readiness-summary.md` link here as `source-of-truth-naming-map.md` (same directory).
- `docs-site/README.md` links here as `../docs/source-of-truth-naming-map.md`.

Writers should copy canonical names from this map rather than inventing variants. When a name change is required, update this map first, then update the referencing documents.
