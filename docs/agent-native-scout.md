# Agent-Native Scout Design

Date: 2026-08-31  
Status: public design direction

## Objective

Make Scout exceptionally easy for AI agents and normal software to understand and integrate with while preserving its open-source, customer-controlled and model-independent character.

## Principles

### Self-description before orchestration

Scout should first describe version, public capabilities, tenant/resource scope, connector/source state, API/SDK contracts, context freshness and operation constraints.

Do not add a general autonomous-agent framework merely to make Scout "agentic".

### Public source of truth

Generate/validate descriptors from existing public routes/contracts/catalogues where practical. Avoid a second drifting list.

### Customer-controlled data remains local

Agent-facing metadata must not imply that Cloud needs raw Scout records/context.

### Scout remains model-independent

Agent consumers may use AI, but deterministic Scout context/provenance remains distinct from downstream model work.

## Public SystemManifest

Candidate fields:

- component/product/release;
- supported public contract/API versions;
- tenant scope model;
- capability catalogue ref;
- state snapshot ref;
- public Discovery refs;
- upgrade-compatibility version;
- docs refs.

No private product detail.

## Public CapabilityDescriptor

Useful groups:

- context read;
- provenance read;
- relationship/context queries;
- connector/source inspection;
- approved import/ingest;
- audit/admin reads;
- migration/upgrade handoff;
- public discovery metadata.

Each descriptor states stable ID/version, endpoint/SDK ref, auth/scope, read vs mutation, bounds, freshness, idempotency/retry and verification.

## State snapshot

Compact public runtime state may include release, caller-appropriate tenant/environment binding, API/datastore/dependency readiness, connector/source health summary, recent ingest refs, context freshness, upgrade compatibility and blockers.

No secrets/raw source data.

## Public Discovery MCP

The public Scout Discovery MCP should expose/reference manifest, capability catalogue, connector metadata and safe runtime state where authenticated/appropriate.

It becomes an inspection adapter over authoritative metadata, not a separate semantic store.

## Discovery Agent

The generic repository Discovery Agent should treat `SYSTEM.md` as a first-class orientation file.

Runtime capability questions should prefer manifest/API metadata over repository discovery.

## Operation receipts

Candidate operations: import/ingest batch, migration/upgrade export, connector validation and material admin operation.

Public-safe receipts include run ID, version, scope, input refs/hashes, counts, result/error, timestamps and verification/audit refs.

## Error semantics

Distinguish invalid input, unauthorised/forbidden, wrong tenant/scope, dependency unavailable, stale/not found, capacity/limit exceeded, partial operation, version conflict and verification failure.

## Acceptance tests

- no private internals in manifest/capabilities;
- descriptors match real public routes/contracts;
- tenant/scope requirements explicit;
- reads/mutations distinguishable;
- state has no secrets/raw source records;
- Discovery MCP returns bounded authoritative metadata;
- model-independent Scout context remains clear;
- upgrade/migration capability is versioned/testable.

## First slice

1. public JSON Schemas for manifest/capability/state;
2. generated manifest;
3. descriptors for context read, source inspection and migration handoff;
4. read-only state endpoint;
5. expose via public Discovery MCP;
6. contract-parity tests across API/SDK where applicable.
