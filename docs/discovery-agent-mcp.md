# Public Discovery Tools in Scout

Scout contains two public discovery tools. The commercial KynticAI Discovery MCP is deliberately not one of them.

## 1. Discovery Agent

Path: `apps/discovery-agent`

The Discovery Agent is a generic local codebase audit and handover tool.

It:

- runs locally;
- inventories project shape, entry points and technology;
- can produce structured handover material;
- does not require Scout to be running;
- does not upload code or audit output to KynticAI.

### CLI

```bash
cd apps/discovery-agent
npm install
npm run build
node dist/index.js --path ../.. --tier 1
```

### MCP tools

| Tool | Purpose |
| --- | --- |
| `audit_codebase` | Run a local tiered codebase audit. |
| `generate_handover` | Produce local Markdown/JSON handover output. |
| `run_three_tier_audit` | Run all three local audit tiers. |
| `check_status` | Return local process status. |

## 2. Scout Discovery MCP

Path: `packages/typescript/scout-discovery-mcp`

This is a metadata-only public Scout tool for inspecting the public connector catalogue, connector manifests and metadata quality.

It may help a technical user understand which Scout connector shapes are available without accessing live customer records.

It must remain metadata-only and public-safe.

## What is not in this repository

The **KynticAI Discovery MCP** commercial buyer workflow is private.

The following do not belong in Scout:

- Discovery Signature generation;
- paid discovery/readiness routing;
- buyer-journey orchestration;
- private synthetic-demo handoff bundles;
- private customer handoff logic.

Those belong with Fortress/private product code.

## Safety

Both public discovery tools should remain local-first and avoid secrets, private keys, token files, database dumps, support bundles and raw customer exports.

See [source-of-truth-naming-map.md](source-of-truth-naming-map.md) for the canonical boundary.
