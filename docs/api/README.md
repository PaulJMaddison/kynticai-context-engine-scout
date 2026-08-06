# API Documentation

The KynticAI Scout exposes a REST API and a GraphQL endpoint. When OpenAPI is enabled, interactive documentation is available through both Swagger UI and Scalar.

## Viewing API Docs Locally

Start the API in Development mode (OpenAPI is enabled by default):

```bash
# Quick start with demo data
sh ./scripts/setup-demo.sh
sh ./scripts/start-demo.sh

# Or run the API directly
dotnet run --project src/KynticAI.Scout.Api
```

Then open:

| UI | URL | Notes |
|---|---|---|
| **Scalar** (recommended) | [http://127.0.0.1:5198/api-docs](http://127.0.0.1:5198/api-docs) | Modern, searchable API reference |
| **Swagger UI** | [http://127.0.0.1:5198/swagger](http://127.0.0.1:5198/swagger) | Classic OpenAPI explorer |
| **GraphQL IDE** | [http://127.0.0.1:5198/graphql](http://127.0.0.1:5198/graphql) | Banana Cake Pop (Hot Chocolate) |

> The root URL (`/`) redirects to Scalar when OpenAPI and REST are both enabled.

## Configuration

OpenAPI documentation is controlled by the `Platform__EnableOpenApi` setting:

| Environment | Default | Notes |
|---|---|---|
| Development | `true` | Swagger + Scalar served automatically |
| Production | `false` | Disabled by default for security |

Set `Platform__EnableOpenApi=true` to enable in any environment. See the [production install checklist](../production-install-checklist.md) for guidance on exposing OpenAPI in production behind authenticated tooling.

## OpenAPI Export

The current REST schema is committed at
[docs/api/openapi.json](openapi.json) so consumers can find the exact API
shape without running anything.

To regenerate it, a helper script starts the API in Development mode,
downloads the spec, and stops the API:

```bash
sh ./scripts/export-openapi.sh
```

This writes the spec to `docs/api/openapi.json` (pretty-printed via
`python3 -m json.tool`). The script pins the API to
`http://127.0.0.1:5198`. You can also download the spec manually while the
API is running:

```bash
curl http://127.0.0.1:5198/swagger/v1/swagger.json -o docs/api/openapi.json
```

The committed export is regenerated from the code and should be refreshed
whenever the REST surface changes. Automating a freshness check in CI is a
documented follow-up; today refreshing the export is a manual step after any
API route change.

## Score API Contract

The separate KynticAI Score API contract is a static OpenAPI 3.1 document at
`schema/kyntic-score.openapi.yaml`. It defines the public
`InvestmentScore`, `CreditScore`, and `JobScore` request/response shapes for
services that implement scoring outside the Scout API process.

## Authentication

The API supports two authentication methods:

### JWT Bearer Token

Log in with user credentials to receive a short-lived JWT:

```bash
TOKEN=$(curl -s -X POST http://127.0.0.1:5198/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"tenantSlug":"demo","email":"admin@scout.local","password":"DemoAdmin123!"}' \
  | jq -r '.accessToken')

curl http://127.0.0.1:5198/api/v1/context/users/123?tenantSlug=demo \
  -H "Authorization: Bearer $TOKEN"
```

### API Key (Machine-to-Machine)

Create a scoped API client, then exchange credentials for a bearer token using the OAuth 2.0 `client_credentials` flow:

```bash
# Exchange client credentials for a bearer token
curl -X POST http://127.0.0.1:5198/api/auth/token \
  -H "Content-Type: application/json" \
  -d '{
    "grantType": "client_credentials",
    "clientId": "<client-id>",
    "clientSecret": "<client-secret>",
    "scope": "context:read"
  }'
```

Alternatively, send the API key directly via headers:
- `X-API-Key` header with client ID and key
- `Authorization: ApiKey {clientId}:{apiKey}` header

See [API Scopes](../api-scopes.md) for the full list of available scopes.

## Rate Limiting

The API applies rate limits to protect against abuse:

| Policy | Endpoint | Strategy |
|---|---|---|
| `auth` | `/api/auth/*` | Fixed window (configurable via `RateLimit__AuthPermitLimit` and `RateLimit__AuthWindowSeconds`) |
| `graphql` | `/graphql` | Token bucket (configurable via `RateLimit__GraphQl*` settings) |

When a rate limit is exceeded, the API returns HTTP `429 Too Many Requests`.

## GraphQL Endpoint

The GraphQL endpoint is available at `/graphql` when `Platform__EnableGraphQl=true` (default). It uses [Hot Chocolate](https://chillicream.com/docs/hotchocolate) and serves the Banana Cake Pop IDE in the browser.

All GraphQL requests require a valid `Authorization: Bearer <token>` header.

Sample queries are available in [samples/graphql/demo-queries.graphql](../../samples/graphql/demo-queries.graphql).

## Further Reading

- [Public API Contract](../public-api-contract.md) — full endpoint reference, SDKs, pagination, errors
- [Machine-to-Machine Identity](../machine-to-machine-identity.md) — M2M auth deep dive
- [Getting Started](../getting-started.md) — setup and first API calls
- [Webhook Events](../webhook-events.md) — event ingestion and signing
