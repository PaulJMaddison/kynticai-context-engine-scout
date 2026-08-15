# KynticAI Scout Engineering Session

## Last Updated

2026-08-15 — Scout source-capture / sovereign Scout -> Fortress continuity engineering.

## Branch / baseline

- Repository: `PaulJMaddison/kynticai-context-engine-scout`
- Branch: `chatgpt/fortress-upgrade-compatible-capture`
- Starting `main` commit: `49f352bfded6452f5ede6c49a1996282368dacae`
- Status: **AUTHORED / NOT RUNTIME-GREEN** until local .NET compile/tests/migrations pass.

This file is public-safe. Do not place proprietary Fortress identity/scoring algorithms or private customer material in Scout.

## Current product decision

Scout should not be a deliberately lossy data collector that forces the customer to reconnect/re-ingest everything when they buy Fortress.

For connector types offered in Scout, the target architecture is:

`customer source -> local Kyntic connector capture -> local PostgreSQL source journal -> Scout simple derivations`

and after upgrade:

`same customer source -> same local connector installation/checkpoint/secret reference -> same PostgreSQL source journal -> Fortress governed engine + rebuildable indexes`

The tier changes the **engine capability**, not the customer's source connection.

Scout may use only a small semantic subset of a source payload for its own features. The capture plane should retain the complete **customer-permitted** source envelope locally so Fortress can later derive richer identity, chronology, relationships, outcomes and governed context without a destructive re-ingest.

`customer-permitted` always means after the customer's allow-list/redaction/minimisation/residency policy. It never means collecting fields the customer did not authorise.

## Important correction made this session: subject reads are not whole-source capture

The existing Scout selector runtime calls `IConnectorPlugin.FetchAsync` for one subject and then computes a selector result. Even if that returned payload is retained perfectly, it proves only that Scout stored everything it happened to ask for. It does **not** prove that Scout captured the whole CRM/database/API estate.

This distinction is now encoded explicitly:

- `SUBJECT_ON_DEMAND` — one subject read performed for Scout context calculation;
- `FULL_SOURCE` — connector capture covering the complete customer-permitted source projection;
- `SNAPSHOT_IMPORT` — complete supplied snapshot/file, but not necessarily change history.

History is separately classified as:

- `COMPLETE`;
- `FROM_RETENTION_BOUNDARY`;
- `SNAPSHOT_ONLY`;
- `ON_DEMAND`;
- `UNKNOWN`.

An event is strongly upgrade-compatible only when it is structurally complete, retains the full customer-permitted payload, has `FULL_SOURCE` coverage and can prove `COMPLETE` or `FROM_RETENTION_BOUNDARY` history.

This prevents a false `LOSSLESS` claim.

## Source journal

Scout already has the right local persistence primitive: `SourceSystemEvent`.

It stores the raw source payload plus local source/event metadata in the customer relational data plane. We are building upgrade continuity around that existing journal rather than creating another copy of the estate.

Scout derived artifacts such as `ContextFact`, `ContextSnapshot`, selector confidence and the basic relationship fallback are **not** promoted to Fortress canonical truth. Fortress rebuilds richer derived state from retained source truth.

## Code authored in this branch

### Local capture contracts

`LocalDataPlaneUpgradeContracts.cs` now defines:

- `kyntic-local-source-capture.v1`;
- `kyntic-scout-upgrade-manifest.v1`;
- `full-permitted.v1`;
- explicit coverage/history semantics;
- source object/record identity;
- operation;
- provider-native source position;
- occurred/source-recorded/ingested timestamps;
- schema fingerprint;
- redaction policy version;
- raw-payload hash;
- permitted-field-set hash;
- deterministic idempotency key;
- earliest source-history boundary.

### Selector-source journaling

`UpgradeCapturingSelectorExecutionEngine` decorates the existing `SelectorExecutionEngine`.

A successful live/scheduled/event-triggered connector read is passed to `LocalSourceCaptureJournal`, which persists the source payload into `SourceSystemEvent` before the read is allowed to exist only as Scout-derived context.

Preview and dry-run do not create durable history.

These records are deliberately marked `SUBJECT_ON_DEMAND / ON_DEMAND`. They are useful continuity evidence but are never treated as proof of full-estate coverage.

### Whole-source connector contract

`IUpgradeSourceCaptureConnector` is separate from `IConnectorPlugin.FetchAsync`.

This is intentional:

- `FetchAsync` answers a Scout selector question for a subject;
- `CaptureBatchAsync` preserves the whole customer-permitted source projection for long-lived Kyntic data continuity.

The contract carries raw payload, normalised payload, stable source record ID, operation, exact position, timestamps, schema/payload hashes, redaction policy, history semantics and continuation/high-water state.

### Whole-source capture checkpoint / lease

`ConnectorCaptureCheckpoint` is a customer-local durable cursor and ownership record.

It tracks:

- connector installation/data-source ID;
- capture profile/version;
- coverage/history classification;
- continuation token;
- local source high-water JSON;
- earliest source boundary;
- earliest/latest captured times;
- completed generation count;
- captured-record count;
- active lease owner/expiry;
- last error.

The lease is the future Scout -> Fortress cutover barrier. Two runtimes must not independently advance the same connector.

The corresponding EF configuration and `DbSet` have been authored. **An EF migration has not yet been generated/executed.**

### Whole-source capture coordinator

`FullSourceCaptureCoordinator`:

1. enumerates local connector installations;
2. finds a matching full-source adapter;
3. acquires the customer-local connector lease;
4. resolves local credential references without exporting secrets;
5. captures a bounded batch;
6. writes idempotent raw source events to the existing local journal;
7. advances cursor/high-water/capture counters;
8. marks completed generations;
9. releases the lease;
10. records failure locally if needed.

It is registered in DI but deliberately **not** started as an automatic hosted worker yet. First generate the migration, compile, add configuration validation and prove lease/restart semantics. Do not make a new background loop own customer connectors merely because the code compiles.

### Whole-source adapters authored

`SqlFullSourceCaptureConnector`

- captures every row in the customer-permitted column set;
- stable record ID comes from `sourceRecordIdColumn` or the existing user-ID column;
- supports the existing local/customer-ops/external PostgreSQL modes;
- pages a source snapshot;
- stores raw/schema/field hashes and deterministic idempotency;
- deliberately reports `SNAPSHOT_ONLY` history because generic SQL polling cannot prove inserts/updates/deletes over time.

This is useful for complete **current-state** capture and cutover, but it is not yet a substitute for PostgreSQL CDC/change-table history. Offset pagination is also not an exact moving-source algorithm; for production continuity use a frozen snapshot/barrier, keyset/watermark capture, or source-native CDC.

`RestFullSourceCaptureConnector`

- requires an explicit collection endpoint (`capturePathTemplate`) and stable record-ID path;
- supports cursor pagination;
- can read a source-native position/operation when configured;
- without a native source position it deliberately reports `SNAPSHOT_ONLY`;
- with a real provider change-feed position it may report `COMPLETE` or `FROM_RETENTION_BOUNDARY` according to the connector definition.

`CsvFullSourceCaptureConnector`

- captures the complete supplied row set;
- deterministic snapshot/row position and hashes;
- reports `SNAPSHOT_ONLY` because one CSV file is a state snapshot, not change history.

These adapters are registered as `IUpgradeSourceCaptureConnector` implementations.

## Upgrade manifest scalability fix

The original compatibility service loaded every retained `PayloadJson` and `HeadersJson` into application memory to build the manifest. That would be unacceptable for a large Scout estate.

`ScoutUpgradeCompatibilityService` has been rewritten to use:

- bounded connector-installation metadata;
- secret references only;
- `ConnectorCaptureCheckpoint` summaries;
- database-side source-event count/min/max aggregates;
- hashes of configuration/high-water state rather than raw values.

It no longer performs an application-level million-row payload scan just to answer "can this installation upgrade safely?".

The manifest does not export the raw high-water source position. It exports a hash/coverage status so control-plane/operator views do not become an accidental customer-data path.

## Same PostgreSQL upgrade decision

For production Scout on PostgreSQL, keep the same customer-local database/cluster where practical.

Think of it as the customer's **Kyntic local data-plane substrate**, not a Scout-owned disposable database.

Fortress should add its own schema/tables additively. Do not drop or rewrite the Scout source journal, connector installations, local credential references or capture checkpoints during cutover.

Preferred cutover:

1. complete a whole-source capture generation;
2. run local preflight;
3. acquire/pause connector lease at a recorded high-water barrier;
4. take a customer-local backup;
5. install Fortress additively on the same PostgreSQL substrate;
6. replay retained source truth through Fortress governed ingestion in source order;
7. build Fortress derived indexes from structured truth;
8. catch up any source tail after the barrier;
9. verify counts/hashes/checkpoints/canary queries;
10. transfer/resume the same connector lease;
11. switch the product query path to Fortress;
12. retain Scout source/connector tables for rollback/audit according to policy.

A customer should not have to type connector credentials again merely because their licence tier changed if the same local secret reference is still valid.

## What "no data loss" means

There are two different questions:

**Did the upgrade lose data?**

Target answer: no. Anything present in the retained Scout source journal at the barrier must survive/replay deterministically into Fortress or the cutover fails.

**Did old Scout versions previously collect every source event needed for Fortress history?**

Not necessarily. Old installations may have only selector/on-demand data or snapshots. That is not data lost by the upgrade; it is data Scout never captured. Those customers must be labelled `HISTORY_LIMITED` with an explicit earliest exact boundary.

For new Scout installations, the target is to enable full-source capture from day one so future Fortress upgrades retain all customer-permitted data from the declared retention boundary.

## Connector strategy from here

The long-term clean model is a **tier-neutral local Connector Host** bundled with Scout and Fortress.

The connector host owns source connectivity, full customer-permitted capture, local credential references, checkpoints and the source journal. Scout and Fortress are consumers of that local source truth at different capability levels.

This avoids maintaining one "small Scout connector" and a second unrelated "Fortress connector" for the same customer integration.

For each connector family we still need to decide its strongest honest capture profile:

- PostgreSQL/SQL: initial full snapshot + CDC/change feed or customer change table for exact ongoing history;
- REST SaaS/CRM: full collection sync + provider cursor/change feed/webhook when available;
- event/webhook source: exact event ID/sequence + operations/tombstones + local durable journal;
- CSV/file: full immutable snapshot/file version; temporal history only if versioned files are retained;
- future enterprise connectors: same full-source contract, provider-specific position semantics.

Fortress can include more connector types than Scout, but any connector type offered in both tiers should use the same capture semantics rather than a deliberately truncated Scout version.

## Sovereign boundary

Strict sovereign/self-contained mode keeps inside the customer environment:

- raw connector payloads/source journal;
- connection strings/tokens/private keys/credential values;
- connector cursors/high-water positions;
- identity/alias state;
- relationships/outcomes/temporal/governed facts;
- vectors/embeddings/LanceDB;
- prompts/context packets/local-model input/output;
- backups, dead letters and proof artifacts.

KynticAI Cloud may receive only licence/subscription/billing/version/update/installation/support routing and approved aggregate health/usage metadata. It is never the canonical customer data plane.

## Remaining implementation work before calling Scout upgrade-ready

1. **Compile/fix this branch.** Several directly authored files have not yet been compiled together.
2. Generate the EF migration for `connector_capture_checkpoints` and prove SQLite + PostgreSQL migrations/rollback.
3. Add focused tests for lease expiry, duplicate workers, checkpoint restart, idempotent recapture and incomplete generation recovery.
4. Review `FullSourceCaptureCoordinator` completion logic so mixed/weak history classifications fail closed rather than accidentally upgrading to a stronger history class.
5. Propagate each adapter's `EarliestAvailableAtUtc` into the checkpoint summary.
6. Add a controlled local command/admin operation to run/continue full-source capture. Do not start an automatic worker until this command is proven.
7. Extend connector configuration schemas/validation for the new full-source fields.
8. Advertise `UpgradeCompatibleCapture` only when the actual full-source adapter/config can support the claimed profile. A subject `FetchAsync` alone is not enough.
9. Add the event/webhook ingestion equivalent of `kynticCapture` so exact event-stream connectors can be genuinely `COMPLETE`/`FROM_RETENTION_BOUNDARY`.
10. For SQL/PostgreSQL, add/prove a source-native change stream or explicit customer change-table/watermark profile if exact temporal continuity is required. The current generic SQL adapter is snapshot-only by design.
11. Add provider-specific REST change-feed/webhook profiles rather than treating arbitrary collection polling as exact history.
12. Make retention explicit. If customer retention prunes raw source history, update the earliest exact boundary rather than keeping a stale lossless claim.
13. Add storage-pressure/backpressure behavior: quota, journal retention, disk-low fail-safe, capture pause and operator health without silently dropping records.
14. Add source schema-drift behavior: detect schema fingerprint changes, persist version, fail closed or continue under approved compatible policy.
15. Test delete/tombstone semantics for connectors that provide them.
16. Ensure PII/redaction policy changes create a new capture-policy version and never expose disallowed fields in an upgrade replay.
17. Run the same-PostgreSQL Scout -> Fortress proof only after both repos are build-green.

## Validation order for OpenCode/Codex

Do not use a large cloud run to discover compiler errors.

1. restore/build Scout;
2. focused unit tests for new contracts/checkpoint/adapters/decorator/manifest;
3. EF migration generation + SQLite smoke;
4. PostgreSQL integration tests;
5. local whole-source 1k fixture capture/restart;
6. local Scout -> Fortress same-DB cutover proof;
7. private disposable GCP 1k then 10k proof;
8. larger scale only after correctness/continuity is green.

## Do not do

- Do not put proprietary Fortress engine logic in Scout.
- Do not export protected credential values or source high-water positions to Cloud.
- Do not call subject-on-demand capture whole-source coverage.
- Do not call snapshot polling exact temporal history.
- Do not silently reconnect a source and call the upgrade seamless.
- Do not delete Scout source/connector tables during Fortress backfill.
- Do not claim SQLite is a proven same-database Fortress substrate; migrate/import locally to PostgreSQL first.
- Do not mark this branch runtime-green until builds/tests/migrations actually pass.
