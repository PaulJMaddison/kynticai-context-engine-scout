# WP-007 — CI/CD reference hardening (OSS-013)

## Metadata

- **Status:** Complete — intentionally disabled by policy
- **Priority:** High
- **Phase:** C — Delivery engineering
- **Depends on:** WP-001 (CI must not accidentally re-introduce forbidden
  content paths), WP-006 (SDK tests should be green before CI gates them)
- **Review gate:** standard

## Completion notes

- `.github/workflows/ci.yml.disabled` is retained as a hardened reference with
  three independent jobs (`backend`, `frontend`, `public-safety`) on
  `ubuntu-latest`; Release build with `-warnaserror`, all four
  local/deterministic .NET test projects with TRX upload, `npm ci`/lint/test/build
  for `apps\web`, TypeScript SDK build/test, and `docs-site` build.
- `.github/workflows/release.yml.disabled` is retained as a hardened reference
  for the `v*` tag trigger with build/test + public-safety scan + GitHub
  Release; npm/NuGet publishing intentionally not configured (documented in the
  workflow).
- `scripts/public-safety-scan.ps1` / `.sh` added as the single source of
  truth for the forbidden-code scan, shared by the pilot-readiness gate and
  CI.
- `scripts/pilot-readiness.ps1` / `.sh`: the "No GitHub Actions workflows"
  step is reworked to require active workflows and assert they contain no
  secrets/private references, external-service triggers, or un-gated
  browser/container steps.
- 8 integration test classes now carry `[Trait("Category", "Integration")]`,
  making `--filter "Category!=Integration"` meaningful (verified: integration
  project reports "No test matches" while Unit 112 / Sdk 40 / EndToEnd 54
  pass).
- `LOCAL_VALIDATION.md` and `README.md` document that GitHub Actions stays
  disabled while the GitHub account lock remains unresolved; the disabled files
  remain the reference for the safe default.
- Local verification: slnx Release build `-warnaserror` 0W/0E; full
  pilot-readiness.ps1 pass; apps\web lint/test/build, TS SDK build + 17 tests,
  docs-site build all green. Live GitHub Actions remain intentionally disabled.

## Context

AGENTS.md sprint item **OSS-013** ("re-enable and harden CI/CD when that work
is picked up") is not done: both GitHub Actions workflows are disabled by file
renaming.

- `.github/workflows/ci.yml.disabled` (41 lines): `build-and-test` job on
  `ubuntu-latest`; `actions/setup-dotnet@v4` via `global.json`; `dotnet
  restore` → `dotnet build --configuration Release --no-restore -warnaserror`
  → `dotnet test KynticAI.Scout.slnx ...` with TRX logger and artifact upload.
- `.github/workflows/release.yml.disabled` (44 lines): `v*` tag trigger;
  identical build/test; creates a GitHub Release via
  `softprops/action-gh-release@v2` with `generate_release_notes: true`.

They are well-formed baselines but cover only .NET — no frontend/SDK/docs-site
jobs, no public-safety scan, and no opt-in gating. Meanwhile
`scripts/pilot-readiness.ps1:20-24` currently FAILS if any active
`.github/workflows/*.yml|*.yaml` file exists — a deliberate guard that now
conflicts with the goal of re-enabling CI.

Two secondary defects to fix while here:

- `scripts/codex-cloud-setup.sh:213` documents
  `dotnet test ... --filter "Category!=Integration"`, but no test file defines
  `[Trait]`/`Category` attributes, so the filter matches zero tests (dead
  weight).
- The frontend e2e path needs the `KYNTIC_RUN_BROWSER_TESTS=1` gate
  (`scripts/require-env.mjs`) so CI must not run Playwright by default.

## Objective

Keep a hardened CI pipeline definition available as disabled reference
workflows that cover the safe-default validation for all four workstreams
(.NET, web, TypeScript SDK, docs-site), add the public-safety scan, keep
browser/container proofs opt-in, and reconcile the pilot-readiness gate with the
intentional disabled state. Do not enable GitHub Actions until the owner
explicitly asks for it.

## Do not do

- Do not run Docker/PostgreSQL or enterprise connector smoke in CI (they need
  `KYNTIC_RUN_EXTERNAL_DOTNET_TESTS=1` and external repos; keep them opt-in
  and out of default CI).
- Do not run Playwright/browser tests in CI without the
  `KYNTIC_RUN_BROWSER_TESTS=1` opt-in (it needs browser install; keep it
  optional or in a separate workflow).
- Do not add secrets to the workflow files or commit credentials.
- Do not publish releases/tags/packages in this package (that requires
  explicit approval; `release.yml` may be re-enabled but must only fire on
  manually created `v*` tags by an authorised actor).
- Do not delete the pilot-readiness guard — change it so it checks the
  workflows for unsafe content instead of forbidding their existence.

## Scope / files touched

- `.github/workflows/ci.yml.disabled` (hardened reference workflow)
- `.github/workflows/release.yml.disabled` (hardened reference workflow)
- `scripts/pilot-readiness.ps1` and `scripts/pilot-readiness.sh` (rework the
  "No GitHub Actions workflows" step)
- `scripts/codex-cloud-setup.sh:213` (fix or remove the dead filter)
- Possibly `Directory.Build.props`/global config if a property is needed for
  CI-only behaviour

## Tasks

1. **Decide the CI scope.** Reference target: one `ci.yml.disabled` with two jobs:
   - `backend`: setup-dotnet from `global.json` → restore → build
     `--configuration Release -warnaserror` → test UnitTests + Sdk.Tests +
     IntegrationTests + EndToEndTests (all local/deterministic) → upload TRX.
   - `frontend`: setup-node (see `scripts/ensure-node.sh` for the pinned
     Node version) → `apps/web`: `npm ci`, `npm run lint`, `npm test`,
     `npm run build` → `packages/typescript/scout-sdk`: `npm ci`,
     `npm run build`, `npm test` → `docs-site`: `npm ci`, `npm run build`.
   Keep them on `ubuntu-latest` and make the jobs independent so frontend
   failures do not mask backend failures.

2. **Add the public-safety scan to CI.** Add a job (or step) that runs the
   same forbidden-code scan used by `scripts/pilot-readiness.ps1:88-90`
   (the existing private-extension, cloud-api, secret-marker, key, and
   service-account patterns) extended per WP-001 with the private-codename,
   vector-database-image, and private-runtime terms across `src apps packages
   docs docs-site deploy tools`. This prevents the WP-001 boundary regressions
   from reaching main. Note: the planning docs under `docs/work-packages/**`
   are written without the literal banned tokens so the scan stays strict.

3. **Rework the pilot-readiness gate.** Replace the "No GitHub Actions
   workflows" step (`pilot-readiness.ps1:20-24` and the `.sh` twin) with a
   check that active workflow files exist AND contain no forbidden content
   (secrets, private repo names, external-service triggers, un-gated browser/
   container steps). The gate's intent was "the public repo must not leak or
   misbehave via CI" — enforce that, not workflow absence.

4. **Fix the dead test filter.** Either add a `Category=Integration` trait to
   integration/E2E test classes (cleaner: use `[Trait("Category","Integration")]`
   on the two integration test classes) and keep the filter meaningful, or
   remove the filter line from `codex-cloud-setup.sh`. Prefer the trait
   approach so the filter becomes real.

5. **Release workflow.** Keep `release.yml.disabled` as the reference for the
   existing `v*` tag trigger: build/test first, then create the release only
   from an approved tag when the workflow is deliberately enabled later. Do NOT
   wire npm/NuGet publishing without explicit approval (that is a separate
   package/decision). Document in the workflow comments that publishing is
   intentionally not configured.

6. **Update `LOCAL_VALIDATION.md` and README badges.** Add a CI badge/status
   note and document that CI runs the safe default (browser/container/enterprise
   proofs stay opt-in and are never in default CI).

## Acceptance criteria

- [x] `.github/workflows/ci.yml.disabled` and
      `.github/workflows/release.yml.disabled` remain disabled by policy and
      contain no secrets or private references.
- [x] The disabled CI reference covers .NET build+test (warnaserror), web
      lint+test+build, SDK build+test, and docs-site build, all on the safe
      default.
- [x] The disabled CI reference includes the extended public-safety scan and
      would fail on any forbidden pattern when deliberately enabled later.
- [x] `scripts/pilot-readiness.ps1`/`.sh` tolerate the intentional disabled
      workflow state while still scanning any active workflow for unsafe
      content.
- [x] `codex-cloud-setup.sh` filter is backed by real
      `Category=Integration` traits.
- [x] Browser and container/enterprise proofs remain opt-in and are not part
      of default CI.
- [x] Local safe-default validation still passes after project-file changes.

## Verification

```powershell
# Pilot-readiness gate now tolerates active workflows
.\scripts\pilot-readiness.ps1

# Ensure the filter actually excludes something meaningful (after traits added)
dotnet test .\KynticAI.Scout.slnx --no-restore --filter "Category!=Integration"

# Full local safe default
dotnet restore .\KynticAI.Scout.slnx
dotnet build .\KynticAI.Scout.slnx
dotnet test .\tests\KynticAI.Scout.UnitTests\KynticAI.Scout.UnitTests.csproj
dotnet test .\tests\KynticAI.Scout.Sdk.Tests\KynticAI.Scout.Sdk.Tests.csproj
cd apps\web; npm run lint; npm run test; npm run build
cd ..\..\packages\typescript\scout-sdk; npm run build; npm test
cd ..\..\..\docs-site; npm install; npm run build
```

> Full CI execution cannot be verified on this machine without pushing to
> GitHub. Push the branch, watch the Actions run, and log the result in the
> session log before marking this package complete.

## Notes

- Coordinating with WP-001: WP-001 must land first or CI will be red on the
  forbidden-content scan.
- This is OSS-013; update AGENTS.md sprint priorities only after the
  pipeline is verified green once.
