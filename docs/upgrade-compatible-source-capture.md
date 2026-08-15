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

## Connector authoring rule

A connector must not advertise `UpgradeCompatibleCapture` unless its normal live/scheduled path can supply valid `ConnectorCaptureMetadata`.

Preview/dry-run paths should not create durable capture history unless their API explicitly says they do.

Provider-native source position should retain all coordinates needed to distinguish changes. For example a database transaction may require a major commit/WAL coordinate plus a mutation ordinal rather than one lossy scalar.

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

## Historical boundary

Events captured before this contract existed may still be valuable and may contain retained payload JSON, but Scout must not claim perfect future reconstruction when exact source order/operation/schema/capture policy cannot be proven.

The product therefore distinguishes complete upgrade-compatible history from `HistoryLimited` history.

## Validation required before capability is shipped

For each Scout connector:

1. live fetch returns full permitted payload;
2. capture metadata is complete;
3. exact replay gives the same idempotency key/position;
4. source update/delete ordering is retained;
5. schema drift changes the fingerprint;
6. secret values never enter source headers/manifest/logs;
7. restart preserves checkpoint/capture state;
8. manifest correctly identifies earliest exact upgrade-compatible history.

See `SESSION.md` on the feature branch for current implementation status and remaining integration work.
