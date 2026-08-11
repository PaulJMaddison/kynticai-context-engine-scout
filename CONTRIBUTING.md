# Contributing to KynticAI Scout

Thanks for your interest in improving KynticAI Scout. This guide covers everything you need to get started.

## Open Core Boundary

This repository is the public open-source core of KynticAI Scout. Contributions here should strengthen the core platform, developer experience, documentation, demo flows, SDKs, and public extension points.

Please do not add paid enterprise implementation code to this repository. In particular, the public repo should not contain:

- proprietary connector implementations for commercial systems
- enterprise SSO or customer-specific identity integrations
- private cloud or on-prem deployment packs intended for paid distribution
- advanced governance, compliance, or support tooling that is meant to live in a private commercial repo
- customer-specific mappings, prompts, schemas, datasets, or secrets

It is fine for the public repo to define stable extension interfaces for future enterprise modules. It is not fine to quietly ship the paid implementations here.

See [docs/open-core-boundary.md](docs/open-core-boundary.md) and [docs/enterprise-extension-points.md](docs/enterprise-extension-points.md) for the detailed boundary.

## Getting Started

### Prerequisites

The setup scripts download repo-local runtimes automatically, so you do **not** need global installs of .NET or Node.js. You only need:

- **Git**
- **bash** (Linux/macOS) or **PowerShell** (Windows)
- **curl** (used by the runtime download scripts)

### Clone and Bootstrap

```bash
git clone https://github.com/PaulJMaddison/kynticai-context-engine-scout.git
cd scout

# Linux / macOS
sh ./scripts/setup-demo.sh
sh ./scripts/start-demo.sh

# Windows
./scripts/setup-demo.ps1
./scripts/start-demo.ps1
```

This downloads .NET 10 and Node.js into the repo, restores packages, seeds SQLite demo data, and starts the API (port 5198) and web app (port 5173).

### Verified Local URLs

| Service | URL |
|---|---|
| Web app | http://127.0.0.1:5173 |
| API base | http://127.0.0.1:5198 |
| GraphQL | http://127.0.0.1:5198/graphql |
| Health check | http://127.0.0.1:5198/health |
| Swagger (when enabled) | http://127.0.0.1:5198/swagger |

### Demo Credentials

| Tenant | Email | Password |
|---|---|---|
| `demo` | `admin@scout.local` | `DemoAdmin123!` |
| `demo` | `rep@scout.local` | `DemoSales123!` |

## Development Workflow

1. **Create a branch** from `main` with a descriptive name (e.g. `fix/selector-preview-null-check`, `docs/update-api-contract`).
2. **Make focused changes** and explain the business reason as well as the code change.
3. **Run quality checks** before pushing (see below).
4. **Open a pull request** against `main`.

### Quality Checks

If you ran `setup-demo.sh`, use `./.dotnet/dotnet` instead of a global `dotnet` command.

```bash
# Backend build and tests (use ./.dotnet/dotnet if no global .NET 10 SDK)
dotnet build KynticAI.Scout.slnx
dotnet test KynticAI.Scout.slnx

# Frontend (from apps/web)
cd apps/web
npm run lint
npm test
npm run build

# TypeScript SDK (from packages/typescript/scout-sdk)
cd packages/typescript/scout-sdk
npm test
```

If you touch setup, seed, or demo flows, verify the happy path end to end by running `setup-demo.sh` followed by `start-demo.sh`.

## Code Style

### C# / .NET

- Follow the existing conventions in the `src/` directory.
- Use `dotnet format` if available; the project uses standard .NET formatting rules.
- Prefer explicit types over `var` for public API surfaces.
- XML doc comments on public types and methods are appreciated but not required for small fixes.

### TypeScript / React

- The frontend uses React 19 with TypeScript, TanStack Router, and Tailwind CSS.
- Run `npm run lint` from `apps/web` to check ESLint rules.
- Prefer named exports over default exports.
- Use the existing TanStack Query patterns for data fetching.

### TypeScript SDK

- The SDK is in `packages/typescript/scout-sdk/`.
- All public types live in `src/types.ts`; all public API methods in `src/client.ts`.
- Add JSDoc comments to any new public API surface.
- Run `npm test` to execute vitest.

### General

- Keep changes focused. Avoid PRs that mix unrelated refactors.
- Prefer improving public extension points over adding hard-coded enterprise behaviour.
- Do not commit secrets, credentials, or customer-specific data.

## What Belongs in the Public Repo

Good contribution areas include:

- core semantic context modelling
- selector execution and provenance handling
- GraphQL, REST, SDK, and CLI developer experience
- local self-hosting and demo usability
- mock connectors, generic connector contracts, and safe default implementations
- public documentation, samples, and tests
- accessibility and internationalisation improvements

## What Should Stay Outside the Public Repo

These normally belong in a future private enterprise repo, a future managed SaaS codebase, or professional services materials:

- premium commercial connector implementations
- customer-specific deployment artefacts
- enterprise auth provider implementations
- advanced policy packs or compliance report packs
- entitlement, billing, or commercial packaging logic

## Pull Requests

- Use clear, descriptive commit messages.
- Describe the user or business scenario the change improves.
- Call out any new environment variables, scripts, or data migrations.
- Call out any new extension interfaces and explain whether they are intended for OSS use, future enterprise use, or both.
- Avoid PRs that mix boundary cleanup, product messaging changes, and unrelated functional refactors in one change set.
- Link any related issues.
- Ensure all quality checks pass before requesting review.

## Issue Templates

When opening an issue, please include:

- **Bug reports**: steps to reproduce, expected behaviour, actual behaviour, runtime mode (LocalDemo/BackendOnly/SaaS), OS, and .NET version.
- **Feature requests**: describe the use case and how it fits with the open-core boundary.
- **Documentation issues**: link to the page and describe what is incorrect or missing.

## Conduct

By participating in this project, you agree to follow the guidelines in [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).
