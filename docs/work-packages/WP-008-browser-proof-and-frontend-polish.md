# WP-008 — Browser proof and frontend polish

## Metadata

- **Status:** Backlog
- **Priority:** Medium
- **Phase:** C — Delivery engineering
- **Depends on:** —
- **Review gate:** standard

## Context

The web console (`apps/web`) is commercial-grade: typed REST/GraphQL clients
verified against the real API, all routes present, demo fallback strictly
gated and visually flagged, build/lint/component tests green. Two items remain
from the audit:

1. **Browser proof is not complete in the repo's own records.** Playwright
   specs exist (playground, selector builder, responsive layout) and
   `test-results/.last-run.json` suggests past local runs, but the path is
   gated behind `KYNTIC_RUN_BROWSER_TESTS=1` (`scripts/require-env.mjs`) and
   was not re-run during the audit. `LOCAL_VALIDATION.md` lists it as a
   known partial/blocked proof until browser dependencies are installed.

2. **US English slip.** `apps/web/src/features/demo/demo-mode-page.tsx:339`
   uses "licensing support" where the brand rule (British English) requires
   "licence support" / "licensing support" decision — user-facing copy must be
   British English.

3. **Public-safe copy review of two recent pilot docs.** The last commit
   ("Add Scout pilot setup wizard") added `docs/paid-pilot-setup.md` and
   `docs/connector-marketplace.md`. These were written fast and have not had a
   full public-safety/consistency read.

## Objective

Complete the browser proof (documented run + any fixes), fix the frontend
copy nit, and do a public-safety review pass on the two pilot docs so the
frontend and its supporting docs are finished and consistent.

## Do not do

- Do not remove the `KYNTIC_RUN_BROWSER_TESTS=1` gate — browser tests need
  dependencies and must stay opt-in (CI must not run them by default; see
  WP-007).
- Do not commit Playwright browser binaries, screenshots, or test artifacts
  unless they are the intended proof artifacts.
- Do not change marketing claims or pricing copy without owner approval;
  this package is a copy review, not a rewrite.

## Scope / files touched

- `apps/web/src/features/demo/demo-mode-page.tsx:339` (copy nit)
- `apps/web` Playwright specs (only if a real failure is found)
- `docs/paid-pilot-setup.md`
- `docs/connector-marketplace.md`
- `docs/getting-started.md` or docs-site only if the docs review requires a
  cross-reference fix (small)

## Tasks

1. **Run the browser proof.** Install the Playwright browser deps for the
   project, set `KYNTIC_RUN_BROWSER_TESTS=1`, run `npm run test:e2e` in
   `apps/web`. Fix any real failures in the specs or the app. Record the run
   (commands, pass/fail, residual skips) in the session log and update
   `LOCAL_VALIDATION.md`'s "Known Partial/Blocked Proofs" section if the
   blocker clears. If the environment cannot install browsers (e.g. offline
   laptop), keep the proof partial and log the exact blocker — do not fake a
   pass.

2. **Fix the copy nit.** Change "licensing support" to British English
   (recommended: "licence support" — verify the noun is correct in context).
   Grep `apps/web/src` for any other US spellings in user-facing strings
   (`licensing`, `behavior`, `organize`, `authorize`, `color`) and fix them.

3. **Review `docs/paid-pilot-setup.md`.** Check for: overclaiming (SaaS
   maturity, vendor-certified connectors, live customers, production
   capability claims), leaked private/enterprise detail, US English, bare
   "Kyntic", and consistency with `docs/paid-pilot.md`,
   `docs/commercial-readiness-summary.md`, and the actual pilot wizard in
   `apps/web/src/features/pilot-setup`. Fix wording to match the commercial-
   readiness ladder ("ready for paid pilot conversations", not "delivering
   paid pilot at scale").

4. **Review `docs/connector-marketplace.md`.** Verify it matches the real
   connector catalogue behaviour: `ConnectorCatalogueSeeder` marks private
   connectors as placeholders ("Unavailable in open source; safe metadata
   only") and the web app's `connector-readiness.ts` labels rows as
   "Executable open-core" / "Mock/local proof" / "Private/customer-specific" /
   "Placeholder" / "Not vendor-certified". Ensure the doc makes the same
   distinction and does not imply vendor certification. Cross-check against
   `docs/connector-marketplace.md` ↔ `docs/connector-plugin-model.md` and the
   docs-site connector pages.

## Acceptance criteria

- [ ] Browser proof either passes with a documented run, or the blocker is
      recorded verbatim and the proof stays partial (no silent pass).
- [ ] No US English slips remain in `apps/web/src` user-facing strings.
- [ ] `docs/paid-pilot-setup.md` and `docs/connector-marketplace.md` are
      public-safe, consistent with the commercial-readiness ladder, and free
      of overclaims; any material change is flagged in the PR.
- [ ] `npm run lint`, `npm run test`, and `npm run build` pass in `apps/web`.

## Verification

```powershell
cd apps/web
npm run lint
npm run test
npm run build
$env:KYNTIC_RUN_BROWSER_TESTS=1; npm run test:e2e

# US-English sweep in user-facing strings
rg -ni "licensing|behavior|organiz|authoriz|color" apps/web/src
```

## Notes

- The docs-review tasks are lightweight reads; do not expand them into a full
  rewrite of the paid-pilot narrative.
- Coordinate with WP-003 if the connector-marketplace review surfaces a
  docs-site contradiction.
