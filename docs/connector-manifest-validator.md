# Connector Manifest Validator

The `@kynticai/scout-connector-validator` package provides a public manifest schema and validator for KynticAI Scout connectors. It is used by connector authors to validate their manifest files before submission and is integrated with the Discovery Agent MCP server.

## Overview

Every public Scout connector must ship a manifest file that describes the connector's identity, supported source types, required configuration fields, safe metadata fields, and sample entity mappings. The validator enforces the public schema and rejects manifests that expose credential or PII field names.

## Manifest Format

A connector manifest is a JSON file with the following structure:

```json
{
  "connectorId": "myCrmConnector",
  "displayName": "My CRM Connector",
  "version": "1.0.0",
  "description": "Connects to a fictional CRM system for demo purposes.",
  "supportedSourceTypes": ["Crm"],
  "requiredConfigFields": [
    { "name": "endpoint", "type": "string", "description": "API base URL." },
    { "name": "tenantId", "type": "string", "description": "Tenant identifier." }
  ],
  "safeMetadataFields": ["connectorId", "displayName", "version", "supportedSourceTypes"],
  "sampleEntityMappings": [
    {
      "sourceField": "deal_probability",
      "semanticAttribute": "conversionProbability",
      "description": "Maps the CRM deal probability to the Scout conversion probability attribute."
    }
  ],
  "aliases": ["demoCrm"],
  "capabilities": ["FetchSubject", "Preview", "DryRun"],
  "configurationSchema": {
    "type": "object",
    "required": ["endpoint", "tenantId"],
    "properties": {
      "endpoint": { "type": "string", "description": "API base URL." },
      "tenantId": { "type": "string", "description": "Tenant identifier." }
    }
  },
  "sampleConfiguration": {
    "endpoint": "https://api.example-crm.com/v2",
    "tenantId": "demo-tenant-001"
  }
}
```

## Validation Rules

### Errors (hard failures)

| Rule | Detail |
|---|---|
| Connector ID format | Must start with a lowercase letter, camelCase, alphanumeric only. |
| Duplicate connector ID | Checked when a list of known IDs is provided. |
| Display name | Non-empty string. |
| Version | Must follow semantic versioning (`MAJOR.MINOR.PATCH`). |
| Description | Non-empty string. |
| Supported source types | Non-empty array. |
| Required config fields | Non-empty array; each entry must have `name`, `type` (valid JSON Schema type), and `description`. |
| Safe metadata fields | Must be an array. Rejects any field matching known credential/PII patterns. |
| Sample entity mappings | Non-empty array; each entry must have `sourceField` and `semanticAttribute`. |
| Configuration schema | If provided, must have `"type": "object"` and a `"properties"` key. |
| Sample configuration | If provided alongside a schema, must include all `required` fields. |

### Warnings (non-blocking)

| Rule | Detail |
|---|---|
| Unknown source type | Source type not in the known public list. |
| Unknown semantic attribute | Attribute not in the 13 reserved public keys. |
| Unknown capability | Capability not in the known public list. |
| Duplicate metadata field | Same field appears more than once in `safeMetadataFields`. |

## Unsafe Field Names

The following field names are rejected in `safeMetadataFields` to prevent credential or PII exposure:

`password`, `secret`, `token`, `credential`, `apiKey`, `apiSecret`, `accessToken`, `refreshToken`, `privateKey`, `connectionString`, `ssn`, `socialSecurityNumber`, `creditCard`, `creditCardNumber`, `cvv`, `pin`, `encryptionKey`, `masterKey`, `sessionToken`, `bearerToken`, `oauthToken`, `clientSecret`.

Matching is case-insensitive.

## Known Source Types

| Source Type | Description |
|---|---|
| `Crm` | Customer relationship management systems. |
| `SqlMetric` | SQL-based metrics and warehouse data. |
| `EventStream` | Real-time event streams and webhooks. |
| `ProductUsage` | Product usage and telemetry data. |

## Known Semantic Attributes

The 13 reserved public semantic attribute keys:

`conversionProbability`, `preferredChannel`, `planInterest`, `churnRisk`, `engagementLevel`, `expansionPotential`, `budgetReadiness`, `decisionMakerLikelihood`, `productFit`, `recommendedSalesMotion`, `stakeholderSeniority`, `salesUrgency`, `recentFeatureAdoption`.

## Usage

### Library

```typescript
import { validateManifest } from '@kynticai/scout-connector-validator'

const result = validateManifest(manifest, {
  knownConnectorIds: ['sqlDatabase', 'restApi'],
})
```

### CLI

```bash
npx scout-validate-manifest ./my-connector-manifest.json
npx scout-validate-manifest ./manifests/ --check-duplicates

# Validate the bundled connector template manifest
cd packages/typescript/scout-connector-validator
node dist/cli.js ../../../samples/connector-template/template-connector-manifest.json
```

### Discovery Agent MCP

The validator remains available through the connector-metadata MCP package as
the `scout_validate_connector_manifest_v2` tool. The canonical codebase
handover agent is `apps/discovery-agent`; it can audit this package alongside
the rest of the repo and produce a Codex-ready handover.

For buyer-facing discovery, `kyntic-discovery-mcp` can validate the manifest and
then include only a metadata-only summary in `kynticai.discovery-signature.v1`.
IT should review connector identity, display name, supported source kinds, safe
metadata fields, and sample entity mappings. The Discovery Signature must not
include records, query output, credentials, tokens, connection strings, raw
payloads, source documents, PII, vectors, embeddings, prompt packages, or local
logs.

## Relationship to Existing Validation

The `.NET` `ConnectorMetadataValidator` in `src/KynticAI.Scout.Infrastructure/Connectors/ConnectorMetadataValidator.cs` validates live `IConnectorPlugin` instances at runtime. This TypeScript validator operates on static JSON manifest files and adds checks for version format, safe metadata fields, and entity mappings. Both validators share the same underlying rules for connector ID, display name, description, and source types.

The existing `scout_validate_connector_manifest` MCP tool validates the legacy connector metadata format (with `connectorType` and `supportedDataSourceKinds`). The new `scout_validate_connector_manifest_v2` tool validates the extended manifest format described in this document.
