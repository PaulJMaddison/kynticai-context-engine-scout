# Hosted PostgreSQL Deployment

This guide describes the first production-like deployment shape for the public KynticAI Scout repo: React as a static docs/demo/admin app, ASP.NET Core as a Docker web service, and a single PostgreSQL database for the Scout data plane. SQLite is used only for local demo mode.

It is not the full future managed control plane. The open-core backend still operates as the customer data plane: connectors, selectors, context facts, provenance, audit logs, and credentials stay in the customer-controlled environment unless the customer explicitly exports them.

## Target Architecture

Scout's production data plane uses a **single PostgreSQL store** (`ScoutDbContext`) holding tenants, users, workspaces, selectors, context facts, provenance, source-event log, audit log, API clients, and the connector catalogue. There is no separately provisioned CustomerOps database: Scout does not require a CustomerOps data source to bootstrap, migrate, or serve context. Upstream CRM/ERP/source systems are reached through connectors with their own credentials and are not Scout-owned stores.

- `apps/web` builds to static docs/demo/admin files with `npm run build` and can be hosted by Render Static Sites, Netlify, Cloudflare Pages, S3/CloudFront, or the bundled nginx image.
- `src/KynticAI.Scout.Api` builds with `src/KynticAI.Scout.Api/Dockerfile` and listens on port `8080`.
- Hosted production mode uses PostgreSQL for the single Scout store.
- Local demo mode remains SQLite-backed and does not require Docker.

## Render Deployment

The repository includes a root `render.yaml` blueprint with:

- `kynticai-scout-web`: static React site built from `apps/web`.
- `kynticai-scout-api`: Docker web service with `/health/ready` as the readiness check.
- `scout-db`: the single managed PostgreSQL database for the Scout data plane.

After creating the Render Blueprint, set these secrets and verify generated service URLs:

```text
Platform__Mode=SelfHosted
Database__Provider=Postgres
Bootstrap__ApplyMigrationsOnStartup=false
Bootstrap__SeedDemoData=false
ConnectionStrings__Scout=<managed Scout PostgreSQL connection string>
ReferenceData__CustomerOpsEnabled=false
Auth__SigningKey=<48+ byte random secret>
Cors__AllowedOrigins__0=https://<frontend-domain>
SaaS__PublicBaseUrl=https://<api-domain>
DataProtection__KeyRingPath=/var/lib/scout/data-protection-keys
DataProtection__RequirePersistentKeys=true
VITE_API_BASE_URL=https://<api-domain>
VITE_GRAPHQL_ENDPOINT=https://<api-domain>/graphql
VITE_DEMO_FALLBACK=false
Telemetry__OtlpEndpoint=<optional OTLP endpoint>
```

`Platform__Mode=SelfHosted` is the production mode. `BackendOnly` and `SaaS` remain recognised as compatibility aliases for existing definitions; new deployments should use `SelfHosted` (on-prem data plane) or `ManagedDataPlane` where a private managed control-plane deployment is used.

Render supports Blueprint fields such as `runtime: docker`, `runtime: static`, `preDeployCommand`, `healthCheckPath`, `rootDir`, and managed Postgres `fromDatabase` environment variables. If you rename services, update the frontend API URL and backend CORS origin at the same time.

## Backend Docker Image

Build locally:

```powershell
docker build -f src/KynticAI.Scout.Api/Dockerfile -t scout-api .
```

Run in hosted-style mode:

```powershell
docker run --rm -p 8080:8080 `
  -e ASPNETCORE_ENVIRONMENT=Production `
  -e Platform__Mode=SelfHosted `
  -e Database__Provider=Postgres `
  -e Bootstrap__ApplyMigrationsOnStartup=false `
  -e Bootstrap__SeedDemoData=false `
  -e ConnectionStrings__Scout="<managed Scout PostgreSQL connection string>" `
  -e ReferenceData__CustomerOpsEnabled=false `
  -e Auth__Issuer=Scout `
  -e Auth__Audience=KynticAI.Scout.Api `
  -e Auth__SigningKey="<48+ byte random secret>" `
  -e DataProtection__KeyRingPath="/var/lib/scout/data-protection-keys" `
  -e DataProtection__RequirePersistentKeys=true `
  -v scout-data-protection-keys:/var/lib/scout/data-protection-keys `
  -e Cors__AllowedOrigins__0="https://<frontend-domain>" `
  scout-api
```

## Database Migration Command

Hosted environments should migrate the single Scout store before starting a new application version. The `migrate` argument forces schema migration even when the production `Bootstrap__ApplyMigrationsOnStartup=false` setting would otherwise skip it, so the connector catalogue can be seeded against a ready schema.

```powershell
docker run --rm `
  -e ASPNETCORE_ENVIRONMENT=Production `
  -e Platform__Mode=SelfHosted `
  -e Database__Provider=Postgres `
  -e Bootstrap__ApplyMigrationsOnStartup=false `
  -e Bootstrap__SeedDemoData=false `
  -e ConnectionStrings__Scout="<managed Scout PostgreSQL connection string>" `
  -e ReferenceData__CustomerOpsEnabled=false `
  -e Auth__SigningKey="<48+ byte random secret>" `
  -e DataProtection__KeyRingPath="/var/lib/scout/data-protection-keys" `
  -e DataProtection__RequirePersistentKeys=true `
  -v scout-data-protection-keys:/var/lib/scout/data-protection-keys `
  scout-api migrate
```

The `migrate` command applies EF Core migrations and seeds safe connector catalogue metadata only. It does not seed demo accounts or fictional customer records. Do not set `Bootstrap__ApplyMigrationsOnStartup=true` for regular production boots; rely on the deploy-time `migrate` command instead.

## Demo Seed Command

Demo seeding is intentionally limited to `LocalDemo`/`Demo` mode:

```powershell
.\scripts\setup-demo.ps1
```

or:

```powershell
ASPNETCORE_ENVIRONMENT=Development `
Platform__Mode=LocalDemo `
Database__Provider=Sqlite `
Bootstrap__ApplyMigrationsOnStartup=true `
Bootstrap__SeedDemoData=true `
dotnet run --project src/KynticAI.Scout.Api -- seed-demo
```

Do not set `Bootstrap__SeedDemoData=true` in hosted SaaS deployments.

## Frontend Static Build

For local development, the frontend defaults to `http://localhost:5198`. For static production hosting, set the build-time API URL explicitly:

```powershell
cd apps/web
$env:VITE_API_BASE_URL="https://<api-domain>"
$env:VITE_GRAPHQL_ENDPOINT="https://<api-domain>/graphql"
$env:VITE_DEMO_FALLBACK="false"
npm ci
npm run build
```

If the frontend and API are served from the same origin, set `VITE_API_BASE_URL=` and `VITE_GRAPHQL_ENDPOINT=/graphql`.

## Health Checks

- `/health/live`: process liveness, no database dependency.
- `/health/ready`: readiness check on the single Scout database.
- `/health`: diagnostic summary with database status.
- `/api/v1/health`: versioned REST health endpoint.

Use `/health/ready` for Render and other rolling deployment platforms. It reports a single `scout-db` check; there is no second CustomerOps database to verify.

## Production Settings

- `ASPNETCORE_ENVIRONMENT=Production`
- `Platform__Mode=SelfHosted` (or `ManagedDataPlane` for a private managed deployment; `BackendOnly`/`SaaS` are compatibility aliases)
- `Database__Provider=Postgres`
- `ConnectionStrings__Scout`: the single Scout PostgreSQL connection string
- `Bootstrap__ApplyMigrationsOnStartup=false`
- `Bootstrap__SeedDemoData=false`
- `ReferenceData__CustomerOpsEnabled=false`
- `Auth__SigningKey`: high-entropy secret, at least 48 bytes recommended.
- `DataProtection__KeyRingPath`: persistent file-system path or mounted volume for ASP.NET Data Protection keys.
- `DataProtection__RequirePersistentKeys=true`: required for Production/SaaS mode so protected connector credentials remain readable after restarts.
- `Cors__AllowedOrigins__0`: exact frontend origin, no trailing slash.
- `RateLimits__*`: tune auth and GraphQL limits per plan and deployment size.
- `Telemetry__OtlpEndpoint`: optional OTLP collector endpoint.
- `ControlPlane__Enabled=false`: keep disabled unless a private hosted control-plane service is available.
- `Licence__Mode=Community`: default for the public repo; paid self-hosted deployments can point `Licence__FilePath` at a mounted local licence file.

Production uses secure cookie policy, HSTS, forwarded proxy headers, JWT signing-key validation, persistent Data Protection key storage, JSON console logging, explicit CORS origins, and configurable rate limits.

The bundled web nginx image sets `X-Content-Type-Options`, `Referrer-Policy`, `Permissions-Policy`, and a baseline `Content-Security-Policy` that permits the optional Cloudflare Turnstile script/frame. If hosting on Render Static Sites, Netlify, Cloudflare Pages, or S3/CloudFront, reproduce equivalent headers in that platform rather than relying on this nginx config.

## Connector Credential Protection

Connector secrets are stored through `IConnectorCredentialStore` as protected references, not plaintext configuration. ASP.NET Data Protection encrypts those stored values. In container or self-hosted deployments the key ring must be persisted, otherwise a restart or new container may be unable to decrypt existing connector credentials.

Use a mounted directory such as `/var/lib/scout/data-protection-keys` and keep it backed up with the application database. Do not copy the key ring into source control or support bundles.

**Path configuration is not the same as persistence.** Setting `DataProtection__KeyRingPath` only tells Scout where to write keys; it cannot guarantee that the hosting platform keeps that path across restarts and redeploys. You must also confirm the platform actually mounts durable storage at that path:

- Render: the `kynticai-scout-api` service provisions a named `scout-data-protection` disk mounted at `/var/lib/scout/data-protection-keys` (see `render.yaml`).
- Docker Compose: use a named volume mounted at the key-ring path, not an anonymous or ephemeral container layer.
- Kubernetes/native: use a `PersistentVolumeClaim` (or equivalent durable volume) at the key-ring path.

Verify persistence with a restart/redeploy proof: store a connector credential, restart or redeploy the API, then resolve the same credential. If it fails or the key ring is absent after redeploy, persistence is not actually guaranteed and connector credentials would be unreadable.

## Logging And OpenTelemetry

Production logs are written to stdout/stderr for the hosting platform to collect. OpenTelemetry tracing and metrics are enabled for ASP.NET Core, outbound HTTP calls, runtime metrics, and background job metrics. Set `Telemetry__OtlpEndpoint` or `OTEL_EXPORTER_OTLP_ENDPOINT` to export to an OTLP collector.

## Backup And Restore

Scout's data plane is a single store, so backup ownership is straightforward: the Scout PostgreSQL database, the Data Protection key ring, and Scout-owned connector/credentials configuration are all backed up together. Upstream CRM/ERP and other source systems remain under the customer's ownership and are not Scout-owned backup responsibilities; their data enters Scout as ingested source events and context facts recorded in the Scout store.

For managed PostgreSQL, prefer the provider's scheduled backups and point-in-time recovery for the Scout database.

Manual backup:

```bash
pg_dump "$SCOUT_DATABASE_URL" --format=custom --file=scout-context.dump
```

Manual restore into an empty database (recreate the database first):

```bash
pg_restore --clean --if-exists --dbname "$SCOUT_DATABASE_URL" scout-context.dump
```

After restore, run the hosted migration command once, then check `/health/ready` and perform a tenant-scoped smoke test through REST or GraphQL.

## Support Bundle

The public repo does not ship private SLA tooling, but a safe support bundle can be produced manually without including connector credentials or raw customer records:

```bash
mkdir -p support-bundle
curl -H "Authorization: Bearer $ADMIN_TOKEN" "$SCOUT_API/api/platform/config" > support-bundle/platform-config.json
curl -H "Authorization: Bearer $ADMIN_TOKEN" "$SCOUT_API/api/v1/licence/status" > support-bundle/licence-status.json
curl -H "Authorization: Bearer $ADMIN_TOKEN" "$SCOUT_API/api/v1/health" > support-bundle/health.json
curl -H "Authorization: Bearer $ADMIN_TOKEN" "$SCOUT_API/api/v1/audit-events?pageSize=200" > support-bundle/audit-events-redacted.json
tar -czf scout-support-bundle.tgz support-bundle
```

Review the bundle before sharing it. Do not include `.env`, connector credential stores, database dumps, raw source event payloads, context packages, or customer-specific blueprint files unless the customer has explicitly approved that transfer.
