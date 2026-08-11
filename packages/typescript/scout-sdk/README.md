# @kynticai/scout-sdk

Typed TypeScript client for the [KynticAI Scout](https://github.com/PaulJMaddison/kynticai-context-engine-scout) API. It gives application teams a stable, fully-typed surface for REST and GraphQL calls instead of hand-rolling HTTP requests.

This package is part of the public open-core. It covers authentication, context lookups, semantic facts, snapshots, selector preview, recompute, governed context and relationship/evidence packages, source-system event ingestion, audit, and the contract client for services implementing the KynticAI Score API.

## SDK Folder Structure

```text
packages/typescript/scout-sdk/
  src/
    client.ts
    errors.ts
    index.ts
    score-client.ts
    types.ts
  tests/
    client.test.ts
  package.json
  tsconfig.json
  vitest.config.ts
  README.md
```

## Supported Capabilities

- authentication
- machine-to-machine token exchange
- tenant scoping
- user context lookup
- account context lookup
- context snapshot retrieval
- semantic fact lookup with REST filtering and pagination options
- selector preview and validation
- context recompute requests
- governed context and relationship/evidence package retrieval
- provider-neutral source-system event ingestion
- audit event lookup
- KynticAI Score API contract client
- typed error handling
- retries for transient failures
- request tracing headers

## Install

```bash
npm install ../packages/typescript/scout-sdk
```

## Quick Start

```ts
import { createScoutClient } from '@kynticai/scout-sdk'

const scout = createScoutClient({
  baseUrl: 'http://127.0.0.1:5198',
  accessToken: process.env.SCOUT_TOKEN,
})

const context = await scout.users.getContext('demo', '123')
const facts = await scout.facts.getForUser('demo', '123', {
  attributeKey: 'health',
  page: 1,
  pageSize: 25,
})
const snapshot = await scout.snapshots.getLatestForUser('demo', '123')
const snapshotById = await scout.snapshots.getById('demo', snapshot!.snapshotId)
const account = await scout.accounts.getContext('demo', 'ACC-123')
```

## Score API Client

The score client calls services that implement `schema/kyntic-score.openapi.yaml`.
Scout does not calculate scores itself.

```ts
import { createKynticScoreClient } from '@kynticai/scout-sdk'

const scores = createKynticScoreClient({
  baseUrl: 'http://127.0.0.1:3016',
  accessToken: process.env.SCORE_TOKEN,
})

const result = await scores.createInvestmentScore({
  subject: {
    name: 'Example Infrastructure Ltd',
    sector: 'Industrial software',
  },
  evidence: [
    {
      id: 'ev-001',
      summary: 'Recurring revenue grew across the last four quarters.',
      source: 'management-account-summary',
      weight: 0.8,
    },
  ],
})
```

## Tenant Scoped Usage

```ts
const demo = scout.forTenant('demo')

const context = await demo.users.getContext('123')
const account = await demo.accounts.getContext('ACC-123')
const facts = await demo.facts.getForUser('123', { attributeKey: 'health' })
const auditEvents = await demo.audit.getEvents()
```

## Authentication

```ts
const session = await scout.auth.login({
  tenantSlug: 'demo',
  email: 'admin@scout.local',
  password: 'DemoAdmin123!',
})
```

Machine-to-machine clients can exchange credentials for a scoped bearer token:

```ts
const token = await scout.auth.getMachineToken({
  grantType: 'client_credentials',
  clientId: process.env.SCOUT_CLIENT_ID!,
  clientSecret: process.env.SCOUT_CLIENT_SECRET!,
  scope: 'context:read context:write audit:read',
})
```

Or provide a lazy token provider:

```ts
const scout = createScoutClient({
  baseUrl: 'http://127.0.0.1:5198',
  getAccessToken: async () => process.env.SCOUT_TOKEN,
})
```

## Selector Preview

```ts
const preview = await scout.selectors.preview({
  tenantSlug: 'demo',
  externalUserId: '123',
  draftSelector: {
    tenantSlug: 'demo',
    targetAttributeDefinitionId: '00000000-0000-0000-0000-000000000001',
    name: 'Preferred Channel',
    description: 'Test selector preview.',
    mappingKind: 'DIRECT_FIELD_MAPPING',
    expressionJson: '{"rule":{"valuePath":"crm.preferredChannel"}}',
    explanationTemplate: 'Preferred channel {{sourceValue}}.',
    validationSchemaJson: '{"requiredPaths":["crm.preferredChannel"]}',
    defaultConfidence: 0.9,
    freshnessWindowMinutes: 60,
    priority: 100,
  },
})
```

## Context Recompute

```ts
const queued = await scout.recompute.queueForUser('demo', '123', 'crm-webhook')
```

## Source-System Events

This posts to the open-core provider-neutral event endpoint. It does not implement paid vendor handlers or customer-specific .NET adapters.

```ts
const accepted = await scout.events.ingestSourceSystemEvent('demo', {
  eventId: 'evt-demo-001',
  sourceSystem: 'product',
  eventType: 'source.product_usage.rollup_ready',
  externalUserId: '123',
  payload: {
    activeDays30: 22,
    lastFeature: 'renewal-report',
  },
})

const scopedAccepted = await demo.events.ingestSourceSystemEvent({
  eventId: 'evt-demo-002',
  sourceSystem: 'web',
  eventType: 'source.web_conversion.received',
  externalAccountId: 'ACC-123',
  payload: {
    pricingPageVisits30d: 4,
  },
})
```

## AI Context Package

This retrieves a scoped context package only. Scout does not call an AI model in this method.

```ts
const pkg = await scout.packages.getAiContextForUser(
  'demo',
  '123',
  'Generate an account brief for the next renewal call.',
)
```

## Error Handling

```ts
import { ScoutError } from '@kynticai/scout-sdk'

try {
  await scout.users.getContext('demo', 'missing-user')
} catch (error) {
  if (error instanceof ScoutError) {
    console.log(error.code)
    console.log(error.correlationId)
  }
}
```

## Local Development

```bash
cd packages/typescript/scout-sdk
npm install
npm run build
npm test
npm run pack:dry-run
```

Run the local API first:

```bash
# From the repo root
sh ./scripts/start-demo.sh      # Linux / macOS
./scripts/start-demo.ps1         # Windows
```

## Versioning

- npm package versioning follows the private product line until package publishing is deliberately configured
- additive API coverage can ship in minor versions
- breaking client contract changes require a major bump

## Packaging

- package name: `@kynticai/scout-sdk`
- published files: `dist/` and `README.md`
- module format: ESM with type declarations
- Publishing is not yet configured for the public npm registry.

## Tests

Current tests cover:

- REST v1 user, account, and snapshot route construction
- REST v1 semantic fact filtering
- source-system event ingestion route construction
- tenant-scoped delegation
- transient retry handling
- typed problem-details error mapping
- Score API path alignment and response-limit validation
