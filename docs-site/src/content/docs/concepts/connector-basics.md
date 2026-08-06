---
title: Connector Basics
description: How KynticAI Scout connectors bring data from existing systems into the semantic layer.
---

Connectors are the bridge between your existing operational systems and the
Scout semantic layer. They fetch raw data from source systems so that the
Selector Engine can transform it into governed semantic facts.

## How Connectors Work

```
Source System ──► Connector ──► Raw Payload ──► Selector Engine ──► Semantic Facts
```

1. A **connector** connects to a source system (database, REST API, file,
   or mock fixture) and retrieves raw records for a given subject.
2. The **Selector Engine** applies admin-authored mapping rules to turn raw
   fields into canonical semantic attributes.
3. The resulting **semantic facts** carry confidence scores, provenance, and
   freshness metadata.

## Built-in Connector Types

Scout registers nine connector plugins in the open-source core. Eight are
generic or demo plugins and one is the authoring template:

| Type | Aliases | Description |
|---|---|---|
| `mock` | `mockPayload`, `mockSignal`, `fileUpload` | Deterministic demo fixtures, signal-backed previews, and tests (all data-source kinds) |
| `restApi` | `apiPayload`, `crmApi`, `billingApi`, `telemetryApi`, `productTelemetry`, `supportApi` | HTTP-backed operational APIs |
| `sqlDatabase` | `sqlTable`, `postgresql` | Generic SQL queries against relational databases |
| `csvUpload` | `csv`, `spreadsheetUpload` | Parsed CSV-style rows for demos and local tests |
| `mockCrm` | `mock-crm`, `demoCrm` | Fictional CRM account, contact, and opportunity signals |
| `mockBilling` | `mock-billing`, `demoBilling` | Fictional plan, renewal, invoice, and payment signals |
| `mockSupport` | `mock-support`, `demoSupport` | Fictional ticket and satisfaction signals |
| `inMemoryInventory` | `demoInventory` | Fictional inventory records for authoring examples |
| `template` | — | Starter connector for community authoring (`samples/connector-template`) |

These connectors are generic by design. They demonstrate the connector
contract without encoding vendor-specific logic. The `mockCrm`, `mockBilling`,
and `mockSupport` plugins drop the `ScheduledSync` capability; the
`inMemoryInventory` plugin declares only fetch, preview, dry-run, health, and
configuration-validation capabilities.

## Connector Plugin Contract

Every connector implements the `IConnectorPlugin` interface, which declares:

- **Metadata** — connector type, display name, and supported capabilities.
- **Configuration schema** — what settings the connector requires.
- **Validation** — checks that a configuration is well-formed before use.
- **Health check** — verifies connectivity to the source system.
- **Fetch** — retrieves raw records for a given subject.

The `IConnectorRegistry` resolves connectors by canonical type or alias at
runtime.

## Credential Storage

Connector credentials are stored as protected references (`secret://…`)
rather than inline values. At execution time, the `IConnectorCredentialStore`
resolves these references so that secrets are never exposed in configuration
JSON.

```json
{
  "credentials": {
    "apiKey": "secret://tenant/data-source/apiKey"
  }
}
```

## Open-Source Boundary

The public repository ships generic connector contracts and safe example
implementations. It intentionally does not include vendor-specific
enterprise connectors.

Commercial connectors for services such as Salesforce, HubSpot, Dynamics,
and others are available as private enterprise modules outside this public
repository. Enterprise connectors implement the same `IConnectorPlugin`
contract and plug into Scout without forking the core.

For enterprise connector enquiries, visit [kynticai.com](https://kynticai.com).

## Writing a Custom Connector

To build your own connector, start from the template in
`samples/connector-template`:

1. Copy `TemplateConnectorPlugin.cs` into your own project and rename the
   class.
2. Implement `IConnectorPlugin` (extend `ConnectorPluginBase`) in that
   assembly.
3. Register it with the `IConnectorRegistry` via dependency injection.
4. Provide a configuration schema and validation logic.
5. Return raw payloads from `FetchAsync` — the Selector Engine handles
   semantic mapping.

Walk the full register → validate → health → fetch → provenance flow in the
[Connector Authoring](/connectors/authoring/) guide.

## Next Steps

- [Connector Authoring](/connectors/authoring/) for the public connector
  contract and the end-to-end tutorial.
- [Open Source vs Enterprise](/concepts/open-source-vs-enterprise/) for the
  full product boundary.
- [API Overview](/apis/overview/) for querying context produced by connectors.
