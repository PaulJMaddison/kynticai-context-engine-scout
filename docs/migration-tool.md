# KynticAI Scout Migration Tool

The KynticAI Scout Migration Tool is the open-core context/evidence export
tool. It reads a tenant's context and evidence data from a local Scout
data-plane deployment and writes it to a local folder as a portable
migration-export package. It is a local-only command-line tool: it never
uploads data anywhere, and it rejects any request to do so.

The tool lives at `tools/KynticAI.Scout.MigrationTool` and is part of the
public Scout solution.

## When to use it

Use the tool when you need to move context/evidence data out of a Scout
deployment:

- preparing a tenant's exported data before decommissioning or moving an
  environment;
- creating a local, portable archive of context snapshots, context facts,
  source events, selector definitions/executions, user signals, provenance,
  tenant metadata, and audit events;
- validating that an export is clean before handing it to another process
  (see `--dry-run` below).

The export contract for the local evidence/context surface is described in
[Evidence Pack Contract v1](evidence-pack-contract-v1.md). Exporting data out
is open core; full enterprise migration **import** of those records into a new
deployment is not part of the open core. See
[Open-Core Product Boundary](open-core-boundary.md).

## Prerequisites

- .NET 10 SDK (use `./.dotnet/dotnet` if you installed the repo-local runtime
  via `scripts/setup-demo.sh`, otherwise a global SDK).
- A local Scout deployment with the target tenant present and the configured
  local storage adapter available. The tool loads the same
  `appsettings.json` files as the API from `src/KynticAI.Scout.Api` (plus any
  file passed with `--settings`), so run it from inside the repository.

Restore the solution once before first use:

```powershell
dotnet restore .\KynticAI.Scout.slnx
```

## Building and running

The tool is a normal .NET console project. You can run it directly with
`dotnet run`, or build it and run the produced binary:

```powershell
dotnet build .\tools\KynticAI.Scout.MigrationTool\KynticAI.Scout.MigrationTool.csproj
dotnet run --project .\tools\KynticAI.Scout.MigrationTool -- --help
```

The `--help` output is the authoritative reference. On this release it is:

```text
Usage:
  dotnet run --project tools/KynticAI.Scout.MigrationTool -- export --tenant <tenant-slug> --out <local-folder> [options]

Options:
  --dry-run                    Validate locally and write reports without export batch files.
  --scope <items>              Comma-separated scopes. Defaults to current Scout migration scopes.
                               Supported aliases: all, relationship-inputs, tenant-metadata,
                               source-events, user-signals, selectors, selector-executions,
                               context-snapshots, context-facts, provenance, audit-events,
                               data-items, relationship-sets, attribution-paths, outcome-events, vectors.
  --max-records <number>       Records per batch. Default: 500.
  --checkpoint <token>         Resume from an export checkpoint.
  --provider <key>             Storage adapter provider. Default: configured StorageAdapter:Provider.
  --tenant-id <guid>           Optional tenant ID guard; slug and ID must match.
  --purpose <text>             Request purpose metadata. Default: scout-open-core-migration-export.
  --correlation-id <id>        Request correlation ID. Default: generated locally.
  --settings <path>            Optional extra local appsettings JSON file.

The tool writes only local files. It has no Cloud upload mode.
```

Running the tool with no arguments, `--help`, `-h`, or `help` prints this
usage text.

## Arguments and defaults

| Argument | Meaning | Default |
|---|---|---|
| `export` | The only supported command. | required |
| `--tenant <slug>` | Tenant slug to export. | required |
| `--out <folder>` | Local output folder. Created if it does not exist. | required |
| `--dry-run` | Validate locally and write `manifest.json`/`validation-report.json` without export batch files. | `false` |
| `--scope <items>` | Comma-separated scopes to export. | current Scout migration scopes (tenant-metadata, source-events, user-signals, selectors, selector-executions, context-snapshots, context-facts, provenance, audit-events) |
| `--max-records <number>` | Records per batch. Must be a positive integer. | `500` |
| `--checkpoint <token>` | Resume from an export checkpoint. | none (start fresh) |
| `--provider <key>` | Storage adapter provider. | configured `StorageAdapter:Provider` |
| `--tenant-id <guid>` | Optional tenant ID guard; slug and ID must match. | none |
| `--purpose <text>` | Request purpose metadata written into the export artefacts. | `scout-open-core-migration-export` |
| `--correlation-id <id>` | Request correlation ID. | generated locally (`scout-migration-<guid>`) |
| `--settings <path>` | Optional extra local `appsettings` JSON file to merge into configuration. | none |

The default purpose value `scout-open-core-migration-export` marks exported
artefacts as open-core migration exports. `--cloud-upload` and `--upload` are
rejected with an error: Scout migration exports are local-only.

## Output format and location

Everything is written under the local folder given by `--out`:

| File | Contents |
|---|---|
| `manifest.json` | Package metadata: package kind (`kynticai.scout.migration-export-package.v1`), contract version (`kynticai.scout.storage-portable-export.v1`), generated-at timestamp, tenant slug/ID, scope, batch settings, purpose, correlation ID, batch count, exported-record count, checkpoint state, and the list of files written. |
| `validation-report.json` | Validation outcome: `isValid`, records checked/exported, counts by record kind, findings, and any errors. Written even when validation fails. |
| `batches/batch-000001.json` … | One JSON batch file per export batch (up to `--max-records` records each). Not written in `--dry-run` mode. |

The tool validates as it exports and will not write a batch file for a failing
batch. On a failed run the exit code is non-zero and the error is printed to
stderr.

Certain sensitive material is always excluded from the export:

- connector credentials and `data_sources.connection_config_json`
- SaaS webhook signing secrets and API-client key material
- source-system event headers
- Data Protection key-ring files and local `.env` files
- licence, private key, and certificate files
- Cloud upload or staging locations

An adapter that reports Cloud data-plane use is rejected; the tool only
exports from local storage.

### Exit codes

| Code | Meaning |
|---|---|
| `0` | Success. |
| `1` | Usage error (bad arguments). |
| `2` | Validation failed (tenant not found, adapter does not support export, or an export batch failed validation). |
| `3` | Storage adapter unavailable or unhealthy. |
| `4` | I/O failure writing the output folder. |
| `5` | Unexpected error (including cancellation). |

## Worked example

From the repository root, export the `demo` tenant with default scopes into a
local folder:

```powershell
dotnet run --project .\tools\KynticAI.Scout.MigrationTool -- export --tenant demo --out .\migration\demo-export
```

When it completes the folder contains:

```text
migration/
└── demo-export/
    ├── manifest.json
    ├── validation-report.json
    └── batches/
        ├── batch-000001.json
        └── ...
```

To validate without writing batch files (useful before a real export):

```powershell
dotnet run --project .\tools\KynticAI.Scout.MigrationTool -- export --tenant demo --out .\migration\demo-dry-run --dry-run
```

## Relationship to Blueprint Import

[Blueprint Import](../README.md#rest-api-v1) (`POST /api/v1/blueprints/import`,
legacy `POST /blueprints/import`) is a separate open-core feature. It validates
and imports a **Scout blueprint** — data-source definitions, semantic
attributes, selectors, prompt templates, PII rules, and audit policies — as
configuration into a Scout deployment. The blueprint schema is published at
[`docs/scout-blueprint.schema.json`](scout-blueprint.schema.json).

The Migration Tool is the mirror for **data**: it exports context and evidence
records out of the local data plane. Importing exported context/evidence
records into another deployment is full enterprise migration import and is not
part of the open core. See [Open-Core Product Boundary](open-core-boundary.md)
for what belongs in the public repository.
