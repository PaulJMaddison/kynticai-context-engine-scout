# KynticAI Scout Engineering Session

## Last Updated

2026-08-15 — Scout source-capture / sovereign Scout -> Fortress continuity engineering.

## Branch / baseline

- Repository: `PaulJMaddison/kynticai-context-engine-scout`
- Branch: `chatgpt/fortress-upgrade-compatible-capture`
- Starting `main`: `49f352bfded6452f5ede6c49a1996282368dacae`
- Current authored head at this update: includes `ed99c5e8a14b839500f60eaf7b5efe156e7790d4`
- Status: **AUTHORED / NOT RUNTIME-GREEN** until local .NET build/tests/EF validation pass.

Code/runtime truth wins over this document. This file is public-safe; do not place proprietary Fortress algorithms or private customer material in Scout.

## Product architecture

Scout must be data-capture rich but engine-simple. A customer upgrading to Fortress should not have to reconnect systems merely because the licence tier changes.

Target production continuity:

`customer source -> customer-local connector host/capture -> customer-local PostgreSQL source journal -> Scout derivations`

then:

`same source + same installation/checkpoint/secret reference -> same PostgreSQL source journal -> Fortress governed engine -> rebuildable derived indexes`

Scout derived facts, selector confidence, snapshots and fallback relationship scores are not Fortress canonical truth. Fortress rebuilds richer identity/relationship/outcome/temporal state from retained source truth.

## Coverage semantics

Subject selector reads and whole-source capture are deliberately different:

- `SUBJECT_ON_DEMAND` = the source rows Scout happened to request for a subject.
- `FULL_SOURCE` = the complete customer-permitted source projection covered by a connector capture generation.
- `SNAPSHOT_IMPORT` = complete supplied snapshot/file, not necessarily change history.

History is independently classified as `COMPLETE`, `FROM_RETENTION_BOUNDARY`, `SNAPSHOT_ONLY`, `ON_DEMAND` or `UNKNOWN`.

`full-permitted.v1` means the complete payload the customer explicitly authorised the connector to retain after allow-list/redaction/minimisation/residency policy. It never means every field the source can expose.

## Implemented on this branch

### Selector continuity

`UpgradeCapturingSelectorExecutionEngine` + `LocalSourceCaptureJournal` persist successful live selector source reads into the existing local `SourceSystemEvent` journal. Preview/dry-run do not create durable source history. These events are marked `SUBJECT_ON_DEMAND / ON_DEMAND` and never establish whole-estate losslessness.

### Whole-source capture

`IUpgradeSourceCaptureConnector`, `FullSourceCaptureCoordinator`, `ConnectorCaptureCheckpoint`, SQL/REST adapters and local journal persistence establish a separate whole-source path. The checkpoint owns continuation/high-water state, coverage/history classification, generation count, local lease ownership and failure state.

The whole-source coordinator is registered but is **not** an automatic hosted background worker. Capture ownership/cutover remains an explicit local operation until runtime proof is green.

### New fail-closed hardening on 2026-08-15

The coordinator now rejects weak or contradictory continuity claims instead of trusting connector configuration strings:

- generic `sqlDatabase` and `restApi` capture may claim only `SNAPSHOT_ONLY` or `UNKNOWN`; they cannot self-promote to `COMPLETE` / `FROM_RETENTION_BOUNDARY` without a provider-specific change-feed implementation;
- generic REST whole-source journaling requires `retainEntireResponseObject=true`, making full-response retention an explicit customer-permitted decision rather than an implication of selector/API access;
- each page must use one known history classification and a paged generation cannot silently change that classification;
- raw payload SHA-256 is recomputed and checked before persistence;
- schema/payload/permitted-field hashes must be valid SHA-256 hex;
- `FROM_RETENTION_BOUNDARY` requires an earliest available source timestamp;
- source position must be a JSON object;
- the full-permitted capture profile is required.

### Paged-generation correctness

`ConnectorCaptureCheckpoint.ObserveCaptureSemantics` records the last non-empty page's history classification and earliest source boundary. An empty terminal page therefore cannot reset a completed generation to `UNKNOWN` when the source size is an exact multiple of the batch size.

### Lease correctness

`LeaseOwner` and `LeaseExpiresAtUtc` are EF optimistic concurrency tokens. Competing local workers cannot both successfully persist ownership of the same checkpoint. After a potentially long source call the coordinator renews the lease **before** writing captured records; if the lease expired while waiting on the source, the batch fails instead of silently reacquiring and overlapping another owner.

This is the same ownership primitive intended for the Scout -> Fortress cutover barrier.

### Empty-source correctness

A fresh PostgreSQL installation with zero retained source rows is no longer automatically labelled lossless. Zero rows are safe only when a completed `FULL_SOURCE` generation independently proves capture coverage and the declared historical boundary. A legitimately empty but fully enumerated source may then be `LOSSLESS_DERIVED_REBUILD`; an uncaptured empty-looking database is `HISTORY_LIMITED`.

### Upgrade manifest scalability

`ScoutUpgradeCompatibilityService` uses checkpoint summaries plus database-side count/min/max aggregates. It does not load every retained payload/header into application memory. A completed generation is authoritative even if the source genuinely contains zero rows; history classification remains a separate gate.

The manifest exports bounded hashes/coverage metadata and local secret references, never raw source payloads, protected credential values or raw source high-water positions.

### EF migration

`20260815221500_ConnectorCaptureCheckpoints.cs` has been authored so the checkpoint table is represented by a discoverable EF migration before local validation.

**Important:** the generated `ScoutDbContextModelSnapshot.cs` has not been regenerated in this direct-GitHub session. Before merge, run the normal EF tooling against the branch and reconcile/regenerate the migration + snapshot so future migrations do not rediscover the table. Do not call migrations green until that is done against both the intended PostgreSQL path and the supported local SQLite path.

## Honest connector capability today

Generic SQL current-state enumeration is snapshot-only. Generic REST collection enumeration is snapshot-only/unknown unless a future provider-specific adapter proves a durable source-native change feed. Exact temporal continuity requires provider-specific CDC/change-log/cursor/webhook semantics.

The architectural target remains a tier-neutral local Connector Host shared by Scout/Fortress: source connectivity, customer-permitted capture, secret references, cursor/checkpoint ownership and source journal belong to the local data plane; engine sophistication belongs to the tier.

## Same-PostgreSQL cutover target

1. Complete and verify a whole-source capture generation.
2. Run local upgrade preflight.
3. Acquire/pause the connector lease at the local source high-water barrier.
4. Take a customer-local backup.
5. Install Fortress additively on the same PostgreSQL substrate.
6. Replay retained source truth through Fortress governed ingestion in source order.
7. Build/reconcile derived indexes only after structured state commits.
8. Catch up the source tail after the barrier.
9. Verify counts/hashes/checkpoints/canary queries/restart state.
10. Transfer/resume the same connector ownership and switch the query path.

Old Scout data that was never captured cannot be magically reconstructed. That is `HISTORY_LIMITED`, not upgrade data loss.

## Remaining gates

- Build the Scout solution and fix compile warnings/errors with warnings-as-errors enabled.
- Run focused tests for coverage policy, checkpoint semantics, duplicate/replay and lease concurrency.
- Regenerate/reconcile the EF migration + model snapshot; prove PostgreSQL and supported SQLite behaviour.
- Expose/validate the new whole-source configuration fields in connector configuration/UI, especially explicit REST full-response retention.
- Add controlled local operator/admin commands for capture/preflight/pause/resume; do not introduce an automatic connector worker before ownership proof.
- Prove restart/incomplete-generation recovery and duplicate worker behaviour against a real local PostgreSQL database.
- Add provider-specific exact change-feed profiles before claiming temporal completeness for SQL/REST.
- Add retention/storage-pressure/schema-drift handling that moves the earliest exact boundary honestly rather than silently dropping history.
- Run the tiny same-PostgreSQL Scout -> Fortress proof only after both repos are build-green.

## Do not do

- Do not put Fortress proprietary engine logic in Scout.
- Do not call subject reads whole-source coverage.
- Do not call generic snapshot polling exact temporal history.
- Do not export raw customer data, credentials or source positions to KynticAI Cloud.
- Do not silently reconnect a source and call the upgrade seamless.
- Do not delete Scout source/connector/checkpoint tables during Fortress backfill.
- Do not call SQLite a proven same-database Fortress production substrate.
- Do not run a large/cloud proof to discover compiler or migration errors.
