# Production Install Checklist

This checklist is for a first paid pilot or production-style self-hosted deployment of the KynticAI Scout customer data plane.

The safest default is that customer operational data stays in the customer's environment. The hosted control plane, if used later, should manage account, licence, download, support, and update-channel concerns without requiring raw source records, connector credentials, context facts, or prompt packages to leave the data plane.

## Deployment Mode

- Use `Platform__Mode=SelfHosted` for production-style deployments; use `ManagedDataPlane` where a private managed control-plane deployment is in place.
- `BackendOnly` and `SaaS` remain recognised compatibility aliases for existing definitions; prefer `SelfHosted`/`ManagedDataPlane` for new deployments.
- Use PostgreSQL for production-style deployments.
- Scout uses a single Scout PostgreSQL store; no separate CustomerOps database is required or provisioned.
- Keep SQLite for local evaluation and laptop demos only.
- Set `Bootstrap__SeedDemoData=false` outside demo environments.
- Set `VITE_DEMO_FALLBACK=false` for any customer-facing environment.
- Confirm the frontend points at the intended API and GraphQL endpoints.

## Demo Fallback Warning

`VITE_DEMO_FALLBACK=true` is for local demos only. It lets the frontend fall back to mock data when a local API is not running.

For paid pilots, production-style deployments, customer environments, and hosted static builds:

- set `VITE_DEMO_FALLBACK=false`
- fail visibly if the API is unavailable
- never use demo fallback to mask API failures
- never present demo fallback data as customer data
- confirm the built frontend was created with the intended production values

## Executable Preflight

Run the local production-style environment check before a paid pilot rehearsal or customer deployment:

```powershell
.\scripts\check-production-env.ps1 -EnvFile .env.production.local
```

On Linux/macOS hosts:

```bash
./scripts/check-production-env.sh .env.production.local
```

The preflight refuses demo fallback, placeholder signing keys, SQLite/local demo mode, demo seeding, temporary Data Protection keys, and missing PostgreSQL connection strings. It prints only a redacted configuration summary.

## Secrets

- Set `Auth__SigningKey` to a high-entropy secret of at least 48 bytes.
- Store secrets in the customer's approved secret store, not in source control.
- Do not commit `.env`, local database files, support bundles, logs, licence files, or private signing keys.
- Rotate any secret that was used on a developer machine during a supply-chain incident.
- Use separate API clients for read, write, event ingestion, and administration.

## Databases

- Use the single Scout PostgreSQL database as the data plane store; no CustomerOps database is required.
- Back up the Scout database and the Data Protection key ring together.
- Upstream CRM/ERP and other source systems are customer-owned; document their backup ownership with the customer rather than treating them as Scout-owned backups.
- Test restore before calling the deployment production ready.
- Run EF Core migrations before starting a new application version (use the `migrate` command; it forces migration even with `Bootstrap__ApplyMigrationsOnStartup=false`).
- Do not enable demo seeding in hosted or customer environments.

## Data Protection Keys

- Persist ASP.NET Data Protection keys in a mounted, backed-up location.
- Set `DataProtection__RequirePersistentKeys=true` in production-style deployments.
- Back up the key ring with the scout database.
- Treat the key ring as sensitive because protected connector credentials depend on it.
- Never commit the key ring or include it in public support bundles.

## Connectors And Credentials

- Start with generic SQL, REST, CSV, mock, or exported datasets unless a paid connector has been commercially agreed.
- Keep vendor-specific enterprise connectors in the private enterprise repo.
- Use least-privilege credentials for every source system.
- Prefer metadata-only ingestion for communication and knowledge systems.
- Require explicit customer opt-in before processing message bodies, document bodies, issue descriptions, calendar descriptions, or attachment content.
- Record provenance for every AI-visible or workflow-visible context fact.

## Privacy And Governance

- Define PII masking rules before exposing context to downstream systems.
- Confirm what each role can see before enabling customer users.
- Configure freshness windows for key semantic facts.
- Mark stale, masked, or low-confidence facts clearly.
- Keep audit logging enabled for context lookup, recompute, selector change, connector change, API key activity, and permission denial.

## API And Access

- Create named API clients per integration.
- Hash API keys and show the clear value only once.
- Rotate API keys on a schedule.
- Give event-ingestion clients only event-ingestion scopes.
- Give downstream readers only context-read scopes.
- Confirm REST and GraphQL endpoints reject cross-tenant access.
- Set `Platform__EnableOpenApi=false` for public production unless Swagger is separately protected behind authenticated operator tooling.
- Keep `GraphQl__DisableIntrospectionInProduction=true`, `GraphQl__EnableGetRequestsInProduction=false`, and bounded GraphQL depth, timeout, batch, and concurrency settings.
- Keep `SecurityHeaders__Enabled=true` and review the CSP before adding browser-facing integrations.
- Configure exact HTTPS CORS origins; do not use wildcard origins in production.
- Treat browser bearer-token storage as a local/admin MVP control; before broad customer users, move to a hardened HttpOnly cookie/session flow or an equivalent protected access pattern.

## Observability

- Configure structured logs.
- Configure OpenTelemetry where available.
- Monitor `/health/live`, `/health/ready`, `/health`, and `/api/v1/health`.
- Capture selector execution failures, source event failures, recompute job failures, and API error rates.
- Keep logs free of secrets, raw connector credentials, and unnecessary source payloads.

## Backups And Support

- Document backup ownership with the customer: Scout owns the Scout database, Data Protection key ring, and Scout-side connector/credentials configuration; upstream CRM/ERP and other source systems remain under customer ownership.
- Document restore steps and expected recovery time.
- Generate support bundles with redaction enabled.
- Exclude raw customer data, secrets, keys, and connector credentials from support bundles unless explicitly approved.
- Agree the support channel, response expectations, and escalation route before the pilot starts.
- If the private cloud control plane is used, align with its backup, restore, incident, support, and live-hosting preflight runbooks without exposing private cloud internals or raw customer data in this public repo.

## Pre-Pilot Go-Live Checklist

- `Bootstrap__SeedDemoData=false`
- `VITE_DEMO_FALLBACK=false`
- PostgreSQL configured and backed up
- Data Protection key ring persisted and backed up
- production signing key configured
- demo accounts removed or disabled
- customer tenant and first admin created
- API clients scoped and recorded
- connector credentials stored safely
- context facts show confidence, freshness, provenance, and masking status
- audit log visible to authorised admins
- rollback and restore path documented

## Customer Data-Plane Responsibility

Confirm ownership before go-live:

- who operates the customer data plane
- who owns database backups and restore tests
- who owns connector credentials and rotation
- who approves data categories and masking rules
- who receives support bundles
- who approves upgrades
- who decides whether any aggregate usage metadata may be shared with an optional Cloud/control plane

## Hosted Control-Plane Boundary

The future hosted control plane may manage accounts, billing, licences, downloads, update channels, support access, entitlement metadata, customer contacts, and optional aggregate usage. It should not require raw customer records, connector credentials, context facts, prompt context packages, or operational source data by default.
