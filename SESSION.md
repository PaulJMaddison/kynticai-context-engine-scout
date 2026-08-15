# KynticAI Scout Engineering Session

## Last Updated

2026-08-16 — Scout source capture, exact replay evidence, snapshot-generation membership and sovereign Scout -> Fortress continuity engineering.

## Branch / status

- Repository: `PaulJMaddison/kynticai-context-engine-scout`
- Branch: `chatgpt/fortress-upgrade-compatible-capture`
- Starting `main`: `49f352bfded6452f5ede6c49a1996282368dacae`
- Status: **AUTHORED / NOT RUNTIME-GREEN** until local .NET build/tests/EF/PostgreSQL proof passes.
- GitHub Actions are not a validation dependency for this branch.

Code/runtime truth wins over prose. Read this file with `LOCAL_VALIDATION.md`, the matching Fortress branch `SESSION.md`, and the latest continuity note in `PaulJMaddison/kyntic-ucl-local-aidocs`.

## Permanent product boundary

Scout is capture-rich but engine-simple. Fortress is the richer governed identity/relationship/outcome/temporal/decision engine. Changing licence tier must not force a compatible customer source to be reconfigured or reconnected.

Preferred continuity architecture for an existing Scout installation:

```text
customer source
    -> SAME customer-local connector installation
    -> SAME connector config / local Data Protection key ring / credential resolver
    -> SAME customer-local PostgreSQL data-plane substrate
    -> retained source journal + exact payload evidence + capture generation state
    -> Scout lightweight engine before upgrade
    -> Fortress governed engine after upgrade
```

The immediate product design is therefore **add Fortress over the existing local connector/data plane**, not decrypt/re-encrypt Scout connector credentials into Rust during cutover. Fortress already has a separate .NET Enterprise connector/vault layer for new/advanced connectors; existing Scout connectors can remain on their existing local credential path until a later shared-vault migration is intentionally designed and proved.

Scout `ContextFact`, context snapshots, fallback relationship scores and agent output are derived Scout artefacts. Fortress never promotes them to canonical truth; it rebuilds from retained source evidence.

## Sovereign boundary

Raw source rows, exact payload evidence, customer identifiers, source positions, connector credential values, governed state, vectors, prompts and local-model traffic stay in the customer data plane. KynticAI Cloud receives only explicitly bounded control metadata. The Scout upgrade JSONL is customer data and must never become a Cloud/support upload artefact.

## Three independent fidelity axes

Do not collapse these into one `lossless` flag.

### Coverage

- `SUBJECT_ON_DEMAND`
- `FULL_SOURCE`
- `SNAPSHOT_IMPORT`

### Historical fidelity

- `COMPLETE`
- `FROM_RETENTION_BOUNDARY`
- `SNAPSHOT_ONLY`
- `ON_DEMAND`
- `UNKNOWN`

### Current-state consistency

- `IMMUTABLE_SNAPSHOT`
- `POINT_IN_TIME`
- `SOURCE_NATIVE_ORDERED`
- `LIVE_KEYSET`
- `API_CURSOR`
- `UNKNOWN`

`FULL_SOURCE` says the customer-permitted projection was enumerated. It does not by itself say the enumeration was one point in time or that historical mutations/deletions were retained.

## Exact payload evidence

`SourceSystemEvent.PayloadJson` remains Scout semantic JSON/jsonb. PostgreSQL jsonb may normalise text, so byte-level replay evidence is kept separately in `SourceCapturePayloadEvidence.ExactPayloadText` with SHA-256 and `exact-text.v1`.

`ScoutDbContext` makes this a persistence invariant for capture events: validate the capture envelope, recompute the hash from the in-memory exact payload, stamp `exact-text.v1`, and insert the sidecar in the same `SaveChanges` transaction.

Deterministic recapture can repair older jsonb-only capture rows without creating duplicates. It attaches exact recaptured text to the existing deterministic event only when connector/data-source/idempotency/hash evidence agrees. Contradiction fails closed.

## Tier-neutral source identity

`SourceNamespace` for whole-source capture is now:

```text
kyntic-connector:<connector-installation-guid>
```

Do not use connector type (`sqlDatabase`, `restApi`, etc.) as the source namespace. Two independent systems can both contain `contact/123`; connector-installation identity is the stable namespace preserved across the tier change.

## Public whole-source connector semantics

### SQL

- explicit `captureFullPermittedPayload=true`;
- explicit `captureColumns`; normal Scout selector `columns` are not reused as an implicit continuity projection;
- explicit `sourceRecordIdColumn` + `sourceRecordIdIsUnique=true`;
- typed keyset pagination, never OFFSET;
- `HistoryCompleteness = SNAPSHOT_ONLY`;
- `CurrentStateConsistency = LIVE_KEYSET`.

Keyset avoids offset-shift skips but does not turn a mutable table into a point-in-time snapshot. A final recapture/write freeze or provider-native change feed is required before zero mutation-gap cutover can be claimed.

### REST

- explicit full-permitted retention and `retainEntireResponseObject=true`;
- explicit collection endpoint and stable source-record ID;
- cursor-loop protection;
- generic REST forcibly remains `SNAPSHOT_ONLY` even if an API exposes an opaque source token;
- `CurrentStateConsistency = API_CURSOR`.

Provider-specific ordering/change-feed semantics require a provider-specific connector.

### CSV

- explicit full retention and unique source-record ID;
- complete row-set SHA pinned into the continuation token;
- changing rows between pages fails the generation;
- `HistoryCompleteness = SNAPSHOT_ONLY`;
- `CurrentStateConsistency = IMMUTABLE_SNAPSHOT`.

Only SQL/REST/CSV are registered as `IUpgradeSourceCaptureConnector` on this branch. Mock/template/in-memory subject connectors are not silently advertised as whole-source continuity connectors.

## Critical anti-resurrection rule — 2026-08-16

A retained snapshot event and a current source record are **not the same fact**.

Failure case discovered:

```text
generation 1: A, B
generation 2: A       // B was deleted at source
```

Scout correctly retains the old generation-1 event for B. If Fortress replayed every retained FULL_SOURCE event, B would be resurrected even though the latest completed snapshot no longer contains it.

### Chosen design: generation membership

New entity/table:

`SourceCaptureGenerationMember` / `source_capture_generation_members`

It records:

- tenant;
- connector installation;
- positive capture generation;
- retained source event ID;
- tier-neutral source namespace;
- source object type;
- source record ID.

Unique source key inside one generation:

```text
Tenant + ConnectorInstallation + Generation + SourceObjectType + SourceRecordId
```

The same unchanged retained event may belong to generation 1 and generation 2. That is expected. Two different events claiming the same source key in one generation fail closed.

The in-flight generation is `checkpoint.Generation + 1`; it stays stable across pages/retries and becomes authoritative only when the checkpoint completes.

Completed checkpoints stamp:

`GenerationMembershipContract = generation-membership.v1`

This marker is essential. An old pre-membership checkpoint with zero membership rows must never be interpreted as a genuinely empty new generation.

### Empty-source correctness

`ConnectorSourceCaptureBatch` now carries batch-level `HistoryCompleteness` and `CurrentStateConsistency`. These are recorded even when the source returns zero rows. Therefore a genuinely empty source can complete a real generation without inventing a synthetic source record.

Zero membership rows mean `source is proven empty for generation N` only when the completed checkpoint also carries `generation-membership.v1`.

## Upgrade readiness

The metadata preflight now independently considers:

- completed `FULL_SOURCE` generation;
- full-permitted capture profile;
- exact-text payload evidence;
- historical fidelity;
- current-state consistency;
- generation-membership contract;
- target connector support;
- local connector/credential continuity.

Missing generation membership or weak/unknown current-state consistency cannot reach the strongest readiness class.

Snapshot-only history remains honestly `HISTORY_LIMITED`; generation membership solves safe current-state reconstruction, not missing historical mutations.

## Official local export contract v2

`tools/KynticAI.Scout.UpgradeExport` now emits:

`kyntic-scout-source-journal-export.v2`

V2 is intentionally incompatible with the old v1 selection semantics.

For current public snapshot connectors, v2 exports **only the membership of each connector's latest completed generation** by joining:

```text
connector_capture_checkpoints
    -> source_capture_generation_members at checkpoint.Generation
    -> source_system_events
    -> source_capture_payload_evidence exact-text.v1
```

Older snapshot generations remain in Scout for bounded audit/history. They are not current-state replay input.

The v2 manifest includes per-connector:

- connector installation ID/type;
- selected generation;
- history class;
- current-state consistency;
- generation-membership contract;
- member count.

The exporter verifies total selected membership count equals emitted exact-evidence rows. A correctly empty source therefore has `memberCount = 0` and still appears in the manifest.

Current v2 exporter deliberately supports `SNAPSHOT_ONLY/UNKNOWN` selection only. Provider-specific exact ordered history will require its own explicit export/replay contract rather than being guessed into this one.

## Fortress v2 target boundary

The matching Fortress branch has a new `scout-snapshot-validate` binary for v2. It independently checks:

- v2 export contract;
- whole-file SHA and row count;
- `exact-text.v1`;
- `generation-membership.v1`;
- sovereign/no-credential flags;
- per-connector selected generation/member count;
- every row belongs to the selected generation;
- no duplicate source key inside the export;
- exact payload hash;
- connector-installation source namespace;
- row/header connector/object/record/data-source identity.

Important: source operation inside the Scout capture envelope is provenance only for v2. A REST record may say `update`, but if it is selected from a `SNAPSHOT_ONLY` latest generation, Fortress must apply it as **`SourceOperation::Snapshot` current state**, not invent temporal update history.

`ucl-identity` already contains `SourceOperation::Snapshot` and treats it as active state. The remaining target implementation seam is exposing that existing operation through the normal `ucl-governed-ingress` commercial path. Do not create a parallel identity engine and do not disguise snapshots as creates/updates.

## Same-PostgreSQL cutover target

1. Run compatible Scout local connector/data plane normally.
2. Complete a fresh full-source generation under `exact-text.v1 + generation-membership.v1`.
3. For LIVE_KEYSET/API_CURSOR sources, establish final recapture/write freeze or an exact change-feed barrier before claiming zero mutation gap.
4. Pause connector ownership at the customer-local barrier.
5. Take local PostgreSQL backup/snapshot.
6. Install Fortress state additively; do not delete Scout capture/evidence/membership/credential/checkpoint state.
7. Generate Scout v2 local export.
8. Run Fortress v2 snapshot validator.
9. Import selected rows through the same commercial governed-ingress authority using `SourceOperation::Snapshot`.
10. Reconcile absence: records not present in the newest completed snapshot must not survive from an older snapshot generation.
11. Build derived indexes only after governed state commits.
12. Verify hashes/counts/source identities/outbox/restart/canary state.
13. Resume the same local connector installation/config/credential path when compatible.

## Migration state

Pending unreleased migration `20260815221500_ConnectorCaptureCheckpoints.cs` now contains:

- `connector_capture_checkpoints`;
- current-state consistency;
- exact payload storage contract;
- generation-membership contract;
- `source_capture_payload_evidence`;
- `source_capture_generation_members` and indexes/FK.

It is **not green** until normal EF tooling regenerates/reconciles `ScoutDbContextModelSnapshot.cs` and both PostgreSQL + supported SQLite behavior are proved. Do not add compensating migrations to an unreleased migration before that validation.

## Focused tests authored

`ScoutUpgradeGenerationMembershipTests` covers:

- positive-generation invariant;
- same retained event may belong to later generation;
- missing membership proof fails readiness closed;
- genuinely empty estate also needs membership proof;
- LIVE_KEYSET/API_CURSOR are not strong point-in-time claims.

The Fortress v2 validator includes focused tests for old-generation rejection and connector-namespace mismatch, including a REST provenance `update` that is still selected as snapshot reconstruction.

## Immediate local validation package

Do not redesign first. Compile and repair this authored branch:

1. `dotnet restore .\KynticAI.Scout.slnx`
2. `dotnet build .\KynticAI.Scout.slnx`
3. `dotnet test .\tests\KynticAI.Scout.UnitTests\KynticAI.Scout.UnitTests.csproj`
4. build/run `tools/KynticAI.Scout.UpgradeExport`;
5. regenerate/reconcile EF migration + model snapshot;
6. prove PostgreSQL migration/up/down and supported SQLite model path;
7. run tiny SQL/REST/CSV fixtures including a genuinely empty source;
8. prove generation 1 `{A,B}` then generation 2 `{A}` exports only A;
9. prove incomplete generation N+1 membership does not affect export while checkpoint.Generation remains N;
10. prove old checkpoint with membership contract UNKNOWN is rejected;
11. pass v2 JSONL + manifest to Fortress `scout-snapshot-validate`;
12. tamper hash/generation/member count/namespace and prove fail-closed behavior.

No GitHub Actions, Qwen, cloud or 100k/1m/10m work to discover compiler errors.

## Known remaining gaps

- expose explicit `SourceOperation::Snapshot` through normal Fortress governed ingress and build the v2 backfill executor;
- target-side absence reconciliation when backfill is applied to a non-empty/restarted target, not only a fresh Fortress state;
- production scheduling/change-feed/tail behavior for the persistent local connector host;
- operator reset semantics for abandoned in-flight `checkpoint.Generation + 1` membership rows;
- credential-free connector semantics in readiness (a connector that needs no secret must not be treated as missing credentials);
- provider-specific exact-history export/replay contracts;
- production encrypted/transactional Fortress commercial state store/key management;
- executable lease/barrier/rollback/canary cutover state machine;
- retention/schema-drift/egress proof.

## Do not do

- do not replay every retained snapshot event;
- do not treat absence in an old generation as current state;
- do not invent a deletion timestamp for a row simply because it is absent from the newest snapshot;
- do not call FULL_SOURCE exact history;
- do not call LIVE_KEYSET/API_CURSOR point-in-time consistency;
- do not use jsonb round-trip text as byte-level evidence;
- do not use connector type as source namespace;
- do not migrate Scout secrets into Rust merely to make the licence upgrade work;
- do not send upgrade JSONL/raw payloads/IDs/credentials/vectors/governed state to Cloud;
- do not call authored code runtime-green before local compiler/migration/proof passes.
