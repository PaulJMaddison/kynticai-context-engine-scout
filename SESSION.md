# KynticAI Scout Engineering Session

## Last Updated

2026-08-15 — Scout source capture, exact replay evidence and sovereign Scout -> Fortress continuity engineering.

## Branch / status

- Repository: `PaulJMaddison/kynticai-context-engine-scout`
- Branch: `chatgpt/fortress-upgrade-compatible-capture`
- Starting `main`: `49f352bfded6452f5ede6c49a1996282368dacae`
- Status: **AUTHORED / NOT RUNTIME-GREEN** until local .NET build/tests/EF/PostgreSQL proof passes.
- GitHub Actions are intentionally not part of this work. Do not add/re-enable paid Actions merely to validate this branch.

Code/runtime truth wins over prose. Read this file together with `LOCAL_VALIDATION.md`, the matching Fortress continuity-branch `SESSION.md`, and the current `kyntic-ucl-local-aidocs` continuity state before changing contracts.

## Permanent product boundary

Scout must be **capture-rich but engine-simple**. Fortress must be **governance/identity/relationship/temporal rich**. Changing licence tier must not force a customer to throw away connector configuration or reconnect systems when the local connector contract is compatible.

Target data plane:

```text
customer source
    -> customer-local connector installation + secret reference
    -> customer-local capture lease/checkpoint
    -> customer-local retained source journal + exact payload evidence
    -> Scout lightweight derivations
```

Upgrade target:

```text
same customer source
    -> same connector installation / config / local secret reference
    -> same customer-local PostgreSQL substrate
    -> retained Scout source truth + exact payload evidence
    -> Fortress commercial governed ingress
    -> Fortress identity/relationship/outcome/temporal truth
    -> rebuildable Lance/vector indexes
```

Scout `ContextFact`, selector confidence, context snapshots and fallback relationship scores are **derived Scout output**. They are not promoted into Fortress canonical truth. Fortress rebuilds from retained source evidence.

## Sovereign-data rule

Raw source records, exact payload evidence, customer identifiers, source positions, connector credential values, governed packets, vectors and prompts remain inside the customer data plane.

KynticAI Cloud/control-plane may receive only explicitly bounded product/control metadata such as installation ID, tier/licence state, software version, aggregate health and billing counters. The upgrade JSONL described below is customer data and is **never** a Cloud/support upload artifact.

## Coverage and history are orthogonal

Never collapse these two axes.

Coverage:

- `SUBJECT_ON_DEMAND` — rows Scout happened to request for a subject;
- `FULL_SOURCE` — complete customer-permitted projection enumerated by a whole-source connector generation;
- `SNAPSHOT_IMPORT` — supplied complete snapshot/file import.

History:

- `COMPLETE` — source-native complete ordered history has been proved;
- `FROM_RETENTION_BOUNDARY` — exact ordered history only from an explicit boundary;
- `SNAPSHOT_ONLY` — current/snapshot state, not ordered change history;
- `ON_DEMAND` — partial history from subject reads;
- `UNKNOWN` — insufficient proof.

`full-permitted.v1` means every field the customer explicitly authorised this connector to retain after its allow-list/redaction/minimisation policy. It never means every field technically exposed by the source.

A full current-state capture can therefore be genuinely useful and seamless for upgrade while still being historically `SNAPSHOT_ONLY`.

## Implemented continuity architecture

### Selector continuity journal

`UpgradeCapturingSelectorExecutionEngine` + `LocalSourceCaptureJournal` persist successful live selector source reads into `SourceSystemEvent`. Preview/dry-run do not create durable history. These rows are explicitly `SUBJECT_ON_DEMAND / ON_DEMAND` and cannot establish estate-wide continuity.

### Whole-source capture

`IUpgradeSourceCaptureConnector`, `FullSourceCaptureCoordinator`, `ConnectorCaptureCheckpoint` and SQL/REST/CSV adapters form a separate whole-source path.

The checkpoint owns:

- connector installation/data-source identity;
- continuation token;
- source high-water metadata;
- coverage/history class;
- earliest available/captured boundary;
- generation count;
- local lease owner/expiry;
- last error;
- `PayloadStorageContract` for the last completed generation.

The coordinator is registered but is **not** an automatic background poller yet. Do not start two source owners before lease/cutover proof is green.

### Connector claim hardening

The branch fails closed on continuity claims:

- generic SQL/list REST and CSV snapshots do not become exact historical CDC merely because configuration says so;
- generic REST full-response retention requires explicit `retainEntireResponseObject=true`;
- each page uses one known history class;
- paged generation semantics cannot silently change mid-generation;
- payload/schema/permitted-field hashes must be valid;
- raw payload SHA-256 is recomputed before persistence;
- `FROM_RETENTION_BOUNDARY` requires an explicit earliest source boundary;
- source position must be structured JSON;
- the full-permitted capture profile is mandatory.

### Lease correctness

`LeaseOwner` and `LeaseExpiresAtUtc` are optimistic concurrency tokens. A source call may be slow, so the coordinator renews the lease **before** writing returned rows; an expired lease causes failure rather than silent reacquisition. This lease/checkpoint is the intended Scout -> Fortress connector-ownership barrier.

### Empty-source correctness

Zero rows are never proof that a source is empty. An empty source is upgrade-safe only after a completed `FULL_SOURCE` generation independently proves enumeration/coverage and its history/storage semantics.

## Exact payload evidence — critical 2026-08-15 decision

### Problem discovered

`SourceSystemEvent.PayloadJson` is PostgreSQL `jsonb` on the production path. Scout originally calculated `RawPayloadSha256` from connector JSON text **before** persistence. PostgreSQL jsonb is allowed to normalise textual representation. Therefore a later database round-trip may preserve identical JSON semantics while producing different bytes/text.

Consequences if left unfixed:

- Fortress could falsely report payload corruption during upgrade;
- or engineers might weaken the hash check and lose byte-verifiable provenance.

### Chosen design

Do **not** change Scout's existing semantic `PayloadJson` contract merely for upgrade replay. Keep two explicit representations:

```text
SourceSystemEvent.PayloadJson
    = Scout-friendly semantic JSON/jsonb

SourceCapturePayloadEvidence.ExactPayloadText
    = exact customer-permitted JSON text used for RawPayloadSha256
```

New entity/table: `source_capture_payload_evidence`.

It stores, customer-locally:

- tenant;
- source event ID;
- connector installation ID;
- `StorageContract`;
- coverage scope;
- exact payload text;
- raw payload SHA-256.

Evidence is **event-scoped**, not generation-scoped. The same deterministic event may be encountered in later full-source generations without becoming a duplicate historical event.

### Persistence invariant

`ScoutDbContext` now treats exact evidence as a persistence invariant for newly added capture events:

1. parse `kynticCapture`;
2. require supported capture contract/connector/coverage/hash metadata;
3. recompute SHA-256 from the in-memory exact `PayloadJson` text;
4. fail if the declared hash differs;
5. stamp `PayloadStorageContract = exact-text.v1` into the persisted capture envelope;
6. add one `SourceCapturePayloadEvidence` sidecar in the same SaveChanges transaction.

Ordinary non-capture `SourceSystemEvent` rows do not receive a sidecar.

### Deterministic legacy repair

A pre-sidecar full-source event can already exist with the same deterministic `capture:<idempotency>` EventId. A later full-source recapture now repairs it safely:

- do not insert a duplicate source event;
- use the newly recaptured connector payload as exact evidence, not the old jsonb round-trip text;
- verify contract, connector, data-source, idempotency key and raw hash match the existing event metadata;
- attach exact-text evidence to the existing event;
- stamp its capture envelope `exact-text.v1`;
- if existing exact evidence contradicts the recapture, fail closed and never overwrite it.

This lets an existing Scout estate move to byte-verifiable upgrade evidence by running a new local full-source generation, without reconnecting the source.

## Upgrade readiness must require exact evidence

`ConnectorCaptureCheckpoint.PayloadStorageContract` starts legacy/unknown and a completed new full-source generation records `exact-text.v1`.

`ScoutUpgradeCompatibilityService` now requires exact payload evidence in addition to coverage, history, connector support and local credential references before advertising the strongest upgrade readiness. Descriptor/manifests include `PayloadStorageContract`.

The metadata-only preflight remains intentionally bounded; it does not scan millions of payloads. **The exporter below is the exhaustive evidence gate.** A bad/missing sidecar therefore cannot reach Fortress merely because a checkpoint flag says exact.

## Sovereign Scout upgrade exporter

New normal-solution project:

`tools/KynticAI.Scout.UpgradeExport`

It connects only to the customer-local Scout PostgreSQL database and refuses export unless every connector installation has a completed `FULL_SOURCE` + `exact-text.v1` checkpoint.

It then joins `source_system_events` to `source_capture_payload_evidence` and streams a JSONL handoff using `ExactPayloadText`, never `PayloadJson` recovered from jsonb.

For every row it rechecks:

- exact payload SHA vs sidecar;
- capture contract;
- FULL_SOURCE coverage;
- exact-text storage declaration;
- capture raw hash.

Output:

```text
<name>.jsonl
<name>.jsonl.manifest.json
```

The manifest (`kyntic-scout-source-journal-export.v1`) contains only handoff integrity/control metadata: row count, whole-file SHA-256, connector types, payload-storage contract and sovereign flags. The JSONL itself contains exact customer-permitted source payloads and must remain local.

The exporter fails if any retained FULL_SOURCE capture lacks exact evidence; it never silently exports a partial estate.

## Matching Fortress validation contract

The matching Fortress branch independently requires `PayloadStorageContract == exact-text.v1` in metadata preflight. Old/missing/legacy-jsonb manifests are `HISTORY_LIMITED`, even if Scout advisory readiness is wrong.

Fortress `scout-journal-validate` then performs the exhaustive local handoff check:

- official export manifest required by default;
- whole JSONL SHA-256;
- row count and connector set;
- sovereign flags / no credential material;
- exact-text contract;
- row raw-payload hash;
- capture/event/idempotency/source/data-source consistency;
- connector/position pairing;
- FULL_SOURCE origin;
- history fidelity.

Unmanifested fixtures require an explicit development override and are not customer cutover material.

## Source-position / replay classes

Do not invent one fake global source sequence.

- SQL full snapshot: `sql-full-snapshot`, current-state import only;
- CSV snapshot: `csv-snapshot`, current-state import only;
- REST page snapshot: `rest-page-snapshot`, current-state import only;
- REST/source-native ordered position: may support exact history, but an opaque native token needs a connector-specific monotonic ordering mapper before Fortress execution.

Hashing an opaque cursor into `u64` is **not** ordering and is forbidden.

## Same-PostgreSQL cutover target

1. Upgrade Scout binaries additively; leave connector installations, data sources and local secret references in place.
2. Complete a fresh FULL_SOURCE continuity generation under `exact-text.v1`.
3. Generate Scout metadata preflight and explain any `HISTORY_LIMITED` source classes.
4. Pause/acquire connector leases and record high-water barrier.
5. Take a customer-local PostgreSQL backup/snapshot.
6. Apply additive Fortress-owned storage/state changes. Do not delete Scout capture tables.
7. Run customer-local Scout upgrade export.
8. Run Fortress export/journal validation; zero customer payload leaves the local data plane.
9. Replay validated source truth through **the same Fortress commercial governed-ingress semantics used by live traffic**, not a parallel identity engine.
10. Build/reconcile derived Lance/vector state only after governed state commits.
11. Catch up/take over source ownership from the recorded barrier according to connector capability.
12. Verify source counts, hashes, identity, checkpoints, outbox, restart and canary queries.
13. Resume the same compatible connector installation/config/secret references under the new owner.
14. Finalise only after continuity proof; otherwise rollback to the local backup/old owner.

## What “no data loss” honestly means

For a source with a durable ordered change feed, target is exact continuous cutover from the proven retention boundary.

For snapshot-only sources, the upgrade can preserve all **captured current-state source data and configuration** without reconnecting, but Scout cannot manufacture historical mutations/deletions it never observed. Where writes can occur during a long snapshot/cutover, a final recapture/write-freeze/provider-specific CDC strategy may be required to prove no change-gap.

That is not licence-upgrade data loss; it is a source/capture capability boundary. It must be surfaced to the customer as `HISTORY_LIMITED`/snapshot continuity rather than hidden behind the word “lossless”.

## Migration state

Pending migration:

`20260815221500_ConnectorCaptureCheckpoints.cs`

It currently includes:

- `connector_capture_checkpoints`;
- `PayloadStorageContract`;
- `source_capture_payload_evidence` with text exact payload storage and source-event FK/indexes.

This migration is unreleased and **not runtime-green**. `ScoutDbContextModelSnapshot.cs` must be regenerated/reconciled with normal EF tooling before merge. Do not create a chain of compensating migrations while the initial migration is still unvalidated.

## Immediate OpenCode/Codex validation package

The next local agent must **compile/fix**, not redesign:

1. `dotnet restore/build KynticAI.Scout.slnx` with warnings treated as errors by repo policy.
2. Fix any compile errors from exact evidence, checkpoint contract and `KynticAI.Scout.UpgradeExport` in the same session.
3. Add/run focused tests for capture-sidecar transactionality, mismatch failure, non-capture behavior, deterministic duplicate repair, contradictory evidence, readiness policy and empty-source behavior.
4. Run `dotnet tool restore`, inspect EF migrations, regenerate/reconcile migration + `ScoutDbContextModelSnapshot`.
5. Prove PostgreSQL migration/up/down on disposable local DB; prove supported SQLite model behavior if Scout still promises SQLite locally.
6. Run a tiny FULL_SOURCE SQL/REST/CSV fixture and ensure exact evidence is created.
7. Run `KynticAI.Scout.UpgradeExport`; verify aggregate-only console output, JSONL SHA and manifest.
8. Hand the JSONL directly to the matching Fortress validator and prove tamper/truncation/missing-sidecar/legacy-jsonb cases fail.
9. Update this SESSION and aidocs with actual commands/results; do not write “green” from authored code alone.

No GitHub Actions. No large 100k/1m/10m run to discover compiler errors.

## Remaining engineering work after build green

- finish executable Fortress governed-state backfill for snapshot imports using explicit `SourceOperation::Snapshot` semantics rather than pretending snapshots are source creates;
- add provider-specific ordered-position mappers/change feeds for exact SQL/REST temporal continuity where valuable;
- make connector ownership transfer an executable local cutover state machine with rollback/canary evidence;
- solve production-approved encrypted/transactional Fortress `CommercialStateStore` and key management; plaintext development journals must remain opt-in only;
- prove final-snapshot/tail strategy for mutable snapshot-only sources;
- retention/storage-pressure policy that moves earliest exact history honestly;
- schema drift compatibility and connector-definition version migration;
- local control-plane egress deny-by-default proof.

## Do not do

- do not put proprietary Fortress weighting/identity/decision algorithms into Scout;
- do not call subject reads whole-source coverage;
- do not call snapshot pagination exact temporal history;
- do not use jsonb round-trip text as byte-level replay evidence;
- do not weaken exact payload hashes just to make an export pass;
- do not send the JSONL, raw payloads, IDs, credentials, vectors or governed packets to KynticAI Cloud;
- do not silently reconnect sources and call the upgrade seamless;
- do not delete Scout source/connector/checkpoint/evidence tables during Fortress rebuild;
- do not call SQLite a proven same-database Fortress production substrate;
- do not run large/cloud/model proofs before build, migration and tiny local continuity gates pass.
