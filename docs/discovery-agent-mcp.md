# Discovery Agent MCP Server

The KynticAI Discovery Agent lives in `apps/discovery-agent`. It is the canonical OSS-022 implementation for local codebase audits, handover generation, and MCP access.

The older `packages/typescript/scout-discovery-mcp` package remains as a connector-metadata utility. New agent handover work should use `apps/discovery-agent`.

## Local-Only Boundary

- Runs on Node.js 20+.
- Does not require Rust, Docker, admin rights, or a running Scout API.
- Does not upload code, prompts, metadata, or audit output to external services.
- Skips `.env`, private keys, local licence files, service-account JSON, token files, dependency folders, build outputs, raw exports, database dumps, and support bundles.

## CLI

```bash
cd apps/discovery-agent
npm install
npm run build
node dist/index.js --path ../.. --tier 1
```

The local `npx` package path from this repository is:

```bash
npx --package ./apps/discovery-agent discovery-agent --path . --tier 1
```

The default output is a JSON handover document suitable for Codex goal prompt injection. Use `--audit-only` to return the raw audit result, or `--format markdown` to return a Markdown handover.

## MCP Tools

| Tool | Input | Description |
|---|---|---|
| `audit_codebase` | `{ "path": string, "tier": 1 \| 2 \| 3 }` | Runs a local tiered audit and returns structured JSON. |
| `generate_handover` | `{ "path": string }` | Produces Markdown and JSON handover output. |
| `run_three_tier_audit` | `{ "path": string }` | Runs Tier 1, Tier 2, and Tier 3 in one pass. |
| `check_status` | `{}` | Returns the in-memory status for the running MCP process. |

## Audit Tiers

| Tier | Coverage |
|---|---|
| Tier 1 | File tree, package inventory, entry points, language and stack detection. |
| Tier 2 | Tier 1 plus API endpoints, type signatures, schema objects, and business logic patterns. |
| Tier 3 | Tier 2 plus data-flow inference, security surface, coupling hot spots, and tech-debt scoring. |

## MCP Client Configuration

Add this to `.claude/mcp.json` from a checkout that includes `apps/discovery-agent`:

```json
{
  "mcpServers": {
    "kyntic-discovery-agent": {
      "command": "npx",
      "args": ["--package", "./apps/discovery-agent", "discovery-agent", "--mcp"]
    }
  }
}
```

For local development from this checkout:

```json
{
  "mcpServers": {
    "kyntic-discovery-agent": {
      "command": "node",
      "args": ["C:/path/to/scout/apps/discovery-agent/dist/index.js", "--mcp"]
    }
  }
}
```

## VS Code Usage

There is no VS Code extension project in this repo. Use the CLI from the VS Code terminal or a workspace task:

```json
{
  "version": "2.0.0",
  "tasks": [
    {
      "label": "KynticAI Discovery Agent Tier 1",
      "type": "shell",
      "command": "npx --package ./apps/discovery-agent discovery-agent --path . --tier 1"
    }
  ]
}
```

## Output Shape

`generate_handover` and the default CLI mode produce:

```json
{
  "project_name": "example",
  "audit_date": "2026-05-29T00:00:00.000Z",
  "tech_stack": { "languages": [], "frameworks": [], "databases": [], "packageManagers": [] },
  "entry_points": [],
  "api_surface": [],
  "key_entities": [],
  "data_stores": [],
  "security_surface": [],
  "recommended_next_agent_prompt": "..."
}
```

## KynticAI Discovery MCP Buyer Wrapper

`kyntic-discovery-mcp` is the buyer-facing product wrapper for IT-manager-led
discovery. It composes two existing public assets:

- `apps/discovery-agent` for local codebase audit and handover.
- `packages/typescript/scout-discovery-mcp` for connector catalogue metadata,
  manifest validation, and metadata quality reports.

It does not duplicate the scanner and does not inspect live customer data.

### Buyer Journey

1. IT runs local discovery on approved paths.
2. IT reviews connector catalogue metadata and any connector manifest output.
3. IT prepares a `kynticai.discovery-signature.v1` draft containing approved
   metadata labels, field names, event names, object names, counts, and bands
   only.
4. The wrapper validates the draft and can export the Discovery Signature to a
   chosen local path.
5. IT reviews the Discovery Signature locally.
6. Optional handoff sends only the Discovery Signature object to an approved
   KynticAI endpoint so KynticAI can build a synthetic demo.

### CLI

```bash
cd apps/discovery-agent
npm install
npm run build

# Local report. No codebase scan and no network.
node dist/kyntic-discovery-mcp.js

# Local codebase shape.
node dist/kyntic-discovery-mcp.js --path ../.. --audit --tier 2

# Local handover.
node dist/kyntic-discovery-mcp.js --path ../.. --handover

# Connector manifest validation plus metadata quality report.
node dist/kyntic-discovery-mcp.js --manifest ./connector-manifest.json

# Validate and print a Discovery Signature v1 draft.
node dist/kyntic-discovery-mcp.js --metadata ./examples/synthetic-approved-metadata.json --signature

# Export the validated signature to a local file.
node dist/kyntic-discovery-mcp.js --metadata ./examples/synthetic-approved-metadata.json --signature --export-signature ./discovery-signature.json
```

Optional handoff is explicit and still sends only the Discovery Signature:

```bash
node dist/kyntic-discovery-mcp.js \
  --metadata ./approved-signature.json \
  --signature \
  --submit-handoff \
  --allow-handoff \
  --consent-handoff \
  --handoff-endpoint https://example.invalid/discovery \
  --handoff-config ./handoff-approved.json
```

The handoff config must be JSON with:

```json
{
  "approved": true,
  "endpoint": "https://example.invalid/discovery",
  "allowedPayload": "kynticai.discovery-signature.v1",
  "approvalReference": "IT-APPROVAL-001"
}
```

### Discovery Signature v1

The Discovery Signature schema id is `kynticai.discovery-signature.v1`.
Top-level fields are closed; anything outside this list fails validation:

| Field | Review expectation |
|---|---|
| `schemaVersion` | Must equal `kynticai.discovery-signature.v1`. |
| `companyType` | Business category broad enough for a synthetic demo. |
| `targetWorkflow` | The buyer workflow KynticAI should demonstrate. |
| `sourceSystemFamilies` | Approved system families, for example CRM, website analytics, product usage, support, billing, ecommerce, or docs systems. |
| `connectorManifests` | Metadata-only connector manifest summaries. Include connector identity, display name, supported source kinds, safe metadata fields, and sample entity mappings only. |
| `conversionPoints` | Approved conversion event or funnel step names. |
| `governanceNotes` | Short review notes, policy constraints, or residual caveats. |
| `closestSyntheticDomain` | The synthetic demo domain KynticAI should use. |
| `approvedForSyntheticDemoBuild` | Approval object with `approved: true`, reviewer, approval timestamp, and optional approval reference. |

Forbidden fields and values fail closed: records, query output, credentials,
tokens, connection strings, raw payloads, source documents, PII, vectors,
embeddings, prompt packages, local logs, credential-looking field names,
raw-looking record arrays, long text blobs, absolute local paths, token-like
strings, and URLs containing credentials or token query parameters.

### MCP Client Configuration

Claude, Codex, and other MCP-compatible clients can use the wrapper:

```json
{
  "mcpServers": {
    "kynticai-discovery-mcp": {
      "command": "npx",
      "args": ["--package", "./apps/discovery-agent", "kyntic-discovery-mcp", "--mcp"]
    }
  }
}
```

Direct local build path:

```json
{
  "mcpServers": {
    "kynticai-discovery-mcp": {
      "command": "node",
      "args": ["C:/path/to/scout/apps/discovery-agent/dist/kyntic-discovery-mcp.js", "--mcp"]
    }
  }
}
```

Enable optional handoff only with explicit endpoint and config:

```json
{
  "mcpServers": {
    "kynticai-discovery-mcp": {
      "command": "node",
      "args": [
        "C:/path/to/scout/apps/discovery-agent/dist/kyntic-discovery-mcp.js",
        "--mcp",
        "--allow-handoff",
        "--consent-handoff",
        "--handoff-endpoint",
        "https://example.invalid/discovery",
        "--handoff-config",
        "C:/path/to/handoff-approved.json"
      ]
    }
  }
}
```

### Wrapper MCP Tools

| Tool | Purpose |
|---|---|
| `kyntic_audit_codebase` | Runs the existing local codebase audit. |
| `kyntic_generate_handover` | Produces local handover output. |
| `kyntic_list_connector_catalogue` | Inspects public Scout connector catalogue metadata. |
| `kyntic_read_connector_manifest` | Reads one public connector manifest by type or alias. |
| `kyntic_validate_connector_manifest` | Validates a connector manifest locally. |
| `kyntic_metadata_quality_report` | Runs the metadata quality report from a manifest only. |
| `kyntic_get_inspection_guide` | Returns guidance for a metadata-only inspection domain. |
| `kyntic_generate_discovery_signature` | Validates and returns a `kynticai.discovery-signature.v1` object. |
| `kyntic_export_discovery_signature` | Validates and writes a Discovery Signature to a chosen local path. |
| `kyntic_create_local_report` | Combines local report sections into one buyer-facing output. |
| `kyntic_submit_approved_handoff` | Submits only the Discovery Signature when handoff is explicitly enabled and approved. |

The wrapper also registers MCP prompts and resources for local codebase shape,
database schema metadata, CRM metadata, website conversion points,
analytics/property/event metadata, support metadata, billing metadata, product
metadata, ecommerce metadata, and docs-system metadata.

### Safe-Output Contract

- Local report output is the default.
- Network handoff is disabled unless `--submit-handoff`, `--allow-handoff`,
  `--consent-handoff`, `--handoff-endpoint`, and an approved
  `--handoff-config` are all present.
- Discovery Signature validation requires schema `kynticai.discovery-signature.v1`
  and `approvedForSyntheticDemoBuild.approved: true`.
- The scanner and wrapper refuse `.env`, private keys, local licence files,
  service-account JSON, token files, dependency folders, build outputs, raw
  exports, database dumps, and support bundles.
- Discovery Signature validation rejects records, query output, credentials,
  tokens, connection strings, raw payloads, source documents, PII, vectors,
  embeddings, prompt packages, local logs, raw-looking record arrays, long text
  blobs, absolute local paths, token-like strings, and URLs containing
  credentials or tokens.
- Handoff request bodies contain the Discovery Signature object only.
- The public wrapper contains no private connector implementation details and
  does not call an AI model.
