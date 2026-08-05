# WP-003 — Docs-site reconciliation and publishing alignment

## Metadata

- **Status:** Complete
- **Priority:** High
- **Phase:** A — Public safety and boundary
- **Depends on:** —
- **Review gate:** standard

## Context

The repository ships two parallel doc trees that contradict each other. The
main tree is `README.md` + `docs/**`; the second is the Astro Starlight site
in `docs-site/` (20 real pages, builds cleanly, but is not linked anywhere
from the main docs and contains internal wording and divergent instructions).

Confirmed contradictions (from audit):

1. **Install path.**
   - `README.md:89` and `docs/getting-started.md:3`: the customer evaluation
     install path "should stay Docker-first".
   - `docs-site/src/content/docs/getting-started/installation.md:25-26`:
     "Option 1 — Local Install (Recommended)", Docker demoted to Option 2.
   - `docs-site/src/content/docs/index.mdx:23-25`: "Get Scout running locally
     in minutes — no Docker, PostgreSQL, or global SDK install required."

2. **SDK package availability.**
   - `docs/getting-started.md:257-259` instructs `npm install
     @kynticai/scout-sdk` and lines 278-280 `dotnet add package
     KynticAI.Scout.Sdk` (implies registry-published packages).
   - `docs-site/src/content/docs/sdks/overview.md:42-45` and
     `sdks/typescript.md:7-8`, `sdks/dotnet.md:7-8`: "public npm and NuGet
     publishing are not configured in this docs slice. Use the local package
     and project-reference paths..."

3. **Ports.**
   - Main docs: full stack on `5198` (`README.md:73`,
     `docs/getting-started.md:72`).
   - docs-site Docker path uses `8080` (`installation.md:53,71`;
     `quickstart.md:33-36`; `apis/overview.md:40,64,88`).
   - Within docs-site itself: `apis/rest.md:104-110` and `quickstart.md:56-59`
     use `5198` for the script path, `apis/overview.md` uses `8080` for Docker,
     and `apis/graphql.md:9-12` uses both. The mapping between the two ports is
     never explained in one place.

4. **Internal wording on a public page.**
   `docs-site/src/content/docs/operations/n8n-node.md:14-16` contains an
   instruction written for internal stakeholders ("Investor/data-room wording
   should keep this status as **partial**...") plus a broken link to a
   non-existent `connector-marketplace-investor-story.md` (link fix is in
   WP-002; wording removal is here).

5. **Discoverability.** No page in the main docs links to `docs-site`.

## Objective

Make the public instruction set single and consistent: one install
recommendation, one documented SDK availability story, one port mapping, no
internal wording, and cross-links between the two doc trees.

## Do not do

- Do not change product behaviour or the actual default ports in code to fit
  the docs; instead make the docs describe the real behaviour accurately.
- Do not claim npm/NuGet packages are published if they are not.
- Do not publish or deploy the docs site in this package (no hosting/CD
  setup — that is a separate decision).

## Scope / files touched

- `docs-site/src/content/docs/getting-started/installation.md`
- `docs-site/src/content/docs/index.mdx`
- `docs-site/src/content/docs/getting-content/quickstart.md` (verify exact
  path; the file is `getting-started/quickstart.md`)
- `docs-site/src/content/docs/apis/overview.md`
- `docs-site/src/content/docs/apis/rest.md`
- `docs-site/src/content/docs/apis/graphql.md`
- `docs-site/src/content/docs/sdks/overview.md`, `sdks/typescript.md`,
  `sdks/dotnet.md`
- `docs-site/src/content/docs/operations/n8n-node.md`
- `docs/getting-started.md`
- `README.md` (add a link to the docs site; adjust SDK install claims)
- `docs/sdks.md` (if it exists) — verify before editing

## Tasks

1. **Agree and document one install story.** Recommended target state:
   - The customer evaluation path is Docker-first (matches the main docs and
     the "customer evaluation install path should stay Docker-first" rule).
   - The scripted local install (`.\scripts\setup-demo.ps1` / `sh
     ./scripts/setup-demo.sh`) is documented as the contributor/development
     path.
   - Edit docs-site `installation.md` and `index.mdx` to match: label the
     Docker option as the recommended evaluation install and the local script
     option as the developer/contributor path. If the project owner disagrees,
     the decision must be made explicitly and applied to BOTH trees — no
     divergence may remain.

2. **SDK availability story.** Decide the single public statement and apply
   it in both trees:
   - Either packages are published (then `docs-site` must drop "not
     configured" and `docs/getting-started.md` is correct), or they are not
     published yet (then `docs/getting-started.md:257-259,278-280` must show
     the local package / project-reference commands that `docs-site` uses).
   - Recommended: not yet published; update `docs/getting-started.md` to use
     the local package path (`npm install ../packages/typescript/scout-sdk`
     and a `KynticAI.Scout.Sdk` project reference), and remove the odd phrase
     "in this docs slice" from `docs-site/sdks/overview.md:42-43`.
   - Record the decision in the roadmap reconciliation (WP-009) so a future
     release that publishes packages flips both trees at once.

3. **Port mapping.** Add a short canonical note (recommended: in
   `docs-site/src/content/docs/apis/overview.md` and in
   `docs/getting-started.md`) stating: the scripted demo/backend runs on
   `http://127.0.0.1:5198`; the Docker quickstart maps the API to `8080`;
   replace `5198` with `8080` when following the Docker path. Then fix each
   docs-site page to use the correct port for the path it documents and
   reference the note rather than re-explaining.

4. **n8n-node internal wording.** Remove the "Investor/data-room wording..."
   sentence from `docs-site/src/content/docs/operations/n8n-node.md:14-16`
   and leave the page with finished public copy (the broken link is fixed in
   WP-002; coordinate if both packages touch the same lines).

5. **Cross-link the two trees.**
   - Add a "Documentation site" link from `README.md` (and
     `docs/getting-started.md` if appropriate) to `docs-site/` — pointing to
     the source or the built `docs-site/dist/index.html` with a note that it
     is built with `npm run build` in `docs-site`.
   - Add a "Repository docs" link in `docs-site` pointing back to `docs/`
     (e.g. a line on `index.mdx` or the architecture page).

6. **Rebuild and verify.** `npm install && npm run build` in `docs-site`;
   the built site must include the updated pages.

## Acceptance criteria

- [x] One install recommendation across `README.md`, `docs/getting-started.md`,
      and `docs-site` — no "Option 1 Local" vs "Docker-first" contradiction.
- [x] One SDK availability statement across both trees.
- [x] Every port reference in docs-site is correct for the path it documents,
      and the 5198↔8080 mapping is explained in exactly one canonical place.
- [x] `n8n-node.md` contains no internal/investor wording.
- [x] `README.md` links the docs site; docs-site links back to the repo docs.
- [x] `docs-site` builds cleanly.

## Verification

```powershell
cd docs-site
npm install
npm run build

# Confirm no remaining contradictions (run from repo root)
rg -n "not configured in this docs slice" docs-site
rg -n "Option 1 — Local Install" docs-site
rg -n "npm install @kynticai/scout-sdk|dotnet add package KynticAI" docs
```

## Notes

- This package changes public instructions, so apply the agreed decisions to
  both trees in the same change set — intermediate commits that fix only one
  tree are acceptable only as WIP and must not be presented as finished.
- If a decision in Task 1 or 2 needs product-owner input, make the
  recommended choice, flag it in the PR/session log, and keep it visible.
