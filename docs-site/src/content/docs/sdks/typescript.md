---
title: TypeScript SDK
description: Using the KynticAI Scout TypeScript SDK.
---

The TypeScript SDK lives in `packages/typescript/scout-sdk` and uses the
package name `@kynticai/scout-sdk`. Public npm publishing is not yet
configured; use the local package path below until a release process is
explicitly approved.

## Install From This Repository

```bash
cd packages/typescript/scout-sdk
npm install
npm run build
```

From another local package:

```bash
npm install ../packages/typescript/scout-sdk
```

## Create A Client

```typescript
import { createScoutClient } from '@kynticai/scout-sdk'

const scout = createScoutClient({
  baseUrl: 'http://127.0.0.1:5198',
  accessToken: process.env.SCOUT_TOKEN,
})
```

## Score API Client

The SDK also exports a client for services that implement
`schema/kyntic-score.openapi.yaml`:

```typescript
import { createKynticScoreClient } from '@kynticai/scout-sdk'

const scores = createKynticScoreClient({
  baseUrl: 'http://127.0.0.1:3016',
  accessToken: process.env.SCORE_TOKEN,
})

const result = await scores.createInvestmentScore({
  subject: { name: 'Example Infrastructure Ltd' },
  evidence: [{ id: 'ev-001', summary: 'Revenue quality is improving.' }],
})
```

## Machine Token Flow

```typescript
import { createScoutClient } from '@kynticai/scout-sdk'

const bootstrap = createScoutClient({
  baseUrl: 'http://127.0.0.1:5198',
})

const token = await bootstrap.auth.getMachineToken({
  grantType: 'client_credentials',
  clientId: process.env.SCOUT_CLIENT_ID!,
  clientSecret: process.env.SCOUT_CLIENT_SECRET!,
  scope: 'context:read context:write audit:read',
})

const scout = createScoutClient({
  baseUrl: 'http://127.0.0.1:5198',
  accessToken: token.accessToken,
})
```

## Context Reads

```typescript
const user = await scout.users.getContext('demo', '123')
const account = await scout.accounts.getContext('demo', 'acct-123')

const facts = await scout.facts.getForUser('demo', '123', {
  attributeKey: 'health',
  page: 1,
  pageSize: 25,
})

const accountFacts = await scout.facts.getForAccount('demo', 'acct-123')
const snapshot = user?.snapshotId
  ? await scout.snapshots.getById('demo', user.snapshotId)
  : null
```

## Events And Recompute

```typescript
await scout.events.ingestSourceSystemEvent('demo', {
  eventId: 'evt-demo-001',
  sourceSystem: 'product',
  eventType: 'source.product_usage.rollup_ready',
  externalUserId: '123',
  payload: { activeDays30: 22 },
})

await scout.recompute.queueForUser('demo', '123', 'product-webhook')
```

## Context Packages

```typescript
const contextPackage = await scout.packages.getAiContextForUser(
  'demo',
  '123',
  'Prepare a renewal-risk brief for the account team.',
)
```

Scout returns a grounded context package for a downstream consumer. The SDK
does not make model-provider calls.

## Tenant-Scoped Client

```typescript
const demo = scout.forTenant('demo')

const user = await demo.users.getContext('123')
const audit = await demo.audit.getEvents()
```

## Build And Test Locally

```bash
cd packages/typescript/scout-sdk
npm install
npm run build
npm run test
```

See [REST API](/apis/rest/) and [GraphQL API](/apis/graphql/) for the
underlying server surfaces.
