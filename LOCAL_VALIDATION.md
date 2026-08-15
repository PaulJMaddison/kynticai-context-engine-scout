# Local Validation

Routine local development must stay on restore, build, and unit-style tests. Do not run Docker, hosted endpoint checks, browser proof, or enterprise connector smoke unless the matching opt-in variable is set.

## Safe Default

```powershell
dotnet restore .\KynticAI.Scout.slnx
dotnet build .\KynticAI.Scout.slnx
dotnet test .\tests\KynticAI.Scout.UnitTests\KynticAI.Scout.UnitTests.csproj
dotnet test .\tests\KynticAI.Scout.Sdk.Tests\KynticAI.Scout.Sdk.Tests.csproj

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

The continuity branch adds whole-source capture checkpoints, exact payload evidence, a metadata-only preflight manifest and a customer-local JSONL export. These are not optional documentation artifacts: they are the evidence chain for a no-reconnect/additive Scout -> Fortress upgrade.

### 1. EF model and migration reconciliation

The continuity migration was authored before runtime validation and **must** be reconciled with the generated EF model snapshot before merge. Do not create a second corrective migration merely to paper over snapshot drift while the first migration is still unreleased.

```powershell
dotnet tool restore

dotnet ef migrations list `
  --project .\src\KynticAI.Scout.Infrastructure\KynticAI.Scout.Infrastructure.csproj `
  --startup-project .\src\KynticAI.Scout.Api\KynticAI.Scout.Api.csproj `
  --context ScoutDbContext

# Inspect the generated model/migration delta using the normal repository EF workflow.
# The final model must contain:
#   connector_capture_checkpoints
#   source_capture_payload_evidence
#   ConnectorCaptureCheckpoint.PayloadStorageContract
# and the ScoutDbContextModelSnapshot must agree with those definitions.
```

The exact payload sidecar intentionally stores `ExactPayloadText` as database `text`. `SourceSystemEvent.PayloadJson` remains Scout's semantic JSON/jsonb storage. Do **not** change the sidecar back to jsonb: PostgreSQL jsonb may normalise textual representation and therefore cannot prove a byte-identical SHA-256 after a database round trip.

### 2. Focused persistence tests required

The local test pass must prove all of the following before this branch is called green:

- adding a capture `SourceSystemEvent` with a valid `RawPayloadSha256` creates one `SourceCapturePayloadEvidence` row in the same SaveChanges transaction;
- the capture envelope persisted into `HeadersJson` is stamped `PayloadStorageContract = exact-text.v1`;
- a raw payload/hash mismatch fails the SaveChanges operation;
- an ordinary non-capture `SourceSystemEvent` does not create a payload-evidence sidecar;
- a deterministic recapture of a legacy jsonb-only capture repairs the existing event with exact evidence rather than inserting a duplicate;
- contradictory existing evidence fails closed and is never overwritten;
- a completed full-source checkpoint is only advertised as exact when `PayloadStorageContract == exact-text.v1`;
- old/missing exact evidence produces `HistoryLimited`, not a lossless claim.

### 3. Customer-local export proof

After a small PostgreSQL Scout fixture has completed a FULL_SOURCE generation, run the exporter locally:

```powershell
dotnet run --project .\tools\KynticAI.Scout.UpgradeExport\KynticAI.Scout.UpgradeExport.csproj -- `
  --connection-string "$env:SCOUT_UPGRADE_CONNECTION_STRING" `
  --tenant demo-tenant `
  --output .\artifacts\upgrade\demo-tenant.scout-source.jsonl `
  --overwrite
```

Expected output is aggregate metadata only. The tool writes:

- `demo-tenant.scout-source.jsonl` containing the exact customer-permitted payload evidence used for the local Fortress rebuild;
- `demo-tenant.scout-source.jsonl.manifest.json` containing the export contract, row count, whole-file SHA-256, connector types and sovereign-data flags.

The exporter must fail if any installed connector lacks a completed `FULL_SOURCE` + `exact-text.v1` checkpoint, or if any retained FULL_SOURCE event lacks its exact payload sidecar. It must never recover replay bytes from PostgreSQL jsonb.

The JSONL contains customer data and **must remain inside the customer data plane**. It is not a support bundle and must never be uploaded to KynticAI Cloud.

### 4. Cross-repo Fortress validation

From the matching Fortress continuity branch, validate the exported journal before any governed-state mutation:

```powershell
cd C:\Kyntic\kynticai-context-engine-fortress\engine
cargo run -p ucl-scout-upgrade --bin scout-journal-validate -- `
  --journal C:\Kyntic\kynticai-context-engine-scout\artifacts\upgrade\demo-tenant.scout-source.jsonl
```

The Fortress validator automatically expects `<journal>.manifest.json` and verifies the whole-file SHA-256, row count, connector set, `exact-text.v1`, capture hashes, source/connector identity, FULL_SOURCE origin, history fidelity and source-position class. `--allow-unmanifested-local-projection` is a development-fixture escape hatch only and must not be used for a customer cutover.

## Connector semantics that tests must preserve

`FULL_SOURCE` means the connector enumerated the complete customer-permitted source projection. It does **not** by itself mean exact historical change capture.

- generic SQL: full current projection, `SNAPSHOT_ONLY` unless a provider-specific ordered change feed is implemented;
- CSV: complete supplied-file snapshot, `SNAPSHOT_ONLY`;
- generic REST list/page API: full current projection, normally `SNAPSHOT_ONLY`;
- source-native ordered change feed: may claim `COMPLETE` or `FROM_RETENTION_BOUNDARY` only when the source position has explicit ordering semantics and the boundary is proven.

Never promote snapshot pagination into fake historical CDC. A Scout -> Fortress upgrade may preserve connector configuration and current captured data without being able to recreate history the source/Scout never retained. That case must be explained as `HISTORY_LIMITED`, not hidden as data loss or falsely called lossless.

## CI (Continuous Integration)

GitHub Actions is currently disabled: the workflows in `.github/workflows` are renamed to `.disabled` because the GitHub account is locked due to a billing issue that prevents Actions jobs from starting. Do not add or re-enable GitHub Actions for this work.

The disabled `ci.yml` remains reference documentation only:

- `backend`: .NET restore/build and deterministic test projects;
- `frontend`: web lint/test/build plus TypeScript SDK;
- `public-safety`: forbidden-code/secret-marker scan.

Browser proof requires `KYNTIC_RUN_BROWSER_TESTS=1`, and Docker/PostgreSQL or enterprise connector proof requires `KYNTIC_RUN_EXTERNAL_DOTNET_TESTS=1`.

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
| Enterprise connector smoke in paid-pilot rehearsal | Opt-in external/live | `.\scripts\paid-pilot-local-rehearsal.ps1` | `KYNTIC_RUN_EXTERNAL_DOTNET_TESTS=1` unless `-SkipEnterpriseConnectorSmoke` is supplied |
| Cross-repo paid-pilot connector proof | Opt-in external/live | `.\scripts\paid-pilot-rehearsal-check.ps1` | `KYNTIC_RUN_EXTERNAL_DOTNET_TESTS=1` unless `-SkipEnterpriseConnectorSmoke` is supplied |

## Required Environment Variables

The safe default path requires no environment variables beyond local SDK/toolchain availability. Browser proof requires `KYNTIC_RUN_BROWSER_TESTS=1`. Docker/PostgreSQL and enterprise connector proof require `KYNTIC_RUN_EXTERNAL_DOTNET_TESTS=1`. The local upgrade exporter may use `SCOUT_UPGRADE_CONNECTION_STRING`; keep it outside the repository and point it only at the customer-local/disposable PostgreSQL instance being validated.

## Expected Outputs

Safe validation should finish with successful .NET restore/build, passing backend unit and SDK tests, clean frontend lint output, passing Vitest output for `apps\web`, a successful web build, passing TypeScript SDK tests, and successful compilation of `KynticAI.Scout.UpgradeExport`.

The continuity proof additionally requires an EF model/migration match, exact payload-sidecar tests, a FULL_SOURCE checkpoint, a successful local export and a successful Fortress journal validation.

## Known Partial/Blocked Proofs

The Scout -> Fortress continuity code is **AUTHORED / NOT RUNTIME-GREEN** until the local .NET/EF/PostgreSQL validation above passes. The pending continuity migration/model snapshot must be reconciled. The export tool and exact-evidence persistence path have not yet been compiler/runtime-proved in this direct-GitHub session.

Playwright browser proof remains opt-in. Docker/PostgreSQL rehearsal is blocked without Docker and the external-test gate. Enterprise connector smoke is partial unless approved connector fixtures/endpoints are available.
