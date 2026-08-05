# WP-002 — Docs reference integrity and naming source of truth

## Metadata

- **Status:** Complete
- **Priority:** High
- **Phase:** A — Public safety and boundary
- **Depends on:** —
- **Review gate:** standard

## Context

Four public files link to `docs/source-of-truth-naming-map.md` as the
"workspace naming source of truth", but that file does not exist:

- `README.md:27`
- `docs/commercial-readiness-summary.md:3`
- `docs/product-positioning.md:3`
- `docs-site/README.md:5`

Additional reference and hygiene defects found during the audit:

- `docs-site/src/content/docs/operations/n8n-node.md:16` links to
  `docs/connector-marketplace-investor-story.md`, which does not exist.
- `docs/releases/v2.7.0.md:22,26,27` leak a machine-specific path
  (a local `dotnet.exe` path outside the repo) and contain a garbled command
  (`npm run build:web app --prefix apps/web`).
- `AGENTS.md:31,46,52` embed private local paths (a private
  `local-aidocs` folder outside this repo) in the committed public repo file.
- British English slips (US "behavior"):
  - `docs/adr/0001-graphql-semantic-scout.md:12` — "engagement and activation
    behavior"
  - `docs/connector-plugin-model.md:160` — "preview-compatible REST behavior"

The repo's own `AGENTS.md` demands British English for user-facing copy, so
the two US spellings should be corrected.

## Objective

Create the missing naming source of truth, fix every broken or machine-
specific reference in public files, and restore British English where it has
slipped. No functional or contract changes.

## Do not do

- Do not change any public API contract, package name, SDK shape, or brand
  term. The naming map documents the existing canonical names; it does not
  invent new ones.
- Do not commit any private path, machine name, or session-log location into
  public files (including this repo's `AGENTS.md`).
- Do not rename products or files; only add the missing map and correct
  references.

## Scope / files touched

- Create `docs/source-of-truth-naming-map.md`
- `README.md:27` (verify link resolves; adjust wording only if needed)
- `docs/commercial-readiness-summary.md:3`
- `docs/product-positioning.md:3`
- `docs-site/README.md:5`
- `docs-site/src/content/docs/operations/n8n-node.md:16` (broken link — see
  Tasks; the internal wording on the same page is handled by WP-003)
- `docs/releases/v2.7.0.md:22,26,27`
- `AGENTS.md:31,46,52`
- `docs/adr/0001-graphql-semantic-scout.md:12`
- `docs/connector-plugin-model.md:160`

## Tasks

1. **Create `docs/source-of-truth-naming-map.md`.** This is the canonical
   naming reference. It must cover, at minimum:
   - Product/workspace names: KynticAI (always with "AI"), KynticAI Scout,
     Universal Context Layer (UCL), and when each is the correct term.
   - Artefact naming: data plane, control plane, open core, enterprise tier,
     managed offering (Scout Cloud), Discovery MCP, Score API, connector
     catalogue, pilot wizard.
   - Naming maturity rules: what public names are approved, what private
     codenames, engine internals, and vector-pipeline terms must never appear
     in public, and the "always KynticAI, never bare Kyntic" rule for
     user-facing copy.
   - Cross-reference the other files that link to this map so the map becomes
     a genuine single source of truth.
   - Keep it public-safe and consistent with `docs/product-positioning.md`.

2. **Verify and fix the four linking files.** Once the map exists, confirm
   the relative links resolve from each file's location (`../../docs/...`
   from inside `docs/` is wrong for `docs/*.md` — fix to `source-of-truth-
   naming-map.md` relative to each file). Fix all four.

3. **Fix `n8n-node.md:16`.** Decide whether `docs/connector-marketplace-
   investor-story.md` should exist. Recommended: do not create an investor-
   story doc in the public repo; instead remove the sentence and repoint the
   page to `docs/connector-marketplace.md` (which exists). The internal
   "investor/data-room wording" instruction in the same file is handled by
   WP-003, but if you are already editing these lines, remove that wording
   too so the page is finished.

4. **Clean `docs/releases/v2.7.0.md`.** Replace the machine-specific
   `dotnet.exe` commands with the portable commands from `LOCAL_VALIDATION.md`
   (`.\scripts\...`, `dotnet test .\tests\...`), and repair the garbled
   `npm run build:web app` command to the correct invocation used by `apps\web`.

5. **Remove private paths from `AGENTS.md:31,46,52`.** Replace
   the private `local-aidocs` path references with generic wording, e.g.
   "the local session log / laptop test-command notes outside this repo". The
   public repo must not reference private machine paths.

6. **British English.** Change `behavior` to `behaviour` at
   `docs/adr/0001-graphql-semantic-scout.md:12` and
   `docs/connector-plugin-model.md:160`. Grep the whole repo (docs, docs-site,
   apps/web copy) for other `behavior`/`color`/`organize`/`authorize` US
   spellings in user-facing copy and fix any additional hits (code identifiers
   are exempt).

## Acceptance criteria

- [x] `docs/source-of-truth-naming-map.md` exists and is the referenced
      source of truth; all four linking files resolve their link.
- [x] `git grep -rn "source-of-truth-naming-map"` shows the map file exists
      and every reference resolves.
- [x] No `C:`/`D:` drive or `/home/` absolute path remains in any public
      `.md` file (check `README.md`, `docs/`, `docs-site/`, `AGENTS.md`).
- [x] `docs/releases/v2.7.0.md` commands are portable and correct.
- [x] `grep -rn "behavior"` in user-facing markdown returns zero hits
      (all `behaviour`).
- [x] `git diff --check` passes on the touched files.

## Verification

```powershell
git diff --check

# Link sweep (run from repo root; both should resolve to an existing file)
Test-Path docs/source-of-truth-naming-map.md
rg -n "source-of-truth-naming-map" README.md docs docs-site

# US English sweep in public copy
rg -ni "behavior|color(?![ ]*picker)|organiz|authoriz" docs docs-site README.md
```

## Notes

- `docs-site` build must still succeed after editing `n8n-node.md` (run
  `npm run build` in `docs-site`).
- This package is quick but high-value: broken public links are a credibility
  defect for a product that markets itself on auditable public docs.
