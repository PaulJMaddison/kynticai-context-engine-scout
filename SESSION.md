# KynticAI Scout Engineering Session

## Last Updated

2026-08-16 — exact local capture + latest-generation snapshot continuity into Fortress.

## Current branch truth

- Repository: `PaulJMaddison/kynticai-context-engine-scout`
- Branch: `chatgpt/fortress-upgrade-compatible-capture`
- Baseline `main`: `49f352bfded6452f5ede6c49a1996282368dacae`
- Current authored branch head before this documentation commit: `8ab918832a4e08cda124b4ec33b69db8c5935451`
- Compare state: **94 commits ahead / 0 behind** baseline.
- Status: **AUTHORED / NOT RUNTIME-GREEN** until local .NET build/tests/EF/PostgreSQL proof passes.

Code/runtime truth wins. Read this with the matching Fortress `SESSION.md`, `LOCAL_VALIDATION.md`, and the latest continuity note in `PaulJMaddison/kyntic-ucl-local-aidocs`.

## Product boundary

Scout is capture-rich and engine-simple. Fortress is the richer governed identity/relationship/outcome/temporal/decision engine.

For an existing compatible Scout customer the immediate continuity architecture is:

```text
SAME customer-local connector/data-plane service
SAME PostgreSQL
SAME connector installation IDs/config
SAME ASP.NET Data Protection key ring / credential resolver
        |
        +-> Scout lightweight engine before upgrade
        +-> Fortress governed engine after upgrade
```

Do not migrate Scout secrets into Rust merely because the licence tier changes. Scout derived context/snapshots/fallback relationship scores are not Fortress canonical truth.

## Sovereign boundary

Raw source rows, exact payload evidence, customer identifiers, source positions, connector credential values, governed state, vectors, prompts and model traffic stay local. The Scout upgrade JSONL is customer data and must never become a Cloud/support upload artefact.

## Capture fidelity is three separate things

### Coverage

- `SUBJECT_ON_DEMAND`
- `FULL_SOURCE`
- `SNAPSHOT_IMPORT`

### History

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

`FULL_SOURCE` does not mean exact history or one coherent point in time.

## Exact payload evidence

`SourceSystemEvent.PayloadJson` remains Scout semantic JSON/jsonb. PostgreSQL jsonb can normalise text, so exact replay evidence lives separately in `SourceCapturePayloadEvidence.ExactPayloadText` + SHA-256 under `exact-text.v1`.

`ScoutDbContext` validates/stamps exact capture evidence and inserts the exact-text sidecar in the same SaveChanges transaction.

Deterministic recapture can repair an older jsonb-only retained event without duplicating it only when connector/data-source/idempotency/hash evidence agrees. Contradiction fails closed.

## Tier-neutral source namespace

Whole-source capture uses:

`kyntic-connector:<connector-installation-guid>`

Never use connector type alone as exact source namespace. Two different systems can both contain `contact/123`.

## Public whole-source connector semantics

### SQL

- explicit `captureFullPermittedPayload=true`;
- explicit broader `captureColumns`; no fallback to normal selector columns;
- explicit unique `sourceRecordIdColumn`;
- typed keyset pagination, no OFFSET;
- history `SNAPSHOT_ONLY`;
- consistency `LIVE_KEYSET`.

### REST

- explicit full retention + `retainEntireResponseObject=true`;
- explicit collection endpoint + stable record ID;
- cursor-loop protection;
- generic REST always `SNAPSHOT_ONLY` regardless of opaque provider token;
- consistency `API_CURSOR`.

### CSV

- explicit full retention + unique source ID;
- row-set SHA pinned across pages;
- changing rows between pages fails the generation;
- history `SNAPSHOT_ONLY`;
- consistency `IMMUTABLE_SNAPSHOT`.

Only SQL/REST/CSV are registered as whole-source continuity connectors on this branch.

## Anti-resurrection rule

Example:

```text
generation 1: A, B
generation 2: A
```

B may have disappeared at source. Scout retains generation-1 evidence, but Fortress must not replay B as current state.

New table/entity:

`SourceCaptureGenerationMember` / `source_capture_generation_members`

It records tenant, connector installation, positive generation, retained event ID, source namespace/object/record.

Unique key inside one generation:

```text
Tenant + ConnectorInstallation + Generation + SourceObjectType + SourceRecordId
```

The same unchanged event may belong to multiple generations. Contradictory events claiming the same source key in one generation fail closed.

In-flight generation is `checkpoint.Generation + 1`; it becomes authoritative only when the checkpoint completes.

Completed checkpoints stamp:

`generation-membership.v1`

An old checkpoint with zero membership rows and no membership marker is not a proven empty source.

## Empty source correctness

`ConnectorSourceCaptureBatch` carries batch-level HistoryCompleteness + CurrentStateConsistency, so a correctly empty source can complete a real generation without inventing a fake row.

Zero members mean `proven empty generation N` only with a completed `generation-membership.v1` checkpoint.

## Upgrade readiness

Scout metadata preflight now considers completed FULL_SOURCE capture, full-permitted profile, exact-text evidence, history, current-state consistency, generation membership, target support and local connector/credential continuity.

Weak/unknown current-state consistency or missing membership cannot reach strongest readiness. Snapshot-only history remains honestly history-limited.

## Official local export v2

Tool:

`tools/KynticAI.Scout.UpgradeExport`

Contract:

`kyntic-scout-source-journal-export.v2`

V2 exports only each connector's **latest completed generation membership**:

```text
checkpoint.Generation
 -> generation member
 -> retained source event
 -> exact-text.v1 evidence
```

Older snapshot generations remain local audit/history evidence and are not current-state replay input.

The v2 manifest is now bound to both:

- Scout `TenantId`;
- tenant slug.

Every emitted row must match that Scout TenantId.

Per connector the manifest contains ID/type, selected generation, history class, current-state consistency, generation-membership contract and member count. A correctly empty connector remains present with `memberCount=0`.

Current v2 deliberately supports `SNAPSHOT_ONLY/UNKNOWN` only. Provider-specific exact ordered history needs a separate explicit handoff contract.

## Fortress target status

The matching Fortress branch now has:

- reusable `snapshot_v2` validation library;
- `scout-snapshot-validate`;
- first-class `CommercialIngress::process_snapshot_source(...)` using existing `SourceOperation::Snapshot`;
- shared CDC/Snapshot commercial mutation path;
- `scout-snapshot-backfill` proof executor.

The backfill validates the entire handoff before mutation, requires an explicit live Fortress `tenant-layer`, allows only a fresh target or same-generation interrupted resume, revalidates before mutation, and applies every selected row as `SourceOperation::Snapshot` through normal governed persistence/outbox.

Scout TenantId/slug remain validated provenance. They are **not** silently used as Fortress canonical tenant identity because live Fortress uses configured `pipeline.tenant_layer`.

The executor refuses an older populated target when the newest Scout snapshot omits an existing source key. Explicit absence reconciliation is still required; do not invent source-native delete time.

## Same-PostgreSQL cutover target

1. existing local connector/data plane runs normally;
2. fresh full-source generation under `exact-text.v1 + generation-membership.v1`;
3. for SQL/REST mutable enumeration, establish final recapture/write freeze or provider-specific ordered barrier;
4. pause connector ownership;
5. customer-local PostgreSQL backup;
6. additive Fortress state install;
7. create Scout v2 export;
8. Fortress v2 validate;
9. governed Snapshot import;
10. absence reconciliation where needed;
11. derived-index drain/rebuild;
12. restart/hash/count/outbox/canary proof;
13. resume the same compatible connector installation/config/credential path.

## Pending migration

`20260815221500_ConnectorCaptureCheckpoints.cs` now contains:

- connector capture checkpoints;
- current-state consistency;
- exact payload storage contract;
- generation-membership contract;
- exact payload evidence table;
- generation-membership table/indexes/FK.

It is not green until normal EF tooling regenerates/reconciles `ScoutDbContextModelSnapshot.cs` and PostgreSQL + supported SQLite behavior are proved. Do not create compensating migrations for this unreleased branch before that validation.

## Focused tests authored

`ScoutUpgradeGenerationMembershipTests` covers generation > 0, same retained event across later generation, missing membership fails readiness, empty estate also needs membership proof, and LIVE_KEYSET/API_CURSOR are not strong point-in-time claims.

## Immediate local validation

```powershell
dotnet restore .\KynticAI.Scout.slnx
dotnet build .\KynticAI.Scout.slnx
dotnet test .\tests\KynticAI.Scout.UnitTests\KynticAI.Scout.UnitTests.csproj
dotnet test .\tests\KynticAI.Scout.Sdk.Tests\KynticAI.Scout.Sdk.Tests.csproj
```

Then:

- build/run `tools/KynticAI.Scout.UpgradeExport`;
- reconcile EF migration/model snapshot;
- prove PostgreSQL migration/up/down + supported SQLite model path;
- tiny SQL/REST/CSV fixtures including a genuinely empty source;
- gen1 `{A,B}` then gen2 `{A}` -> v2 exports only A;
- incomplete gen3 must not affect export while checkpoint.Generation remains 2;
- old membership contract UNKNOWN rejects v2 export;
- tamper TenantId/generation/member-count/hash/namespace -> fail closed;
- hand v2 output to Fortress validator/backfill.

No GitHub Actions, Qwen, cloud or large-scale run to discover compiler errors.

## Remaining gaps

- local .NET compiler/test repair;
- EF snapshot/migration proof;
- persistent connector-host production scheduling/tail/change-feed behavior;
- abandoned in-flight generation reset semantics;
- credential-free connector readiness semantics;
- provider-specific exact-history contracts;
- executable lease/barrier/rollback/canary tooling;
- retention/schema-drift/egress proof;
- one accidental REST health diagnostic regression remains to restore during local compile repair: health details currently return `{}` instead of the prior serialized HTTP status code. Do not change continuity semantics while fixing it.

## Do not regress

- never replay all retained snapshot events;
- never infer source-native delete time from snapshot absence;
- never call FULL_SOURCE exact history;
- never call LIVE_KEYSET/API_CURSOR point-in-time consistency;
- never use jsonb round-trip text as exact evidence;
- never use connector type as source namespace;
- never migrate Scout secrets into Rust merely for licence transition;
- never send upgrade JSONL/raw payloads/IDs/credentials/vectors/governed state to Cloud;
- never call authored code runtime-green before local compiler/migration/proof passes.
