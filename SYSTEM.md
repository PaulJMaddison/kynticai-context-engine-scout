# KynticAI Scout System Orientation

KynticAI Scout is the open-source **Explore** product and public customer-controlled context foundation in the Scout -> Fortress -> Elite progression.

This file gives AI agents a compact public-safe map so they do not need to reconstruct Scout's role from the codebase.

## Read order

1. `SYSTEM.md`;
2. relevant `README.md` sections;
3. `docs/agent-native-scout.md`;
4. the smallest public API/connector/data-plane/work-package document needed;
5. targeted code/tests.

## Authority

Scout owns the open-source customer-side context foundation: approved source ingestion through Scout boundaries, selectors/mapping, evidence/provenance, related-record links, useful context APIs/SDKs, audit/auth/local operations, public Discovery Agent and public metadata Discovery MCP, and controlled upgrade compatibility towards Fortress.

Scout core remains useful without calling an AI model.

## Public boundary

Scout must not contain or imply private Fortress/Elite implementation details, private commercial connectors, the commercial Discovery MCP buyer workflow, hidden hosted-model dependencies, Cloud ownership of raw customer data or enterprise-only private internals.

## Agent-native role

Scout should be the public reference for **context-system legibility**.

A fresh agent should be able to discover:

- instance/version;
- tenant/resource scope;
- public capabilities;
- configured source/connector health;
- evidence/context freshness;
- authoritative API/SDK surface;
- what an operation may change;
- how to verify the result.

This should not require broad source search or loading raw datasets.

## Public agent-facing contracts

Target small versioned public descriptors for:

- system/instance manifest;
- public capability catalogue;
- compact state/readiness;
- bounded context/evidence references;
- receipts for meaningful import/migration/admin operations.

The public Discovery Agent/MCP may expose these descriptors, but must retain their current public boundaries and never grow private commercial discovery behaviour.

## Context economy

For runtime questions prefer current API/state/provenance to source-code inference.

For engineering work prefer `SYSTEM.md`, the relevant public contract and targeted tests before scanning unrelated code.
