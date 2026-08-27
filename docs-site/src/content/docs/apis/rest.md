---
title: REST API
description: Public REST endpoints, authentication, pagination, and error shapes for KynticAI Scout.
---

Scout exposes two REST surfaces. `/api/v1` is the canonical machine REST API; `/api/rest` is legacy compatibility only.

| Surface | Prefix | Status |
|---|---|---|
| Versioned REST | `/api/v1` | Preferred for new integrations. |
| Legacy REST | `/api/rest` | Kept for compatibility with earlier demos and docs. |

When `Platform__EnableOpenApi=true` and REST is enabled, interactive
reference UIs are available at `/api-docs` and `/swagger`.

## Authentication

Interactive operators authenticate through:

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/api/auth/login` | Exchange tenant, email, and password for a bearer token. |
| `GET` | `/api/auth/me` | Read the current authenticated operator. |

Machine clients authenticate through:

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/api/auth/token` | Exchange client credentials for a scoped bearer token. |
| `GET` | `/api/auth/api-clients` | List API clients through the auth admin surface. |
| `POST` | `/api/auth/api-clients` | Create an API client. |
| `POST` | `/api/auth/api-clients/{clientId}/rotate` | Rotate an API client secret. |
| `POST` | `/api/auth/api-clients/{clientId}/revoke` | Revoke an API client. |

## Versioned REST v1

The versioned API is implemented in
`src/KynticAI.Scout.Api/Rest/VersionedRestEndpointRouteBuilderExtensions.cs`.

| Method | Path | Required scope | Purpose |
|---|---|---|---|
| `GET` | `/api/v1/health` | Anonymous | Versioned health check. |
| `GET` | `/api/v1/connectors/catalogue` | Anonymous | List connector catalogue entries. |
| `GET` | `/api/v1/workspaces` | `context:read` | List workspaces for the current tenant. |
| `GET` | `/api/v1/licence/status` | `context:read` | Read local licence status. |
| `GET` | `/api/v1/context/users/{externalUserId}` | `context:read` | Fetch user context. |
| `GET` | `/api/v1/context/accounts/{externalAccountId}` | `context:read` | Fetch account context. |
| `GET` | `/api/v1/context/users/{externalUserId}/facts` | `context:read` | Fetch user semantic facts. |
| `GET` | `/api/v1/context/accounts/{externalAccountId}/facts` | `context:read` | Fetch account semantic facts. |
| `GET` | `/api/v1/context/snapshots/{snapshotId}` | `context:read` | Fetch a context snapshot. |
| `POST` | `/api/v1/context/users/{externalUserId}/ai-safe-context-package` | `context:read` | Fetch a scoped context package. |
| `POST` | `/api/v1/intelligence/next-action` | `context:read` | Legacy compatibility endpoint. Scout core returns `501 feature.external_consumer_required`; build next-action logic in a reference/customer-owned consumer using governed context. |
| `POST` | `/api/v1/context/recompute` | `context:write` | Queue recomputation. |
| `POST` | `/api/v1/selectors/preview` | `selectors:write` | Preview a selector. |
| `POST` | `/api/v1/selectors/validate` | `selectors:write` | Validate a selector. |
| `GET` | `/api/v1/semantic-attributes` | `context:read` | List semantic attributes. |
| `GET` | `/api/v1/audit-events` | `audit:read` | List audit events. |
| `GET` | `/api/v1/audit-events/export` | `admin:manage` | Export audit events. |
| `GET` | `/api/v1/admin/organisation` | `admin:manage` | Read organisation settings. |
| `GET` | `/api/v1/admin/users` | `admin:manage` | List operator users. |
| `GET` | `/api/v1/blueprints` | `admin:manage` | List blueprint imports. |
| `GET` | `/api/v1/governance/policies` | `admin:manage` | List governance policies. |
| `GET` | `/api/v1/billing/usage` | `billing:read` | Read usage-metering overview. |
| `POST` | `/api/v1/events/source-system` | `events:ingest` | Ingest a provider-neutral source event. |
| `POST` | `/api/v1/api-clients` | `admin:manage` | Create an API client. |
| `POST` | `/api/v1/api-clients/{id}/rotate` | `admin:manage` | Rotate an API client secret. |
| `DELETE` | `/api/v1/api-clients/{id}` | `admin:manage` | Revoke an API client. |
| `GET` | `/api/v1/webhook-signing-secrets` | `admin:manage` | List webhook signing secrets. |
| `POST` | `/api/v1/webhook-signing-secrets` | `admin:manage` | Create a webhook signing secret. |
| `POST` | `/api/v1/webhook-signing-secrets/{id}/rotate` | `admin:manage` | Rotate a webhook signing secret. |
| `DELETE` | `/api/v1/webhook-signing-secrets/{id}` | `admin:manage` | Revoke a webhook signing secret. |
| `POST` | `/api/v1/blueprints/upload` | `blueprints:write` | Upload a blueprint payload. |
| `POST` | `/api/v1/blueprints/validate` | `blueprints:write` | Validate a blueprint payload. |
| `POST` | `/api/v1/blueprints/preview` | `blueprints:write` | Preview a blueprint import. |
| `POST` | `/api/v1/blueprints/import` | `blueprints:write` | Import a blueprint. |

## Legacy REST

The legacy surface is implemented in
`src/KynticAI.Scout.Api/Rest/RestEndpointRouteBuilderExtensions.cs`.

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/api/rest/tenants/{tenantSlug}/users/{externalUserId}/context` | Fetch user context. |
| `GET` | `/api/rest/tenants/{tenantSlug}/users/{externalUserId}/facts` | Fetch user facts. |
| `POST` | `/api/rest/tenants/{tenantSlug}/users/{externalUserId}/sales-context-package` | Fetch a scoped context package. |
| `GET` | `/api/rest/tenants/{tenantSlug}/audit-events` | Fetch audit events. |
| `GET` | `/api/rest/tenants/{tenantSlug}/saas/overview` | Fetch local architecture overview metadata. |
| `POST` | `/api/rest/tenants/{tenantSlug}/users/{externalUserId}/recompute` | Queue recomputation. |
| `GET` | `/api/rest/connectors/plugins` | List connector plugin definitions. |
| `POST` | `/api/rest/connectors/register` | Register connector metadata. |
| `POST` | `/api/rest/connectors/validate` | Validate connector configuration. |
| `POST` | `/api/rest/connectors/health` | Check connector health. |
| `POST` | `/api/rest/blueprints/upload` | Upload a blueprint payload. |
| `POST` | `/api/rest/blueprints/validate` | Validate a blueprint payload. |
| `POST` | `/api/rest/blueprints/preview` | Preview a blueprint import. |
| `POST` | `/api/rest/blueprints/import` | Import a blueprint. |
| `POST` | `/api/rest/selectors/preview` | Preview a selector. |
| `POST` | `/api/rest/selectors/validate` | Validate a selector. |

## Example

These examples use the Docker demo stack or local demo scripts on port
`5198`. For the API-only Docker path, use port `8080` — see the [API
Overview port note](/apis/overview/#ports).

```bash
TOKEN=$(curl -s -X POST http://127.0.0.1:5198/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"tenantSlug":"demo","email":"admin@scout.local","password":"DemoAdmin123!"}' \
  | jq -r '.accessToken')

curl "http://127.0.0.1:5198/api/v1/context/users/123?tenantSlug=demo" \
  -H "Authorization: Bearer $TOKEN"
```

## Pagination

List endpoints use `page` and `pageSize`. Page size is capped at `200`.

```json
{
  "items": [],
  "page": 1,
  "pageSize": 50,
  "totalCount": 0,
  "hasMore": false
}
```

## Errors

Versioned REST errors use an envelope with a stable code and correlation ID:

```json
{
  "error": {
    "code": "context.user_not_found",
    "message": "User context was not found.",
    "correlationId": "0HMS...",
    "details": null
  }
}
```

## OpenAPI

When `Platform__EnableOpenApi=true`, `/api-docs`, `/swagger`, and
`/swagger/v1/swagger.json` are the local runtime sources of truth for the
generated OpenAPI contract.

The static KynticAI Score API contract is separate from the Scout runtime
OpenAPI output. It lives at `schema/kyntic-score.openapi.yaml` and defines
`InvestmentScore`, `CreditScore`, and `JobScore` request/response shapes.

## Related Pages

- [GraphQL API](/apis/graphql/)
- [API Overview](/apis/overview/)
- [Score API Contract](/apis/score-api/)
- [SDKs](/sdks/overview/)
