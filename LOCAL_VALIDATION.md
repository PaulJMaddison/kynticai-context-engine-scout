# Local Validation

Routine local development must stay on restore, build, and unit-style tests. Do not run Docker, hosted endpoint checks, browser proof, or enterprise connector smoke unless the matching opt-in variable is set.

## Safe Default

```powershell
dotnet restore .\KynticAI.Scout.slnx
dotnet build .\KynticAI.Scout.slnx
dotnet test .\tests\KynticAI.Scout.UnitTests\KynticAI.Scout.UnitTests.csproj
dotnet test .\tests\KynticAI.Scout.Sdk.Tests\KynticAI.Scout.Sdk.Tests.csproj

cd .\apps\web
npm install
npm run lint
npm run test
npm run build

cd ..\..\packages\typescript\scout-sdk
npm install
npm run test
```

## CI (Continuous Integration)

The GitHub Actions workflow in `.github/workflows/ci.yml` runs the safe
default only. It never runs browser, container, or enterprise proof paths:

- `backend` job: `.NET` restore, Release build with warnings as errors, then
  the unit, SDK, integration, and end-to-end test projects (all
  local/deterministic) with TRX results uploaded as an artefact.
- `frontend` job: `apps\web` lint/test/build, TypeScript SDK build/test, and
  the docs-site build.
- `public-safety` job: the forbidden-code/secret-marker scan over
  `src apps packages docs docs-site/src deploy tools`, failing on any match.

Browser proof requires `KYNTIC_RUN_BROWSER_TESTS=1`, and Docker/PostgreSQL or
enterprise connector proof requires `KYNTIC_RUN_EXTERNAL_DOTNET_TESTS=1`.
These opt-in paths are never part of default CI.

A quick non-integration test run (used by the cloud setup script) is:

```powershell
dotnet test .\KynticAI.Scout.slnx --no-restore --filter "Category!=Integration"
```

The integration test classes carry `[Trait("Category", "Integration")]`, so
this filter meaningfully excludes them while leaving the other projects'
tests in the run.

## Optional External/Container/Live Commands

| Proof path | Class | Command | Required opt-in |
|---|---|---|---|
| Web browser/Playwright proof | Opt-in browser | `cd apps\web; npm run test:e2e` | `KYNTIC_RUN_BROWSER_TESTS=1` |
| README screenshot capture | Opt-in browser | `cd apps\web; node .\.capture-readme-screenshots.mjs` | `KYNTIC_RUN_BROWSER_TESTS=1` |
| Docker/PostgreSQL production rehearsal | Opt-in container | `.\scripts\production-rehearsal.ps1 -RunDocker` | `KYNTIC_RUN_EXTERNAL_DOTNET_TESTS=1` |
| Enterprise connector smoke in paid-pilot rehearsal | Opt-in external/live | `.\scripts\paid-pilot-local-rehearsal.ps1` | `KYNTIC_RUN_EXTERNAL_DOTNET_TESTS=1` unless `-SkipEnterpriseConnectorSmoke` is supplied |
| Cross-repo paid-pilot connector proof | Opt-in external/live | `.\scripts\paid-pilot-rehearsal-check.ps1` | `KYNTIC_RUN_EXTERNAL_DOTNET_TESTS=1` unless `-SkipEnterpriseConnectorSmoke` is supplied |

## Required Environment Variables

The safe default path requires no environment variables beyond local SDK/toolchain availability. Browser proof requires `KYNTIC_RUN_BROWSER_TESTS=1`. Docker/PostgreSQL and enterprise connector proof require `KYNTIC_RUN_EXTERNAL_DOTNET_TESTS=1`. Manual hosted/API runs may use `.env.example`, but those values are not required for safe validation.

## Expected Outputs

Safe validation should finish with successful .NET restore/build, passing backend unit and SDK tests, clean frontend lint output, passing Vitest output for `apps\web`, a successful web build, and passing TypeScript SDK tests.

## Known Partial/Blocked Proofs

Playwright browser proof has been run and passes locally with `KYNTIC_RUN_BROWSER_TESTS=1` (6/6 specs across the agent playground, selector builder, and responsive layout suites; run documented in WP-008). It remains an opt-in path. Docker/PostgreSQL rehearsal is blocked without Docker and the external-test gate. Enterprise connector smoke is partial unless the enterprise repo and any approved connector fixtures/endpoints are available; routine paid-pilot checks should pass with connector smoke skipped.
