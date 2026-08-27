# Connector Plugin Model

KynticAI Scout now resolves source-system access through a registry-backed connector plugin model. The selector engine still receives raw and normalised payloads for a subject, but connector lifecycle concerns now live behind dedicated plugin interfaces.

## Goals

- Keep selector expression, validation, and fact materialization logic unchanged.
- Let multiple source-system connectors share one runtime contract.
- Support preview, dry run, scheduled sync, and event-triggered recompute modes.
- Return provenance, freshness, diagnostics, and health signals in a consistent shape.
- Persist credentials outside the selector-visible configuration JSON.

## Core Interfaces

- `IConnectorPlugin`
  - declares connector metadata, supported capabilities, config schema, validation, health checks, and fetch execution
- `IConnectorRegistry`
  - resolves a connector by canonical type or alias such as `crmApi`, `billingApi`, `telemetryApi`, `productTelemetry`, `supportApi`, `postgresql`, `csv`, or `fileUpload`
- `IConnectorCredentialStore`
  - stores secrets as protected `secret://...` references and resolves them at runtime
- `ConnectorConfigurationDescriptor`, `ConnectorConfigurationField`, `ConnectorEventShape`, and `ConnectorIngestEvent`
  - document the public config and event vocabulary used by connector authoring tools

Key files:

- `src/KynticAI.Scout.Application/Abstractions/IConnectorPlugin.cs`
- `src/KynticAI.Scout.Infrastructure/Connectors/ConnectorRegistry.cs`
- `src/KynticAI.Scout.Infrastructure/Connectors/ProtectedConnectorCredentialStore.cs`

## Built-In Plugins

The runtime registers nine `IConnectorPlugin` implementations
(`src/KynticAI.Scout.Infrastructure/DependencyInjection.cs`): eight demo/generic
plugins plus the `template` starter:

- `mock`
  - aliases: `mockPayload`, `mockSignal`, `fileUpload`
  - current use: deterministic demos, uploaded payload fixtures, safe file-style examples, and tests
  - kinds: all four data-source kinds
- `restApi`
  - aliases: `apiPayload`, `crmApi`, `billingApi`, `telemetryApi`, `productTelemetry`, `supportApi`
  - current use: generic HTTP-backed operational APIs and demo integrations
  - kinds: `Crm`, `EventStream`, `ProductUsage`, `SqlMetric`
- `sqlDatabase`
  - aliases: `sqlTable`, `postgresql`
  - current use: SQL and warehouse-style selectors against generic or demo schemas
  - kinds: `SqlMetric`, `Crm`, `ProductUsage`
- `csvUpload`
  - aliases: `csv`, `spreadsheetUpload`
  - current use: parsed CSV-style rows for demos and local tests; it does not watch arbitrary directories
  - kinds: `Crm`, `SqlMetric`, `ProductUsage`, `EventStream`
- `mockCrm`
  - aliases: `mock-crm`, `demoCrm`
  - current use: fictional CRM account, contact, and opportunity fields for demos and tests
  - kinds: `Crm`, `EventStream`
- `mockBilling`
  - aliases: `mock-billing`, `demoBilling`
  - current use: fictional plan, renewal, invoice, and payment signals for demos and tests
  - kinds: `SqlMetric`, `EventStream`
- `mockSupport`
  - aliases: `mock-support`, `demoSupport`
  - current use: fictional ticket and satisfaction signals for demos and tests
  - kinds: `Crm`, `EventStream`
- `inMemoryInventory`
  - alias: `demoInventory`
  - current use: fictional inventory data for connector-authoring examples
  - kinds: `ProductUsage`, `SqlMetric`
- `template`
  - aliases: none
  - current use: complete local template for community connector projects (`samples/connector-template`)
  - kinds: `Crm`

### Capabilities

`ConnectorPluginBase` declares the default capability set:

`FetchSubject`, `Preview`, `DryRun`, `ScheduledSync`, `EventTriggeredRecompute`,
`HealthCheck`, `ConfigurationValidation`, `SecureCredentialStorage`.

The demo business connectors (`mockCrm`, `mockBilling`, `mockSupport`) override
this and drop `ScheduledSync`. `inMemoryInventory` declares a narrower set
(`FetchSubject`, `Preview`, `DryRun`, `HealthCheck`,
`ConfigurationValidation`). The catalogue seed mirrors these in `GenericCapabilities()`
and `DemoCapabilities()`.

## Open core connector boundary

The public repository may include mock connectors, generic SQL examples, generic REST examples, and safe file/upload fixtures when they use fictional data and do not encode a customer-specific schema.

The public repository must not implement real enterprise connectors. Vendor-specific connectors, customer-specific mappings, managed sync implementations, credential vault integrations, and support-backed connector packages should live in a private enterprise repository and depend on the public connector contracts.

If a connector describes the generic protocol shape, it can be public. If it describes how to integrate a named vendor or customer estate, it should normally be private.

## Connector Configuration

All persisted connector configurations include a canonical `connectorType` field.

### SQL Example

The `customerOpsDatabase` mode below is **LocalDemo/reference-only**. It is rejected when the optional fictional CustomerOps reference store is disabled, including production-shaped Scout. For real external PostgreSQL sources use the explicit `connectionString` mode and keep credentials in the protected credential store.

```json
{
  "connectorType": "sqlDatabase",
  "mode": "customerOpsDatabase",
  "tableName": "customer_context_rollups",
  "tenantSlug": "demo",
  "tenantSlugColumn": "tenant_slug",
  "userIdColumn": "external_user_id",
  "observedAtColumn": "observed_at_utc",
  "columns": ["plan_interest_signal", "active_days_30"]
}
```

### REST Example

```json
{
  "connectorType": "restApi",
  "baseUrl": "https://api.example.com",
  "pathTemplate": "/v1/customers/{externalUserId}",
  "method": "GET",
  "observedAtPath": "meta.observedAtUtc",
  "credentials": {
    "apiKey": "secret://tenant/data-source/apiKey"
  }
}
```

### Mock Example

```json
{
  "connectorType": "mock",
  "records": [
    {
      "externalUserId": "123",
      "observedAtUtc": "2026-05-11T12:00:00Z",
      "payload": {
        "crm": {
          "preferredChannel": "email"
        }
      }
    }
  ]
}
```

Connector manifests can also declare a provider-neutral event shape:

```json
{
  "eventShape": {
    "sourceSystem": "sampleCrm",
    "entityType": "account",
    "sourceIdField": "externalUserId",
    "timestampField": "observedAtUtc",
    "payloadRoot": "payload"
  }
}
```

Runtime event objects should use `ConnectorIngestEvent` and pass `ConnectorContractRules.ValidateIngestEvent(...)` before being handed to event ingestion code.

## GraphQL

New GraphQL operations:

- `connectorPlugins`
- `registerConnector(input: RegisterConnectorInput!)`
- `validateConnectorConfiguration(input: ValidateConnectorConfigurationInput!)`
- `checkConnectorHealth(input: CheckConnectorHealthInput!)`

These operations reuse the application service and credential store instead of bypassing the domain model.

## Credential Storage

Secrets are stored in `connector_credentials` with protected values. Persisted connector configs only keep references:

```json
{
  "credentials": {
    "apiKey": "secret://<tenant>/<data-source>/apiKey"
  }
}
```

The selector engine resolves secrets through `IConnectorCredentialStore` immediately before plugin execution.

## Tests

Coverage added in:

- `tests/KynticAI.Scout.UnitTests/ConnectorPluginModelTests.cs`
- `tests/KynticAI.Scout.UnitTests/SelectorExecutionEngineTests.cs`
- `tests/KynticAI.Scout.IntegrationTests/GraphQlAuthorizationIntegrationTests.cs`

These tests cover alias resolution, secret persistence, preview-compatible REST behaviour, selector execution through the plugin registry, and GraphQL connector registration.

The public repository intentionally ships only generic connector contracts and safe example implementations. Premium commercial connector implementations are expected to live in a separate private enterprise repository.

For the wider public/private product boundary, see [open-core-boundary.md](open-core-boundary.md).
