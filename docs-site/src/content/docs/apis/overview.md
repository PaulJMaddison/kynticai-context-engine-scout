---
title: API Overview
description: An overview of the KynticAI Scout API surfaces — REST, GraphQL, and SDKs.
---

KynticAI Scout exposes context through GraphQL, REST, and typed client SDKs
for TypeScript and .NET. Use GraphQL when consumers need shaped context
queries. Use REST for conventional HTTP integrations, machine clients,
event ingestion, and OpenAPI-based tooling.

The versioned REST surface also exposes `POST /api/v1/intelligence/next-action`
for the customer data-plane demo flow. It links exact authorised records such
as CRM contact/account, email engagement, web conversion, opportunities,
support, usage, billing, and won/lost outcome signals into relationship JSON
with relationships, attribution-path evidence, Scout basic fallback-only signals,
provenance, governance decisions, and a recommended next action. Canonical
relationship-set analysis belongs to proprietary analysis modules outside the
open-core deliverable. Raw records stay in
the data plane; optional
Cloud/control-plane payloads are aggregate usage metadata only and exclude
relationship types, weighted signals, recommendations, confidence, caveats,
and citation IDs.

## Public Surfaces

| Surface | Path or package | Reference |
|---|---|---|
| GraphQL | `/graphql` | [GraphQL API](/apis/graphql/) |
| REST v1 | `/api/v1` | [REST API](/apis/rest/) |
| Legacy REST | `/api/rest` | [REST API](/apis/rest/#legacy-rest) |
| TypeScript SDK | `@kynticai/scout-sdk` | [TypeScript SDK](/sdks/typescript/) |
| .NET SDK | `KynticAI.Scout.Sdk` | [.NET SDK](/sdks/dotnet/) |

## Ports

The full demo stack — Docker via `scripts/start-scout-docker.sh` / `.\scripts\start-scout-docker.ps1`, or the local demo scripts — exposes the API at `http://127.0.0.1:5198` and the web console at `http://127.0.0.1:5173`. The API-only Docker quickstart (`docker compose -f deploy/docker-compose.yml up scout-api`, or a manual `docker run -p 8080:8080`) exposes the API at `http://127.0.0.1:8080`. When following the API-only Docker path, use port `8080`; everywhere else, use port `5198`.

## Authentication

Obtain a bearer token by logging in or exchanging client credentials:

```bash
# Interactive login
curl -X POST http://127.0.0.1:5198/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"tenantSlug":"demo","email":"admin@scout.local","password":"DemoAdmin123!"}'

# Machine-to-machine token exchange
curl -X POST http://127.0.0.1:5198/api/auth/token \
  -H "Content-Type: application/json" \
  -d '{
    "grantType": "client_credentials",
    "clientId": "<client-id>",
    "clientSecret": "<client-secret>",
    "scope": "context:read context:write"
  }'
```

Use the returned `accessToken` as a `Bearer` token in subsequent requests.

## Runtime API Documentation

When `Platform__EnableOpenApi=true` (the default for development), browse
the full reference:

| UI | URL |
|---|---|
| **Scalar** (recommended) | [http://127.0.0.1:5198/api-docs](http://127.0.0.1:5198/api-docs) |
| **Swagger UI** | [http://127.0.0.1:5198/swagger](http://127.0.0.1:5198/swagger) |

## First GraphQL Query

```graphql
query {
  userContext(input: { tenantSlug: "demo", externalUserId: "123" }) {
    fullName
    companyName
    summary
    overallConfidence
    facts {
      attributeKey
      confidence
      explanation
    }
  }
}
```

## First REST Call

```bash
curl "http://127.0.0.1:5198/api/v1/context/users/123?tenantSlug=demo" \
  -H "Authorization: Bearer <token>"
```

## Scopes

API clients are assigned scopes that control access. The canonical scopes
are:

| Scope | Description |
|---|---|
| `context:read` | Read user and account context, facts, and snapshots |
| `context:write` | Trigger recomputation and write context data |
| `selectors:write` | Create and update selector definitions |
| `events:ingest` | Submit events for context recomputation |
| `audit:read` | Read audit and provenance records |
| `admin:manage` | Manage API clients, webhook secrets, and admin records |
| `blueprints:write` | Upload, validate, preview, and import blueprints |
| `billing:read` | Read usage-metering overview |

## Next Steps

- [GraphQL API](/apis/graphql/)
- [REST API](/apis/rest/)
- [SDK Overview](/sdks/overview/)
