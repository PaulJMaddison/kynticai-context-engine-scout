# Local Validation

Routine local development must stay on restore, build, and unit-style tests. Do not run Docker, hosted endpoint checks, browser proof, or enterprise connector smoke unless the matching opt-in variable is set.

## Safe Default

```powershell
dotnet restore .\KynticAI.Scout.slnx
dotnet build .\KynticAI.Scout.slnx -c Release -warnaserror
dotnet test .\KynticAI.Scout.slnx -c Release --no-build

cd .\apps\web
npm install
npm run lint
npm run test
npm run build

cd ..\..\packages\typescript\scout-sdk
npm install
npm run test
```

`tools/KynticAI.Scout.UpgradeExport` is part of `KynticAI.Scout.slnx`; the normal solution build therefore compiles the sovereign upgrade exporter too.

## Scout -> Fortress continuity validation

Scout now has a durable source-ownership barrier in addition to whole-source checkpoints, exact payload evidence, generation membership and the customer-local JSONL export.

The relevant ownership states are:

```text
ScoutActive -> ScoutPausedForCutover -> FortressOwned
```

A cutover binding contains the connector installation, selected completed generation, snapshot completion time, high-water SHA-256, cutover epoch, cutover-token SHA-256 and ownership timestamps. The raw cutover token is never persisted.

The upgrade exporter now establishes the Scout pause barrier before selecting any export rows. This closes the previous race where export could read generation N and a later capture could complete generation N+1 before ownership transfer.

### 1. EF model and migration reconciliation

The ownership table is represented by:

- runtime mapping: `ConnectorCaptureOwnershipConfiguration`;
- migration: `20260816115800_ConnectorCaptureOwnership`;
- `ScoutDbContextModelSnapshot` ownership block.

Before any release, prove that EF sees no model drift:

```powershell
dotnet tool restore

dotnet ef migrations list `
  --project .\src\KynticAI.Scout.Infrastructure\KynticAI.Scout.Infrastructure.csproj `
  --startup-project .\src\KynticAI.Scout.Api\KynticAI.Scout.Api.csproj `
  --context ScoutDbContext

dotnet ef migrations has-pending-model-changes `
  --project .\src\KynticAI.Scout.Infrastructure\KynticAI.Scout.Infrastructure.csproj `
  --startup-project .\src\KynticAI.Scout.Api\KynticAI.Scout.Api.csproj `
  --context ScoutDbContext
```

PostgreSQL startup calls `Database.MigrateAsync()`, so a pending model difference is a release blocker rather than generated-file housekeeping.

The exact payload sidecar intentionally stores `ExactPayloadText` as database `text`. `SourceSystemEvent.PayloadJson` remains Scout's semantic JSON/jsonb storage. Do **not** change the sidecar back to jsonb: PostgreSQL jsonb may normalise textual representation and therefore cannot prove a byte-identical SHA-256 after a database round trip.

### 2. Focused persistence and cutover tests required

The local test pass must prove all of the following before the branch is called green:

- adding a capture `SourceSystemEvent` with a valid `RawPayloadSha256` creates one `SourceCapturePayloadEvidence` row in the same SaveChanges transaction;
- the capture envelope persisted into `HeadersJson` is stamped `PayloadStorageContract = exact-text.v1`;
- a raw payload/hash mismatch fails the SaveChanges operation;
- an ordinary non-capture `SourceSystemEvent` does not create a payload-evidence sidecar;
- a deterministic recapture of a legacy jsonb-only capture repairs the existing event with exact evidence rather than inserting a duplicate;
- contradictory existing evidence fails closed and is never overwritten;
- completed FULL_SOURCE generations have durable generation membership;
- checkpoint lease acquisition is exclusive across concurrent workers;
- cutover pause waits for/rejects an active worker lease instead of stealing it;
- a worker rechecks ownership after acquiring its durable lease and therefore cannot reach credentials/source I/O after a committed pause;
- a retry with the same cutover epoch/token is deterministic;
- a different epoch/token cannot overwrite an existing paused binding;
- `FortressOwned` cannot be reclaimed by Scout;
- old/missing exact evidence produces `HistoryLimited`, not a lossless claim.

### 3. Customer-local export proof

After a PostgreSQL Scout fixture has completed a FULL_SOURCE generation, generate a fresh local cutover epoch and token. The token must contain at least 32 characters of entropy and must be kept out of the repository/logs.

```powershell
$env:SCOUT_CUTOVER_TOKEN = '<local-secret-at-least-32-characters>'
$cutoverEpoch = [guid]::NewGuid().ToString()

dotnet run --project .\tools\KynticAI.Scout.UpgradeExport\KynticAI.Scout.UpgradeExport.csproj -- `
  --connection-string "$env:SCOUT_UPGRADE_CONNECTION_STRING" `
  --tenant demo-tenant `
  --cutover-epoch $cutoverEpoch `
  --output .\artifacts\upgrade\demo-tenant.scout-source.jsonl `
  --overwrite
```

The exporter now performs the pause/bind operation itself before it reads export selection. Expected behaviour:

- every connector checkpoint is locked transactionally;
- an active capture lease makes cutover fail rather than overlap source capture;
- every installed connector must have a completed FULL_SOURCE generation;
- ownership is persisted as `ScoutPausedForCutover` for the supplied epoch/token hash;
- export reads only the persisted selected generation;
- row count must equal that generation's membership count;
- every row must have matching exact-text evidence and capture metadata;
- retry with the same epoch/token can resume safely after an export failure without silently selecting a newer generation.

The tool writes:

- `demo-tenant.scout-source.jsonl` containing exact customer-permitted payload evidence used for the local Fortress rebuild;
- `demo-tenant.scout-source.jsonl.manifest.json` containing the export contract, row count, whole-file SHA-256, connector types, cutover epoch/token hash and sovereign-data flags.

A failed export after the pause is intentionally fail closed: Scout remains paused. Do not manually unpause it by editing the database.

The JSONL contains customer data and **must remain inside the customer data plane**. It is not a support bundle and must never be uploaded to KynticAI Cloud.

### 4. Anti-resurrection proof

For every snapshot-style connector family, run this minimum fixture:

1. generation 1 contains `A` and `B`;
2. generation 2 contains only `A`;
3. generation 3 is started but left incomplete;
4. run upgrade export.

The ownership binding and export must select completed generation 2. `B` must not appear and incomplete generation 3 must not move the export boundary.

### 5. Cross-repo Fortress validation

From the matching Fortress continuity branch, validate the exported journal before any governed-state mutation:

```powershell
cd C:\Kyntic\kynticai-context-engine-fortress\engine
cargo run -p ucl-scout-upgrade --bin scout-journal-validate -- `
  --journal C:\Kyntic\kynticai-context-engine-scout\artifacts\upgrade\demo-tenant.scout-source.jsonl
```

The Fortress validator must verify the whole-file SHA-256, row count, connector set, `exact-text.v1`, capture hashes, source/connector identity, FULL_SOURCE origin, history fidelity and source-position class. A development fixture escape hatch must never be used for a customer cutover.

### 6. Required disposable-cloud proof

The checked-in GCP harness lives under `scripts/cloud-tests/` and the full runbook is:

```text
docs/testing/gcp-precloud-validation.md
```

It runs the actual Release build/test/EF/PostgreSQL migration path on an ephemeral London Compute Engine VM with a two-hour automatic-delete limit. The required synthetic acceptance matrix covers 100k rows, anti-resurrection, concurrent pause/cutover, tamper/fail-closed behaviour and crash/restart. A 1m-row pass is optional.

The cloud suite uses the mock LLM provider because source continuity/cutover correctness is deterministic and model-independent.

## Connector semantics that tests must preserve

`FULL_SOURCE` means the connector enumerated the complete customer-permitted source projection. It does **not** by itself mean exact historical change capture.

- generic SQL: full current projection, `SNAPSHOT_ONLY` unless a provider-specific ordered change feed is implemented;
- CSV: complete supplied-file snapshot, `SNAPSHOT_ONLY`;
- generic REST list/page API: full current projection, normally `SNAPSHOT_ONLY`;
- source-native ordered change feed: may claim `COMPLETE` or `FROM_RETENTION_BOUNDARY` only when the source position has explicit ordering semantics and the boundary is proven.

Never promote snapshot pagination into fake historical CDC. A Scout -> Fortress upgrade may preserve connector configuration and current captured data without being able to recreate history the source/Scout never retained. That case must be explained as `HISTORY_LIMITED`, not hidden as data loss or falsely called lossless.

## CI (Continuous Integration)

GitHub Actions is currently disabled: the repository workflows are stored as `.disabled`. A temporary validation workflow attempted during the 2026-08-16 code review did not receive a runner and executed zero steps, so it was removed immediately and no workflow/config change remains.

Do not treat that infrastructure failure as either a code pass or a code failure. Use the local commands above or the disposable GCP runbook for the executable gate.

A quick non-integration test run is:

```powershell
dotnet test .\KynticAI.Scout.slnx --no-restore --filter "Category!=Integration"
```

## Optional External/Container/Live Commands

| Proof path | Class | Command | Required opt-in |
|---|---|---|---|
| Web browser/Playwright proof | Opt-in browser | `cd apps\web; npm run test:e2e` | `KYNTIC_RUN_BROWSER_TESTS=1` |
| README screenshot capture | Opt-in browser | `cd apps\web; node .\.capture-readme-screenshots.mjs` | `KYNTIC_RUN_BROWSER_TESTS=1` |
| Docker/PostgreSQL production rehearsal | Opt-in container | `.\scripts\production-rehearsal.ps1 -RunDocker` | `KYNTIC_RUN_EXTERNAL_DOTNET_TESTS=1` |
| Scout continuity PostgreSQL fixture/export | Opt-in local/container | run FULL_SOURCE capture + `KynticAI.Scout.UpgradeExport` | `KYNTIC_RUN_EXTERNAL_DOTNET_TESTS=1` |
| Disposable GCP pre-cloud proof | Opt-in cloud | `scripts/cloud-tests/gcp-precloud-setup.sh` then `gcp-precloud-run.sh` | explicit Google Cloud project/billing setup |
| Enterprise connector smoke in paid-pilot rehearsal | Opt-in external/live | `.\scripts\paid-pilot-local-rehearsal.ps1` | `KYNTIC_RUN_EXTERNAL_DOTNET_TESTS=1` unless `-SkipEnterpriseConnectorSmoke` is supplied |

## Required Environment Variables

The safe default path requires no environment variables beyond local SDK/toolchain availability. Browser proof requires `KYNTIC_RUN_BROWSER_TESTS=1`. Docker/PostgreSQL and enterprise connector proof require `KYNTIC_RUN_EXTERNAL_DOTNET_TESTS=1`.

The local upgrade exporter may use `SCOUT_UPGRADE_CONNECTION_STRING` and `SCOUT_CUTOVER_TOKEN`; keep both outside the repository and point the connection string only at the customer-local/disposable PostgreSQL instance being validated.

The GCP harness requires `GCP_PROJECT_ID`; the optional budget helper additionally requires `GCP_BILLING_ACCOUNT_ID`.

## Expected Outputs

Safe validation should finish with successful .NET restore/build, passing deterministic tests and successful compilation of `KynticAI.Scout.UpgradeExport`.

The continuity proof additionally requires:

- no EF pending model changes;
- successful PostgreSQL migration including `connector_capture_ownership`;
- exact payload-sidecar and generation-membership tests;
- a FULL_SOURCE checkpoint;
- successful pause-bound local export;
- anti-resurrection proof;
- concurrent cutover proof;
- successful Fortress journal validation;
- disposable-cloud 100k reconciliation and teardown before production use.

## Known Partial/Blocked Proofs

The 2026-08-16 direct-GitHub review fixed the stale export/cutover race, added the ownership migration and reconciled the EF model snapshot. Static concurrency paths were reviewed directly in the branch.

This environment could not complete a real .NET build/test: local Git/DNS access was unavailable and GitHub Actions did not allocate a runner. Therefore the code remains **AUTHORED / EXECUTABLE VALIDATION REQUIRED** until the local or GCP gates above pass. Do not infer runtime-green status from the static review.
