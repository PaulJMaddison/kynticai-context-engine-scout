# Connector Authoring Tutorial

This tutorial walks a connector author from scaffold to a working, verified
KynticAI Scout connector: copy the template, understand the contract rules,
register the connector, validate it, check its health, fetch a subject, and
observe provenance in a selector preview. Every step runs with the safe local
default: no Docker, no external services, and no live vendor connections.

The companion reference is [connector-authoring.md](connector-authoring.md);
the plugin and catalogue model is described in
[connector-plugin-model.md](connector-plugin-model.md).

## 1. Scaffold from the template

Start from `samples/connector-template`:

| File | Purpose |
|---|---|
| `TemplateConnectorPlugin.cs` | Skeleton connector implementing `ConnectorPluginBase`. |
| `template-connector-config.json` | Sample configuration JSON for the template connector. |
| `template-connector-manifest.json` | Public manifest used by the local validation tooling. |
| `README.md` | Short authoring notes and constraints. |

The template is a single C# source file, not a standalone project: the copy
in `src/KynticAI.Scout.Infrastructure/Connectors/TemplateConnectorPlugin.cs`
is compiled as part of the Infrastructure project, and the two files are kept
byte-for-byte identical. When you author your own connector, copy the file
into your own project, rename the class, and register it as `IConnectorPlugin`:

```csharp
services.AddScoped<IConnectorPlugin, AcmeCrmConnectorPlugin>();
```

Check that your scaffold is still correct after you rename it:

```powershell
# The template tests cover the scaffold: implements the contract, passes
# metadata validation, has a valid JSON Schema, accepts the sample config,
# and fetches a known subject.
dotnet test .\tests\KynticAI.Scout.UnitTests\KynticAI.Scout.UnitTests.csproj --filter "FullyQualifiedName~ConnectorAuthoringTests|FullyQualifiedName~ConnectorPluginModelTests"
```

The template ships no `.csproj`, so its build and test coverage is provided by
these unit tests (`TemplateConnector_*` in
`tests/KynticAI.Scout.UnitTests/ConnectorAuthoringTests.cs`) plus the
byte-for-byte diff against the Infrastructure copy. See the verification
notes at the end of this page for the commands.

## 2. Contract rules recap

Every public connector must follow these rules. They are enforced by tests and
by the runtime:

- **Deterministic provenance.** `FetchAsync` returns a `ConnectorFetchResult`
  whose `ProvenanceJson` names the source, the subject, the observation time,
  and the run mode. Prefer the source's own observation timestamp (the
  template reads `observedAtUtc` from the record) over `DateTime.UtcNow`, so
  repeated runs over the same fixture produce the same provenance.
- **`secret://` credential references.** Never persist plaintext credentials
  in connector configuration. Persisted configs keep references such as
  `secret://<tenant>/<data-source>/apiKey`; `ProtectedConnectorCredentialStore`
  resolves them immediately before plugin execution, and
  `ConnectorMetadataValidator` rejects raw secret values in sample
  configuration.
- **Event shape validation.** Event-shaped records must pass
  `ConnectorContractRules.ValidateIngestEvent(...)`, which requires a
  `sourceSystem`, a `sourceId`, an `entityType`, a non-empty `rawPayload`, and
  a UTC `timestampUtc`.
- **No external AI models.** A connector fetches and normalises operational
  data; it must not call AI models, embed content, or run vector pipelines.
- **No local persistence of customer data beyond the contract.** Connectors
  return payloads and provenance to the selector engine; they must not store
  customer extracts in the repository or on the local machine.

## 3. Register the connector

`registerConnector` creates a DataSource backed by your plugin and persists
any supplied credentials as protected references.

### GraphQL

```graphql
mutation RegisterConnector($input: RegisterConnectorInput!) {
  registerConnector(input: $input) {
    dataSourceId
    connectorType
    status
  }
}
```

Variables:

```json
{
  "input": {
    "tenantSlug": "demo",
    "name": "Template Data Source",
    "description": "Local template connector data source.",
    "kind": "Crm",
    "connectorType": "template",
    "configurationJson": "{ \"records\": [ { \"externalUserId\": \"123\", \"observedAtUtc\": \"2026-01-15T09:00:00Z\", \"payload\": { \"status\": \"active\", \"score\": 85 } } ] }"
  }
}
```

The service injects the canonical `connectorType` into the persisted
configuration, so the stored config always carries it even if the input omits
it. The response is a `ConnectorRegistrationResult` with a `dataSourceId`
(GUID), the sanitised configuration JSON, and the DataSource status.

### REST / API-client path

The legacy tenant-admin API exposes the same operations:

```http
POST /api/rest/connectors/register
```

with the same `RegisterConnectorInput` fields. Roles: PlatformOwner,
TenantAdmin, or IntegrationAdmin. The equivalent read-only list is
`GET /api/rest/connectors/plugins`, and the public v1 catalogue is
`GET /api/v1/connectors/catalogue` (no auth; optional `availability`,
`category`, `q`, `page`, `pageSize` query parameters).

If your connector uses credentials, include a `credentialsJson` with the
secret values; the service persists them through
`ProtectedConnectorCredentialStore` and rewrites the stored config to
`secret://` references. Use the DataSource `id` from the registration response
for the health check below.

## 4. Validate and check health

Validate a configuration before you register it (or re-validate an existing
one), and check health against the registered DataSource.

### GraphQL

```graphql
mutation ValidateConnectorConfiguration($input: ValidateConnectorConfigurationInput!) {
  validateConnectorConfiguration(input: $input) {
    connectorType
    isValid
    errors
    sanitizedConfigurationJson
    configurationSchemaJson
  }
}

mutation CheckConnectorHealth($input: CheckConnectorHealthInput!) {
  checkConnectorHealth(input: $input) {
    dataSourceId
    connectorType
    isHealthy
    status
    messages
    detailsJson
    checkedAtUtc
  }
}
```

`validateConnectorConfiguration` takes `connectorType`, `kind`, and
`configurationJson` (plus optional `credentialsJson`); it runs the plugin's
`ValidateConfigurationAsync` and returns errors and the sanitised config.
`checkConnectorHealth` takes `tenantSlug`, `dataSourceId`, and optional
`externalUserId` and `mode`; it resolves secrets, loads the DataSource
configuration, and returns the plugin's health result.

### REST / API-client path

```http
POST /api/rest/connectors/validate
POST /api/rest/connectors/health
```

### Local manifest tooling

The TypeScript packages validate the manifest without running Scout:

```bash
cd packages/typescript/scout-connector-test-harness
npm install && npm run build

# Validate the template manifest (manifest shape, metadata audit, entity
# mappings, unsafe-field blocklist, and optional auth config)
node dist/cli.js ../../../samples/connector-template/template-connector-manifest.json
```

The manifest validator CLI is equivalent:

```bash
cd packages/typescript/scout-connector-validator
node dist/cli.js ../../../samples/connector-template/template-connector-manifest.json
```

Both are described in [connector-manifest-validator.md](connector-manifest-validator.md)
and [connector-test-harness.md](connector-test-harness.md).

## 5. Use the connector: DataSource, fetch, and provenance

Registering the connector already created the DataSource. Selectors run through
`SelectorExecutionEngine`, which resolves the connector from the stored
configuration (`connectorType`), resolves any `secret://` references, calls the
plugin's `FetchAsync`, normalises the payload, applies the selector rules, and
builds provenance and a pipeline trace. If the stored configuration has no
`connectorType`, the engine falls back to the `mock` connector.

Preview a selector to observe the provenance produced by your connector:

```http
POST /api/v1/selectors/preview
```

```json
{
  "externalUserId": "123",
  "draftSelector": {
    "tenantSlug": "demo",
    "dataSourceId": "<dataSourceId from registerConnector>",
    "targetAttributeDefinitionId": "<an existing attribute GUID>",
    "name": "Template engagement",
    "description": "Maps the template score into the engagement level attribute.",
    "mappingKind": "DirectFieldMapping",
    "expressionJson": "{ \"transforms\": [], \"rule\": { \"valuePath\": \"payload.score\" } }",
    "explanationTemplate": "Score is {{value}}.",
    "validationSchemaJson": "{ \"requiredPaths\": [ \"payload.score\" ] }",
    "defaultConfidence": 0.8,
    "freshnessWindowMinutes": 60,
    "priority": 100
  }
}
```

(The equivalent GraphQL mutation is `previewSelector(input: PreviewSelectorInput!)`.)

The response is a `SelectorExecutionPreviewResult`. Two fields are your
audit trail:

- `provenanceJson` — includes the `selector` summary, the resolved
  `connectorType`, the `source` block from your `ConnectorFetchResult.ProvenanceJson`,
  `validationErrors`, the applied `transforms`, the `rule` trace, and the
  `confidence` score.
- `pipelineTraceJson` — the full trace with `rawSourceObservedAtUtc`, the
  `normalizedPayload`, transforms, rule trace, `explanation`, and `confidence`.

Connectors that emit events send them through the source-system event API
(`POST /api/v1/events/source-system`, or per-DataSource
`POST /api/v1/connectors/{dataSourceId}/events/source-system`). The
`productTelemetryEvents` and `firstPartyConversionEvents` catalogue entries are
public event-contract examples of this path.

## 6. Marketplace placement

After you publish a connector, it appears in the connector catalogue if it is
seeded by `ConnectorCatalogueSeeder`. Open-core entries are executable; paid or
planned entries are metadata-only placeholders:

- `availability`: `OpenCore`, `Enterprise`, `SaaSManaged`, or `ComingSoon`.
- `isPlaceholder`: placeholder rows are catalogue metadata only. They register
  no runtime plugin and must never be presented as vendor-certified.
- Placeholder health-check text is "Unavailable in open source; safe metadata
  only."

The web console derives readiness labels from the same catalogue row
(`apps/web/src/features/connectors/connector-readiness.ts`):

| Label | Shown when |
|---|---|
| Executable open-core | Registered plugin path available in the public build. |
| Mock/local proof | Demo/dry-run/approved-export proof path. |
| Private/customer-specific | Enterprise/SaaS-managed or customer-specific listing. |
| Placeholder | `isPlaceholder` or coming-soon entry. |
| Not vendor-certified | Always shown; metadata is never a certification claim. |

See [connector-marketplace.md](connector-marketplace.md) for the full public
catalogue boundary.

## Verification notes

- Connector unit tests (ConnectorAuthoringTests + ConnectorPluginModelTests):
  27 tests pass with the safe local default, covering template validation,
  metadata validation, `ConnectorContractRules.ValidateIngestEvent`, alias
  resolution, secret persistence, and the open-core catalogue boundary.
- The harness CLI against the template manifest passes 21/21 checks; the
  validator CLI reports "All valid".
- The template carries no standalone `.csproj`; there is no separate template
  build command to run. Its build and test coverage is the Infrastructure
  project plus the unit tests above, and the byte-for-byte diff between
  `samples/connector-template/TemplateConnectorPlugin.cs` and
  `src/KynticAI.Scout.Infrastructure/Connectors/TemplateConnectorPlugin.cs`.
