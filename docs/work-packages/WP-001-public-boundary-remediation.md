# WP-001 — Public boundary remediation (remove proprietary leaks)

## Metadata

- **Status:** Backlog
- **Priority:** High
- **Phase:** A — Public safety and boundary
- **Depends on:** —
- **Review gate:** xhigh (public repo boundary, brand)

## Context

The audit found proprietary material in public-facing assets that directly
contradicts `AGENTS.md` ("Do not add enterprise internals, private connector
code, proprietary Fortress logic, LanceDB, embedded LLMs, vector pipelines,
or obfuscation logic to Scout") and `docs-site/STYLE.md` ("Do not document:
private enterprise internals, proprietary engine internals...").

The worst offenders are in the public documentation site and in public deploy
and tooling code, because they name the private engine codename and describe
private engine internals. These must be removed before anything is published
or presented as deliverable.

Confirmed locations:

- `docs-site/src/content/docs/architecture.md:30` — "managed deployment code,
  the proprietary Enterprise Rust engine/vector..."
- `docs-site/src/content/docs/architecture.md:105` — "category boundary:
  proprietary Enterprise Rust engine/vector DB..."
- `docs-site/src/content/docs/concepts/open-source-vs-enterprise.md:42` —
  "proprietary Enterprise Rust engine/vector DB for relationship sets..."
- `docs-site/src/content/docs/concepts/open-source-vs-enterprise.md:70` —
  ASCII diagram lists "Fortress Rust/vector engine, connectors"
- `docs-site/src/content/docs/getting-started/what-is-scout.md:58-60` —
  "Enterprise uses the proprietary Rust engine/vector..." and "KynticAI
  open-source/private LLM runtime"
- `docs-site/src/content/docs/apis/overview.md:17-18` — "relationship-set
  analysis belongs to the Enterprise Rust engine/vector"
- `tools/KynticAI.Scout.MigrationTool/Program.cs:145,172` — default purpose
  string `scout-fortress-migration-export`
- `deploy/docker-compose.yml:6,93-95` — "Production-like setup (PostgreSQL +
  pgvector)" and `image: pgvector/pgvector:pg16`

The rest of the repo (README, `docs/`, `src/`, `apps/`) already maintains the
boundary correctly; `docs/open-core-boundary.md:109-121` defines what is
explicitly private. This package brings the outliers back into compliance.

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
- Do not remove functionality to satisfy this package; if removing the
  `pgvector` profile changes Docker behaviour, the open-core deployment must
  still start with plain PostgreSQL.

## Scope / files touched

- `docs-site/src/content/docs/architecture.md`
- `docs-site/src/content/docs/concepts/open-source-vs-enterprise.md`
- `docs-site/src/content/docs/getting-started/what-is-scout.md`
- `docs-site/src/content/docs/apis/overview.md`
- `tools/KynticAI.Scout.MigrationTool/Program.cs`
- `deploy/docker-compose.yml`
- `docs-site/src/content/docs/concepts/connector-basics.md` (if it references
  the same terms; verify first)
- `scripts/pilot-readiness.ps1` / `scripts/pilot-readiness.sh` (forbidden-code
  scan extension)
- `CHANGELOG.md` (note any behavioural change, e.g. migration purpose string)

## Tasks

1. **Docs-site copy rewrite.** Rewrite the five flagged passages in the four
   docs-site pages so they describe the enterprise/commercial tier without
   naming the engine, the vector database, Fortress, or a private LLM runtime.
   Use the boundary vocabulary already used elsewhere in the repo (e.g.
   "commercial extension capabilities", "capabilities outside the open-core
   deliverable", referencing `docs/open-core-boundary.md`). Match the tone and
   British English of neighbouring pages. Do not over-specify: if the only
   honest public statement is "some capabilities are outside the open-source
   core", say exactly that and link to the boundary doc.

2. **MigrationTool purpose string.** Rename the default purpose string
   `scout-fortress-migration-export` to a public-safe value (suggested:
   `scout-open-core-migration-export`). Grep the whole repo for other
   `fortress` references and any tests asserting the old string, and update
   them. Note the change in `CHANGELOG.md` under `[Unreleased]` because
   exported artefacts will carry the new value.

3. **docker-compose pgvector.** Decide and implement the correct public
   behaviour:
   - Verify whether the open-core API actually needs the `pgvector`
     extension (search `src/` for `vector`, `pgvector`, `EnableVector`, etc.).
   - If not needed, replace `image: pgvector/pgvector:pg16` with
     `image: postgres:16`, remove the "pgvector" comments at lines 6 and 93,
     and keep the rest of the production-like profile behaviour identical.
   - If it genuinely is needed, you must first open a discussion: the
     `AGENTS.md` no-vector-pipeline rule conflicts, and a public decision is
     required before keeping it.
   - Update any docs (e.g. `docs/hosted-deployment.md`, docs-site
     `self-hosting.md`) that mention the pgvector image.

4. **Harden the pilot-readiness scan.** Extend the forbidden-code scan in
   `scripts/pilot-readiness.ps1:88-90` (and the `.sh` twin) to also fail on
   `Fortress`, `pgvector`, `Rust engine`, and `vector DB` in `docs-site/src`,
   `docs/`, `deploy/`, and `tools/`. Keep the existing patterns intact.

5. **Grep sweep.** Run a repo-wide case-insensitive grep for
   `fortress|pgvector|lance|vector pipeline|Rust engine|vector DB|private LLM`
   across public files and confirm the only remaining hits are inside private
   local folders outside the repo (`C:\Kyntic\UCL-local-aidocs\*`) or are
   legitimate uses (e.g. the `EnableVector` open-core storage boundary test in
   `tests/` which asserts vector defaults are off — verify and keep).

## Acceptance criteria

- [ ] `grep -ri "fortress"` in `docs-site/src`, `docs/`, `deploy/`, `tools/`
      returns zero hits (except none — the MigrationTool string is gone).
- [ ] No "Rust engine", "vector DB", "private LLM runtime", or "pgvector"
      text remains in any public doc, docs-site page, or deploy config.
- [ ] `deploy/docker-compose.yml` uses a plain `postgres:16` image (or a
      documented decision to keep pgvector was explicitly approved).
- [ ] MigrationTool builds, runs `--help`, and exports with the new default
      purpose string; any test referencing the old string is updated.
- [ ] `scripts/pilot-readiness.ps1` and `.sh` pass on the updated tree
      (extended forbidden scan included).
- [ ] `docs-site` builds cleanly (`npm install && npm run build` in
      `docs-site`).
- [ ] Safe default validation passes (see below).

## Verification

```powershell
# Repo-wide sweep (should only show .gitignore'd/private-local matches if any)
rg -i "fortress|pgvector|lance|rust engine|vector db|private llm" docs docs-site/src deploy tools src tests

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

- This is the highest-priority package: the leaks are in public assets that a
  prospective customer, contributor, or reviewer would see first.
- Do not close this package until the `docs-site` build succeeds; the site is
  part of the public deliverable.
