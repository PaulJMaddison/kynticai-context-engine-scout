# Pilot Readiness Runbook

This runbook is for paid pilots of customer-owned data-plane deployments. It does not claim self-serve SaaS readiness.

## Local Check

Run:

```powershell
.\scripts\pilot-readiness.ps1 -SkipBuild -SkipTests
```

Remove `-SkipBuild` and `-SkipTests` when the required .NET SDK is installed.

## PostgreSQL Smoke

- Set `ConnectionStrings__Scout` for the single Scout store.
- Run the readiness script with `-ProductionMode`.
- Confirm migrations, health checks, context reads, source event ingest, audit reads, and rollback notes.

## Backup Restore Rehearsal

- Take a schema-only and data backup from a non-production pilot database.
- Restore into a disposable database.
- Run `/health/ready`, a tenant-scoped context lookup, and an audit query.
- Record restore duration and the person responsible for restore approval.

## Support Bundle Dry Run

- Generate only redacted configuration, health, version, and audit metadata.
- Confirm raw records, secrets, API keys, webhook signing secrets, tokens, local databases, and licence files are excluded.
- Review the bundle with the customer before sending it outside their environment.

## Upgrade And Rollback Rehearsal

- Record current image/package versions.
- Apply the upgrade in a staging or disposable pilot environment.
- Run smoke checks and focused tests.
- Restore the previous package and database backup if rollback is required.

## Handover Checklist

- Production examples keep `VITE_DEMO_FALLBACK=false`.
- Production backend config keeps `Bootstrap__SeedDemoData=false`.
- PostgreSQL connection strings are supplied through the host secret store.
- API clients use least-privilege scopes.
- Webhook signing secrets are separate from API keys and stored hashed.
- Customer connector credentials remain in the customer-approved vault.
- No GitHub Actions workflows, generated artefacts, local databases, logs, support bundles, or secrets are committed.

## Customer-Specific Work

Each pilot still needs customer-approved identity setup, connector consent, data classification, retention policy, backup owner, incident contact, and support-bundle approval.
