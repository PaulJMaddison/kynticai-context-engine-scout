---
title: Connector Authoring Guide
description: Public connector authoring guide for KynticAI Scout.
---

Scout connectors implement the public `IConnectorPlugin` contract. A
connector fetches approved source data, normalises it into JSON, and
returns provenance so the selector engine can materialise semantic facts.

For the end-to-end walkthrough (scaffold, register, validate, health, fetch,
and provenance) see the repository tutorial at
`docs/connector-authoring-tutorial.md`. This page is the landing reference;
the repository guide at `docs/connector-authoring.md` is the full technical
specification for existing contributors.

## Public Contract

The contract is defined in
`src/KynticAI.Scout.Application/Abstractions/IConnectorPlugin.cs`.

Required members:

| Member | Purpose |
|---|---|
| `ConnectorType` | Unique connector identifier. |
| `DisplayName` | Name shown in admin/catalogue surfaces. |
| `Description` | Short technical summary. |
| `Aliases` | Optional alternate identifiers. |
| `SupportedDataSourceKinds` | Data-source categories the connector supports. |
| `SupportedCapabilities` | Fetch, preview, dry-run, health, validation, and credential capabilities. |
| `GetConfigurationSchema()` | JSON Schema for connector configuration. |
| `GetCredentialSchema()` | JSON Schema for secret fields. |
| `GetSampleConfiguration()` | Valid sample configuration. |
| `ValidateConfigurationAsync(...)` | Configuration validation. |
| `CheckHealthAsync(...)` | Connectivity and readiness check. |
| `FetchAsync(...)` | Source fetch returning payload, provenance, and observation times. |

## Recommended Starting Point

Copy the template:

```text
samples/connector-template/TemplateConnectorPlugin.cs
```

The template folder also ships `template-connector-config.json` and
`template-connector-manifest.json` for local validation, and the repository
tutorial (`docs/connector-authoring-tutorial.md`) runs the template through
registration, validation, health, fetch, and provenance with only local
dependencies.

Then:

1. Rename the connector class and `ConnectorType`.
2. Set `DisplayName`, `Description`, and supported data-source kinds.
3. Return a JSON Schema from `GetConfigurationSchema()`.
4. Return a JSON Schema from `GetCredentialSchema()` for secret inputs.
5. Return a sample configuration that satisfies all required fields.
6. Override validation for connector-specific checks.
7. Implement `FetchAsync(...)` without logging credentials or unnecessary raw payloads.
8. Register the plugin with dependency injection.
9. Add focused tests for metadata validation, configuration validation, health, and fetch.

## Fetch Result Shape

`FetchAsync(...)` returns `ConnectorFetchResult`:

| Field | Requirement |
|---|---|
| `RawPayloadJson` | Raw source response as JSON. Avoid extra fields that selectors do not need. |
| `NormalizedPayload` | `JsonObject` shape that selectors can traverse consistently. |
| `ProvenanceJson` | Source, record, timestamp, and mode metadata. |
| `ObservedAtUtc` | Time the source value was observed. |
| `FreshUntilUtc` | Optional freshness hint. |
| `DiagnosticsJson` | Safe diagnostics; no secrets or raw credentials. |

## Open-Core Boundary

Public connectors must stay generic and protocol-level:

- SQL/table-style reads
- REST API reads
- CSV/file imports
- mock-safe demo fixtures
- customer-authored custom connectors

Do not add vendor-specific enterprise connector implementations, private
customer schemas, managed sync code, credential vault integrations, private
product internals, private data-processing pipelines, or model-provider
behaviour to the Scout repo.

## Validation Helpers

`ConnectorMetadataValidator` checks common authoring mistakes:

- empty type/name/description
- missing data-source kinds
- missing capabilities
- malformed configuration schema
- malformed credential schema
- sample configuration missing required schema fields

Use the validator in tests before shipping a connector.

## Discovery Agent Check

Use `apps/discovery-agent` for an agent-readable handover before larger
connector changes:

```bash
cd apps/discovery-agent
npm install
npm run build
node dist/index.js --path ../.. --tier 2
```

The connector-metadata MCP package still provides
`scout_validate_connector_manifest_v2` for manifest validation. The Discovery
Agent adds repo-wide audit and handover output.

## Related Docs

- [Connector Basics](/concepts/connector-basics/)
- [Discovery Agent](/operations/discovery-agent/)
- [Schema Reference](/schema-reference/)
- [REST API](/apis/rest/)
- [GraphQL API](/apis/graphql/)

Repository references:

- `docs/connector-authoring.md` — full technical specification.
- `docs/connector-authoring-tutorial.md` — end-to-end authoring tutorial.
- `docs/connector-plugin-model.md` — plugin, alias, and capability model.
- `docs/connector-marketplace.md` — catalogue labels, readiness labels, and
  the open-core boundary.
- `docs/connector-manifest-validator.md` and `docs/connector-test-harness.md` —
  local manifest validation tooling.
