# Connector Test Harness

The **Scout Connector Test Harness** (`@kynticai/scout-connector-test-harness`) is a local-only validation tool for connector authors. It validates a connector against the public KynticAI Scout interfaces without requiring a running Scout instance, enterprise internals, or live data connections.

## When to Use

Use the test harness when:

- Authoring a new connector and want to validate before integration testing.
- Checking that a manifest follows the public schema.
- Verifying sample entity mappings resolve against sample data.
- Ensuring no unsafe fields (passwords, tokens, secrets) leak into public metadata.
- Testing error handling in a fake fetch implementation.

## Quick Start

### CLI

```bash
cd packages/typescript/scout-connector-test-harness
npm install && npm run build

# Run against the bundled sample manifest
node dist/cli.js ../scout-connector-validator/data/sample-manifest.json

# Run against the connector template manifest in samples/connector-template
node dist/cli.js ../../../samples/connector-template/template-connector-manifest.json

# JSON output for machine-readable reports
node dist/cli.js ../../../samples/connector-template/template-connector-manifest.json --json
```

The CLI takes a single manifest JSON file as its positional argument. To run
entity-mapping checks against sample records, pass `--records <file.json>`
where the file is a JSON array of `SampleRecord` objects (see the package
README's "Sample Records Format"); the records file must be a bare array, not
a wrapped definition object.

### Programmatic

```typescript
import { runTestHarness } from '@kynticai/scout-connector-test-harness'

const report = await runTestHarness({
  manifest: { /* ConnectorManifest */ },
  sampleRecords: [ /* SampleRecord[] */ ],
  fakeFetch: (userId) => { /* return payload or throw */ },
})

if (!report.passed) {
  for (const r of report.results.filter(r => !r.passed)) {
    console.error(`[${r.suite}] ${r.name}: ${r.message}`)
  }
}
```

## Test Suites

### 1. Manifest Shape (`manifest-shape`)

Delegates to `@kynticai/scout-connector-validator`. Checks:

- Required fields: `connectorId`, `displayName`, `version`, `description`, `supportedSourceTypes`, `requiredConfigFields`, `safeMetadataFields`, `sampleEntityMappings`.
- `version` follows semver.
- `connectorId` follows camelCase naming.
- `configurationSchema` has `"type": "object"` with `"properties"`.
- `sampleConfiguration` satisfies all `required` schema fields.
- Optional `eventShape` declares `sourceSystem`, `entityType`, and `sourceIdField`.

### 2. Metadata Extraction (`metadata-extraction`)

Delegates to `@kynticai/scout-metadata-audit`. Checks:

- Configuration schema is present.
- No error-level audit warnings.
- Readiness score meets minimum threshold (40/100).
- Sample records are acknowledged when provided.

### 3. Entity Mapping (`entity-mapping`)

- At least one mapping declared.
- At least one mapping uses a recognised public semantic attribute.
- Each mapping has non-empty `sourceField` and `semanticAttribute`.
- When sample records are provided, each `sourceField` appears in at least one record payload.

### 4. Error Handling (`error-handling`)

Only runs when a `fakeFetch` function is supplied:

- Fetch returns a valid non-null plain object for each test user ID.
- Error test user triggers a thrown `Error` instance (not a raw string or other type).
- Supports both sync and async fetch functions.

### 5. Unsafe Fields (`unsafe-fields`)

Checks the public unsafe-field blocklist against:

- `safeMetadataFields` entries.
- `configurationSchema` property names.
- `sampleConfiguration` key names.
- Entity mapping `sourceField` and `semanticAttribute` values.
- Connector event shape field names.

Blocked names include: `password`, `secret`, `token`, `credential`, `apiKey`, `accessToken`, `refreshToken`, `privateKey`, `connectionString`, `ssn`, `creditCard`, and others.

## Relation to Other Packages

| Package | Role |
|---|---|
| `@kynticai/scout-connector-validator` | Validates manifest JSON shape. Used internally by the harness. |
| `@kynticai/scout-metadata-audit` | Runs metadata quality audit. Used internally by the harness. |
| `@kynticai/scout-connector-test-harness` | Orchestrates all checks into a single report with pass/fail per suite. |
| `@kynticai/scout-discovery-mcp` | MCP server for AI agent discovery. Separate concern; not used by the harness. |

## Open-Core Boundary

The test harness uses only public Scout interfaces. It does not reference:

- Enterprise connectors (Salesforce, Dynamics, SAP, etc.).
- Private connector logic or vendor-specific adapters.
- private vector-store integrations, embedded model runtimes, or unrelated AI tooling.
- Credential vaults or secret resolution.
