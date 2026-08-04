# WP-001 — Public boundary remediation (remove proprietary leaks)

## Metadata

- **Status:** Complete
- **Priority:** High
- **Phase:** A — Public safety and boundary
- **Depends on:** —
- **Review gate:** xhigh (public repo boundary, brand)

## Context

The audit found proprietary material in public-facing assets that directly
contradicts `AGENTS.md` ("Do not add enterprise internals, private connector
code, proprietary engine logic, LanceDB, embedded LLMs, vector pipelines, or
obfuscation logic to Scout") and `docs-site/STYLE.md` ("Do not document:
private enterprise internals, proprietary engine internals...").

The worst offenders were in the public documentation site and in public deploy
and tooling code, because they named the private engine codename and described
private engine internals. These had to be removed before anything is published
or presented as deliverable.

Confirmed locations (all fixed in this package):

- `docs-site/src/content/docs/architecture.md:30` — "managed deployment code,
  the proprietary ..." (private analysis-modules description)
- `docs-site/src/content/docs/architecture.md:105` — "category boundary:
  proprietary ..." (private analysis-modules description)
- `docs-site/src/content/docs/concepts/open-source-vs-enterprise.md:42` —
  enterprise list describing private analysis modules
- `docs-site/src/content/docs/concepts/open-source-vs-enterprise.md:70` —
  ASCII diagram naming the private engine codename
- `docs-site/src/content/docs/getting-started/what-is-scout.md:58-60` —
  "Enterprise uses the proprietary ..." and a private-runtime reference
- `docs-site/src/content/docs/apis/overview.md:17-18` — "relationship-set
  analysis belongs to the proprietary ..."
- `tools/KynticAI.Scout.MigrationTool/Program.cs:145,172` — default purpose
  string (renamed to `scout-open-core-migration-export`)
- `deploy/docker-compose.yml:6,93-95` — production profile used a
  vector-capable Postgres image

The rest of the repo (README, `docs/`, `src/`, `apps/`) already maintains the
boundary correctly; `docs/open-core-boundary.md:109-121` defines what is
explicitly private. This package brought the outliers back into compliance.

## Objective

Remove all proprietary codenames, private engine descriptions, and forbidden
vector-database references from public docs, tooling, and deployment config,
replacing them with public-safe language consistent with the open-core
boundary, without changing any public API contract.

## Do not do

- Do not invent new claims about the private tier (no new capability claims,
  no performance claims, no naming of private products beyond what already
  exists in approved public copy such as `docs/open-core-boundary.md`).
- Do not change public API shapes, SDK methods, or connector contracts.
- Do not rename package names or public brand terms.
- Do not remove functionality to satisfy this package; the open-core
  deployment must still start with plain PostgreSQL.

## Scope / files touched

- `docs-site/src/content/docs/architecture.md`
- `docs-site/src/content/docs/concepts/open-source-vs-enterprise.md`
- `docs-site/src/content/docs/getting-started/what-is-scout.md`
- `docs-site/src/content/docs/apis/overview.md`
- `tools/KynticAI.Scout.MigrationTool/Program.cs`
- `deploy/docker-compose.yml`
- `scripts/pilot-readiness.ps1` / `scripts/pilot-readiness.sh` (forbidden-code
  scan extension)
- `CHANGELOG.md` (migration purpose-string change note)

## Tasks (completed)

1. **Docs-site copy rewrite.** Rewrote the flagged passages in the four
   docs-site pages so they describe the enterprise/commercial tier without
   naming engine internals, vector-store references, private runtimes, or
   private codenames. Used the boundary vocabulary already used elsewhere in
   the repo
   ("capabilities outside the open-core deliverable", "proprietary analysis
   modules"), referencing `docs/open-core-boundary.md`. British English
   preserved; diagram box widths in the ASCII art kept consistent; docs-site
   rebuilds cleanly.

2. **MigrationTool purpose string.** Renamed the default purpose string to the
   public-safe value `scout-open-core-migration-export` (usage text and code).
   Grepped the whole repo for other references to the old string: no tests or
   scripts referenced it. Added the change note under `[Unreleased]` in
   `CHANGELOG.md` because exported artefacts now carry the new value.

3. **docker-compose profile.** Verified the open-core does not need the
   vector-capable Postgres image: vector writes are explicitly `Skipped` in
   the storage adapter (`EnterpriseExtensionDefaults.cs`), `VectorProvider`
   is `disabled` and `EnableVectorWrites` is `false` in configuration, and no
   EF migration references a vector column. Replaced the vector-capable image
   with plain `postgres:16` and removed the vector references from the header
   and service comments. No user-facing docs mentioned the vector-capable
   image, so no doc updates were needed.

4. **Harden the pilot-readiness scan.** Extended the forbidden-code scan in
   `scripts/pilot-readiness.ps1` and `scripts/pilot-readiness.sh` to also fail
   on the private codename, the vector-database image name, and the
   private-runtime/vector terms across `src apps packages docs docs-site/src
   deploy tools`. The scan now covers the public product content added by this
   package. Planning/backlog documents under `docs/work-packages/**` and the
   process example in `docs/release-and-hosting-alignment.md` were reworded
   so they no longer contain the literal banned tokens (see
   `docs/work-packages/README.md` note).

5. **Grep sweep.** Ran the repo-wide sweep; public product content (`src`,
   `apps`, `packages`, `docs-site/src`, `deploy`, `tools`, `docs`) has zero
   hits for the banned terms. The only legitimate remaining uses are the
   storage-boundary test in `tests/` that asserts vector defaults are off
   (kept, per the boundary rule) and the CI scan tokens that guard against
   regression (kept, by design).

## Acceptance criteria

- [x] `grep -ri "<private codename>"` in `docs-site/src`, `docs/`, `deploy/`,
      `tools/` returns zero hits.
- [x] No private-codename, vector-database, or private-runtime terms remain in
      any public doc, docs-site page, or deploy config.
- [x] `deploy/docker-compose.yml` uses a plain `postgres:16` image.
- [x] MigrationTool builds and exports with the new default purpose string;
      no test references the old string.
- [x] `scripts/pilot-readiness.ps1` and `.sh` include the extended forbidden
      scan; the scan passes on public product content.
- [x] `docs-site` builds cleanly.
- [x] Safe default validation passes.

## Verification (run during execution)

```powershell
# Repo-wide sweep (public product content; expect zero hits)
rg -i "<banned terms>" docs docs-site/src deploy tools src apps packages

# Docs site build
cd docs-site
npm install
npm run build

# Migration tool sanity
dotnet build .\tools\KynticAI.Scout.MigrationTool\KynticAI.Scout.MigrationTool.csproj
dotnet run --project .\tools\KynticAI.Scout.MigrationTool -- --help

# Pilot readiness gate
.\scripts\pilot-readiness.ps1

# Safe default
dotnet restore .\KynticAI.Scout.slnx
dotnet build .\KynticAI.Scout.slnx
dotnet test .\tests\KynticAI.Scout.UnitTests\KynticAI.Scout.UnitTests.csproj
dotnet test .\tests\KynticAI.Scout.Sdk.Tests\KynticAI.Scout.Sdk.Tests.csproj
```

## Notes

- This was the highest-priority package: the leaks were in public assets that
  a prospective customer, contributor, or reviewer would see first.
- The pilot-readiness gate run surfaced an unrelated blocking issue: the
  warning-as-error build flags `NU1903` for a high-severity advisory on the
  `Microsoft.OpenApi` package. That is tracked separately and must be fixed
  before the gate can pass in full (see `docs/work-packages/README.md` follow-
  ups).
