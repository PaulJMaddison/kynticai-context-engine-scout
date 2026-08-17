# AGENTS.md

## Project Overview

KynticAI Scout is the open-source public repo for the Universal Context Layer. It is MIT-licensed and provides the public data-plane foundation: .NET API, TypeScript SDK, .NET SDK, React admin console, connector abstractions, docs, samples, and demo tooling.

Scout is the public face of KynticAI. Keep it useful, auditable, and safe for public release. It must not contain enterprise-only implementation details or private planning material.

## Repo Topology

- `src/` - .NET API, application, domain, infrastructure, and SDK projects.
- `tests/` - .NET unit, integration, SDK, and end-to-end tests.
- `apps/web/` - Vite React admin/demo console.
- `apps/discovery-agent/` - generic local codebase-audit CLI/MCP only; not the commercial KynticAI Discovery MCP buyer workflow.
- `packages/typescript/` - public TypeScript SDK, connector tooling, metadata Discovery MCP, contract parity and n8n integrations.
- `docs/` - public documentation, API notes, diagrams, and brand assets.
- `deploy/` - Docker and deployment configuration.
- `scripts/` - local setup, demo, cloud validation, and automation scripts.
- `samples/` - public example integrations and fixtures.

## Build/Test Commands

The setup scripts install repo-local .NET and Node.js runtimes. Use `./.dotnet/dotnet` if you do not have a global .NET 10 SDK.

- Restore/build/unit .NET: `dotnet restore .\KynticAI.Scout.slnx`, `dotnet build .\KynticAI.Scout.slnx`, `dotnet test .\tests\KynticAI.Scout.UnitTests\KynticAI.Scout.UnitTests.csproj`, `dotnet test .\tests\KynticAI.Scout.Sdk.Tests\KynticAI.Scout.Sdk.Tests.csproj`.
- Web app: `cd apps\web`, then `npm install`, `npm run build`, `npm run lint`, `npm run test`.
- TypeScript packages: validate every package under `packages/typescript`; prefer `npm ci` where a lockfile exists, then run the package's build/test and pack dry-run scripts where provided.
- Generic Discovery Agent: `cd apps\discovery-agent`, then install dependencies, `npm run build`, `npm run test`, and smoke `node dist/index.js --path ../.. --tier 1`.
- Local demo (Linux/macOS): `sh ./scripts/setup-demo.sh`, then `sh ./scripts/start-demo.sh`.
- Local demo (Windows): `.\scripts\setup-demo.ps1`, then `.\scripts\start-demo.ps1`.
- Browser proof: `cd apps\web`, set `KYNTIC_RUN_BROWSER_TESTS=1`, then `npm run test:e2e`.
- Docker/PostgreSQL and enterprise connector proof paths require explicit opt-in; see `LOCAL_VALIDATION.md`.
- Disposable GCP full-repo sign-off: see `docs/testing/gcp-precloud-validation.md`; pin `SCOUT_EXPECTED_SHA` for final proof.
- Laptop local-folder rule: before running tests on this machine, check the local laptop test-command notes kept outside this repo and use the nearest safe command for the folder touched; docs-only changes can use `git diff --check`.

## Do-Not-Do List

- Do not add enterprise internals, private connector code, private-engine or private vector/embedding pipeline logic, embedded model runtime code, or obfuscation logic to Scout.
- Do not add the commercial KynticAI Discovery MCP buyer journey, Discovery Signature generation, paid discovery/readiness routing, private customer handoff bundle logic, or other Fortress discovery product logic to Scout. The generic codebase-audit Discovery Agent and the public metadata-only `@kynticai/scout-discovery-mcp` package are separate open-source components and may remain here.
- Do not add stubs, placeholder implementations, fake integrations, TODO-only paths, or demo shortcuts and present them as finished work.
- Do not leak private planning docs, customer material, credentials, tokens, service-account files, or paid-customer details.
- Do not change package names, public API contracts, or SDK shapes without compatibility notes and tests.
- Do not add user-facing copy that says plain "Kyntic" when it means the public brand.
- Do not publish releases, tags, packages, or public deployment changes without explicit approval.
- Do not introduce telemetry that sends customer data to third parties.

## Commercial Quality Bar

- Every implementation must be commercial-standard code: real behaviour, typed errors, compatibility-aware public contracts, safe defaults, and focused tests for the changed behaviour.
- As a default for AI-generated changes, add realistic edge-case, malformed-input, failure-path, retry/idempotency, restart/state, concurrency and feature-interaction tests where those risks apply. Do not add coverage-only tests with no behavioural value.
- If a live dependency, dataset, credential, or external service is unavailable, implement the public boundary cleanly, mark the task partial, document the blocker in the session log kept outside this repo, and do not hide the gap behind a stub.
- Prefer small complete public-safe slices over broad incomplete scaffolding.

## Review/Test Gates

- Use xhigh review gates for public API, SDK, connector-contract, data-model, security-sensitive, persistence/restart or concurrency-sensitive changes before marking them complete.
- When Scout work depends on private engine contracts, do not treat the integration as complete until the relevant engine change has passed the review policy in the private engine review notes kept outside this repo.
- Prefer slower, meaningful verification over quick unchecked completion. Log tests run, skipped tests, and residual risk.
- Routine .NET integration/E2E tests must stay deterministic: use EF InMemory or in-memory SQLite when no real provider behaviour is under test. Live PostgreSQL/provider proof must be explicit, isolated and logged.
- For a final release/sign-off, the disposable GCP gate must validate the exact SHA when local disk or external-provider constraints prevent a complete workstation run.

## Brand Rules

- Public brand is `KynticAI`, always with `AI`.
- Product tier name is `KynticAI Scout`.
- Use British English for user-facing copy.
- Public positioning: context infrastructure for AI-enabled products. Scout does not call an AI model.
- Keep the Aged Book/Sovereign Rust visual direction when touching public UI or docs.
- Hard logo rule: every public image, screenshot, social card, README graphic, or generated marketing asset must use the approved KynticAI logo file (`docs/images/brand/kynticai-logo-mark.png` or `docs/images/brand/kynticai-logo-lockup.png`). Do not redraw, approximate, or AI-generate the logo; overlay the approved file after generating any background imagery.

## Current Sprint Priorities

- V2-003: keep this root AGENTS.md current after meaningful sessions.
- OSS-021: keep user-facing brand text aligned to `KynticAI`.
- OSS-013: GitHub Actions CI/CD is currently disabled (`*.yml` renamed to `*.yml.disabled`) because the GitHub account is locked due to a billing issue that prevents Actions jobs from starting; re-enable and harden it when the lock is resolved.
- OSS-015 and OSS-019: public docs and connector authoring remain upcoming public-facing work.

## State/Update Expectations

- Check `git status` before editing and preserve unrelated local changes.
- Read nearby code and existing docs before changing behaviour or public wording.
- Record session outcomes in your working notes after meaningful work.
- Record commands run, verification results, and any skipped checks.
- Keep this file under 200 lines and public-safe.
