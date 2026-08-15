# KynticAI Scout Engineering Session

## Last Updated

2026-08-15 — upgrade-compatible local capture and tier-continuity work.

## Branch / baseline

- Repository: `PaulJMaddison/kynticai-context-engine-scout`
- Branch: `chatgpt/fortress-upgrade-compatible-capture`
- Starting `main` commit: `49f352bfded6452f5ede6c49a1996282368dacae`
- Status: **AUTHORED / NOT RUNTIME-GREEN** until local .NET compile/tests pass.

This file is intentionally public-safe. Do not place proprietary Fortress implementation or private customer material in this repository.

## Goal

Make Scout an excellent source-data foundation that can later move to a richer KynticAI tier without making the customer reconnect the systems they already configured or deliberately throwing away locally retained source history.

The important distinction is:

- Scout may expose simpler context/relationship capabilities;
- connectors should capture a stable, upgrade-compatible **customer-permitted source envelope** locally;
- richer tiers may rebuild their own derived state from that source envelope;
- Scout-derived context/fallback scores are not a substitute for later canonical derived state.

## Current data-plane truth

Scout already persists `SourceSystemEvent.PayloadJson` and source-event metadata in the local relational store. `ConnectorFetchResult` already had a raw payload plus normalised payload/provenance. This means upgrade continuity can build on existing source retention rather than creating a second copy of all raw data.

Before this branch, the missing information was mainly the upgrade semantics around the payload: stable connector instance, connector definition/capture version, provider-native exact source position/checkpoint, source object/record identity, operation, schema fingerprint, redaction policy and idempotency key.

## Architecture decision: full customer-permitted capture

For connector types offered in Scout and a later KynticAI tier, the desired capture profile is the same conceptual source envelope.

`full customer-permitted` means the complete payload the customer has authorised the connector to collect **after** configured allow-list/redaction/data-minimisation rules. It never means collecting fields the customer has disallowed.

Scout itself can consume only the subset it needs. Retaining the authorised source envelope locally allows a later tier to build richer identities, chronology, relationships and governed context without asking the source system to replay data that Scout discarded.

Storage/retention remains a customer/local deployment concern and should be configurable.

## New contracts in this branch

### `LocalDataPlaneUpgradeContracts.cs`

Adds:

- `kyntic-local-source-capture.v1`
- `kyntic-scout-upgrade-manifest.v1`
- `full-permitted.v1`
- upgrade readiness categories:
  - `Lossless`
  - `LosslessDerivedRebuild`
  - `HistoryLimited`
  - `ReconnectRequired`
  - `Unsupported`

The source capture metadata includes connector instance/version, source namespace/object/record ID, operation, exact provider-native `SourcePositionJson`, occurred/source-recorded/ingested times, schema fingerprint, redaction-policy version, full-permitted-retention flag and deterministic idempotency key.

### Connector abstraction change

`IConnectorPlugin.cs` now supports:

- `ConnectorCapability.UpgradeCompatibleCapture`
- optional `ConnectorCaptureMetadata` on `ConnectorFetchResult`

The optional parameter preserves source compatibility for existing connector implementations while allowing upgraded connectors to provide the stronger capture contract.

### `LocalSourceCaptureEnvelope`

Converts connector capture metadata into the public local capture contract and merges it into source-event headers under `kynticCapture`.

It does not duplicate raw payload into the metadata block and it rejects incomplete/incorrectly timed capture metadata.

### `ScoutUpgradeCompatibilityService`

Builds a metadata-only upgrade manifest from the local installation. It reads local connector/data-source/source-event state but exports only safe continuity metadata:

- connector instance/data-source/workspace IDs;
- connector type/status;
- hash of connection configuration, not configuration contents;
- local secret **references**, never protected secret values;
- retained event counts and time coverage;
- capture profiles/schema fingerprints;
- history/full-payload completeness;
- storage provider and upgrade classification.

The manifest explicitly says customer data remains local and contains no credentials.

## PostgreSQL decision

Production upgrades are intended to keep the same customer-local PostgreSQL database/cluster where practical.

The database should be thought of as the customer's **KynticAI local data-plane substrate**, not something that must be thrown away because the licence tier changes.

A later tier should add its own schema/tables additively, rebuild its richer derived state from retained source truth, then continue the same compatible connector installations/checkpoints.

Scout's SQLite mode remains useful for development/smaller local use. It is not currently claimed as the same-database target for the richer tier. A customer on SQLite needs a proven local SQLite -> PostgreSQL migration/import path before the same-database upgrade claim can be made.

## Connector continuity requirements

A Scout connector is upgrade-compatible only when it can prove all relevant points:

1. Stable connector installation ID.
2. Stable local data-source ID.
3. Reusable local credential reference; never export/re-upload a credential merely because the tier changed.
4. Versioned connector definition and capture profile.
5. Full customer-permitted source payload retained locally.
6. Stable source namespace/object/record identity.
7. Create/update/delete/tombstone operation semantics where the source provides them.
8. Exact provider-native source position/checkpoint, including a minor ordinal/sequence when one major source position can contain multiple changes.
9. Occurred/source-recorded/ingested timestamps.
10. Deterministic idempotency key for exact replay.
11. Schema fingerprint/version so drift can be detected.
12. Redaction/allow-list policy version.
13. Known earliest retained upgrade-compatible event so historical guarantees have an honest boundary.

## Material integration gap

The contracts/helpers/service exist, but the new `ConnectorCaptureMetadata` is **not yet wired through every real connector fetch -> source event ingestion path**.

Next coding session must inspect the normal connector execution path in `ScoutService` / selector execution and:

- persist `kynticCapture` beside every live/scheduled connector-sourced event when the connector declares `UpgradeCompatibleCapture`;
- make each connector offered in Scout populate the metadata correctly;
- preserve `RawPayloadJson` as the full permitted local payload;
- ensure preview/dry-run does not accidentally create durable source history unless explicitly intended;
- expose metrics/health that show capture-profile version and earliest exact upgrade boundary without exposing source values.

Until this is done and deployed, historical Scout data should not automatically be labelled lossless-upgrade compatible.

## Relationship / IP boundary

Scout's simple relationship engine remains a fallback/open-core capability. Do not copy private richer-tier algorithms into Scout.

Scout should improve the **quality and continuity of source evidence**. The richer tier can consume that evidence and build its own canonical identity/relationship/temporal state.

This separation is deliberate: upgrade compatibility is achieved by preserving source truth and provenance, not by making Scout secretly contain the commercial engine.

## Sovereign boundary

Scout local/self-hosted deployments must keep source payloads, credentials, database rows, context, prompts and local model data inside the customer environment unless the customer explicitly authorises an export.

A KynticAI control plane may handle entitlement/licence/subscription/version/update and bounded aggregate operational/billing metadata. It must not require raw source records for this upgrade workflow.

The new upgrade manifest is designed so it can be reviewed/used as continuity metadata without transporting source payloads or secret values.

## Tests authored

`ScoutFortressUpgradeCompatibilityTests.cs` currently covers:

- complete PostgreSQL/full-capture state -> derived rebuild without reconnect;
- legacy retained events without capture metadata -> `HistoryLimited`;
- missing local credential reference -> `ReconnectRequired`;
- unsupported connector -> `Unsupported`;
- exact source position survives the capture envelope and secret-shaped fields are not present.

These tests have **not yet been executed** in this direct-GitHub session.

## Required next validation

1. Pull this branch into a clean worktree.
2. `dotnet restore` / build the normal Scout solution.
3. Run focused unit tests for the new upgrade code.
4. Fix compile/test failures in the same package.
5. Add integration coverage with SQLite for quick deterministic behaviour.
6. Add PostgreSQL integration coverage for upgrade manifest/provider detection and retained source events.
7. Wire capture metadata into the actual connector execution path.
8. Update connector-authoring tests so an `UpgradeCompatibleCapture` connector cannot claim that capability while returning missing/incomplete capture metadata in live mode.
9. Validate each Scout-shipped connector against the full-permitted envelope.
10. Run a small synthetic PostgreSQL cutover proof with the private richer-tier repo only after both sides compile.

## Do not do

- Do not place proprietary Fortress scoring/identity algorithms in this public repo.
- Do not export protected credential values in an upgrade manifest.
- Do not send customer payloads to KynticAI Cloud to perform a local upgrade.
- Do not claim all historical Scout data is losslessly upgradeable merely because `PayloadJson` exists; exact source/capture semantics matter too.
- Do not silently reconnect a source and call it seamless. Report `ReconnectRequired` when continuity cannot be proven.
- Do not call SQLite a proven same-database production upgrade substrate until that migration path is explicitly built and tested.
