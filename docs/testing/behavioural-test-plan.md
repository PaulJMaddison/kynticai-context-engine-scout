# Scout Behavioural Test Plan

Scope: `KynticAI.Scout` (public open-source Context Engine proof). Maintained by the
WP-00/01 final code quality check loop. For each area this records the behaviour being
protected, currently tested contexts, missing contexts, risk if the behaviour fails,
tests to add, and the appropriate test level.

Reference: `C:\Kyntic\kynticai-workspace-docs\work-packages\2026-08-10-final-code-quality-check\00-final-code-quality-check-standard.md`
and `01-final-code-quality-check-ucl-scout.md`.

## Summary Of Priorities

| Priority | Area | Confidence | Highest remaining risk |
|---|---|---|---|
| P1 | Event / data-item ingestion and validation | High | Negative validation paths for the UCL data-item contract |
| P2 | Identity / data-quality relationships, aliases, duplicate candidates | High | Out-of-order and duplicate identity resolution in attribution paths |
| P3 | Ordered attribution-path construction by timestamp | High | Rejection of out-of-order / non-increasing sequences |
| P4 | Outcome / value weighting fallback logic | High | Fallback objective synonym handling, unknown objectives |
| P5 | Governed JSON packet creation and schema validation | High | Rejection of forbidden cloud payload shapes |
| P6 | Local API / CLI / service boundaries | High | Idempotent event ingest, signed webhook replay |
| P7 | PostgreSQL/pgvector or local persistence behaviour | Medium | Export/import boundary, UTC normalisation, secret-key guard |
| P8 | Connector / import seams and malformed-but-valid source data | Medium | Connector metadata validation rejections |
| P9 | Permission / privacy boundary | High | No raw customer data in cloud or audit payloads |

## Area Detail

### P1/P2/P3 — UCL data-item attribution contract (`UclDataItemAttributionV1Validator`)

Behaviour protected:
- Data items carry the exact contract kind/version/data-plane and required metadata.
- Every data item has at least one identity with type, value, normalised value and scope.
- Duplicate `dataItemId` values are rejected.
- `exactPayload` must be a JSON object.
- Relationship sets require a known subject data item and edges that reference known items.
- Attribution paths require strictly increasing `Sequence` values and preserve observed
  event order (`OccurredAtUtc` never goes backwards).
- Outcomes reference known data items and carry non-empty type fields.
- Enterprise analysis input requires public fallback scope, no cloud control plane, no
  enterprise-only internals, and the three required outputs.
- Cloud aggregate control-plane payloads allow only whitelisted root/counter/boundary
  properties and forbid raw data shapes anywhere in the JSON.

Currently tested contexts (`tests/KynticAI.Scout.UnitTests/UclDataItemAttributionContractTests.cs`):
- Fixture `synthetic-source-data-items.json` and `local-exact-item-export.json` validate and match.
- Identity linking by email / cookie / account across fixture data items.
- Fixture attribution paths preserve sequence and observed order; possible actions present.
- Relationship sets are fallback-only scope with historical outcomes.
- Enterprise input fixture validates; cloud control-plane fixture validates and one unsafe
  inline payload (with raw `dataItems`) is rejected.
- JSON schema files define the expected DTO names.

Missing contexts (high risk):
- Rejection of out-of-order attribution events (decreasing or repeated `Sequence`).
- Rejection of out-of-order `OccurredAtUtc` timestamps that contradict the sequence.
- Rejection of duplicate `dataItemId` values.
- Rejection of empty identity lists and unknown data-item references in edges, paths,
  outcomes and citations.
- Rejection of non-object `exactPayload`.
- Boundary checks: `confidence` outside `[0,1]`, zero-length edge/path/outcome lists.

Tests to add:
- Negative-path unit tests over `UclDataItemAttributionV1Validator` with hand-built
  `DataItem` / `RelationshipSet` / `AttributionPath` shapes.

Currently covered after the 2026-08-11 iteration
(`tests/KynticAI.Scout.UnitTests/UclDataItemAttributionValidatorNegativeTests.cs`):
- Duplicate `dataItemId` rejection; empty identity list rejection; non-object
  `exactPayload` rejection.
- Unknown subject data item; unknown event data item; unknown edge citation;
  unknown outcome data item.
- Attribution path with reversed sequence (non-increasing) rejected; path with
  timestamps that contradict observed order rejected.
- Edge `confidence` outside `[0,1]` rejected.
- Relationship set with enterprise scope rejected as not public-fallback.
- Enterprise input that requires a cloud control plane rejected; enterprise input
  missing required enterprise outputs rejected.
- Cloud payload with forbidden identity property, missing required counter, or a
  `dataBoundary` flag that leaks raw data, all rejected.

Appropriate level: unit (no persistence required).

### P4 — Outcome / value weighting fallback (`BasicRelationshipEngine`, `NextActionIntelligenceService`)

Behaviour protected: fallback-only relationship weights are deterministic per type and
objective; scout weights are declared non-canonical; enterprise owns canonical weighting.
Currently tested: one happy-path weight, ownership metadata, and an integration of the
weighting contract into the evidence pack. Missing contexts: objective synonyms
(`sales`/`sell`, `convert`, `retain`), unknown objective fallback weight, and the full
weight table across relationship types. Tests to add: parameterised weight-table test.

### P5 — Governed JSON packets (`UclEvidencePackV1`, enterprise handoff, cloud usage)

Behaviour protected: local evidence packs carry exact records and citations; cloud
payloads never contain raw records, fields, recommendations, citation IDs or derived
intelligence; enterprise handoff excludes private weight internals.
Currently tested: happy paths, forbidden-property scanning, unsafe cloud payload examples,
handoff required outputs. Missing contexts: mostly covered; keep regression green.

### P6 — Local API / service boundaries (REST v1, GraphQL, webhook signing)

Behaviour protected: source-system events are tenant-scoped, idempotent by
source-system+event-id, replay-rejected when signed, and dead-lettered when no user
profile matches. Currently tested: idempotency, cross-tenant rejection, bad signature,
dead-letter, connector-bound route, secret rotate/revoke/replay, GraphQL history filter.
Missing contexts: none identified as high risk.

### P7 — Storage / migration export boundary (`ScoutPostgresStorageAdapter`, MigrationTool)

Behaviour protected: export is local-only, no cloud data plane, UTC-normalised
timestamps, secret/credential key rejection, paged batches, dry-run reports, tenant
guard, vector writes skipped without a private runtime. Currently tested: capabilities,
health, export paging, tenant metadata, header exclusion, UTC normalisation, provenance
promotion, secret rejection, dry run, vector rejection, migration tool manifests and
batch files. Missing contexts: none identified as high risk.

### P8 — Connector seams (`ConnectorMetadataValidator`, connector plugins)

Behaviour protected: connector manifests reject missing IDs, malformed URLs, duplicate
scopes, unsafe defaults, raw secret sample values, and empty records. Currently tested:
comprehensive rejection matrix in `ConnectorAuthoringTests` and `ConnectorPluginModelTests`.
Missing contexts: none identified as high risk.

### P9 — Permission / privacy boundary

Behaviour protected: read-only actors receive masked fields; cloud and audit payloads
exclude raw customer data and derived intelligence; validation failures do not echo
sensitive payloads. Currently tested: masking, cloud/audit exclusions, no-echo validation.
Missing contexts: none identified as high risk.

## Required WP-01 Scenarios Status

| Required scenario | Status | Evidence |
|---|---|---|
| Serious happy-path local proof | Covered | `NextActionIntelligenceServiceTests`, `V1RestApiIntegrationTests`, `GoldenPathE2ETests` |
| Alias / identity relationship | Covered | `SameEmail_LinksToContactAccountAndHistory`, identity fixture linking |
| Out-of-order timestamped events | **Covered by new negative tests** | `UclDataItemAttributionValidatorNegativeTests` |
| Duplicate / replayed event idempotency | Covered | `SourceSystemEvents_AreIdempotent...`, webhook replay tests |
| Invalid source payload rejection | Covered | Validator negative tests, `NextActionValidationFailures_DoNotEchoSensitivePayloads` |
| JSON schema / contract output | Covered | `UclEvidencePackContractTests`, `UclDataItemAttributionContractTests` |
| Persistence reload / restart boundary | Covered | `StorageAdapterBoundaryTests`, `MigrationTool_*` tests |
| Realistic-volume local fixture | Covered | `tests/proof/test_ucl_scale_failure_proof.py`, `scripts/ucl-scale-failure-proof.py` |

## Running The Checks

Safe default for this repo: `dotnet restore .\KynticAI.Scout.slnx`, `dotnet build .\KynticAI.Scout.slnx`,
`dotnet test .\tests\KynticAI.Scout.UnitTests\KynticAI.Scout.UnitTests.csproj`. No Docker,
live databases, or external proof is started by the routine checks.
