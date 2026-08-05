---
title: GraphQL API
description: Public GraphQL query and mutation surface for KynticAI Scout.
---

The GraphQL API is served by Hot Chocolate at:

```text
http://127.0.0.1:5198/graphql
```

The port depends on the run mode; see the [API Overview port
note](/apis/overview/#ports) for the API-only Docker path. In local
development, the browser IDE is available at the same path. Production
settings can disable introspection, schema requests, and browser GET
requests.

## Authentication

GraphQL requires an authenticated bearer token. Operator tokens come from
`POST /api/auth/login`. Machine clients exchange credentials through
`POST /api/auth/token` and must have the required scope for the operation.

Common API-client scopes:

| Scope | Used for |
|---|---|
| `context:read` | Context lookups, facts, snapshots, connector catalogue reads. |
| `context:write` | Queueing recomputation. |
| `selectors:write` | Selector preview and validation. |
| `events:ingest` | Source-system event submission through REST. |
| `audit:read` | Audit-event reads. |
| `admin:manage` | API-client, webhook, operator, and administrative reads/writes. |
| `blueprints:write` | Blueprint upload, validation, preview, and import. |
| `billing:read` | Usage-metering overview reads. |

## Query Fields

The query root is implemented in
`src/KynticAI.Scout.Api/GraphQL/Query.cs`.

| Field | Purpose |
|---|---|
| `tenants` | List tenants for platform owners and tenant admins. |
| `userProfiles` | List user profile summaries for a tenant. |
| `dataSources` | List registered data sources. |
| `connectorPlugins` | List registered connector plugin definitions. |
| `connectorCatalogue` | List public connector catalogue entries. |
| `licenceStatus` | Read local licence status. |
| `semanticAttributes` | List semantic attribute definitions. |
| `selectors` | List selector definitions. |
| `selectorExecutions` | List selector execution records. |
| `promptTemplates` | List prompt templates. |
| `agentRuns` | List demo agent-run records. |
| `auditEvents` | List audit events. |
| `sourceSystemEvents` | List ingested source-system events. |
| `saasArchitectureOverview` | Read local architecture overview metadata. |
| `organisationSettings` | Read tenant organisation settings. |
| `workspaces` | List workspace summaries. |
| `operatorAccounts` | List operator accounts. |
| `apiClients` | List machine API clients. |
| `blueprintImports` | List blueprint import history. |
| `governancePolicies` | List governance policy metadata. |
| `currentPlan` | Read local plan metadata. |
| `billingUsage` | Read usage-metering overview. |
| `userContext` | Fetch a context profile for a user. |
| `accountContext` | Fetch aggregated context for an account. |
| `contextSnapshot` | Fetch a context snapshot by ID. |
| `contextFacts` | Fetch semantic facts for a user or account. |
| `salesContextPackage` | Fetch a scoped context package. |

## Mutation Fields

The mutation root is implemented in
`src/KynticAI.Scout.Api/GraphQL/Mutation.cs`.

| Field | Purpose |
|---|---|
| `updateOperatorAccount` | Update an operator account. |
| `createApiClient` | Create a machine API client. |
| `rotateApiClient` | Rotate a machine API client secret. |
| `revokeApiClient` | Revoke a machine API client. |
| `submitOnboarding` | Submit a local onboarding application. |
| `uploadBlueprint` | Upload a blueprint import payload. |
| `validateBlueprint` | Validate a blueprint import payload. |
| `previewBlueprint` | Preview a blueprint import. |
| `importBlueprint` | Import a validated blueprint. |
| `upsertDataSource` | Create or update a data source. |
| `registerConnector` | Register connector installation metadata. |
| `validateConnectorConfiguration` | Validate connector configuration. |
| `checkConnectorHealth` | Run connector health checks. |
| `upsertSemanticAttribute` | Create or update a semantic attribute. |
| `upsertSelector` | Create or update a selector. |
| `publishSelector` | Publish a selector. |
| `queueContextRecompute` | Queue context recomputation. |
| `previewSelector` | Preview selector execution. |
| `validateSelector` | Validate selector execution. |
| `runScheduledRecompute` | Dispatch scheduled recomputation. |
| `upsertPromptTemplate` | Create or update a prompt template. |
| `createAgentRun` | Create an example demo agent run. |

## Example Query

```graphql
query DemoUserContext {
  userContext(input: { tenantSlug: "demo", externalUserId: "123" }) {
    tenantSlug
    externalUserId
    fullName
    companyName
    summary
    overallConfidence
    facts {
      attributeKey
      valueJson
      valueType
      confidence
      observedAtUtc
      freshUntilUtc
      explanation
      provenanceJson
    }
  }
}
```

## Example Fact Lookup

```graphql
query DemoSemanticFactLookup {
  contextFacts(
    tenantSlug: "demo"
    externalUserId: "123"
    attributeKey: "health"
    skip: 0
    take: 25
  ) {
    attributeKey
    valueJson
    valueType
    confidence
    explanation
  }
}
```

## Schema Discovery

In local development, schema requests are enabled unless configuration turns
them off. In production-style hosted mode, schema requests and introspection
can be disabled through `GraphQl__EnableSchemaRequestsInProduction` and
`GraphQl__DisableIntrospectionInProduction`.

Sample GraphQL operations are available in the repository at
`samples/graphql/demo-queries.graphql`.

## Related Pages

- [REST API](/apis/rest/)
- [API Overview](/apis/overview/)
- [TypeScript SDK](/sdks/typescript/)
- [.NET SDK](/sdks/dotnet/)
