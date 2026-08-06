# Connector Authoring Guide

This guide shows how to build a public KynticAI Scout connector without reading any private repository. A connector is a small .NET plugin that fetches subject-scoped operational data, returns a normalised JSON payload, and describes its configuration and event shape for authoring tools.

> New to authoring? Follow the end-to-end
> [Connector Authoring Tutorial](connector-authoring-tutorial.md) first: it walks
> from `samples/connector-template` through register, validate, health, fetch,
> and provenance. This page is the reference for the full contract.

The example below uses a fictional AcmeCRM API. It is intentionally generic: no vendor SDKs, no customer schemas, no CDC, no vector or embedding work, and no enterprise-only implementation details.

## Public Contract

Every connector implements `IConnectorPlugin` from `src/KynticAI.Scout.Application/Abstractions/IConnectorPlugin.cs`. In the Scout repo, extend `ConnectorPluginBase` from `src/KynticAI.Scout.Infrastructure/Connectors/ConnectorPluginBase.cs` unless you have a strong reason to implement the interface directly.

Required members:

| Member | Purpose |
|---|---|
| `ConnectorType` | Unique camelCase identifier, for example `acmeCrm`. |
| `DisplayName` | Human-readable admin UI name. |
| `Description` | Short public description of the connector. |
| `SupportedDataSourceKinds` | One or more public kinds: `Crm`, `SqlMetric`, `EventStream`, `ProductUsage`. |
| `GetConfigurationSchema()` | JSON Schema object for non-secret configuration. |
| `GetCredentialSchema()` | JSON Schema object for secret fields. Mark secrets with `"secret": true`. |
| `GetSampleConfiguration()` | Safe example config that satisfies every required schema field. |
| `ValidateConfigurationAsync(...)` | Checks data-source kind plus connector-specific config rules. |
| `CheckHealthAsync(...)` | Read-only connectivity or local-shape check. |
| `FetchAsync(...)` | Fetches one subject and returns `ConnectorFetchResult`. |

The public model also includes:

| Model | Purpose |
|---|---|
| `ConnectorConfigurationDescriptor` | Stable description of schema, credentials, sample config, and config fields. |
| `ConnectorConfigurationField` | A single config field with type, required flag, secret flag, and optional default JSON. |
| `ConnectorEventShape` | Provider-neutral event declaration: source system, entity type, source ID field, timestamp field, and payload root. |
| `ConnectorIngestEvent` | Runtime event shape for connector-emitted events: source system, source ID, entity type, raw payload, and UTC timestamp. |
| `ConnectorContractRules` | Small validation helper for public event contracts. |

## Build AcmeCRM

Start from `samples/connector-template/TemplateConnectorPlugin.cs`. Rename the class and connector ID, then keep the shape below.

```csharp
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Domain.Enums;
using KynticAI.Scout.Infrastructure.Connectors;

internal sealed class AcmeCrmConnectorPlugin(IHttpClientFactory httpClientFactory)
    : ConnectorPluginBase
{
    public override string ConnectorType => "acmeCrm";
    public override string DisplayName => "AcmeCRM Connector";
    public override string Description => "Fetches account health and opportunity signals from a generic CRM-style API.";
    public override IReadOnlyList<DataSourceKind> SupportedDataSourceKinds => [DataSourceKind.Crm];
    public override IReadOnlyList<string> Aliases => ["acme"];

    public override JsonObject GetConfigurationSchema() => new()
    {
        ["type"] = "object",
        ["required"] = new JsonArray("baseUrl", "tenantSlug"),
        ["properties"] = new JsonObject
        {
            ["baseUrl"] = new JsonObject { ["type"] = "string" },
            ["tenantSlug"] = new JsonObject { ["type"] = "string" },
            ["pathTemplate"] = new JsonObject { ["type"] = "string", ["default"] = "/v1/accounts/{externalUserId}" },
            ["observedAtPath"] = new JsonObject { ["type"] = "string", ["default"] = "meta.observedAtUtc" }
        }
    };

    public override JsonObject GetCredentialSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["apiKey"] = new JsonObject { ["type"] = "string", ["secret"] = true }
        }
    };

    public override JsonObject GetSampleConfiguration() => new()
    {
        ["baseUrl"] = "https://api.example.com",
        ["tenantSlug"] = "demo",
        ["pathTemplate"] = "/v1/accounts/{externalUserId}",
        ["observedAtPath"] = "meta.observedAtUtc"
    };

    public override async Task<ConnectorConfigurationValidationResult> ValidateConfigurationAsync(
        ConnectorConfigurationValidationRequest request,
        CancellationToken cancellationToken)
    {
        var baseline = await base.ValidateConfigurationAsync(request, cancellationToken);
        var errors = baseline.Errors.ToList();

        var baseUrl = request.Configuration["baseUrl"]?.GetValue<string>();
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add("AcmeCRM connector requires an absolute http or https baseUrl.");
        }

        if (string.IsNullOrWhiteSpace(request.Configuration["tenantSlug"]?.GetValue<string>()))
        {
            errors.Add("AcmeCRM connector requires tenantSlug.");
        }

        return baseline with { IsValid = errors.Count == 0, Errors = errors };
    }

    public override async Task<ConnectorFetchResult> FetchAsync(
        ConnectorFetchRequest request,
        CancellationToken cancellationToken)
    {
        var baseUrl = request.Configuration["baseUrl"]!.GetValue<string>().TrimEnd('/');
        var template = request.Configuration["pathTemplate"]?.GetValue<string>() ?? "/v1/accounts/{externalUserId}";
        var uri = baseUrl + template.Replace("{externalUserId}", Uri.EscapeDataString(request.Subject.ExternalUserId), StringComparison.Ordinal);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, uri);
        if (request.Credentials["apiKey"]?.GetValue<string>() is { Length: > 0 } apiKey)
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        var client = httpClientFactory.CreateClient("scout-connectors");
        using var response = await client.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var payload = JsonNode.Parse(rawJson) as JsonObject
            ?? throw new InvalidOperationException("AcmeCRM response must be a JSON object.");

        var observedAtUtc = payload["meta"]?["observedAtUtc"] switch
        {
            JsonValue value when value.TryGetValue<DateTime>(out var dateTime) => dateTime,
            JsonValue value when value.TryGetValue<string>(out var stringValue) && DateTime.TryParse(stringValue, out var parsed) => parsed,
            _ => DateTime.UtcNow
        };
        return new ConnectorFetchResult(
            rawJson,
            payload,
            JsonSerializer.Serialize(new[]
            {
                new
                {
                    source = ConnectorType,
                    request.Subject.ExternalUserId,
                    observedAtUtc,
                    mode = request.Mode.ToString()
                }
            }),
            observedAtUtc,
            null,
            "{}");
    }
}
```

Register it in dependency injection:

```csharp
services.AddScoped<IConnectorPlugin, AcmeCrmConnectorPlugin>();
```

## Manifest

Connector authors should also ship a manifest for local validation tools. Keep it safe for publication.

```json
{
  "connectorId": "acmeCrm",
  "displayName": "AcmeCRM Connector",
  "version": "1.0.0",
  "description": "Fetches account health and opportunity signals from a generic CRM-style API.",
  "supportedSourceTypes": ["Crm"],
  "requiredConfigFields": [
    {
      "name": "baseUrl",
      "type": "string",
      "description": "Base URL for the AcmeCRM-compatible API."
    },
    {
      "name": "tenantSlug",
      "type": "string",
      "description": "Tenant or workspace slug used by the upstream API."
    }
  ],
  "safeMetadataFields": ["connectorId", "displayName", "version", "supportedSourceTypes"],
  "sampleEntityMappings": [
    {
      "sourceField": "deal_probability",
      "semanticAttribute": "conversionProbability",
      "description": "Maps deal probability to the Scout conversion probability attribute."
    }
  ],
  "capabilities": ["FetchSubject", "Preview", "HealthCheck", "ConfigurationValidation"],
  "configurationSchema": {
    "type": "object",
    "required": ["baseUrl", "tenantSlug"],
    "properties": {
      "baseUrl": { "type": "string" },
      "tenantSlug": { "type": "string" },
      "pathTemplate": { "type": "string", "default": "/v1/accounts/{externalUserId}" }
    }
  },
  "sampleConfiguration": {
    "baseUrl": "https://api.example.com",
    "tenantSlug": "demo",
    "pathTemplate": "/v1/accounts/{externalUserId}"
  },
  "eventShape": {
    "sourceSystem": "acmeCrm",
    "entityType": "account",
    "sourceIdField": "externalUserId",
    "timestampField": "observedAtUtc",
    "payloadRoot": "payload"
  }
}
```

## Fetch Result Rules

`FetchAsync` must return:

- `RawPayloadJson`: the raw source JSON string.
- `NormalizedPayload`: a JSON object the selector engine can traverse.
- `ProvenanceJson`: a JSON array describing source, subject, observation time, and mode.
- `ObservedAtUtc`: when the source data was observed.
- `FreshUntilUtc`: optional freshness expiry.
- `DiagnosticsJson`: non-secret diagnostics such as status code or timing.

Take `ObservedAtUtc` from the source response when the source provides it (for
example the configured `observedAtPath`) so provenance is deterministic: the
same fixture produces the same result on every run. Only fall back to
`DateTime.UtcNow` when the source genuinely does not expose a timestamp.

Never include raw credentials in `RawPayloadJson`, `NormalizedPayload`, `ProvenanceJson`, or `DiagnosticsJson`.

## Validation Checklist

Before submitting a connector, walk the [Connector Authoring Tutorial](connector-authoring-tutorial.md)
end to end, then:

1. Run `ConnectorMetadataValidator.Validate(plugin)` in a unit test.
2. Verify `GetSampleConfiguration()` satisfies every required schema field.
3. Keep secret fields in `GetCredentialSchema()` and use `secret://` references in persisted samples.
4. Validate `ConnectorIngestEvent` values with `ConnectorContractRules.ValidateIngestEvent(...)` when emitting event-shaped records.
5. Run the TypeScript manifest validator or harness:

```bash
cd packages/typescript/scout-connector-validator
npm test

cd ../scout-connector-test-harness
npm test
```

## Discovery Agent Check

Use the canonical Discovery Agent when you want an agent-readable handover for
connector work:

```bash
cd apps/discovery-agent
npm install
npm run build
node dist/index.js --path ../.. --tier 2
```

The connector-metadata MCP package still exposes
`scout_validate_connector_manifest_v2` for manifest validation. The Discovery
Agent adds repo-wide audit, handover, and governance output.

For buyer-facing discovery, use the `kyntic-discovery-mcp` wrapper after a
manifest is ready for IT review:

```bash
cd apps/discovery-agent
npm install
npm run build
node dist/kyntic-discovery-mcp.js --manifest ../../samples/connector-template/template-connector-manifest.json
```

The wrapper validates the manifest locally, runs the metadata quality report,
and can include a metadata-only manifest summary in a
`kynticai.discovery-signature.v1` draft that IT reviews. Do not add sample
records, exports, ticket contents, database dumps, credentials, tokens,
connection strings, raw payloads, source documents, PII, vectors, embeddings,
prompt packages, local logs, or private connector implementation details to the
signature draft.

## n8n Event Sink

The local `packages/typescript/n8n-node` package provides a write-only n8n node
that maps incoming n8n items to the public source-system event endpoint. It is
an event-ingestion bridge, not a private connector implementation. See
`docs/n8n-node.md` for local build and mapping details.

## Built-In Public References

| Connector | Type | Boundary |
|---|---|---|
| Generic SQL | `sqlDatabase` | Generic SQL/PostgreSQL subject row fetches. No vendor-specific warehouse logic. |
| Generic REST | `restApi` | Generic HTTP subject payload fetches with bearer, API key, or basic auth. |
| CSV upload | `csvUpload` | Parsed row input for demos and local tests. It does not watch arbitrary directories or process untrusted files. |
| In-memory inventory | `inMemoryInventory` | Fictional data for authoring examples. |
| Template | `template` | Complete local template for new connector projects. |

## Open-Core Boundary

Public Scout connectors may include generic protocol connectors, fictional demo data, authoring templates, and local validation tools.

Do not add vendor-specific connector code, private mappings, managed sync implementations, change-data-capture pipelines, embedded model calls, vector-store pipelines, credential vault integrations, or private planning material to this repository.
