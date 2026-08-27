# Getting Started with KynticAI Scout

This guide covers the recommended Scout evaluation install: a Docker-contained stack with PostgreSQL, API, web console, and observability. Local .NET/Node scripts still exist for contributors, but the customer evaluation install path should stay Docker-first so the data plane is self-contained.

---

## Prerequisites

You need:

| Approach | Requirements |
|---|---|
| **Docker evaluation** | Git, Docker Desktop or Docker Engine 24+ with Docker Compose |
| **Contributor-only local runtime** | .NET 10.0 SDK and Node.js 22+, or the repo-local runtime helper scripts |

---

## Option 1 - Docker Quick Start

Clone and start the full stack:

```bash
git clone https://github.com/PaulJMaddison/kynticai-context-engine-scout.git
cd scout
sh ./scripts/start-scout-docker.sh --reset
```

Windows PowerShell:

```powershell
git clone https://github.com/PaulJMaddison/kynticai-context-engine-scout.git
cd scout
.\scripts\start-scout-docker.ps1 -Reset
```

The script builds images, starts Docker Compose, waits for API and web readiness, logs in, verifies that the guided demo customer context is available, validates/registers a standard connector, runs connector health, and sends local/LAN source-event webhooks.

When the self-test finishes it opens a local installation report in your browser and saves it at:

```text
.local/scout-install-report.html
```

The report includes the verified checks, running URLs, detected LAN webhook URL, login details, first walkthrough, webhook guidance, and upgrade/stop commands. Use `--no-open-report` on Unix shells or `-NoOpenReport` on PowerShell if you want to generate the report without opening it.

Open [http://127.0.0.1:5173](http://127.0.0.1:5173) and log in:

| Field | Value |
|---|---|
| Tenant | `demo` |
| Email | `admin@scout.local` |
| Password | `DemoAdmin123!` |

The seeded walkthrough is a customer data-plane proof. It links exact authorised demo records such as CRM contact/account, account registration/profile, sales activity, opportunity, email reply, meeting-booked, web conversion, pricing-page, support-ticket, product-usage, billing-health, and won/lost outcome signals into relationships, attribution paths, and governed JSON with citations and masking decisions. Optional Cloud/control-plane concerns are limited to commercial metadata and are not required for the local demo.

Recommended web walkthrough:

1. Open `/demo` for the executive walkthrough.
2. Open `/customers/123` for Avery Stone at Larkspur Logistics Group.
3. Open `/relationship-intelligence` to inspect exact linked records, relationships, citations, masking, and the recommended next action.
4. Open `/data-sources` to use the connector lab: choose an executable connector, validate the sample configuration, register it, run health, and send a safe source event as a new data item.
5. Open `/admin/events` to confirm the source event was accepted and stored.
6. Open `/admin/connectors` to compare executable open-core connectors with enterprise/SaaS catalogue placeholders.

The standard executable connectors in the public Docker build are generic SQL/PostgreSQL, generic REST API with static-response preview support, CSV upload rows, mock CRM, mock billing, mock support, mock payload/signal, in-memory inventory, and the connector authoring template. Vendor-specific CRM, warehouse, support, ERP, email, chat, calendar, analytics, issue, project, and knowledge connectors are catalogue placeholders unless a private/customer package implements them.

Docker services:

| Service | URL |
|---|---|
| Web console | [http://127.0.0.1:5173](http://127.0.0.1:5173) |
| API | [http://127.0.0.1:5198](http://127.0.0.1:5198) |
| OpenAPI / Scalar | [http://127.0.0.1:5198/api-docs](http://127.0.0.1:5198/api-docs) |
| Grafana | [http://127.0.0.1:3000](http://127.0.0.1:3000) |
| Prometheus | [http://127.0.0.1:9090](http://127.0.0.1:9090) |
| Tempo | [http://127.0.0.1:3200](http://127.0.0.1:3200) |

### Ports

The full demo stack — Docker via `scripts/start-scout-docker.sh` / `.\scripts\start-scout-docker.ps1`, or the local demo scripts — exposes the API at `http://127.0.0.1:5198` and the web console at `http://127.0.0.1:5173`. The API-only Docker quickstart (`docker compose -f deploy/docker-compose.yml up scout-api`, or a manual `docker run -p 8080:8080`) exposes the API at `http://127.0.0.1:8080`. When following the API-only Docker path, use port `8080`; everywhere else, use port `5198`.

### Local / LAN Webhooks Without DNS

The Docker compose file publishes the API on `0.0.0.0:5198`. If another system is on the same trusted LAN/VPN, DNS is optional; use the host IP address:

```text
http://<host-ip>:5198/api/v1/events/source-system?tenantSlug=demo
```

The start scripts print this URL when they detect a LAN IP. For example:

```text
http://192.168.1.145:5198/api/v1/events/source-system?tenantSlug=demo
```

IP-only HTTP is suitable for local evaluation, workshops, private networks, VPNs, or static private IP customer installs. For public internet webhooks, use HTTPS with stable DNS or a reverse proxy. If testing from another Windows machine, allow inbound TCP `5198` through the host firewall.

Quick JWT-auth smoke test:

```bash
HOST_API=http://<host-ip>:5198
TOKEN=$(curl -s -X POST "$HOST_API/api/auth/login" \
  -H "Content-Type: application/json" \
  -d '{"tenantSlug":"demo","email":"admin@scout.local","password":"DemoAdmin123!"}' \
  | sed -n 's/.*"accessToken":"\([^"]*\)".*/\1/p')

curl -X POST "$HOST_API/api/v1/events/source-system?tenantSlug=demo" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "eventId": "lan-smoke-001",
    "sourceSystem": "customer_context_rollups",
    "eventType": "source.product_usage.rollup_ready",
    "externalUserId": "123",
    "externalAccountId": "acct-larkspur-logistics",
    "payload": {
      "active_days_30": 26,
      "pricing_page_visits_30": 4,
      "source": "lan-webhook-smoke"
    }
  }'
```

For production-style machine senders, create an API client with `events:ingest` and use the HMAC signing flow in [Webhook Events](webhook-events.md).

Useful commands:

```bash
docker compose ps
docker compose logs -f api web
docker compose down
docker compose down -v
```

Upgrade a local evaluation install:

```bash
git pull
sh ./scripts/start-scout-docker.sh
```

This rebuilds changed images and keeps the Docker volumes. Use `--reset` only when you want to delete local Scout demo data and reseed from scratch.

---

## Option 2 - Production-Style Docker Settings

The default compose file is a local demo. For production-style customer data-plane rehearsals, create an environment file and disable demo seeding:

```bash
cp .env.example .env.production.local
```

Set at minimum:

```env
ASPNETCORE_ENVIRONMENT=Production
Platform__Mode=BackendOnly
Database__Provider=Postgres
Bootstrap__ApplyMigrationsOnStartup=false
Bootstrap__SeedDemoData=false
FeatureFlags__DemoExperience=false
VITE_DEMO_FALLBACK=false
Auth__SigningKey=<48+-byte-random-secret>
DataProtection__RequirePersistentKeys=true
```

Before any customer-facing deployment, run:

```powershell
.\scripts\check-production-env.ps1 -EnvFile .env.production.local
```

See [Production Install Checklist](production-install-checklist.md), [Customer Data-Plane Install Runbook](customer-data-plane-install-runbook.md), and [Hosted Deployment](hosted-deployment.md).

---

## Option 3 - Contributor-Only Local Runtime

Use this only for source development where running the API and Vite outside Docker is useful:

```bash
sh ./scripts/setup-demo.sh
sh ./scripts/start-demo.sh
```

Windows:

```powershell
.\scripts\setup-demo.ps1
.\scripts\start-demo.ps1
```

---

## First API Calls

Once the Docker stack is running, try these requests to verify everything works.

### Health Check

```bash
curl http://127.0.0.1:5198/health/ready
```

Expected response:

```json
{"status":"ok","service":"KynticAI.Scout.Api","checks":[{"name":"scout-db","status":"ok"}]}
```

### Platform Configuration

```bash
curl http://127.0.0.1:5198/api/platform/config
```

Returns the effective runtime mode, enabled features, and endpoint configuration.

### Authenticate and Query Context

Most API and GraphQL endpoints require a JWT token. Log in first:

```bash
# 1. Log in with demo credentials
TOKEN=$(curl -s -X POST http://127.0.0.1:5198/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"tenantSlug":"demo","email":"admin@scout.local","password":"DemoAdmin123!"}' \
  | sed -n 's/.*"accessToken":"\([^"]*\)".*/\1/p')

# 2. Fetch user context (REST)
curl "http://127.0.0.1:5198/api/v1/context/users/123?tenantSlug=demo" \
  -H "Authorization: Bearer $TOKEN"

# 3. GraphQL introspection
curl -X POST http://127.0.0.1:5198/graphql \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"query":"{ __schema { queryType { name } } }"}'
```

Or open [http://127.0.0.1:5198/graphql](http://127.0.0.1:5198/graphql) in a browser for the Banana Cake Pop GraphQL IDE (you will need to set the `Authorization: Bearer <token>` header).

### OpenAPI / Swagger

When `Platform__EnableOpenApi=true` (the default for development), browse the full API reference:

| UI | URL |
|---|---|
| **Scalar** (recommended) | [http://127.0.0.1:5198/api-docs](http://127.0.0.1:5198/api-docs) |
| **Swagger UI** | [http://127.0.0.1:5198/swagger](http://127.0.0.1:5198/swagger) |

See [API Documentation](api/README.md) for details on exporting the spec and authentication.

---

## SDK Usage

Public npm and NuGet publishing are not yet configured, so the commands below use the in-repository package paths.

### TypeScript

```bash
npm install ../packages/typescript/scout-sdk
```

```typescript
import { createScoutClient } from '@kynticai/scout-sdk'

const scout = createScoutClient({
  baseUrl: 'http://127.0.0.1:5198',
  accessToken: process.env.SCOUT_TOKEN,
})

const context = await scout.users.getContext('demo', '123')
console.log(context?.fullName, context?.overallConfidence)

const facts = await scout.facts.getForUser('demo', '123', { attributeKey: 'health' })
console.log(facts)
```

### .NET

```bash
dotnet add reference src/KynticAI.Scout.Sdk/KynticAI.Scout.Sdk.csproj
```

```csharp
using KynticAI.Scout.Sdk;

var client = new ScoutClient(new ScoutOptions
{
    BaseUrl = "http://127.0.0.1:5198",
    AccessToken = Environment.GetEnvironmentVariable("SCOUT_TOKEN"),
});

var context = await client.Users.GetContextAsync("demo", "123");
Console.WriteLine($"{context.FullName} — confidence: {context.OverallConfidence}");
```

---

## Building the Docker Image Manually

```bash
docker build -t scout-api -f src/KynticAI.Scout.Api/Dockerfile .
docker run --rm -p 8080:8080 \
  -e Database__Provider=Sqlite \
  -e "ConnectionStrings__Scout=Data Source=/var/lib/scout/scout_context.db" \
  -e Bootstrap__ApplyMigrationsOnStartup=true \
  -e Bootstrap__SeedDemoData=true \
  -e "Auth__SigningKey=change-me-to-at-least-32-bytes-random" \
  scout-api
```

---

## Running Tests

```bash
# If you ran setup-demo.sh, use the repo-local SDK:
./.dotnet/dotnet test KynticAI.Scout.slnx

# Or with a global .NET 10 SDK on your PATH:
dotnet test KynticAI.Scout.slnx
```

```bash
# TypeScript SDK tests
cd packages/typescript/scout-sdk
npm install && npm test

# Web app tests and lint (from repo root)
cd apps/web
npm install && npm run lint && npm test
```

---

## Configuration Reference

See the full list of environment variables in [deploy/.env.example](../deploy/.env.example) and the root [.env.example](../.env.example).

Key settings:

| Variable | Description | Default |
|---|---|---|
| `Platform__Mode` | `BackendOnly`, `LocalDemo`, or `SaaS` | `BackendOnly` |
| `Database__Provider` | `Sqlite` or `Postgres` | `Sqlite` |
| `Bootstrap__SeedDemoData` | Seed fictional demo data on startup | `false` |
| `Auth__SigningKey` | JWT signing key (48+ bytes for production) | dev placeholder |
| `Platform__EnableOpenApi` | Enable API docs at `/api-docs` and `/swagger` | `true` |
| `Platform__EnableGraphQl` | Enable GraphQL endpoint at `/graphql` | `true` |

---

## Further Reading

- [Public API Contract](public-api-contract.md) — GraphQL, REST, SDK, auth, pagination, error contracts
- [Connector Plugin Model](connector-plugin-model.md) — how to build and register connectors
- [Connector Authoring Guide](connector-authoring.md) — step-by-step guide to writing a new connector
- [Connector Test Harness](connector-test-harness.md) — local-only validation tool for connector authors
- [Connector Manifest Validator](connector-manifest-validator.md) — public manifest schema and CLI
- [Customer Data Plane](customer-data-plane.md) — where data lives and what the customer owns
- [Integration Layer](integration-layer.md) — how source systems and consumers integrate
- [SDK Development](sdk-development.md) — guide for TypeScript and .NET SDK contributors
- [Production Install Checklist](production-install-checklist.md) — pre-deployment safety checks
- [Hosted Deployment](hosted-deployment.md) — Render Blueprint and cloud hosting
- [Migration Tool](migration-tool.md) — export context/evidence data out of a Scout deployment
- [Pilot Lead Capture](pilot-lead-capture.md) — how to enable the `/pilot` landing-page lead form
