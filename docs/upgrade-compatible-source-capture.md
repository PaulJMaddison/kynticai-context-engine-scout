# Upgrade-compatible local source capture

Scout is designed so a customer can move to a richer KynticAI tier without treating the upgrade as a new source-integration project.

This document is public-safe. It describes data continuity contracts only; it does not describe proprietary higher-tier identity, ranking or relationship algorithms.

## Capture principle

For a connector that advertises `UpgradeCompatibleCapture`, Scout retains the **full customer-permitted source payload** locally together with enough source metadata to replay the mutation deterministically later.

“Full customer-permitted” means after the customer's configured allow-lists, redaction, minimisation, residency and security policy. The capability is not permission to collect disallowed fields.

Scout may use only a subset of that payload for its own context features. Retention of the authorised source envelope prevents the tier boundary from becoming a destructive data boundary.

## v1 metadata contract

`kyntic-local-source-capture.v1` records:

- connector installation ID;
- connector definition version;
- capture profile/version;
- optional source namespace;
- source object type;
- source record ID;
- operation;
- provider-native source position/checkpoint;
- occurred/source-recorded/ingested timestamps;
- schema fingerprint;
- redaction-policy version;
- whether the full customer-permitted payload was retained;
- deterministic idempotency key.

The metadata is stored beside the existing local source-event payload; it does not copy payload data into the header.

Exact retained replay text is stored separately under `exact-text.v1` so PostgreSQL jsonb normalisation cannot change the byte representation used for SHA-256 verification.

## Whole-source generation contract

Snapshot-style connectors retain membership of each completed FULL_SOURCE generation under `generation-membership.v1`.

That makes current-state replay explicit. If generation 1 contains `A,B` and generation 2 contains only `A`, the upgrade export for generation 2 contains only `A`. Older retained evidence remains available locally but does not resurrect `B` into the current Fortress state.

A zero-row generation is considered a proven empty source only when the generation completed successfully and carries the generation-membership contract.

## Connector authoring rule

A connector must not advertise `UpgradeCompatibleCapture` unless its normal live/scheduled path can supply valid `ConnectorCaptureMetadata`.

Preview/dry-run paths should not create durable capture history unless their API explicitly says they do.

Provider-native source position should retain all coordinates needed to distinguish changes. For example a database transaction may require a major commit/WAL coordinate plus a mutation ordinal rather than one lossy scalar.

`FULL_SOURCE` is a coverage claim. It is not automatically a historical CDC or point-in-time consistency claim.

## Upgrade manifest

`kyntic-scout-upgrade-manifest.v1` is metadata only. It can report:

- installation/data-source/workspace IDs;
- connector type/status;
- configuration hash;
- local secret references;
- retained event counts/time coverage;
- capture profiles;
- schema fingerprints;
- upgrade readiness.

It must not contain protected credential values or raw source payloads.

## Durable cutover ownership

Source ownership is persisted separately from the metadata-only readiness manifest.

The cutover states are:

```text
ScoutActive -> ScoutPausedForCutover -> FortressOwned
```

For each connector, the durable pause binds:

- selected completed generation;
- snapshot completion timestamp;
- high-water SHA-256;
- cutover epoch;
- cutover-token SHA-256.

The raw cutover token is not persisted.

The normal capture worker checks ownership before and after durable checkpoint lease acquisition. A committed paused/Fortress-owned state therefore blocks credential retrieval and source I/O even when a worker was already waiting for the checkpoint lease.

## Customer-local upgrade export

`tools/KynticAI.Scout.UpgradeExport` produces `kyntic-scout-source-journal-export.v2`.

The exporter establishes the persistent `ScoutPausedForCutover` barrier **before** it reads export selection. It locks each connector checkpoint, refuses an active worker lease, persists the selected generation for the supplied cutover epoch/token hash, commits that barrier and then selects rows through the persisted ownership record.

This ordering is deliberate. Exporting first and pausing later would allow a fresh capture to complete between those operations and could hand Fortress a stale but internally valid snapshot.

If export fails after pause, Scout stays paused. A retry with the same epoch/token is deterministic; a different paused binding or `FortressOwned` binding cannot be overwritten.

The JSONL contains customer data and remains customer-local. It is never a KynticAI Cloud/support upload artefact.

## Historical boundary

Events captured before these contracts existed may still be valuable and may contain retained payload JSON, but Scout must not claim perfect future reconstruction when exact source order/operation/schema/capture policy cannot be proven.

The product therefore distinguishes complete upgrade-compatible history from `HistoryLimited` history.

## Validation required before capability is shipped

For each Scout connector:

1. live fetch returns full permitted payload;
2. capture metadata is complete;
3. exact replay gives the same idempotency key/position;
4. exact-text evidence hash matches retained bytes;
5. completed generation membership reconciles exactly;
6. anti-resurrection (`{A,B}` then `{A}`) exports only the newest completed generation;
7. source update/delete ordering is retained only where the source genuinely supplies it;
8. schema drift changes the fingerprint;
9. secret values never enter source headers/manifest/logs;
10. restart preserves checkpoint/capture/ownership state;
11. concurrent workers cannot overlap the committed cutover pause;
12. wrong cutover epoch/token and tampered hashes/counts fail closed;
13. manifest correctly identifies earliest exact upgrade-compatible history;
14. PostgreSQL migration/model state is clean;
15. the disposable-cloud 100k synthetic proof in `docs/testing/gcp-precloud-validation.md` passes before production cutover.

See `SESSION.md` and `LOCAL_VALIDATION.md` for the current implementation and proof status.
