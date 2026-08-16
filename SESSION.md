# KynticAI Scout Engineering Session

## Last updated

2026-08-16 — pre-cloud static review, durable Scout -> Fortress ownership cutover, bounded GCP validation design.

## Current branch truth

- Repository: `PaulJMaddison/kynticai-context-engine-scout`
- Branch: `chatgpt/precloud-static-fixes-20260816`
- Pull request: #38 into `main`
- Status: **STATIC REVIEW/FIXES COMPLETE FOR THE CUTOVER PATH; EXECUTABLE VALIDATION STILL REQUIRED**.
- The direct-GitHub environment used for this review could not complete a real .NET build/test. Local Git/DNS access was unavailable and a temporary GitHub Actions validation job received no runner and executed zero steps. The temporary workflow was removed; no CI configuration change remains.
- The required executable proof is now specified in `LOCAL_VALIDATION.md` and `docs/testing/gcp-precloud-validation.md`.

Code/runtime truth wins. Do not call the branch runtime-green until the local or disposable GCP build/test/EF/PostgreSQL gates pass.

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

## Capture fidelity remains three separate things

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

## Exact payload and generation evidence

`SourceSystemEvent.PayloadJson` remains semantic JSON/jsonb. Exact replay evidence is retained separately as `SourceCapturePayloadEvidence.ExactPayloadText` + SHA-256 under `exact-text.v1`.

Whole-source current-state membership is retained as `SourceCaptureGenerationMember` under `generation-membership.v1`.

This prevents the anti-resurrection failure:

```text
generation 1: A, B
generation 2: A
```

Fortress rebuild input for generation 2 must contain only A. Older generation evidence remains local history/audit evidence but is not current-state replay input.

A genuinely empty source is valid only when a completed generation has `generation-membership.v1` and zero members. An old checkpoint with no membership evidence is not proof of an empty source.

## Durable connector ownership barrier

The reviewed branch now persists connector ownership in:

```text
connector_capture_ownership
```

State progression:

```text
ScoutActive -> ScoutPausedForCutover -> FortressOwned
```

The binding records:

- tenant and connector installation;
- selected completed generation;
- snapshot completion timestamp;
- SHA-256 of the selected high-water mark;
- cutover epoch;
- SHA-256 of the cutover token;
- Scout paused timestamp;
- Fortress owned timestamp.

`State` and `CutoverEpoch` are EF concurrency tokens. Checkpoint `LeaseOwner` and `LeaseExpiresAtUtc` remain concurrency tokens as well.

### Concurrency reasoning verified in this review

The normal capture path checks ownership before attempting a checkpoint lease and checks it again after durable lease acquisition.

That second check is important. A worker that was waiting behind the export pause transaction may acquire a lease after the pause commits, but it then sees `ScoutPausedForCutover`, releases the lease and exits before credential retrieval or source I/O.

Two processes cannot both successfully acquire the same checkpoint lease because the lease fields participate in EF optimistic concurrency and the coordinator handles `DbUpdateConcurrencyException` as a failed acquisition.

## Export race found and fixed

The original PR could select/export a completed checkpoint without proving that the same generation was the one later bound into the persistent ownership transfer.

That left a race:

```text
export reads generation N
capture completes generation N+1
ownership transfer binds N+1
Fortress receives a validly hashed but stale generation N export
```

`tools/KynticAI.Scout.UpgradeExport` now closes this race.

Before reading export selection it:

1. opens a PostgreSQL transaction;
2. locks every installed connector checkpoint with `FOR UPDATE`;
3. refuses an active/unexpired capture lease;
4. requires every connector to have a completed generation with no in-flight continuation/error;
5. persists/updates `ScoutPausedForCutover` for the supplied cutover epoch and token hash;
6. commits the pause barrier;
7. reads export selection by joining through those persisted ownership rows.

The JSONL and manifest are therefore bound to the same persisted selected generation later used for ownership handoff.

If export fails after pause, Scout intentionally remains paused. Retry with the same epoch/token is deterministic and does not silently move to a newer generation. A different paused binding or `FortressOwned` binding cannot be overwritten.

## Upgrade export v2 invocation

Tool:

```text
tools/KynticAI.Scout.UpgradeExport
```

Contract:

```text
kyntic-scout-source-journal-export.v2
```

Required cutover inputs now include:

- `--cutover-epoch <non-empty-guid>`;
- cutover token via `--cutover-token` or `SCOUT_CUTOVER_TOKEN`;
- token minimum 32 characters;
- only the token SHA-256 is persisted/output in metadata.

Example:

```powershell
$env:SCOUT_CUTOVER_TOKEN = '<local-secret-at-least-32-characters>'
$epoch = [guid]::NewGuid().ToString()

dotnet run --project .\tools\KynticAI.Scout.UpgradeExport\KynticAI.Scout.UpgradeExport.csproj -- `
  --connection-string "$env:SCOUT_UPGRADE_CONNECTION_STRING" `
  --tenant demo-tenant `
  --cutover-epoch $epoch `
  --output .\artifacts\upgrade\demo-tenant.scout-source.jsonl
```

The manifest records the cutover epoch and token hash, selected generation per connector, row/member counts, history/current-state classes, whole-file SHA-256 and sovereign-data flags.

## EF migration status

The ownership model is now represented in all runtime migration-critical places:

- `ConnectorCaptureOwnership` entity;
- `ConnectorCaptureOwnershipConfiguration`;
- `ScoutDbContext.ConnectorCaptureOwnerships`;
- migration `20260816115800_ConnectorCaptureOwnership`;
- `ScoutDbContextModelSnapshot` ownership block.

The stale model snapshot found during review was fixed. The snapshot change was checked at commit level and contains only the expected ownership entity block, including `State` and `CutoverEpoch` concurrency tokens.

The ownership migration was authored directly through GitHub rather than generated by local `dotnet ef`; unlike the repository's older generated migrations, it currently has no generated `.Designer.cs`. That is not being hidden by a mutable/dynamic designer. The executable validation gate must run normal EF tooling and confirm migration scripting/up/down behaviour before production use. If local EF generation produces a static designer/metadata delta, commit that generated output rather than hand-maintaining historical target model code.

## Cloud validation now designed and checked in

Runbook:

```text
docs/testing/gcp-precloud-validation.md
```

Scripts:

```text
scripts/cloud-tests/gcp-precloud-budget.sh
scripts/cloud-tests/gcp-precloud-setup.sh
scripts/cloud-tests/gcp-precloud-run.sh
scripts/cloud-tests/gcp-precloud-teardown.sh
```

Default proof environment:

- Google Cloud `europe-west2` / `europe-west2-b`;
- one `e2-standard-4` VM (4 vCPU / 16 GB);
- optional `e2-standard-8` only for 1m-row scale proof;
- 50 GB balanced boot disk;
- no GPU/TPU;
- no Cloud SQL;
- two-hour maximum VM runtime;
- automatic instance deletion at the runtime limit;
- 25 billing-account currency-unit alerting budget at 50/80/100%;
- no public Scout application firewall port.

The budget is an alert, not the hard cutoff. The real containment controls are the machine whitelist, one-VM design, no GPU/managed DB, two-hour automatic delete and explicit teardown.

Core cloud test uses the mock LLM provider. The Scout -> Fortress source-continuity proof is deterministic and should not pay for or depend on model inference.

## Required cloud acceptance matrix

Before production cutover, prove all of the following with synthetic data:

1. Release build with warnings as errors.
2. Full deterministic .NET tests.
3. EF `has-pending-model-changes` passes.
4. PostgreSQL `MigrateAsync` succeeds and creates ownership table/indexes.
5. Scout starts/readiness passes against PostgreSQL.
6. Exact payload evidence and generation membership reconcile.
7. gen1 `{A,B}` then gen2 `{A}` exports only A.
8. incomplete gen3 cannot move the export boundary.
9. 8 and then 32 concurrent capture workers cannot overlap a committed pause.
10. wrong cutover epoch/token fails closed; same binding retries deterministically.
11. `FortressOwned` cannot be reclaimed.
12. tenant/generation/member-count/hash/namespace tampering fails export.
13. restart/crash around lease, pause and export preserves safe ownership state.
14. required 100,000-row capture/export row-count + SHA reconciliation passes.
15. optional 1,000,000-row pass on `e2-standard-8` if wanted.
16. VM/resource teardown and actual spend are checked.

## Validation performed in this direct-GitHub review

Performed:

- full live PR diff/code-path review of the cutover branch;
- checkpoint lease/concurrency trace;
- ownership state-machine trace;
- export selection/ownership race analysis;
- direct fixes for the race, migration and snapshot;
- commit-diff verification that snapshot change is isolated;
- GCP test harness and runbook authoring.

Not performed successfully in this environment:

- real `dotnet restore/build/test`;
- real EF command execution;
- PostgreSQL migration execution;
- Docker run;
- GCP run;
- Fortress cross-repo replay execution.

These remain explicit executable gates, not assumed passes.

## Same-PostgreSQL cutover target

1. existing local connector/data plane runs normally;
2. fresh full-source generation under `exact-text.v1 + generation-membership.v1`;
3. run upgrade export with a fresh cutover epoch/token — exporter establishes the persistent pause barrier first;
4. customer-local PostgreSQL backup;
5. additive Fortress state install;
6. Fortress v2 validate;
7. governed Snapshot import;
8. absence reconciliation where needed;
9. derived-index drain/rebuild;
10. restart/hash/count/outbox/canary proof;
11. transfer durable state to `FortressOwned`;
12. resume the same compatible connector installation/config/credential path under Fortress ownership.

## Remaining work after this branch

- run local or GCP executable validation and commit any genuinely generated EF metadata required by that run;
- merge PR #38 once the reviewed branch is acceptable;
- perform the requested full code-only review of the resulting `main` branch and fix any remaining code issues directly on `main`;
- execute the 100k synthetic cloud proof before production cutover;
- run the matching Fortress validation/backfill proof;
- keep provider-specific exact-history/change-feed contracts separate from snapshot continuity.

## Do not regress

- never replay all retained snapshot events as current state;
- never export before establishing the durable Scout pause binding;
- never allow capture source I/O after a committed paused/Fortress-owned state;
- never infer source-native delete time from snapshot absence;
- never call FULL_SOURCE exact history;
- never call LIVE_KEYSET/API_CURSOR point-in-time consistency;
- never use jsonb round-trip text as exact evidence;
- never use connector type as exact source namespace;
- never store or log the raw cutover token;
- never overwrite a different paused/Fortress ownership binding;
- never migrate Scout secrets into Rust merely for licence transition;
- never send upgrade JSONL/raw payloads/IDs/credentials/vectors/governed state to Cloud;
- never call authored/static-reviewed code runtime-green before executable compiler/migration/proof passes.
