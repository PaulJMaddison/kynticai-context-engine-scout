# WP-010 — Missing user documentation

## Metadata

- **Status:** Backlog
- **Priority:** Medium
- **Phase:** D — Documentation completeness
- **Depends on:** WP-001 (MigrationTool naming change should land first so the
  docs match the code)
- **Review gate:** standard

## Context

The audit found three shipped features or tooling paths with thin or missing
user documentation:

1. **MigrationTool has no user docs.** `tools/KynticAI.Scout.MigrationTool`
   exists and is a real CLI (buildable, included in the solution), but no
   `.md` file covers it (no matches for "MigrationTool" in any doc). Its
   export contract is partially covered by `docs/evidence-pack-contract-v1.md`
   and `docs/scout-blueprint.schema.json`, but a user has no entry point that
   explains when to use it and how.

2. **Pilot lead capture is only in release notes.** `CHANGELOG.md` (2.4.1)
   mentions `VITE_PILOT_LEAD_ENDPOINT` and the `/pilot` landing page lead
   capture, but there is no how-to doc for operators who want to enable it,
   and the web app env example may not document the variable.

3. **OpenAPI export is undocumented/absent.** `docs/api/README.md:50`
   instructs exporting OpenAPI to `docs/api/openapi.json`, but that file is
   not committed and the export command lives only in
   `scripts/export-openapi.sh`. A consumer cannot find the current API schema
   in the repo.

## Objective

Provide the missing user documentation so every shipped tool and integration
path has a discoverable, accurate how-to in the public docs.

## Do not do

- Do not write docs for features that do not exist (verify each command and
  env var against the code before documenting).
- Do not commit a generated `openapi.json` that drifts from the code; either
  pin it with a CI check or document the regeneration command clearly.
- Do not add the pilot lead endpoint to CI or change its behaviour.

## Scope / files touched

- Create `docs/migration-tool.md`
- `docs/api/README.md` (OpenAPI export note)
- `docs/getting-started.md` or a new `docs/guides/` page for pilot lead
  capture (prefer a new short page if no natural home exists)
- `.env.example` / `apps/web/.env.example` (document
  `VITE_PILOT_LEAD_ENDPOINT` if not already present)
- Optionally `docs-site/src/content/docs/self-hosting.md` (add a section on
  the migration tool)
- Possibly a CI step (WP-007) to check the committed OpenAPI is current —
  coordinate with WP-007 if so

## Tasks

1. **Write `docs/migration-tool.md`.**
   - Read `tools/KynticAI.Scout.MigrationTool/Program.cs` fully first.
   - Document: what it is (open-core export tool), when to use it (moving
     context/evidence data out of a Scout deployment; export contract per
     `docs/evidence-pack-contract-v1.md`), how to build and run it (portable
     commands, `dotnet run --project tools\KynticAI.Scout.MigrationTool --
     --help`), every CLI argument (with the post-WP-001 default purpose
     string), the output format and location, and the relationship to the
     Blueprint Import feature (`/blueprints/import`). Include a small worked
     example and a note that full enterprise migration import is not part of
     the open core (reference `docs/open-core-boundary.md`).

2. **Document pilot lead capture.**
   - Read `apps/web` code for the `/pilot` page and where
     `VITE_PILOT_LEAD_ENDPOINT` is consumed; document exactly what the
     variable does, where the form posts, and what happens when it is unset.
   - Create a short how-to (recommended `docs/pilot-lead-capture.md`) covering
     setup (env example), data format expectation at the endpoint, privacy
     note (consent per `docs/legal/cookie-and-event-consent-draft.md`), and
     the safe default (off/unset in production examples).
   - Ensure `.env.example` and `apps/web/.env.example` document the variable.

3. **Make OpenAPI discoverable.**
   - Decide between: (a) commit a generated `docs/api/openapi.json` plus a
     CI freshness check (preferred — see WP-007 for the job), or (b) document
     the regeneration command (`scripts/export-openapi.sh`) prominently in
     `docs/api/README.md`.
   - Implement the choice. If (a), add the export to CI after WP-007 lands.

4. **Cross-link.** Link `docs/migration-tool.md` from `docs/getting-started.md`
   and `docs/evidence-pack-contract-v1.md`; link the pilot how-to from the
   README or getting-started; ensure docs-site self-hosting mentions the
   migration tool where relevant.

## Acceptance criteria

- [ ] `docs/migration-tool.md` exists, matches the real CLI (every argument
      verified), includes a worked example, and references the open-core
      boundary.
- [ ] Pilot lead capture is documented with env var, endpoint expectation,
      default-off behaviour, and privacy note.
- [ ] `docs/api/README.md` correctly describes how to obtain the OpenAPI
      document; the committed export (if chosen) is fresh.
- [ ] Docs are cross-linked from their natural entry points.
- [ ] `git diff --check` passes; docs-only changes need no runtime tests.

## Verification

```powershell
# Confirm the documented CLI flags match reality
dotnet run --project .\tools\KynticAI.Scout.MigrationTool -- --help

# Confirm env var references resolve
rg -n "VITE_PILOT_LEAD_ENDPOINT" apps/web .env.example

# OpenAPI export command sanity (see script before running)
& .\scripts\export-openapi.sh
```

## Notes

- Docs written here must not leak the pre-WP-001 purpose string; align with
  WP-001 before finalising the MigrationTool page.
