---
title: Self-Hosting
description: Self-hosting KynticAI Scout as a customer-owned data plane.
---

Scout can run as a customer-owned data plane. The default production-style
shape is:

- ASP.NET Core API
- PostgreSQL for Scout persistence
- PostgreSQL or another approved source database for operational data
- static React admin console pointing at the API
- explicit API clients for downstream integrations
- protected connector credentials
- audit and provenance enabled

SQLite remains useful for local evaluation and laptop demos. Do not use it
as the production-style database.

## Docker API Smoke Test

For the fastest local API check:

```bash
docker compose -f deploy/docker-compose.yml up -d scout-api --build
curl http://127.0.0.1:8080/health/ready
```

This path is useful for evaluating the API and OpenAPI/GraphQL surfaces. It
is not a production deployment recipe.

## Production-Style Checklist

Before running a customer or production-style environment:

| Area | Required baseline |
|---|---|
| Mode | Use `Platform__Mode=SaaS` or `Platform__Mode=BackendOnly`. |
| Database | Use PostgreSQL and apply EF Core migrations. |
| Demo data | Set `Bootstrap__SeedDemoData=false`. |
| Frontend fallback | Set `VITE_DEMO_FALLBACK=false`. |
| Signing key | Set `Auth__SigningKey` to a high-entropy secret of at least 48 bytes. |
| Data Protection | Persist and back up ASP.NET Data Protection keys. |
| OpenAPI | Disable public OpenAPI in production unless separately protected. |
| GraphQL | Keep production introspection and GET requests disabled unless explicitly approved. |
| CORS | Use exact HTTPS origins, not wildcards. |
| API clients | Create scoped clients per integration. |
| Connectors | Use least-privilege source credentials and safe provenance. |
| Audit | Keep audit logging enabled. |

The legacy checklist remains in `docs/production-install-checklist.md`.

## Migrations

Run EF Core migrations before starting a production-style API version. The
solution contains migrations for both Scout persistence and the demo
customer-operations database:

```bash
dotnet run --project src/KynticAI.Scout.Api/KynticAI.Scout.Api.csproj -- migrate
```

The API host treats `migrate`, `init`, `bootstrap`, and `migrate-database`
as migration-only arguments. Use the repository rehearsal scripts before
changing persistent infrastructure.

## Exporting Data

To move context/evidence data out of a deployment, use the Scout Migration
Tool (`docs/migration-tool.md` in this repository). It is a local-only
export CLI that writes a portable migration-export package to a local folder
and never uploads data.

## Secrets

Do not commit:

- `.env` files
- database files
- support bundles
- logs with customer data
- licence files
- signing keys
- Data Protection key rings
- connector credentials

Use a customer-approved secret store for production-style environments.

## Health Checks

Useful local health endpoints:

| Path | Purpose |
|---|---|
| `/health/live` | Process liveness. |
| `/health/ready` | Database readiness. |
| `/health` | Aggregate API health. |
| `/api/v1/health` | Versioned REST health check. |

## Support Boundary

The public repo documents the Scout customer data plane. It does not add
hosting automation, CI/CD, GitHub Pages, release workflows, deployment
pipelines, managed installers, or private enterprise deployment packs.
