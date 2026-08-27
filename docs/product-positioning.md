# Product Positioning

Canonical names and boundaries are defined in [source-of-truth-naming-map.md](source-of-truth-naming-map.md).

## In one sentence

**KynticAI Scout connects authorised business data, keeps the evidence in the customer's environment, links related information and makes the result easy for other software to use.**

## The product progression

- **Scout — Explore:** open source. Connect data, prove the basic context flow and build against normal APIs.
- **Fortress — Prove:** private production product for advanced governed analysis and enterprise extensions.
- **Elite — Scale:** the organisation-wide scale product for programmes spanning systems, divisions and security boundaries.

Cloud/control-plane services are optional supporting infrastructure, not the third product.

## What Scout does

Customers keep their existing CRM, ERP, support, billing, warehouse, product, spreadsheet and legacy systems.

Scout sits beside those systems. It:

1. reads or receives only authorised data;
2. keeps approved source evidence locally;
3. maps fields into reusable business facts;
4. links related records;
5. records where each item came from and when it was observed;
6. returns the result through REST, GraphQL and SDKs.

Scout core does not call an AI model. A customer can pass Scout output into an app, workflow, report, agent or model runtime of their choice.

## What is public

This repository contains the open-source Scout core, generic connectors, APIs, SDKs, admin/demo UI, local deployment path, public tooling and extension contracts.

It also contains public metadata tools:

- the generic Discovery Agent for local codebase audit/handover;
- the metadata-only Scout Discovery MCP for connector/catalogue inspection.

## What is private

Fortress/Elite implementation, private enterprise connectors, advanced private analysis, commercial Discovery MCP workflow/Discovery Signature generation, private deployment packs and customer-specific material stay outside this repo.

## Control plane

An optional control plane may manage commercial metadata such as licences, downloads, update channels, support access and approved aggregate usage counters.

It is not a raw customer-data store and is not required for Scout open-source use.

## Companion products

KynticAI Score is a separate product. Scout may publish a compatibility contract/client, but Scout does not calculate Score results.

Clarity and Importance are separate KynticAI products.

## Public claim discipline

Do not describe Scout as the full Fortress/Elite engine. Do not present examples or fallback heuristics as calibrated production intelligence. Do not describe optional control-plane services as a hosted Scout data plane.
