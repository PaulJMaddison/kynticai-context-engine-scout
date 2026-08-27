# Production Install Rehearsal

This rehearsal checks whether a KynticAI Scout customer data plane is configured like a first paid pilot environment. It is not a production deployment and does not prove customer go-live readiness by itself.

The rehearsal protects the open-core boundary: it checks a customer-owned data plane that can feed the customer's own tools, not a self-serve hosted SaaS or paid enterprise connector suite.

## Rehearsal Command

PowerShell:

```powershell
.\scripts\production-rehearsal.ps1
```

Bash:

```bash
./scripts/production-rehearsal.sh
```

Add `-RunDocker` on PowerShell or `--run-docker` on Bash only when Docker is available and it is safe to start local containers.

## What The Rehearsal Checks

- production mode settings: `ASPNETCORE_ENVIRONMENT=Production` and `Platform__Mode=SelfHosted` (or `ManagedDataPlane` for a private managed deployment; `BackendOnly`/`SaaS` remain compatibility aliases)
- demo fallback disabled: `VITE_DEMO_FALLBACK=false`
- PostgreSQL configuration: `Database__Provider=Postgres`
- strong JWT signing key: `Auth__SigningKey` at least 48 characters and not a development placeholder
- persistent ASP.NET Data Protection keys: `DataProtection__RequirePersistentKeys=true` and a non-empty key ring path
- database migration path: `dotnet run --project src/KynticAI.Scout.Api/KynticAI.Scout.Api.csproj -- migrate`
- seed data disabled unless explicitly requested: `Bootstrap__SeedDemoData=false`
- backup command for the single Scout database
- restore command for disposable restore validation
- health check endpoints: `/health/live`, `/health/ready`, `/health`, `/api/v1/health`
- GraphQL endpoint: `/graphql`
- REST endpoints: `/api/rest` and `/api/v1`
- log output location: host/container stdout plus the configured platform log collector
- OpenTelemetry export settings: `Telemetry__OtlpEndpoint` or `OTEL_EXPORTER_OTLP_ENDPOINT`
- API key or machine-to-machine auth: `/api/auth/token` and API-client scopes
- basic smoke commands for health and authenticated context lookup

## PostgreSQL Backup And Restore Examples

Set these values in the shell or secret store before use:

```powershell
$env:PGHOST="localhost"
$env:PGPORT="5432"
$env:PGUSER="postgres"
$env:PGPASSWORD="<from secret store>"
```

Backup:

```powershell
pg_dump --format=custom --file .\backup\scout_context_db.dump scout_context_db
pg_dump --format=custom --file .\backup\customer_ops_db.dump customer_ops_db
```

Restore into disposable databases:

```powershell
createdb scout_context_restore_check
createdb customer_ops_restore_check
pg_restore --clean --if-exists --dbname scout_context_restore_check .\backup\scout_context_db.dump
pg_restore --clean --if-exists --dbname customer_ops_restore_check .\backup\customer_ops_db.dump
```

Run `/health/ready` and one tenant-scoped context lookup against the restored environment before calling backup/restore rehearsed.

## Smoke Test Commands

Anonymous liveness:

```powershell
Invoke-RestMethod http://127.0.0.1:5198/health/live
Invoke-RestMethod http://127.0.0.1:5198/health/ready
```

Machine token flow after an API client has been created:

```powershell
$token = Invoke-RestMethod http://127.0.0.1:5198/api/auth/token -Method Post -ContentType "application/json" -Body '{"grant_type":"client_credentials","client_id":"<client-id>","client_secret":"<client-secret>","scope":"context:read"}'
Invoke-RestMethod http://127.0.0.1:5198/api/rest/tenants/<tenant>/users/<external-user-id>/context -Headers @{ Authorization = "Bearer $($token.access_token)" }
```

GraphQL smoke after authentication:

```powershell
Invoke-RestMethod http://127.0.0.1:5198/graphql -Method Post -ContentType "application/json" -Headers @{ Authorization = "Bearer $($token.access_token)" } -Body '{"query":"query($input: UserContextLookupInput!){ userContext(input:$input){ tenantSlug externalUserId facts { attributeKey valueJson confidence provenanceJson } } }","variables":{"input":{"tenantSlug":"<tenant>","externalUserId":"<external-user-id>"}}}'
```

## Local Rehearsal Result

Record each run with:

- date and operator
- commit SHA
- operating system
- .NET SDK version
- Docker/PostgreSQL availability
- commands run
- pass/fail result
- blockers and exact next action

If Docker or PostgreSQL is not available, the script prints the exact commands to run in a safe environment rather than pretending the deployment succeeded.
