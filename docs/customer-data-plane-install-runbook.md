# Customer Data-Plane Install Runbook

This runbook covers a production-style customer-owned Scout data-plane install for a supported paid pilot.

## Prerequisites

- approved customer owner and technical owner
- PostgreSQL database or managed PostgreSQL service
- secret store for signing keys, database credentials, API clients, connector credentials, and Data Protection keys
- network route to approved source systems
- agreed backup owner, restore rehearsal owner, support channel, and incident contact
- reviewed data categories, masking rules, retention, and connector scope

## Environment Variables

Required production-style values:

```text
ASPNETCORE_ENVIRONMENT=Production
Platform__Mode=SelfHosted
Database__Provider=Postgres
Bootstrap__ApplyMigrationsOnStartup=false
Bootstrap__SeedDemoData=false
FeatureFlags__DemoExperience=false
VITE_DEMO_FALLBACK=false
ConnectionStrings__Scout=<secret reference>
Auth__SigningKey=<secret reference>
DataProtection__KeyRingPath=<persistent mounted path>
DataProtection__RequirePersistentKeys=true
Cors__AllowedOrigins__0=<approved admin console origin>
Telemetry__OtlpEndpoint=<optional collector>
```

Run:

```powershell
.\scripts\check-production-env.ps1 -EnvFile .env.production.local
```

## Database Setup And Migrations

- create separate scout and customer-ops databases/schemas where possible
- run migrations from a controlled admin job before starting the new application version
- disable demo seed data
- capture migration logs with secrets redacted

## Secrets And Data Protection

- generate `Auth__SigningKey` outside the repo
- persist ASP.NET Data Protection keys on a backed-up volume
- never commit key rings, local `.env` files, licence files, logs, support bundles, or local databases
- rotate any secret copied into chat, tickets, logs, or screenshots

## Admin Credentials And API Clients

- create named admin users for named people only
- create separate API clients for read, write, event ingestion, and admin tasks
- use least-privilege scopes from `docs/api-scopes.md`
- record rotation owner and review date

## Enterprise Package And Licence

- install private enterprise packages only through an approved private delivery method
- place the licence file outside git on a protected local path
- confirm the licence entitlement matches installed modules

## Registration

If using the private cloud control plane, register the data plane with a short-lived token. Registration and heartbeat metadata must be aggregate-only and must not include source rows, customer records, prompt packages, connector credentials, or context facts.

## Health Checks And Smoke Tests

- `/health/live`
- `/health/ready`
- `/health`
- `/api/v1/health`
- login/token flow
- scoped context read
- selector preview or dry-run
- audit event visibility
- backup command dry-run

## Logs, Backup, And Rollback

- send structured logs to the customer's approved sink
- exclude secrets and raw payloads from logs
- back up databases and Data Protection key ring
- rehearse restore before go-live
- keep the previous package/container available for rollback
- document migration rollback or restore-from-backup decision points

## Common Failure Modes

- demo fallback left on
- SQLite selected accidentally
- Data Protection keys not persisted
- signing key is a placeholder
- CORS points at the wrong admin origin
- connector credential pasted inline rather than referenced through a vault
- support bundle contains raw customer data
