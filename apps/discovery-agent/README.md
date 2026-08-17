# KynticAI Discovery Agent

The KynticAI Discovery Agent is a local-only Node.js 20+ CLI and MCP server for auditing a codebase and producing a structured handover document for agent work. It does not call a hosted API, upload source code, or require Rust, Docker, admin rights, or a running KynticAI Scout instance.

This open-source package is the generic codebase-audit utility only. The commercial KynticAI Discovery MCP buyer workflow, Discovery Signature generation and controlled customer handoff are private Fortress functionality and are intentionally not implemented in Scout.

## Install And Run

Local `npx` package path from this repository:

```bash
npx --package ./apps/discovery-agent discovery-agent --path . --tier 1
```

Direct local development path:

```bash
cd apps/discovery-agent
npm install
npm run build
node dist/index.js --path ../.. --tier 1
```

By default the CLI prints a JSON handover document. Use `--audit-only` for the raw audit structure, or `--format markdown` for the Markdown handover.

## Audit Tiers

| Tier | Command | What It Inspects |
|---|---|---|
| 1 | `--tier 1` | File tree, package inventory, entry points, language and stack detection. |
| 2 | `--tier 2` | Tier 1 plus API endpoints, type signatures, schema objects, and business logic patterns. |
| 3 | `--tier 3` | Tier 2 plus data-flow inference, security surface, coupling hot spots, and tech-debt scoring. |

The scanner skips `.env`, private keys, local licence files, service-account JSON, token files, dependency folders, build outputs, raw exports, database dumps, and support bundles.

## CLI Examples

```bash
# JSON handover for Codex goal prompt injection
npx --package ./apps/discovery-agent discovery-agent --path . --tier 3

# Raw Tier 2 audit
npx --package ./apps/discovery-agent discovery-agent --path . --tier 2 --audit-only

# Markdown handover
npx --package ./apps/discovery-agent discovery-agent --path . --tier 3 --format markdown
```

## MCP Tools

Start the stdio MCP server:

```bash
npx --package ./apps/discovery-agent discovery-agent --mcp
```

Available tools:

| Tool | Input | Purpose |
|---|---|---|
| `audit_codebase` | `{ "path": string, "tier": 1 \| 2 \| 3 }` | Run one audit tier and return structured JSON. |
| `generate_handover` | `{ "path": string }` | Return Markdown and JSON handover output. |
| `run_three_tier_audit` | `{ "path": string }` | Run all tiers and return the full audit. |
| `check_status` | `{}` | Read the in-memory audit status for the running MCP process. |

## Claude Code MCP Config

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

Direct node path:

```json
{
  "mcpServers": {
    "kyntic-discovery-agent": {
      "command": "node",
      "args": ["C:/Kyntic/UCL/apps/discovery-agent/dist/index.js", "--mcp"]
    }
  }
}
```

## VS Code Usage

This repository does not currently include a VS Code extension project. Use the CLI from a VS Code terminal or a workspace task:

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

## Development

```bash
npm install
npm run build
npm run test
node dist/index.js --path ../.. --tier 1
```

The implementation is TypeScript only and is designed to run unchanged on Windows, macOS, and Linux.
