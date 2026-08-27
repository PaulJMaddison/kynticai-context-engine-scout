# KynticAI Product and Naming Map — Source of Truth

This document is the canonical source of truth for current public product names and boundaries in the KynticAI Scout repository.

If another current document contradicts this map, update that document. Historical release notes may retain older terminology only when they are clearly historical.

## Product line

KynticAI has three products in this progression:

| Product | Stage | Public meaning |
| --- | --- | --- |
| **KynticAI Scout** | **Explore** | The open-source, MIT-licensed product in this repository. Scout connects to authorised sources, retains approved evidence locally, maps and links information, and exposes reusable context through APIs and SDKs. |
| **KynticAI Fortress** | **Prove** | The private production product for governed, advanced analysis and private enterprise extensions. Fortress implementation does not belong in this public repository. |
| **KynticAI Elite** | **Scale** | The enterprise scale offering for programmes spanning multiple systems, teams, divisions or security boundaries. Elite implementation does not belong in this public repository. |

**Cloud is not the third product.** "Cloud" and "control plane" describe optional hosted/commercial services that may support Scout, Fortress or Elite. They must not be used as a replacement product name for Elite.

## Scout

Use `KynticAI Scout` for the public product.

Scout is customer-controlled context infrastructure. Its core responsibilities are:

- connect to or receive authorised source data;
- retain approved source evidence locally;
- map source fields into reusable business facts;
- link related records;
- record where information came from and when it was observed;
- expose the resulting context through normal APIs and SDKs;
- provide safe extension points for private products without shipping their implementation.

Scout core **does not execute an AI model**. Customers may feed Scout output into their own applications, workflows, agents or model runtimes.

## Fortress

Use `KynticAI Fortress` for the private Prove product.

Public Scout documentation may describe the boundary at a high level, but must not publish Fortress source code, private algorithms, private connector implementations, private deployment details or commercial buyer workflow internals.

## Elite

Use `KynticAI Elite` for the Scale product.

Elite is the product name for organisation-wide scale. Do not call the third product Cloud, PrivateCloud, assisted private tier or similar legacy names in current public copy.

## Cloud / control plane

Use `control plane` for an optional hosted service that handles commercial and operational metadata such as:

- account/licence/entitlement state;
- downloads and update channels;
- support access;
- deployment registration and safe health/version metadata;
- explicitly approved aggregate usage counters.

The control plane is **not** the Scout data store. It must not receive raw customer operational records, connector credentials, retained source evidence, context facts, relationship intelligence, prompts or generated customer content by default.

## Discovery tooling

Three concepts must remain distinct:

1. **Discovery Agent** — the generic local codebase audit/handover tool in `apps/discovery-agent`. Public.
2. **Scout Discovery MCP** — the metadata-only Scout connector/catalogue inspection package in `packages/typescript/scout-discovery-mcp`. Public.
3. **KynticAI Discovery MCP** — the commercial buyer/readiness workflow, including Discovery Signature generation, commercial readiness routing and private customer handoff. Private; belongs with Fortress/private product code and must not be implemented in Scout.

Public Scout code may document how to inspect metadata. It must not recreate the commercial buyer workflow.

## Companion products

`KynticAI Score` is a separate KynticAI product. Scout may carry a public compatibility contract/client while that is useful, but Scout does not calculate KynticAI Score results itself and Score must not be listed as a Scout engine capability.

Clarity and Importance are separate KynticAI products and are not required Scout dependencies.

## Technical names

Existing stable contract names such as `UCL` may remain where they are part of repository history, schemas or compatibility contracts.

Use plain language in new public copy. Avoid turning internal architecture terms into additional product names.

## Brand rules

- Always write **KynticAI**, never bare "Kyntic", in user-facing copy.
- Use British English spellings.
- Do not claim self-serve SaaS, vendor certification, customer traction or private capabilities the public repo does not ship.
- Do not expose private engine codenames, private vector/embedding internals, customer-specific mappings, credentials, tokens or support bundles.
- Use **Scout → Fortress → Elite** consistently in current product copy.
