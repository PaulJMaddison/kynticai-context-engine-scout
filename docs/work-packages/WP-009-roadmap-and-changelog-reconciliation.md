# WP-009 — Roadmap and changelog reconciliation

## Metadata

- **Status:** Complete
- **Priority:** Medium
- **Phase:** D — Documentation completeness
- **Depends on:** WP-007 (so the post-CI state can be reflected truthfully)
- **Review gate:** standard

## Completion notes

- `docs/roadmap.md` refreshed: "Shipped" table lists the delivered open-core
  capabilities with doc pointers and the release that introduced each (items
  merged after the last tagged release are honestly marked "main after
  v2.8.0"); "Directional priorities" section frames future work as
  directional; the near-disclosure about private commercial repositories is
  replaced with "Capabilities beyond the open-core deliverable remain outside
  this repository"; the Scout Cloud section now matches the README
  (optional, support-only today; managed control plane = next candidate step);
  a "How we track" note points at the work-package backlog.
- `docs/releases/CHANGELOG.md` is now a thin index/pointer: it links the root
  `CHANGELOG.md` as the canonical changelog and lists the per-release notes
  (v1.0.0-v2.8.0) with the v0.1.0/v0.1.1/v2.1.1 gap documented.
- `README.md` changelog link now targets the canonical root `CHANGELOG.md`,
  with a separate row for the release-notes index.
- Root `CHANGELOG.md` `[Unreleased]` entries from WP-004..WP-008 left intact.

## Context

Two documentation hygiene defects from the audit:

1. **`docs/roadmap.md` is stale.** It is a directional boundary statement (65
   lines) with no milestones, dates, or version targets, and it predates most
   shipped work. It does not mention: Blueprint Import, webhook signing
   secrets, M2M identity, the Score API, Discovery MCP/agent, the n8n node,
   the docs site, or the Scout pilot setup wizard — all shipped per
   `CHANGELOG.md`. It also frames the managed control plane conditionally
   ("If a managed control-plane offering is developed later...") while
   `README.md:101,530-531` and `docs/cloud-commercial-control.md` describe
   Scout Cloud as a real optional offering. Line 48 nearly discloses private
   commercial repos ("Some of these capabilities now exist in private
   commercial repositories...").

2. **Two changelogs diverge.** `README.md:549` links the "Public Scout
   changelog" to `docs/releases/CHANGELOG.md`, which omits releases 2.4.0,
   2.4.1, 2.5.0, 2.5.1, and 2.6.0 (it jumps from 2.3.0 to 2.7.0), while the
   root `CHANGELOG.md` documents all releases v0.1.0 → v2.8.0. Two sources of
   truth with different scopes is a maintenance hazard. Additionally, the
   root `CHANGELOG.md` has an empty `[Unreleased]` section (line 7).

## Objective

Make the roadmap reflect shipped reality and future direction without
overclaiming, and make the changelog single-sourced so future releases cannot
drift again.

## Do not do

- Do not add invented milestones or dates that the team cannot commit to. The
  roadmap should mark shipped work as shipped and future work as directional.
- Do not remove the public/private boundary language — keep it, but soften
  the near-disclosure at `docs/roadmap.md:48` to the same framing used by
  `docs/commercial-readiness-summary.md` and `docs/open-core-boundary.md`.
- Do not rewrite release-note content; only reconcile the index/changelog.
- Do not claim Scout Cloud is GA; describe it consistently with the existing
  "optional, support-only" framing in the README.

## Scope / files touched

- `docs/roadmap.md`
- `docs/releases/CHANGELOG.md`
- `CHANGELOG.md`
- `README.md:549` (link target)
- Optionally `docs/cloud-commercial-control.md` if the roadmap change
  surfaces a wording mismatch (verify first)

## Tasks

1. **Refresh `docs/roadmap.md`.**
   - Add a "Shipped" section listing the features now delivered in the open
     core (semantic engine, facts/snapshots, GraphQL + REST, SQLite +
     PostgreSQL, connector plugin model + catalogue, Blueprint Import,
     webhook signing secrets, M2M identity/API clients, Score API, Discovery
     MCP/agent, n8n node, docs site, pilot setup wizard) with pointers to the
     relevant docs and the release that introduced each.
   - Keep a "Directional priorities" section for the future open-core work
     (semantic model strengthening, selector provenance/confidence/freshness,
     DX, SDK usability, extension contracts, docs/tests).
   - Keep the public/private boundary and the future-managed-control-plane
     section, but align the Cloud framing with `README.md` (optional,
     support-only today; roadmap says "next candidate step" rather than "if
     developed later").
   - Soften line 48: reword the near-disclosure to the approved public
     phrasing used elsewhere (e.g. "capabilities beyond the open-core
     deliverable remain outside this repository").
   - Add a short "How we track" note pointing at this work-package backlog
     (`docs/work-packages/README.md`) so the roadmap and the backlog stay in
     sync.

2. **Reconcile the changelogs.**
   - Recommended: make `docs/releases/CHANGELOG.md` a thin pointer/index that
     links the root `CHANGELOG.md` and `docs/releases/vX.Y.Z.md` notes, or
     backfill the missing 2.4.0–2.6.0 entries so both files are complete. Do
     not leave two divergent full changelogs.
   - Add the missing releases (2.4.0, 2.4.1, 2.5.0, 2.5.1, 2.6.0) from the
     root `CHANGELOG.md` into `docs/releases/CHANGELOG.md` if the pointer
     approach is rejected.
   - Update `README.md:549` to the canonical changelog target.

3. **Add an `[Unreleased]` entry for the current work.** After WP-001..WP-008
   land, add the corresponding `[Unreleased]` entries to the root
   `CHANGELOG.md` (per release-process conventions in
   `docs/releases/release-process.md`). This package can add the section
   shape; individual WP PRs should carry their own entries.

## Acceptance criteria

- [ ] `docs/roadmap.md` lists all shipped features with doc/release pointers
      and marks them shipped; future items are directional.
- [ ] The roadmap no longer says private commercial repos exist in a way that
      reads as a disclosure; Cloud framing matches the README.
- [ ] One canonical changelog: `docs/releases/CHANGELOG.md` is either a
      pointer/index or fully reconciled with `CHANGELOG.md`.
- [ ] `README.md` links the canonical changelog.
- [ ] No factual contradictions remain between roadmap, README, and
      `cloud-commercial-control.md`.

## Verification

```powershell
# Changelog coverage check: every vX.Y.Z with a root entry appears in docs/releases/CHANGELOG.md or the index
rg -n "^## v" CHANGELOG.md
rg -n "^## v" docs/releases/CHANGELOG.md

# Spot-check shipped items appear in roadmap
rg -n "Blueprint|M2M|Score|Discovery MCP|n8n|docs site|pilot" docs/roadmap.md
```

## Notes

- Roadmap content is a public commitment surface; get owner sign-off on the
  refreshed wording before closing.
- If WP-003 changes the SDK publishing story, reflect that in the roadmap's
  "Directional" section (e.g. "publish npm/NuGet packages on approval").
